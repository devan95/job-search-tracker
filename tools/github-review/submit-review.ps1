[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ReviewFile
)

$ErrorActionPreference = 'Stop'

function Stop-Review([string]$Message) {
    Write-Host ""
    Write-Host "STOP: $Message" -ForegroundColor Red
    exit 1
}

function Invoke-GhJson([string[]]$GhArguments) {
    $output = & gh @GhArguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        Stop-Review ("GitHub CLI command failed: gh " + ($GhArguments -join ' ') + "`n" + ($output -join "`n"))
    }
    $joined = $output -join "`n"
    try {
        return ($joined | ConvertFrom-Json)
    } catch {
        Stop-Review "GitHub CLI returned data that was not valid JSON."
    }
}

function Add-PatchLine([hashtable]$Set, [int]$Number) {
    $Set[[string]$Number] = $true
}

try {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Stop-Review "GitHub CLI (gh) is not installed. Install it and run 'gh auth login' first."
    }

    & gh auth status --hostname github.com 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Stop-Review "You are not authenticated to github.com. Run 'gh auth login' and retry."
    }

    if (-not (Test-Path -LiteralPath $ReviewFile -PathType Leaf)) {
        Stop-Review "Review payload not found: $ReviewFile"
    }

    try {
        $review = Get-Content -LiteralPath $ReviewFile -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        Stop-Review "review.json is invalid JSON: $($_.Exception.Message)"
    }

    foreach ($field in @('repository', 'pull_number', 'commit_id', 'event', 'body', 'comments')) {
        if ($null -eq $review.$field) {
            Stop-Review "Required field '$field' is missing."
        }
    }

    if ([string]$review.repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
        Stop-Review "repository must use owner/repository format."
    }
    if ([int]$review.pull_number -lt 1) {
        Stop-Review "pull_number must be a positive integer."
    }
    if ([string]$review.commit_id -notmatch '^[0-9a-fA-F]{40}$') {
        Stop-Review "commit_id must be a full 40-character commit SHA."
    }

    $reviewEvent = ([string]$review.event).ToUpperInvariant()
    if ($reviewEvent -ne 'COMMENT') {
        Stop-Review "AC6 review packages must use the COMMENT event only."
    }
    if ($review.comments -isnot [System.Array]) {
        Stop-Review "comments must be a JSON array (use [] when there are no inline comments)."
    }
    if ([string]::IsNullOrWhiteSpace([string]$review.body) -and $review.comments.Count -eq 0) {
        Stop-Review "The review must contain a summary body or at least one inline comment."
    }

    $repo = [string]$review.repository
    $prNumber = [int]$review.pull_number
    $pr = Invoke-GhJson @('api', "repos/$repo/pulls/$prNumber")
    if ([string]$pr.state -ne 'open') {
        Stop-Review "PR #$prNumber is not open (state: $($pr.state))."
    }
    if ([string]$pr.head.sha -ne [string]$review.commit_id) {
        Stop-Review "The payload is stale. Payload SHA: $($review.commit_id); current PR head: $($pr.head.sha). Regenerate the review against the latest diff."
    }

    # Fetch every changed file. --slurp produces one JSON array containing all pages.
    $filesRaw = & gh api --paginate --slurp "repos/$repo/pulls/$prNumber/files?per_page=100" 2>&1
    if ($LASTEXITCODE -ne 0) {
        Stop-Review ("Could not fetch the PR file list: " + ($filesRaw -join "`n"))
    }
    try {
        $pages = (($filesRaw -join "`n") | ConvertFrom-Json)
    } catch {
        Stop-Review "Could not parse the PR file list returned by GitHub."
    }
    $changedFiles = @()
    foreach ($page in $pages) {
        if ($page -is [System.Array]) { $changedFiles += $page }
        elseif ($null -ne $page) { $changedFiles += $page }
    }

    $diffMap = @{}
    foreach ($file in $changedFiles) {
        if ([string]::IsNullOrEmpty([string]$file.patch)) {
            $diffMap[[string]$file.filename] = @{ right = @{}; left = @{}; unavailable = $true }
            continue
        }

        $right = @{}
        $left = @{}
        $rightHunks = @{}
        $leftHunks = @{}
        $oldLine = 0
        $newLine = 0
        $hunkId = 0
        foreach ($patchLine in ([string]$file.patch -split "`n")) {
            if ($patchLine -match '^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@') {
                $oldLine = [int]$Matches[1]
                $newLine = [int]$Matches[3]
                $hunkId++
                continue
            }
            # GitHub's per-file patch contains hunk bodies, not the file-level +++/--- headers.
            # Therefore a content line beginning with ++ or -- must still be counted.
            if ($patchLine.StartsWith('\')) { continue }
            if ($patchLine.StartsWith('+')) {
                Add-PatchLine $right $newLine
                $rightHunks[[string]$newLine] = $hunkId
                $newLine++
            } elseif ($patchLine.StartsWith('-')) {
                Add-PatchLine $left $oldLine
                $leftHunks[[string]$oldLine] = $hunkId
                $oldLine++
            } elseif ($patchLine.StartsWith(' ')) {
                Add-PatchLine $right $newLine
                Add-PatchLine $left $oldLine
                $rightHunks[[string]$newLine] = $hunkId
                $leftHunks[[string]$oldLine] = $hunkId
                $newLine++
                $oldLine++
            }
        }
        $diffMap[[string]$file.filename] = @{
            right = $right
            left = $left
            rightHunks = $rightHunks
            leftHunks = $leftHunks
            unavailable = $false
        }
    }

    $validatedComments = @()
    foreach ($comment in $review.comments) {
        foreach ($field in @('path', 'line', 'side', 'body')) {
            if ($null -eq $comment.$field) {
                Stop-Review "An inline comment is missing required field '$field'."
            }
        }
        $path = [string]$comment.path
        $line = [int]$comment.line
        $side = ([string]$comment.side).ToUpperInvariant()
        if ($line -lt 1 -or $side -notin @('LEFT', 'RIGHT')) {
            Stop-Review "Invalid line or side for inline comment on '$path'."
        }
        if (-not $diffMap.ContainsKey($path)) {
            Stop-Review "Inline comment path '$path' is not in the current PR file list."
        }
        $fileDiff = $diffMap[$path]
        if ($fileDiff.unavailable) {
            Stop-Review "GitHub did not provide a patch for '$path'; cannot safely validate the inline comment."
        }
        $sideKey = $side.ToLowerInvariant()
        if (-not $fileDiff[$sideKey].ContainsKey([string]$line)) {
            Stop-Review "Line $line on side $side of '$path' is not present in the current PR diff."
        }

        $apiComment = @{
            path = $path
            line = $line
            side = $side
            body = [string]$comment.body
        }
        if ([string]::IsNullOrWhiteSpace([string]$comment.body)) {
            Stop-Review "Inline comment body cannot be empty."
        }

        if ($null -ne $comment.start_line -or $null -ne $comment.start_side) {
            if ($null -eq $comment.start_line -or $null -eq $comment.start_side) {
                Stop-Review "Multi-line comments must provide both start_line and start_side."
            }
            $startLine = [int]$comment.start_line
            $startSide = ([string]$comment.start_side).ToUpperInvariant()
            if ($startLine -lt 1 -or $startSide -notin @('LEFT', 'RIGHT')) {
                Stop-Review "Invalid start_line or start_side for inline comment on '$path'."
            }
            $startKey = $startSide.ToLowerInvariant()
            if (-not $fileDiff[$startKey].ContainsKey([string]$startLine)) {
                Stop-Review "Start line $startLine on side $startSide of '$path' is not present in the current PR diff."
            }
            if ($startSide -ne $side -or $startLine -gt $line) {
                Stop-Review "Multi-line comment range must use the same side and start at or before the ending line."
            }
            $hunkMapKey = $startKey + 'Hunks'
            $startHunk = $fileDiff[$hunkMapKey][[string]$startLine]
            $endHunk = $fileDiff[$hunkMapKey][[string]$line]
            if ($null -eq $startHunk -or $null -eq $endHunk -or $startHunk -ne $endHunk) {
                Stop-Review "Multi-line comment range on '$path' must stay within one diff hunk."
            }
            $apiComment.start_line = $startLine
            $apiComment.start_side = $startSide
        }
        $validatedComments += $apiComment
    }

    Write-Host ""
    Write-Host "GitHub PR review preflight" -ForegroundColor Cyan
    Write-Host "Repository : $repo"
    Write-Host "PR         : #$prNumber - $($pr.title)"
    Write-Host "Head SHA   : $($pr.head.sha)"
    Write-Host "Event      : $reviewEvent"
    Write-Host "Inline     : $($validatedComments.Count) comment(s)"
    Write-Host ""
    Write-Host "Summary:"
    Write-Host ([string]$review.body)
    foreach ($c in $validatedComments) {
        Write-Host ""
        Write-Host ("[{0}] {1}:{2}" -f $c.side, $c.path, $c.line) -ForegroundColor Yellow
        Write-Host ([string]$c.body)
    }

    Write-Host ""
    Write-Host "This will submit ONE GitHub review. It will not edit files, push commits, merge, or change repository settings." -ForegroundColor Cyan
    $confirmation = Read-Host "Type SUBMIT to continue"
    if ($confirmation -cne 'SUBMIT') {
        Write-Host "Cancelled. No review was submitted." -ForegroundColor Yellow
        exit 0
    }

    # Re-fetch after the interactive pause so a newly pushed commit cannot receive this stale review.
    $prAfterConfirmation = Invoke-GhJson @('api', "repos/$repo/pulls/$prNumber")
    if ([string]$prAfterConfirmation.state -ne 'open') {
        Stop-Review "PR #$prNumber is no longer open. No review was submitted."
    }
    if ([string]$prAfterConfirmation.head.sha -ne [string]$review.commit_id) {
        Stop-Review "The PR head changed while awaiting confirmation. Payload SHA: $($review.commit_id); current PR head: $($prAfterConfirmation.head.sha). No review was submitted. Regenerate the review."
    }

    $payload = @{
        commit_id = [string]$review.commit_id
        event = 'COMMENT'
        body = [string]$review.body
        comments = $validatedComments
    }
    $tempFile = Join-Path ([System.IO.Path]::GetTempPath()) ("gh-pr-review-" + [guid]::NewGuid().ToString('N') + ".json")
    try {
        $jsonBody = $payload | ConvertTo-Json -Depth 100
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($tempFile, $jsonBody, $utf8NoBom)
        $result = Invoke-GhJson @('api', "repos/$repo/pulls/$prNumber/reviews", '--method', 'POST', '--input', $tempFile)
    } finally {
        if (Test-Path -LiteralPath $tempFile) {
            Remove-Item -LiteralPath $tempFile -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Host ""
    Write-Host "Review submitted successfully." -ForegroundColor Green
    if ($result.html_url) {
        Write-Host "Review URL: $($result.html_url)" -ForegroundColor Green
    } else {
        Write-Host "GitHub returned review ID: $($result.id)"
    }
    exit 0
} catch {
    Stop-Review $_.Exception.Message
}
