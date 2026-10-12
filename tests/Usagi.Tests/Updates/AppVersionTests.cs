using Usagi.Core.Updates;

namespace Usagi.Tests.Updates;

public class AppVersionTests
{
    private static AppVersion Parse(string text)
    {
        Assert.True(AppVersion.TryParse(text, out var version), $"\"{text}\" should parse");
        return version;
    }

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3, null)]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("V10.0", 10, 0, 0, null)]
    [InlineData(" v1.2.3 ", 1, 2, 3, null)]
    [InlineData("1.3.0-beta.1", 1, 3, 0, "beta.1")]
    [InlineData("1.2.3+abc123", 1, 2, 3, null)]
    [InlineData("1.2.3-rc.2+abc123", 1, 2, 3, "rc.2")]
    public void TryParse_ReadsTagsAndInformationalVersions(string text, int major, int minor, int patch, string? preRelease)
    {
        Assert.Equal(new AppVersion(major, minor, patch, preRelease), Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("1")]
    [InlineData("1.2.3.4")]
    [InlineData("1.x.3")]
    [InlineData("1.2.3-")]
    [InlineData("latest")]
    [InlineData("-1.2.3")]
    public void TryParse_RejectsWhatIsNotAVersion(string? text)
    {
        Assert.False(AppVersion.TryParse(text, out _));
    }

    [Theory]
    [InlineData("1.0.1", "1.0.0")]
    [InlineData("1.1.0", "1.0.9")]
    [InlineData("2.0.0", "1.99.99")]
    [InlineData("1.10.0", "1.9.0")]
    [InlineData("1.0.0", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.1", "1.0.0-beta.2")]
    [InlineData("1.0.0-beta.11", "1.0.0-beta.2")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta")]
    [InlineData("1.0.0-beta", "1.0.0-1")]
    public void Compare_OrdersTheSemanticVersioningWay(string newer, string older)
    {
        Assert.True(Parse(newer) > Parse(older));
        Assert.True(Parse(older) < Parse(newer));
    }

    [Fact]
    public void Compare_IgnoresTheVPrefixAndBuildMetadata()
    {
        Assert.Equal(0, Parse("v1.2.3").CompareTo(Parse("1.2.3+sha")));
        Assert.True(Parse("1.2") >= Parse("1.2.0"));
    }

    [Fact]
    public void ToString_IsTheVersionWithoutPrefix()
    {
        Assert.Equal("1.2.0", Parse("v1.2").ToString());
        Assert.Equal("1.3.0-beta.1", Parse("v1.3.0-beta.1+sha").ToString());
    }
}
