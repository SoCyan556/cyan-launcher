using System.Security.Cryptography;

namespace CyanLauncher.Core;

public static class Hashing
{
    public static bool IsSha256(string? s) => s is { Length: 64 } && s.All(Uri.IsHexDigit);

    public static string Sha256File(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    public static string Sha1File(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(fs)).ToLowerInvariant();
    }

    public static string Sha512File(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA512.HashData(fs)).ToLowerInvariant();
    }

    public static bool Matches(string path, string expected, HashAlgorithmName alg)
    {
        if (!File.Exists(path)) return false;
        var actual = alg == HashAlgorithmName.SHA1 ? Sha1File(path) : Sha256File(path);
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }
}
