using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Xapper.Inspector.Actions;

namespace Xapper.Tests;

/// <summary>Coordinate-free Save must commit edits in the same order as an ordinary mouse click.</summary>
[Collection(DesktopWindowCollection.Name)]
public sealed class ButtonFocusTests
{
    #region Tests

    [Fact]
    public void EventClick_CommitsThePreviouslyFocusedEditorBeforeSaving()
    {
        StaThread.Run(() =>
        {
            var editor = new TextBox();
            var save = new Button { Content = "Save" };
            var panel = new StackPanel();
            panel.Children.Add(editor);
            panel.Children.Add(save);
            var committed = "old";
            string? saved = null;
            editor.LostKeyboardFocus += (_, _) => committed = editor.Text;
            save.Click += (_, _) => saved = committed;
            var window = new Window { Content = panel, Width = 200, Height = 120, ShowActivated = false };
            try
            {
                window.Show();
                editor.Focus();
                Assert.Same(editor, Keyboard.FocusedElement);
                editor.Text = "new";
                var outcome = ClickAction.Execute(save);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Assert.Null(outcome.Error);
                Assert.Equal("new", saved);
                Assert.Same(save, Keyboard.FocusedElement);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void HoverClick_DoesNotMoveKeyboardFocus()
    {
        StaThread.Run(() =>
        {
            var button = new Button { ClickMode = ClickMode.Hover };
            var focused = false;
            button.GotKeyboardFocus += (_, _) => focused = true;
            ClickAction.Execute(button);
            Assert.False(focused);
        });
    }

    #endregion
}
