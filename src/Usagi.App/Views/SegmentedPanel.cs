using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Usagi.App.Views;

/// <summary>
/// The segments of a segmented control (SegmentedTrackStyle / SegmentedButtonStyle in
/// Themes/SettingsStyles.xaml): its RadioButtons side by side, each as wide as the widest, over a
/// raised card that marks the chosen one and slides to the next one chosen. The slide plays only
/// on a choice made while the control is on screen; the card is simply in place when it's first
/// shown, and it doesn't move at all when Windows' animations are off.
/// </summary>
public sealed class SegmentedPanel : Panel
{
    private static readonly Duration SlideDuration = TimeSpan.FromMilliseconds(220);

    private readonly Border _card = new() { CornerRadius = new CornerRadius(5), IsHitTestVisible = false };
    private readonly TranslateTransform _cardOffset = new();

    // Where the card is, or is sliding to; NaN until it's first placed.
    private double _target = double.NaN;

    public SegmentedPanel()
    {
        _card.SetResourceReference(Border.BackgroundProperty, "ControlBackgroundBrush");
        _card.SetResourceReference(EffectProperty, "ControlShadowEffect");
        _card.RenderTransform = _cardOffset;
        // First of the children, so it's drawn under the segments.
        InternalChildren.Add(_card);
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => MoveCard(animate: IsLoaded)));
    }

    private IEnumerable<UIElement> Segments => InternalChildren.Cast<UIElement>().Where(child => child != _card);

    private int CheckedIndex => Segments.Select((segment, i) => (segment, i))
        .FirstOrDefault(s => s.segment is ToggleButton { IsChecked: true }, (null!, -1)).i;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0.0;
        var height = 0.0;
        var count = 0;
        foreach (var segment in Segments)
        {
            segment.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            width = Math.Max(width, segment.DesiredSize.Width);
            height = Math.Max(height, segment.DesiredSize.Height);
            count++;
        }
        _card.Measure(new Size(width, height));
        return new Size(width * count, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = Segments.Count();
        var width = count == 0 ? 0 : finalSize.Width / count;
        var i = 0;
        foreach (var segment in Segments)
            segment.Arrange(new Rect(width * i++, 0, width, finalSize.Height));

        _card.Arrange(new Rect(0, 0, width, finalSize.Height));
        // A resize (or the first layout) puts the card where it belongs, without a slide.
        MoveCard(animate: false);
        return finalSize;
    }

    // Moves the card under the chosen segment: sliding there for a choice made on screen,
    // straight there otherwise (and when it's already there or on its way, not at all).
    private void MoveCard(bool animate)
    {
        var index = CheckedIndex;
        _card.Visibility = index < 0 ? Visibility.Hidden : Visibility.Visible;
        if (index < 0)
            return;

        var to = _card.RenderSize.Width * index;
        if (to == _target)
            return;

        var slide = animate && !double.IsNaN(_target) && SystemParameters.ClientAreaAnimation;
        _target = to;
        if (slide)
        {
            // From wherever it is now, mid-slide included.
            _cardOffset.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(to, SlideDuration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        else
        {
            _cardOffset.BeginAnimation(TranslateTransform.XProperty, null);
            _cardOffset.X = to;
        }
    }
}
