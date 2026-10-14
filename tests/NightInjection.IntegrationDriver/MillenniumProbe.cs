using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

internal static class MillenniumProbe
{
    public static async Task RunAsync(string method)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var parameters = await Console.In.ReadToEndAsync();
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(parameters) ? "{}" : parameters);
        using var frame = new MemoryStream(); frame.WriteByte(0x83);
        Text(frame, "id"); Text(frame, "v2-diagnostic"); Text(frame, "method"); Text(frame, method); Text(frame, "params"); Encode(frame, document.RootElement);
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(Path.Combine(Path.GetTempPath(), "millennium-mep.sock")), timeout.Token);
        using var connection = new NetworkStream(socket);
        var header = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)frame.Length);
        await connection.WriteAsync(header, timeout.Token); await connection.WriteAsync(frame.ToArray(), timeout.Token);
        await connection.ReadExactlyAsync(header, timeout.Token);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header); if (length > 1024 * 1024) throw new InvalidDataException();
        var response = new byte[(int)length]; await connection.ReadExactlyAsync(response, timeout.Token);
        var position = 0; Console.WriteLine(JsonSerializer.Serialize(Decode(response, ref position)));
    }
    private static void Text(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length < 32) stream.WriteByte((byte)(0xa0 | bytes.Length));
        else if (bytes.Length < 256) { stream.WriteByte(0xd9); stream.WriteByte((byte)bytes.Length); }
        else { stream.WriteByte(0xda); var length = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)bytes.Length); stream.Write(length); }
        stream.Write(bytes);
    }
    private static void Encode(Stream stream, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var fields = value.EnumerateObject().ToArray(); if (fields.Length > 15) throw new InvalidDataException();
                stream.WriteByte((byte)(0x80 | fields.Length)); foreach (var field in fields) { Text(stream, field.Name); Encode(stream, field.Value); } break;
            case JsonValueKind.String: Text(stream, value.GetString()!); break;
            case JsonValueKind.True: stream.WriteByte(0xc3); break;
            case JsonValueKind.False: stream.WriteByte(0xc2); break;
            case JsonValueKind.Null: stream.WriteByte(0xc0); break;
            case JsonValueKind.Number: stream.WriteByte(0xd3); var number = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(number, value.GetInt64()); stream.Write(number); break;
            default: throw new InvalidDataException();
        }
    }
    private static ulong Number(byte[] data, ref int offset, int length)
    { ulong number = 0; for (var i = 0; i < length; i++) number = (number << 8) | data[offset++]; return number; }
    private static string String(byte[] data, ref int offset, int length)
    { var text = Encoding.UTF8.GetString(data, offset, length); offset += length; return text; }
    private static object? Decode(byte[] data, ref int offset)
    {
        var type = data[offset++];
        if (type <= 0x7f) return (int)type;
        if (type >= 0xe0) return (int)(sbyte)type;
        if ((type & 0xe0) == 0xa0) return String(data, ref offset, type & 31);
        if ((type & 0xf0) == 0x80) return Map(data, ref offset, type & 15);
        if ((type & 0xf0) == 0x90) return Array(data, ref offset, type & 15);
        switch (type)
        {
            case 0xc0: return null; case 0xc2: return false; case 0xc3: return true;
            case 0xcc: return Number(data, ref offset, 1); case 0xcd: return Number(data, ref offset, 2);
            case 0xce: return Number(data, ref offset, 4); case 0xcf: return Number(data, ref offset, 8);
            case 0xd0: return (sbyte)Number(data, ref offset, 1); case 0xd1: return (short)Number(data, ref offset, 2);
            case 0xd2: return (int)Number(data, ref offset, 4); case 0xd3: return (long)Number(data, ref offset, 8);
            case 0xcb: return BitConverter.Int64BitsToDouble((long)Number(data, ref offset, 8));
            case 0xca: return BitConverter.Int32BitsToSingle((int)Number(data, ref offset, 4));
            case 0xd9: return String(data, ref offset, (int)Number(data, ref offset, 1));
            case 0xda: return String(data, ref offset, (int)Number(data, ref offset, 2));
            case 0xdb: return String(data, ref offset, (int)Number(data, ref offset, 4));
            case 0xdc: return Array(data, ref offset, (int)Number(data, ref offset, 2));
            case 0xdd: return Array(data, ref offset, (int)Number(data, ref offset, 4));
            case 0xde: return Map(data, ref offset, (int)Number(data, ref offset, 2));
            case 0xdf: return Map(data, ref offset, (int)Number(data, ref offset, 4));
            default: throw new InvalidDataException($"Unsupported diagnostic msgpack value {type:X2}");
        }
    }
    private static object?[] Array(byte[] bytes, ref int offset, int count)
    { var items = new object?[count]; for (var i = 0; i < count; i++) items[i] = Decode(bytes, ref offset); return items; }
    private static Dictionary<string, object?> Map(byte[] bytes, ref int offset, int count)
    { var map = new Dictionary<string, object?>(); for (var i = 0; i < count; i++) { var key = Decode(bytes, ref offset)?.ToString()!; map[key] = Decode(bytes, ref offset); } return map; }
}
