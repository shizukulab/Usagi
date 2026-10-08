using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Usagi.App.Settings;
using Usagi.App.ViewModels;

namespace Usagi.App.Views;

/// <summary>
/// The pixel-art creature that sits beside the taskbar bars and acts out how much usage is left:
/// hopping among sparkles after a reset, blinking while there's room, sweating as it runs short, flailing near
/// the limit, asleep once it's used up.
/// <para>
/// Drawn in code, block by block, on a grid of <see cref="Columns"/> x <see cref="Rows"/> blocks
/// of <see cref="Block"/> units each. The body starts <see cref="BodyTop"/> rows down, leaving
/// headroom for raised arms and flying sweat, and there's a spare column either side for shaking.
/// </para>
/// <para>
/// Animation is frame by frame, a few frames a second, in a loop of <see cref="LoopFrames"/>.
/// How much it moves is the user's choice (<see cref="MascotAnimation"/>). Subtle, the default,
/// isn't continuous — something moving all the time on the taskbar pulls the eye: a mood plays
/// its loop twice when it sets in and then once a minute, resting in a still pose in between,
/// and the calm mood only blinks. Lively never rests: every mood loops, a little faster, and the
/// calm one gets a longer loop of its own in which it also glances about and hops.
/// </para>
/// <para>
/// It can wear a headband of rabbit ears (<see cref="BunnyEars"/>), which fits in the headroom:
/// three blocks tall, so it still clears the top when the creature hops.
/// </para>
/// <para>
/// And it can have a carrot lying beside it (<see cref="Carrot"/>), which makes it
/// <see cref="CarrotColumns"/> wide: a gauge of the session, eaten down from the tip as
/// <see cref="Usage"/> climbs, with a bite taken — a few frames of chewing — each time it shortens.
/// </para>
/// <para>
/// Three more things it does if the user wants them, each of them only while it's animated: wake
/// with a stretch when a reset ends its sleep (<see cref="WakesOnReset"/>), start when the usage
/// jumps (<see cref="StartlesOnJump"/>), and keep its eyes on the cursor (<see cref="FollowsCursor"/>).
/// And with <see cref="IdleActs"/>, every few minutes of a calm mood it does something of its own
/// for a while — reads, skips rope, hums a tune — picked at random.
/// </para>
/// <para>
/// Its body can be a gauge of the session too (<see cref="BodyGauge"/>): as much of it as is used
/// up fades, from the top down, in one of three ways. Its eyes never do, so its face still reads.
/// </para>
/// </summary>
public sealed partial class MascotView : FrameworkElement
{
    private const double Block = 2;
    private const int Columns = 26;
    private const int Rows = 20;
    private const int BodyTop = 4;
    private const int Eyes = BodyTop + 2; // the row the eyes are on
    private const int BodyRows = 16;      // its body and legs, top to toe: the steps of the body gauge

    private const int LoopFrames = 16;
    private const int BurstFrames = LoopFrames * 2;
    private const int CycleFrames = 240;   // a burst a minute
    private const int BlinkEvery = 24;     // the calm mood's blink, every six seconds
    private const int BlinkFrame = 11;
    private const int CalmLoopFrames = 48; // the calm mood's own loop when lively: blinks, glances, a couple of hops

    private static readonly TimeSpan SubtleFrameTime = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan LivelyFrameTime = TimeSpan.FromMilliseconds(180);

    /// <summary>How long a frame lasts, as it moves: an act with a pace of its own sets the timer to that while it plays.</summary>
    private TimeSpan FrameTime => Animation == MascotAnimation.Lively ? LivelyFrameTime : SubtleFrameTime;

    // What everything it does is drawn in. The rest of the colors are with what uses them.
    private static readonly Brush Body = Frozen(Color.FromRgb(217, 119, 87));
    private static readonly Brush Dark = Frozen(Color.FromRgb(30, 20, 18));
    private static readonly Brush Sweat = Frozen(Color.FromRgb(110, 190, 255));

