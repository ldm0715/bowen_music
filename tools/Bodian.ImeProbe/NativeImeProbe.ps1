# Run in Windows PowerShell 5.1, STA. This process does not initialize WinUI.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$nativeLogDirectory = Join-Path $env:LOCALAPPDATA 'Bodian\ime-probe'
[IO.Directory]::CreateDirectory($nativeLogDirectory) | Out-Null
$nativeLogPath = Join-Path $nativeLogDirectory ('native-{0}-{1}.log' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), $PID)
$nativeLogWriter = New-Object IO.StreamWriter($nativeLogPath, $false, (New-Object Text.UTF8Encoding($false)))
$nativeLogWriter.AutoFlush = $true
function Write-NativeProbeLog([string]$Message) {
    $nativeLogTime = Get-Date -Format 'HH:mm:ss.fff'
    $nativeLogWriter.WriteLine("$nativeLogTime $Message")
}
Write-NativeProbeLog "start native EDIT pid=$PID os=$([Environment]::OSVersion)"

$nativeForm = New-Object System.Windows.Forms.Form
$nativeForm.Text = '输入法对照测试 — 原生 EDIT（独立进程）'
$nativeForm.ClientSize = New-Object Drawing.Size(740, 400)
$nativeForm.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
$nativeForm.AutoScaleMode = [System.Windows.Forms.AutoScaleMode]::Dpi
$nativeForm.Font = New-Object Drawing.Font('Microsoft YaHei UI', 11)
$nativeInstructions = New-Object System.Windows.Forms.Label
$nativeInstructions.Text = '使用微信输入法输入 nihao，观察是否出现候选框。再切换微软拼音重复。'
$nativeInstructions.SetBounds(20, 20, 700, 55)
$nativeForm.Controls.Add($nativeInstructions)
$nativeSingle = New-Object System.Windows.Forms.TextBox
$nativeSingle.SetBounds(20, 85, 690, 35)
$nativeSingle.Add_TextChanged({ Write-NativeProbeLog "single text-length=$($nativeSingle.TextLength)" })
$nativeSingle.Add_GotFocus({ Write-NativeProbeLog 'focus=single' })
$nativeForm.Controls.Add($nativeSingle)
$nativeMulti = New-Object System.Windows.Forms.TextBox
$nativeMulti.Multiline = $true
$nativeMulti.SetBounds(20, 145, 690, 120)
$nativeMulti.Add_TextChanged({ Write-NativeProbeLog "multi text-length=$($nativeMulti.TextLength)" })
$nativeMulti.Add_GotFocus({ Write-NativeProbeLog 'focus=multi' })
$nativeForm.Controls.Add($nativeMulti)
$nativeLogLabel = New-Object System.Windows.Forms.Label
$nativeLogLabel.Text = '日志：' + $nativeLogPath
$nativeLogLabel.SetBounds(20, 285, 700, 85)
$nativeForm.Controls.Add($nativeLogLabel)
$nativeForm.Add_Shown({ $nativeSingle.Focus() | Out-Null })
try { [System.Windows.Forms.Application]::Run($nativeForm) }
finally {
    Write-NativeProbeLog 'closed'
    $nativeLogWriter.Dispose()
    $nativeForm.Dispose()
}
