using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Usagi.App.ViewModels;

namespace Usagi.App.Views;

/// <summary>
/// The usage rows as the compact flyout and the taskbar bars draw them, laid out by a
/// <see cref="PartLayout"/>; the creature, given as <see cref="Mascot"/>, goes where the layout
/// puts it. The colors are the place's own.
/// </summary>
public partial class UsageLineView : UserControl
{
    public UsageLineView() => InitializeComponent();

    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(IEnumerable), typeof(UsageLineView));

    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(PartLayout), typeof(UsageLineView), new PropertyMetadata(PartLayout.Default));

    public static readonly DependencyProperty MascotProperty = DependencyProperty.Register(
        nameof(Mascot), typeof(object), typeof(UsageLineView));

    public static readonly DependencyProperty PrimaryBrushProperty = DependencyProperty.Register(
        nameof(PrimaryBrush), typeof(Brush), typeof(UsageLineView));

    public static readonly DependencyProperty SecondaryBrushProperty = DependencyProperty.Register(
        nameof(SecondaryBrush), typeof(Brush), typeof(UsageLineView));

    public static readonly DependencyProperty SecondaryOpacityProperty = DependencyProperty.Register(
        nameof(SecondaryOpacity), typeof(double), typeof(UsageLineView), new PropertyMetadata(1.0));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(UsageLineView));

    /// <summary>The rows (<see cref="UsageRowViewModel"/>s), one line each.</summary>
    public IEnumerable? Rows
    {
        get => (IEnumerable?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public PartLayout Layout
    {
        get => (PartLayout)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    /// <summary>The creature, shown if the layout has it: one instance, kept as the layout changes, so it carries on with what it's doing.</summary>
    public object? Mascot
    {
        get => GetValue(MascotProperty);
        set => SetValue(MascotProperty, value);
    }

    /// <summary>The percentage's color.</summary>
    public Brush? PrimaryBrush
    {
        get => (Brush?)GetValue(PrimaryBrushProperty);
        set => SetValue(PrimaryBrushProperty, value);
    }

    /// <summary>The names' and reset times' color, and how opaque they are.</summary>
    public Brush? SecondaryBrush
    {
        get => (Brush?)GetValue(SecondaryBrushProperty);
        set => SetValue(SecondaryBrushProperty, value);
    }

    public double SecondaryOpacity
    {
        get => (double)GetValue(SecondaryOpacityProperty);
        set => SetValue(SecondaryOpacityProperty, value);
    }

    /// <summary>The bars' track.</summary>
    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }
}
