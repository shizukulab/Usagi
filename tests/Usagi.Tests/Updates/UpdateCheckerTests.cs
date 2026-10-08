using Usagi.Core.Updates;

namespace Usagi.Tests.Updates;

public class UpdateCheckerTests
{
    private static readonly AppVersion Current = new(1, 2, 0);

    private sealed class Source(Func<ReleaseInfo?> latest) : IReleaseSource
    {
        public Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken ct = default) => Task.FromResult(latest());
    }

    private static ReleaseInfo Release(int major, int minor, int patch) =>
        new(new AppVersion(major, minor, patch), $"v{major}.{minor}.{patch}", new Uri($"https://github.com/o/r/releases/tag/v{major}.{minor}.{patch}"));

    [Fact]
    public async Task CheckAsync_ReportsANewerRelease()
    {
        var checker = new UpdateChecker(new Source(() => Release(1, 3, 0)), Current);

        var result = await checker.CheckAsync();

        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, result.Outcome);
        Assert.Equal(new AppVersion(1, 3, 0), result.Release!.Version);
    }

    [Theory]
    [InlineData(1, 2, 0)]
    [InlineData(1, 1, 9)]
    public async Task CheckAsync_IsUpToDateWhenTheLatestIsNotNewer(int major, int minor, int patch)
    {
        var checker = new UpdateChecker(new Source(() => Release(major, minor, patch)), Current);

        var result = await checker.CheckAsync();

        Assert.Equal(UpdateCheckOutcome.UpToDate, result.Outcome);
        Assert.Null(result.Release);
    }

    [Fact]
    public async Task CheckAsync_IsUpToDateWhenNothingIsReleased()
    {
        var checker = new UpdateChecker(new Source(() => null), Current);

        Assert.Equal(UpdateCheckOutcome.UpToDate, (await checker.CheckAsync()).Outcome);
    }

    [Fact]
    public async Task CheckAsync_ReportsAFailure()
    {
        var checker = new UpdateChecker(new Source(() => throw new UpdateCheckException("offline")), Current);

        var result = await checker.CheckAsync();

        Assert.Equal(UpdateCheckOutcome.Failed, result.Outcome);
        Assert.Equal("offline", result.Error);
    }
}
