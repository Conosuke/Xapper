using Xapper.Inspector;

namespace Xapper.Tests;

/// <summary>Isolation is opt-in and must explain unsupported actions before sending input.</summary>
[Collection(DesktopWindowCollection.Name)]
public sealed class IsolatedModeTests
{
    #region Tests

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("true", false)]
    [InlineData("01", false)]
    [InlineData(" 1 ", false)]
    [InlineData("1", true)]
    public void EnabledBy_RequiresExactOne(string? value, bool expected)
    {
        Assert.Equal(expected, IsolatedMode.EnabledBy(value));
    }

    [Fact]
    public void Refusals_OnlyApplyInIsolation()
    {
        var previous = IsolatedMode.IsOn;
        try
        {
            IsolatedMode.IsOn = false;
            Assert.Null(IsolatedMode.RefuseRealInput("wheel"));
            Assert.Null(IsolatedMode.RefuseScreenCapture());
            Assert.Null(IsolatedMode.RefuseForeground());
            IsolatedMode.IsOn = true;
            Assert.Contains("isolated desktop", IsolatedMode.RefuseRealInput("wheel"));
            Assert.Contains("wheel", IsolatedMode.RefuseRealInput("wheel"));
            Assert.Contains("mode=\"render\"", IsolatedMode.RefuseScreenCapture());
            Assert.Contains("isolated desktop", IsolatedMode.RefuseForeground());
        }
        finally { IsolatedMode.IsOn = previous; }
    }

    #endregion
}
