using System.Windows.Media;

namespace Usagi.App.Views;

// What it does at certain hours, among its acts (see Acts): a stretch under the morning sun, a
// rice ball at lunchtime, and a yawn in a nightcap at night. And the sign it holds up when the
// usage passes a notification threshold.
public sealed partial class MascotView
{
    // The pace of these: slower than a bounce, they're poses held, but quicker than a mood.
    private static readonly TimeSpan DaytimeFrameTime = TimeSpan.FromMilliseconds(150);

    private static readonly Brush Sun = Frozen(Color.FromRgb(255, 200, 60));
    private static readonly Brush Cap = Frozen(Color.FromRgb(76, 111, 216));
    private static readonly Brush Nori = Frozen(Color.FromRgb(43, 43, 43));

    // The hours each belongs to.
    private static bool IsMorning(int hour) => hour is >= 5 and < 10;
    private static bool IsLunchtime(int hour) => hour is >= 11 and < 14;
    private static bool IsNight(int hour) => hour is >= 22 or < 4;

    // Its arms up and its eyes shut, a big stretch, with the sun up behind it.
    private static void DrawMorning(Stage stage, int f)
    {
        var painter = stage.Painter(0, 0);
        DrawSun(painter, 20, 0);
        _ = f switch
        {
            < 2 or >= 11 => DrawPose(stage, Standing, 0, ArmPose.Side),
            2 or 10 => DrawPose(stage, Standing, 0, ArmPose.Raised, eyesShut: true),
            _ => DrawPose(stage, Stretched, 0, ArmPose.Up, eyesShut: true, mouth: Mouth.Small)
        };
    }

    // A rice ball held in both hands: a bite, and a few chews.
    private static void DrawLunch(Stage stage, int f)
    {
        var mouth = f switch
        {
            3 => Mouth.Open,
            5 or 7 or 9 => Mouth.Small,
            _ => Mouth.None
        };
        var (x, top) = DrawPose(stage, Standing, 0, ArmPose.Front, eyesShut: f is >= 11 and < 14, mouth: mouth);
        DrawRiceBall(stage.Painter(0, 0), x + 6, top + 7, bitten: f >= 4);
    }

    // Sleepy in a nightcap: a long yawn with a stretch, a "Z" floating up, and a nod.
    private static void DrawNight(Stage stage, int f)
    {
        var (shape, arms, mouth) = f switch
        {
            3 => (Standing, ArmPose.Side, Mouth.Small),
            >= 4 and < 9 => (Stretched, ArmPose.Raised, Mouth.Open),
            >= 12 => (Settling, ArmPose.Side, Mouth.None),
            _ => (Standing, ArmPose.Side, Mouth.None)
        };
        DrawPose(stage, shape, 0, arms, eyesShut: true, mouth: mouth, head: Headwear.Nightcap);
        if (f >= 3)
            DrawZ(stage.Painter(0, 0), 0, f is >= 6 and < 12 ? 0 : 1);
    }

    /// <summary>A sun, its rays about it, with its top-left corner at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    private static void DrawSun(Painter painter, int x, int y)
    {
        painter.DrawStill(Sun, x + 1, y + 1, 3, 3);
        foreach (var (dx, dy) in ((int, int)[])[(2, 0), (2, 4), (0, 2), (4, 2)])
            painter.DrawStill(Sun, x + dx, y + dy, 1, 1);
    }

    /// <summary>A rice ball 6 wide and 4 tall, between its hands, its nori at the bottom; a bite out of its top right.</summary>
    private static void DrawRiceBall(Painter painter, int x, int y, bool bitten)
    {
        painter.Draw(Brushes.White, x + 2, y, bitten ? 1 : 2, 1);
        painter.Draw(Brushes.White, x + 1, y + 1, bitten ? 3 : 4, 1);
        painter.Draw(Brushes.White, x, y + 2, 6, 2);
        painter.Draw(Nori, x + 2, y + 2, 2, 2);
    }

