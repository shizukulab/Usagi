using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Usagi.App.Views;

/// <summary>Where a dragged item would go, relative to the item under it.</summary>
public enum DropSide { None, Before, After }

/// <summary>An item that can tell whether the cursor is on it — in any of the places it's shown (see <see cref="DragReorder.TracksHover"/>).</summary>
public interface IHoverable
{
    bool IsHovered { get; set; }
}

/// <summary>
/// Lets the items of an ItemsControl be dragged into another order (<c>views:DragReorder.IsEnabled</c>),
/// along its <see cref="OrientationProperty"/>: the item under the cursor is marked with the side
/// it would go (<see cref="DropSideProperty"/>, on its container, for the item template to draw a
/// line), and the drop hands <see cref="MoveCommandProperty"/> the move as (from, to) indexes.
/// A press that doesn't become a drag is a click: <see cref="ClickCommandProperty"/>, given the item.
/// </summary>
public static class DragReorder
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(DragReorder), new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.RegisterAttached(
        "Orientation", typeof(Orientation), typeof(DragReorder), new PropertyMetadata(Orientation.Vertical));

    /// <summary>Given a <c>(int From, int To)</c> on a drop that moves an item.</summary>
    public static readonly DependencyProperty MoveCommandProperty = DependencyProperty.RegisterAttached(
        "MoveCommand", typeof(ICommand), typeof(DragReorder));

    /// <summary>Given the item on a press and release that didn't drag it.</summary>
    public static readonly DependencyProperty ClickCommandProperty = DependencyProperty.RegisterAttached(
        "ClickCommand", typeof(ICommand), typeof(DragReorder));

    /// <summary>On an item's container: the side the dragged item would be dropped on.</summary>
    public static readonly DependencyProperty DropSideProperty = DependencyProperty.RegisterAttached(
        "DropSide", typeof(DropSide), typeof(DragReorder), new PropertyMetadata(DropSide.None));

    /// <summary>On an item's container: whether it's the one being dragged.</summary>
    public static readonly DependencyProperty IsDraggedProperty = DependencyProperty.RegisterAttached(
        "IsDragged", typeof(bool), typeof(DragReorder), new PropertyMetadata(false));

    /// <summary>On an element whose data context is an <see cref="IHoverable"/>: tells it when the cursor is on the element.</summary>
    public static readonly DependencyProperty TracksHoverProperty = DependencyProperty.RegisterAttached(
        "TracksHover", typeof(bool), typeof(DragReorder), new PropertyMetadata(false, OnTracksHoverChanged));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(PressState), typeof(DragReorder));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);
    public static Orientation GetOrientation(DependencyObject d) => (Orientation)d.GetValue(OrientationProperty);
    public static void SetOrientation(DependencyObject d, Orientation value) => d.SetValue(OrientationProperty, value);
    public static ICommand? GetMoveCommand(DependencyObject d) => (ICommand?)d.GetValue(MoveCommandProperty);
    public static void SetMoveCommand(DependencyObject d, ICommand? value) => d.SetValue(MoveCommandProperty, value);
    public static ICommand? GetClickCommand(DependencyObject d) => (ICommand?)d.GetValue(ClickCommandProperty);
    public static void SetClickCommand(DependencyObject d, ICommand? value) => d.SetValue(ClickCommandProperty, value);
    public static DropSide GetDropSide(DependencyObject d) => (DropSide)d.GetValue(DropSideProperty);
    public static void SetDropSide(DependencyObject d, DropSide value) => d.SetValue(DropSideProperty, value);
    public static bool GetIsDragged(DependencyObject d) => (bool)d.GetValue(IsDraggedProperty);
    public static void SetIsDragged(DependencyObject d, bool value) => d.SetValue(IsDraggedProperty, value);
    public static bool GetTracksHover(DependencyObject d) => (bool)d.GetValue(TracksHoverProperty);
    public static void SetTracksHover(DependencyObject d, bool value) => d.SetValue(TracksHoverProperty, value);

    // The item pressed on, where, and whether it's become a drag.
    private sealed class PressState
    {
        public object? Item;
        public Point Start;
        public bool Dragging;
    }

    // What travels with the drag: only a drop on the same list takes it.
    private sealed record Payload(ItemsControl Source, object Item);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl list)
            return;

        list.PreviewMouseLeftButtonDown -= OnPress;
        list.PreviewMouseMove -= OnMove;
        list.PreviewMouseLeftButtonUp -= OnRelease;
        list.DragOver -= OnDragOver;
        list.DragLeave -= OnDragLeave;
        list.Drop -= OnDrop;
        if (!(bool)e.NewValue)
            return;

        list.AllowDrop = true;
        list.SetValue(StateProperty, new PressState());
        list.PreviewMouseLeftButtonDown += OnPress;
        list.PreviewMouseMove += OnMove;
        list.PreviewMouseLeftButtonUp += OnRelease;
        list.DragOver += OnDragOver;
        list.DragLeave += OnDragLeave;
        list.Drop += OnDrop;
    }

    private static PressState State(ItemsControl list) => (PressState)list.GetValue(StateProperty);

    private static FrameworkElement? ContainerAt(ItemsControl list, object source) =>
        source is DependencyObject element ? ItemsControl.ContainerFromElement(list, element) as FrameworkElement : null;

    private static void OnPress(object sender, MouseButtonEventArgs e)
    {
        var list = (ItemsControl)sender;
        var state = State(list);
        state.Item = ContainerAt(list, e.OriginalSource) is { } container ? list.ItemContainerGenerator.ItemFromContainer(container) : null;
        state.Start = e.GetPosition(list);
        state.Dragging = false;
    }

    private static void OnMove(object sender, MouseEventArgs e)
    {
        var list = (ItemsControl)sender;
        var state = State(list);
        if (state.Item is not { } item || state.Dragging || e.LeftButton != MouseButtonState.Pressed)
            return;

        var moved = e.GetPosition(list) - state.Start;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        state.Dragging = true;
        var container = list.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
        if (container is not null)
            SetIsDragged(container, true);
        try
        {
            DragDrop.DoDragDrop(container ?? list, new DataObject(typeof(Payload), new Payload(list, item)), DragDropEffects.Move);
        }
        finally
        {
            if (container is not null)
                SetIsDragged(container, false);
            ClearDropSides(list);
            state.Item = null;
        }
    }

    private static void OnRelease(object sender, MouseButtonEventArgs e)
    {
        var list = (ItemsControl)sender;
        var state = State(list);
        if (state.Item is { } item && !state.Dragging
            && ContainerAt(list, e.OriginalSource) is { } container && list.ItemContainerGenerator.ItemFromContainer(container) == item
            && GetClickCommand(list) is { } click && click.CanExecute(item))
        {
            click.Execute(item);
        }
        state.Item = null;
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        var list = (ItemsControl)sender;
        e.Handled = true;
        if (Target(list, e) is not var (container, side))
        {
            e.Effects = DragDropEffects.None;
            ClearDropSides(list);
            return;
        }
        e.Effects = DragDropEffects.Move;
        ClearDropSides(list, except: container);
        SetDropSide(container, side);
    }

    private static void OnDragLeave(object sender, DragEventArgs e)
    {
        var list = (ItemsControl)sender;
        // Leaving one item for the next raises this too; only off the list is the line taken away.
        var at = e.GetPosition(list);
        if (at.X < 0 || at.Y < 0 || at.X >= list.ActualWidth || at.Y >= list.ActualHeight)
            ClearDropSides(list);
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        var list = (ItemsControl)sender;
        e.Handled = true;
        var target = Target(list, e);
        ClearDropSides(list);
        if (target is not var (container, side) || e.Data.GetData(typeof(Payload)) is not Payload payload)
            return;

        var from = list.Items.IndexOf(payload.Item);
        var to = list.Items.IndexOf(list.ItemContainerGenerator.ItemFromContainer(container)) + (side == DropSide.After ? 1 : 0);
        if (from < to)
            to--; // taken out first, so everything after it moves up one
        if (from >= 0 && to != from && GetMoveCommand(list) is { } move && move.CanExecute((from, to)))
            move.Execute((from, to));
    }

    // The item the drag is over (not the dragged one itself) and the side of it the cursor is on.
    private static (FrameworkElement Container, DropSide Side)? Target(ItemsControl list, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(Payload)) is not Payload payload || payload.Source != list
            || ContainerAt(list, e.OriginalSource) is not { } container
            || list.ItemContainerGenerator.ItemFromContainer(container) == payload.Item)
        {
            return null;
        }
        var at = e.GetPosition(container);
        var before = GetOrientation(list) == Orientation.Horizontal ? at.X < container.ActualWidth / 2 : at.Y < container.ActualHeight / 2;
        return (container, before ? DropSide.Before : DropSide.After);
    }

    private static void ClearDropSides(ItemsControl list, FrameworkElement? except = null)
    {
        foreach (var item in list.Items)
        {
            if (list.ItemContainerGenerator.ContainerFromItem(item) is DependencyObject container && container != except)
                SetDropSide(container, DropSide.None);
        }
    }

    private static void OnTracksHoverChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;
        element.MouseEnter -= OnEnter;
        element.MouseLeave -= OnLeave;
        if ((bool)e.NewValue)
        {
            element.MouseEnter += OnEnter;
            element.MouseLeave += OnLeave;
        }
    }

    private static void OnEnter(object sender, MouseEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is IHoverable item)
            item.IsHovered = true;
    }

    private static void OnLeave(object sender, MouseEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is IHoverable item)
            item.IsHovered = false;
    }
}
