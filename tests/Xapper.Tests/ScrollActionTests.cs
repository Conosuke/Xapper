using System.Windows;
using System.Windows.Controls;
using Xapper.Inspector.Actions;

namespace Xapper.Tests;

/// <summary>Empty scroll ranges must return an actionable failure rather than a provider exception.</summary>
public sealed class ScrollActionTests
{
    [Theory]
    [InlineData(-1, 50, "height")]
    [InlineData(50, -1, "width")]
    public void EmptyRange_ExplainsTheRequestedAxis(double horizontal, double vertical, string dimension)
    {
        StaThread.Run(() =>
        {
            var viewer = new ScrollViewer { Content = new TextBlock { Text = "fits" } };
            viewer.Measure(new Size(300, 300));
            viewer.Arrange(new Rect(0, 0, 300, 300));
            var outcome = ScrollAction.Execute(viewer, horizontal, vertical);
            Assert.NotNull(outcome.Error);
            Assert.Contains(dimension, outcome.Error);
            Assert.Contains("0", outcome.Error);
        });
    }

    [Fact]
    public void UnchangedAxes_DoNotRequireAScrollableRange()
    {
        StaThread.Run(() => Assert.Null(ScrollAction.Execute(new ScrollViewer(), -1, -1).Error));
    }

    [Fact]
    public void VerticalScroll_LeavesAnUnavailableHorizontalAxisUnchanged()
    {
        StaThread.Run(() =>
        {
            var viewer = new ScrollViewer { Content = new Border { Height = 1000, Width = 50 }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            viewer.Measure(new Size(200, 100));
            viewer.Arrange(new Rect(0, 0, 200, 100));
            viewer.UpdateLayout();
            Assert.True(viewer.ScrollableHeight > 0);
            Assert.Null(ScrollAction.Execute(viewer, -1, 50).Error);
        });
    }
}
