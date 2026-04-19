namespace NetGui.Views;

public partial class AboutPage : ContentPage
{
	public AboutPage()
	{
		InitializeComponent();
	}

    private async void OnOfficialSiteClicked(object? sender, EventArgs e)
    {
        await Launcher.Default.OpenAsync("https://summercart64.dev/");
    }

    private async void OnRepoClicked(object? sender, EventArgs e)
    {
        await Launcher.Default.OpenAsync("https://github.com/KM198912/SC64Manager");
    }
}