    /// <summary>The cap over its head, the ears under it: a cuff, the cap, its tip flopped over with a pompom.</summary>
    private static void DrawNightcap(Painter painter, int x, int top, int width)
    {
        painter.Draw(Cap, x + 1, top - 2, width - 2, 2);
        painter.Draw(Cap, x + 4, top - 3, width - 9, 1);
        painter.Draw(Cap, x + width - 7, top - 4, 4, 1);
        painter.Draw(Cap, x + width - 3, top - 4, 2, 3);
        painter.Draw(Brushes.White, x + width - 2, top - 1, 2, 2);
        painter.Draw(Brushes.White, x + 1, top, width - 2, 1);
    }

    private static void DrawZ(Painter painter, int x, int y)
    {
        painter.DrawStill(Rope, x, y, 4, 1);
        painter.DrawStill(Rope, x + 2, y + 1, 1, 1);
        painter.DrawStill(Rope, x + 1, y + 2, 1, 1);
        painter.DrawStill(Rope, x, y + 3, 4, 1);
    }

    // ---- The sign ------------------------------------------------------------------------------

    private const int SignFrames = 22;
    private static readonly TimeSpan SignFrameTime = TimeSpan.FromMilliseconds(100);

    // Digits 3 blocks wide and 5 tall, and the percent sign, a row of blocks each.
    private static readonly string[][] Digits =
    [
        ["111", "101", "101", "101", "111"], ["010", "110", "010", "010", "111"], ["111", "001", "111", "100", "111"],
        ["111", "001", "111", "001", "111"], ["101", "101", "111", "001", "001"], ["111", "100", "111", "001", "111"],
        ["111", "100", "111", "101", "111"], ["111", "001", "010", "010", "010"], ["111", "101", "111", "101", "111"],
        ["111", "101", "111", "001", "111"]
    ];
    private static readonly string[] Percent = ["101", "001", "010", "100", "101"];

    private static readonly Brush SignWarm = Frozen(Color.FromRgb(236, 154, 0));
    private static readonly Brush SignOrange = Frozen(Color.FromRgb(236, 117, 0));
    private static readonly Brush SignRed = Frozen(Color.FromRgb(233, 49, 71));

    // It crouches under a sign held up over its head, with the percentage on it, and puts it down.
    private static void DrawSign(Stage stage, int f, int percent)
    {
        if (f is 0 or >= SignFrames - 2)
        {
            DrawPose(stage, f == SignFrames - 1 ? Settling : Standing, 0, ArmPose.Raised);
            return;
        }

        var (x, top) = DrawPose(stage, Crouched, 0, ArmPose.Up, eyesShut: f is 9 or 10);
        var painter = stage.Painter(0, 0);
        var edge = percent >= 95 ? SignRed : percent >= 90 ? SignOrange : SignWarm;
        var (left, width, signTop) = (x - 2, StandWidth + 4, top - 7);
        painter.Draw(edge, left, signTop, width, 7);
        painter.Draw(Brushes.White, left + 1, signTop + 1, width - 2, 5);

        var text = percent.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var at = left + (width - (text.Length * 4 + 3)) / 2;
        foreach (var digit in text)
        {
            DrawGlyph(painter, Digits[digit - '0'], at, signTop + 1);
            at += 4;
        }
        DrawGlyph(painter, Percent, at, signTop + 1);

        // Its hands over the sign's edges, holding it.
        painter.DrawFlesh(Body, x - 3, top - 4, 3, 4);
        painter.DrawFlesh(Body, x + StandWidth, top - 4, 3, 4);
    }

    private static void DrawGlyph(Painter painter, string[] rows, int x, int y)
    {
        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < rows[row].Length; column++)
            {
                if (rows[row][column] == '1')
                    painter.Draw(Dark, x + column, y + row, 1, 1);
            }
        }
    }
}
