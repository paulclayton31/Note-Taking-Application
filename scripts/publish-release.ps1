$ErrorActionPreference = 'Stop'

$releaseVersion = (Get-Content -Raw -LiteralPath 'release-version.txt').Trim()
if ($releaseVersion -notmatch '^\d+\.\d+\.\d+(?:-(?:preview|alpha|beta|rc)\.\d+)?$') { throw 'Invalid release version' }
$isPreview = $releaseVersion.Contains('-')
$releaseTag = "v$releaseVersion"
$releaseNotes = "docs/releases/$releaseTag.md"
if (-not (Test-Path -LiteralPath $releaseNotes)) { throw "Missing release notes: $releaseNotes" }
$releaseAssets = @(Get-ChildItem -LiteralPath 'release-assets' -File)
$expectedNames = @('SurfaceNotebook-windows-x64.exe', 'SurfaceNotebook-windows-arm64.exe', 'SHA256SUMS.txt',
    'workspace-light.png', 'workspace-dark.png', 'paper-settings.png', 'focus-mode.png', 'portrait.png', 'compact-window.png')
foreach ($assetName in $expectedNames) {
    if ($releaseAssets.Name -notcontains $assetName) { throw "Missing release asset: $assetName" }
}

# Keep a failed upload as a draft. Never silently replace an already public release.
$existingRelease = gh release view $releaseTag --json isDraft,targetCommitish 2>$null
if ($LASTEXITCODE -eq 0) {
    $releaseState = $existingRelease | ConvertFrom-Json
    if (-not $releaseState.isDraft) { throw "Release $releaseTag is already published. Bump release-version.txt." }
    if ($releaseState.targetCommitish -ne $env:RELEASE_COMMIT) { throw 'Existing draft targets a different commit' }
}
else {
    gh release create $releaseTag --target $env:RELEASE_COMMIT --title "Surface Notebook $releaseTag" --notes-file $releaseNotes --draft
    if ($LASTEXITCODE -ne 0) { throw 'Could not create draft release' }
}

$releasePaths = @($releaseAssets.FullName)
gh release upload $releaseTag @releasePaths --clobber
if ($LASTEXITCODE -ne 0) { throw 'Release asset upload failed; the release remains a draft' }
$uploadedRelease = gh release view $releaseTag --json assets | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Could not verify uploaded assets' }
foreach ($assetName in $expectedNames) {
    $localAsset = $releaseAssets | Where-Object Name -eq $assetName
    $remoteAsset = $uploadedRelease.assets | Where-Object name -eq $assetName
    if (-not $remoteAsset -or $remoteAsset.size -ne $localAsset.Length) {
        throw "Uploaded release asset is missing or incomplete: $assetName"
    }
}
if ($isPreview) {
    gh release edit $releaseTag --draft=false --prerelease --latest=false
}
else {
    gh release edit $releaseTag --draft=false --prerelease=false --latest
}
if ($LASTEXITCODE -ne 0) { throw 'Could not publish release' }
gh release view $releaseTag --json url
