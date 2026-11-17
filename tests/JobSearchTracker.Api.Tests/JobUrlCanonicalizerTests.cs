using JobSearchTracker.Api.Data;

namespace JobSearchTracker.Api.Tests;

/// <summary>The URL canonicalization rule of docs/api.md (Jobs). Pure unit tests; no database needed.</summary>
public class JobUrlCanonicalizerTests
{
    private static string Canonical(string url)
    {
        Assert.True(JobUrlCanonicalizer.TryCanonicalize(url, out var canonical, out var error), error);
        return canonical;
    }

    [Theory]
    [InlineData("https://Jobs.Example.com/careers/123", "https://jobs.example.com/careers/123")]
    [InlineData("https://www.example.com/careers/123", "https://example.com/careers/123")]
    [InlineData("https://example.com:443/careers/123", "https://example.com/careers/123")]
    [InlineData("http://example.com:80/careers/123", "http://example.com/careers/123")]
    [InlineData("https://example.com:8443/careers/123", "https://example.com:8443/careers/123")]
    [InlineData("https://example.com/careers/123/", "https://example.com/careers/123")]
    [InlineData("https://example.com/", "https://example.com")]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("https://example.com/careers/123#apply", "https://example.com/careers/123")]
    [InlineData("  https://example.com/Careers/ABC  ", "https://example.com/Careers/ABC")]
    public void Host_port_fragment_and_trailing_slash_are_normalized_and_the_path_keeps_its_case(string input, string expected) =>
        Assert.Equal(expected, Canonical(input));

    [Theory]
    [InlineData("https://example.com/job?utm_source=x&utm_medium=y&gh_jid=42", "https://example.com/job?gh_jid=42")]
    [InlineData("https://example.com/job?UTM_Campaign=x&currentJobId=99", "https://example.com/job?currentJobId=99")]
    [InlineData("https://example.com/job?fbclid=a&gclid=b&msclkid=c&trk=d&trackingId=e&refId=f", "https://example.com/job")]
    [InlineData("https://example.com/job?TRACKINGID=e&REFID=f&jobId=7", "https://example.com/job?jobId=7")]
    public void Only_tracking_parameters_are_removed(string input, string expected) =>
        Assert.Equal(expected, Canonical(input));

    [Fact]
    public void Remaining_parameters_are_sorted_so_their_order_does_not_matter()
    {
        Assert.Equal(
            Canonical("https://example.com/job?b=2&a=1&gh_jid=42"),
            Canonical("https://example.com/job?gh_jid=42&a=1&b=2&utm_source=mail"));
    }

    [Fact]
    public void Job_identifying_parameters_are_never_treated_as_tracking()
    {
        Assert.False(JobUrlCanonicalizer.IsTrackingParameter("gh_jid"));
        Assert.False(JobUrlCanonicalizer.IsTrackingParameter("currentJobId"));
        Assert.True(JobUrlCanonicalizer.IsTrackingParameter("utm_anything"));
        Assert.True(JobUrlCanonicalizer.IsTrackingParameter("FBCLID"));
    }

    [Fact]
    public void Different_job_identifiers_stay_different()
    {
        Assert.NotEqual(Canonical("https://boards.example.com/acme?gh_jid=1"), Canonical("https://boards.example.com/acme?gh_jid=2"));
    }

    [Fact]
    public void The_scheme_is_kept_so_http_and_https_are_different_canonical_urls()
    {
        Assert.NotEqual(Canonical("http://example.com/job"), Canonical("https://example.com/job"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("example.com/job")]
    [InlineData("ftp://example.com/job")]
    [InlineData("mailto:hr@example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:secret@example.com/job")]
    public void Anything_that_is_not_an_absolute_http_or_https_url_without_credentials_is_rejected(string? input)
    {
        Assert.False(JobUrlCanonicalizer.TryCanonicalize(input, out var canonical, out var error));
        Assert.Equal(string.Empty, canonical);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void A_url_over_the_length_limit_is_rejected()
    {
        var url = "https://example.com/" + new string('a', JobLimits.Url);
        Assert.False(JobUrlCanonicalizer.TryCanonicalize(url, out _, out var error));
        Assert.Contains("at most", error);
    }
}
