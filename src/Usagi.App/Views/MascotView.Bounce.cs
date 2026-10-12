using System.Windows.Media;

namespace Usagi.App.Views;

// The creature drawn in a pose of its own rather than its mood's: its body squashed or stretched,
// each arm placed, its eyes open or shut, its mouth, and what's on its head. Its feet stay on the
// ground (but in the air): squashed, it's wider and lower on bent legs; stretched, narrower and
// taller. The "banzai" is drawn with it. The jump has poses of its own (see JumpPose): it keeps
// its width, and its arms and legs do more than a shape can say.
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

    /// <summary>
    /// A pose of the jump: the row its body's top is on and how tall the body is, how long its
    /// legs are (or <paramref name="Buckled"/>: given under it, knees out), where its left arm is
    /// — from the left edge of the drawing and from the top of its body; the right arm mirrors
    /// it — and how tall its eyes are, and how far down its face from where they are as it stands.
    /// </summary>
    private readonly record struct JumpPose(int Top, int Height, int Legs, (int X, int Y, int Width, int Height) Arm,
        int EyeHeight = 2, int EyeDrop = 0, bool Buckled = false);

    // The jump, pose by pose. It's as wide all the way through: what gives is its legs, and what
    // stretches is its height. There are four rows over its head as it stands, and they have to
    // hold its arms too, so it draws its legs in in the air: its feet leave the ground by four
    // blocks for the two its body rises.
    private static readonly JumpPose Upright = new(BodyTop, StandHeight, StandLegs, (0, 3, 3, 5));
    private static readonly JumpPose Gathered = new(BodyTop + 2, StandHeight, 2, (0, 6, 3, 4), EyeDrop: 3); // down on its legs, arms and eyes sunk
    private static readonly JumpPose Launched = new(BodyTop - 1, StandHeight + 1, StandLegs, (1, -2, 4, 4), EyeHeight: 4); // drawn out tall, eyes too, arms at the corners of its head
    private static readonly JumpPose Rising = new(BodyTop - 1, StandHeight, 2, (4, -2, 3, 2));        // its own shape again, arms straight up over its head
    private static readonly JumpPose AtTheTop = new(BodyTop - 2, StandHeight, 2, (5, -2, 3, 2));
    private static readonly JumpPose Falling = new(BodyTop - 1, StandHeight, StandLegs, (2, -3, 4, 4));
    private static readonly JumpPose Landed = new(1, StandHeight + 3, StandLegs, (0, 5, 3, 5), EyeHeight: 5, Buckled: true); // its feet have stopped and the rest hasn't
    private static readonly JumpPose Sunk = new(BodyTop + 1, StandHeight - 1, StandLegs, (0, 6, 3, 4), EyeHeight: 3, Buckled: true); // then the body comes down onto them
    private static readonly JumpPose Risen = new(BodyTop - 1, StandHeight, StandLegs + 1, (0, 8, 3, 4)); // and up on straight legs, a block taller than it stands, arms still low

    // Down on its legs, drawn out tall as it leaves the ground, arms up over its head in the air,
    // and a landing its legs buckle under before it's up again.
    private static void DrawBounce(Stage stage, int f)
    {
        var pose = f switch
        {
            0 or 1 => Gathered,
            2 => Launched,
            3 or 8 => Rising,
            >= 4 and <= 7 => AtTheTop,
            9 or 10 => Falling,
            11 => Landed,
            12 => Sunk,
            13 or 14 => Risen,
            _ => Upright
        };
        // Over its head is where the headband's ears are: wearing it, the arms go up outside them.
        if (stage.Ears && pose.Arm.Y < 0)
            pose = pose with { Arm = (1, -2, 4, 4) };

        // Drawn as if its body's top were where it is standing, and lifted to where it is, so
        // that the body gauge's level rides with the body.
        var painter = stage.Painter(0, pose.Top - BodyTop);
        var feet = BodyTop + pose.Height;
        foreach (var (x, outward) in ((int, int)[])[(3, -1), (8, -1), (14, 1), (19, 1)]) // legs: a pair under each side
        {
            if (pose.Buckled)
            {
                painter.DrawFlesh(Body, x + outward, feet, 2, 2); // the knee out to the side
                painter.DrawFlesh(Body, x, feet + 2, 2, 2);       // and the foot back under it
            }
            else
            {
                painter.DrawFlesh(Body, x, feet, 2, pose.Legs);
            }
        }

        var arm = pose.Arm;
        painter.DrawFlesh(Body, arm.X, BodyTop + arm.Y, arm.Width, arm.Height);
        painter.DrawFlesh(Body, Columns - 2 - arm.X - arm.Width, BodyTop + arm.Y, arm.Width, arm.Height);
        painter.DrawFlesh(Body, 3, BodyTop, StandWidth, pose.Height);
        if (pose == Landed)
            painter.DrawFlesh(Body, 1, BodyTop + 10, StandWidth + 4, 2); // its sides bulge under its arms

        painter.Draw(Dark, 6, Eyes + pose.EyeDrop, 2, pose.EyeHeight);
        painter.Draw(Dark, 16, Eyes + pose.EyeDrop, 2, pose.EyeHeight);
        // The headband needs three rows over it for its ears: where the body goes higher than
        // that, it's left behind on its forehead.
        if (stage.Ears)
            DrawEars(painter, 0, Math.Max(pose.Top, 3));
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
