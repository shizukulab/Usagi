using System.Windows.Media;

namespace Usagi.App.Views;

// The carrot beside the creature: a gauge of the session, eaten down from the tip as the usage
// climbs, with a bite taken — a few frames of chewing — each time it shortens.
public sealed partial class MascotView
{
    private const int CarrotColumns = 44;  // the width with a carrot: the creature, a gap, the carrot and its leaves
    private const int CarrotLength = 12;   // blocks of carrot when none is eaten, each about 8% of the session
    private const int CarrotRight = 38;    // the column of its thick end, where the leaves start
    private const int CarrotMiddle = BodyTop + 13; // the row it's centered on: at its thickest it reaches the ground the creature stands on
    private const int ChewFrames = 6;

    private static readonly Brush CarrotFlesh = Frozen(Color.FromRgb(242, 140, 40));
    private static readonly Brush CarrotRing = Frozen(Color.FromRgb(201, 98, 10));
    private static readonly Brush CarrotCore = Frozen(Color.FromRgb(255, 197, 110));
    private static readonly Brush Leaf = Frozen(Color.FromRgb(88, 184, 78));
    private static readonly Brush LeafDark = Frozen(Color.FromRgb(47, 138, 58));

    private int _chewFramesLeft;

    /// <summary>The blocks of carrot left at a usage. Rounded up, so the last of it goes only when the session does.</summary>
    private static int CarrotLeft(double usage) => (int)Math.Ceiling((100 - Math.Clamp(usage, 0, 100)) / 100 * CarrotLength);

    // A bite is taken when the carrot gets shorter. Only while it's animated (and so seen): the
    // timer is what plays the chewing out.
    private void OnUsageChanged(double before, double now)
    {
        if (Carrot && _timer.IsEnabled && CarrotLeft(now) < CarrotLeft(before))
            _chewFramesLeft = ChewFrames;
        OnUsagePassed(before, now);
    }

    /// <summary>Moves the chewing of a bite on a frame; whether there was one being chewed.</summary>
    private bool AdvanceChewing()
    {
        if (_chewFramesLeft == 0)
            return false;
        _chewFramesLeft--;
        return true;
    }

    // The crumbs of a bite fly while it chews, but not over an act: that has the stage to itself.
    private void DrawCarrot(DrawingContext context) =>
        DrawCarrot(new Painter(context, 0, 0), CarrotLeft(Usage), _act is null ? _chewFramesLeft : 0);
    // The carrot lies with its leaves to the right and its tip towards the creature, and is eaten
    // from the tip. It tapers — five blocks thick by the leaves, then three, then a one-block tip —
    // about the row CarrotMiddle. What's eaten stays as a shadow.
    private static void DrawCarrot(Painter painter, int left, int chew)
    {
        for (var i = 0; i < CarrotLength; i++)
        {
            var x = CarrotRight - i;
            var thickness = CarrotThickness(i);
            DrawCarrotColumn(painter, i < left ? CarrotFlesh : Shadow, x, thickness);
            if (i < left && thickness >= 3 && i % 3 == 1)
                painter.DrawStill(CarrotRing, x, CarrotMiddle - thickness / 2, 1, 1);
        }
        // The bitten end shows its core, a block in from the skin (a one-block tip has none to show).
        if (left > 0 && left < CarrotLength && CarrotThickness(left - 1) is >= 3 and var bitten)
            DrawCarrotColumn(painter, CarrotCore, CarrotRight - (left - 1), bitten - 2);

        // Three fronds of leaf: one straight out, one up, one down.
        painter.DrawStill(LeafDark, CarrotRight + 1, CarrotMiddle, 3, 1);
        painter.DrawStill(Leaf, CarrotRight + 1, CarrotMiddle - 1, 1, 1);
        painter.DrawStill(Leaf, CarrotRight + 2, CarrotMiddle - 2, 2, 1);
        painter.DrawStill(Leaf, CarrotRight + 3, CarrotMiddle - 3, 1, 1);
        painter.DrawStill(Leaf, CarrotRight + 1, CarrotMiddle + 1, 1, 1);
        painter.DrawStill(Leaf, CarrotRight + 2, CarrotMiddle + 2, 2, 1);

        // Crumbs off the end just bitten.
        if (chew > 0)
        {
            var x = CarrotRight - left;
            painter.DrawStill(CarrotFlesh, x, CarrotMiddle - 4 - chew % 2, 1, 1);
            if (chew > 3)
                painter.DrawStill(CarrotFlesh, x - 2, CarrotMiddle - 3, 1, 1);
        }
    }

    /// <summary>How thick the carrot is <paramref name="i"/> blocks from its leaves: rounded at the shoulder, then tapering.</summary>
    private static int CarrotThickness(int i) => i switch
    {
        0 => 3,
        <= 3 => 5,
        <= 8 => 3,
        _ => 1
    };

    private static void DrawCarrotColumn(Painter painter, Brush brush, int x, int thickness) =>
        painter.DrawStill(brush, x, CarrotMiddle - thickness / 2, 1, thickness);
}