    // What's used up and gone — the eaten end of the carrot, the body faded to a shadow — left
    // so that what remains can be read against the whole. Translucent gray, like the track of
    // the tray icon's ring: it has to show on a light taskbar and a dark one.
    private static readonly Brush Shadow = Frozen(Color.FromArgb(0x4D, 128, 128, 128));

    // How much of its color the faded part of the body keeps when it's just drawn pale.
    private const double PaleOpacity = 0.3;
    public static readonly DependencyProperty MoodProperty = DependencyProperty.Register(
        nameof(Mood), typeof(MascotMood), typeof(MascotView),
        new FrameworkPropertyMetadata(MascotMood.Calm, FrameworkPropertyMetadataOptions.AffectsRender,
            (d, e) => ((MascotView)d).OnMoodChanged((MascotMood)e.OldValue)));

    public static readonly DependencyProperty AnimationProperty = DependencyProperty.Register(
        nameof(Animation), typeof(MascotAnimation), typeof(MascotView),
        new FrameworkPropertyMetadata(MascotAnimation.Subtle, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((MascotView)d).Restart()));

    public static readonly DependencyProperty BunnyEarsProperty = DependencyProperty.Register(
        nameof(BunnyEars), typeof(bool), typeof(MascotView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CarrotProperty = DependencyProperty.Register(
        nameof(Carrot), typeof(bool), typeof(MascotView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UsageProperty = DependencyProperty.Register(
        nameof(Usage), typeof(double), typeof(MascotView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender,
            (d, e) => ((MascotView)d).OnUsageChanged((double)e.OldValue, (double)e.NewValue)));

    public static readonly DependencyProperty WakesOnResetProperty = DependencyProperty.Register(
        nameof(WakesOnReset), typeof(bool), typeof(MascotView), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty StartlesOnJumpProperty = DependencyProperty.Register(
        nameof(StartlesOnJump), typeof(bool), typeof(MascotView), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty HoldsSignProperty = DependencyProperty.Register(
        nameof(HoldsSign), typeof(bool), typeof(MascotView), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty SignThresholdsProperty = DependencyProperty.Register(
        nameof(SignThresholds), typeof(IReadOnlyList<int>), typeof(MascotView), new FrameworkPropertyMetadata(Array.Empty<int>()));

    public static readonly DependencyProperty UsageJumpsProperty = DependencyProperty.Register(
        nameof(UsageJumps), typeof(int), typeof(MascotView),
        new FrameworkPropertyMetadata(0, (d, _) => ((MascotView)d).OnUsageJumped()));

    public static readonly DependencyProperty FollowsCursorProperty = DependencyProperty.Register(
        nameof(FollowsCursor), typeof(bool), typeof(MascotView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((MascotView)d)._look = default));

    public static readonly DependencyProperty BodyGaugeProperty = DependencyProperty.Register(
        nameof(BodyGauge), typeof(MascotBodyGauge), typeof(MascotView),
        new FrameworkPropertyMetadata(MascotBodyGauge.None, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IdleActsProperty = DependencyProperty.Register(
        nameof(IdleActs), typeof(bool), typeof(MascotView), new FrameworkPropertyMetadata(false, (d, _) => ((MascotView)d).Restart()));

    private readonly DispatcherTimer _timer = new();
    private int _tick;
    private int? _drawnFrame;

    public MascotView()
    {
        SnapsToDevicePixels = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

        _timer.Tick += (_, _) => OnTick();
        // Nothing runs while it can't be seen: hidden, or its window hidden behind a full-screen app.
        IsVisibleChanged += (_, _) => Restart();
    }
    public MascotMood Mood
    {
        get => (MascotMood)GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    /// <summary>How much it moves; off, it holds each mood's still pose.</summary>
    public MascotAnimation Animation
    {
        get => (MascotAnimation)GetValue(AnimationProperty);
        set => SetValue(AnimationProperty, value);
    }

    /// <summary>Whether it wears a headband of rabbit ears.</summary>
    public bool BunnyEars
    {
        get => (bool)GetValue(BunnyEarsProperty);
        set => SetValue(BunnyEarsProperty, value);
    }

    /// <summary>Whether it wakes with a stretch when a reset ends its sleep, rather than just being awake.</summary>
    public bool WakesOnReset
    {
        get => (bool)GetValue(WakesOnResetProperty);
        set => SetValue(WakesOnResetProperty, value);
    }

    /// <summary>Whether it starts each time <see cref="UsageJumps"/> goes up.</summary>
    public bool StartlesOnJump
    {
        get => (bool)GetValue(StartlesOnJumpProperty);
        set => SetValue(StartlesOnJumpProperty, value);
    }

    /// <summary>Whether it holds up a sign with the percentage when <see cref="Usage"/> passes one of <see cref="SignThresholds"/>.</summary>
    public bool HoldsSign
    {
        get => (bool)GetValue(HoldsSignProperty);
        set => SetValue(HoldsSignProperty, value);
    }

    /// <summary>The usages, in percent, it holds up a sign at as the usage passes them.</summary>
    public IReadOnlyList<int> SignThresholds
    {
        get => (IReadOnlyList<int>)GetValue(SignThresholdsProperty);
        set => SetValue(SignThresholdsProperty, value);
    }

    /// <summary>A count of the times a refresh has found the usage well up on the one before: each new one is a jump to react to.</summary>
    public int UsageJumps
    {
        get => (int)GetValue(UsageJumpsProperty);
        set => SetValue(UsageJumpsProperty, value);
    }

    /// <summary>Whether, every few minutes of a calm mood, it does something of its own for a while.</summary>
    public bool IdleActs
    {
        get => (bool)GetValue(IdleActsProperty);
        set => SetValue(IdleActsProperty, value);
    }

    /// <summary>Whether its eyes turn to the cursor while that's nearby.</summary>
    public bool FollowsCursor
    {
        get => (bool)GetValue(FollowsCursorProperty);
        set => SetValue(FollowsCursorProperty, value);
    }

    /// <summary>Whether a carrot lies beside it, as much of it left as there is of the session.</summary>
    public bool Carrot
    {
        get => (bool)GetValue(CarrotProperty);
        set => SetValue(CarrotProperty, value);
    }

    /// <summary>Whether its body fades from the top as the session is used up, and how the faded part is drawn.</summary>
    public MascotBodyGauge BodyGauge
    {
        get => (MascotBodyGauge)GetValue(BodyGaugeProperty);
        set => SetValue(BodyGaugeProperty, value);
    }

    /// <summary>How much of the session is used, in percent: how far the carrot is eaten, and the body faded.</summary>
    public double Usage
    {
        get => (double)GetValue(UsageProperty);
        set => SetValue(UsageProperty, value);
    }

    // Most ticks fall in a rest and change nothing: only a new frame of the mood's loop is worth
    // a redraw, or the next one of a bite being chewed, a reaction or an act, or eyes with
    // somewhere new to look.
    private void OnTick()
    {
        _tick++;
        var chewing = AdvanceChewing();
        var reacting = AdvanceReaction();
        var acting = AdvanceAct(idle: !chewing && !reacting);
        var looked = TurnEyes();
        if (chewing || reacting || acting || looked || Frame() != _drawnFrame)
            InvalidateVisual();
    }

    // From the top of a cycle, so a mood plays as soon as it sets in.
    private void Restart()
    {
        _tick = 0;
        UpdatePace();
        _timer.IsEnabled = Animation != MascotAnimation.Off && IsVisible;
        // An act belongs to the calm mood it started in, and to being watched.
        DropAct();
        if (!_timer.IsEnabled)
        {
            _chewFramesLeft = 0;
            _reaction = Reaction.None;
            _look = default;
        }
    }
    /// <summary>The frame of the loop to draw now, or null for the mood's still pose.</summary>
    private int? Frame()
    {
        switch (Animation)
        {
            case MascotAnimation.Off:
                return null;
            case MascotAnimation.Lively:
                return _tick % (Mood == MascotMood.Calm ? CalmLoopFrames : LoopFrames);
        }

        if (Mood == MascotMood.Calm)
            return _tick % BlinkEvery == BlinkEvery - 1 ? BlinkFrame : null;
        return _tick % CycleFrames < BurstFrames ? _tick % LoopFrames : null;
    }

    protected override Size MeasureOverride(Size availableSize) => new((Carrot ? CarrotColumns : Columns) * Block, Rows * Block);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var frame = Frame();
        _drawnFrame = frame;
        var gauge = BodyGauge;
        var faded = gauge == MascotBodyGauge.None ? 0 : (int)Math.Round(Math.Clamp(Usage, 0, 100) / 100 * BodyRows);
        var stage = new Stage(drawingContext, BunnyEars, new Fade(gauge, faded));

        // A reaction comes before an act, and either takes the place of the mood's loop.
        if (_reaction != Reaction.None)
            DrawReaction(stage, _reaction, _reactionFrame, _signPercent);
        else if (_act is not null)
            DrawAct(stage);
        else
            DrawMood(stage, frame);

        if (Carrot)
            DrawCarrot(drawingContext);
    }
    /// <summary>How much of the creature has faded with the session's usage: the rows of it from the top, and how they're drawn.</summary>
    private readonly record struct Fade(MascotBodyGauge Style, int Rows)
    {
        public bool Any => Rows > 0;
    }

    /// <summary>What a frame is drawn on, and how the creature is turned out for it: with its ears or without, and how far faded.</summary>
    private readonly record struct Stage(DrawingContext Context, bool Ears, Fade Fade)
    {
        /// <summary>A painter for a frame in which the creature is moved by (<paramref name="shift"/>, <paramref name="lift"/>) blocks.</summary>
        public Painter Painter(int shift, int lift) => new(Context, shift, lift, Fade);
    }

    /// <summary>
    /// Draws blocks for one frame: <see cref="Draw"/> for the creature, which moves as a whole by
    /// (<paramref name="Shift"/>, <paramref name="Lift"/>) blocks, and <see cref="DrawStill"/>
    /// for what's around it rather than part of it, which stays put while it hops.
    /// <see cref="DrawFlesh"/> is for its body, the one thing that fades with the usage.
    /// </summary>
    private readonly record struct Painter(DrawingContext Context, int Shift, int Lift, Fade Fade = default)
    {
        public void Draw(Brush brush, int x, int y, int width, int height) => Fill(brush, x + Shift, y + Lift, width, height);

        public void DrawStill(Brush brush, int x, int y, int width, int height) => Fill(brush, x, y, width, height);

        /// <summary>
        /// A part of the creature's own body, which moves with it: the rows of the part above the
        /// level the body has faded to are drawn faded. The level goes by where the part is on
        /// the body, so it rides up and down with a hop.
        /// </summary>
        public void DrawFlesh(Brush brush, int x, int y, int width, int height)
        {
            // (An arm thrown up is above the body's top row: without the check it would count as faded with nothing used.)
            var faded = Fade.Any ? Math.Clamp(BodyTop + Fade.Rows - y, 0, height) : 0;
            if (faded > 0)
                FillFaded(brush, x + Shift, y + Lift, width, faded);
            if (faded < height)
                Fill(brush, x + Shift, y + faded + Lift, width, height - faded);
        }

        private void FillFaded(Brush brush, int x, int y, int width, int height)
        {
            switch (Fade.Style)
            {
                case MascotBodyGauge.Pale:
                    Context.PushOpacity(PaleOpacity);
                    Fill(brush, x, y, width, height);
                    Context.Pop();
                    break;
                case MascotBodyGauge.Shadow:
                    Fill(Shadow, x, y, width, height);
                    break;
                default:
                    // Every other block, like a checkerboard — one that's fixed to the body, so it
                    // doesn't flicker as the body moves.
                    for (var row = y; row < y + height; row++)
                    {
                        for (var column = x; column < x + width; column++)
                        {
                            if (((column - Shift + row - Lift) & 1) == 0)
                                Fill(brush, column, row, 1, 1);
                        }
                    }
                    break;
            }
        }

        // Column 0 of the drawing is one block in: the spare column on the left.
        private void Fill(Brush brush, int x, int y, int width, int height) =>
            Context.DrawRectangle(brush, null, new Rect((x + 1) * Block, y * Block, width * Block, height * Block));
    }
    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
