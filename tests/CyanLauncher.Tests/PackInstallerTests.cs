using System.Security.Cryptography;
using System.Text;
using CyanLauncher.Core;
using CyanLauncher.Core.Pack;

namespace CyanLauncher.Tests;

public sealed class PackInstallerTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "cyan-tests-" + Guid.NewGuid().ToString("N"));
    string Src => Path.Combine(_root, "src");
    string Game => Path.Combine(_root, "game", ".minecraft");
    PackInstaller Installer => new(Path.Combine(_root, "cache"), Path.Combine(_root, "retired"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    PackFile Blob(string path, string content, PackFileKind kind, bool clientSide = false, string? modId = null, List<string>? depends = null)
    {
        Directory.CreateDirectory(Src);
        var bytes = Encoding.UTF8.GetBytes(content);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var file = Path.Combine(Src, sha);
        File.WriteAllBytes(file, bytes);
        return new PackFile { Path = path, Url = new Uri(file).AbsoluteUri, Sha256 = sha, Size = bytes.Length, Kind = kind, ClientSide = clientSide, ModId = modId, Depends = depends };
    }

    static PackManifest Pack(string version, params PackFile[] files) => new() { PackVersion = version, Files = files.ToList() };

    string Read(string rel) => File.ReadAllText(Path.Combine(Game, rel.Replace('/', '\\')));
    bool Exists(string rel) => File.Exists(Path.Combine(Game, rel.Replace('/', '\\')));

    [Fact]
    public async Task FreshInstall_WritesModsPacksAndDefaults()
    {
        var pack = Pack("1.0.0",
            Blob("mods/a.jar", "A1", PackFileKind.Mod),
            Blob("resourcepacks/r.zip", "R", PackFileKind.ResourcePack),
            Blob("options.txt", "defaults", PackFileKind.Default));
        var r = await Installer.InstallAsync(pack, Game, false);
        Assert.Equal("A1", Read("mods/a.jar"));
        Assert.Equal("R", Read("resourcepacks/r.zip"));
        Assert.Equal("defaults", Read("options.txt"));
        Assert.Single(r.DefaultsWritten);
        Assert.True(PackInstaller.IsUpToDate(pack, Game));
    }

    [Fact]
    public async Task Update_KeepsPlayerSettings_UnlessReset()
    {
        var opts = Blob("options.txt", "cyan", PackFileKind.Default);
        await Installer.InstallAsync(Pack("1", opts), Game, false);
        File.WriteAllText(Path.Combine(Game, "options.txt"), "mine");

        await Installer.InstallAsync(Pack("2", opts), Game, false);
        Assert.Equal("mine", Read("options.txt"));

        await Installer.InstallAsync(Pack("2", opts), Game, resetDefaults: true);
        Assert.Equal("cyan", Read("options.txt"));
    }

    [Fact]
    public async Task Update_AddsNewDefaultsOnlyWhereMissing()
    {
        await Installer.InstallAsync(Pack("1", Blob("config/a.json", "a", PackFileKind.Default)), Game, false);
        File.WriteAllText(Path.Combine(Game, "config", "a.json"), "edited");
        await Installer.InstallAsync(Pack("2", Blob("config/a.json", "a2", PackFileKind.Default), Blob("config/b.json", "b", PackFileKind.Default)), Game, false);
        Assert.Equal("edited", Read("config/a.json"));
        Assert.Equal("b", Read("config/b.json"));
    }

    [Fact]
    public async Task Update_ReplacesChangedMods_AndRetiresRemovedOnes()
    {
        await Installer.InstallAsync(Pack("1", Blob("mods/a-1.jar", "A1", PackFileKind.Mod), Blob("mods/gone.jar", "G", PackFileKind.Mod)), Game, false);
        File.WriteAllText(Path.Combine(Game, "mods", "players-own.jar"), "mine");

        var r = await Installer.InstallAsync(Pack("2", Blob("mods/a-2.jar", "A2", PackFileKind.Mod)), Game, false);

        Assert.Equal("A2", Read("mods/a-2.jar"));
        Assert.False(Exists("mods/a-1.jar"));
        Assert.False(Exists("mods/gone.jar"));
        Assert.Contains("mods/gone.jar", r.Retired);
        Assert.True(Directory.EnumerateFiles(Path.Combine(_root, "retired"), "gone.jar", SearchOption.AllDirectories).Any());
        Assert.Equal("mine", Read("mods/players-own.jar")); // never touches files it didn't install
    }

    [Fact]
    public async Task Repair_RestoresTamperedFile()
    {
        var pack = Pack("1", Blob("mods/a.jar", "A1", PackFileKind.Mod));
        await Installer.InstallAsync(pack, Game, false);
        File.WriteAllText(Path.Combine(Game, "mods", "a.jar"), "corrupt");
        Assert.False(PackInstaller.IsUpToDate(pack, Game));
        await Installer.InstallAsync(pack, Game, false);
        Assert.Equal("A1", Read("mods/a.jar"));
    }

    [Fact]
    public async Task BadDownload_IsRejected_AndNothingInstalled()
    {
        var f = Blob("mods/a.jar", "A1", PackFileKind.Mod);
        f.Sha256 = new string('0', 64);
        await Assert.ThrowsAsync<InvalidDataException>(() => Installer.InstallAsync(Pack("1", f), Game, false));
        Assert.False(Exists("mods/a.jar"));
    }

    [Theory]
    [InlineData("../evil.jar")]
    [InlineData("mods/../../evil.jar")]
    [InlineData("C:/evil.jar")]
    [InlineData("/evil.jar")]
    [InlineData("mods\\evil.jar")]
    public void UnsafePaths_AreRejected(string path)
    {
        var pack = Pack("1", new PackFile { Path = path, Url = "https://x/y", Sha256 = new string('a', 64), Kind = PackFileKind.Mod });
        Assert.Throws<InvalidDataException>(pack.Validate);
    }

    [Theory]
    [InlineData("saves/New World/level.dat")]
    [InlineData("accounts.json")]
    [InlineData("xaero/minimap/waypoints.txt")]
    [InlineData("schematics/house.litematic")]
    [InlineData("config/voicechat/player-volumes.properties")]
    [InlineData("config/lyricsplus/cache/song.lrc")]
    public void PersonalData_IsRefused(string path)
    {
        var pack = Pack("1", new PackFile { Path = path, Url = "https://x/y", Sha256 = new string('a', 64), Kind = PackFileKind.Default });
        Assert.Throws<InvalidDataException>(pack.Validate);
    }

    [Fact]
    public async Task PlainHttpDownloads_AreRefused()
    {
        var f = Blob("mods/a.jar", "A1", PackFileKind.Mod);
        f.Url = "http://example.com/a.jar";
        await Assert.ThrowsAsync<InvalidDataException>(() => Installer.InstallAsync(Pack("1", f), Game, false));
    }

    [Fact]
    public void ArgFileQuoting_KeepsWindowsPaths()
    {
        // Java argfiles treat '\' as an escape inside quotes; "C:/x" would be parsed as a URL by log4j.
        Assert.Equal("\"-Dlog4j.configurationFile=C:\\\\a b\\\\c.xml\"",
            CyanLauncher.Core.Game.GameLauncher.Quote("-Dlog4j.configurationFile=C:\\a b\\c.xml"));
    }

    [Fact]
    public void Versions_CompareNumerically()
    {
        Assert.True(AppInfo.CompareVersions("0.1.10", "0.1.9") > 0);
        Assert.True(AppInfo.CompareVersions("v1.0.0", "1.0") == 0);
        Assert.True(AppInfo.CompareVersions("1.0.0", "1.0.1") < 0);
    }

    // ───────── Mods tab ─────────

    [Fact]
    public async Task RequiredMods_CannotBeTurnedOff()
    {
        var pack = Pack("1", Blob("mods/core.jar", "C", PackFileKind.Mod));
        await Installer.InstallAsync(pack, Game, false);
        Assert.Throws<ModToggleException>(() => ModManager.SetEnabled(pack, Game, "mods/core.jar", false));
        Assert.True(Exists("mods/core.jar"));
    }

    [Fact]
    public async Task ClientMod_Toggle_RenamesJar_AndSurvivesUpdate()
    {
        var hud = Blob("mods/hud.jar", "H", PackFileKind.Mod, clientSide: true, modId: "hud");
        await Installer.InstallAsync(Pack("1", hud), Game, false);

        ModManager.SetEnabled(Pack("1", hud), Game, "mods/hud.jar", false);
        Assert.False(Exists("mods/hud.jar"));
        Assert.True(Exists("mods/hud.jar.disabled"));

        await Installer.InstallAsync(Pack("2", hud, Blob("mods/new.jar", "N", PackFileKind.Mod)), Game, false);
        Assert.False(Exists("mods/hud.jar"));
        Assert.True(Exists("mods/hud.jar.disabled"));
        Assert.True(PackInstaller.IsUpToDate(Pack("2", hud, Blob("mods/new.jar", "N", PackFileKind.Mod)), Game));

        ModManager.SetEnabled(Pack("2", hud), Game, "mods/hud.jar", true);
        Assert.True(Exists("mods/hud.jar"));
        Assert.False(Exists("mods/hud.jar.disabled"));
    }

    [Fact]
    public async Task ClientMod_UpdatedVersion_StaysDisabled()
    {
        var v1 = Blob("mods/hud.jar", "H1", PackFileKind.Mod, clientSide: true, modId: "hud");
        await Installer.InstallAsync(Pack("1", v1), Game, false);
        ModManager.SetEnabled(Pack("1", v1), Game, "mods/hud.jar", false);

        var v2 = Blob("mods/hud.jar", "H2", PackFileKind.Mod, clientSide: true, modId: "hud");
        await Installer.InstallAsync(Pack("2", v2), Game, false);
        Assert.False(Exists("mods/hud.jar"));
        Assert.Equal("H2", Read("mods/hud.jar.disabled"));
    }

    [Fact]
    public async Task UnmarkingClientMod_ReEnablesIt()
    {
        var hud = Blob("mods/hud.jar", "H", PackFileKind.Mod, clientSide: true);
        await Installer.InstallAsync(Pack("1", hud), Game, false);
        ModManager.SetEnabled(Pack("1", hud), Game, "mods/hud.jar", false);

        var required = Blob("mods/hud.jar", "H", PackFileKind.Mod, clientSide: false);
        await Installer.InstallAsync(Pack("2", required), Game, false);
        Assert.True(Exists("mods/hud.jar"));
        Assert.False(Exists("mods/hud.jar.disabled"));
    }

    [Fact]
    public async Task DependencyGuard_BlocksDisablingNeededLibrary()
    {
        var lib = Blob("mods/malilib.jar", "L", PackFileKind.Mod, clientSide: true, modId: "malilib");
        var lite = Blob("mods/litematica.jar", "T", PackFileKind.Mod, clientSide: true, modId: "litematica", depends: new() { "malilib" });
        var pack = Pack("1", lib, lite);
        await Installer.InstallAsync(pack, Game, false);

        var e = Assert.Throws<ModToggleException>(() => ModManager.SetEnabled(pack, Game, "mods/malilib.jar", false));
        Assert.Contains("needed by", e.Message);

        ModManager.SetEnabled(pack, Game, "mods/litematica.jar", false);
        ModManager.SetEnabled(pack, Game, "mods/malilib.jar", false);
        Assert.Throws<ModToggleException>(() => ModManager.SetEnabled(pack, Game, "mods/litematica.jar", true));
    }

    [Fact]
    public async Task DefaultDisabledClientMod_StartsOff_OnlyOnce()
    {
        var shader = Blob("mods/shiny.jar", "S", PackFileKind.Mod, clientSide: true);
        shader.DefaultEnabled = false;
        await Installer.InstallAsync(Pack("1", shader), Game, false);
        Assert.True(Exists("mods/shiny.jar.disabled"));

        ModManager.SetEnabled(Pack("1", shader), Game, "mods/shiny.jar", true);
        await Installer.InstallAsync(Pack("2", shader), Game, false);
        Assert.True(Exists("mods/shiny.jar")); // player's choice wins after the first install
    }

    [Fact]
    public void Manifest_RoundTripsJson()
    {
        var pack = Pack("1.2.3", Blob("mods/a.jar", "A", PackFileKind.Mod, clientSide: true, modId: "a"));
        pack.Launcher = new LauncherRelease { Version = "0.2.0", Url = "https://x/y", Sha256 = new string('b', 64) };
        var back = PackManifest.Parse(pack.ToJson());
        Assert.Equal("1.2.3", back.PackVersion);
        Assert.True(back.Files[0].ClientSide);
        Assert.Equal(PackFileKind.Mod, back.Files[0].Kind);
        Assert.Contains("\"kind\": \"mod\"", pack.ToJson());
    }
}
