# Security policy

## Supported versions

Only the latest published release is supported with security fixes.

## Reporting a vulnerability

Do not open a public issue for a vulnerability that could expose notification text, Discord account data, local file paths, or arbitrary code execution. Use GitHub's private vulnerability reporting feature after the repository is published.

## Security boundaries

The relay does not use a Discord user token or automate a Discord account. It reads Windows notification metadata locally. Sender-avatar recovery reads the local Windows notification database using a read-only SQLite connection and accepts image files only from the current user's temporary directory after path, extension, reparse-point, and size validation. Notification text is not written to logs.
