using System.Windows.Controls;
using System.Windows.Input;

namespace Usagi.App.Views.SettingsPages;

public partial class NotificationsPage : UserControl
{
    public NotificationsPage() => InitializeComponent();

    // Keeps the box that adds a notification threshold digits-only.
    private void OnDigitsOnlyPreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !e.Text.All(char.IsAsciiDigit);
}
