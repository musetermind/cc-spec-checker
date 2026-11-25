namespace SpecChecker.App;

/// <summary>
/// The shop branding: every shop name, contact line and link the
/// window shows, in one place. The CC build is the default. A branded
/// build for another shop swaps these values at build time — the file
/// is NOT committed with a customer's details; the public repo keeps
/// the CC defaults. Nothing else changes: the engine, the cards and
/// the tool strip are identical.
/// NOTE: the two title strings carry the version by hand. On a version
/// bump, update them here as well as AppInfo.Version and the .csproj
/// &lt;Version&gt; values.
/// </summary>
public static class Branding
{
    /// <summary>The app name as the user sees it.</summary>
    public const string AppName = "CC Spec Checker";

    /// <summary>The window title.</summary>
    public const string WindowTitle = "CC Spec Checker v2.2 — Computer Corner";

    /// <summary>The header title.</summary>
    public const string HeaderTitle = "CC SPEC CHECKER v2.2";

    /// <summary>The shop name in the header credit.</summary>
    public const string ShopName = "CC Computer Corner";

    /// <summary>The text after the shop name in the credit line.</summary>
    public const string CreditSuffix = " (Yangon, Myanmar)";

    /// <summary>The contact line under the credit.</summary>
    public const string ContactLine = "Contact - ccpcbuilding@gmail.com";

    /// <summary>A second contact line under the contact line, e.g. the
    /// shop's email. Empty for the CC build (no line shown).</summary>
    public const string EmailLine = "";

    /// <summary>The shop's Facebook page, opened when the shop name is
    /// clicked. Null when the shop has no page linked — the name then
    /// shows as plain text, not a button. Static (not const) so the
    /// null checks that branch on it stay reachable.</summary>
    public static readonly string? FacebookUrl = "https://www.facebook.com/ccpcbuilder";

    /// <summary>A small centred credit at the very bottom of the
    /// window. Empty for the CC build (no credit line shown).</summary>
    public const string CreditLine = "";
}
