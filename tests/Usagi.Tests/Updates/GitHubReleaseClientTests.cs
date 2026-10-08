using System.Net;
using Usagi.Core.Updates;

namespace Usagi.Tests.Updates;

public class GitHubReleaseClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static GitHubReleaseClient ClientReturning(HttpStatusCode status, string? json, out StubHandler handler)
    {
        handler = new StubHandler(_ => new HttpResponseMessage(status)
        {
            Content = json is null ? null : new StringContent(json)
        });
        return new GitHubReleaseClient(new HttpClient(handler), "owner", "repo", "Usagi/1.0.0");
    }

    [Fact]
    public async Task GetLatestReleaseAsync_ReadsTheTagAndPage()
    {
        const string json = """
        { "tag_name": "v1.4.0", "html_url": "https://github.com/owner/repo/releases/tag/v1.4.0",
          "draft": false, "prerelease": false, "name": "1.4.0" }
        """;
        var client = ClientReturning(HttpStatusCode.OK, json, out _);

        var release = await client.GetLatestReleaseAsync();

        Assert.NotNull(release);
        Assert.Equal(new AppVersion(1, 4, 0), release.Version);
        Assert.Equal("v1.4.0", release.Tag);
        Assert.Equal(new Uri("https://github.com/owner/repo/releases/tag/v1.4.0"), release.PageUrl);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_SendsAGetWithTheHeadersGitHubRequires()
    {
        var client = ClientReturning(HttpStatusCode.NotFound, null, out var handler);

        await client.GetLatestReleaseAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri("https://api.github.com/repos/owner/repo/releases/latest"), request.RequestUri);
        Assert.Contains("Usagi/1.0.0", request.Headers.UserAgent.ToString());
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_ReturnsNullWhenNothingIsReleasedYet()
    {
        var client = ClientReturning(HttpStatusCode.NotFound, """{ "message": "Not Found" }""", out _);

        Assert.Null(await client.GetLatestReleaseAsync());
    }

    [Theory]
    [InlineData("""{ "tag_name": "v2.0.0", "html_url": "https://github.com/owner/repo/releases/tag/v2.0.0", "draft": true }""")]
    [InlineData("""{ "tag_name": "v2.0.0-beta.1", "html_url": "https://github.com/owner/repo/releases/tag/v2.0.0-beta.1", "prerelease": true }""")]
    public async Task GetLatestReleaseAsync_IgnoresDraftsAndPreReleases(string json)
    {
        var client = ClientReturning(HttpStatusCode.OK, json, out _);

        Assert.Null(await client.GetLatestReleaseAsync());
    }

    [Theory]
    [InlineData("https://evil.example.com/download")]
    [InlineData("http://github.com/owner/repo/releases/tag/v1.4.0")]
    [InlineData("not a url")]
    [InlineData(null)]
    public async Task GetLatestReleaseAsync_OnlyEverLinksToGitHub(string? htmlUrl)
    {
        var json = htmlUrl is null
            ? """{ "tag_name": "v1.4.0" }"""
            : $$"""{ "tag_name": "v1.4.0", "html_url": "{{htmlUrl}}" }""";
        var client = ClientReturning(HttpStatusCode.OK, json, out _);

        var release = await client.GetLatestReleaseAsync();

        Assert.Equal(new Uri("https://github.com/owner/repo/releases"), release!.PageUrl);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, """{ "message": "API rate limit exceeded" }""")]
    [InlineData(HttpStatusCode.InternalServerError, null)]
    [InlineData(HttpStatusCode.OK, "<html>not json</html>")]
    [InlineData(HttpStatusCode.OK, """{ "tag_name": "nightly" }""")]
    public async Task GetLatestReleaseAsync_ThrowsUpdateCheckExceptionOnAnythingElse(HttpStatusCode status, string? json)
    {
        var client = ClientReturning(status, json, out _);

        await Assert.ThrowsAsync<UpdateCheckException>(() => client.GetLatestReleaseAsync());
    }

    [Fact]
    public async Task GetLatestReleaseAsync_WrapsNetworkFailures()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("offline"));
        var client = new GitHubReleaseClient(new HttpClient(handler), "owner", "repo", "ua");

        await Assert.ThrowsAsync<UpdateCheckException>(() => client.GetLatestReleaseAsync());
    }
}
