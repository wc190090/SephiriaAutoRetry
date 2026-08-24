# Changelog

## 0.2.3 - 2026-08-25

- Move preserved player network objects to the saved floor entrance immediately before restart.
- Prevent stale death-room coordinates from revealing that room during floor regeneration.
- Prevent stale coordinates from prematurely triggering boss entrance dialogue on retry.
- Fall back to the original game-over flow if the active floor or its entrance spawn point cannot be validated.

## 0.2.2 - 2026-08-25

- Move installer backups outside the Sephiria save directory to prevent recursive self-copying.
- Reject any overlapping source and destination paths before a backup directory is created.
- Exclude the legacy `ModBackups` directory when backing up saves.
- Add installer self-tests for both the exclusion rule and recursive-path rejection.

## 0.2.1 - 2026-08-24

- Keep the installer UI responsive while saves and previous plugin files are backed up.
- Show backup source and destination before copying begins so long backups no longer look like a failed click.
- Run uninstall backup work outside the UI thread as well.

## 0.2.0 - 2026-08-24

- Added host-only multiplayer retry. Guests do not install the mod.
- Require the host's original `-allow_rejoin` launch option so multiplayer floor checkpoints are actually written.
- Validate active connections, original multiplayer list, saved player count, unique save slots, and player GUIDs before retrying.
- Intercept the server-side `RpcGameOver` before the original result broadcast and restore all players through the original restart flow.
- Fall back to the original result screen on disconnects, late joins, invalid checkpoints, incompatible state, or restart failures.
- Added release packaging, offline installer, checksums, and GitHub/Gitee mirror documentation.

## 0.1.0 - 2026-08-23

- Initial single-player automatic floor retry.
- Preserve and reload the TMP checkpoint while suppressing duplicate starting items and potions.
