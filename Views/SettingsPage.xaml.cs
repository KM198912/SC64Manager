using NetGui.ViewModels;

namespace NetGui.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(MainViewModel vm)
	{
		InitializeComponent();
        BindingContext = vm;
	}
}
