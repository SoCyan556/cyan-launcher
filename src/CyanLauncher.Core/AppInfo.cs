using System.Reflection;

namespace CyanLauncher.Core;

public static class AppInfo
{
    public static string Version { get; } = ReadVersion();

    static string ReadVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(info)) return asm.GetName().Version?.ToString(3) ?? "0.0.0";
        var plus = info.IndexOf('+');
        return plus >= 0 ? info[..plus] : info;
    }

    /// <summary>Compares dotted versions ("1.2.10" &gt; "1.2.9"). Non-numeric suffixes are ignored.</summary>
    public static int CompareVersions(string a, string b)
    {
        static Version P(string s)
        {
            var core = new string(s.TrimStart('v', 'V').TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
            var parts = core.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            while (parts.Count < 3) parts.Add(0);
            return new Version(parts[0], parts[1], parts[2], parts.Count > 3 ? parts[3] : 0);
        }
        return P(a).CompareTo(P(b));
    }
}
