using Xapper.Protocol;

namespace Xapper.Inspector;

/// <summary>Prevents isolated applications from sending input to or reading the person's desktop.</summary>
internal static class IsolatedMode
{
    #region Properties

    /// <summary>Captured at Inspector initialization; tests restore this value after changing it.</summary>
    public static bool IsOn { get; internal set; } = EnabledBy(Environment.GetEnvironmentVariable(IsolatedDesktopEnvironment.Variable));

    #endregion

    #region Policy

    /// <summary>Only the explicit launch marker enables isolation.</summary>
    internal static bool EnabledBy(string? value) => value == "1";

    /// <summary>Explains why falling back to real input would not reach the application.</summary>
    public static string? RefuseRealInput(string gesture) => !IsOn ? null :
        $"Cannot perform the {gesture} using real mouse input on an isolated desktop. The in-process synthetic " +
        "path was unavailable (the input hook could not be installed, or another window of this app covers " +
        "the point). Real mouse input goes to the person's desktop, so no real input was sent. Choose a point " +
        "not covered by a popup or dialog, or use select, toggle, type, key, or scroll instead.";

    /// <summary>Offers rendering rather than unrelated pixels from the person's desktop.</summary>
    public static string? RefuseScreenCapture() => !IsOn ? null :
        "Screen capture is unavailable on an isolated desktop: screen mode reads the person's desktop pixels, " +
        "where this app is not displayed. Use mode=\"render\" to redraw the app; find and snapshot can read its " +
        "separate popups and menus.";

    /// <summary>Explains that isolated interaction does not require foreground activation.</summary>
    public static string? RefuseForeground() => !IsOn ? null :
        "Cannot bring an app on an isolated desktop to the person's foreground. This app is not on that " +
        "desktop, and in-process interaction works without bringing it to the front.";

    #endregion
}
