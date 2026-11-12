# Cyan Civilization Launcher

A standalone Windows launcher for the Cyan Civilization modpack (Minecraft 1.21.1 + Fabric).
It installs the pack, keeps it updated, lets players turn client-side mods on and off, and starts the game.
It does not use Prism Launcher.

```
src/CyanLauncher.Core   Pack format, installer/updater, Mods logic, Minecraft/Fabric/Java download, sign-in, launch
src/CyanLauncher        The WPF app (Play / Mods / Settings tabs)
tools/CyanPack          Builds pack.json for a release and test-installs it
tests/                  Unit tests (installer, settings preservation, mod toggles, path safety)
pack-overrides.json     Your hand-edited marks: which mods are client-side, display names
```

## For players

1. Download **CyanLauncher-Setup.exe** and double-click it. That's the whole install: no admin rights, no wizard.
   It installs to `%LOCALAPPDATA%\Programs\CyanCivilizationLauncher`, adds **Cyan Civilization Launcher** (with icon)
   to the Start menu (so it shows up in Windows search) and the desktop, registers it in Settings â†’ Apps
   (with Uninstall), and opens it.
2. Sign in with Microsoft (top right).
3. Press **Install Cyan Civilization**. One click downloads the mods, resource packs, shaders, Cyan settings,
   Minecraft, Fabric and Java. Then the button becomes **Play** (or **Update & Play** when a new pack version is out).

The setup exe and the app are the same file: run from anywhere other than its install folder, it installs itself.
Running a newer setup over an existing install upgrades it in place. `--portable` runs it without installing.

## What the launcher does

- **Install/update**: on start it fetches `pack.json` from the latest GitHub release. Mods, resource packs and
  shaderpacks are kept exactly in sync with it. Every file is SHA-256 checked, and everything is downloaded
  before anything installed is touched. Mods removed from the pack are moved to `%LOCALAPPDATA%\CyanLauncher\retired`,
  never deleted.
- **Settings**: Cyan defaults (`options.txt`, `config/...`) are only written where a file is missing, so updates
  never overwrite a player's settings or keybinds. **Settings â†’ Reset to Cyan defaults** overwrites them on request.
- **Never shipped or touched**: accounts, saves, waypoints, explored map data, schematics, lyric caches and
  per-player voice settings. `pack.json` files containing those paths are rejected (`ExcludedPaths`).
- **Mods tab**: lists every mod. Mods marked `clientSide` can be toggled (renamed to `.jar.disabled`). A mod can't be
  turned off while an enabled mod depends on it. Choices survive pack updates.
- **Play**: downloads Minecraft, libraries and assets from Mojang, the Fabric loader from fabricmc.net and
  Java 21 (Eclipse Temurin) from Adoptium into `%LOCALAPPDATA%\CyanLauncher`, then starts the game.
- **Self-update**: if `pack.json` names a newer `launcher.version`, the new exe is downloaded, verified, swapped
  in, and the launcher restarts.

Data lives in `%LOCALAPPDATA%\CyanLauncher` (game folder: `instance\.minecraft`, changeable in Settings).

## Marking client-side mods

Nothing is marked yet. When you decide, list mod ids (or `mods/<file>.jar` paths) in `pack-overrides.json`:

```json
{
  "clientSide": ["inventoryhud", "fpsdisplay", "particlerain"],
  "defaultDisabled": ["fpsdisplay"],
  "names": { "fwa": "First-person Weapon Animations" }
}
```

`defaultDisabled` client mods start off on a fresh install (players can turn them on). `CyanPack build` prints,
for reference, which mods declare themselves client-only in their own metadata, but it never marks anything automatically.

## Releasing an update

Releases are hosted on GitHub (`ReleaseChannel.GitHubRepo` in `src/CyanLauncher.Core/LauncherSettings.cs`; change it
before the first release). Files are content-addressed: each file is uploaded once to a release tagged `files`
as `<sha256>`, and every pack release just ships a new `pack.json`.

1. Prepare a reference `.minecraft` with the exact mods/resourcepacks/shaderpacks, and a `defaults/` folder of settings.
2. (Launcher change only) bump `<Version>` in `src/CyanLauncher/CyanLauncher.csproj`, then:
   `dotnet publish src/CyanLauncher -c Release -o publish`
3. Build the pack (add `--launcher-exe publish/CyanLauncher.exe --launcher-version X.Y.Z` when shipping a launcher):
   ```
   dotnet run --project tools/CyanPack -- build --source <.minecraft> --defaults <defaults> --pack-version 1.0.1 --overrides pack-overrides.json --out out --modrinth
   ```
   `--modrinth` points mods that are published on Modrinth at Modrinth's CDN instead of re-hosting them.
4. Test it: `dotnet run --project tools/CyanPack -- try --pack out/pack.json --data <scratch> --launch`
   (blobs must be reachable; build with `--blob-base-url file:///â€¦/out/blobs/` to test before uploading).
5. Upload new blobs (listed in `out/blobs-to-upload.txt`) to the `files` release, then publish a release with `pack.json`:
   ```
   gh release upload files out/blobs/<sha256> ... --repo SoCyan556/cyan-launcher
   gh release create v1.0.1 out/pack.json --repo SoCyan556/cyan-launcher --title "Pack 1.0.1"
   ```
   The launcher reads `releases/latest/download/pack.json`, so publishing the release is what ships the update.

Every pack content change needs a new `--pack-version`.

## Microsoft sign-in setup (required before players can sign in)

A standalone launcher needs its own Azure app registration:

1. Azure portal â†’ Microsoft Entra ID â†’ App registrations â†’ New registration. Supported accounts:
   *Personal Microsoft accounts only*. Under Authentication, enable **Allow public client flows**.
2. Apply for Minecraft API access for that client ID (Mojang/Microsoft review: <https://aka.ms/mce-reviewappid>).
   Until approved, Minecraft login fails with 403 even for valid accounts.
3. Put the client ID in `ReleaseChannel.MicrosoftClientId` (or Settings â†’ Microsoft sign-in client ID).

The refresh token is stored encrypted with Windows DPAPI in `account.dat`; the Minecraft access token is only kept in memory
and is masked in the game log. Until sign-in is approved, **Settings â†’ Offline name** allows singleplayer testing.

## Build & test

```
dotnet build CyanLauncher.slnx
dotnet test tests/CyanLauncher.Tests
dotnet publish src/CyanLauncher -c Release -o publish     # single self-contained CyanLauncher.exe
copy publish\CyanLauncher.exe dist\CyanLauncher-Setup.exe  # the file you hand to players
```

Set `CYANLAUNCHER_HOME=<folder>` to point all launcher data at a test folder instead of `%LOCALAPPDATA%\CyanLauncher`.
For the first public release, also attach `CyanLauncher-Setup.exe` to the GitHub release so players have a download link.
