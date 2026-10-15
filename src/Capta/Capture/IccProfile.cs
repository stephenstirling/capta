using System.Buffers.Binary;
using System.Text;

namespace Capta.Capture;

/// <summary>Reads the bits of an ICC profile Capta shows (its name).</summary>
public static class IccProfile
{
    /// <summary>The profile's description ('desc' tag, ICC v2 or v4), or null if it has none.</summary>
    public static string? Describe(byte[] icc)
    {
        try
        {
            var span = icc.AsSpan();
            var count = BinaryPrimitives.ReadUInt32BigEndian(span[128..]);
            for (var i = 0; i < count; i++)
            {
                var entry = span.Slice(132 + i * 12, 12);
                if (!entry[..4].SequenceEqual("desc"u8)) continue;
                var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
                var size = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
                var tag = span.Slice(offset, size);
                var text = tag[..4].SequenceEqual("desc"u8) ? TextDescription(tag)
                    : tag[..4].SequenceEqual("mluc"u8) ? MultiLocalized(tag)
                    : null;
                text = text?.Trim();
                return string.IsNullOrEmpty(text) ? null : text;
            }
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            // Malformed profile: no name.
        }
        return null;
    }

    /// <summary>v2 textDescriptionType: the ASCII description.</summary>
    private static string TextDescription(ReadOnlySpan<byte> tag)
    {
        var length = (int)BinaryPrimitives.ReadUInt32BigEndian(tag[8..]);
        return Encoding.ASCII.GetString(tag.Slice(12, length)).TrimEnd('\0');
    }

    /// <summary>v4 multiLocalizedUnicodeType: the first record (UTF-16BE).</summary>
    private static string? MultiLocalized(ReadOnlySpan<byte> tag)
    {
        if (BinaryPrimitives.ReadUInt32BigEndian(tag[8..]) == 0) return null;
        var length = (int)BinaryPrimitives.ReadUInt32BigEndian(tag[20..]);
        var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(tag[24..]);
        return Encoding.BigEndianUnicode.GetString(tag.Slice(offset, length)).TrimEnd('\0');
    }
}
