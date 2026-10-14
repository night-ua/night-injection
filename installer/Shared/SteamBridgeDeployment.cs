using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Buffers.Binary;

namespace NightInjection.Installation;

public static class SteamBridgeDeployment
{
    public const string FileName = "space.darkdevil.steam-bridge.star";
    public const string ReceiptName = "steam-bridge-install.json";
    public const int MaximumPayloadBytes = 1024 * 1024;

    public static void ValidatePayload(ReadOnlySpan<byte> bytes)
    {
        // Starlight v2: u32 LE shim length, Lua shim, 12-byte STAR header,
        // then 256-byte section records. Section offsets are relative to STAR.
        if (bytes.Length < 16 || bytes.Length > MaximumPayloadBytes)
            throw new InvalidDataException("The Steam Bridge payload has an invalid size.");
        var shimLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (shimLength == 0 || shimLength > (uint)(bytes.Length - 16))
            throw new InvalidDataException("The Steam Bridge bootstrap length is invalid.");
        var headerOffset = 4 + (int)shimLength;
        var header = bytes.Slice(headerOffset, 12);
        if (!header[..4].SequenceEqual("STAR"u8) || header[4] != 2 || header[5] != 0)
            throw new InvalidDataException("The Steam Bridge payload is not a supported Starlight v2 package.");
        var count = header[6];
        var tableLength = 12 + count * 256;
        var star = bytes[headerOffset..];
        if (count == 0 || tableLength > star.Length || (header[7] & ~1) != 0)
            throw new InvalidDataException("The Steam Bridge section table is invalid.");
        uint shimHash = 0x811c9dc5;
        foreach (var value in bytes.Slice(4, (int)shimLength))
            shimHash = unchecked((shimHash ^ value) * 0x01000193);
        if (shimHash != BinaryPrimitives.ReadUInt32LittleEndian(header[8..]))
            throw new InvalidDataException("The Steam Bridge bootstrap failed its integrity check.");
        var ids = new HashSet<byte>();
        ulong previousEnd = (ulong)tableLength;
        for (var index = 0; index < count; index++)
        {
            var entry = star.Slice(12 + index * 256, 256);
            var offset = BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]);
            var length = BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]);
            if (!ids.Add(entry[0]) || length == 0 || offset < previousEnd
                || offset > (ulong)star.Length || length > (ulong)star.Length - offset)
                throw new InvalidDataException("The Steam Bridge contains invalid section ranges.");
            var data = star.Slice((int)offset, (int)length);
            uint crc = 0xffffffff;
            foreach (var value in data)
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++)
                    crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
            }
            if (~crc != BinaryPrimitives.ReadUInt32LittleEndian(entry[24..]))
                throw new InvalidDataException("The Steam Bridge section failed its integrity check.");
            previousEnd = offset + length;
        }
        // The bridge needs metadata, a native backend, the client frontend and store preload.
        if (!ids.IsSupersetOf([1, 2, 3, 4]))
            throw new InvalidDataException("The Steam Bridge package is missing required components.");
    }

    public static void ValidatePayloadFile(string path)
    {
        if (new FileInfo(path).Length > MaximumPayloadBytes)
            throw new InvalidDataException("The Steam Bridge payload exceeds its size limit.");
        ValidatePayload(File.ReadAllBytes(path));
    }

    public static bool HasMillennium(string steamRoot) =>
        File.Exists(Path.Combine(steamRoot, "steam.exe"))
        && File.Exists(Path.Combine(steamRoot, "millennium", "lib", "millennium.dll"))
        && File.Exists(Path.Combine(steamRoot, "millennium", "bin", "millennium.luavm64.exe"));

    public static string Target(string steamRoot) =>
        Path.Combine(Path.GetFullPath(steamRoot), "millennium", "plugins", FileName);

    public static void Install(string payload, string steamRoot, string applicationRoot)
    {
        steamRoot = Path.GetFullPath(steamRoot);
        if (!HasMillennium(steamRoot)) throw new IOException("Steam with Millennium was not found at the selected path.");
        var target = Target(steamRoot);
        EnsureNoLinks(target);
        ValidatePayloadFile(payload);
        var bytes = File.ReadAllBytes(payload);
        ValidatePayload(bytes);
        var receiptPath = Path.Combine(applicationRoot, ReceiptName);
        Directory.CreateDirectory(applicationRoot);
        if (File.Exists(target))
        {
            if (!File.Exists(receiptPath)) throw new IOException("A Steam Bridge package already exists without an installation receipt. Remove it manually or select another Steam path.");
            var previous = JsonSerializer.Deserialize(File.ReadAllText(receiptPath), BridgeJsonContext.Default.SteamBridgeReceipt)
                ?? throw new InvalidDataException("Invalid Steam Bridge installation receipt.");
            if (!Target(previous.SteamRoot).Equals(target, StringComparison.OrdinalIgnoreCase)
                || Hash(target) != previous.Sha256)
                throw new IOException("The existing Steam Bridge package was changed outside Setup. It was preserved.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var staged = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var backup = target + "." + Guid.NewGuid().ToString("N") + ".bak";
        var hadTarget = File.Exists(target);
        try
        {
            File.WriteAllBytes(staged, bytes);
            if (hadTarget) File.Replace(staged, target, backup);
            else File.Move(staged, target);
            try
            {
                var receiptStage = receiptPath + ".tmp";
                File.WriteAllText(receiptStage, JsonSerializer.Serialize(new SteamBridgeReceipt(steamRoot, Hash(target)), BridgeJsonContext.Default.SteamBridgeReceipt));
                File.Move(receiptStage, receiptPath, overwrite: true);
            }
            catch
            {
                if (hadTarget) File.Move(backup, target, overwrite: true);
                else File.Delete(target);
                throw;
            }
        }
        finally { File.Delete(staged); File.Delete(backup); }
    }

    public static bool Remove(string applicationRoot)
    {
        var receiptPath = Path.Combine(applicationRoot, ReceiptName);
        if (!File.Exists(receiptPath)) return false;
        var receipt = JsonSerializer.Deserialize(File.ReadAllText(receiptPath), BridgeJsonContext.Default.SteamBridgeReceipt)
            ?? throw new InvalidDataException("Invalid Steam Bridge installation receipt.");
        if (!File.Exists(Path.Combine(receipt.SteamRoot, "steam.exe"))) return false;
        var target = Target(receipt.SteamRoot);
        EnsureNoLinks(target);
        if (File.Exists(target))
        {
            if (Hash(target) != receipt.Sha256) return false;
            File.Delete(target);
        }
        File.Delete(receiptPath);
        return true;
    }

    private static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void EnsureNoLinks(string target)
    {
        for (var path = target; !string.IsNullOrEmpty(path); path = Path.GetDirectoryName(path))
        {
            if ((File.Exists(path) || Directory.Exists(path))
                && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Steam Bridge paths may not contain links or junctions.");
        }
    }
}

internal sealed record SteamBridgeReceipt(string SteamRoot, string Sha256);

[JsonSerializable(typeof(SteamBridgeReceipt))]
internal partial class BridgeJsonContext : JsonSerializerContext;
