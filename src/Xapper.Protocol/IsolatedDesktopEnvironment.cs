namespace Xapper.Protocol;

/// <summary>Names the launch marker shared by the server and every Inspector target framework.</summary>
public static class IsolatedDesktopEnvironment
{
    /// <summary>Exactly "1" means that real input and desktop pixels belong to a different desktop.</summary>
    public const string Variable = "XAPPER_ISOLATED";
}
