param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $projectRoot 'artifacts'
$stage = Join-Path $artifacts 'app'
$release = Join-Path $artifacts 'release'
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source

if (-not $stage.StartsWith($projectRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to clean a stage path outside the project root.'
}
Remove-Item $stage, $release -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $stage, $release -Force | Out-Null

$common = @(
    '--configuration', $Configuration,
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    "--property:Version=$Version",
    '--property:ContinuousIntegrationBuild=true'
)

& $dotnet publish (Join-Path $projectRoot 'src\DiscordRelay.Settings\DiscordRelay.Settings.csproj') @common --output $stage
if ($LASTEXITCODE -ne 0) { throw 'Settings publish failed.' }
& $dotnet publish (Join-Path $projectRoot 'src\DiscordRelay.Worker\DiscordRelay.Worker.csproj') @common --output $stage
if ($LASTEXITCODE -ne 0) { throw 'Worker publish failed.' }

Copy-Item (Join-Path $projectRoot 'README.md') $stage
Copy-Item (Join-Path $projectRoot 'LICENSE') $stage
Copy-Item (Join-Path $projectRoot 'TRADEMARKS.md') $stage
Copy-Item (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') $stage

$stagePrefix = $stage.TrimEnd('\') + '\'
$manifest = Get-ChildItem $stage -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($stagePrefix.Length).Replace('\', '/')
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $relative"
}
$manifest | Set-Content (Join-Path $stage 'SHA256SUMS') -Encoding ascii

$zipPath = Join-Path $release "PDB-$Version-win-x64.zip"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipStream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
$archive = [IO.Compression.ZipArchive]::new(
    $zipStream,
    [IO.Compression.ZipArchiveMode]::Create,
    $false
)
try {
    $fixedTimestamp = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
    foreach ($file in (Get-ChildItem $stage -File -Recurse | Sort-Object FullName)) {
        $relative = $file.FullName.Substring($stagePrefix.Length).Replace('\', '/')
        $entry = $archive.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = $fixedTimestamp
        $inputStream = $file.OpenRead()
        $outputStream = $entry.Open()
        try {
            $inputStream.CopyTo($outputStream)
        } finally {
            $outputStream.Dispose()
            $inputStream.Dispose()
        }
    }
} finally {
    $archive.Dispose()
    $zipStream.Dispose()
}
$zipHash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()

[pscustomobject]@{
    Version = $Version
    StageDirectory = $stage
    FileCount = @(Get-ChildItem $stage -File -Recurse).Count
    ZipPath = $zipPath
    ZipSha256 = $zipHash
} | ConvertTo-Json -Compress
