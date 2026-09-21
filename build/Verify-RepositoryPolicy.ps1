param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$excludedDirectories = @('.git', '.codex', '.codex_budget_work', 'bin', 'obj', 'artifacts', 'node_modules')
$textExtensions = @('.cs', '.csproj', '.css', '.html', '.js', '.json', '.jsonl', '.manifest', '.md', '.props', '.ps1', '.slnx', '.targets', '.txt', '.xaml', '.xml', '.yaml', '.yml')
$violations = [System.Collections.Generic.List[string]]::new()

function Test-IsExcluded([string] $fullName) {
    $relative = [IO.Path]::GetRelativePath($root, $fullName)
    $segments = $relative.Replace([IO.Path]::AltDirectorySeparatorChar, [IO.Path]::DirectorySeparatorChar).Split([IO.Path]::DirectorySeparatorChar)
    return @($segments | Where-Object { $excludedDirectories -contains $_ }).Count -gt 0
}

$files = Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object {
    -not (Test-IsExcluded $_.FullName) -and $textExtensions -contains $_.Extension.ToLowerInvariant()
}

foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
    $lineNumber = 0
    foreach ($line in [IO.File]::ReadLines($file.FullName)) {
        $lineNumber++
        # External HTTPS links are explicitly allowed by repository policy.
        $candidate = [Text.RegularExpressions.Regex]::Replace($line, 'https://[^\s)>`\"]+', '')
        $escapedSeparator = [Text.RegularExpressions.Regex]::Escape([string][char]92)
        # Ordinary C# and JavaScript strings encode a two-separator UNC prefix
        # with four textual backslashes. Literal-preserving source forms use two
        # and a single share separator; requiring both server and share segments
        # avoids treating regex escapes such as \s as UNC paths.
        $uncCount = if ($file.Extension -in @('.cs', '.js', '.ps1')) { 4 } else { 2 }
        $literalUncPattern = '(?<!' + $escapedSeparator + ')' + $escapedSeparator + '{2}(?!' + $escapedSeparator + ')' +
            '[A-Za-z0-9][A-Za-z0-9._-]{0,252}' + $escapedSeparator + '(?!' + $escapedSeparator + ')[A-Za-z0-9]'
        $patterns = @(
            # A bare drive root and a variable-led first segment are absolute too.
            # The lookahead still excludes regex text such as F:\/[...].
            ('(?<![A-Za-z])\b[A-Za-z]:[' + $escapedSeparator + '/](?=$|[\s\"''`]|[A-Za-z0-9._$-])'),
            ($escapedSeparator + '{' + $uncCount + '}[A-Za-z0-9]'),
            $literalUncPattern,
            ($escapedSeparator + '{2,4}\?' + $escapedSeparator + '{1,2}(?:UNC' + $escapedSeparator + '{1,2})?'),
            ('(?i)fi' + 'le:/{2,}[A-Za-z0-9.]'),
            ('[\"'']/(?:home|Users|root|tmp|var/tmp|etc|opt|srv|usr|bin|sbin|dev|proc|sys|mnt|media|run)(?:/|[\"''])')
        )

        foreach ($pattern in $patterns) {
            if ($candidate -match $pattern) {
                $violations.Add("${relative}:${lineNumber}: forbidden filesystem path form")
                break
            }
        }
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    throw "Repository path-policy verification failed with $($violations.Count) violation(s)."
}

Write-Host "Repository path-policy verification passed for $($files.Count) text files."
