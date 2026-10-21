using System.Runtime.InteropServices;
using System.Windows.Input;
using Xapper.Inspector.Actions;

namespace Xapper.Tests;

/// <summary>Pins WPF text filtering, focus transitions, system keys, and transient pressed-key state.</summary>
[Collection(DesktopWindowCollection.Name)]
public sealed class KeyboardCompositionTests
{
    #region Tests

    [Theory]
    [InlineData("Down", ModifierKeys.Alt, true)]
    [InlineData("Down", ModifierKeys.Alt | ModifierKeys.Control, false)]
    [InlineData("F10", ModifierKeys.None, true)]
    [InlineData("F10", ModifierKeys.Control, false)]
    public void Send_UsesSystemKeysOnlyForWindowsSystemCombinations(string key, ModifierKeys modifiers, bool system)
    {
        OffscreenTextBoxes.Run(box =>
        {
            KeyEventArgs? seen = null;
            box.PreviewKeyDown += (_, e) => seen = e;
            var result = KeyboardInput.Send(null, key, modifiers);
            Assert.Null(result.Error);
            Assert.NotNull(seen);
            Assert.Equal(system ? Key.System : Enum.Parse<Key>(key), seen.Key);
            if (system) Assert.Equal(Enum.Parse<Key>(key), seen.SystemKey);
        });
    }

    [Fact]
    public void TypeText_RespectsPreviewTextInputFiltering()
    {
        OffscreenTextBoxes.Run(box =>
        {
            box.PreviewTextInput += (_, e) => e.Handled = e.Text.Any(char.IsLetter);
            var result = KeyboardInput.TypeText(null, "12a-3");
            Assert.Null(result.Error);
            Assert.Equal("12-3", box.Text);
        });
    }

    [Fact]
    public void HandledKeyDown_DoesNotInjectItsFollowingCharacter()
    {
        OffscreenTextBoxes.Run(box =>
        {
            box.PreviewKeyDown += (_, e) => e.Handled = true;
            var result = KeyboardInput.Send(null, UnmodifiedCharacter().ToString(), ModifierKeys.None);
            Assert.Null(result.Error);
            Assert.Equal("", box.Text);
        });
    }

    [Fact]
    public void KeyDownFocusChange_RetargetsFollowingTextAndSubsequentStrokes()
    {
        OffscreenTextBoxes.Run((box, editor) =>
        {
            box.PreviewKeyDown += (_, _) => editor.Focus();
            var text = new string(UnmodifiedCharacter(), 2);
            var result = KeyboardInput.TypeText(null, text);
            Assert.Null(result.Error);
            Assert.Equal("", box.Text);
            Assert.Equal(text, editor.Text);
        });
    }

    [Theory]
    [InlineData("_")]
    [InlineData("한")]
    public void CharactersRequiringModifiers_DoNotEmitAMisleadingBaseKey(string text)
    {
        OffscreenTextBoxes.Run(box =>
        {
            var downs = new List<Key>();
            box.PreviewKeyDown += (_, e) => downs.Add(e.Key);
            var result = KeyboardInput.TypeText(null, text);
            Assert.Null(result.Error);
            Assert.Equal(text, box.Text);
            var mapped = VkKeyScanW(text[0]);
            if (mapped == -1 || (mapped & 0xff00) != 0)
                Assert.Empty(downs);
        });
    }

    [Fact]
    public void KeyState_IsDownDuringKeyDown_AndReleasedBeforeKeyUp()
    {
        OffscreenTextBoxes.Run(box =>
        {
            Assert.True(InputSpoof.EnsureInstalled());
            bool? down = null;
            bool? up = null;
            box.PreviewKeyDown += (_, e) => down = e.IsDown;
            box.PreviewKeyUp += (_, e) => up = e.IsDown;
            Assert.Null(KeyboardInput.Send(null, "F24", ModifierKeys.None).Error);
            Assert.Equal(true, down);
            Assert.Equal(false, up);
        });
    }

    [Fact]
    public void KeyState_IsScopedToTheInjectingThread_AndCanBeReleased()
    {
        Assert.True(InputSpoof.EnsureInstalled());
        const int key = 0x87; // F24, unlikely to be physically held on a test machine.
        var real = GetKeyState(key);
        try
        {
            InputSpoof.BeginKey(key);
            Assert.True(GetKeyState(key) < 0);
            short? otherThread = null;
            var thread = new Thread(() => otherThread = GetKeyState(key));
            thread.Start();
            Assert.True(thread.Join(5000));
            Assert.Equal(real < 0, otherThread < 0);
        }
        finally { InputSpoof.EndKey(); }
        Assert.Equal(real < 0, GetKeyState(key) < 0);
    }

    [Fact]
    public void ThrowingKeyHandler_DoesNotLeaveTheInjectedKeyPressed()
    {
        OffscreenTextBoxes.Run(box =>
        {
            Assert.True(InputSpoof.EnsureInstalled());
            var before = GetKeyState(0x87);
            box.PreviewKeyDown += (_, _) => throw new InvalidOperationException("test handler");
            Assert.Throws<InvalidOperationException>(() => KeyboardInput.Send(null, "F24", ModifierKeys.None));
            Assert.Equal(before < 0, GetKeyState(0x87) < 0);
        });
    }

    #endregion

    #region Layout Helpers

    /// <summary>Text/key coupling requires an actual unmodified mapping in the active Windows layout.</summary>
    private static char UnmodifiedCharacter()
    {
        foreach (var character in "abcdefghijklmnopqrstuvwxyz0123456789.,-;")
        {
            var mapped = VkKeyScanW(character);
            if (mapped != -1 && (mapped & 0xff00) == 0) return character;
        }
        throw new InvalidOperationException("The current keyboard layout exposes no unmodified printable character for this input test.");
    }

    #endregion

    #region Interop

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern short VkKeyScanW(char character);
    [DllImport("user32.dll")]
    private static extern short GetKeyState(int key);

    #endregion
}
