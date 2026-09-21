namespace FluoAxis.Domain.Tests;

using Xunit;

public class WellIdTests
{
    [Fact]
    public void Constructor_accepts_upper_boundaries()
    {
        var well = new WellId('H', 12);

        Assert.Equal('H', well.Row);
        Assert.Equal(12, well.Column);
    }

    [Fact]
    public void Constructor_rejects_row_outside_A_to_H()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WellId('I', 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WellId('a', 1));
    }

    [Fact]
    public void Constructor_rejects_column_outside_1_to_12()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WellId('A', 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WellId('A', 13));
    }

    [Fact]
    public void AllWells_contains_exactly_96_wells_in_plate_order()
    {
        var wells = WellId.AllWells;

        Assert.Equal(96, wells.Count);
        Assert.Equal("A1", wells[0].ToString());
        Assert.Equal("A12", wells[11].ToString());
        Assert.Equal("B1", wells[12].ToString());
        Assert.Equal("H12", wells[^1].ToString());
    }

    [Theory]
    [MemberData(nameof(AllPlateWells))]
    public void Parse_roundtrips_every_well(string text, char row, int column)
    {
        var well = WellId.Parse(text);

        Assert.Equal(row, well.Row);
        Assert.Equal(column, well.Column);
        Assert.Equal(text, well.ToString());
        Assert.Equal(new WellId(row, column), well);
    }

    [Theory]
    [InlineData("a1", "A1")]
    [InlineData("h12", "H12")]
    public void Parse_accepts_lowercase_letters_by_normalizing(string input, string expected)
    {
        Assert.Equal(expected, WellId.Parse(input).ToString());
    }

    [Theory]
    [InlineData("A")]
    [InlineData("A0")]
    [InlineData("A13")]
    [InlineData("I1")]
    [InlineData("a0")]
    [InlineData("A1x")]
    [InlineData("1A")]
    [InlineData("A 1")]
    [InlineData("A01")]
    public void Parse_rejects_malformed_identifiers(string text)
    {
        Assert.Throws<FormatException>(() => WellId.Parse(text));
    }

    [Fact]
    public void Parse_rejects_null_and_whitespace()
    {
        Assert.ThrowsAny<ArgumentException>(() => WellId.Parse(null!));
        Assert.ThrowsAny<ArgumentException>(() => WellId.Parse("   "));
    }

    public static TheoryData<string, char, int> AllPlateWells()
    {
        var data = new TheoryData<string, char, int>();
        for (char row = 'A'; row <= 'H'; row++)
        {
            for (int column = 1; column <= 12; column++)
            {
                data.Add($"{row}{column}", row, column);
            }
        }

        return data;
    }
}
