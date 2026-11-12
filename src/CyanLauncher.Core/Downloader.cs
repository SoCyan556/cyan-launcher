using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace CyanLauncher.Core;

/// <summary>HTTPS (or local file, for testing) downloads with mandatory hash verification.</summary>
public sealed class Downloader
{
    public static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var c = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromMinutes(10),
        };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CyanLauncher", AppInfo.Version));
        return c;
    }

    public static async Task<string> GetStringAsync(string url, CancellationToken ct = default)
    {
        if (TryLocalPath(url, out var local)) return await File.ReadAllTextAsync(local, ct);
        RequireHttps(url);
        return await Http.GetStringAsync(url, ct);
    }

    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="destination"/> unless a file with the expected
    /// hash is already there. The file is written to a temp name, verified, then moved into place,
    /// so a failed or tampered download never replaces anything.
    /// </summary>
    public static async Task<bool> DownloadVerifiedAsync(string url, string destination, string expectedHash,
        HashAlgorithmName alg, CancellationToken ct = default)
    {
        if (Hashing.Matches(destination, expectedHash, alg)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + "." + Guid.NewGuid().ToString("N")[..8] + ".part";
        try
        {
            if (TryLocalPath(url, out var local))
            {
                File.Copy(local, temp, true);
            }
            else
            {
                RequireHttps(url);
                using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(temp);
                await src.CopyToAsync(dst, ct);
            }
            if (!Hashing.Matches(temp, expectedHash, alg))
                throw new InvalidDataException($"Checksum mismatch for {Path.GetFileName(destination)} from {url}. Nothing was installed.");
            File.Move(temp, destination, true);
            return true;
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    static void RequireHttps(string url)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Only HTTPS downloads are allowed: {url}");
    }

    /// <summary>file:// URLs and plain local paths are accepted so packs can be tested before release.</summary>
    static bool TryLocalPath(string url, out string path)
    {
        if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(url, UriKind.Absolute, out var u))
        {
            path = u.LocalPath;
            return true;
        }
        if (Path.IsPathFullyQualified(url))
        {
            path = url;
            return true;
        }
        path = "";
        return false;
    }

    /// <summary>Runs downloads with limited parallelism.</summary>
    public static async Task ForEachParallelAsync<T>(IEnumerable<T> items, int parallelism, Func<T, Task> body, CancellationToken ct)
    {
        await Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = ct },
            async (item, _) => await body(item));
    }
}
