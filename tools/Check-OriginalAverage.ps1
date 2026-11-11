param([string]$Assembly, [string]$CecilPath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LocalSettings.ps1')
if (!$Assembly) { $Assembly = Get-BeatInsightReferenceAssembly Analysis (Get-BeatInsightSetting 'GameRoot') }
Add-Type -Path (Get-BeatInsightSetting 'CecilPath' $CecilPath)
$module = [Mono.Cecil.ModuleDefinition]::ReadModule((Resolve-Path -LiteralPath $Assembly).Path)
try {
    $method = $module.Types.Where({ $_.Name -eq 'BeatmapAnalysis' }).Methods.Where({ $_.Name -eq 'VWETw0JdWlsOrjdtF5ntIKo' }) | Select-Object -First 1
    if (!$method) { throw 'Expected original method not found.' }
    # Re-emit the ORIGINAL scalar IL, including obfuscated control flow. No plugin initializer runs.
    # The unused instance parameter is represented by object. Reject all unsupported operand types.
    $dynamic = [System.Reflection.Emit.DynamicMethod]::new('OriginalAverage', [float], [type[]]@([object], [int[]]))
    $il = $dynamic.GetILGenerator()
    foreach ($v in $method.Body.Variables) {
        $type = switch ($v.VariableType.FullName) { 'System.Int32' { [int] } 'System.UInt32' { [uint32] } 'System.Int32[]' { [int[]] } default { throw "Unsupported local $_" } }
        $null = $il.DeclareLocal($type)
    }
    $labels = @{}
    foreach ($i in $method.Body.Instructions) { $labels[$i.Offset] = $il.DefineLabel() }
    foreach ($i in $method.Body.Instructions) {
        $il.MarkLabel($labels[$i.Offset])
        $field = [System.Reflection.Emit.OpCodes].GetField($i.OpCode.Name.Replace('.', '_'), [System.Reflection.BindingFlags]'Public,Static,IgnoreCase')
        if (!$field) { throw "Unsupported opcode $($i.OpCode)" }
        $op = $field.GetValue($null)
        switch ($i.OpCode.OperandType.ToString()) {
            'InlineNone' { $il.Emit($op) }
            'InlineI' { $il.Emit($op, [int]$i.Operand) }
            'ShortInlineI' { $il.Emit($op, [sbyte]$i.Operand) }
            'ShortInlineR' { $il.Emit($op, [float]$i.Operand) }
            'InlineBrTarget' { $il.Emit($op, [System.Reflection.Emit.Label]$labels[$i.Operand.Offset]) }
            'ShortInlineBrTarget' { $il.Emit($op, [System.Reflection.Emit.Label]$labels[$i.Operand.Offset]) }
            'InlineSwitch' { $il.Emit($op, [System.Reflection.Emit.Label[]]@($i.Operand | ForEach-Object { $labels[$_.Offset] })) }
            'ShortInlineVar' { $il.Emit($op, [byte]$i.Operand.Index) }
            default { throw "Unsupported operand $($i.OpCode.OperandType); refusing approximate execution." }
        }
    }
    $run = $dynamic.CreateDelegate([Func[object,int[],float]])
    $cases = @(
        @{ Values = [int[]]@(); Expected = 0 },
        @{ Values = [int[]]@(0,0,0); Expected = 0 },
        @{ Values = [int[]]@(0,2,0,4); Expected = 3 },
        @{ Values = [int[]]@(1,2,3); Expected = 2 },
        @{ Values = [int[]]@(0,0,20,0); Expected = 20 }
    )
    foreach ($case in $cases) {
        $actual = $run.Invoke($null, $case.Values)
        if ($actual -ne $case.Expected) { throw "Original average mismatch: $actual" }
    }
    "Passed $($cases.Count) checks against original method IL. SHA256=$((Get-FileHash -LiteralPath $Assembly).Hash)"
} finally { $module.Dispose() }
