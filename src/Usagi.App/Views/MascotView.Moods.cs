using System.Windows;
using System.Windows.Media;
using Usagi.App.ViewModels;

namespace Usagi.App.Views;

// How the creature looks in each mood: its body, its face, and its ears if it wears them.
public sealed partial class MascotView
{
    private static readonly Brush Sparkle = Frozen(Color.FromRgb(255, 200, 60));
    private static readonly Brush Sleep = Frozen(Color.FromRgb(150, 150, 160));

    // The ears are pink rather than white or black: it's the one that shows on a light taskbar and a dark one.
    private static readonly Brush Ear = Frozen(Color.FromRgb(247, 168, 192));
    private static readonly Brush Band = Frozen(Color.FromRgb(184, 67, 107));

    // Its mood's own loop: the frame of it that's due, or (frame null) the pose the mood rests in.
    private void DrawMood(Stage stage, int? frame)
    {
        var mood = Mood;
        var f = frame ?? RestFrame(mood);
        var painter = stage.Painter(Shift(mood, f, resting: frame is null), Lift(mood, f));

        DrawBody(painter, mood, f);
        if (stage.Ears)
            DrawEars(painter, mood, f, resting: frame is null);
        switch (mood)
        {
            case MascotMood.Happy:
                DrawHappy(painter, f, _look);
                break;
            case MascotMood.Worried:
                DrawWorried(painter, f);
                break;
            case MascotMood.Panic:
                DrawPanic(painter, f, stage.Ears);
                break;
            case MascotMood.Tired:
                DrawTired(painter, f, stage.Ears);
                break;
            default:
                DrawCalm(painter, f, chewing: _chewFramesLeft > 0, _look);
                break;
        }

        // Chewing a bite of its carrot: the mouth opens and shuts. It doesn't eat in a panic or in its sleep.
        if (Carrot && _chewFramesLeft > 0 && mood is not (MascotMood.Panic or MascotMood.Tired))
            painter.Draw(Dark, 10, Eyes + 5, 4, _chewFramesLeft % 2 == 0 ? 2 : 1);
    }
    // The pose a mood rests in is one of its frames, picked to show the mood without the motion.
    private static int RestFrame(MascotMood mood) => mood switch
    {
        MascotMood.Happy => 2,
        MascotMood.Worried => 9,
        _ => 0
    };

    // Sideways movement of the whole body: the panic's shake (not while resting).
    private static int Shift(MascotMood mood, int f, bool resting) =>
        mood == MascotMood.Panic && !resting ? (f % 4) switch { 0 => -1, 2 => 1, _ => 0 } : 0;

    // Vertical movement of the whole body: up a block for a hop, down one for a sag.
    private static int Lift(MascotMood mood, int f) => mood switch
    {
        MascotMood.Happy => f % 4 < 2 ? -1 : 0,
        MascotMood.Calm => IsCalmHop(f) ? -1 : 0,
        MascotMood.Tired => f is >= 6 and < 14 ? 1 : 0,
        _ => 0
    };

    private static void DrawBody(Painter painter, MascotMood mood, int f)
    {
        // Arms: out to the sides, thrown up, or hanging.
        var (leftArm, rightArm, armLength) = mood switch
        {
            MascotMood.Panic => f % 2 == 0 ? (BodyTop - 2, BodyTop + 2, 6) : (BodyTop + 2, BodyTop - 2, 6),
            MascotMood.Tired => (BodyTop + 6, BodyTop + 6, 5),
            MascotMood.Happy when f % 4 < 2 => (BodyTop + 1, BodyTop + 1, 5),
            MascotMood.Calm when IsCalmHop(f) => (BodyTop + 1, BodyTop + 1, 5),
            _ => (BodyTop + 3, BodyTop + 3, 5)
        };
        DrawBody(painter, leftArm, rightArm, armLength);
    }

    private static void DrawBody(Painter painter, int leftArm, int rightArm, int armLength)
    {
        painter.DrawFlesh(Body, 3, BodyTop, 18, 12);
        foreach (var x in (int[])[3, 8, 14, 19])
            painter.DrawFlesh(Body, x, BodyTop + 12, 2, Math.Max(4 - Math.Max(painter.Lift, 0), 0)); // legs: a pair under each side; they give when it sags
        painter.DrawFlesh(Body, 0, leftArm, 3, armLength);
        painter.DrawFlesh(Body, 21, rightArm, 3, armLength);
    }

    private static void DrawEyes(Painter painter, int x = 0, int y = 0)
    {
        painter.Draw(Dark, 6 + x, Eyes + y, 2, 2);
        painter.Draw(Dark, 16 + x, Eyes + y, 2, 2);
    }

    private static void DrawShutEyes(Painter painter)
    {
        painter.Draw(Dark, 6, Eyes + 1, 2, 1);
        painter.Draw(Dark, 16, Eyes + 1, 2, 1);
    }

