using System.Windows;
using System.Windows.Media;
using Usagi.App.ViewModels;

namespace Usagi.App.Views;

// What the creature does in answer to something: waking at a reset, starting at a jump in the
// usage, holding up a sign as the usage passes a notification threshold, and keeping its eyes on
// the cursor.
public sealed partial class MascotView
{
    private const int WakeFrames = 7;
    private const int StartleFrames = 11;

    // How near the cursor has to be for the eyes to follow it, and how far off-center before they
    // turn its way, in device-independent pixels from between the eyes.
    private const double LookRange = 240;
    private const double LookDeadZone = 16;

    // Something it does once, in place of its mood's loop, and then goes back to the loop.
    private enum Reaction { None, Wake, Startle, Sign }

    private Reaction _reaction;
    private int _reactionFrame;

    // The percentage on the sign it's holding up.
    private int _signPercent;

    // Whether it's had a usage yet: the first is where the usage has been all along, not a rise past anything.
    private bool _usageSeen;

    // Where its eyes are turned: a block left or right (-1, 1), a block up or down.
    private (int X, int Y) _look;

    // A mood plays from the top of its cycle as soon as it sets in. Asleep is only ever left by a
    // reset (nothing else takes the usage back under 100%), which is what it wakes to.
    private void OnMoodChanged(MascotMood before)
    {
        Restart();
        if (WakesOnReset && before == MascotMood.Tired)
            React(Reaction.Wake);
    }

    private void OnUsageJumped()
    {
        if (StartlesOnJump)
            React(Reaction.Startle);
    }

    // Up past a threshold (the highest, if it went past more than one at once): it holds that up.
    private void OnUsagePassed(double before, double now)
    {
        var first = !_usageSeen;
        _usageSeen = true;
        if (first || !HoldsSign || now <= before)
            return;
        var passed = SignThresholds.Where(threshold => before < threshold && threshold <= now).DefaultIfEmpty().Max();
        if (passed == 0)
            return;
        _signPercent = passed;
        React(Reaction.Sign);
    }

    // Only while it's animated (and so seen): the timer is what plays a reaction out.
    private void React(Reaction reaction)
    {
        if (!_timer.IsEnabled)
            return;
        _reaction = reaction;
        _reactionFrame = 0;
        // Whatever it was idly doing, this comes first.
        if (_act is not null)
            DropAct();
        UpdatePace();
        InvalidateVisual();
    }

    /// <summary>Moves a reaction on a frame, to its end; whether there was one playing.</summary>
    private bool AdvanceReaction()
    {
        if (_reaction == Reaction.None)
            return false;
        var frames = _reaction switch
        {
            Reaction.Wake => WakeFrames,
            Reaction.Sign => SignFrames,
            _ => StartleFrames
        };
        if (++_reactionFrame >= frames)
        {
            _reaction = Reaction.None;
            UpdatePace();
        }
        return true;
    }

    /// <summary>Turns its eyes to where the cursor is now; whether that's somewhere new.</summary>
    private bool TurnEyes()
    {
        var look = LookAtCursor();
        if (look == _look)
            return false;
        _look = look;
        return true;
    }

    /// <summary>Which way the cursor is from between its eyes, if it's following the cursor and that's near enough.</summary>
    private (int X, int Y) LookAtCursor()
    {
        if (!FollowsCursor || PresentationSource.FromVisual(this) is null)
            return default;

        var eyes = PointToScreen(new Point(Columns * Block / 2, (Eyes + 1) * Block));
        var cursor = System.Windows.Forms.Cursor.Position;
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var x = (cursor.X - eyes.X) / scale;
        var y = (cursor.Y - eyes.Y) / scale;
        if (Math.Abs(x) > LookRange || Math.Abs(y) > LookRange)
            return default;
        return (Math.Abs(x) < LookDeadZone ? 0 : Math.Sign(x), Math.Abs(y) < LookDeadZone ? 0 : Math.Sign(y));
    }
    // What it does once and then goes back to its mood: the same creature, posed frame by frame.
    private static void DrawReaction(Stage stage, Reaction reaction, int f, int signPercent)
    {
        switch (reaction)
        {
            case Reaction.Wake:
                DrawWake(stage, f);
                break;
            case Reaction.Sign:
                DrawSign(stage, f, signPercent);
                break;
            default:
                DrawStartle(stage, f);
                break;
        }
    }

    // Waking: it stirs, eyes still shut; stretches with both arms up; then opens its eyes, arms
    // still up. Its mood's own loop takes over from there (after a reset, the hop).
    private static void DrawWake(Stage stage, int f)
    {
        var stretching = f >= 2;
        var painter = stage.Painter(0, stretching ? -1 : 0);
        if (stretching)
            DrawBody(painter, BodyTop - 3, BodyTop - 3, 7);
        else
            DrawBody(painter, BodyTop + 6, BodyTop + 6, 5);
        if (stage.Ears)
            DrawEars(painter, 0, BodyTop + (f >= 3 ? -1 : 0)); // back in place, and a frame behind the stretch

        if (f < 5)
        {
            painter.Draw(Dark, 6, Eyes + 1, 2, 1);
            painter.Draw(Dark, 16, Eyes + 1, 2, 1);
        }
        else
        {
            painter.Draw(Dark, 6, Eyes, 2, 2);
            painter.Draw(Dark, 16, Eyes, 2, 2);
        }
    }

    // Startled: a start, arms up; a shiver; then a bead of sweat running down as it settles.
    private static void DrawStartle(Stage stage, int f)
    {
        var jumping = f < 2;
        var shivering = f is >= 2 and < 5;
        var painter = stage.Painter(shivering ? (f % 2 == 0 ? -1 : 1) : 0, jumping ? -1 : 0);
        var arms = jumping ? BodyTop + 1 : BodyTop + 3;
        DrawBody(painter, arms, arms, 5);
        if (stage.Ears)
            DrawEars(painter, 0, BodyTop + (f is 1 or 2 ? -1 : 0)); // a frame behind the start, and still while it shivers

        painter.Draw(Dark, 6, Eyes, 2, 2);
        painter.Draw(Dark, 16, Eyes, 2, 2);
        if (jumping || shivering)
            return;

        var drop = f - 5;
        painter.Draw(Sweat, 22, BodyTop - 3 + drop, 1, 1);
        painter.Draw(Sweat, 21, BodyTop - 2 + drop, 2, 2);
    }
}
