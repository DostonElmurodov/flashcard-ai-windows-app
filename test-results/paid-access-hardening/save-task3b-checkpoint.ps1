$ErrorActionPreference = 'Stop'
$checkpointRoot = 'D:\07 Hobby\FlashcardAI'
$checkpointFolder = Join-Path $checkpointRoot 'test-results\paid-access-hardening'
$checkpointSdd = Join-Path $checkpointRoot '.superpowers\sdd\2026-09-27-paid-access-hardening'
$checkpointName = 'task3b-checkpoint-544c16e'
$checkpointZip = Join-Path $checkpointFolder ($checkpointName + '.zip')
if (Test-Path -LiteralPath $checkpointZip) { throw 'Checkpoint already exists; do not overwrite.' }
$checkpointPatch = Join-Path $checkpointFolder ($checkpointName + '-ios.patch')
$checkpointRepo = Join-Path $checkpointFolder 'ios'
git -C $checkpointRepo diff --binary --output=$checkpointPatch 9096c1e3b20fb920726bd471ed4a092783ee3cc7 544c16e1173c3fe2b219be050bd6416d3e2b89b2
if ($LASTEXITCODE -ne 0) { throw 'Could not save full iOS patch.' }
$checkpointFiles = @(Get-ChildItem -LiteralPath $checkpointSdd -Recurse -File | Where-Object { $_.Name -ne 'task-3B-source-review-544c16e.md' })
$checkpointFiles += @(Get-ChildItem -LiteralPath (Join-Path $checkpointRoot 'docs\superpowers\plans') -File)
$checkpointFiles += @(Get-ChildItem -LiteralPath (Join-Path $checkpointFolder 'task3b-results') -Recurse -File)
$checkpointFiles += Get-Item -LiteralPath $checkpointPatch,$PSCommandPath
$checkpointEntries = @($checkpointFiles | Sort-Object FullName -Unique | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($checkpointRoot, $_.FullName)
    if ($relative.StartsWith('..') -or [IO.Path]::IsPathRooted($relative)) { throw "Path outside workspace: $relative" }
    [pscustomobject]@{ path=$_.FullName; entry=$relative.Replace('\','/'); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$checkpointManifest = Join-Path $checkpointFolder ($checkpointName + '-manifest.json')
[ordered]@{ iosHead='544c16e1173c3fe2b219be050bd6416d3e2b89b2'; createdUtc=[DateTime]::UtcNow.ToString('o'); scope='Current SDD/plans, every Task3B Mac evidence file including blocked jobs, full iOS patch from original base; active source review excluded until complete. Complements immutable earlier archives. Final candidate has no Mac GREEN because GitHub billing blocked both jobs before start.'; files=$checkpointEntries } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $checkpointManifest
$manifestFile = Get-Item -LiteralPath $checkpointManifest
$checkpointEntries += [pscustomobject]@{ path=$checkpointManifest; entry=[IO.Path]::GetRelativePath($checkpointRoot,$checkpointManifest).Replace('\','/'); bytes=$manifestFile.Length; sha256=(Get-FileHash -LiteralPath $checkpointManifest -Algorithm SHA256).Hash }
$archive = [IO.Compression.ZipFile]::Open($checkpointZip,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($entry in $checkpointEntries) {
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$entry.path,$entry.entry,[IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $archive.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($checkpointZip)
try {
    if ($archive.Entries.Count -ne $checkpointEntries.Count) { throw 'Archive entry count mismatch.' }
    foreach ($entry in $checkpointEntries) {
        $stream = $archive.GetEntry($entry.entry).Open()
        try { $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
        finally { $stream.Dispose() }
        if ($digest -ne $entry.sha256) { throw "Archive hash mismatch: $($entry.entry)" }
    }
} finally { $archive.Dispose() }
$receipt = [ordered]@{ archive=$checkpointZip; sha256=(Get-FileHash -LiteralPath $checkpointZip -Algorithm SHA256).Hash; bytes=(Get-Item -LiteralPath $checkpointZip).Length; verifiedEntries=$checkpointEntries.Count; iosHead='544c16e1173c3fe2b219be050bd6416d3e2b89b2'; manifest=$checkpointManifest; createdUtc=[DateTime]::UtcNow.ToString('o') }
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $checkpointSdd 'task3b-544c16e-archive-receipt.json')
$receipt | ConvertTo-Json
