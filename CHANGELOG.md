# Changelog

All notable changes will be documented in this file. Versioning follows Semantic Versioning.

## [1.0.2] - 2026-09-20

### Fixed

- Read new Discord notification payloads directly from the local Windows notification database.
- Retained `UserNotificationListener` for access authorization and database-read fallback.
- Reduced measured source-to-PDB submission latency from approximately 300 ms to tens of milliseconds.

## [1.0.1] - 2026-09-20

### Fixed

- Removed obsolete relay startup entries during upgrades.
- Blocked legacy workers with shared mutex names.
- Reduced source processing to one metadata query.
- Removed the event fallback delay.
- Replaced the current PDB notification instead of stacking entries.
- Added measured relay latency to status.

## [1.0.0] - 2026-09-18

### Added

- Windows notification listener for Discord Stable and Canary.
- Message-content-free replacement banners with sender avatars.
- Persistent or transient Notification Center behavior.
- Best-effort Discord protocol activation.
- Default, Instant Message, Mail, Reminder, SMS, Silent, and Custom WAV sounds.
- WinUI 3 settings application.
- Reproducible self-contained x64 publishing and WiX MSI source.
