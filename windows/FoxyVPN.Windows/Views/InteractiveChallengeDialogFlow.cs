using System.Windows;
using FoxyVPN.Windows.Services;

namespace FoxyVPN.Windows.Views;

public sealed record ChallengeImportResult(bool UserCompleted, int ImportedCookies);

/// <summary>One explicit, user-mediated challenge interaction followed by domain-scoped cookie import.</summary>
public static class InteractiveChallengeDialogFlow
{
    public static ChallengeImportResult ShowAndImport(
        Window? owner,
        Uri origin,
        Func<Uri, IReadOnlyList<InteractiveChallengeCookie>, int> importer)
    {
        ArgumentNullException.ThrowIfNull(importer);
        var dialog = new InteractiveChallengeWindow(origin);
        if (owner is not null) dialog.Owner = owner;
        if (dialog.ShowDialog() != true) return new ChallengeImportResult(false, 0);
        var count = importer(origin, dialog.CapturedCookies);
        return new ChallengeImportResult(true, count);
    }
}
