using System.Windows.Media;

namespace Usagi.App.Views;

// The creature turning to one side. It keeps the outline it has from the front: what changes is
// that the edge it has turned away from is its body's color, darker — a strip that follows the
// outline, arm and legs and all, so it reads as the side of one solid — and that its eyes move
// over and closer together. Looking back, spinning round and its start at a jump are drawn with it.
public sealed partial class MascotView
{
    // The side it has turned away.
    private static readonly Brush Side = Frozen(Color.FromRgb(187, 102, 75));

    // The grid a pose is laid out on before it's drawn: the drawing's own columns (the spare one
    // either side isn't a pose's to use), and rows enough for legs drawn out under a lifted body.
    private const int TurnColumns = 24;
    private const int TurnRows = 24;
    private const byte Flesh = 1;
    private const byte Away = 2;

    private const int LookBackFrames = 42;
    private const int SpinFrames = 15;
    private const int StartleFrames = 23;

    // Straight down, too short to reach so the feet turn a block aside, or given under it, knees out.
    private enum LegPose { Straight, Hooked, Buckled }

    /// <summary>
    /// A pose it can be turned in. <paramref name="Turn"/> is how far, 1 to 3, and which way: to
    /// the right of the picture, or (negative) the left; <paramref name="Back"/> is seen from
    /// behind. Its body's top is on the row <paramref name="Top"/> and it's <paramref name="Height"/>
    /// tall, on legs <paramref name="LegLength"/> long (0: as long as it takes to reach the ground);
    /// its arms start <paramref name="ArmTop"/> rows down from the top of its body.
    /// </summary>
    private readonly record struct TurnPose(int Turn = 0, bool Back = false, int Top = BodyTop, int Height = StandHeight,
        LegPose Legs = LegPose.Straight, int LegLength = 0, int ArmTop = 3, int ArmLength = 5,
        int EyeHeight = 2, bool EyesShut = false, int Mouth = 0, int Shift = 0);

    private static readonly TurnPose Facing = new(Turn: 0); // (not new(): on a struct that skips the defaults)
    private static readonly TurnPose Ducked = Facing with { Top = BodyTop + 1, Legs = LegPose.Hooked };
    private static readonly TurnPose Touchdown = Facing with { Top = BodyTop + 1, Height = StandHeight - 1, Legs = LegPose.Buckled, ArmTop = 6, ArmLength = 4, EyeHeight = 3 };
    private static readonly TurnPose Straightening = Facing with { Top = BodyTop - 1, LegLength = StandLegs + 1, ArmTop = 8, ArmLength = 4 };

    // Round to one side in two steps, a blink while it's there, and back; then the other side.
    private static void DrawLookBack(Stage stage, int f)
    {
        var way = f < LookBackFrames / 2 ? 1 : -1;
        var pose = (f % (LookBackFrames / 2)) switch
        {
            0 or 14 => Facing with { Turn = way },
            9 => Facing with { Turn = 2 * way, EyesShut = true },
            < 14 => Facing with { Turn = 2 * way },
            _ => Facing
        };
        DrawTurned(stage, pose);
    }

    // Round on the spot with a little jump: the dark side widens until its eyes are off the edge,
    // its plain back goes by, and they come back on the other side.
    private static void DrawSpin(Stage stage, int f)
    {
        var pose = f switch
        {
            0 or 1 => Ducked with { ArmTop = 5, ArmLength = 4 },
            2 => Facing with { Turn = 1, Top = BodyTop - 1, LegLength = 3 },
            3 => Facing with { Turn = 2, Top = BodyTop - 2, LegLength = 3 },
            4 => Facing with { Turn = 3, Top = BodyTop - 2, LegLength = 2 },
            5 or 6 => Facing with { Back = true, Top = BodyTop - 2, LegLength = 2 },
            7 => Facing with { Turn = -3, Top = BodyTop - 2, LegLength = 2 },
            8 => Facing with { Turn = -2, Top = BodyTop - 2, LegLength = 3 },
            9 => Facing with { Turn = -1, Top = BodyTop - 1, LegLength = 3 },
            10 => Touchdown,
            11 or 12 => Straightening,
            _ => Facing
        };
        DrawTurned(stage, pose);
    }

    // Startled: off the ground with its eyes and mouth open, a shiver once it's down, and then,
    // ducked, a look to either side before it's itself again.
    private static void DrawStartle(Stage stage, int f)
    {
        var agape = Facing with { ArmTop = -1, ArmLength = 4, EyeHeight = 3, Mouth = 2 };
        var pose = f switch
        {
            0 or 1 => agape with { Top = BodyTop - 1, LegLength = 3 },
            2 or 3 => agape,
            < 10 => Facing with { Shift = f % 2 == 0 ? -1 : 1, ArmTop = 1, EyeHeight = 3 },
            10 => Ducked with { Turn = 1 },
            < 14 => Ducked with { Turn = 2 },
            14 => Ducked,
            < 18 => Ducked with { Turn = -2 },
            18 => Facing with { Turn = -1 },
            20 => Facing with { EyesShut = true },
            _ => Facing
        };
        DrawTurned(stage, pose);

        var painter = stage.Painter(pose.Shift, 0);
        if (f < 4)
        {
            painter.DrawStill(Sparkle, 1, 0, 1, 2);
            painter.DrawStill(Sparkle, 11, 0, 2, 1);
            painter.DrawStill(Sparkle, 22, 0, 1, 2);
        }
        else if (f < 10)
        {
            painter.Draw(Sweat, 22, BodyTop - 3, 1, 2);
        }
    }

