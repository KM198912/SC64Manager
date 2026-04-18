namespace NetGui;

public partial class MainPage : ContentPage
{
	public MainPage(ViewModels.MainViewModel vm)
	{
		InitializeComponent();
		BindingContext = vm;
	}
}
