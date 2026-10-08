using System.Windows;
using System.Windows.Media;
using Usagi.App.Settings;
using Usagi.App.ViewModels;

namespace Usagi.App.Views;

// What the creature does of its own accord: every few minutes of a calm mood, one of the acts
// below, picked at random, in place of the mood's loop.
public sealed partial class MascotView
{
    // How long it leaves between two acts, picked at random within the range. Subtle is meant to
    // move only now and then, so it waits longer.
    private static readonly (TimeSpan Min, TimeSpan Max) SubtleActGap = (TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(6));
    private static readonly (TimeSpan Min, TimeSpan Max) LivelyActGap = (TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3));

    // What it holds in its acts: a rope, and a book in blue and white (which stand out on its
    // orange, and on either taskbar), and the notes it hums.
    private static readonly Brush Rope = Frozen(Color.FromRgb(140, 147, 163));
    private static readonly Brush Blue = Frozen(Color.FromRgb(59, 130, 246));
    private static readonly Brush Note = Frozen(Color.FromRgb(155, 109, 242));

    // The pace of the acts that bounce: a squash or a stretch is over in a blink, and drawn at the
    // mood's pace it would look like a pose held rather than a spring.
    private static readonly TimeSpan BounceFrameTime = TimeSpan.FromMilliseconds(80);

    /// <summary>
    /// One act: <paramref name="Draw"/> poses the creature for a frame of it (the frame within a
    /// round), a round is <paramref name="Round"/> frames, and it goes <paramref name="Rounds"/> rounds,
    /// at its own <paramref name="Pace"/> if it has one, or else the mood's — and only in the
    /// <paramref name="Hours"/> it belongs to, if it belongs to some (given the hour of the day).
    /// </summary>
    private sealed record Act(int Round, int Rounds, Action<Stage, int> Draw, TimeSpan? Pace = null, Func<int, bool>? Hours = null)
    {
        public int Frames => Round * Rounds;
    }

    // To add one: a method that draws a frame of it, and a line here.
    private static readonly Act[] Acts =
    [
        new(20, 1, DrawLookAround),
        new(18, 1, DrawStretch),
        new(4, 6, DrawJumpRope),
        new(24, 2, DrawRead),
        new(16, 1, DrawSpin),
        new(16, 2, DrawHum),
        new(8, 4, DrawSquat),
        new(24, 1, DrawHide),
        new(12, 1, DrawBounce, BounceFrameTime),
        new(14, 1, DrawBanzai, BounceFrameTime),
        new(14, 1, DrawMorning, DaytimeFrameTime, IsMorning),
        new(16, 1, DrawLunch, DaytimeFrameTime, IsLunchtime),
        new(16, 1, DrawNight, DaytimeFrameTime, IsNight)
    ];

    private Act? _act;
    private Act? _lastAct;
    private int _actFrame;
    private DateTime _nextActAt;

    private void ScheduleAct()
    {
        var (min, max) = Animation == MascotAnimation.Lively ? LivelyActGap : SubtleActGap;
        _nextActAt = DateTime.Now + min + (max - min) * Random.Shared.NextDouble();
    }

    // Any of them but the one it did last, of those that belong to now.
    private void StartAct()
    {
        var hour = DateTime.Now.Hour;
        Act[] choices = [.. Acts.Where(act => act != _lastAct && (act.Hours?.Invoke(hour) ?? true))];
        _act = _lastAct = choices[Random.Shared.Next(choices.Length)];
        _actFrame = 0;
        UpdatePace();
    }

    /// <summary>Stops whatever act it's in, and starts the wait for the next over.</summary>
    private void DropAct()
    {
        _act = null;
        UpdatePace();
        ScheduleAct();
    }

    // The timer's pace: a sign's, an act's own, or else the mood's.
    private void UpdatePace() =>
        _timer.Interval = _reaction == Reaction.Sign ? SignFrameTime : _act?.Pace ?? FrameTime;

    /// <summary>
    /// Moves an act on a frame, to its end — or starts one, if it's time for one and the creature
    /// is <paramref name="idle"/> (nothing else is playing) in a calm mood. Whether it's in one.
    /// </summary>
    private bool AdvanceAct(bool idle)
    {
        if (_act is not null)
        {
            if (++_actFrame >= _act.Frames)
                DropAct();
            return true;
        }

        if (!idle || !IdleActs || Mood != MascotMood.Calm || DateTime.Now < _nextActAt)
            return false;
        StartAct();
        return true;
    }

    private void DrawAct(Stage stage)
    {
        // Clipped to its own box: hiding takes it below the ground, a note floats off the top.
        stage.Context.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        _act!.Draw(stage, _actFrame % _act.Round);
        stage.Context.Pop();
    }
    // Left, ahead, right, up, ahead.
    private static void DrawLookAround(Stage stage, int f)
    {
        var painter = stage.Painter(0, 0);
        DrawBody(painter, BodyTop + 3, BodyTop + 3, 5);
        if (stage.Ears)
            DrawEars(painter, 0, BodyTop);
        var (x, y) = (f / 4) switch { 0 => (-1, 0), 2 => (1, 0), 3 => (0, -1), _ => (0, 0) };
        DrawEyes(painter, x, y);
    }

    // Both arms up and its eyes shut, held a while.
    private static void DrawStretch(Stage stage, int f)
    {
        var stretching = f is >= 2 and < 11;
        var painter = stage.Painter(0, stretching ? -1 : 0);
        var arms = stretching ? BodyTop - 3 : BodyTop + 3;
        DrawBody(painter, arms, arms, stretching ? 7 : 5);
        if (stage.Ears)
            DrawEars(painter, 0, BodyTop + (f is >= 3 and < 12 ? -1 : 0)); // a frame behind
        if (stretching)
            DrawShutEyes(painter);
        else
            DrawEyes(painter);
    }

    // The rope over its head, then under its feet as it jumps.
    private static void DrawJumpRope(Stage stage, int f)
    {
        var over = f < 2;
        var painter = stage.Painter(0, over ? 0 : -1);
        DrawBody(painter, BodyTop + 3, BodyTop + 3, 5);
        if (stage.Ears)
            DrawEars(painter, 0, BodyTop + (f is 0 or 3 ? -1 : 0)); // a frame behind the jump
        DrawEyes(painter);
        if (over)
        {
            painter.DrawStill(Rope, 2, 0, 20, 1);
            painter.DrawStill(Rope, 1, 1, 1, 6);
            painter.DrawStill(Rope, 22, 1, 1, 6);
        }
        else
        {
            painter.DrawStill(Rope, 2, 19, 20, 1);
            painter.DrawStill(Rope, 1, 12, 1, 7);
            painter.DrawStill(Rope, 22, 12, 1, 7);
        }
    }

    // A book held open in front of it, its eyes going along the lines; a page turns at the end of each.
    private static void DrawRead(Stage stage, int f)
    {
        var painter = stage.Painter(0, 0);
        DrawBody(painter, BodyTop + 6, BodyTop + 6, 5);
        if (stage.Ears)
            DrawEars(painter, 0, BodyTop);
        if (f == 23)
            DrawShutEyes(painter);
        else
            DrawEyes(painter, f % 12 < 6 ? -1 : 1, 1);

        painter.Draw(Blue, 6, BodyTop + 7, 12, 5);
        painter.Draw(Brushes.White, 7, BodyTop + 7, 10, 4);
        painter.Draw(Rope, 12, BodyTop + 7, 1, 4);
        if (f % 12 == 11)
            painter.Draw(Brushes.White, 12, BodyTop + 5, 2, 2);
    }

    // Round on the spot with a little jump: its eyes slide off one side, its plain back goes by,
    // and they come back on the other.
    private static void DrawSpin(Stage stage, int f)
    {
        var painter = stage.Painter(0, f is >= 5 and < 10 ? -1 : 0);
        DrawBody(painter, BodyTop + 3, BodyTop + 3, 5);
        if (stage.Ears)
            DrawEars(painter, 0, BodyTop + (f is >= 6 and < 11 ? -1 : 0)); // a frame behind the jump
        int[] eyes = f switch
        {
            4 => [11, 20],
            5 => [20],
            >= 6 and <= 8 => [],
            9 => [2],
            10 => [2, 11],
            _ => [6, 16]
        };
        foreach (var x in eyes)
            painter.Draw(Dark, x, Eyes, 2, 2);
    }

    // Eyes shut, swaying, a note floating up on either side.
    private static void DrawHum(Stage stage, int f)
    {
        var sway = (f / 2 % 4) switch { 1 => 1, 3 => -1, _ => 0 };
        var painter = stage.Painter(sway, 0);
        DrawBody(painter, BodyTop + 3, BodyTop + 3, 5);
        if (stage.Ears)
            DrawEars(painter, sway, BodyTop);
        DrawShutEyes(painter);
        DrawNote(painter, 22, 6 - f % 8);
        if (f >= 4)
            DrawNote(painter, -1, 8 - (f - 4) % 8);
    }

    private static void DrawNote(Painter painter, int x, int y)
    {
        painter.DrawStill(Note, x + 1, y, 1, 3);
        painter.DrawStill(Note, x, y + 2, 2, 1);
        painter.DrawStill(Note, x + 2, y, 1, 1);
    }

    // Down with its arms out, and up again with them thrown high, with a bead of sweat for the effort.
    private static void DrawSquat(Stage stage, int f)
    {
        var down = f < 4;
        var painter = stage.Painter(0, down ? 3 : 0);
        var arms = down ? BodyTop + 3 : BodyTop - 3;
        DrawBody(painter, arms, arms, down ? 5 : 7);
        if (stage.Ears)
            DrawEars(painter, 0, BodyTop + (f is 0 ? 2 : down ? 3 : 0)); // a moment behind on the way down
        DrawEyes(painter);
        if (f >= 6)
            painter.Draw(Sweat, 22, BodyTop - 2, 1, 2);
    }

    // It sinks out of sight until only its eyes show, looks one way and the other, and comes back up.
    private static void DrawHide(Stage stage, int f)
    {
        var lift = f switch
        {
            < 4 or >= 19 => 0,
            4 or 18 => 3,
            5 or 17 => 6,
            6 or 16 => 9,
            _ => 10
        };
        var painter = stage.Painter(0, lift);
        DrawBody(painter, BodyTop + 3, BodyTop + 3, 5);
        if (stage.Ears)
            DrawEars(painter, 0, BodyTop + lift);
        DrawEyes(painter, f is >= 8 and < 11 ? -1 : f is >= 12 and < 15 ? 1 : 0);
    }
}
