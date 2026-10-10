param(
    [Parameter(Mandatory = $true)]
    [string] $PublishDirectory,
    [switch] $UseCurrentWorkspace,
    [switch] $SkipAssetGate
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$relative = $PublishDirectory.Replace('\','/')
if ([IO.Path]::IsPathRooted($PublishDirectory) -or $relative.Contains(':') -or @($relative.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count) {
    throw 'PublishDirectory must be repository-relative without traversal.'
}
$executable = [IO.Path]::GetFullPath((Join-Path $repository ($relative + '/FluoAxis.App.exe')))
if (-not $executable.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Executable must remain inside the repository.' }
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Published executable is missing.' }
if (-not $SkipAssetGate) { & (Join-Path $PSScriptRoot 'Verify-PublishedAssets.ps1') -RepositoryRoot $repository -PublishDirectory $relative }

$outputEntry = 'artifacts/startup-validation/' + [guid]::NewGuid().ToString('N')
$mode = if ($UseCurrentWorkspace) { '--verify-startup' } else { '--verify-empty-startup' }
$process = Start-Process -FilePath $executable -ArgumentList @($mode, $outputEntry) -WorkingDirectory ([IO.Path]::GetDirectoryName($repository)) -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(60000)) {
    $active = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
    if ($active -and $active.Path -eq $executable) { Stop-Process -Id $process.Id }
    throw 'Published startup verification timed out.'
}
$resultPath = Join-Path $repository ($outputEntry + '/startup.json')
if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) { throw 'Published application did not produce startup evidence.' }
$result = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
if ($process.ExitCode -ne 0 -or $result.Status -ne 'pass') {
    $failure = $result.Failure
    Write-Output "Startup rejected: $($failure.Code), stage=$($failure.Stage), hresult=$($failure.HResult), operation=$($failure.Operation). Evidence: $outputEntry/startup.json"
    throw 'Published startup verification failed.'
}
Write-Output "Published startup passed: state=$($result.State), workspace=$($result.Workspace), elapsedMs=$($result.ElapsedMilliseconds). Evidence: $outputEntry/startup.json"
