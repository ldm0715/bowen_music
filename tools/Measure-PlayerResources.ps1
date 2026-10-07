param(
    [Parameter(Mandatory)][int]$ProcessId,
    [string]$Scenario = 'manual',
    [ValidateRange(1, 60)][int]$Seconds = 10,
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$taskProcess = Get-Process -Id $ProcessId
$taskProcess.Refresh()
$taskCpuStarted = $taskProcess.TotalProcessorTime.TotalMilliseconds
$taskClock = [Diagnostics.Stopwatch]::StartNew()
$taskSamples = [Collections.Generic.List[object]]::new()
for ($taskSample = 0; $taskSample -lt $Seconds; $taskSample++) {
    Start-Sleep -Seconds 1
    $taskProcess.Refresh()
    if ($taskProcess.HasExited) { throw "Process $ProcessId exited during measurement." }
    $taskCounters = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -Filter "IDProcess=$ProcessId"
    if (!$taskCounters) { throw "Private working set counter unavailable for process $ProcessId." }
    $taskSamples.Add([pscustomobject]@{
        PrivateWorkingSetMiB = $taskCounters.WorkingSetPrivate / 1MB
        WorkingSetMiB = $taskProcess.WorkingSet64 / 1MB
        PrivateBytesMiB = $taskProcess.PrivateMemorySize64 / 1MB
        Threads = $taskProcess.Threads.Count
        Handles = $taskProcess.HandleCount
    })
}
$taskProcess.Refresh()
$taskElapsed = $taskClock.Elapsed.TotalSeconds
$taskReport = [pscustomobject]@{
    Scenario = $Scenario
    ProcessId = $ProcessId
    LogicalProcessors = [Environment]::ProcessorCount
    SampleCount = $taskSamples.Count
    ElapsedSeconds = [math]::Round($taskElapsed, 2)
    CpuPercent = [math]::Round(($taskProcess.TotalProcessorTime.TotalMilliseconds - $taskCpuStarted) / ($taskElapsed * 1000) / [Environment]::ProcessorCount * 100, 2)
    PrivateWorkingSetMiB = [math]::Round(($taskSamples | Measure-Object PrivateWorkingSetMiB -Average).Average, 1)
    WorkingSetMiB = [math]::Round(($taskSamples | Measure-Object WorkingSetMiB -Average).Average, 1)
    PrivateBytesMiB = [math]::Round(($taskSamples | Measure-Object PrivateBytesMiB -Average).Average, 1)
    Threads = $taskSamples[-1].Threads
    Handles = $taskSamples[-1].Handles
}
$taskJson = $taskReport | ConvertTo-Json
if ($OutputPath) { $taskJson | Set-Content -LiteralPath $OutputPath -Encoding utf8 }
$taskJson
