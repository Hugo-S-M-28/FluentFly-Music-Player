using FluentFlyout.Classes.Utils;
using System.Windows.Media;
using Xunit;

namespace FluentFlyout.Tests;

public class BitmapHelperColorExtractionTests
{
    [Fact]
    public void ExtractRepresentativeColorForTests_PrefersSaturatedAccentOverLargeGrayArea()
    {
        var color = BitmapHelper.ExtractRepresentativeColorForTests(
            (240, 240, 240, 0.9),
            (235, 235, 235, 0.9),
            (220, 40, 70, 1.8),
            (210, 45, 78, 1.8),
            (205, 50, 82, 1.6));

        Assert.True(color.R > color.G);
        Assert.True(color.R > color.B);
        Assert.True(color.G < 120);
    }

    [Fact]
    public void ExtractRepresentativeColorForTests_IgnoresNeutralWhenVividBlueExists()
    {
        var color = BitmapHelper.ExtractRepresentativeColorForTests(
            (70, 70, 70, 1.2),
            (80, 80, 80, 1.2),
            (40, 120, 220, 1.7),
            (45, 125, 225, 1.7));

        Assert.True(color.B > color.R);
        Assert.True(color.B > color.G);
    }
}
