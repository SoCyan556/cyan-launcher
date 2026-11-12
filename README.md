# Comp Craft Launcher

The **Comp Craft** launcher installs, updates and plays the **Cyan Civilization** modpack (Minecraft 1.21.1 + Fabric).
It is a .NET 8 WPF window hosting a Vue 3 UI (WebView2), with [CmlLib.Core](https://github.com/CmlLib/CmlLib.Core)
for Minecraft/Java and [CmlLib.Core.Auth.Microsoft](https://github.com/CmlLib/CmlLib.Core.Auth.Microsoft) for
Microsoft sign-in. The landscape, logo and card art are drawn procedurally (canvas/SVG).

```
src/CompCraft.Launcher   WPF host, services (account, game, modpack, setup, self-update)
frontend/                Vue UI (Home / Modpack / Servers / Settings)
config/                  launcher.json (name, tagline) and servers.json, compiled into the exe
src/CyanLauncher.Core    Pack format, installer/updater, client-mod toggles, self-update
tools/CyanPack           Builds pack.json for a release and test-installs it
tests/                   Unit tests for the pack installer and Mods logic
pack-overrides.json      Which mods are client-side (optional), display names
```

## For players

1. Download **CompCraft-Setup.exe** from the latest release and double-click it. That's the whole install: no admin
   rights, no wizard. It installs to `%LOCALAPPDATA%\Programs\CompCraft`, adds **Comp Craft** (with its icon) to the
   Start menu, so Windows search finds it, and to the desktop, registers it in Settings → Apps (with Uninstall), and opens it.
2. Press **Install modpack**. One click downloads everything: Fabric, Minecraft, Java, all mods, resource packs,
   shaders and the Cyan default settings.
3. Sign in with Microsoft, then press **Play** (or **Update & Play** when a new pack version is out).

Game data lives in `%APPDATA%\.compcraft`; launcher data (settings, saved account, UI cache) in `%APPDATA%\.compcraft\launcher`.

## What the launcher does

- **Updates**: on start it reads `pack.json` from the latest GitHub release. Mods, resource packs and shaderpacks are
  kept exactly in sync with it. Every file is SHA-256 checked, and everything is downloaded before anything installed
  is touched. Files removed from the pack go to `launcher\retired`, never deleted.
- **Settings**: the pack's default settings (`options.txt`, `config/...`) are only written where a file is missing, so
  updates never overwrite a player's settings or keybinds. **Settings → Reset game settings to pack defaults** does on request.
- **Never shipped or touched**: accounts, saves, waypoints, explored map data, schematics, lyric caches and per-player
  voice settings (`ExcludedPaths`).
- **Modpack page**: every mod with its Modrinth icon and description. Mods marked client-side appear under
  *Optional client-side mods* with an on/off switch (the jar is renamed to `.jar.disabled`). A mod can't be turned off
  while an enabled mod needs it, and choices survive updates.
- **Self-update**: when `pack.json` names a newer `launcher.version`, the launcher downloads the new exe, verifies it,
  swaps it in and restarts.

## Marking client-side mods

None are marked yet. List mod ids (or `mods/<file>.jar` paths) in `pack-overrides.json`, then build and publish a release:

```json
{
  "clientSide": ["inventoryhud", "fpsdisplay", "particlerain"],
  "defaultDisabled": ["fpsdisplay"],
  "names": { "fwa": "First-person Weapon Animations" }
}
```

`defaultDisabled` mods start off on a fresh install. `CyanPack build` prints which mods declare themselves
client-only in their own metadata, for reference; it never marks anything by itself.

## Releasing an update

Files are content-addressed: each file is uploaded once to the `files` release (a pre-release) as `<sha256>`, and
each release ships a new `pack.json`. Mods and packs published on Modrinth download from Modrinth's CDN instead.

1. Prepare a reference `.minecraft` (mods/resourcepacks/shaderpacks) and a `defaults/` folder of settings.
2. Launcher change only: bump `<Version>` in `src/CompCraft.Launcher/CompCraft.Launcher.csproj`, then
   `dotnet publish src/CompCraft.Launcher -c Release -o publish-cc`.
3. Build the pack (add `--launcher-exe publish-cc/CompCraft.exe --launcher-version X.Y.Z` when shipping a launcher):
   ```
   dotnet run --project tools/CyanPack -- build --source <.minecraft> --defaults <defaults> --pack-version 1.0.1 --overrides pack-overrides.json --modrinth --out out
   ```
4. Test: `dotnet run --project tools/CyanPack -- try --pack out/pack.json --data <scratch folder>`.
5. Upload new blobs (`out/blobs-to-upload.txt`) and publish:
   ```
   gh release upload files out/blobs/<sha256> ... --repo SoCyan556/cyan-launcher
   gh release create v1.0.1 out/pack.json CompCraft-Setup.exe --repo SoCyan556/cyan-launcher --title "Cyan Civilization 1.0.1"
   ```
   Launchers read `releases/latest/download/pack.json`, so publishing the release is what ships the update.

Every pack content change needs a new `--pack-version`.

## Build and run

Requirements: Windows 10/11, .NET SDK 8+, Node.js 20+, WebView2 Runtime (preinstalled on Windows 11).

```
dotnet run --project src/CompCraft.Launcher          # dev build; never runs setup
dotnet test tests/CyanLauncher.Tests
dotnet publish src/CompCraft.Launcher -c Release -o publish-cc   # single self-contained CompCraft.exe
```

The build runs `npm ci` (first time) and `vite build`, zips the UI and compiles it into the exe.
`npm --prefix frontend run dev` previews the UI in a browser with fictional data.

Testing switches: `COMPCRAFT_HOME=<folder>` moves all game and launcher data; a `config\launcher.json` next to the exe
overrides the built-in one (e.g. `"manifestUrl"` pointing at a local `pack.json`); debug builds honor
`COMPCRAFT_DEVTOOLS_PORT=9333`. `--portable` runs a release exe without installing it.

Not affiliated with Mojang or Microsoft. Builds are unsigned, so Windows SmartScreen may warn on first run.
