function Get-BeatInsightDuplicateMapDeclarations([object[]]$Cases) {
    $groups=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach($case in $Cases){
        $folder=[IO.Path]::GetFullPath($case.Folder)
        $file=[IO.Path]::GetFullPath((Join-Path $folder $case.File))
        $key=ConvertTo-Json -InputObject @($folder.ToUpperInvariant(),$case.Characteristic,$case.Difficulty,$file.ToUpperInvariant()) -Compress
        if($groups.ContainsKey($key)){$groups[$key].Occurrences++}else{$groups.Add($key,[pscustomobject]@{Case=$case;Occurrences=1})}
    }
    foreach($group in $groups.Values | Where-Object Occurrences -gt 1){
        [pscustomobject][ordered]@{code='duplicate-map-declaration';folder=$group.case.Folder;characteristic=$group.case.Characteristic;
            difficulty=$group.case.Difficulty;file=$group.case.File;occurrences=$group.Occurrences}
    }
}

function Get-BeatInsightMapInfoFiles([string]$Root, [Collections.Generic.List[object]]$Skipped) {
    $pending=[Collections.Generic.Stack[string]]::new()
    $pending.Push($Root)
    while($pending.Count) {
        $folder=$pending.Pop()
        $info=Get-ChildItem -LiteralPath $folder -File -Filter 'info.dat' | Select-Object -First 1
        if($info) {
            # A level is a unit: descendants belong to that level (editor saves,
            # backups, assets), not additional installed levels.
            foreach($nested in Get-ChildItem -LiteralPath $folder -File -Filter 'info.dat' -Recurse | Where-Object FullName -ne $info.FullName) {
                $Skipped.Add(@{folder=$nested.DirectoryName;reason='nested-info-under-level-root';level=$folder})
            }
            $info
            continue
        }
        foreach($child in Get-ChildItem -LiteralPath $folder -Directory | Sort-Object Name -Descending) {
            if($child.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                $Skipped.Add(@{folder=$child.FullName;reason='directory-link-not-followed'})
            } elseif($child.Name -match '^(autosaves|backups|~.*backup.*)$') {
                $Skipped.Add(@{folder=$child.FullName;reason='editor-backup-directory'})
            } else { $pending.Push($child.FullName) }
        }
    }
}
