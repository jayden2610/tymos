using Microsoft.UI.Xaml;

namespace TymosPill;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            Log.Write($"OnLaunched failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
    }
}
