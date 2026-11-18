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
$DefaultRepo = 'C:\Users\Devan Shah\Repo\job-search-tracker'

function Fail([string]$m) {
    Write-Host ""
    Write-Host "STOP: $m" -ForegroundColor Red
    if ($script:branchCreated -and -not $script:committed) { Rollback }
    exit 1
}

function Rollback {
    # Only reached before the commit, on a tree that was clean when the script started.
    try {
        Set-Location -LiteralPath $script:repo
        $ErrorActionPreference = 'Continue'
        & git reset --hard HEAD *> $null
        $own = @($script:manifest.files)
        if ($own.Count -gt 0) { & git clean -fdq -- @own *> $null }
        if ($script:updateMode) {
            # The branch already existed and was pushed: keep it, only undo the uncommitted changes.
            Write-Host "Rolled back: working tree on $($script:manifest.branch) restored to its last commit." -ForegroundColor Yellow
        } else {
            & git switch main *> $null
            & git branch -D $script:manifest.branch *> $null
            Write-Host "Rolled back: branch $($script:manifest.branch) removed, working tree restored." -ForegroundColor Yellow
        }
    } catch {
        Write-Host "Rollback incomplete; check 'git status' in $($script:repo)." -ForegroundColor Yellow
    }
    $script:branchCreated = $false
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
if ($manifest.branch -notmatch '^ac[1-6]/[a-z0-9][a-z0-9-]*$') { Fail "Branch '$($manifest.branch)' does not match ac1..ac6/<short-task-name>." }
# One path form (forward slashes) for every later comparison.
$files = @($manifest.files | ForEach-Object { "$_" -replace '\\', '/' })
$deleted = @($manifest.deleted | ForEach-Object { "$_" -replace '\\', '/' })
if ($files.Count + $deleted.Count -eq 0) { Fail "Manifest lists no files." }
foreach ($p in ($files + $deleted)) {
    if (-not (Test-SafePath $p)) { Fail "Unsafe path in manifest: '$p'." }
}
foreach ($p in $files) {
    if ($deleted -contains $p) { Fail "Path is listed as both changed and deleted: $p" }
}
foreach ($p in $files) {
    if (-not (Test-Path -LiteralPath (Join-Path $pkg ($p -replace '/', '\')) -PathType Leaf)) { Fail "Listed file is missing from the delivery folder: $p" }
}
$helpers = @('delivery-manifest.json', 'commit.txt', 'run-delivery.cmd')
if (-not $script:updateMode) { $helpers += @('pr-title.txt', 'pr.txt') }
foreach ($h in $helpers) {
    if (-not (Test-Path -LiteralPath (Join-Path $pkg $h))) { Fail "Delivery file missing: $h" }
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
    $null = Git-Try @('ls-remote', '--exit-code', '--heads', 'origin', $manifest.branch)
    if ($script:rc -ne 0) { Fail "Remote branch $($manifest.branch) does not exist. Update mode needs an existing, pushed branch." }
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
} else {
    $originMain = "$(Git-Try @('rev-parse', 'origin/main'))".Trim()
    if ($originMain -ne $manifest.baseCommit) {
        Fail "origin/main is $originMain but this delivery was built on $($manifest.baseCommit). Ask for a rebuilt delivery; do not copy files by hand."
    }
    $null = Git-Try @('show-ref', '--verify', '--quiet', "refs/heads/$($manifest.branch)")
    if ($script:rc -eq 0) { Fail "Local branch $($manifest.branch) already exists." }
    $null = Git-Try @('ls-remote', '--exit-code', '--heads', 'origin', $manifest.branch)
    if ($script:rc -eq 0) { Fail "Remote branch $($manifest.branch) already exists." }
}
foreach ($p in $deleted) {
    if (-not (Test-Path -LiteralPath (Join-Path $script:repo ($p -replace '/', '\')) -PathType Leaf)) { Fail "File listed as deleted does not exist in the repository: $p" }
}

# 7. Apply only the manifest (new mode: on a new branch from origin/main; update mode: on the checked-out PR branch)
Write-Host "== Applying delivery ==" -ForegroundColor Cyan
if (-not $script:updateMode) { Git-Do @('switch', '-c', $manifest.branch, '--no-track', 'origin/main') }
$script:branchCreated = $true
try {
    # Deletions first: a deleted file may block the directory that a new file needs (file replaced by a folder).
    foreach ($p in $deleted) { Remove-Item -LiteralPath (Join-Path $script:repo ($p -replace '/', '\')) -Force }
    foreach ($p in $files) {
        $dst = Join-Path $script:repo ($p -replace '/', '\')
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
Git-Do @('push', '-u', 'origin', $manifest.branch)
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
