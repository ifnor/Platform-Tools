using PlatformTools.App.Models;

namespace PlatformTools.Tests;

public sealed class ShareProfileTests
{
    [Fact]
    public void ShareCode_RoundTripsAllConnectionFields()
    {
        var expected = new ShareProfile(1, "office-ssh", "ssh", "ssh.example.com", 2222, true);
        var actual = ShareProfile.Parse(expected.ToShareCode());
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void JsonFile_IsAcceptedAsPtLinkPayload()
    {
        const string json = """{"Version":1,"Name":"demo","Protocol":"rdp","Hostname":"rdp.example.com","SuggestedLocalPort":3390,"RequiresAccess":true}""";
        var profile = ShareProfile.Parse(json);
        Assert.Equal("rdp", profile.Protocol);
        Assert.Equal(3390, profile.SuggestedLocalPort);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-share-code")]
    public void InvalidPayload_IsRejected(string payload) => Assert.ThrowsAny<Exception>(() => ShareProfile.Parse(payload));

    [Fact]
    public void UnsupportedProtocol_IsRejected()
    {
        const string json = """{"Version":1,"Name":"bad","Protocol":"file","Hostname":"file:///etc/passwd","SuggestedLocalPort":80,"RequiresAccess":false}""";
        Assert.Throws<FormatException>(() => ShareProfile.Parse(json));
    }

    [Fact]
    public void InvalidPort_IsRejected()
    {
        const string json = """{"Version":1,"Name":"bad","Protocol":"tcp","Hostname":"db.example.com","SuggestedLocalPort":70000,"RequiresAccess":false}""";
        Assert.Throws<FormatException>(() => ShareProfile.Parse(json));
    }
}
