using NetGui.ViewModels;
using System.ComponentModel;

namespace NetGui;

public partial class MainPage : ContentPage
{
    private MainViewModel? _viewModel;

	public MainPage(MainViewModel vm)
	{
		InitializeComponent();
        BindingContext = vm;
	}

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        // Ensure we handle the subscription correctly during theme/context changes
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (BindingContext is MainViewModel vm)
        {
            _viewModel = vm;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.LogText))
        {
            // Windows-Specific Force Autoscroll:
            // We dispatch to the back of the UI queue and use a coordinate-max target.
            // This bypasses 'Element Not Found' and 'Measurement Stale' errors in WinUI 3.
            Dispatcher.Dispatch(async () => 
            {
                try
                {
                    // Delay captures the "next frame" after the text has physically expanded the label
                    await Task.Delay(100);
                    
                    // 999999 is a "Safe Infinity" for Windows Scrollbars - it forces them to the mechanical bottom.
                    await LogScrollView.ScrollToAsync(0, 999999, true);
                }
                catch
                {
                    // Silent fail for background updates
                }
            });
        }
    }
}
