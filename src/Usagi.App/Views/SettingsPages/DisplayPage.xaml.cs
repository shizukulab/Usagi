using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Usagi.App.ViewModels;

namespace Usagi.App.Views.SettingsPages;

public partial class DisplayPage : UserControl
{
    private static readonly Duration SwitchDuration = TimeSpan.FromMilliseconds(250);
    private const double SwitchDistance = 24;

    private SettingsViewModel? _viewModel;

    public DisplayPage()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_viewModel is not null)
                _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel = e.NewValue as SettingsViewModel;
            if (_viewModel is not null)
                _viewModel.PropertyChanged += OnViewModelChanged;
            ShowTab(animate: false);
        };
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SelectedSurface))
            ShowTab(animate: true);
    }

    // Shows the chosen tab's content, kept built, and hides the other's. A switch fades the new one
    // in as it slides the short way into place: from the right for the taskbar (the tab on the
    // right), from the left for the window, like the tabs' card moving across. Not when the page is
    // first shown, nor with Windows' animations off.
    private void ShowTab(bool animate)
    {
        var taskbar = _viewModel?.SelectedSurface == DisplaySurface.Taskbar;
        var shown = taskbar ? TaskbarContent : WindowContent;
        (taskbar ? WindowContent : TaskbarContent).Visibility = Visibility.Collapsed;
        shown.Visibility = Visibility.Visible;
        if (!animate || !IsLoaded || !SystemParameters.ClientAreaAnimation)
            return;

        var offset = new TranslateTransform(taskbar ? SwitchDistance : -SwitchDistance, 0);
        shown.RenderTransform = offset;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        offset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, SwitchDuration) { EasingFunction = ease });
        shown.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, SwitchDuration) { EasingFunction = ease });
    }
}
