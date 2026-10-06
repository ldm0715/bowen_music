#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$ProjectDirectory = (Join-Path $PSScriptRoot '..\src\Bodian.WinUI'),
    [string]$PriDumpFile
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath $ProjectDirectory).Path
$outputRoot = (Resolve-Path -LiteralPath $OutputDirectory).Path
$priPath = Join-Path $outputRoot 'Bodian.WinUI.pri'
if (-not (Test-Path -LiteralPath $priPath)) { throw "Resource index missing: $priPath" }

if (-not $PriDumpFile) {
    $assetsPath = Join-Path $projectRoot 'obj\project.assets.json'
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    $sdkLibrary = @($assets.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'Microsoft.Windows.SDK.BuildTools/*' }) | Select-Object -First 1
    if (-not $sdkLibrary) { throw 'Windows SDK BuildTools was not found in the restored project assets.' }
    $makePri = $null
    foreach ($packageFolder in $assets.packageFolders.PSObject.Properties.Name) {
        $sdkRoot = Join-Path $packageFolder $sdkLibrary.ToLowerInvariant()
        if (-not (Test-Path -LiteralPath $sdkRoot)) { continue }
        $makePri = @(rg --files $sdkRoot -g makepri.exe | Where-Object { $_ -match '[\\/]x64[\\/]' }) | Select-Object -First 1
        if ($makePri) { break }
    }
    if (-not $makePri) { throw 'Windows SDK makepri.exe was not found.' }
    $repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
    $dumpDirectory = Join-Path $repositoryRoot 'artifacts\resource-check'
    [IO.Directory]::CreateDirectory($dumpDirectory) | Out-Null
    $PriDumpFile = Join-Path $dumpDirectory ('resources-{0}.xml' -f [Guid]::NewGuid().ToString('N'))
    $dumpOutput = & $makePri dump /if $priPath /of $PriDumpFile /dt detailed
    if ($LASTEXITCODE -ne 0) { throw ('makepri dump failed: ' + ($dumpOutput -join [Environment]::NewLine)) }
}

[xml]$dump = Get-Content -LiteralPath $PriDumpFile -Raw
$resources = @{}
foreach ($resource in $dump.SelectNodes('//NamedResource')) { $resources[$resource.GetAttribute('uri')] = $resource }
$xamlFiles = @(rg --files $projectRoot -g '*.xaml' -g '!**/obj/**' -g '!**/bin/**')
if ($LASTEXITCODE -ne 0 -or $xamlFiles.Count -eq 0) { throw 'Could not enumerate the project XAML files.' }
$failures = [Collections.Generic.List[string]]::new()
foreach ($xamlFile in $xamlFiles) {
    $relative = [IO.Path]::GetRelativePath($projectRoot, $xamlFile)
    $relative = [IO.Path]::ChangeExtension($relative, '.xbf').Replace('\', '/')
    $uri = 'ms-resource://Bodian.WinUI/Files/' + $relative
    $resource = $resources[$uri]
    if ($null -eq $resource) { $failures.Add("Missing XAML index: $relative"); continue }
    $candidates = @($resource.SelectNodes('Candidate'))
    if ($candidates.Count -eq 0) { $failures.Add("No resource candidate: $relative"); continue }
    foreach ($candidate in $candidates) {
        if ($candidate.GetAttribute('type') -ne 'EmbeddedData') { continue }
        $value = $candidate.SelectSingleNode('Base64Value')
        if ($null -eq $value) { $failures.Add("Empty XBF data: $relative"); continue }
        $bytes = [Convert]::FromBase64String($value.InnerText)
        if ($bytes.Length -lt 4 -or $bytes[0] -ne 0x58 -or $bytes[1] -ne 0x42 -or $bytes[2] -ne 0x46 -or $bytes[3] -ne 0) {
            $failures.Add("Invalid XBF header: $relative")
        }
    }
}
foreach ($resource in $resources.Values) {
    if (-not $resource.GetAttribute('uri').StartsWith('ms-resource://Bodian.WinUI/Files/')) { continue }
    foreach ($candidate in $resource.SelectNodes('Candidate')) {
        if ($candidate.GetAttribute('type') -ne 'Path') { continue }
        $value = $candidate.SelectSingleNode('Value')
        if ($null -eq $value -or -not $value.InnerText) { $failures.Add('Empty file resource reference'); continue }
        $resourceFile = Join-Path $outputRoot $value.InnerText
        if (-not (Test-Path -LiteralPath $resourceFile -PathType Leaf)) { $failures.Add("Missing file resource: $($value.InnerText)") }
    }
}
if ($failures.Count -gt 0) {
    throw ("WinUI resource verification failed ($($failures.Count) errors):`n" + ($failures -join "`n"))
}
Write-Output "WinUI resources verified: $($xamlFiles.Count) XAML files, no missing or invalid entries."
Write-Output "Output: $outputRoot"
