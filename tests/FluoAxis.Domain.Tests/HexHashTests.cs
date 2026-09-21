namespace FluoAxis.Domain.Tests;

using Xunit;

using System.Security.Cryptography;

public class HexHashTests
{
    [Fact]
    public void Constructor_normalizes_to_lowercase_hex()
    {
        var hash = new HexHash("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF");

        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", hash.Value);
    }

    [Fact]
    public void Constructor_rejects_wrong_length()
    {
        Assert.Throws<ArgumentException>(() => new HexHash("abc"));
        Assert.Throws<ArgumentException>(() => new HexHash(new string('a', 63)));
        Assert.Throws<ArgumentException>(() => new HexHash(new string('a', 65)));
    }

    [Fact]
    public void Constructor_rejects_non_hex_characters()
    {
        Assert.Throws<ArgumentException>(() => new HexHash(new string('g', 64)));
        Assert.Throws<ArgumentException>(() => new HexHash(new string('z', 64)));
    }

    [Fact]
    public void FromBytes_produces_lowercase_sha256_hex()
    {
        byte[] digest = SHA256.HashData([1, 2, 3]);

        var expected = Convert.ToHexString(digest).ToLowerInvariant();
        var hash = HexHash.FromBytes(digest);

        Assert.Equal(expected, hash.Value);
        Assert.Equal(64, hash.Value.Length);
    }

    [Fact]
    public void FromBytes_requires_exactly_a_32_byte_digest()
    {
        Assert.Throws<ArgumentException>(() => HexHash.FromBytes([1, 2, 3]));
        Assert.Throws<ArgumentException>(() => HexHash.FromBytes(new byte[31]));
        Assert.Throws<ArgumentException>(() => HexHash.FromBytes(new byte[33]));
    }

    [Fact]
    public void FromText_hashes_utf8_content_deterministically()
    {
        var first = HexHash.FromText("stable input");
        var second = HexHash.FromText("stable input");
        var other = HexHash.FromText("other input");

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public void Equality_follows_hex_value()
    {
        var upper = new HexHash("AB".PadRight(64, '0'));
        var lower = new HexHash("ab".PadRight(64, '0'));

        Assert.Equal(upper, lower);
    }
}
