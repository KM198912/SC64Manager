using NetGui.ViewModels;

namespace NetGui.Views;

public partial class MemoryPage : ContentPage
{
	public MemoryPage(MemoryViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}
}
