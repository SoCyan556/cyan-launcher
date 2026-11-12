using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using CyanLauncher.Core.Auth;

namespace CyanLauncher.Core.Game;

public sealed record LaunchOptions(string JavaPath, string GameDir, int MemoryMb, string ArgFileDir, string LogFile);

/// <summary>Builds the JVM/game command line from the Mojang + Fabric version data and starts Minecraft.</summary>
public static class GameLauncher
{
    public static List<string> BuildJvmArgs(ResolvedGame game, LaunchOptions o)
    {
        var vars = Variables(game, o, null);
        var args = new List<string> { $"-Xmx{o.MemoryMb}M", $"-Xms{Math.Min(1024, o.MemoryMb)}M" };
        args.AddRange(ExpandArgs(game.VanillaJson["arguments"]?["jvm"] as JsonArray, vars));
        args.AddRange(ExpandArgs(game.FabricJson["arguments"]?["jvm"] as JsonArray, vars));
        if (game.LoggingArgument != null) args.Add(game.LoggingArgument);
        return args;
    }

    public static List<string> BuildGameArgs(ResolvedGame game, LaunchOptions o, GameSession s)
    {
        var vars = Variables(game, o, s);
        var args = ExpandArgs(game.VanillaJson["arguments"]?["game"] as JsonArray, vars);
        args.AddRange(ExpandArgs(game.FabricJson["arguments"]?["game"] as JsonArray, vars));
        return args;
    }

    public static Process Launch(ResolvedGame game, LaunchOptions o, GameSession session)
    {
        Directory.CreateDirectory(o.GameDir);
        Directory.CreateDirectory(o.ArgFileDir);
        Directory.CreateDirectory(Path.GetDirectoryName(o.LogFile)!);

        // JVM args + classpath go in a Java @argfile to stay under the Windows command-line limit.
        // It contains no secrets: the access token is passed only on the process command line.
        var argFile = Path.Combine(o.ArgFileDir, "jvm-args.txt");
        var sb = new StringBuilder();
        foreach (var a in BuildJvmArgs(game, o)) sb.Append(Quote(a)).Append('\n');
        sb.Append(Quote(game.MainClass)).Append('\n');
        File.WriteAllText(argFile, sb.ToString(), new UTF8Encoding(false));

        var psi = new ProcessStartInfo(o.JavaPath)
        {
            WorkingDirectory = o.GameDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("@" + argFile);
        foreach (var a in BuildGameArgs(game, o, session)) psi.ArgumentList.Add(a);

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var log = new StreamWriter(o.LogFile, false, new UTF8Encoding(false)) { AutoFlush = true };
        var token = session.AccessToken;
        void Write(string? line)
        {
            if (line is null) return;
            lock (log) log.WriteLine(token.Length > 8 ? line.Replace(token, "<token>") : line);
        }
        proc.OutputDataReceived += (_, e) => Write(e.Data);
        proc.ErrorDataReceived += (_, e) => Write(e.Data);
        proc.Exited += (_, _) => { lock (log) log.Dispose(); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        return proc;
    }

    static Dictionary<string, string> Variables(ResolvedGame g, LaunchOptions o, GameSession? s) => new()
    {
        ["natives_directory"] = Fwd(g.NativesDir),
        ["launcher_name"] = "CyanLauncher",
        ["launcher_version"] = AppInfo.Version,
        ["classpath"] = string.Join(";", g.Classpath.Select(Fwd)),
        ["classpath_separator"] = ";",
        ["library_directory"] = Fwd(g.LibrariesDir),
        ["version_name"] = g.VersionId,
        ["game_directory"] = o.GameDir,
        ["assets_root"] = g.AssetsRoot,
        ["game_assets"] = g.AssetsRoot,
        ["assets_index_name"] = g.AssetIndexId,
        ["auth_player_name"] = s?.Username ?? "",
        ["auth_uuid"] = s?.Uuid ?? "",
        ["auth_access_token"] = s?.AccessToken ?? "",
        ["auth_session"] = s?.AccessToken ?? "",
        ["auth_xuid"] = s?.Xuid ?? "",
        ["clientid"] = "",
        ["user_type"] = s?.UserType ?? "msa",
        ["user_properties"] = "{}",
        ["version_type"] = "release",
    };

    static List<string> ExpandArgs(JsonArray? arr, Dictionary<string, string> vars)
    {
        var result = new List<string>();
        if (arr is null) return result;
        foreach (var item in arr)
        {
            if (item is JsonValue v) result.Add(Expand((string)v!, vars));
            else if (item is JsonObject obj && Rules.Allowed(obj["rules"] as JsonArray))
            {
                var value = obj["value"];
                if (value is JsonArray many) result.AddRange(many.Select(x => Expand((string)x!, vars)));
                else if (value is not null) result.Add(Expand((string)value!, vars));
            }
        }
        return result;
    }

    static string Expand(string s, Dictionary<string, string> vars)
    {
        foreach (var kv in vars) s = s.Replace("${" + kv.Key + "}", kv.Value);
        return s;
    }

    static string Fwd(string p) => p.Replace('\\', '/');

    /// <summary>
    /// Java argfile quoting: inside quotes a backslash is an escape character, so backslashes are doubled
    /// (not turned into '/', which makes log4j read "C:/..." as a URL with protocol "c").
    /// </summary>
    internal static string Quote(string a) => "\"" + a.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
