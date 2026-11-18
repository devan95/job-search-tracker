@echo off
rem One-click delivery runner. Every delivery ZIP carries this file at its root; extract and double-click it.
rem Drag this package folder onto the launcher to pass its path explicitly. See docs/development.md, "Applying a delivery".
rem Two modes, chosen by "mode" in delivery-manifest.json: "new" (default: new branch + commit + push + PR) and
rem "update" (an existing pushed PR branch: check it out, apply the files, commit, push; no new PR).
setlocal
set "DELIVERY_CMD=%~f0"
set "DELIVERY_PKG=%~1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$t=[IO.File]::ReadAllText($env:DELIVERY_CMD); $i=$t.IndexOf('#__PS'+'_BELOW__'); Invoke-Expression $t.Substring($i)"
set "RC=%ERRORLEVEL%"
echo.
if not "%RC%"=="0" echo run-delivery stopped with exit code %RC%. Read the messages above to see how far it got (commit, push, PR).
pause
exit /b %RC%
#__PS_BELOW__
$ErrorActionPreference = 'Stop'
$script:repo = $null
$script:branchCreated = $false   # true once the script has started changing the clone; triggers Rollback until the commit
$script:committed = $false
$script:updateMode = $false
$script:manifest = $null
$script:createdPaths = @()
$script:deletedPaths = @()       # manifest deletions; a deleted file may become a directory
$script:destinationReason = ''  # why the last Test-SafeDestinationPath call refused a path
$DefaultRepo = 'C:\Users\Devan Shah\Repo\job-search-tracker'

function Fail([string]$m) {
    Write-Host ""
    Write-Host "STOP: $m" -ForegroundColor Red
    if ($script:branchCreated -and -not $script:committed) { Rollback }
    exit 1
}

function Rollback {
    # Only reached before the commit, on a tree that was clean when the script started.
    $rollbackFailures = @()
    $rollbackComplete = $true
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        Set-Location -LiteralPath $script:repo
        & git reset --hard HEAD *> $null
        if ($LASTEXITCODE -ne 0) {
            $rollbackFailures += 'git reset --hard HEAD'
            $rollbackComplete = $false
        }
        # Remove only files created by this run. Exact-path removal handles Git-ignored files;
        # never use git clean -x because that could remove pre-existing ignored data.
        foreach ($p in $script:createdPaths) {
            $createdPath = Join-Path $script:repo ($p -replace '/', '\')
            try {
                if (-not (Test-SafeDestinationPath $p $script:deletedPaths)) {
                    throw "Destination check failed: $($script:destinationReason). Refusing to remove through it."
                }
                # After the reset, a tracked path was added by another commit and is not a file this run created.
                if (Test-ExactTrackedIndexPath $p) {
                    throw "Path is tracked after reset; it was not created by this run. Not removing."
                }
                if (Test-Path -LiteralPath $createdPath) {
                    Remove-Item -LiteralPath $createdPath -Force -ErrorAction Stop
                }
                if (Test-Path -LiteralPath $createdPath) {
                    throw "Created file remains after removal."
                }
            } catch {
                $rollbackFailures += "remove created file $p"
                $rollbackComplete = $false
                Write-Host "Could not safely remove delivery-created file: $createdPath ($($_.Exception.Message))" -ForegroundColor Yellow
            }
        }
        if ($rollbackComplete -and -not $script:updateMode) {
            & git switch main *> $null
            if ($LASTEXITCODE -ne 0) {
                $rollbackFailures += 'git switch main'
                $rollbackComplete = $false
            } else {
                & git branch -D $script:manifest.branch *> $null
                if ($LASTEXITCODE -ne 0) {
                    $rollbackFailures += "git branch -D $($script:manifest.branch)"
                    $rollbackComplete = $false
                }
            }
        }
        if ($rollbackComplete) {
            if ($script:updateMode) {
                Write-Host "Rollback completed: working tree on $($script:manifest.branch) restored to its last commit." -ForegroundColor Yellow
            } else {
                Write-Host "Rollback completed: branch $($script:manifest.branch) removed, working tree restored." -ForegroundColor Yellow
            }
        } else {
            Write-Host "Rollback incomplete (failed: $($rollbackFailures -join '; ')). Check 'git status' in $($script:repo) before retrying." -ForegroundColor Yellow
            & git status --short
        }
    } catch {
        Write-Host "Rollback incomplete: $($_.Exception.Message). Check 'git status' in $($script:repo) before retrying." -ForegroundColor Yellow
        & git status --short
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
        $script:branchCreated = $false
    }
}

