param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $PublishDirectory,
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$normalized = $PublishDirectory.Replace('\', '/')
if ([IO.Path]::IsPathRooted($PublishDirectory) -or $normalized.Contains(':') -or
    @($normalized.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -gt 0) {
    throw 'PublishDirectory must be a repository-relative directory without traversal.'
}
$publishedRoot = [IO.Path]::GetFullPath((Join-Path $repository $normalized))
$approvedPrefix = $repository.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $publishedRoot.StartsWith($approvedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'PublishDirectory must remain inside the repository.'
}

function Assert-NoResourceRedirect([string] $path) {
    $file = [IO.FileInfo]::new($path)
    if ($file.Exists -and $file.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
        throw 'Published resource redirects are not permitted.'
    }
    # Include the workspace root without emitting machine-local paths.
    for ($directory = [IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName($path)); $null -ne $directory; $directory = $directory.Parent) {
        if ($directory.Exists -and $directory.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
            throw 'Published resource redirects are not permitted.'
        }
    }
}

$required = @(
    'App.xbf',
    'MainWindow.xbf',
    'Themes/WorkbenchTheme.xbf',
    'FluoAxis.App.pri',
    'FluoAxis.Visualization.pri',
    'PlotHost/plot-host.html',
    'PlotHost/plot-host.css',
    'PlotHost/plot-host.js',
    'PlotHost/vendor/plotly.min.js',
    'PlotHost/vendor/plotly.LICENSE.txt'
)

foreach ($entry in $required) {
    $path = Join-Path $publishedRoot $entry
    try {
        Assert-NoResourceRedirect $path
        if (-not [IO.File]::Exists($path)) { throw 'Missing resource.' }
        $stream = [IO.File]::OpenRead($path)
        try {
            $length = $stream.Length
            if ($length -eq 0) { throw 'Empty resource.' }
            # Check the generated binary envelope, not executable code or the
            # complete XAML/PRI payload. Native startup remains a separate gate.
            if ($entry.EndsWith('.xbf', [StringComparison]::OrdinalIgnoreCase)) {
                if ($length -lt 16) { throw 'Truncated XBF.' }
                $header = [byte[]]::new(16)
                $stream.ReadExactly($header, 0, $header.Length)
                if ([Convert]::ToHexString($header, 0, 4) -cne '58424600') { throw 'Invalid XBF signature.' }
                $expectedLength = [uint64][BitConverter]::ToUInt32($header, 4) + [uint64][BitConverter]::ToUInt32($header, 8) + 12
                if ($expectedLength -ne [uint64]$length) { throw 'Invalid XBF length.' }
            }
            elseif ($entry.EndsWith('.pri', [StringComparison]::OrdinalIgnoreCase)) {
                if ($length -lt 32) { throw 'Truncated PRI.' }
                $header = [byte[]]::new(32)
                $stream.ReadExactly($header, 0, $header.Length)
                if ([Text.Encoding]::ASCII.GetString($header, 0, 8) -cne 'mrm_pri2') { throw 'Invalid PRI signature.' }
                if ([uint64][BitConverter]::ToUInt32($header, 12) -ne [uint64]$length) { throw 'Invalid PRI length.' }
            }
        }
        finally { $stream.Dispose() }
    }
    catch {
        throw "Published asset is missing, unreadable, redirected, empty, or malformed: $entry"
    }
}

Write-Output "Published assets passed: $($required.Count) required XBF, PRI, and local PlotHost resources."
