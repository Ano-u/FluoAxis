param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $Executable
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path

function Assert-PerMonitorManifest([string] $manifestText, [string] $description) {
    $document = [xml]$manifestText
    $namespaces = [Xml.XmlNamespaceManager]::new($document.NameTable)
    $namespaces.AddNamespace('assembly', 'urn:schemas-microsoft-com:asm.v1')
    $namespaces.AddNamespace('application', 'urn:schemas-microsoft-com:asm.v3')
    $namespaces.AddNamespace('dpi', 'http://schemas.microsoft.com/SMI/2016/WindowsSettings')
    $namespaces.AddNamespace('legacy', 'http://schemas.microsoft.com/SMI/2005/WindowsSettings')
    $prefix = '/assembly:assembly/application:application/application:windowsSettings/'
    $modern = $document.SelectNodes($prefix + 'dpi:dpiAwareness', $namespaces)
    $legacy = $document.SelectNodes($prefix + 'legacy:dpiAware', $namespaces)
    if ($modern.Count -ne 1 -or $modern[0].InnerText.Trim() -cne 'PerMonitorV2' -or
        $legacy.Count -ne 1 -or $legacy[0].InnerText.Trim() -cne 'true/pm') {
        throw "$description must declare PerMonitorV2 with true/pm fallback in the correct Windows namespaces."
    }
}

$source = Join-Path $root 'src/FluoAxis.App/app.manifest'
Assert-PerMonitorManifest ([IO.File]::ReadAllText($source)) 'Source manifest'
[xml]$project = Get-Content -Raw -LiteralPath (Join-Path $root 'src/FluoAxis.App/FluoAxis.App.csproj')
if (@($project.Project.PropertyGroup.ApplicationManifest) -notcontains 'app.manifest') { throw 'The app must compile the checked DPI manifest.' }

if ($Executable) {
    if ([IO.Path]::IsPathRooted($Executable) -or $Executable.Replace('\','/').Split('/') -contains '..') {
        throw 'Executable must be repository-relative.'
    }
    $target = [IO.Path]::GetFullPath((Join-Path $root $Executable))
    if (-not $target.StartsWith($root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Executable must stay inside the repository.'
    }
    if (-not ('FluoAxis.Build.ManifestResource' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
namespace FluoAxis.Build {
    public static class ManifestResource {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern IntPtr LoadLibraryExW(string file, IntPtr reserved, uint flags);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern IntPtr FindResourceW(IntPtr module, IntPtr name, IntPtr type);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern uint SizeofResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern IntPtr LockResource(IntPtr resource);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern bool FreeLibrary(IntPtr module);
        public static string Read(string transientPath) {
            // Resource-only load: no entry point or dependency DLL is executed.
            IntPtr module = LoadLibraryExW(transientPath, IntPtr.Zero, 0x22);
            if (module == IntPtr.Zero) throw new InvalidOperationException("DPI_RESOURCE_OPEN_FAILED");
            try {
                IntPtr resource = FindResourceW(module, new IntPtr(1), new IntPtr(24));
                uint size = resource == IntPtr.Zero ? 0 : SizeofResource(module, resource);
                if (size == 0 || size > 1024 * 1024) throw new InvalidOperationException("DPI_MANIFEST_RESOURCE_INVALID");
                IntPtr pointer = LockResource(LoadResource(module, resource));
                if (pointer == IntPtr.Zero) throw new InvalidOperationException("DPI_MANIFEST_RESOURCE_UNREADABLE");
                byte[] bytes = new byte[size]; Marshal.Copy(pointer, bytes, 0, bytes.Length);
                return Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').TrimEnd('\0');
            } finally { FreeLibrary(module); }
        }
    }
}
'@
    }
    Assert-PerMonitorManifest ([FluoAxis.Build.ManifestResource]::Read($target)) 'Embedded executable manifest'
    Write-Output 'Windows DPI policy passed for source and embedded executable manifest (PerMonitorV2).'
}
else { Write-Output 'Windows DPI policy passed for source manifest (PerMonitorV2).'}
