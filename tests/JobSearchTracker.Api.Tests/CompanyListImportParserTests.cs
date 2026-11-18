using JobSearchTracker.Api.Management;

namespace JobSearchTracker.Api.Tests;

public class CompanyListImportParserTests
{
    [Theory]
    [InlineData("Acme\r\nGlobex\r\nInitech")]
    [InlineData("Acme\nGlobex\nInitech")]
    [InlineData("Acme\rGlobex\rInitech")]
    [InlineData("Acme\r\nGlobex\nInitech")]
    [InlineData("Acme\rGlobex\r\nInitech\r\n")]
    public void Splits_on_crlf_lf_and_cr(string text)
    {
        var result = CompanyListImportParser.Parse(text);

        Assert.Equal(new[] { "Acme", "Globex", "Initech" }, result.Rows.Select(r => r.Name));
        Assert.Equal(new[] { 1, 2, 3 }, result.Rows.Select(r => r.RowNumber));
        Assert.False(result.TooManyRows);
    }

    [Fact]
    public void Skips_blank_and_whitespace_only_lines_but_counts_them_in_row_numbers()
    {
        var result = CompanyListImportParser.Parse("\nAcme\n   \n\t\nGlobex\n\n");

        Assert.Equal(new[] { 2, 5 }, result.Rows.Select(r => r.RowNumber));
        Assert.Equal(new[] { "Acme", "Globex" }, result.Rows.Select(r => r.Name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n \t \n")]
    public void Text_without_a_name_has_no_rows(string? text)
    {
        var result = CompanyListImportParser.Parse(text);

        Assert.Empty(result.Rows);
        Assert.False(result.TooManyRows);
    }

    [Fact]
    public void Trims_each_line_but_keeps_the_pasted_input()
    {
        var row = Assert.Single(CompanyListImportParser.Parse("   Acme   Corp \t").Rows);

        Assert.Equal("Acme   Corp", row.Name);
        Assert.Equal("acme corp", row.NormalizedName);
        Assert.Equal("   Acme   Corp \t", row.Input);
    }

    [Fact]
    public void Never_splits_on_commas()
    {
        var rows = CompanyListImportParser.Parse("Acme, Inc.\nSmith, Jones & Partners, LLP").Rows;

        Assert.Equal(new[] { "Acme, Inc.", "Smith, Jones & Partners, LLP" }, rows.Select(r => r.Name));
        Assert.All(rows, r => Assert.Equal(ParsedRowKind.Candidate, r.Kind));
    }

    [Theory]
    [InlineData("Ａcme Corp")] // full-width A
    [InlineData("ＡＣＭＥ　ＣＯＲＰ")] // full-width letters and ideographic space
    [InlineData("ACME   corp")]
    public void Treats_nfkc_and_case_and_whitespace_variants_as_the_same_name(string variant)
    {
        var rows = CompanyListImportParser.Parse($"Acme Corp\n{variant}").Rows;

        Assert.Equal(ParsedRowKind.Candidate, rows[0].Kind);
        Assert.Equal(ParsedRowKind.DuplicateInInput, rows[1].Kind);
        Assert.Equal("acme corp", rows[1].NormalizedName);
        Assert.Equal(1, rows[1].DuplicateOfRow);
    }

    [Fact]
    public void The_first_occurrence_wins_and_every_later_duplicate_points_to_it()
    {
        var rows = CompanyListImportParser.Parse("Acme\nGlobex\n\nacme\nGLOBEX\nACME").Rows;

        Assert.Equal(
            new[] { ParsedRowKind.Candidate, ParsedRowKind.Candidate, ParsedRowKind.DuplicateInInput, ParsedRowKind.DuplicateInInput, ParsedRowKind.DuplicateInInput },
            rows.Select(r => r.Kind));
        Assert.Equal(new int?[] { null, null, 1, 2, 1 }, rows.Select(r => r.DuplicateOfRow));
        Assert.Equal(new[] { 1, 2, 4, 5, 6 }, rows.Select(r => r.RowNumber));
    }

    [Fact]
    public void A_name_of_200_characters_is_valid_and_201_is_invalid_without_claiming_the_name()
    {
        var ok = new string('a', 200);
        var tooLong = new string('b', 201);

        var rows = CompanyListImportParser.Parse($"{ok}\n{tooLong}\n{tooLong}").Rows;

        Assert.Equal(ParsedRowKind.Candidate, rows[0].Kind);
        Assert.Equal(ParsedRowKind.Invalid, rows[1].Kind);
        Assert.Equal(ParsedRowKind.Invalid, rows[2].Kind); // invalid rows never become the "first" of a duplicate
        Assert.Null(rows[1].Name);
        Assert.Null(rows[1].NormalizedName);
        Assert.Contains("200", rows[1].Error);
    }

    [Fact]
    public void Accepts_1000_non_blank_rows_and_rejects_more()
    {
        string Lines(int count) => string.Join("\n", Enumerable.Range(1, count).Select(i => $"Company {i}\n"));

        var atLimit = CompanyListImportParser.Parse(Lines(CompanyListImportParser.MaxRows));
        var over = CompanyListImportParser.Parse(Lines(CompanyListImportParser.MaxRows + 1));

        Assert.Equal(1000, CompanyListImportParser.MaxRows);
        Assert.Equal(1000, atLimit.Rows.Count);
        Assert.False(atLimit.TooManyRows);
        Assert.True(over.TooManyRows);
    }

    [Fact]
    public void Blank_lines_do_not_count_towards_the_row_limit()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, CompanyListImportParser.MaxRows).Select(i => $"Company {i}"));

        var result = CompanyListImportParser.Parse(text);

        Assert.Equal(CompanyListImportParser.MaxRows, result.Rows.Count);
        Assert.False(result.TooManyRows);
    }
}
