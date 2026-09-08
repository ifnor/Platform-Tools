using PlatformTools.App.Services;

namespace PlatformTools.Tests;

public sealed class LocalizationServiceTests
{
    [Fact]
    public void LocalizationDictionary_CanBeInitialized()
    {
        var translated = LocalizationService.T("首页", "Home");

        Assert.Equal("首页", translated);
    }
}
