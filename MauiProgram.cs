using Microsoft.Extensions.Logging;
using NetGui.Services;
using NetGui.ViewModels;
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
		builder.Services.AddSingleton<ViewModels.MainViewModel>();
		builder.Services.AddSingleton<MainPage>();

		return builder.Build();
	}
}
