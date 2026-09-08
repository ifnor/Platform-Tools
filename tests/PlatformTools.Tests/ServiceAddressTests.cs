using PlatformTools.App.Models;

namespace PlatformTools.Tests;

public sealed class ServiceAddressTests
{
    [Theory]
    [InlineData("8080", "http", "http://localhost:8080")]
    [InlineData("localhost:22", "ssh", "ssh://localhost:22")]
    [InlineData("rdp://127.0.0.1:3389", "rdp", "rdp://127.0.0.1:3389")]
    [InlineData("tcp://192.168.1.9:3306", "tcp", "tcp://192.168.1.9:3306")]
    public void ValidServiceAddress_IsNormalized(string value, string protocol, string expected) =>
        Assert.Equal(expected, ServiceAddress.Normalize(value, protocol));

    [Theory]
    [InlineData("http://localhost:8080", "ssh")]
    [InlineData("70000", "tcp")]
    [InlineData("not a host", "http")]
    public void InvalidOrMismatchedServiceAddress_IsRejected(string value, string protocol) =>
        Assert.Null(ServiceAddress.Normalize(value, protocol));
}
