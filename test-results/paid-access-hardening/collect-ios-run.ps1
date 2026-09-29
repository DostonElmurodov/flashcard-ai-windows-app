param(
    [Parameter(Mandatory=$true)][long]$RunId,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-f]{40}$')][string]$ExpectedSha,
    [Parameter(Mandatory=$true)][string]$ResultDirectory
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath('D:\07 Hobby\FlashcardAI\test-results\paid-access-hardening\task3b-results\')
$taskDestination = [IO.Path]::GetFullPath($ResultDirectory)
if (-not $taskDestination.StartsWith($taskRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence destination must stay within Task3B results.' }
New-Item -ItemType Directory -Path $taskDestination -Force | Out-Null
$taskPreviousToken = $env:GH_TOKEN
try {
    $taskCredential = gh auth token --hostname github.com --user DostonElmurodov
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($taskCredential)) { throw 'Credential unavailable.' }
    $env:GH_TOKEN = $taskCredential
    $taskMetadata = gh run view $RunId --repo DostonElmurodov/flashcard-ai-ios --json databaseId,headSha,status,conclusion,url,createdAt,updatedAt,jobs
    if ($LASTEXITCODE -ne 0) { throw 'Run metadata failed.' }
    $taskRun = $taskMetadata | ConvertFrom-Json
    if ($taskRun.headSha -ne $ExpectedSha) { throw 'Run source differs from expected commit.' }
    if ($taskRun.status -ne 'completed') { throw 'Run has not completed.' }
    $taskMetadata | Set-Content -LiteralPath (Join-Path $taskDestination 'run.json') -Encoding utf8
    gh run view $RunId --repo DostonElmurodov/flashcard-ai-ios --log | Set-Content -LiteralPath (Join-Path $taskDestination 'full.log') -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw 'Log download failed.' }
    gh run download $RunId --repo DostonElmurodov/flashcard-ai-ios --dir (Join-Path $taskDestination 'artifacts')
    if ($LASTEXITCODE -ne 0) { throw 'Artifact download failed.' }
} finally { $env:GH_TOKEN = $taskPreviousToken; $taskCredential = $null }
$taskArtifactRoot = Join-Path $taskDestination 'artifacts\ios-validation'
$taskSource = (Get-Content -LiteralPath (Join-Path $taskArtifactRoot 'source-head.txt') -Raw).Trim()
if ($taskSource -ne $ExpectedSha) { throw 'Artifact source differs from expected commit.' }
$taskTestLog = Join-Path $taskArtifactRoot 'xcode-test.log'
$taskCases = @()
if (Test-Path -LiteralPath $taskTestLog) {
    $taskLogText = Get-Content -LiteralPath $taskTestLog -Raw
    $taskPattern = "Test Case '-\[(?<class>[^ ]+) (?<name>[^\]]+)\]' (?<outcome>passed|failed) \((?<seconds>[0-9.]+) seconds\)\."
    $taskCases = @([regex]::Matches($taskLogText, $taskPattern) | ForEach-Object { [pscustomobject]@{
        suite=$_.Groups['class'].Value; name=$_.Groups['name'].Value
        outcome=$_.Groups['outcome'].Value; seconds=$_.Groups['seconds'].Value
    } })
    ConvertTo-Json -InputObject $taskCases -Depth 5 | Set-Content -LiteralPath (Join-Path $taskDestination 'cases.json') -Encoding utf8
}
$taskHashPath = Join-Path $taskDestination 'evidence-hashes.json'
$taskHashes = @(Get-ChildItem -LiteralPath $taskDestination -Recurse -File | Where-Object { $_.FullName -ne $taskHashPath } | ForEach-Object {
    [pscustomobject]@{ path=$_.FullName.Substring($taskDestination.Length + 1); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
ConvertTo-Json -InputObject $taskHashes -Depth 5 | Set-Content -LiteralPath $taskHashPath -Encoding utf8
[pscustomobject]@{ run=$RunId; sha=$taskSource; conclusion=$taskRun.conclusion; cases=$taskCases.Count; passed=@($taskCases | Where-Object outcome -eq 'passed').Count; failed=@($taskCases | Where-Object outcome -eq 'failed').Count; files=$taskHashes.Count } | ConvertTo-Json
$taskCases | Where-Object outcome -eq 'failed' | Format-Table -AutoSize
