using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Usagi.App.Views.SettingsPages;

public partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
        AppIconImage.Source = LoadIconFrame(48);
    }

    // An Image given the .ico itself shows its first frame, the 16px one, blown up and blurred.
    // The 48px frame draws the creature's blocks at 2px each, so they stay whole at 100%, 150%
    // and 200% scaling.
    private static BitmapFrame LoadIconFrame(int size)
    {
        var decoder = BitmapDecoder.Create(
            new Uri("pack://application:,,,/Assets/AppIcon.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return decoder.Frames.FirstOrDefault(f => f.PixelWidth == size) ?? decoder.Frames.MaxBy(f => f.PixelWidth)!;
    }
}
