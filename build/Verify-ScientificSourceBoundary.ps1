param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$surfaceRoots = @(
    'src/FluoAxis.App',
    'src/FluoAxis.Presentation',
    'src/FluoAxis.Visualization',
    'web/FluoAxis.PlotHost'
)
$excludedDirectories = @('bin', 'obj', 'node_modules', 'vendor')
$sourceExtensions = @('.cs', '.xaml', '.js', '.html')
$violations = [System.Collections.Generic.List[string]]::new()

function Test-IsExcluded([string] $surfaceRoot, [string] $fullName) {
    $relative = [IO.Path]::GetRelativePath($surfaceRoot, $fullName)
    $segments = $relative.Replace([IO.Path]::AltDirectorySeparatorChar, [IO.Path]::DirectorySeparatorChar).Split([IO.Path]::DirectorySeparatorChar)
    return @($segments | Where-Object { $excludedDirectories -contains $_ }).Count -gt 0
}

function Add-Violation([string] $relative, [string] $rule) {
    $violations.Add("${relative}: $rule")
}

$files = [System.Collections.Generic.List[IO.FileInfo]]::new()
foreach ($relativeRoot in $surfaceRoots) {
    $surfaceRoot = Join-Path $root $relativeRoot
    if (-not (Test-Path -LiteralPath $surfaceRoot -PathType Container)) {
        continue
    }

    Get-ChildItem -LiteralPath $surfaceRoot -File -Recurse | Where-Object {
        -not (Test-IsExcluded $surfaceRoot $_.FullName) -and $sourceExtensions -contains $_.Extension.ToLowerInvariant()
    } | ForEach-Object { $files.Add($_) }
}

$implementationSymbols = @(
    'AnalysisMath',
    'BrafAnalysisEngine',
    'IAnalysisEngine',
    'BrafMethodSnapshot',
    'PlateauSpec',
    'NtcSpec',
    'WtSpec',
    'ClassificationSpec',
    'CalibrationInput',
    'ControlQcInput',
    'SampleAnalysisInput'
)

foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
    $content = [IO.File]::ReadAllText($file.FullName)

    if ($content -match '(?m)^\s*using\s+FluoAxis\.(?:Analysis|Domain|Persistence)\s*;' -or
        $content -match 'FluoAxis\.(?:Analysis|Domain|Persistence)\.') {
        Add-Violation $relative 'presentation surface references a forbidden implementation namespace'
    }

    foreach ($symbol in $implementationSymbols) {
        if ($content -match ('(?<![A-Za-z0-9_])' + [Text.RegularExpressions.Regex]::Escape($symbol) + '(?![A-Za-z0-9_])')) {
            Add-Violation $relative "presentation surface contains Analysis/Method implementation symbol '$symbol'"
            break
        }
    }

    $formulaRules = @(
        @{ Name = 'normalization formula'; Pattern = '(?is)\(\s*(?:f|rfu|signal)[A-Za-z0-9_]*\s*-\s*fb[A-Za-z0-9_]*\s*\)\s*/\s*\(\s*fm[A-Za-z0-9_]*\s*-\s*fb[A-Za-z0-9_]*\s*\)' },
        @{ Name = 'plateau relative-range formula'; Pattern = '(?is)\(\s*max[A-Za-z0-9_]*\s*-\s*min[A-Za-z0-9_]*\s*\)\s*/\s*mean[A-Za-z0-9_]*' },
        @{ Name = 'dr-prime distance formula'; Pattern = '(?is)(?:drPrime|dr_prime|drprime)\s*=.{0,400}?(?:Math\.Sqrt|Math\.Pow|sqrt\s*\(|\*\*)' },
        @{ Name = 'dr-prime classification comparison'; Pattern = '(?is)(?:drPrime|dr_prime|drprime)\s*(?:>=|<=|>|<)\s*(?:0?\.4(?:0+)?|[A-Za-z0-9_.]*Threshold[A-Za-z0-9_.]*)' }
    )

    foreach ($rule in $formulaRules) {
        if ($content -match $rule.Pattern) {
            Add-Violation $relative "contains a recognized formal $($rule.Name) copy"
        }
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    throw "Scientific source-boundary verification failed with $($violations.Count) violation(s)."
}

Write-Host "Scientific source-boundary verification passed for $($files.Count) first-party presentation files."
