using System.Windows;
using System.Windows.Controls.Primitives;

namespace Usagi.App.Views;

/// <summary>
/// Places a popup under its target with their left edges lined up, whatever Windows' menu
/// alignment is. <see cref="PlacementMode.Bottom"/> follows that setting (Tablet PC Settings >
/// Handedness, on by default on some touch and pen PCs) and lines the right edges up instead,
/// which throws off a popup whose offsets were worked out from the left.
/// </summary>
public static class PopupPlacement
{
    public static readonly DependencyProperty BelowLeftAlignedProperty = DependencyProperty.RegisterAttached(
        "BelowLeftAligned", typeof(bool), typeof(PopupPlacement), new PropertyMetadata(false, OnBelowLeftAlignedChanged));

    public static bool GetBelowLeftAligned(Popup popup) => (bool)popup.GetValue(BelowLeftAlignedProperty);

    public static void SetBelowLeftAligned(Popup popup, bool value) => popup.SetValue(BelowLeftAlignedProperty, value);

    private static void OnBelowLeftAlignedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Popup popup || e.NewValue is not true)
            return;

        popup.Placement = PlacementMode.Custom;
        // Below the target, or above it where there's no room below (near the bottom of the screen).
        // The popup's HorizontalOffset and VerticalOffset are added to these by the popup itself.
        popup.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
        [
            new CustomPopupPlacement(new Point(0, targetSize.Height), PopupPrimaryAxis.Vertical),
            new CustomPopupPlacement(new Point(0, -popupSize.Height), PopupPrimaryAxis.Vertical)
        ];
    }
}
