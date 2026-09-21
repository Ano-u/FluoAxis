namespace FluoAxis.Domain;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Lowercase 64-character hexadecimal SHA-256 digest. Safe to persist;
/// carries no filesystem or machine information.
/// </summary>
public readonly record struct HexHash
{
    public const int HexLength = 64;
    public const int ByteLength = 32;

    public string Value { get; }

    public HexHash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length != HexLength)
        {
            throw new ArgumentException($"Hash must be exactly {HexLength} hexadecimal characters.", nameof(value));
        }

        foreach (char c in value)
        {
            if (!Uri.IsHexDigit(c))
            {
                throw new ArgumentException("Hash must contain only hexadecimal characters.", nameof(value));
            }
        }

        Value = value.ToLowerInvariant();
    }

    /// <summary>Wraps a digest produced by <see cref="System.Security.Cryptography.SHA256"/>.</summary>
    public static HexHash FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteLength)
        {
            throw new ArgumentException($"Digest must be exactly {ByteLength} bytes (SHA-256).", nameof(bytes));
        }

        return new HexHash(Convert.ToHexString(bytes).ToLowerInvariant());
    }

    /// <summary>SHA-256 of the UTF-8 bytes of <paramref name="text"/>; pure in-memory computation.</summary>
    public static HexHash FromText(string text) => FromBytes(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public override string ToString() => Value;
}
