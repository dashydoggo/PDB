param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
if ($env:WIX_ACCEPT_EULA -ne 'true') {
    throw @'
WiX Toolset 7 requires acceptance of its Open Source Maintenance Fee EULA.
Review https://wixtoolset.org/osmf/ and set WIX_ACCEPT_EULA=true only if you are authorized to accept it.
The project does not accept this agreement automatically.
'@
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$stage = Join-Path $projectRoot 'artifacts\app'
if (-not (Test-Path (Join-Path $stage 'PDB.exe'))) {
    & (Join-Path $PSScriptRoot 'publish.ps1') -Version $Version
    if ($LASTEXITCODE -ne 0) { throw 'Application publishing failed.' }
}

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
& $dotnet build (Join-Path $projectRoot 'installer\DiscordRelay.Installer.wixproj') `
    --configuration Release `
    --property:StageDir="$stage" `
    --property:ProductVersion=$Version `
    --property:AcceptEula=wix7
if ($LASTEXITCODE -ne 0) {
    throw 'MSI build failed.'
}

$msi = Get-ChildItem (Join-Path $projectRoot 'artifacts\msi') -Filter '*.msi' |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
[pscustomobject]@{
    MsiPath = $msi.FullName
    Sha256 = (Get-FileHash $msi.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    Signed = $false
} | ConvertTo-Json -Compress
