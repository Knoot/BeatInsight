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
