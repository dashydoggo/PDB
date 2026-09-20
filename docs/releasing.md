# Release

## Build

```powershell
./scripts/publish.ps1 -Version 1.0.1
$env:WIX_ACCEPT_EULA = 'true'
./scripts/build-msi.ps1 -Version 1.0.1
```

Outputs:

```text
artifacts/release/PDB-1.0.1-win-x64.zip
artifacts/msi/PDB-1.0.1-x64.msi
```

WiX 7 requires OSMF EULA acceptance. Set `WIX_ACCEPT_EULA` only when authorized.

## Sign

Sign `PDB.exe`, `PDB.Worker.exe`, and the MSI with Authenticode before a production release. This repository does not contain signing credentials.

## Publish

1. Test the MSI on clean Windows.
2. Tag the commit as `v1.0.1`.
3. Run the `release` workflow.
4. Verify signatures and SHA-256 checksums.

Do not publish settings, notification databases, custom sounds, screenshots, or credentials.
