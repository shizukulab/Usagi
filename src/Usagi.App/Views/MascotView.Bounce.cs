using System.Windows.Media;

namespace Usagi.App.Views;

// The creature drawn in a pose of its own rather than its mood's: its body squashed or stretched,
// each arm placed, its eyes open or shut, its mouth, and what's on its head. Its feet stay on the
// ground (but in the air): squashed, it's wider and lower on bent legs; stretched, narrower and
// taller. The acts that bounce — a hop with a springy landing, a "banzai" — are drawn with it.
public sealed partial class MascotView
{
    // Its body as it stands (see DrawBody): 18 wide and 12 tall on legs 4 tall, from column 3.
    private const int StandWidth = 18;
    private const int StandHeight = 12;
    private const int StandLegs = 4;
    private const int Ground = BodyTop + StandHeight + StandLegs; // the row under its feet

    // An arm bent across its chest, a shade darker than its body so that it reads in front of it.
    private static readonly Brush Forearm = Frozen(Color.FromRgb(169, 82, 58));

    private enum ArmPose { Side, Raised, Up, Front }

    private enum Mouth { None, Small, Open }

    private enum Headwear { Ears, Nightcap }

    /// <summary>A shape for its body: as wide and tall as it is, on legs so tall, set so far apart.</summary>
    private readonly record struct Shape(int Width = StandWidth, int Height = StandHeight, int Legs = StandLegs, int Spread = 0, int EyeHeight = 2);

    private static readonly Shape Standing = new(StandWidth, StandHeight, StandLegs); // (not new(): on a struct that skips the defaults)
    private static readonly Shape Squashed = new(Width: 20, Height: 10, Legs: 2, Spread: 1);
    private static readonly Shape Settling = new(Width: 19, Height: 11, Legs: 3);
    private static readonly Shape Stretched = new(Width: 16, Height: 13, EyeHeight: 3);
    private static readonly Shape Crouched = new(Width: StandWidth, Height: StandHeight, Legs: 1, Spread: 1);

    // Up, a squash on landing that springs it up tall, and a smaller one as it settles.
    private static void DrawBounce(Stage stage, int f)
    {
        var (shape, lift) = f switch
        {
            0 => (Settling, 0),     // crouches to spring
            1 => (Stretched, -1),   // off the ground
            2 => (Standing, -2),
            3 => (Standing, -2),
            4 => (Standing, -1),
            5 => (Squashed, 0),     // lands
            6 => (Stretched, 0),    // and springs up tall
            7 => (Stretched, 0),
            8 => (Standing, 0),
            9 => (Settling, 0),     // a smaller give
            _ => (Standing, 0)
        };
        DrawPose(stage, shape, lift, ArmPose.Side);
    }

    // Both arms up past its head, two little bounces, and down with a squash.
    private static void DrawBanzai(Stage stage, int f)
    {
        var (shape, lift, arms) = f switch
        {
            < 2 => (Standing, 0, ArmPose.Side),
            2 => (Standing, 0, ArmPose.Raised),
            3 or 5 or 7 or 8 or 9 => (Standing, 0, ArmPose.Up),
            4 or 6 => (Standing, -1, ArmPose.Up),
            10 => (Standing, 0, ArmPose.Raised),
            11 => (Squashed, 0, ArmPose.Side),
            _ => (Standing, 0, ArmPose.Side)
        };
        DrawPose(stage, shape, lift, arms);
    }

    /// <summary>Both arms the same way.</summary>
    private static (int X, int Top) DrawPose(Stage stage, Shape shape, int lift, ArmPose arms,
        bool eyesShut = false, Mouth mouth = Mouth.None, Headwear head = Headwear.Ears) =>
        DrawPose(stage, shape, lift, arms, arms, eyesShut, mouth, head);

    /// <summary>
    /// The creature in a pose, centered where it stands, <paramref name="lift"/> rows off the
    /// ground. Where its body ended up — its left edge and top row, before the lift — for what it
    /// holds or wears to be placed by.
    /// </summary>
    private static (int X, int Top) DrawPose(Stage stage, Shape shape, int lift, ArmPose left, ArmPose right,
        bool eyesShut = false, Mouth mouth = Mouth.None, Headwear head = Headwear.Ears)
    {
        var painter = stage.Painter(0, lift);
        var x = 3 + (StandWidth - shape.Width) / 2;
        var bottom = Ground - shape.Legs;
        var top = bottom - shape.Height;

        foreach (var (at, outward) in ((int, int)[])[(0, -1), (5, -1), (shape.Width - 7, 1), (shape.Width - 2, 1)])
            painter.DrawFlesh(Body, x + at + outward * shape.Spread, bottom, 2, shape.Legs); // legs: a pair under each side
        DrawArm(painter, left, x - 3, top);
        DrawArm(painter, right, x + shape.Width, top);
        painter.DrawFlesh(Body, x, top, shape.Width, shape.Height);
        if (left == ArmPose.Front)
            painter.Draw(Forearm, x, top + 8, 6, 2);
        if (right == ArmPose.Front)
            painter.Draw(Forearm, x + shape.Width - 6, top + 8, 6, 2);

        if (head == Headwear.Nightcap)
            DrawNightcap(painter, x, top, shape.Width);
        else if (stage.Ears)
            DrawEars(painter, 0, top + lift);

        foreach (var eye in (int[])[x + 3, x + shape.Width - 5])
        {
            if (eyesShut)
                painter.Draw(Dark, eye, top + 3, 2, 1);
            else
                painter.Draw(Dark, eye, top + 2, 2, shape.EyeHeight);
        }
        if (mouth != Mouth.None)
            painter.Draw(Dark, x + shape.Width / 2 - 1, top + 5, 2, mouth == Mouth.Open ? 2 : 1);
        return (x, top);
    }

    private static void DrawArm(Painter painter, ArmPose pose, int x, int top)
    {
        switch (pose)
        {
            case ArmPose.Side:
                painter.DrawFlesh(Body, x, top + 3, 3, 5);
                break;
            case ArmPose.Raised: // on the way up, level with its head
                painter.DrawFlesh(Body, x, top - 1, 3, 4);
                break;
            case ArmPose.Up: // turned straight up, from beside its eyes to past the top of its head
                painter.DrawFlesh(Body, x, top - 4, 3, 7);
                break;
            case ArmPose.Front: // its upper arm, the rest bent in front of its tummy (drawn over the body)
                painter.DrawFlesh(Body, x, top + 5, 3, 5);
                break;
        }
    }
}
