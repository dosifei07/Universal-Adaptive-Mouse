using UAM.Desktop.Shared.Formatting;

namespace UAM.Desktop.Tests;

public sealed class UamFormatTests
{
    [Fact]
    public void Duration_UnderAnHour_ShowsMinutesAndSeconds()
    {
        Assert.Equal("05:09", UamFormat.Duration(TimeSpan.FromSeconds(309)));
    }

    [Fact]
    public void Duration_OverAnHour_ShowsHours()
    {
        Assert.Equal("1:02:03", UamFormat.Duration(new TimeSpan(1, 2, 3)));
    }

    [Fact]
    public void Duration_NegativeIsClampedToZero()
    {
        Assert.Equal("00:00", UamFormat.Duration(TimeSpan.FromSeconds(-5)));
    }
}
