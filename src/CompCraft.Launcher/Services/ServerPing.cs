using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CompCraft.Launcher.Services;

public sealed record ServerStatus(string Id, bool Online, int? Players, int? MaxPlayers, long? Latency, string? Version, string? Motd, string? Favicon);

/// <summary>Minecraft Server List Ping (the same request the in-game multiplayer screen sends).</summary>
public static class ServerPing
{
    public static async Task<ServerStatus> PingAsync(ServerEntry server, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(server.Address, server.Port, timeout.Token);
            await using var stream = client.GetStream();

            // Handshake (next state = status), then status request.
            var handshake = new MemoryStream();
            WriteVarInt(handshake, 0x00);
            WriteVarInt(handshake, -1);
            WriteString(handshake, server.Address);
            handshake.Write([(byte)(server.Port >> 8), (byte)server.Port]);
            WriteVarInt(handshake, 1);
            await SendAsync(stream, handshake.ToArray(), timeout.Token);
            await SendAsync(stream, [0x00], timeout.Token);

            await ReadVarIntAsync(stream, timeout.Token); // packet length
            await ReadVarIntAsync(stream, timeout.Token); // packet id
            var length = await ReadVarIntAsync(stream, timeout.Token);
            if (length is < 0 or > 1 << 20) throw new InvalidDataException();
            var json = Encoding.UTF8.GetString(await ReadExactAsync(stream, length, timeout.Token));

            var watch = Stopwatch.StartNew();
            var ping = new MemoryStream();
            WriteVarInt(ping, 0x01);
            ping.Write(BitConverter.GetBytes(Environment.TickCount64));
            await SendAsync(stream, ping.ToArray(), timeout.Token);
            await ReadVarIntAsync(stream, timeout.Token);
            await ReadExactAsync(stream, 9, timeout.Token);
            var latency = watch.ElapsedMilliseconds;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            int? online = null, max = null;
            if (root.TryGetProperty("players", out var players))
            {
                online = players.TryGetProperty("online", out var o) ? o.GetInt32() : null;
                max = players.TryGetProperty("max", out var m) ? m.GetInt32() : null;
            }
            var version = root.TryGetProperty("version", out var v) && v.TryGetProperty("name", out var vn) ? vn.GetString() : null;
            var motd = root.TryGetProperty("description", out var d) ? Flatten(d).Trim() : null;
            var favicon = root.TryGetProperty("favicon", out var f) && f.GetString() is { } icon && icon.StartsWith("data:image/png;base64,") ? icon : null;
            return new(server.Id, true, online, max, latency, version, motd, favicon);
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or JsonException or InvalidDataException or InvalidOperationException or FormatException)
        {
            ct.ThrowIfCancellationRequested();
            return new(server.Id, false, null, null, null, null, null, null);
        }
    }

    /// <summary>Plain text of a chat component, without § formatting codes.</summary>
    private static string Flatten(JsonElement e)
    {
        var sb = new StringBuilder();
        void Walk(JsonElement x)
        {
            if (x.ValueKind == JsonValueKind.String) sb.Append(x.GetString());
            else if (x.ValueKind == JsonValueKind.Array) foreach (var c in x.EnumerateArray()) Walk(c);
            else if (x.ValueKind == JsonValueKind.Object)
            {
                if (x.TryGetProperty("text", out var t)) Walk(t);
                if (x.TryGetProperty("extra", out var extra)) Walk(extra);
            }
        }
        Walk(e);
        var text = sb.ToString();
        var clean = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++) { if (text[i] == '§') { i++; continue; } clean.Append(text[i]); }
        return clean.ToString();
    }

    private static async Task SendAsync(Stream stream, byte[] packet, CancellationToken ct)
    {
        var framed = new MemoryStream();
        WriteVarInt(framed, packet.Length);
        framed.Write(packet);
        await stream.WriteAsync(framed.ToArray(), ct);
    }

    private static void WriteVarInt(Stream s, int value)
    {
        var v = (uint)value;
        do { var b = (byte)(v & 0x7F); v >>= 7; if (v != 0) b |= 0x80; s.WriteByte(b); } while (v != 0);
    }

    private static void WriteString(Stream s, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarInt(s, bytes.Length);
        s.Write(bytes);
    }

    private static async Task<int> ReadVarIntAsync(Stream s, CancellationToken ct)
    {
        int result = 0, shift = 0;
        var one = new byte[1];
        while (true)
        {
            await s.ReadExactlyAsync(one, ct);
            result |= (one[0] & 0x7F) << shift;
            if ((one[0] & 0x80) == 0) return result;
            shift += 7;
            if (shift > 35) throw new InvalidDataException();
        }
    }

    private static async Task<byte[]> ReadExactAsync(Stream s, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        await s.ReadExactlyAsync(buffer, ct);
        return buffer;
    }
}
