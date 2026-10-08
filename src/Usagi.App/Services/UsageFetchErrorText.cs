using Usagi.App.Localization;
using Usagi.Core.Usage;

namespace Usagi.App.Services;

/// <summary>Words a failed <see cref="UsageFetchResult"/> for the user, in the current UI language.</summary>
public static class UsageFetchErrorText
{
    /// <returns>The message, or null if <paramref name="result"/> carries no error.</returns>
    public static string? Describe(UsageFetchResult result) => result.Error switch
    {
        UsageFetchError.SignInRejected => SignInProblem(Loc.Get("Error_SignInRejected"), result),
        UsageFetchError.SignInExpired => Loc.Get("Error_SignInExpired"),
        UsageFetchError.DesktopSignInExpired => Loc.Get("Error_DesktopSignInExpired"),
        UsageFetchError.NotSignedIn => Loc.Get("Error_NotSignedIn"),
        UsageFetchError.ApiStatus => Loc.Format("Error_Status", result.ErrorStatusCode),
        UsageFetchError.MessagesApiFailed => Loc.Get("Error_MessagesApiFailed"),
        UsageFetchError.Network => Loc.Get("Error_Network"),
        _ => null
    };

    // Says how to fix it: a WSL-only setup has to sign in from inside WSL; anything else, in the desktop app.
    private static string SignInProblem(string problem, UsageFetchResult result)
        => result.WslLocationName is { } wsl
            ? Loc.Format("Error_SignInInWsl", problem, wsl)
            : Loc.Format("Error_SignInInDesktop", problem);
}
