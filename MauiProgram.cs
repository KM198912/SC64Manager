using Microsoft.Extensions.Logging;
using NetGui.Services;
using NetGui.ViewModels;
using NetGui.Views;
using CommunityToolkit.Maui;

namespace NetGui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<SC64Device>();
		builder.Services.AddSingleton<SettingsService>();
		builder.Services.AddSingleton<GamesDbService>();
		builder.Services.AddSingleton<ViewModels.MainViewModel>();
		builder.Services.AddSingleton<ViewModels.MemoryViewModel>();
		builder.Services.AddSingleton<MainPage>();
		builder.Services.AddSingleton<HardwarePage>();
		builder.Services.AddSingleton<MemoryPage>();
		builder.Services.AddTransient<UpdatePage>();
		builder.Services.AddTransient<AboutPage>();
		builder.Services.AddTransient<SettingsPage>();

		return builder.Build();
	}
}
