using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Usagi.Core.Updates;

/// <summary>A published release: its version and the page it's downloaded from.</summary>
public sealed record ReleaseInfo(AppVersion Version, string Tag, Uri PageUrl);

/// <summary>Where the latest release is looked up. An interface so the checker can be tested without the network.</summary>
public interface IReleaseSource
{
    /// <summary>The latest published release, or null if there is none yet.</summary>
    /// <exception cref="UpdateCheckException">The release couldn't be read.</exception>
    Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken ct = default);
}

public sealed class UpdateCheckException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Reads the latest release of a public GitHub repository from the REST API
/// (GET /repos/{owner}/{repo}/releases/latest). That endpoint already leaves out drafts and
/// pre-releases, so only a release meant for everyone is ever offered. No token: the
/// unauthenticated limit (60 requests an hour per IP) is plenty for a button pressed by hand.
/// </summary>
public sealed class GitHubReleaseClient(HttpClient httpClient, string owner, string repository, string userAgent) : IReleaseSource
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Uri LatestReleaseEndpoint { get; } = new($"https://api.github.com/repos/{owner}/{repository}/releases/latest");

    /// <summary>Where all releases are listed; what to open when the latest one's page isn't known.</summary>
    public Uri ReleasesPage { get; } = new($"https://github.com/{owner}/{repository}/releases");

    public async Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseEndpoint);
        // GitHub rejects API requests without a User-Agent.
        request.Headers.UserAgent.ParseAdd(userAgent);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, ct);
        }
        catch (HttpRequestException e)
        {
            throw new UpdateCheckException("GitHub couldn't be reached.", e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new UpdateCheckException("GitHub didn't answer in time.", e);
        }

        using (response)
        {
            // No release has been published yet.
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
                throw new UpdateCheckException($"GitHub returned status {(int)response.StatusCode}.");

            ReleaseDto? dto;
            try
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                dto = JsonSerializer.Deserialize<ReleaseDto>(body, JsonOptions);
            }
            catch (HttpRequestException e)
            {
                throw new UpdateCheckException("The connection to GitHub was lost.", e);
            }
            catch (JsonException e)
            {
                throw new UpdateCheckException("GitHub returned an unreadable release.", e);
            }

            if (dto is null || dto.Draft || dto.Prerelease)
                return null;

            if (!AppVersion.TryParse(dto.TagName, out var version))
                throw new UpdateCheckException($"The latest release's tag \"{dto.TagName}\" isn't a version.");

            return new ReleaseInfo(version, dto.TagName!, PageFor(dto.HtmlUrl));
        }
    }

    // The page is opened in the browser, so only ever a github.com address: anything else
    // (which the API shouldn't send) falls back to the repository's release list.
    private Uri PageFor(string? htmlUrl) =>
        Uri.TryCreate(htmlUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps && url.Host == "github.com"
            ? url
            : ReleasesPage;

    private sealed class ReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        public bool Draft { get; set; }

        public bool Prerelease { get; set; }
    }
}