    // A headband of rabbit ears, worn rather than grown, and it shows: it trails a hop by a frame,
    // stays where it is while the body shakes under it, and sits further askew the worse things
    // get — a block aside when worried, two in a panic, slid down the forehead once asleep.
    // Not part of the body, it doesn't fade with it: the body alone is the gauge, and the ears keep
    // their color (as on the app's icon) rather than going pale at the first few percent used.
    private static void DrawEars(Painter painter, MascotMood mood, int f, bool resting)
    {
        var loop = mood == MascotMood.Calm ? CalmLoopFrames : LoopFrames;
        var lift = resting || mood == MascotMood.Tired ? painter.Lift : Lift(mood, (f + loop - 1) % loop);
        var (slip, slide) = mood switch
        {
            MascotMood.Worried => (1, 0),
            MascotMood.Panic => (2, 0),
            MascotMood.Tired => (-1, 1),
            _ => (0, 0)
        };
        var top = BodyTop + slide + lift;
        // In a panic the two ears take turns dropping a block.
        var (leftDrop, rightDrop) = mood == MascotMood.Panic ? (f % 2 == 0 ? (0, 1) : (1, 0)) : (0, 0);

        DrawEars(painter, slip, top, leftDrop, rightDrop);
    }

    /// <summary>The headband with its band on the row <paramref name="top"/>, <paramref name="slip"/> blocks aside.</summary>
    private static void DrawEars(Painter painter, int slip, int top, int leftDrop = 0, int rightDrop = 0)
    {
        painter.DrawStill(Band, 4 + slip, top, 16, 1);
        foreach (var (x, drop) in ((int, int)[])[(5, leftDrop), (16, rightDrop)]) // an ear above each eye
        {
            painter.DrawStill(Ear, x + slip, top - 3 + drop, 3, 3);
            painter.DrawStill(Brushes.White, x + 1 + slip, top - 2 + drop, 1, 2);
        }
    }

    // Its own plain face — the hop says it — with sparkles twinkling either side.
    private static void DrawHappy(Painter painter, int f, (int X, int Y) look)
    {
        painter.Draw(Dark, 6 + look.X, Eyes + look.Y, 2, 2);
        painter.Draw(Dark, 16 + look.X, Eyes + look.Y, 2, 2);
        if (f % 4 < 2)
        {
            painter.DrawStill(Sparkle, 0, 2, 1, 3);
            painter.DrawStill(Sparkle, -1, 3, 3, 1);
            painter.DrawStill(Sparkle, 23, 0, 1, 3);
            painter.DrawStill(Sparkle, 22, 1, 3, 1);
        }
        else
        {
            painter.DrawStill(Sparkle, 0, 0, 1, 1);
            painter.DrawStill(Sparkle, 22, 2, 1, 1);
        }
    }

    // Eyes darting, a bead of sweat running down.
    private static void DrawWorried(Painter painter, int f)
    {
        var look = (f / 4) switch { 1 => -1, 3 => 1, _ => 0 };
        painter.Draw(Dark, 6 + look, Eyes + 1, 2, 2);
        painter.Draw(Dark, 16 + look, Eyes + 1, 2, 2);
        var drop = f % 8;
        if (drop < 6)
        {
            painter.Draw(Sweat, 22, BodyTop - 3 + drop, 1, 1);
            painter.Draw(Sweat, 21, BodyTop - 2 + drop, 2, 2);
        }
    }

    // Eyes darting, sweat flying — clear of the ears, when it wears them.
    private static void DrawPanic(Painter painter, int f, bool ears)
    {
        DrawEyes(painter, f % 2);
        if (f % 2 == 1)
        {
            painter.Draw(Sweat, ears ? 4 : 5, BodyTop - 4, 1, 2);
            painter.Draw(Sweat, ears ? 22 : 18, BodyTop - 4, 1, 2);
        }
        else
        {
            painter.Draw(Sweat, 2, BodyTop - 3, 1, 2);
            painter.Draw(Sweat, 11, BodyTop - 4, 2, 1);
            painter.Draw(Sweat, 21, BodyTop - 3, 1, 2);
        }
    }

    // Asleep: eyes shut, and a z rising off it — small, then large, then gone. The small one
    // starts further right when there's an ear where it would be.
    private static void DrawTired(Painter painter, int f, bool ears)
    {
        painter.Draw(Dark, 6, Eyes + 1, 2, 1);
        painter.Draw(Dark, 16, Eyes + 1, 2, 1);
        switch (f / 4)
        {
            case 0:
                var x = ears ? 20 : 16;
                painter.DrawStill(Sleep, x, 1, 3, 1);
                painter.DrawStill(Sleep, x + 1, 2, 1, 1);
                painter.DrawStill(Sleep, x, 3, 3, 1);
                break;
            case 1 or 2:
                painter.DrawStill(Sleep, 19, 0, 4, 1);
                painter.DrawStill(Sleep, 21, 1, 1, 1);
                painter.DrawStill(Sleep, 20, 2, 1, 1);
                painter.DrawStill(Sleep, 19, 3, 4, 1);
                break;
        }
    }

    // Square eyes, shut for the one frame of a blink, glancing left then right — or at the
    // carrot while it chews a bite of it, or wherever the cursor has them look.
    private static void DrawCalm(Painter painter, int f, bool chewing, (int X, int Y) look)
    {
        var blink = f is BlinkFrame or 35;
        var glance = chewing ? 1 : look.X != 0 ? look.X : f is >= 16 and < 20 ? -1 : f is >= 20 and < 24 ? 1 : 0;
        var y = blink ? Eyes + 1 : Eyes + look.Y;
        painter.Draw(Dark, 6 + glance, y, 2, blink ? 1 : 2);
        painter.Draw(Dark, 16 + glance, y, 2, blink ? 1 : 2);
    }

    // Two quick hops near the end of the calm mood's lively loop.
    private static bool IsCalmHop(int frame) => frame is 40 or 41 or 44 or 45;
}
