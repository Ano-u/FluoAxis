param(
    [string] $OutputDirectory = 'artifacts/release/research-win-x64',
    [switch] $SkipBuild,
    [switch] $SkipNativeStartup
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
if ([IO.Path]::IsPathRooted($OutputDirectory) -or $OutputDirectory.Replace('\', '/').Split('/') -contains '..') {
    throw 'OutputDirectory must be a repository-relative artifact path.'
}
$output = [IO.Path]::GetFullPath((Join-Path $repository $OutputDirectory))
$approved = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts/release')) + [IO.Path]::DirectorySeparatorChar
if (-not $output.StartsWith($approved, [StringComparison]::OrdinalIgnoreCase)) { throw 'Release output must remain under artifacts/release.' }
if (Test-Path -LiteralPath $output) { throw 'Use a new output directory; existing research builds are never overwritten.' }

function Test-ContainsBuildMachinePath([string] $artifact) {
    # Scan bounded chunks in every artifact, including binaries, for this build
    # machine's workspace/profile identity in UTF-8 and UTF-16. No searched
    # absolute value is emitted or included in the release manifest.
    $latin = [Text.Encoding]::Latin1
    $identities = @($repository, [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile))
    $needles = @($identities | Where-Object { $_.Length -gt 3 } | ForEach-Object {
        foreach ($value in @($_, $_.Replace('\','/'))) {
            $latin.GetString([Text.Encoding]::UTF8.GetBytes($value))
            $latin.GetString([Text.Encoding]::Unicode.GetBytes($value))
        }
    })
    $overlap = ($needles | ForEach-Object Length | Measure-Object -Maximum).Maximum
    $buffer = [byte[]]::new(1024 * 1024)
    $tail = ''
    $inputStream = [IO.File]::OpenRead($artifact)
    try {
        while (($count = $inputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $text = $tail + $latin.GetString($buffer, 0, $count)
            foreach ($needle in $needles) { if ($text.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true } }
            $tail = $text.Substring([Math]::Max(0, $text.Length - $overlap))
        }
        return $false
    }
    finally { $inputStream.Dispose() }
}

Push-Location $repository
try {
    & ./build/Verify-RepositoryPolicy.ps1
    & ./build/Verify-ScientificSourceBoundary.ps1
    if (-not $SkipBuild) {
        & dotnet restore src/FluoAxis.App/FluoAxis.App.csproj --locked-mode
        if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
    }
    & ./build/Generate-DependencyEvidence.ps1
    $publishArgs = @('publish', 'src/FluoAxis.App/FluoAxis.App.csproj', '-c', 'Release', '--no-restore', '--self-contained', 'true', '-o', $OutputDirectory, '-p:DebugType=None', '-p:DebugSymbols=false')
    if ($SkipBuild) { $publishArgs += '--no-build' }
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw 'Research publish failed.' }
    & ./build/Verify-WindowsDpi.ps1 -Executable ($OutputDirectory.Replace('\','/') + '/FluoAxis.App.exe')
    Copy-Item -LiteralPath 'docs/design/Research-User-Guide.md' -Destination (Join-Path $output 'USER-GUIDE.md')
    Copy-Item -LiteralPath 'DEPENDENCIES.md' -Destination (Join-Path $output 'DEPENDENCIES.md')
    Copy-Item -LiteralPath 'artifacts/compliance' -Destination (Join-Path $output 'compliance') -Recurse
    & ./build/Verify-PublishedAssets.ps1 -PublishDirectory $OutputDirectory
    if (-not $SkipNativeStartup) {
        & ./build/Verify-PublishedStartup.ps1 -PublishDirectory $OutputDirectory
    }
    $assets = @(Get-ChildItem -LiteralPath $output -File -Recurse | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($output, $_.FullName).Replace('\', '/')
        if ($relative.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase)) { throw "Unexpected debug symbol in research package: $relative" }
        if (Test-ContainsBuildMachinePath $_.FullName) { throw "Build-machine identity found in research artifact: $relative" }
        if ($_.Extension -in @('.json','.xml','.config','.md','.txt','.html','.css','.js','.ps1')) {
            $text = [IO.File]::ReadAllText($_.FullName)
            if ($text -match '(?i)[A-Z]:[\\/](?:Users|Program Files|Windows)[\\/]' -or $text -match '(?i)file:/{3}') {
                throw "Machine-local path found in research artifact: $relative"
            }
        }
        [ordered]@{ entry = $relative; bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $manifest = [ordered]@{
        schemaVersion = 1; product = 'FluoAxis'; purpose = 'research-use-only'; qualification = 'unsigned-engineering-build';
        methodApproval = 'draft-unapproved'; platform = 'win-x64'; webView2Runtime = 'locally-installed-Evergreen-required';
        sourceCommit = ((& git rev-parse HEAD) -join '').Trim(); sourceWorkingTree = 'may-include-local-changes'; files = $assets
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'research-build.json') -Encoding utf8NoBOM
    & ./build/Verify-ReleaseManifest.ps1 -PublishDirectory $OutputDirectory
    Write-Output "Research build published: $OutputDirectory ($($assets.Count) files). No signing or scientific qualification is implied."
}
finally { Pop-Location }
