# Changelog

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
