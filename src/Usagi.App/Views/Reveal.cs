using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Usagi.App.Views;

/// <summary>How an element comes and goes with <see cref="Reveal"/>.</summary>
public enum RevealMode
{
    /// <summary>Folds up to nothing in height, so what's under it moves up as it goes.</summary>
    Vertical,

    /// <summary>Narrows to nothing, so what's beside it closes the gap as it goes.</summary>
    Horizontal,

    /// <summary>Grows from a point with a little overshoot, and shrinks back to it: for a badge.</summary>
    Pop,

    /// <summary>Only fades, taking its space as it is.</summary>
    Fade
}

/// <summary>
/// Shows and hides an element with motion rather than all at once (<c>views:Reveal.IsShown</c>
/// in place of a Visibility binding): it fades as it folds, narrows or pops (<see cref="RevealMode"/>),
/// and is collapsed once gone. In the manner of Fluent's motion — what arrives decelerates into
/// place, what leaves accelerates away and quicker. Only on screen: an element is simply shown or
/// hidden as it's first laid out, and always when Windows' animations are off.
/// <para>
/// The folding scales the element's layout, not its margin: put a margin inside it (a padding)
/// for it to fold away with the rest.
/// </para>
/// </summary>
public static class Reveal
{
    private static readonly Duration ShowDuration = TimeSpan.FromMilliseconds(250);
    private static readonly Duration HideDuration = TimeSpan.FromMilliseconds(167);

    public static readonly DependencyProperty IsShownProperty = DependencyProperty.RegisterAttached(
        "IsShown", typeof(bool), typeof(Reveal), new PropertyMetadata(true, OnIsShownChanged));

    public static readonly DependencyProperty ModeProperty = DependencyProperty.RegisterAttached(
        "Mode", typeof(RevealMode), typeof(Reveal), new PropertyMetadata(RevealMode.Vertical));

    /// <summary>How long it waits before it starts to show: a few elements given a growing delay come in one after another.</summary>
    public static readonly DependencyProperty DelayProperty = DependencyProperty.RegisterAttached(
        "Delay", typeof(TimeSpan), typeof(Reveal), new PropertyMetadata(TimeSpan.Zero));

    public static bool GetIsShown(DependencyObject element) => (bool)element.GetValue(IsShownProperty);

    public static void SetIsShown(DependencyObject element, bool value) => element.SetValue(IsShownProperty, value);

    public static RevealMode GetMode(DependencyObject element) => (RevealMode)element.GetValue(ModeProperty);

    public static void SetMode(DependencyObject element, RevealMode value) => element.SetValue(ModeProperty, value);

    public static TimeSpan GetDelay(DependencyObject element) => (TimeSpan)element.GetValue(DelayProperty);

    public static void SetDelay(DependencyObject element, TimeSpan value) => element.SetValue(DelayProperty, value);

    private static void OnIsShownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;

        var show = (bool)e.NewValue;
        if (element.IsLoaded && SystemParameters.ClientAreaAnimation)
            Animate(element, show);
        else
            Place(element, show);
    }

    // Straight to how it ends up.
    private static void Place(FrameworkElement element, bool show)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (Scale(element, create: false) is { } scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = scale.ScaleY = 1;
        }
        element.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void Animate(FrameworkElement element, bool show)
    {
        var mode = GetMode(element);
        var arriving = show && element.Visibility != Visibility.Visible;
        // From nothing if it's arriving; otherwise from wherever it is, a reversal mid-way included.
        double? from = arriving ? 0 : null;
        var duration = show ? ShowDuration : HideDuration;
        IEasingFunction ease = show switch
        {
            true when mode == RevealMode.Pop => new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 },
            true => new CubicEase { EasingMode = EasingMode.EaseOut },
            false => new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var delay = show ? GetDelay(element) : TimeSpan.Zero;
        var to = show ? 1.0 : 0.0;

        if (arriving)
            element.Visibility = Visibility.Visible;

        var fade = Animation(from, to, duration, delay, new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn });
        if (!show)
        {
            fade.Completed += (_, _) =>
            {
                // Unless it's been asked back meanwhile.
                if (!GetIsShown(element))
                    Place(element, show: false);
            };
        }
        element.BeginAnimation(UIElement.OpacityProperty, fade);

        if (mode == RevealMode.Fade || Scale(element, create: true) is not { } scale)
            return;
        if (mode is RevealMode.Horizontal or RevealMode.Pop)
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Animation(from, to, duration, delay, ease));
        if (mode is RevealMode.Vertical or RevealMode.Pop)
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Animation(from, to, duration, delay, ease));
    }

    private static AnimationTimeline Animation(double? from, double to, Duration duration, TimeSpan delay, IEasingFunction ease)
    {
        // Held at its start through the delay: a plain animation that hasn't begun would leave it
        // at its full value until then.
        if (from is { } start && delay > TimeSpan.Zero)
        {
            return new DoubleAnimationUsingKeyFrames
            {
                KeyFrames =
                {
                    new DiscreteDoubleKeyFrame(start, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                    new DiscreteDoubleKeyFrame(start, KeyTime.FromTimeSpan(delay)),
                    new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(delay + duration.TimeSpan), ease)
                }
            };
        }

        var animation = new DoubleAnimation(to, duration) { EasingFunction = ease };
        if (from is { } value)
            animation.From = value;
        return animation;
    }

    // The scale it folds with, on its layout so that what's around it moves along.
    private static ScaleTransform? Scale(FrameworkElement element, bool create)
    {
        if (element.LayoutTransform is ScaleTransform { IsFrozen: false } scale)
            return scale;
        if (!create)
            return null;
        scale = new ScaleTransform(1, 1);
        element.LayoutTransform = scale;
        return scale;
    }
}
