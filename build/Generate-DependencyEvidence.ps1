param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $OutputDirectory = 'artifacts/compliance',
    [string] $MetadataFile = 'build/dependency-metadata.json'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$metadataPath = if ([IO.Path]::IsPathRooted($MetadataFile)) {
    [IO.Path]::GetFullPath($MetadataFile)
} else {
    [IO.Path]::GetFullPath((Join-Path $root $MetadataFile))
}
$rootBoundary = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $metadataPath.StartsWith($rootBoundary, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Dependency metadata must stay under the repository root.'
}
$centralPath = Join-Path $root 'Directory.Packages.props'
$metadata = Get-Content -Raw -LiteralPath $metadataPath | ConvertFrom-Json -AsHashtable
[xml] $central = Get-Content -Raw -LiteralPath $centralPath

$pins = @{}
foreach ($node in $central.SelectNodes('//PackageVersion')) {
    if ($pins.ContainsKey($node.Include)) { throw "Duplicate central package pin: $($node.Include)" }
    if ([string]::IsNullOrWhiteSpace($node.Version) -or $node.Version -match '[*]' -or $node.Version -match '(?i)(preview|alpha|beta|rc)') {
        throw "Package pin is not an exact stable version: $($node.Include)"
    }
    $pins[$node.Include] = $node.Version
}

$directIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($project in Get-ChildItem -LiteralPath $root -Recurse -Filter '*.csproj' -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj|artifacts)[\\/]' }) {
    [xml] $projectXml = Get-Content -Raw -LiteralPath $project.FullName
    foreach ($reference in $projectXml.SelectNodes('//PackageReference')) {
        if ($reference.Version) { throw "PackageReference must use central version management: $($project.Name) -> $($reference.Include)" }
        if (-not $pins.ContainsKey($reference.Include)) { throw "Direct package has no central pin: $($reference.Include)" }
        [void] $directIds.Add($reference.Include)
    }
}

$resolved = @{}
$usedBy = @{}
$edges = @{}
$lockFiles = Get-ChildItem -LiteralPath $root -Recurse -Filter 'packages.lock.json' -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj|artifacts)[\\/]' }
foreach ($lockFile in $lockFiles) {
    $relativeLock = [IO.Path]::GetRelativePath($root, $lockFile.FullName).Replace('\', '/')
    $lock = Get-Content -Raw -LiteralPath $lockFile.FullName | ConvertFrom-Json -AsHashtable
    foreach ($framework in $lock.dependencies.Values) {
        foreach ($entry in $framework.GetEnumerator()) {
            $value = $entry.Value
            if ($value.type -eq 'Project') { continue }
            $key = "$($entry.Key.ToLowerInvariant())@$($value.resolved)"
            if ($resolved.ContainsKey($key) -and $resolved[$key].contentHash -ne $value.contentHash) { throw "Conflicting lock hashes for $key" }
            $resolved[$key] = @{ id = $entry.Key; version = $value.resolved; contentHash = $value.contentHash }
            if (-not $usedBy.ContainsKey($key)) { $usedBy[$key] = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal) }
            [void] $usedBy[$key].Add($relativeLock)
            if (-not $edges.ContainsKey($key)) { $edges[$key] = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase) }
            if ($value.dependencies) {
                foreach ($dependency in $value.dependencies.GetEnumerator()) {
                    [void] $edges[$key].Add($dependency.Key.ToLowerInvariant())
                }
            }
        }
    }
}

$metadataById = @{}
foreach ($item in $metadata.packages) {
    if ($metadataById.ContainsKey($item.id)) { throw "Duplicate dependency metadata: $($item.id)" }
    foreach ($field in @('license', 'source', 'owner', 'upgradePolicy', 'replacementPlan')) {
        if ([string]::IsNullOrWhiteSpace($item[$field])) { throw "Missing $field metadata for $($item.id)" }
    }
    if ($item.source -notmatch '^https://') { throw "Dependency source must be HTTPS: $($item.id)" }
    $metadataById[$item.id] = $item
}

