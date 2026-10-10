param(
    [Parameter(Mandatory = $true)][string] $PublishDirectory,
    [Parameter(Mandatory = $true)][string] $OutputDirectory,
    [string] $Publisher = 'CN=FluoAxis Research',
    [string] $Version = '1.0.0.0'
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
function Resolve-Entry([string] $entry) {
    $normal = $entry.Replace('\','/')
    if ([IO.Path]::IsPathRooted($entry) -or $normal.Contains(':') -or @($normal.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count) { throw 'Use repository-relative entries without traversal.' }
    $resolved = Join-Path $repository $normal
    for ($current = [IO.DirectoryInfo]::new($resolved); $null -ne $current; $current = $current.Parent) {
        if ($current.Exists -and $current.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Redirected package directories are forbidden.' }
    }
    return $resolved
}
$source = Resolve-Entry $PublishDirectory
if (-not $OutputDirectory.Replace('\','/').StartsWith('artifacts/release/', [StringComparison]::Ordinal)) { throw 'Output must be under artifacts/release.' }
$output = Resolve-Entry $OutputDirectory
if (Test-Path -LiteralPath $output) { throw 'Use a new output directory; packages are never overwritten.' }
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$' -or @($Version.Split('.') | Where-Object { [long]$_ -gt 65535 }).Count) { throw 'Version must have four components in the MSIX range.' }
if ([string]::IsNullOrWhiteSpace($Publisher) -or $Publisher.Length -gt 256 -or $Publisher -notmatch '^CN=[^<>\r\n]+$') { throw 'Provide a bounded certificate subject beginning CN=.' }
& (Join-Path $PSScriptRoot 'Verify-PublishedAssets.ps1') -PublishDirectory $PublishDirectory
& (Join-Path $PSScriptRoot 'Verify-ReleaseManifest.ps1') -PublishDirectory $PublishDirectory

# Tool version follows the checked-in central package pin and restored SDK.
[xml]$central = Get-Content -Raw -LiteralPath (Join-Path $repository 'Directory.Packages.props')
$pin = ($central.Project.ItemGroup.PackageVersion | Where-Object Include -eq 'Microsoft.Windows.SDK.BuildTools').Version
$assets = Get-Content -Raw -LiteralPath (Join-Path $repository 'src/FluoAxis.App/obj/project.assets.json') | ConvertFrom-Json
$sdk = @($assets.packageFolders.PSObject.Properties.Name | ForEach-Object { Join-Path $_ "microsoft.windows.sdk.buildtools/$pin" } | Where-Object { Test-Path -LiteralPath $_ }) | Select-Object -First 1
if (-not $sdk) { throw 'Restore the pinned Windows SDK BuildTools before packaging.' }
$makeappx = Get-ChildItem -LiteralPath (Join-Path $sdk 'bin') -Filter makeappx.exe -Recurse | Where-Object { $_.Directory.Name -eq 'x64' } | Select-Object -First 1
if (-not $makeappx) { throw 'Pinned x64 MakeAppx is unavailable.' }
New-Item -ItemType Directory -Path $output | Out-Null
$payload = Join-Path $output 'payload'
New-Item -ItemType Directory -Path $payload | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse) {
    if ($file.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Redirected payload file.' }
    $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
    $destination = Join-Path $payload $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
}
New-Item -ItemType Directory -Path (Join-Path $payload 'Assets') -Force | Out-Null
Add-Type -AssemblyName System.Drawing
foreach ($size in @(44, 50, 150)) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#0E7078'))
    $pen = [Drawing.Pen]::new([Drawing.Color]::White, [float]($size * 0.08))
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.DrawLines($pen, [Drawing.PointF[]]@([Drawing.PointF]::new($size*.29,$size*.77),[Drawing.PointF]::new($size*.29,$size*.23),[Drawing.PointF]::new($size*.73,$size*.23)))
        $graphics.DrawLine($pen, [float]($size*.29), [float]($size*.47), [float]($size*.65), [float]($size*.47))
        $bitmap.Save((Join-Path $payload "Assets/Logo$size.png"), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}
$escapedPublisher = [Security.SecurityElement]::Escape($Publisher)
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities" IgnorableNamespaces="uap rescap">
  <Identity Name="FluoAxis.Research" Publisher="$escapedPublisher" Version="$Version" ProcessorArchitecture="x64" />
  <Properties><DisplayName>FluoAxis Research</DisplayName><PublisherDisplayName>FluoAxis Research</PublisherDisplayName><Logo>Assets/Logo50.png</Logo><Description>Research fluorescence workbench. Method draft; not for clinical use.</Description></Properties>
  <Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.22621.0" MaxVersionTested="10.0.22631.0" /></Dependencies>
  <Resources><Resource Language="zh-cn" /><Resource Language="en-us" /></Resources>
  <Applications><Application Id="App" Executable="FluoAxis.App.exe" EntryPoint="Windows.FullTrustApplication"><uap:VisualElements DisplayName="FluoAxis Research" Description="Research fluorescence workbench" Square150x150Logo="Assets/Logo150.png" Square44x44Logo="Assets/Logo44.png" BackgroundColor="transparent" /></Application></Applications>
  <Capabilities><rescap:Capability Name="runFullTrust" /></Capabilities>
</Package>
"@
[IO.File]::WriteAllText((Join-Path $payload 'AppxManifest.xml'), $manifest, [Text.UTF8Encoding]::new($false))
$package = Join-Path $output "FluoAxis.Research_${Version}_x64_unsigned.msix"
$packLog = & $makeappx.FullName pack /d $payload /p $package /o 2>&1
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx package validation failed; inspect the manifest and pinned SDK.' }
$unpacked = Join-Path $output 'unpacked'
$unpackLog = & $makeappx.FullName unpack /p $package /d $unpacked /o 2>&1
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx unpack validation failed.' }
$count = 0
foreach ($file in Get-ChildItem -LiteralPath $payload -File -Recurse) {
    $relative = [IO.Path]::GetRelativePath($payload, $file.FullName)
    $restored = Join-Path $unpacked $relative
    if (-not (Test-Path -LiteralPath $restored) -or (Get-FileHash -LiteralPath $restored).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) { throw 'MSIX payload roundtrip mismatch.' }
    $count++
}
[ordered]@{ schemaVersion=1; package=[IO.Path]::GetFileName($package); sha256=(Get-FileHash -LiteralPath $package).Hash.ToLowerInvariant(); bytes=(Get-Item -LiteralPath $package).Length; version=$Version; publisher=$Publisher; publisherStatus='provisional-until-certificate-subject-confirmed'; sdkBuildTools=$pin; payloadFiles=$count; makeAppxValidation='passed'; payloadRoundtrip='passed'; signature='absent'; trustedInstallation='not-tested'; cleanMachine='not-tested'; webView2='requires-installed-Evergreen-runtime'; scientificMethod='draft-unapproved'; formalReleaseQualified=$false } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'package-evidence.json') -Encoding utf8NoBOM
Write-Output "Unsigned MSIX built and payload verified: $OutputDirectory ($count files). Signature and installation qualification remain pending."
