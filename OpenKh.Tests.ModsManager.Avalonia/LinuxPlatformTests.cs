using OpenKh.Tools.ModsManager.Services;
using Xunit;

namespace OpenKh.Tests.ModsManager.Avalonia;

public class LinuxPlatformTests
{
    [Fact]
    public void WindowsOnlyCapabilitiesAreDisabled()
    {
        if (!OperatingSystem.IsLinux())
            return;

        Assert.False(PlatformCapabilities.SupportsPcsx2Injection);
        Assert.False(PlatformCapabilities.SupportsPanacea);
        Assert.False(PlatformCapabilities.SupportsEpicGamesStore);
        Assert.False(PlatformCapabilities.SupportsSelfUpdate);
    }

    [Theory]
    [InlineData("/home/openkh/mods", "Z:\\home\\openkh\\mods")]
    [InlineData("/tmp/openkh", "Z:\\tmp\\openkh")]
    public void GamePathsUseWineZDriveOnLinux(string linuxPath, string expected)
    {
        if (!OperatingSystem.IsLinux())
            return;

        Assert.Equal(expected, WinePathUtil.ToGamePath(linuxPath));
    }

    [Fact]
    public void ForwardSlashGamePathsAreTomlFriendlyOnLinux()
    {
        if (!OperatingSystem.IsLinux())
            return;

        Assert.Equal("Z:/home/openkh/mods", WinePathUtil.ToGamePathForwardSlashes("/home/openkh/mods"));
    }
}
