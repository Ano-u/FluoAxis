namespace FluoAxis.Domain;

using System.Collections.Frozen;

/// <summary>
/// Normalized project-package-relative path. This is the persisted
/// filesystem reference type of FluoAxis: rooted paths, traversal outside
/// the package root, current-directory segments, Windows reserved device
/// names, and file:// URIs are all rejected. The stored form uses forward
/// slashes and never contains a machine-specific location (CLAUDE.md §4).
/// Equality is exact string comparison on the canonical form.
/// </summary>
public sealed record RelativePath
{
    private static readonly FrozenSet<string> ReservedDeviceNames = new[]
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Canonical '/'-separated, '.'-free representation.</summary>
    public string Value { get; }

    /// <summary>Final path segment.</summary>
    public string FileName => Value[(Value.LastIndexOf('/') + 1)..];

    /// <summary>Parent path, or null when this path has a single segment.</summary>
    public RelativePath? Parent
    {
        get
        {
            int lastSlash = Value.LastIndexOf('/');
            return lastSlash < 0 ? null : new RelativePath(Value[..lastSlash]);
        }
    }

    private RelativePath(string value) => Value = value;

    /// <summary>
    /// Parses and canonicalizes a persisted or user-supplied path.
    /// Backslashes are converted to '/', consecutive separators are
    /// collapsed, and every segment is validated. Throws
    /// <see cref="FormatException"/> for anything outside the package root.
    /// </summary>
    public static RelativePath Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        string candidate = text.Trim().Replace('\\', '/');

        if (candidate.StartsWith('/') || candidate.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || Path.IsPathRooted(text.AsSpan()))
        {
            throw new FormatException($"'{text}' is a rooted path or URI; only project-relative paths are allowed.");
        }

        string[] rawSegments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var segments = new List<string>(rawSegments.Length);

        foreach (string segment in rawSegments)
        {
            if (segment == ".")
            {
                throw new FormatException($"'{text}' contains a '.' segment; use explicit relative names.");
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    throw new FormatException($"'{text}' escapes the project root with '..'.");
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            if (segment.EndsWith('.'))
            {
                throw new FormatException($"'{text}' contains segment '{segment}' with a trailing dot, which Windows would strip.");
            }

            if (IsReservedDeviceName(segment))
            {
                throw new FormatException($"'{text}' uses the reserved Windows device name '{segment}'.");
            }

            foreach (char c in segment)
            {
                if (char.IsControl(c) || c is '<' or '>' or ':' or '"' or '|' or '?' or '*')
                {
                    throw new FormatException($"'{text}' contains the invalid path character '{c}'.");
                }
            }

            segments.Add(segment);
        }

        if (segments.Count == 0)
        {
            throw new FormatException($"'{text}' does not name a resource inside the project package.");
        }

        return new RelativePath(string.Join("/", segments));
    }

    /// <summary>Alias of <see cref="Parse"/> for builder-style call sites.</summary>
    public static RelativePath Create(string text) => Parse(text);

    /// <summary>Appends <paramref name="other"/> under this path.</summary>
    public RelativePath Combine(RelativePath other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new RelativePath(Value + "/" + other.Value);
    }

    /// <summary>Appends a relative sub-path string under this path.</summary>
    public RelativePath Combine(string other) => Combine(Parse(other));

    private static bool IsReservedDeviceName(string segment)
    {
        string name = segment;
        int dot = name.IndexOf('.');
        if (dot >= 0)
        {
            name = name[..dot];
        }

        return ReservedDeviceNames.Contains(name);
    }

    public override string ToString() => Value;
}
