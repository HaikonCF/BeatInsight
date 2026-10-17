[CmdletBinding()]
param(
    [string]$Version,
    [ValidatePattern('^[a-z0-9][a-z0-9.-]*$')]
    [string]$Channel = 'stable',
    [ValidatePattern('^[a-z0-9][a-z0-9.-]*$')]
    [string]$FeedName,
    [switch]$SkipRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'BeatInsight\BeatInsight.csproj'
$helperProjectPath = Join-Path $repositoryRoot 'BeatInsight\Diagnostics\NativeLazer\NativeLazerFastRefreshRunner.csproj'
$lazerKeyRunnerProjectPath = Join-Path $repositoryRoot 'tools\LazerKeyOverlayRunner\LazerKeyOverlayRunner.csproj'
$communityOverlaysProjectPath = Join-Path $repositoryRoot 'BeatInsight.CommunityOverlays.Poc\BeatInsight.CommunityOverlays.Poc.csproj'
$publishProfile = Join-Path $repositoryRoot 'BeatInsight\Properties\PublishProfiles\PublicBetaWinX64.pubxml'
$iconPath = Join-Path $repositoryRoot 'BeatInsight\Assets\Brand\BeatInsight.ico'
$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
$releaseRoot = Join-Path $artifactsRoot 'release'
$stagingBase = Join-Path $artifactsRoot 'release-staging'
if ([string]::IsNullOrWhiteSpace($FeedName)) {
    $FeedName = $Channel
}
$feedDirectory = Join-Path (Join-Path $artifactsRoot 'update-feed') $FeedName

function Assert-FileExists([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label is missing: $Path"
    }
}

function Invoke-Checked([string]$Description, [scriptblock]$Command) {
    Write-Host $Description
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Get-PortableRelativePath([string]$BasePath, [string]$TargetPath) {
    $baseFullPath = [IO.Path]::GetFullPath($BasePath).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $targetFullPath = [IO.Path]::GetFullPath($TargetPath)
    $baseUri = [Uri]::new($baseFullPath)
    $targetUri = [Uri]::new($targetFullPath)
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('/', '\')
}

Assert-FileExists $projectPath 'BeatInsight project'
Assert-FileExists $helperProjectPath 'NativeLazer helper project'
Assert-FileExists $lazerKeyRunnerProjectPath 'Lazer key overlay runner project'
Assert-FileExists $communityOverlaysProjectPath 'CommunityOverlays companion project'
Assert-FileExists $publishProfile 'win-x64 publish profile'
Assert-FileExists $iconPath 'BeatInsight icon'

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$versionNode = $project.SelectSingleNode('/Project/PropertyGroup/BeatInsightVersion')
if ($null -eq $versionNode) {
    throw 'BeatInsightVersion is missing from BeatInsight.csproj.'
}
$configuredVersion = [string]$versionNode.InnerText
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $configuredVersion
}
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    throw "Requested version '$Version' is not a supported semantic version."
}

$releaseDirectory = Join-Path $releaseRoot $Version
$stagingRoot = Join-Path $stagingBase $Version
$publishDirectory = Join-Path $stagingRoot 'publish'

if (Test-Path -LiteralPath $releaseDirectory) {
    Remove-Item -LiteralPath $releaseDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $feedDirectory -Force | Out-Null

if (-not $SkipRestore) {
    Invoke-Checked 'Restoring pinned release tooling...' {
        dotnet tool restore
    }
    Invoke-Checked 'Restoring NativeLazer helper for win-x64...' {
        dotnet restore $helperProjectPath -r win-x64
    }
    Invoke-Checked 'Restoring Lazer key overlay runner for win-x64...' {
        dotnet restore $lazerKeyRunnerProjectPath -r win-x64
    }
    Invoke-Checked 'Restoring CommunityOverlays companion for win-x64...' {
        dotnet restore $communityOverlaysProjectPath -r win-x64
    }
    Invoke-Checked 'Restoring BeatInsight for win-x64...' {
        dotnet restore $projectPath -r win-x64
    }
}

Invoke-Checked 'Publishing BeatInsight Release win-x64 self-contained...' {
    dotnet publish $projectPath -c Release -r win-x64 --self-contained true --no-restore `
        -p:PublishProfile=$publishProfile -p:PublishDir=$publishDirectory `
        -p:BeatInsightVersion=$Version -p:Version=$Version -p:InformationalVersion=$Version
}

$requiredFiles = @(
    'BeatInsight.exe',
    'BeatInsight.dll',
    'LICENSE.txt',
    'THIRD-PARTY-NOTICES.md',
    'ASSET-NOTICES.md',
    'THIRD-PARTY-SOURCE.md',
    'NativeLazer\NativeLazerFastRefreshRunner.exe',
    'NativeLazer\NativeLazerFastRefreshRunner.dll',
    'NativeLazer\NativeLazerFastRefreshRunner.deps.json',
    'NativeLazer\NativeLazerFastRefreshRunner.runtimeconfig.json',
    'NativeLazer\Microsoft.Diagnostics.Runtime.dll',
    'LazerKeys\LazerKeyOverlayRunner.exe',
    'LazerKeys\LazerKeyOverlayRunner.dll',
    'LazerKeys\LazerKeyOverlayRunner.deps.json',
    'LazerKeys\LazerKeyOverlayRunner.runtimeconfig.json',
    'LazerKeys\LazerKeyOverlayReader.dll',
    'LazerKeys\Microsoft.Diagnostics.Runtime.dll',
    'CommunityOverlays\BeatInsight.CommunityOverlays.Poc.exe',
    'CommunityOverlays\BeatInsight.CommunityOverlays.Poc.dll',
    'CommunityOverlays\Microsoft.Web.WebView2.Core.dll',
    'CommunityOverlays\Microsoft.Web.WebView2.Wpf.dll',
    'CommunityOverlays\runtimes\win-x64\native\WebView2Loader.dll',
    'CommunityOverlays\SampleOverlay\index.html'
)
foreach ($relativePath in $requiredFiles) {
    Assert-FileExists (Join-Path $publishDirectory $relativePath) "Required publish file '$relativePath'"
}

$forbiddenFiles = Get-ChildItem -LiteralPath $publishDirectory -Recurse -File | Where-Object {
    $_.Extension -in @('.pdb', '.cs', '.csx', '.log') -or
    $_.Name -in @('NativeLazerCleanRoomRunner.dll', 'NativeLazerGameplayClockDiscoveryRunner.dll') -or
    $_.FullName -match '[\\/](SelfTest|TestAssets)[\\/]' -or
    $_.FullName -match '[\\/](obj|TestResults|\.git)[\\/]'
}
if ($forbiddenFiles) {
    throw "Forbidden files entered the publish output: $($forbiddenFiles.FullName -join ', ')"
}

$publishFiles = Get-ChildItem -LiteralPath $publishDirectory -Recurse -File |
    Sort-Object { Get-PortableRelativePath $publishDirectory $_.FullName }
$manifestPath = Join-Path $releaseDirectory 'publish-manifest.tsv'
$manifestLines = @('relative_path' + "`t" + 'size_bytes')
$manifestLines += $publishFiles | ForEach-Object {
    $relative = (Get-PortableRelativePath $publishDirectory $_.FullName).Replace('\', '/')
    $relative + "`t" + $_.Length
}
[IO.File]::WriteAllLines($manifestPath, $manifestLines, [Text.UTF8Encoding]::new($false))

# A failed reporting step may leave this exact version in the controlled feed.
# Replace only that version; older releases remain available for delta generation.
Get-ChildItem -LiteralPath $feedDirectory -File -Filter '*.nupkg' |
    Where-Object { $_.Name -like "*$Version*" } |
    Remove-Item -Force

Invoke-Checked 'Creating Velopack per-user installer...' {
    dotnet tool run vpk pack `
        --packId BeatInsightApp `
        --packVersion $Version `
        --packDir $publishDirectory `
        --mainExe BeatInsight.exe `
        --packTitle BeatInsight `
        --packAuthors BeatInsight `
        --runtime win-x64 `
        --channel $Channel `
        --icon $iconPath `
        --shortcuts StartMenuRoot `
        --noPortable `
        --outputDir $feedDirectory
}

$versionPackages = Get-ChildItem -LiteralPath $feedDirectory -File -Filter '*.nupkg' |
    Where-Object { $_.Name -like "*$Version*" }
if (-not $versionPackages) {
    throw "Velopack did not produce a package for version '$Version'."
}

$feedMetadata = Get-ChildItem -LiteralPath $feedDirectory -File | Where-Object {
    $_.Name -like '*-Setup.exe' -or
    $_.Name -like 'RELEASES*' -or
    $_.Name -like 'assets*.json' -or
    $_.Name -like 'releases*.json'
}
foreach ($file in @($versionPackages) + @($feedMetadata)) {
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $releaseDirectory $file.Name) -Force
}

$installer = Get-ChildItem -LiteralPath $releaseDirectory -File -Filter '*-Setup.exe' |
    Sort-Object Name |
    Select-Object -First 1
if ($null -eq $installer) {
    throw 'Velopack did not produce a Setup.exe installer.'
}

$hashPath = Join-Path $releaseDirectory 'SHA256SUMS.txt'
$hashFiles = Get-ChildItem -LiteralPath $releaseDirectory -File | Where-Object {
    $_.Extension -in @('.exe', '.nupkg')
} | Sort-Object Name
$hashLines = $hashFiles | ForEach-Object {
    $itemHash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
    "$($itemHash.Hash.ToLowerInvariant())  $($_.Name)"
}
[IO.File]::WriteAllLines($hashPath, $hashLines, [Text.UTF8Encoding]::new($false))
$installerHash = Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256

$summaryPath = Join-Path $releaseDirectory 'release-summary.txt'
$publishSize = ($publishFiles | Measure-Object -Property Length -Sum).Sum
$fullPackageNames = (@($versionPackages | Where-Object Name -like '*-full.nupkg') |
    ForEach-Object { $_.Name }) -join ','
$deltaPackageNames = (@($versionPackages | Where-Object Name -like '*-delta.nupkg') |
    ForEach-Object { $_.Name }) -join ','
$summary = @(
    "product=BeatInsight",
    "version=$Version",
    'runtime=win-x64',
    "channel=$Channel",
    "feed_directory=$feedDirectory",
    'self_contained=true',
    'publish_trimmed=false',
    'publish_single_file=false',
    "publish_file_count=$($publishFiles.Count)",
    "publish_size_bytes=$publishSize",
    "installer=$($installer.Name)",
    "installer_size_bytes=$($installer.Length)",
    "installer_sha256=$($installerHash.Hash.ToLowerInvariant())",
    "full_packages=$fullPackageNames",
    "delta_packages=$deltaPackageNames",
    'signed=false',
    'install_root=%LOCALAPPDATA%\BeatInsightApp',
    'user_data_policy=preserve %LOCALAPPDATA%\BeatInsight on uninstall'
)
[IO.File]::WriteAllLines($summaryPath, $summary, [Text.UTF8Encoding]::new($false))

Write-Host "Release artifacts: $releaseDirectory"
Write-Host "Local update feed: $feedDirectory"
Write-Host "Installer: $($installer.FullName)"
Write-Host "SHA256: $($installerHash.Hash)"
