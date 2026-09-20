# Discord PDB

Discord PDB displays Discord banners without message content. The original notification remains in Notification Center.

Discord PDB is independent software and is not affiliated with Discord Inc.

## Setup

1. Install `PDB-1.0.1-x64.msi`.
2. Open **Discord PDB**.
3. Select **Grant access**.
4. In Windows notification settings:
   - Discord: banners off, Notification Center on.
   - Discord PDB: banners on.
5. Select **Test**.

See [docs/setup.md](docs/setup.md) for the setup wizard steps.

## Options

- Sender avatar
- Notification history
- Replacement text
- Discord Stable or Canary
- Default, IM, Mail, Reminder, SMS, silent, or custom WAV sound

## Limits

Discord does not provide message routing data to Windows. A click opens the exact link when available. Otherwise, it opens Discord Direct Messages.

Avatar recovery reads the local Windows notification database. It does not use Discord credentials or APIs. Notification text is not logged.

## Build

Requirements are pinned in the repository.

```powershell
dotnet restore src/DiscordRelay.Worker/DiscordRelay.Worker.csproj --runtime win-x64 --locked-mode
dotnet restore src/DiscordRelay.Settings/DiscordRelay.Settings.csproj --runtime win-x64 --locked-mode
./scripts/publish.ps1 -Version 1.0.1
$env:WIX_ACCEPT_EULA = 'true'
./scripts/build-msi.ps1 -Version 1.0.1
```

WiX 7 requires acceptance of its OSMF EULA. See [docs/releasing.md](docs/releasing.md).

## License

[MIT](LICENSE). See [SECURITY.md](SECURITY.md), [TRADEMARKS.md](TRADEMARKS.md), and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