# Runs git, stops the whole script on a non-zero exit code.
function Git-Do([string[]]$GitArgs) {
    & git @GitArgs
    if ($LASTEXITCODE -ne 0) { Fail "git $($GitArgs -join ' ') failed (exit $LASTEXITCODE)." }
}

# Runs git quietly for a probe; returns stdout lines and sets $script:rc. Never stops the script.
function Git-Try([string[]]$GitArgs) {
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $out = & git @GitArgs 2>$null
    $script:rc = $LASTEXITCODE
    $ErrorActionPreference = $prev
    return $out
}

# Returns true only when the exact repository path is present in the Git index.
function Test-ExactTrackedIndexPath([string]$p) {
    $entries = Git-Try @('-c', 'core.quotepath=false', '--literal-pathspecs', 'ls-files', '--error-unmatch', '--', $p)
    if ($script:rc -ne 0) { return $false }
    $normalized = @($entries | ForEach-Object { ([string]$_) -replace '\\', '/' })
    return ($normalized.Count -eq 1 -and $normalized[0] -ceq $p)
}

# Returns the SHA only when the exact remote branch ref exists. A failed query is not "branch absent".
function Get-RemoteBranchSha([string]$Branch) {
    $expectedRef = "refs/heads/$Branch"
    $rows = Git-Try @('ls-remote', '--exit-code', '--heads', 'origin', $expectedRef)
    if ($script:rc -eq 2) { return $null }
    if ($script:rc -ne 0) {
        Fail "Could not query remote ref $expectedRef (git ls-remote exit $script:rc)."
    }
    $matches = @()
    foreach ($row in $rows) {
        $parts = ([string]$row).Trim() -split '\s+'
        if ($parts.Count -eq 2 -and $parts[1] -ceq $expectedRef) {
            $matches += $parts[0]
        }
    }
    if ($matches.Count -gt 1) { Fail "Remote query returned more than one exact result for $expectedRef." }
    if ($matches.Count -eq 1) { return [string]$matches[0] }
    return $null
}

function Test-SafePath([string]$p) {
    if ([string]::IsNullOrWhiteSpace($p)) { return $false }
    if ($p -match '^[\\/]' -or $p.Contains(':')) { return $false }
    foreach ($seg in ($p -split '[\\/]')) {
        if ($seg -eq '' -or $seg -eq '.' -or $seg -eq '..') { return $false }
        # Windows drops trailing dots and spaces and accepts 8.3 short names, so compare the normalized segment.
        $norm = $seg.TrimEnd('. ').ToLowerInvariant()
        if ($norm -eq '' -or $norm -eq '.git' -or $norm -match '^git~\d+$') { return $false }
    }
    return $true
}

# Reject any existing reparse point (junction/symlink) in the repo-relative destination path, and any existing
# file where a directory is needed. $DeletedFileAncestors lists tracked files the delivery deletes first, so a
# file that is replaced by a directory is allowed. Sets $script:destinationReason when it returns $false.
function Test-SafeDestinationPath([string]$p, [string[]]$DeletedFileAncestors = @()) {
    $script:destinationReason = ''
    if (-not (Test-SafePath $p)) { $script:destinationReason = 'the path fails the safe-path rules'; return $false }
    $segments = $p -split '/'
    $current = $script:repo
    for ($i = 0; $i -lt $segments.Count; $i++) {
        $current = Join-Path $current $segments[$i]
        $relative = ($segments[0..$i] -join '/')
        try {
            $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
        } catch {
            # Only a genuinely absent path component permits stopping. Inspection errors fail closed and are reported as such.
            if ($_.CategoryInfo.Category -eq [Management.Automation.ErrorCategory]::ObjectNotFound) {
                break
            }
            $script:destinationReason = "could not inspect '$relative': $($_.Exception.Message)"
            return $false
        }
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            $script:destinationReason = "'$relative' is a junction or symbolic link"
            return $false
        }
        if ($i -lt ($segments.Count - 1) -and -not $item.PSIsContainer) {
            # Nothing can exist beneath a file, so a file that the delivery deletes may be replaced by a directory.
            if ($DeletedFileAncestors -contains $relative) { break }
            $script:destinationReason = "'$relative' is a file, not a directory"
            return $false
        }
    }
    return $true
}

