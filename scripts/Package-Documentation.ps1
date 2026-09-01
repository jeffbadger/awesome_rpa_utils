[CmdletBinding()]
param(
    [string]$ArchivePath = "artifacts/AwesomeRpaUtils-Documentation.zip"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$archiveFullPath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $ArchivePath))

# Returns $null for a path outside the repository root (e.g. a link like
# "../../releases" that climbs above it) rather than throwing - callers must
# treat $null as "not part of the bundle."
function Get-RepoRelativePath([string]$FullPath) {
    $full = [System.IO.Path]::GetFullPath($FullPath)
    $rootFull = [System.IO.Path]::GetFullPath($repositoryRoot)
    $rootWithSeparator = $rootFull.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $null
    }
    $rel = $full.Substring($rootWithSeparator.Length)
    return $rel.Replace('\', '/')
}

# This is the documentation release contract: only the method-reference pages
# belong in the bundle - the top-level README (which links to the tool
# documentation), tools/README.md, every component's README plus its
# Documentation/*.md worked examples, and project-docs/ (coding standards
# and Pega usability reviews). Source files, LICENSE, TESTING.md, build config,
# and the repo's own GitHub URLs are deliberately excluded, and any link to
# something outside this set gets unlinked (not left dangling) below.
$topLevelReadmes = @(
    Join-Path $repositoryRoot "README.md"
    Join-Path $repositoryRoot "tools/README.md"
) | Where-Object { Test-Path -LiteralPath $_ }
$componentReadmes = @(
    Get-ChildItem -Path (Join-Path $repositoryRoot "src") -Directory |
        ForEach-Object { Join-Path $_.FullName "README.md" } |
        Where-Object { Test-Path -LiteralPath $_ }
)
$documentationPages = @(
    Get-ChildItem -Path (Join-Path $repositoryRoot "src") -Recurse -Filter "*.md" |
        Where-Object { $_.Directory.Name -eq "Documentation" } |
        Select-Object -ExpandProperty FullName
)
$projectDocsPages = @(
    Get-ChildItem -Path (Join-Path $repositoryRoot "project-docs") -Recurse -Filter "*.md" |
        Select-Object -ExpandProperty FullName
)

$allDocFiles = $topLevelReadmes + $componentReadmes + $documentationPages + $projectDocsPages
$bundledRelativePaths = [System.Collections.Generic.HashSet[string]]::new(
    [string[]]($allDocFiles | ForEach-Object { Get-RepoRelativePath $_ }),
    [System.StringComparer]::OrdinalIgnoreCase)

Write-Host "Bundling $($allDocFiles.Count) documentation files."

function Test-LinkTargetBundled($ResolvedRelativePath) {
    if ([string]::IsNullOrEmpty($ResolvedRelativePath)) {
        # $null means the link target resolved outside the repository root
        # entirely (e.g. "../../releases") - never bundled.
        return $false
    }
    if ($bundledRelativePaths.Contains($ResolvedRelativePath)) {
        return $true
    }
    # A directory-index link (e.g. "coding-standards/") is fine as long as the
    # directory itself ends up in the bundle with files under it.
    $prefix = $ResolvedRelativePath.TrimEnd('/') + "/"
    foreach ($bundled in $bundledRelativePaths) {
        if ($bundled.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

# Matches [label](target) - not images (![...]), which this doc set doesn't use.
$linkPattern = [regex]'(?<!!)\[([^\]]*)\]\(([^)]+)\)'

function Resolve-DocLinks([string]$Content, [string]$SourceFileFullPath) {
    $sourceDir = Split-Path -Parent $SourceFileFullPath
    $matches = $linkPattern.Matches($Content)

    if ($matches.Count -eq 0) {
        return $Content
    }

    $builder = [System.Text.StringBuilder]::new()
    $cursor = 0

    foreach ($match in $matches) {
        [void]$builder.Append($Content.Substring($cursor, $match.Index - $cursor))
        $cursor = $match.Index + $match.Length

        $label = $match.Groups[1].Value
        $target = $match.Groups[2].Value
        $keepAsIs = $true

        if ($target -notmatch '^(https?://|mailto:)') {
            if ($target.StartsWith('#')) {
                # Pure same-file anchor (e.g. "#notes--caveats") - always fine.
            }
            else {
                $anchorIndex = $target.IndexOf('#')
                $pathPart = if ($anchorIndex -ge 0) { $target.Substring(0, $anchorIndex) } else { $target }
                if (-not [string]::IsNullOrEmpty($pathPart)) {
                    $resolvedFull = [System.IO.Path]::GetFullPath((Join-Path $sourceDir $pathPart))
                    $resolvedRelative = Get-RepoRelativePath $resolvedFull
                    $keepAsIs = Test-LinkTargetBundled $resolvedRelative
                }
            }
        }

        if ($keepAsIs) {
            [void]$builder.Append($match.Value)
        }
        else {
            # Target isn't part of the documentation bundle (source file,
            # LICENSE, build config, a GitHub-relative URL trick, etc.) -
            # unlink rather than ship a reference that can't resolve locally.
            [void]$builder.Append($label)
        }
    }

    [void]$builder.Append($Content.Substring($cursor))
    return $builder.ToString()
}

$stagingDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("AwesomeRpaUtils-Docs-" + [guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Path $stagingDirectory | Out-Null

    foreach ($docFile in $allDocFiles) {
        $relativePath = Get-RepoRelativePath $docFile
        $destinationPath = Join-Path $stagingDirectory $relativePath
        $destinationDirectory = Split-Path -Parent $destinationPath
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

        $content = Get-Content -LiteralPath $docFile -Raw
        $rewritten = Resolve-DocLinks -Content $content -SourceFileFullPath $docFile
        Set-Content -LiteralPath $destinationPath -Value $rewritten -NoNewline
    }

    $archiveDirectory = Split-Path -Parent $archiveFullPath
    New-Item -ItemType Directory -Path $archiveDirectory -Force | Out-Null

    if (Test-Path -LiteralPath $archiveFullPath) {
        Remove-Item -LiteralPath $archiveFullPath -Force
    }

    Compress-Archive -Path (Join-Path $stagingDirectory "*") -DestinationPath $archiveFullPath
    Write-Host "Created $archiveFullPath with $($allDocFiles.Count) documentation files."
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}
