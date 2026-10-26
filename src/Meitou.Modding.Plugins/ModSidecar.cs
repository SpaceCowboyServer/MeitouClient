using System.Text;
using Meitou.Modding.Api;

namespace Meitou.Modding.Plugins;

/// <summary>
/// The sidecar bytes for one mod: a magic and format header, then each saveable's stable key and payload. The host writes this under
/// <c>&lt;save&gt;/meitou-mods/&lt;mod id&gt;.bin</c>, outside the known quick.save/platoon/zone files.
/// </summary>
static class ModSidecar
{
    public const int CurrentFormat = 1;
    public const int MaxBytes = 16 * 1024 * 1024;
    const int MaxSaveables = 1024;
    static ReadOnlySpan<byte> Magic => "MTMS"u8;

    public static byte[] Capture(IReadOnlyList<ISaveable> saveables)
    {
        if (saveables.Count > MaxSaveables)
            throw new InvalidDataException($"A mod has more than {MaxSaveables} saveables.");
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(CurrentFormat);
            writer.Write(saveables.Count);
            foreach (var saveable in saveables)
            {
                using var payload = new MemoryStream();
                saveable.Write(payload);
                byte[] bytes = payload.ToArray();
                if (bytes.Length > MaxBytes)
                    throw new InvalidDataException($"Saveable '{saveable.Key}' is larger than {MaxBytes} bytes.");
                writer.Write(saveable.Key);
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
        }
        if (stream.Length > MaxBytes)
            throw new InvalidDataException($"The mod sidecar is larger than {MaxBytes} bytes.");
        return stream.ToArray();
    }

    public static Dictionary<string, byte[]> Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        Span<byte> magic = stackalloc byte[4];
        reader.Read(magic);
        if (!magic.SequenceEqual(Magic)) throw new InvalidDataException("Not a Meitou mod sidecar.");
        int format = reader.ReadInt32();
        if (format != CurrentFormat) throw new InvalidDataException($"Mod sidecar format {format} is not supported.");
        int count = reader.ReadInt32();
        if (count is < 0 or > MaxSaveables) throw new InvalidDataException($"Mod sidecar has {count} saveables.");

        var saveables = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            string key = reader.ReadString();
            int length = reader.ReadInt32();
            if (length is < 0 or > MaxBytes) throw new InvalidDataException($"Saveable '{key}' has an invalid length.");
            byte[] payload = reader.ReadBytes(length);
            if (payload.Length != length) throw new EndOfStreamException("The mod sidecar ends in the middle of a saveable.");
            if (!saveables.TryAdd(key, payload)) throw new InvalidDataException($"Saveable key '{key}' appears twice.");
        }
        return saveables;
    }
}
