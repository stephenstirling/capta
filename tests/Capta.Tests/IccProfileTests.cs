using System.Buffers.Binary;
using System.Text;
using Capta.Capture;

namespace Capta.Tests;

public class IccProfileTests
{
    [Fact]
    public void NamesTheDisplayP3Profile() =>
        Assert.Equal("Display P3", IccProfile.Describe(ColourOutput.DisplayP3Profile));

    [Fact]
    public void ReadsAVersion4Name() =>
        Assert.Equal("DELL U2723QE", IccProfile.Describe(Profile("desc", Mluc("DELL U2723QE"))));

    [Fact]
    public void NoDescriptionMeansNoName() =>
        Assert.Null(IccProfile.Describe(Profile("cprt", Mluc("Copyright"))));

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(131)]
    public void MalformedProfilesHaveNoName(int length) =>
        Assert.Null(IccProfile.Describe(new byte[length]));

    [Fact]
    public void ATagPointingOutsideTheProfileHasNoName()
    {
        var profile = Profile("desc", Mluc("x"));
        BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(136), 100_000); // the tag's offset
        Assert.Null(IccProfile.Describe(profile));
    }

    /// <summary>A header, a one-entry tag table and the tag.</summary>
    private static byte[] Profile(string signature, byte[] tag)
    {
        var profile = new byte[144 + tag.Length];
        BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(0), (uint)profile.Length);
        Encoding.ASCII.GetBytes("acsp").CopyTo(profile, 36);
        BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(128), 1);
        Encoding.ASCII.GetBytes(signature).CopyTo(profile, 132);
        BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(136), 144);
        BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(140), (uint)tag.Length);
        tag.CopyTo(profile, 144);
        return profile;
    }

    /// <summary>multiLocalizedUnicodeType with one en-US record.</summary>
    private static byte[] Mluc(string text)
    {
        var utf16 = Encoding.BigEndianUnicode.GetBytes(text);
        var tag = new byte[28 + utf16.Length];
        Encoding.ASCII.GetBytes("mluc").CopyTo(tag, 0);
        BinaryPrimitives.WriteUInt32BigEndian(tag.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32BigEndian(tag.AsSpan(12), 12);
        Encoding.ASCII.GetBytes("enUS").CopyTo(tag, 16);
        BinaryPrimitives.WriteUInt32BigEndian(tag.AsSpan(20), (uint)utf16.Length);
        BinaryPrimitives.WriteUInt32BigEndian(tag.AsSpan(24), 28);
        utf16.CopyTo(tag, 28);
        return tag;
    }
}