$components = @()
foreach ($key in ($resolved.Keys | Sort-Object)) {
    $package = $resolved[$key]
    if (-not $metadataById.ContainsKey($package.id)) { throw "Resolved package has no reviewed metadata: $($package.id)" }
    $meta = $metadataById[$package.id]
    $bytes = [Convert]::FromBase64String($package.contentHash)
    $hex = [Convert]::ToHexString($bytes)
    $components += [ordered]@{
        id = $package.id
        version = $package.version
        relationship = if ($directIds.Contains($package.id)) { 'direct' } else { 'transitive' }
        license = $meta.license
        source = $meta.source
        packageHash = [ordered]@{ algorithm = 'SHA-512'; value = $hex }
        owner = $meta.owner
        securitySource = $metadata.securitySource
        upgradePolicy = $meta.upgradePolicy
        replacementPlan = $meta.replacementPlan
        usedByLockFiles = @($usedBy[$key] | Sort-Object)
    }
}
$resolvedById = @{}
foreach ($key in $resolved.Keys) {
    $id = $resolved[$key].id.ToLowerInvariant()
    if ($resolvedById.ContainsKey($id) -and $resolvedById[$id] -ne $key) {
        throw "Multiple resolved versions require framework-specific dependency handling: $id"
    }
    $resolvedById[$id] = $key
}
foreach ($key in $edges.Keys) {
    foreach ($target in $edges[$key]) {
        if (-not $resolvedById.ContainsKey($target)) { throw "Lock dependency target is unresolved: $key -> $target" }
    }
}

$resolvedIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$components | ForEach-Object { [void] $resolvedIds.Add($_.id) }
foreach ($id in $directIds) { if (-not $resolvedIds.Contains($id)) { throw "Direct package is absent from lock files: $id" } }
foreach ($id in $metadataById.Keys) { if (-not $resolvedIds.Contains($id)) { throw "Reviewed metadata is stale or unused: $id" } }

$inventory = [ordered]@{
    schemaVersion = 1
    status = 'phase-1-evidence-skeleton'
    generatedFrom = @('Directory.Packages.props', '**/packages.lock.json', 'build/dependency-metadata.json')
    componentCount = $components.Count
    components = $components
}

$bomComponents = foreach ($component in $components) {
    [ordered]@{
        type = 'library'
        'bom-ref' = "pkg:nuget/$($component.id)@$($component.version)"
        name = $component.id
        version = $component.version
        purl = "pkg:nuget/$($component.id)@$($component.version)"
        hashes = @([ordered]@{ alg = 'SHA-512'; content = $component.packageHash.value })
        licenses = @([ordered]@{ license = [ordered]@{ id = $component.license } })
        externalReferences = @([ordered]@{ type = 'vcs'; url = $component.source })
        properties = @(
            [ordered]@{ name = 'fluoaxis:relationship'; value = $component.relationship },
            [ordered]@{ name = 'fluoaxis:owner'; value = $component.owner }
        )
    }
}
$dependencies = foreach ($key in ($resolved.Keys | Sort-Object)) {
    $package = $resolved[$key]
    $refs = @($edges[$key] | Sort-Object | ForEach-Object { $target = $resolved[$resolvedById[$_]]; "pkg:nuget/$($target.id)@$($target.version)" })
    [ordered]@{ ref = "pkg:nuget/$($package.id)@$($package.version)"; dependsOn = $refs }
}
$rootDependencies = @($components | Where-Object { $_.relationship -eq 'direct' } | ForEach-Object { "pkg:nuget/$($_.id)@$($_.version)" } | Sort-Object)
$bom = [ordered]@{
    bomFormat = 'CycloneDX'
    specVersion = '1.5'
    version = 1
    metadata = [ordered]@{ component = [ordered]@{ type = 'application'; 'bom-ref' = 'pkg:generic/FluoAxis'; name = 'FluoAxis' } }
    components = @($bomComponents)
    dependencies = @([ordered]@{ ref = 'pkg:generic/FluoAxis'; dependsOn = $rootDependencies }) + @($dependencies)
}

$outputRoot = if ([IO.Path]::IsPathRooted($OutputDirectory)) { [IO.Path]::GetFullPath($OutputDirectory) } else { [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory)) }
$approvedRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts')).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not ($outputRoot + [IO.Path]::DirectorySeparatorChar).StartsWith($approvedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Dependency evidence output must stay under artifacts.' }
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$jsonOptions = @{ Depth = 20 }
[IO.File]::WriteAllText((Join-Path $outputRoot 'dependencies-and-licenses.json'), (($inventory | ConvertTo-Json @jsonOptions) + "`n"), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $outputRoot 'sbom.cyclonedx.json'), (($bom | ConvertTo-Json @jsonOptions) + "`n"), [Text.UTF8Encoding]::new($false))
Write-Host "Dependency evidence generated for $($components.Count) resolved components."
