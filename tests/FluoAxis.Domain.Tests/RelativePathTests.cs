namespace FluoAxis.Domain.Tests;

using Xunit;

public class RelativePathTests
{
    [Theory]
    [MemberData(nameof(AcceptedPaths))]
    public void Parse_accepts_and_normalizes_valid_paths(string input, string expected)
    {
        var path = RelativePath.Parse(input);

        Assert.Equal(expected, path.Value);
        Assert.Equal(expected, path.ToString());
    }

    public static TheoryData<string, string> AcceptedPaths()
    {
        TheoryData<string, string> data = new();
        data.Add("data/raw/a.xlsx", "data/raw/a.xlsx");
        data.Add("reports/last.html", "reports/last.html");
        data.Add("data\\raw\\a.xlsx", "data/raw/a.xlsx");
        data.Add("data//raw///a.xlsx", "data/raw/a.xlsx");
        data.Add("  data/raw/a.xlsx  ", "data/raw/a.xlsx");
        data.Add("a", "a");
        data.Add("methods/BRAF V1.0.json", "methods/BRAF V1.0.json");
        data.Add("plots/run-1/3d-scatter.svg", "plots/run-1/3d-scatter.svg");
        data.Add("assets/中文资源.bin", "assets/中文资源.bin");
        data.Add("data/derived/normalized.parquet", "data/derived/normalized.parquet");
        return data;
    }

    [Theory]
    [MemberData(nameof(RejectedPaths))]
    public void Parse_rejects_invalid_paths(string input)
    {
        Assert.Throws<FormatException>(() => RelativePath.Parse(input));
    }

    public static TheoryData<string> RejectedPaths()
    {
        TheoryData<string> data = new();
        data.Add(string.Concat("C", ":", "\\", "x"));
        data.Add(string.Concat("C", ":", "x"));
        data.Add(string.Concat("\\", "\\", "server", "\\", "share"));
        data.Add(string.Concat("/", "etc"));
        data.Add("../escape");
        data.Add("a/../../escape");
        data.Add("a/b/../../../escape");
        data.Add("..");
        data.Add(string.Concat("file", ":", "///", "c", ":", "/x"));
        data.Add("file:relative.bin");
        data.Add("CON");
        data.Add("con.txt");
        data.Add("NUL.bin");
        data.Add("COM1.log");
        data.Add("aux");
        data.Add(".");
        data.Add("a/./b");
        data.Add("a/b.");
        data.Add("a/b<");
        data.Add("a/b:");
        data.Add("a|b");
        return data;
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("   ")]
    public void Parse_rejects_blank_input(string input)
    {
        Assert.ThrowsAny<ArgumentException>(() => RelativePath.Parse(input));
    }

    [Fact]
    public void Parse_rejects_null()
    {
        Assert.ThrowsAny<ArgumentException>(() => RelativePath.Parse(null!));
    }

    [Fact]
    public void FileName_returns_the_final_segment()
    {
        Assert.Equal("a.xlsx", RelativePath.Parse("data/raw/a.xlsx").FileName);
        Assert.Equal("a", RelativePath.Parse("a").FileName);
    }

    [Fact]
    public void Parent_walks_up_one_level()
    {
        var path = RelativePath.Parse("data/raw/a.xlsx");

        var parent = Assert.IsType<RelativePath>(path.Parent);
        Assert.Equal("data/raw", parent.Value);
        Assert.Equal("data", Assert.IsType<RelativePath>(parent.Parent).Value);
        Assert.Null(RelativePath.Parse("data").Parent);
    }

    [Fact]
    public void Combine_appends_child_paths()
    {
        var root = RelativePath.Parse("data");

        var withObject = root.Combine(RelativePath.Parse("raw/a.xlsx"));
        Assert.Equal("data/raw/a.xlsx", withObject.Value);

        var withString = root.Combine("raw/b.xlsx");
        Assert.Equal("data/raw/b.xlsx", withString.Value);
    }

    [Fact]
    public void Equality_is_exact_on_canonical_form()
    {
        var forward = RelativePath.Parse("data/raw/a.xlsx");
        var backslash = RelativePath.Parse("data\\raw\\a.xlsx");

        Assert.Equal(forward, backslash);
        Assert.Equal(forward.GetHashCode(), backslash.GetHashCode());
        Assert.NotEqual(forward, RelativePath.Parse("data/raw/a.xlsx.bak"));
        Assert.NotEqual(forward, RelativePath.Parse("DATA/RAW/A.XLSX"));
    }

    [Fact]
    public void Parse_collapses_traversal_that_stays_inside_the_root()
    {
        Assert.Equal("c", RelativePath.Parse("a/b/../../c").Value);
    }
}
