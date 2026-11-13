using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public class AppInfoTests
{
    [Fact]
    public void NameIsStable()
    {
        Assert.Equal("DeskSnip", AppInfo.Name);
    }

    [Fact]
    public void VersionIsSemver()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+", AppInfo.Version);
    }
}