function Test-Clone([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return $false }
    if (-not (Test-Path -LiteralPath (Join-Path $path '.git'))) { return $false }
    $url = Git-Try @('-C', $path, 'remote', 'get-url', 'origin')
    return ($script:rc -eq 0 -and "$url" -match '(?i)devan95/job-search-tracker')
}

Write-Host "== Delivery preflight ==" -ForegroundColor Cyan

# 1. Tools
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Fail "Git is not installed or not on PATH." }
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { Fail "GitHub CLI (gh) is not installed or not on PATH." }
$prevEap = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$null = & gh auth status 2>&1
$ghRc = $LASTEXITCODE
$ErrorActionPreference = $prevEap
if ($ghRc -ne 0) { Fail "GitHub CLI is not authenticated. Run 'gh auth login' and retry." }

# 2. Delivery folder
$pkg = $env:DELIVERY_PKG
if ([string]::IsNullOrWhiteSpace($pkg)) {
    $here = Split-Path -Parent $env:DELIVERY_CMD
    if (Test-Path -LiteralPath (Join-Path $here 'delivery-manifest.json')) { $pkg = $here }
    else { $pkg = (Read-Host "Paste the full path of the extracted delivery folder").Trim('"').Trim() }
}
if (-not (Test-Path -LiteralPath $pkg -PathType Container)) { Fail "Delivery folder not found: $pkg" }
$pkg = (Resolve-Path -LiteralPath $pkg).Path.TrimEnd('\')

# Require the runner copy shipped at the package root; do not rely on a separate repo copy.
$packagedRunner = Join-Path $pkg 'run-delivery.cmd'
if (-not (Test-Path -LiteralPath $packagedRunner -PathType Leaf)) {
    Fail "The delivery is missing root-level run-delivery.cmd. Rebuild the ZIP; no manual copy is supported."
}
$invokedRunner = (Resolve-Path -LiteralPath $env:DELIVERY_CMD).Path
$packagedRunner = (Resolve-Path -LiteralPath $packagedRunner).Path
if (-not [string]::Equals($invokedRunner, $packagedRunner, [StringComparison]::OrdinalIgnoreCase)) {
    Fail "Run the root-level run-delivery.cmd included in this extracted package."
}

# 3. Manifest and package files
$manifestPath = Join-Path $pkg 'delivery-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { Fail "delivery-manifest.json is missing in $pkg." }
$script:manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifest = $script:manifest
$mode = "$($manifest.mode)".Trim().ToLowerInvariant()
if ($mode -eq '') { $mode = 'new' }
if ($mode -ne 'new' -and $mode -ne 'update') { Fail "Manifest mode must be 'new' or 'update' (found '$mode')." }
$script:updateMode = ($mode -eq 'update')
$required = @('baseCommit', 'branch', 'commitSubject')
if (-not $script:updateMode) { $required += 'prTitle' }
foreach ($f in $required) {
    if ([string]::IsNullOrWhiteSpace($manifest.$f)) { Fail "Manifest field '$f' is missing." }
}
if ($manifest.baseCommit -notmatch '^[0-9a-f]{40}$') { Fail "Manifest baseCommit is not a full 40-character SHA." }
if ($script:updateMode) {
    # An existing PR branch keeps its own name (for example chore/<name>); it must still be a plain, safe ref name.
    if ($manifest.branch -cnotmatch '^[a-z0-9][a-z0-9._/-]*$' -or $manifest.branch -cmatch '\.\.|//|/$|\.lock$|\.$') { Fail "Branch '$($manifest.branch)' is not a safe existing PR branch name." }
} elseif ($manifest.branch -notmatch '^ac[1-6]/[a-z0-9][a-z0-9-]*$') { Fail "Branch '$($manifest.branch)' does not match ac1..ac6/<short-task-name>." }
# One path form (forward slashes) for every later comparison.
$files = @($manifest.files | ForEach-Object { "$_" -replace '\\', '/' })
$deleted = @($manifest.deleted | ForEach-Object { "$_" -replace '\\', '/' })
$script:deletedPaths = $deleted
if ($files.Count + $deleted.Count -eq 0) { Fail "Manifest lists no files." }
foreach ($p in ($files + $deleted)) {
    if (-not (Test-SafePath $p)) { Fail "Unsafe path in manifest: '$p'." }
}
$helpers = @('delivery-manifest.json', 'commit.txt', 'run-delivery.cmd')
if (-not $script:updateMode) { $helpers += @('pr-title.txt', 'pr.txt') }

# Package helpers are ZIP metadata/launcher files, never repository manifest paths.
$helperInManifest = @($files + $deleted | Where-Object {
    $candidate = ([string]$_).TrimEnd([char[]]@('.', ' '))
    ($candidate.IndexOf('/') -lt 0) -and ($helpers -contains $candidate)
})
if ($helperInManifest.Count -gt 0) {
    Fail "Package helper(s) must not appear in manifest.files or manifest.deleted: $($helperInManifest -join ', ')"
}

foreach ($h in $helpers) {
    if (-not (Test-Path -LiteralPath (Join-Path $pkg $h))) { Fail "Delivery file missing: $h" }
}
# Reject short-name aliases that resolve to a package helper as well as the long filename.
$packageHelperNames = @{}
foreach ($h in $helpers) {
    $helperItem = Get-Item -LiteralPath (Join-Path $pkg $h) -Force
    $packageHelperNames[$helperItem.Name.ToLowerInvariant()] = $true
}
foreach ($p in ($files + $deleted)) {
    $candidatePath = Join-Path $pkg ($p -replace '/', '\')
    if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
        $candidateItem = Get-Item -LiteralPath $candidatePath -Force
        $isPackageRoot = [string]::Equals($candidateItem.DirectoryName, $pkg, [StringComparison]::OrdinalIgnoreCase)
        $resolvesToHelper = $packageHelperNames.ContainsKey($candidateItem.Name.ToLowerInvariant())
        $looksLikeLauncherAlias = ($p -notmatch '/') -and ([string]$p -match '(?i)^RUN(?:-?DE)?~[0-9]+\.CMD$')
        if ($isPackageRoot -and ($resolvesToHelper -or $looksLikeLauncherAlias)) {
            Fail "Manifest path '$p' resolves to a reserved package helper. Package helpers must not be copied or staged into the clone."
        }
    }
}
foreach ($p in $files) {
    if ($deleted -contains $p) { Fail "Path is listed as both changed and deleted: $p" }
}
foreach ($p in $files) {
    if (-not (Test-Path -LiteralPath (Join-Path $pkg ($p -replace '/', '\')) -PathType Leaf)) { Fail "Listed file is missing from the delivery folder: $p" }
}
$allowed = @{}
foreach ($p in $files) { $allowed[(($p -replace '/', '\').ToLowerInvariant())] = $true }
foreach ($h in $helpers) { $allowed[$h.ToLowerInvariant()] = $true }
foreach ($item in (Get-ChildItem -LiteralPath $pkg -Recurse -File -Force)) {
    $rel = $item.FullName.Substring($pkg.Length + 1).ToLowerInvariant()
    if (-not $allowed.ContainsKey($rel)) { Fail "The delivery folder contains a file that is not in the manifest: $rel" }
}
$commitSubject = Get-Content -LiteralPath (Join-Path $pkg 'commit.txt') -TotalCount 1
if ($commitSubject -ne $manifest.commitSubject) { Fail "commit.txt first line does not match the manifest commit subject." }
if (-not $script:updateMode) {
    $prTitle = Get-Content -LiteralPath (Join-Path $pkg 'pr-title.txt') -TotalCount 1
    if ($prTitle -ne $manifest.prTitle) { Fail "pr-title.txt does not match the manifest PR title." }
    if (-not (Select-String -LiteralPath (Join-Path $pkg 'pr.txt') -Pattern $manifest.baseCommit -SimpleMatch -Quiet)) { Fail "pr.txt does not state the manifest base commit." }
}

# 4. Repository clone: saved path, otherwise the known default, otherwise ask once and save
$cfgPath = Join-Path $env:LOCALAPPDATA 'job-search-tracker-delivery.json'
$candidate = $env:JOBSEARCH_REPO
if ([string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $cfgPath)) {
    $candidate = (Get-Content -LiteralPath $cfgPath -Raw | ConvertFrom-Json).repoPath
}
if ([string]::IsNullOrWhiteSpace($candidate)) { $candidate = $DefaultRepo }
if (-not (Test-Clone $candidate)) {
    Write-Host "No valid job-search-tracker clone at: $candidate" -ForegroundColor Yellow
    $candidate = (Read-Host "Enter the full path of your job-search-tracker clone").Trim('"').Trim()
    if (-not (Test-Clone $candidate)) { Fail "That folder is not a clone of devan95/job-search-tracker." }
    @{ repoPath = $candidate } | ConvertTo-Json | Set-Content -LiteralPath $cfgPath -Encoding ASCII
    Write-Host "Saved the clone path to $cfgPath (local only, not in the repository)."
}
$script:repo = (Resolve-Path -LiteralPath $candidate).Path.TrimEnd('\')
if ($pkg.StartsWith($script:repo + '\', [StringComparison]::OrdinalIgnoreCase)) { Fail "The delivery folder is inside the repository clone. Extract it somewhere else." }
Set-Location -LiteralPath $script:repo

# 5. Clean tree, then leave the current branch only when that is safe
$dirty = Git-Try @('status', '--porcelain')
if ($script:rc -ne 0) { Fail "git status failed." }
if ($dirty) { Fail "The clone has uncommitted changes. Nothing was changed. Commit or stash them first:`n$($dirty -join "`n")" }
Git-Do @('fetch', 'origin')
$current = "$(Git-Try @('branch', '--show-current'))".Trim()
if (-not $current) { Fail "The clone is on a detached HEAD. Switch to a branch first." }
$target = 'main'
if ($script:updateMode) { $target = $manifest.branch }
if ($current -ne $target) {
    $upstream = "$(Git-Try @('rev-parse', '--abbrev-ref', '--symbolic-full-name', '@{u}'))".Trim()
    if ($script:rc -eq 0 -and $upstream) {
        $unshared = "$(Git-Try @('rev-list', '--count', "$upstream..HEAD"))".Trim()
        if ($script:rc -ne 0) { Fail "Cannot compare '$current' with its upstream $upstream." }
        if ($unshared -ne '0') { Fail "Branch '$current' has $unshared commit(s) not pushed to $upstream. Push or keep them, then switch to main yourself." }
    } else {
        $onRemote = Git-Try @('branch', '-r', '--contains', 'HEAD')
        if ($script:rc -ne 0 -or -not $onRemote) { Fail "Branch '$current' has no upstream and its commits are not on any remote branch. Push it or switch to main yourself." }
    }
    Write-Host "Leaving '$current' (clean, nothing unshared)."
}
if ($script:updateMode) {
    # Update mode: check out the existing, already pushed PR branch and bring it level with origin (fast-forward only).
    $remoteBranchSha = Get-RemoteBranchSha $manifest.branch
    if (-not $remoteBranchSha) { Fail "Exact remote branch refs/heads/$($manifest.branch) does not exist. Update mode needs an existing, pushed branch." }
    $null = Git-Try @('show-ref', '--verify', '--quiet', "refs/heads/$($manifest.branch)")
    if ($script:rc -ne 0) {
        Git-Do @('switch', '-c', $manifest.branch, '--track', "origin/$($manifest.branch)")
    } elseif ($current -ne $manifest.branch) {
        Git-Do @('switch', $manifest.branch)
    }
    $ahead = "$(Git-Try @('rev-list', '--count', "origin/$($manifest.branch)..$($manifest.branch)"))".Trim()
    $behind = "$(Git-Try @('rev-list', '--count', "$($manifest.branch)..origin/$($manifest.branch)"))".Trim()
    if ($ahead -ne '0') { Fail "Local $($manifest.branch) is $ahead commit(s) ahead of origin (or diverged; behind $behind). Resolve it manually; nothing was reset or overwritten." }
    if ($behind -ne '0') { Git-Do @('merge', '--ff-only', "origin/$($manifest.branch)") }
} else {
    $null = Git-Try @('show-ref', '--verify', '--quiet', 'refs/heads/main')
    if ($script:rc -ne 0) {
        Git-Do @('switch', '-c', 'main', '--track', 'origin/main')
    } elseif ($current -ne 'main') {
        Git-Do @('switch', 'main')
    }
    $ahead = "$(Git-Try @('rev-list', '--count', 'origin/main..main'))".Trim()
    $behind = "$(Git-Try @('rev-list', '--count', 'main..origin/main'))".Trim()
    if ($ahead -ne '0') { Fail "Local main is $ahead commit(s) ahead of origin/main (or diverged; behind $behind). Resolve it manually; nothing was reset or overwritten." }
    if ($behind -ne '0') { Git-Do @('merge', '--ff-only', 'origin/main') }
}

# 6. Preconditions
$dirty = Git-Try @('status', '--porcelain')
if ($dirty) { Fail "The working tree is not clean on $target." }
if ($script:updateMode) {
    # The delivery was built on one exact tip of the PR branch; if the branch has moved, the files may overwrite newer work.
    $tip = "$(Git-Try @('rev-parse', 'HEAD'))".Trim()
    if ($tip -ne $manifest.baseCommit) {
        Fail "$($manifest.branch) is at $tip but this delivery was built on $($manifest.baseCommit). Ask for a rebuilt delivery; do not copy files by hand."
    }
    if ($remoteBranchSha -ne $manifest.baseCommit) {
        Fail "Exact remote branch refs/heads/$($manifest.branch) is at $remoteBranchSha but this delivery was built on $($manifest.baseCommit). Ask for a rebuilt delivery; do not copy files by hand."
    }
} else {
    $originMain = "$(Git-Try @('rev-parse', 'origin/main'))".Trim()
    if ($originMain -ne $manifest.baseCommit) {
        Fail "origin/main is $originMain but this delivery was built on $($manifest.baseCommit). Ask for a rebuilt delivery; do not copy files by hand."
    }
    $null = Git-Try @('show-ref', '--verify', '--quiet', "refs/heads/$($manifest.branch)")
    if ($script:rc -eq 0) { Fail "Local branch $($manifest.branch) already exists." }
    $remoteBranchSha = Get-RemoteBranchSha $manifest.branch
    if ($remoteBranchSha) { Fail "Exact remote branch refs/heads/$($manifest.branch) already exists." }
}
# 7. Apply only the manifest (new mode: on a new branch from origin/main; update mode: on the checked-out PR branch)
Write-Host "== Applying delivery ==" -ForegroundColor Cyan
if (-not $script:updateMode) { Git-Do @('switch', '-c', $manifest.branch, '--no-track', 'origin/main') }
$script:branchCreated = $true
try {
    # Validate all targets before the first removal or copy.
    foreach ($p in ($files + $deleted)) {
        if (-not (Test-SafeDestinationPath $p $deleted)) {
            Fail "Manifest path '$p' failed the destination check: $($script:destinationReason). Refusing to continue."
        }
    }
    foreach ($p in $deleted) {
        if (-not (Test-ExactTrackedIndexPath $p)) {
            Fail "Deletion path is not an exact tracked Git index entry: $p"
        }
        $deletePath = Join-Path $script:repo ($p -replace '/', '\')
        if (-not (Test-Path -LiteralPath $deletePath -PathType Leaf)) {
            Fail "File listed as deleted does not exist in the repository: $p"
        }
    }
    # Deletions first: a deleted file may block the directory that a new file needs (file replaced by a folder).
    foreach ($p in $deleted) {
        if (-not (Test-SafeDestinationPath $p)) {
            Fail "Deletion path '$p' failed the destination check: $($script:destinationReason). Refusing to remove through it."
        }
        Remove-Item -LiteralPath (Join-Path $script:repo ($p -replace '/', '\')) -Force
    }
    foreach ($p in $files) {
        $dst = Join-Path $script:repo ($p -replace '/', '\')
        if (-not (Test-SafeDestinationPath $p)) {
            Fail "Destination '$p' failed the destination check: $($script:destinationReason). Refusing to copy through it."
        }
        if (Test-Path -LiteralPath $dst -PathType Container) {
            Fail "Destination is an existing directory, not a file: $p"
        }
        if (Test-Path -LiteralPath $dst) {
            # A clean git status can hide ignored files. Require the exact, literal index path.
            if (-not (Test-ExactTrackedIndexPath $p)) {
                Fail "Destination already exists but is not an exact tracked Git index path (possibly ignored or matched by a wildcard): $p"
            }
        } else {
            # Keep an exact list so rollback removes new files even when Git ignores them.
            $script:createdPaths += $p
        }
        $dir = Split-Path -Parent $dst
        if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        Copy-Item -LiteralPath (Join-Path $pkg ($p -replace '/', '\')) -Destination $dst -Force
    }
    if ($files.Count -gt 0) { Git-Do (@('add', '--') + $files) }
    if ($deleted.Count -gt 0) { Git-Do (@('add', '-A', '--') + $deleted) }
} catch {
    Fail "A file operation failed before the commit: $($_.Exception.Message)"
}

# 8. The change set must be exactly the manifest
# --no-renames: a delete plus an add is reported as two paths, never as 'old -> new'.
$status = Git-Try @('-c', 'core.quotepath=false', 'status', '--porcelain', '--no-renames', '--untracked-files=all')
$changed = @()
foreach ($line in $status) { if ($line.Length -gt 3) { $changed += ($line.Substring(3).Trim('"') -replace '\\', '/') } }
$expected = @($files + $deleted)
$extra = @($changed | Where-Object { $expected -notcontains $_ })
$unchanged = @($expected | Where-Object { $changed -notcontains $_ })
if ($extra.Count -gt 0) { Fail "Changes outside the manifest: $($extra -join ', ')" }
if ($unchanged.Count -gt 0) { Fail "Listed in the manifest but identical to the base commit (a file that did not change must not be delivered): $($unchanged -join ', ')" }
if ($changed.Count -eq 0) { Fail "The delivery produces no change on the base commit." }

# 9. Summary, then straight on: commit, push, PR (never force-push, never merge)
Write-Host ""
Write-Host "Mode:           $mode"
Write-Host "Branch:         $($manifest.branch)"
Write-Host "Base commit:    $($manifest.baseCommit)"
Write-Host "Commit subject: $($manifest.commitSubject)"
if (-not $script:updateMode) { Write-Host "PR title:       $($manifest.prTitle)" }
Write-Host "Changed paths:"
foreach ($c in $changed) { Write-Host "  $c" }
Write-Host ""
Git-Do @('diff', '--cached', '--stat')
Write-Host ""
Git-Do @('commit', '-F', (Join-Path $pkg 'commit.txt'))
$script:committed = $true
# Capture the exact commit just made; later pushes send this ID, never the mutable branch name.
$deliveryCommit = "$(Git-Try @('rev-parse', '--verify', 'HEAD'))".Trim()
if ($script:rc -ne 0 -or $deliveryCommit -notmatch '^[0-9a-f]{40}$') { Fail "Could not read the delivery commit after git commit. Nothing was pushed." }
if ($script:updateMode) {
    # The update is based on exactly baseCommit; the lease prevents recreating or replacing a moved/deleted PR ref.
    $parentCommit = "$(Git-Try @('rev-parse', '--verify', "$deliveryCommit^"))".Trim()
    if ($script:rc -ne 0 -or $parentCommit -ne $manifest.baseCommit) {
        Fail "Delivery commit $deliveryCommit is not directly on $($manifest.baseCommit) (parent: $parentCommit). Nothing was pushed; inspect the clone."
    }
    $expectedRemoteRef = "refs/heads/$($manifest.branch):$($manifest.baseCommit)"
    Git-Do @('push', "--force-with-lease=$expectedRemoteRef", 'origin', "${deliveryCommit}:refs/heads/$($manifest.branch)")
    # Tracking is set only after the push succeeded.
    $null = Git-Try @('branch', "--set-upstream-to=origin/$($manifest.branch)", $manifest.branch)
    if ($script:rc -ne 0) { Write-Host "Pushed, but could not set upstream tracking for $($manifest.branch). Run: git branch --set-upstream-to=origin/$($manifest.branch)" -ForegroundColor Yellow }
} else {
    Git-Do @('push', '-u', 'origin', $manifest.branch)
}
if ($script:updateMode) {
    # The PR already exists and picks up the new commit by itself; just show its address.
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $url = & gh pr view $manifest.branch --json url --jq .url 2>$null
    $ErrorActionPreference = $prev
    Write-Host ""
    Write-Host "Done. The new commit is pushed to $($manifest.branch). Review continues on the existing pull request." -ForegroundColor Green
    if ($url) { Write-Host "Pull request: $url" }
    exit 0
}
& gh pr create --base main --head $manifest.branch --title $manifest.prTitle --body-file (Join-Path $pkg 'pr.txt')
if ($LASTEXITCODE -ne 0) { Fail "gh pr create failed. The branch was pushed; open the PR manually from $($manifest.branch) with pr-title.txt and pr.txt." }
Write-Host ""
Write-Host "Done. Review happens on the pull request. You can delete the delivery folder and ZIP." -ForegroundColor Green
exit 0
