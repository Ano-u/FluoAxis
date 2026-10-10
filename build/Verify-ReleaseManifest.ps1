param(
    [Parameter(Mandatory = $true)][string] $PublishDirectory,
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath $RepositoryRoot).Path
function Assert-Relative([string] $entry) {
    if ([string]::IsNullOrWhiteSpace($entry) -or [IO.Path]::IsPathRooted($entry) -or $entry.Contains(':') -or $entry.Contains('\') -or $entry.IndexOfAny([char[]]'<>"|?*') -ge 0 -or
        @($entry.Split('/') | Where-Object { $_ -in @('', '.', '..') -or $_ -match '[. ]$|[\x00-\x1f]|^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)' }).Count) { throw 'Unsafe release entry.' }
}
Assert-Relative $PublishDirectory
$source = Join-Path $repository $PublishDirectory
for ($ancestor = [IO.DirectoryInfo]::new($source); $null -ne $ancestor; $ancestor = $ancestor.Parent) {
    if ($ancestor.Exists -and $ancestor.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Redirected release directory.' }
}
$manifestPath = Join-Path $source 'research-build.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or (Get-Item -LiteralPath $manifestPath).Length -gt 2MB) { throw 'Release manifest unavailable or oversized.' }
if ((Get-Item -LiteralPath $manifestPath).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Redirected release manifest.' }
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.product -ne 'FluoAxis' -or $manifest.purpose -ne 'research-use-only' -or @($manifest.files).Count -notin 1..2000) { throw 'Unsupported release manifest.' }
$entries = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
[void]$entries.Add('research-build.json')
foreach ($asset in $manifest.files) {
    Assert-Relative $asset.entry
    if ($asset.entry -match '(?i)(?:^|/)[^/]*\.WebView2(?:/|$)|\.pdb$' -or -not $entries.Add($asset.entry)) { throw 'Duplicate or runtime-only release entry.' }
    if ($asset.sha256 -cnotmatch '^[a-f0-9]{64}$' -or $asset.bytes -lt 0 -or $asset.bytes -gt 512MB) { throw 'Invalid release file identity.' }
    $path = Join-Path $source $asset.entry
    for ($parent = [IO.FileInfo]::new($path).Directory; $null -ne $parent -and $parent.FullName.Length -ge $source.Length; $parent = $parent.Parent) {
        if ($parent.Exists -and $parent.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Redirected release entry.' }
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Missing release file.' }
    $info = Get-Item -LiteralPath $path
    if ($info.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) -or $info.Length -ne $asset.bytes -or (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -ne $asset.sha256) { throw 'Release file identity mismatch.' }
}
$actual = @(Get-ChildItem -LiteralPath $source -File -Recurse -Force)
if ($actual.Count -ne $entries.Count) { throw 'Unlisted or missing release files.' }
foreach ($file in $actual) {
    if (-not $entries.Contains([IO.Path]::GetRelativePath($source,$file.FullName).Replace('\','/'))) { throw 'Unlisted release file.' }
}
Write-Output "Release manifest passed: $($manifest.files.Count) payload files; no unlisted runtime data."
