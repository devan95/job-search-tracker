using JobSearchTracker.Api.Data;

namespace JobSearchTracker.Api.Management;

/// <summary>What the parser decided about one non-blank pasted row (before any database lookup).</summary>
public enum ParsedRowKind
{
    /// <summary>A valid name that is the first row with its normalized name; the database decides new, existing or already in the list.</summary>
    Candidate,

    /// <summary>The normalized name was already used by an earlier valid row.</summary>
    DuplicateInInput,

    /// <summary>The row cannot be used (for example longer than 200 characters).</summary>
    Invalid,
}

/// <param name="RowNumber">1-based line number in the pasted text; blank lines count.</param>
/// <param name="Input">The line as pasted (without its line break).</param>
/// <param name="Name">The trimmed display name; null for an invalid row.</param>
/// <param name="NormalizedName">The canonical normalized name; null for an invalid row.</param>
/// <param name="DuplicateOfRow">For <see cref="ParsedRowKind.DuplicateInInput"/>: the row number of the first row with the same normalized name.</param>
/// <param name="Error">For <see cref="ParsedRowKind.Invalid"/>: why the row is invalid.</param>
public sealed record ParsedRow(
    int RowNumber,
    string Input,
    string? Name,
    string? NormalizedName,
    ParsedRowKind Kind,
    int? DuplicateOfRow,
    string? Error);

/// <param name="Rows">The non-blank rows in input order (at most <see cref="CompanyListImportParser.MaxRows"/>).</param>
/// <param name="TooManyRows">True when the text has more than <see cref="CompanyListImportParser.MaxRows"/> non-blank rows; parsing stopped there.</param>
public sealed record ParseResult(IReadOnlyList<ParsedRow> Rows, bool TooManyRows);

/// <summary>
/// Pure parser for the company paste workflow (one company per line). Lines end at CRLF, LF or CR only; commas are never
/// delimiters; each line is trimmed; blank lines are skipped but still counted for row numbers. Names are normalized with
/// the existing <see cref="NameNormalizer"/>, so uniqueness here is the same as in the database.
/// </summary>
public static class CompanyListImportParser
{
    /// <summary>Maximum number of non-blank rows accepted in one request.</summary>
    public const int MaxRows = 1000;

    public static ParseResult Parse(string? text)
    {
        var rows = new List<ParsedRow>();
        if (string.IsNullOrEmpty(text))
        {
            return new ParseResult(rows, false);
        }

        var firstRowByName = new Dictionary<string, int>(StringComparer.Ordinal);
        var lineNumber = 0;
        var start = 0;
        while (true)
        {
            var end = start;
            while (end < text.Length && text[end] != '\n' && text[end] != '\r')
            {
                end++;
            }

            lineNumber++;
            var raw = text.Substring(start, end - start);
            var trimmed = raw.Trim();
            if (trimmed.Length > 0)
            {
                if (rows.Count == MaxRows)
                {
                    return new ParseResult(rows, true);
                }

                rows.Add(ParseRow(lineNumber, raw, trimmed, firstRowByName));
            }

            if (end >= text.Length)
            {
                break;
            }

            start = text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? end + 2 : end + 1;
        }

        return new ParseResult(rows, false);
    }

    private static ParsedRow ParseRow(int rowNumber, string raw, string trimmed, Dictionary<string, int> firstRowByName)
    {
        if (trimmed.Length > NameNormalizer.MaxLength)
        {
            return Invalid(rowNumber, raw, $"A name must be at most {NameNormalizer.MaxLength} characters (this one has {trimmed.Length}).");
        }

        string normalized;
        try
        {
            normalized = NameNormalizer.Normalize(trimmed);
        }
        catch (ArgumentException)
        {
            // Only reachable when NFKC expands the name beyond the limit.
            return Invalid(rowNumber, raw, $"A name must be at most {NameNormalizer.MaxLength} characters after normalization.");
        }

        if (firstRowByName.TryGetValue(normalized, out var firstRow))
        {
            return new ParsedRow(rowNumber, raw, trimmed, normalized, ParsedRowKind.DuplicateInInput, firstRow, null);
        }

        firstRowByName[normalized] = rowNumber;
        return new ParsedRow(rowNumber, raw, trimmed, normalized, ParsedRowKind.Candidate, null, null);
    }

    private static ParsedRow Invalid(int rowNumber, string raw, string error) =>
        new(rowNumber, raw, null, null, ParsedRowKind.Invalid, null, error);
}
