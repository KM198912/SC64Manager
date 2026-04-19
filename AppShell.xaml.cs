using NetGui.ViewModels;
using NetGui.Views;

namespace NetGui;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

        // Register Routes for Navigation
        Routing.RegisterRoute(nameof(AboutPage), typeof(AboutPage));
        Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
        Routing.RegisterRoute(nameof(UpdatePage), typeof(UpdatePage));

        // Inject ViewModel for MenuBar Commands
        BindingContext = ServiceProviderHelper.GetService<MainViewModel>();
	}

    private void OnExitClicked(object? sender, EventArgs e)
    {
        Application.Current?.Quit();
    }
}

// Helper to access DI from Shell (which is often created outside standard DI flow in some Maui versions)
public static class ServiceProviderHelper
{
    public static T GetService<T>() where T : notnull => 
        Application.Current!.Handler!.MauiContext!.Services.GetRequiredService<T>();
}
