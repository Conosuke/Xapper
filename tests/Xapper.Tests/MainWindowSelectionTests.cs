using System.Windows;
using System.Windows.Interop;
using Xapper.Inspector.NativeWindows;

namespace Xapper.Tests;

/// <summary>An untitled decoration HWND must not win default window targeting.</summary>
[Collection(DesktopWindowCollection.Name)]
public sealed class MainWindowSelectionTests
{
    [Fact]
    public void Main_FallbackIgnoresAnUntitledDecorationWindow()
    {
        StaThread.Run(() =>
        {
            using var glow = new HwndSource(new HwndSourceParameters("")
            {
                Width = 120, Height = 100, WindowStyle = 0x10000000 // WS_VISIBLE, no caption
            });
            var main = new Window { Title = "Xapper main selection", Width = 200, Height = 100, ShowActivated = false };
            try
            {
                main.Show();
                var expected = new WindowInteropHelper(main).Handle;
                Assert.Equal(expected, ProcessWindows.Main());
                Assert.NotEqual(glow.Handle, ProcessWindows.Main());
            }
            finally { main.Close(); }
        });
    }
}
