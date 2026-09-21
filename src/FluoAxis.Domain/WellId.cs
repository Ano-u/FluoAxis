namespace FluoAxis.Domain;

/// <summary>
/// 96-well plate well identifier: row A–H, column 1–12.
/// </summary>
public readonly record struct WellId
{
    public const char MinRow = 'A';
    public const char MaxRow = 'H';
    public const int MinColumn = 1;
    public const int MaxColumn = 12;

    public char Row { get; }
    public int Column { get; }

    public WellId(char row, int column)
    {
        if (row is < MinRow or > MaxRow)
        {
            throw new ArgumentOutOfRangeException(nameof(row), row, "Well row must be between 'A' and 'H'.");
        }

        if (column is < MinColumn or > MaxColumn)
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, "Well column must be between 1 and 12.");
        }

        Row = row;
        Column = column;
    }

    /// <summary>Strictly parses identifiers of the form "A1" through "H12".</summary>
    public static WellId Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        if (text.Length is < 2 or > 3)
        {
            throw new FormatException($"Well identifier '{text}' must be one row letter (A–H) followed by a column number (1–12).");
        }

        char row = char.ToUpperInvariant(text[0]);
        if (row is < MinRow or > MaxRow)
        {
            throw new FormatException($"Well identifier '{text}' has row outside the A–H range.");
        }

        if (text[1] == '0')
        {
            throw new FormatException($"Well identifier '{text}' must not have a leading zero in its column number.");
        }

        int column = 0;
        for (int i = 1; i < text.Length; i++)
        {
            if (!char.IsAsciiDigit(text[i]))
            {
                throw new FormatException($"Well identifier '{text}' must contain only digits after the row letter.");
            }

            column = (column * 10) + (text[i] - '0');
        }

        if (column is < MinColumn or > MaxColumn)
        {
            throw new FormatException($"Well identifier '{text}' has column outside the 1–12 range.");
        }

        return new WellId(row, column);
    }

    /// <summary>All 96 wells in plate order (A1..A12, B1..B12, ...).</summary>
    public static IReadOnlyList<WellId> AllWells { get; } = CreateAllWells();

    public override string ToString() => $"{Row}{Column}";

    private static WellId[] CreateAllWells()
    {
        var wells = new WellId[8 * 12];
        int index = 0;
        for (char row = MinRow; row <= MaxRow; row++)
        {
            for (int column = MinColumn; column <= MaxColumn; column++)
            {
                wells[index++] = new WellId(row, column);
            }
        }

        return wells;
    }
}
