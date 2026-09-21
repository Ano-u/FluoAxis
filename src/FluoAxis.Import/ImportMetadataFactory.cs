namespace FluoAxis.Import;

using FluoAxis.Domain;

/// <summary>
/// Produces the <see cref="SourceFileMetadata"/> for an imported file:
/// streams a SHA-256 digest, records the byte size, and keeps only the
/// bare file name plus the project-relative resource path. The original
/// machine location is discarded at this boundary (P0 path policy,
/// CLAUDE.md §4.2). The stream is read without closing it and its
/// position is restored.
/// </summary>
public static class ImportMetadataFactory
{
    public static SourceFileMetadata Create(
        string originalFileName,
        RelativePath relativeResourcePath,
        Stream content,
        string mediaType,
        DateTimeOffset importedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(originalFileName);
        ArgumentNullException.ThrowIfNull(relativeResourcePath);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        if (!content.CanSeek)
        {
            throw new ArgumentException("The content stream must be seekable for hashing and size accounting.", nameof(content));
        }

        if (!content.CanRead)
        {
            throw new ArgumentException("The content stream must be readable.", nameof(content));
        }

        long position = content.Position;
        try
        {
            content.Position = 0;
            byte[] digest = System.Security.Cryptography.SHA256.HashData(content);
            return new SourceFileMetadata(
                originalFileName,
                relativeResourcePath,
                HexHash.FromBytes(digest),
                content.Length,
                importedAtUtc,
                mediaType);
        }
        finally
        {
            content.Position = position;
        }
    }
}
