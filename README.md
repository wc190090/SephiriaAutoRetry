# Sephiria Auto Retry

[中文说明](README.zh-CN.md)

A BepInEx 5 mod for *Sephiria* that retries the current floor from its entrance checkpoint after a real death.

Version 0.2.2 supports:

- Single-player runs.
- Host-only multiplayer: only the host installs the mod; guests use the unmodified game.
- Conservative fail-open behavior for victory, give-up, disconnects, late joins, missing saves, or incompatible player slots.

The multiplayer host must launch the game with `-allow_rejoin`. Add it in Steam under **Sephiria > Properties > General > Launch Options**. Guests do not need the mod or this launch option.

At game over, the host validates the active connections against the TMP checkpoint, prevents the original result screen from deleting that checkpoint, invokes the game's own restart flow, and restores every connected player from the saved floor entrance. No custom network message or client-side protocol is used.

## Install

Download one of the assets from the [latest GitHub release](https://github.com/wc190090/SephiriaAutoRetry/releases/latest) or the [Gitee mirror](https://gitee.com/wc190092/SephiriaAutoRetry/releases):

- Run the offline installer; or
- Extract the manual ZIP into the game's root directory.

Installer backups are stored at `Documents\Saved Games\SephiriaModBackups\SephiriaAutoRetry`, outside the game's save directory. The installer rejects overlapping backup paths and excludes the legacy `Sephiria\ModBackups` directory.

For manual installation, BepInEx 5 x64 must already be installed. The DLL belongs at:

```text
Sephiria/BepInEx/plugins/SephiriaAutoRetry/SephiriaAutoRetry.dll
```

## Compatibility

Built and statically verified against Sephiria 1.0.30, Steam build `24838233`, Windows x64 Mono. The compatible `Assembly-CSharp.dll` SHA-256 is:

```text
C3939A7D431F1362DACA655938480569E11F501F7D0820A989873765555A7C39
```

Runtime compatibility checks disable the mod safely when critical game methods or fields are missing. Multiplayer restoration has been code-verified but still benefits from real 2–4 player testing after game updates.

## Build

See [BUILDING.md](BUILDING.md). This repository does not redistribute game assemblies or BepInEx binaries.

## License

[MIT](LICENSE)
