using NetGui.ViewModels;

namespace NetGui.Views;

public partial class HardwarePage : ContentPage
{
	public HardwarePage(MainViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}
}
