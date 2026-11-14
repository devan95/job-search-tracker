using System.Globalization;
using JobSearchTracker.Api.Data;

namespace JobSearchTracker.Api.Tests;

public class NameNormalizerTests
{
    [Theory]
    [InlineData("Acme Corp", "acme corp")]
    [InlineData("  Acme   Corp  ", "acme corp")]
    [InlineData("Acme\tCorp Ltd", "acme corp ltd")]
    [InlineData("ACME, Inc.", "acme, inc.")]
    [InlineData("Ａcme", "acme")] // full-width A -> NFKC -> a
    [InlineData("Café", "café")] // e + combining acute -> precomposed
    [InlineData("İstanbul", "i̇stanbul")] // invariant lowering of dotted capital I, not Turkish
    public void Normalize_applies_the_documented_rule(string input, string expected) =>
        Assert.Equal(expected, NameNormalizer.Normalize(input));

    [Fact]
    public void Normalize_ignores_the_current_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal("title", NameNormalizer.Normalize("TITLE"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Blank_names_are_rejected(string? input)
    {
        Assert.Throws<ArgumentException>(() => NameNormalizer.Normalize(input));
        Assert.Throws<ArgumentException>(() => NameNormalizer.CleanDisplayName(input));
    }

    [Fact]
    public void Names_longer_than_the_limit_are_rejected()
    {
        var tooLong = new string('a', NameNormalizer.MaxLength + 1);
        Assert.Throws<ArgumentException>(() => NameNormalizer.CleanDisplayName(tooLong));
        Assert.Throws<ArgumentException>(() => NameNormalizer.Normalize(tooLong));
        Assert.Equal(NameNormalizer.MaxLength, NameNormalizer.Normalize(new string('a', NameNormalizer.MaxLength)).Length);
    }

    [Fact]
    public void Display_name_is_trimmed_but_otherwise_kept()
    {
        Assert.Equal("Acme,  Corp", NameNormalizer.CleanDisplayName("  Acme,  Corp "));
    }

    [Fact]
    public void Entities_store_display_and_normalized_names()
    {
        var employer = new Employer("  Acme   Corp ");
        Assert.Equal("Acme   Corp", employer.Name);
        Assert.Equal("acme corp", employer.NormalizedName);

        var list = new CompanyList("Pune Tech", "  Pune ", WorkModes.Hybrid);
        Assert.Equal("Pune", list.LocationLabel);
        Assert.Equal(WorkModes.Hybrid, list.DefaultWorkModes);
        list.SetLocationLabel("   ");
        Assert.Null(list.LocationLabel);

        var profile = new SearchProfile(Guid.NewGuid(), "My Profile");
        Assert.Equal(1, profile.ProfileVersion);
        Assert.Null(profile.WorkModes);
        Assert.False(profile.CompanyMonitoringEnabled);
        Assert.False(profile.BroadSearchEnabled);
    }
}
