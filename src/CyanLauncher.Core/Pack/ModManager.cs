namespace CyanLauncher.Core.Pack;

public sealed record ModEntry(
    string Path,
    string DisplayName,
    string? ModId,
    string? Version,
    bool ClientSide,
    bool Enabled,
    string? DeclaredEnvironment);

public sealed class ModToggleException(string message) : Exception(message);

/// <summary>
/// Backs the Mods tab. Only mods marked "clientSide": true in pack.json can be turned off.
/// A mod is turned off by renaming mods/x.jar to mods/x.jar.disabled, which Fabric ignores.
/// </summary>
public static class ModManager
{
    public static IReadOnlyList<ModEntry> List(PackManifest pack, string gameDir)
    {
        var state = InstanceState.Load(gameDir);
        return pack.Mods
            .Select(m => new ModEntry(
                m.Path,
                m.Name ?? System.IO.Path.GetFileNameWithoutExtension(m.Path),
                m.ModId,
                m.ModVersion,
                m.ClientSide,
                !state.DisabledMods.Contains(m.Path),
                m.DeclaredEnvironment))
            .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void SetEnabled(PackManifest pack, string gameDir, string path, bool enabled)
    {
        var mod = pack.Mods.FirstOrDefault(m => m.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
                  ?? throw new ModToggleException($"{path} is not part of this pack.");
        if (!mod.ClientSide)
            throw new ModToggleException($"{mod.Name ?? path} is required by the pack and can't be turned off.");

        var state = InstanceState.Load(gameDir);
        bool IsOn(PackFile m) => !state.DisabledMods.Contains(m.Path);

        if (!enabled)
        {
            if (mod.ModId != null)
            {
                var needers = pack.Mods.Where(m => m != mod && IsOn(m) && (m.Depends?.Contains(mod.ModId, StringComparer.OrdinalIgnoreCase) ?? false))
                    .Select(m => m.Name ?? m.Path).ToList();
                if (needers.Count > 0)
                    throw new ModToggleException($"{mod.Name ?? path} is needed by: {string.Join(", ", needers)}. Turn those off first.");
            }
        }
        else
        {
            var missing = (mod.Depends ?? new())
                .Select(id => pack.Mods.FirstOrDefault(m => string.Equals(m.ModId, id, StringComparison.OrdinalIgnoreCase)))
                .Where(m => m != null && !IsOn(m)).Select(m => m!.Name ?? m.Path).ToList();
            if (missing.Count > 0)
                throw new ModToggleException($"{mod.Name ?? path} needs: {string.Join(", ", missing)}. Turn those on first.");
        }

        var on = SafePath.Combine(gameDir, mod.Path);
        var off = SafePath.Combine(gameDir, PackInstaller.DisabledPath(mod.Path));
        if (enabled)
        {
            if (File.Exists(off)) File.Move(off, on, true);
            state.DisabledMods.Remove(mod.Path);
        }
        else
        {
            if (File.Exists(on)) File.Move(on, off, true);
            state.DisabledMods.Add(mod.Path);
        }
        state.SeenClientMods.Add(mod.Path);
        state.Save(gameDir);
    }
}
