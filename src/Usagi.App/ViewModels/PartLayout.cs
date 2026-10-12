using System.Windows;
using Usagi.App.Settings;

namespace Usagi.App.ViewModels;

/// <summary>
/// Where a row's parts go on one side of the creature: each part's column among those shown on
/// this side, in the user's order, or none (-1) if it isn't shown here.
/// </summary>
public sealed record PartColumns(int Label, int Bar, int Percentage, int ResetTime)
{
    public static readonly PartColumns None = new(-1, -1, -1, -1);

    public bool ShowLabel => Label >= 0;
    public bool ShowBar => Bar >= 0;
    public bool ShowPercentage => Percentage >= 0;
    public bool ShowResetTime => ResetTime >= 0;

    /// <summary>Whether this side has any of the parts: a block of rows of its own.</summary>
    public bool Any => ShowLabel || ShowBar || ShowPercentage || ShowResetTime;

    // For Grid.Column, which can't be -1 (a part that isn't shown is collapsed anyway).
    public int LabelColumn => Math.Max(Label, 0);
    public int BarColumn => Math.Max(Bar, 0);
    public int PercentageColumn => Math.Max(Percentage, 0);
    public int ResetTimeColumn => Math.Max(ResetTime, 0);
}

/// <summary>
/// How a place (the compact flyout, the taskbar bars) lays out what it shows: the parts before the
/// creature, the creature, and the parts after it — each side a block of rows with the parts in
/// the user's order. Without the creature, all of them are on one side.
/// </summary>
public sealed record PartLayout(PartColumns Left, bool ShowMascot, PartColumns Right)
{
    private const double MascotGap = 8;

    /// <summary>The layout from before there was a choice: the creature, then every part.</summary>
    public static readonly PartLayout Default = From(AppSettings.DefaultPartOrder, _ => true);

    /// <summary>Whether there are rows at all: not with the creature by itself.</summary>
    public bool ShowsRows => Left.Any || Right.Any;

    /// <summary>The creature's gap to the rows, on whichever sides they are.</summary>
    public Thickness MascotMargin => new(Left.Any ? MascotGap : 0, 0, Right.Any ? MascotGap : 0, 0);

    public static PartLayout From(IEnumerable<DisplayPart> order, Func<DisplayPart, bool> shown)
    {
        var left = new List<DisplayPart>();
        var right = new List<DisplayPart>();
        var mascot = false;
        foreach (var part in order.Where(shown))
        {
            if (part == DisplayPart.Mascot)
                mascot = true;
            else
                (mascot ? right : left).Add(part);
        }
        return new PartLayout(Columns(left), mascot, Columns(right));
    }

    public static PartLayout ForFlyout(AppSettings settings) => From(settings.EffectiveFlyoutPartOrder, part => part switch
    {
        DisplayPart.Mascot => settings.FlyoutShowMascot,
        DisplayPart.Labels => settings.CompactShowLabels,
        DisplayPart.Bar => settings.EffectiveCompactShowBar,
        DisplayPart.Percentage => settings.CompactShowPercentage,
        _ => settings.CompactShowResetTime
    });

    public static PartLayout ForTaskbarBar(AppSettings settings) => From(settings.EffectiveTaskbarBarPartOrder, part => part switch
    {
        DisplayPart.Mascot => settings.TaskbarBarShowMascot,
        DisplayPart.Labels => settings.TaskbarBarShowLabels,
        DisplayPart.Bar => settings.EffectiveTaskbarBarShowBar,
        DisplayPart.Percentage => settings.TaskbarBarShowPercentage,
        _ => settings.TaskbarBarShowResetTime
    });

    private static PartColumns Columns(List<DisplayPart> parts) => new(
        parts.IndexOf(DisplayPart.Labels), parts.IndexOf(DisplayPart.Bar),
        parts.IndexOf(DisplayPart.Percentage), parts.IndexOf(DisplayPart.ResetTime));
}