    /// <summary>
    /// The creature in a pose. It's laid out as turned to the right of the picture and mirrored
    /// for the other way; and as if its body's top were where it is standing, then lifted to
    /// where it is, so that the body gauge's level rides with the body.
    /// </summary>
    private static void DrawTurned(Stage stage, TurnPose pose)
    {
        var far = Math.Abs(pose.Turn);
        var mirrored = pose.Turn < 0;
        var painter = stage.Painter(pose.Shift, pose.Top - BodyTop);
        var bottom = BodyTop + pose.Height;

        Span<byte> cells = stackalloc byte[TurnColumns * TurnRows];
        cells.Clear();
        Put(cells, 3, BodyTop, StandWidth, pose.Height);
        int[] legs = far >= 2 ? [4, 9, 13, 18] : [3, 8, 14, 19]; // turned well round, the pairs are closer together
        for (var i = 0; i < legs.Length; i++)
        {
            var x = legs[i];
            switch (pose.Legs)
            {
                case LegPose.Hooked:
                    Put(cells, x, bottom, 2, 2);
                    Put(cells, x + (i % 2 == 0 ? 1 : -1), bottom + 2, 2, 1);
                    break;
                case LegPose.Buckled:
                    Put(cells, x + (i < 2 ? -1 : 1), bottom, 2, 2);
                    Put(cells, x, bottom + 2, 2, 2);
                    break;
                default:
                    Put(cells, x, bottom, 2, pose.LegLength > 0 ? pose.LegLength : Ground - pose.Top - pose.Height);
                    break;
            }
        }
        Put(cells, 0, BodyTop + pose.ArmTop, 3, pose.ArmLength);
        Put(cells, 21, BodyTop + pose.ArmTop, 3, pose.ArmLength);

        // The side it has turned away: on each row, the first few blocks in from the outline. An
        // arm is narrower than the body, and so is its side — until it's turned right round, when
        // the side is as wide as the eyes have moved over.
        for (var y = 0; far > 0 && y < TurnRows; y++)
        {
            var row = cells.Slice(y * TurnColumns, TurnColumns);
            var x = row.IndexOf(Flesh);
            if (x is < 0 or > 6)
                continue;
            var reach = far == 3 ? 9 - x : (x < 3 ? 2 : 3) - (far == 1 ? 1 : 0);
            for (var n = 0; n < reach && row[x + n] == Flesh; n++)
                row[x + n] = Away;
        }

        for (var y = 0; y < TurnRows; y++)
        {
            var row = cells.Slice(y * TurnColumns, TurnColumns);
            for (var x = 0; x < TurnColumns;)
            {
                var kind = row[x];
                var end = x + 1;
                while (end < TurnColumns && row[end] == kind)
                    end++;
                if (kind != 0)
                    painter.DrawFlesh(kind == Away ? Side : Body, Across(mirrored, x, end - x), y, end - x, 1);
                x = end;
            }
        }

        if (stage.Ears)
        {
            // The headband needs three rows over it for its ears: where the body goes higher than
            // that, it's left behind on its forehead.
            var band = Math.Max(pose.Top, 3);
            painter.DrawStill(Band, 4, band, 16, 1);
            int[] ears = far switch { 1 => [7, 17], 2 => [8, 17], 3 => [10, 18], _ => [5, 16] };
            foreach (var x in ears)
            {
                var at = Across(mirrored, x, 3);
                painter.DrawStill(Ear, at, band - 3, 3, 3);
                if (!pose.Back)
                    painter.DrawStill(Brushes.White, at + 1, band - 2, 1, 2);
            }
            if (pose.Back)
                painter.Draw(Ear, 11, BodyTop + 8, 2, 2); // and with rabbit ears goes a tail
        }
        if (pose.Back)
            return;

        // Its face has turned with it: the eye on the far side moves over more than the near one,
        // and right round there's only the near one left.
        int[] eyes = far switch { 1 => [8, 17], 2 => [9, 17], 3 => [19], _ => [6, 16] };
        foreach (var x in eyes)
            painter.Draw(Dark, Across(mirrored, x, 2), pose.EyesShut ? Eyes + 1 : Eyes, 2, pose.EyesShut ? 1 : pose.EyeHeight);
        if (pose.Mouth > 0)
            painter.Draw(Dark, Across(mirrored, far switch { 1 => 12, 2 => 13, 3 => 15, _ => 11 }, 2), Eyes + 3, 2, pose.Mouth);
    }

    private static void Put(Span<byte> cells, int x, int y, int width, int height)
    {
        for (var row = y; row < y + height; row++)
            cells.Slice(row * TurnColumns + x, width).Fill(Flesh);
    }

    // Where a part that's at x, turned to the right, is turned to the left.
    private static int Across(bool mirrored, int x, int width) => mirrored ? TurnColumns - x - width : x;
}
