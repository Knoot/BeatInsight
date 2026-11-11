#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$MapsRoot,
    [string]$GameRoot,
    [string]$Manifest,
    [string]$OutputDirectory,
    [ValidateRange(5,3600)][int]$TimeoutSeconds = 120
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'LocalSettings.ps1')
. (Join-Path $PSScriptRoot 'MapInventory.ps1')
$GameRoot = Get-BeatInsightSetting 'GameRoot' $GameRoot
$MapsRoot = Get-BeatInsightSetting 'MapsRoot' $MapsRoot $GameRoot
$MapsRoot = (Resolve-Path -LiteralPath $MapsRoot).Path
$GameRoot = (Resolve-Path -LiteralPath $GameRoot).Path
if (!$OutputDirectory) { $OutputDirectory = Join-Path $workspace ('.local/runs/' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$workspacePrefix = [IO.Path]::GetFullPath($workspace).TrimEnd('\') + '\'
if (!$OutputDirectory.StartsWith($workspacePrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'OutputDirectory must stay inside BeatInsight workspace.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new OutputDirectory; existing results will not be overwritten.' }
$project = Join-Path $workspace 'tools/BeatInsight.Compare/BeatInsight.Compare.csproj'
$cache = Get-BeatInsightSetting 'NuGetPackageRoot'
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$buildArtifacts=Join-Path $OutputDirectory '.build'
& dotnet restore $project --artifacts-path $buildArtifacts --source $cache -p:NuGetAudit=false "-p:GameRoot=$GameRoot"
if ($LASTEXITCODE) { throw 'Restore failed. Populate the local package cache first.' }
& dotnet build $project --artifacts-path $buildArtifacts --no-restore -c Release --verbosity quiet "-p:GameRoot=$GameRoot"
if ($LASTEXITCODE) { throw 'Comparison build failed.' }
$binary = Join-Path $buildArtifacts 'bin/BeatInsight.Compare/release/BeatInsight.Compare.dll'
& dotnet $binary $GameRoot --self-test
if ($LASTEXITCODE) { throw 'Oracle self-test failed; bulk comparison was not started.' }
$caseDirectory = Join-Path $OutputDirectory 'cases'
New-Item -ItemType Directory -Path $caseDirectory | Out-Null
function Read-Cases([string]$folder) {
    $infoFile = Get-ChildItem -LiteralPath $folder -File -Filter 'info.dat' | Select-Object -First 1
    if (!$infoFile) { throw "Missing Info.dat: $folder" }
    $info = Get-Content -LiteralPath $infoFile.FullName -Raw | ConvertFrom-Json
    if ($info._difficultyBeatmapSets) {
        foreach ($set in $info._difficultyBeatmapSets) {
            foreach ($d in $set._difficultyBeatmaps) {
                [ordered]@{Folder=$folder;Characteristic=$set._beatmapCharacteristicName;Difficulty=$d._difficulty;File=$d._beatmapFilename}
            }
        }
    } else {
        foreach ($d in $info.difficultyBeatmaps) {
            [ordered]@{Folder=$folder;Characteristic=$d.characteristic;Difficulty=$d.difficulty;File=$d.beatmapDataFilename}
        }
    }
}
$cases = [Collections.Generic.List[object]]::new()
$inventoryErrors = [Collections.Generic.List[object]]::new()
$inventorySkipped = [Collections.Generic.List[object]]::new()
if ($Manifest) {
    $selection = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
    foreach ($selected in $selection.maps) {
        $folder = Join-Path $MapsRoot $selected.Folder
        $match = @(Read-Cases $folder | Where-Object { $_.Characteristic -ceq $selected.Characteristic -and $_.Difficulty -ceq $selected.Difficulty -and (!$selected.File -or $_.File -ceq $selected.File) })
        if ($match.Count -ne 1) { throw "Manifest selection is not unique: $($selected.Folder)" }
        $cases.Add($match[0])
    }
} else {
    foreach ($info in Get-BeatInsightMapInfoFiles $MapsRoot $inventorySkipped | Sort-Object FullName) {
        try { foreach ($case in Read-Cases $info.DirectoryName) { $cases.Add($case) } }
        catch { $inventoryErrors.Add(@{folder=$info.DirectoryName;error=$_.Exception.Message}) }
    }
}
if (!$cases.Count) { throw 'No beatmaps discovered.' }
$code = @(Get-ChildItem (Join-Path $workspace 'src'),(Join-Path $workspace 'tools') -Recurse -File | Where-Object { $_.Extension -in '.cs','.csproj','.ps1' -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Sort-Object FullName | ForEach-Object { @{file=[IO.Path]::GetRelativePath($workspace,$_.FullName);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
$referenceFiles=@('Analysis','Preview')|ForEach-Object {Get-BeatInsightReferenceAssembly $_ $GameRoot}
$dependencies = @($referenceFiles)+@('BeatmapCore.dll','DataModels.dll','UnityEngine.CoreModule.dll'|ForEach-Object {Join-Path $GameRoot ('Beat Saber_Data/Managed/'+$_)}) | ForEach-Object { @{file=[IO.Path]::GetRelativePath($GameRoot,$_);sha256=(Get-FileHash -LiteralPath $_).Hash} }
$run = [ordered]@{schemaVersion=3;profileVersion=2;startedUtc=[DateTime]::UtcNow.ToString('o');gameRoot=$GameRoot;mapsRoot=$MapsRoot;selection=$Manifest;expectedCases=$cases.Count;timeoutSeconds=$TimeoutSeconds;code=$code;dependencies=$dependencies;binarySha256=(Get-FileHash -LiteralPath $binary).Hash;inventoryErrors=$inventoryErrors.ToArray();inventorySkipped=$inventorySkipped.ToArray();inventoryPolicy='stop-at-level-root';coverage='vanilla-neutral; gameplay objects only; per-hand input order; synthetic history positions and last-100 selection; event capture not compared';cases=$cases.ToArray()}
$run | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'run.json') -Encoding utf8NoBOM
$counts = [ordered]@{pass=0;mismatch=0;unsupported=0;error=0;timeout=0;crash=0}
$ledgerPath = Join-Path $OutputDirectory 'results.jsonl'
$ledger = [IO.StreamWriter]::new($ledgerPath,$false,[Text.UTF8Encoding]::new($false))
$ledger.AutoFlush = $true
try {
    for ($index=0;$index -lt $cases.Count;$index++) {
        $id = '{0:D6}' -f $index
        $requestPath = Join-Path $caseDirectory ($id + '.request.json')
        $resultPath = Join-Path $caseDirectory ($id + '.json')
        $cases[$index] | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $requestPath -Encoding utf8NoBOM
        $start = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet).Source)
        $start.UseShellExecute=$false; $start.CreateNoWindow=$true
        $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
        foreach ($argument in @($binary,$GameRoot,'--case',$requestPath,$resultPath)) { $start.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::new();$process.StartInfo=$start
        $timer = [Diagnostics.Stopwatch]::StartNew()
        try {
            $null=$process.Start()
            $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
            if (!$process.WaitForExit($TimeoutSeconds*1000)) {
                $process.Kill($true);$process.WaitForExit()
                $record=@{schemaVersion=3;profileVersion=2;status='timeout';request=$cases[$index];error="Exceeded $TimeoutSeconds seconds"}
            } elseif (Test-Path -LiteralPath $resultPath) {
                $record=Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json -AsHashtable
                $expectedExit=@{pass=0;mismatch=2;unsupported=3;error=4}[$record.status]
                if ($null -eq $expectedExit -or $expectedExit -ne $process.ExitCode) { throw 'Child exit code disagrees with result status.' }
            } else {
                $record=@{schemaVersion=3;profileVersion=2;status='crash';request=$cases[$index];exitCode=$process.ExitCode;error=$stderr.GetAwaiter().GetResult()}
            }
            $err=$stderr.GetAwaiter().GetResult()
            if ($err) { [IO.File]::WriteAllText((Join-Path $caseDirectory ($id+'.stderr.txt')),$err) }
            $null=$stdout.GetAwaiter().GetResult()
        } catch { $record=@{schemaVersion=3;profileVersion=2;status='crash';request=$cases[$index];error=$_.Exception.ToString()} }
        finally {
            try { if (!$process.HasExited) { $process.Kill($true);$process.WaitForExit() } } catch { }
            $process.Dispose()
        }
        $record['caseId']=$id;$record['elapsedSeconds']=$timer.Elapsed.TotalSeconds
        $record | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $resultPath -Encoding utf8NoBOM
        $counts[$record.status]++
        $ledger.WriteLine(($record | ConvertTo-Json -Depth 20 -Compress))
        $summary=@{schemaVersion=3;profileVersion=2;complete=$false;processed=$index+1;expected=$cases.Count;counts=$counts;inventoryErrors=$inventoryErrors.Count;inventorySkipped=$inventorySkipped.Count;allCasesCompared=($counts.unsupported -eq 0 -and $counts.error -eq 0 -and $counts.timeout -eq 0 -and $counts.crash -eq 0 -and $inventoryErrors.Count -eq 0);updatedUtc=[DateTime]::UtcNow.ToString('o')}
        $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'summary.json') -Encoding utf8NoBOM
        Write-Host ('[{0}/{1}] {2}: {3} {4}/{5}' -f ($index+1),$cases.Count,$record.status,(Split-Path $cases[$index].Folder -Leaf),$cases[$index].Characteristic,$cases[$index].Difficulty)
    }
    $summary.complete=$true
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'summary.json') -Encoding utf8NoBOM
} finally { $ledger.Dispose() }
Write-Host "Results: $OutputDirectory"
$counts | Format-Table -AutoSize
if ($counts.mismatch -or $counts.error -or $counts.timeout -or $counts.crash -or $inventoryErrors.Count) { exit 2 }
if ($counts.unsupported) { exit 3 }
