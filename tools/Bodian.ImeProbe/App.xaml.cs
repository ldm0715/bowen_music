using Microsoft.UI.Xaml;

namespace Bodian.ImeProbe;

public partial class App : Application
{
    private ProbeWindow? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new ProbeWindow();
        _window.Activate();
    }
}
