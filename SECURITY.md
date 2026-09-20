# Security policy

## Supported versions

Only the latest published release is supported with security fixes.

## Reporting a vulnerability

Do not open a public issue for a vulnerability that could expose notification text, Discord account data, local file paths, or arbitrary code execution. Use GitHub's private vulnerability reporting feature after the repository is published.

## Security boundaries

The relay does not use a Discord user token or automate a Discord account. It reads new Discord notification metadata directly from the local Windows notification database using a read-only SQLite connection, uses only the title and routing metadata, and does not write notification text to logs. Sender-avatar recovery accepts image files only from the current user's temporary directory after path, extension, reparse-point, and size validation.
