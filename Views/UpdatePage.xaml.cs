using NetGui.Services;
using NetGui.ViewModels;
using System.Diagnostics;
using System.Text;

namespace NetGui.Views;

public partial class UpdatePage : ContentPage
{
    private readonly UpdateService _updater;
    private readonly MainViewModel _mainVm;
    private UpdateAsset? _targetDeployer;
    private UpdateAsset? _targetFirmware;
    private Process? _deployerProcess;
    private bool _isBusy;

    public UpdatePage() : this(ServiceProviderHelper.GetService<MainViewModel>())
    {
    }

    public UpdatePage(MainViewModel mainVm)
	{
		InitializeComponent();
        _mainVm = mainVm;
        _updater = new UpdateService();
	}

    private void LogTerm(string text)
    {
        MainThread.BeginInvokeOnMainThread(() => {
            TermOutput.Text += $"{DateTime.Now:HH:mm:ss} > {text}\n";
            TermScroll.ScrollToAsync(0, 999999, false);
        });
    }

    private async void OnCheckUpdatesClicked(object? sender, EventArgs e)
    {
        if (_isBusy) return;
        _isBusy = true;
        SetUIState(true);
        
        LogTerm("Querying GitHub API for latest release...");
        WorkingIndicator.IsRunning = true;
        StatusLabel.Text = "Checking for updates...";

        var (deployer, firmware) = await _updater.GetLatestReleaseAssetsAsync();

        if (deployer == null || firmware == null)
        {
            LogTerm("ERROR: Could not fetch release metadata. Check internet connection.");
            StatusLabel.Text = "Update check failed.";
        }
        else
        {
            _targetDeployer = deployer;
            _targetFirmware = firmware;
            LogTerm($"FOUND: {deployer.Name}");
            LogTerm($"FOUND: {firmware.Name}");
            LogTerm("System ready for deployment.");
            StatusLabel.Text = "Updates found.";
            BtnStart.IsEnabled = true;
        }

        WorkingIndicator.IsRunning = false;
        _isBusy = false;
        SetUIState(false);
    }

    private async void OnStartDeploymentClicked(object? sender, EventArgs e)
    {
        if (_isBusy || _targetDeployer == null || _targetFirmware == null) return;
        
        _isBusy = true;
        SetUIState(true);
        WorkingIndicator.IsRunning = true;

        LogTerm("Preparing deployment environment...");
        string baseDir = Path.Combine(FileSystem.CacheDirectory, "Updates");
        
        // 1. Download & Extract
        LogTerm($"Downloading {_targetDeployer.Name}...");
        string? exePath = await _updater.DownloadAndExtractAsync(_targetDeployer, Path.Combine(baseDir, "deployer"));
        
        LogTerm($"Downloading {_targetFirmware.Name}...");
        string? fwPath = await _updater.DownloadFirmwareAsync(_targetFirmware, Path.Combine(baseDir, "firmware"));

        if (string.IsNullOrEmpty(exePath) || string.IsNullOrEmpty(fwPath))
        {
            LogTerm("ERROR: Asset preparation failed.");
            _isBusy = false;
            WorkingIndicator.IsRunning = false;
            SetUIState(false);
            return;
        }

        // 2. Hardware Safety Guard
        LogTerm("DISCONNECTING serial bridge for exclusive hardware access...");
        _mainVm.DisconnectCommand.Execute(null);

        // 3. Launch Deployer
        RunDeployer(exePath, fwPath);
    }

    private void RunDeployer(string exe, string fw)
    {
        try
        {
            LogTerm("INITIATING sc64-deployer...");
            
            _deployerProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"firmware update \"{fw}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true
                }
            };

            _deployerProcess.Start();

            // Direct stream reading to catch prompts without newlines
            Task.Run(() => ReadStreamAsync(_deployerProcess.StandardOutput));
            Task.Run(() => ReadStreamAsync(_deployerProcess.StandardError, "[ERR] "));

            StatusLabel.Text = "Deployer running...";
        }
        catch (Exception ex)
        {
            LogTerm($"FATAL: Failed to launch process. {ex.Message}");
            EndDeployment();
        }
    }

    private async Task ReadStreamAsync(StreamReader reader, string prefix = "")
    {
        char[] buffer = new char[1024];
        while (true)
        {
            int read = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (read == 0) break; // End of stream
            
            string data = new string(buffer, 0, read);
            MainThread.BeginInvokeOnMainThread(() => {
                TermOutput.Text += $"{prefix}{data}";
                TermScroll.ScrollToAsync(0, 999999, false);
                HandleProcessOutput(data);
            });
        }
    }

    private void HandleProcessOutput(string data)
    {
        // Detect confirmation prompt
        if (data.Contains("want to perform the upgrade?") || data.Contains("[y/N]"))
        {
            MainThread.BeginInvokeOnMainThread(() => {
                BtnConfirm.IsVisible = true;
                StatusLabel.Text = "WAITING FOR CONFIRMATION";
            });
        }

        // Detect completion/error states to trigger UI reset if necessary
        if (data.Contains("Upgrade successful") || data.Contains("Upgrade failed") || data.Contains("done"))
        {
            EndDeployment();
        }
    }

    private void OnConfirmClicked(object? sender, EventArgs e)
    {
        if (_deployerProcess != null && !_deployerProcess.HasExited)
        {
            _deployerProcess.StandardInput.WriteLine("y");
            LogTerm("USER CONFIRMED UPGRADE. Sending 'y'...");
            BtnConfirm.IsVisible = false;
        }
    }

    private void EndDeployment()
    {
        MainThread.BeginInvokeOnMainThread(() => {
            _isBusy = false;
            WorkingIndicator.IsRunning = false;
            SetUIState(false);
            StatusLabel.Text = "Deployment process concluded.";
            LogTerm("Process terminated. You can now safely exit the console.");
        });
    }

    private void SetUIState(bool busy)
    {
        BtnCheck.IsEnabled = !busy;
        BtnStart.IsEnabled = !busy && _targetDeployer != null;
        BtnClose.IsEnabled = !busy;
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        if (_isBusy)
        {
            bool answer = await DisplayAlertAsync("Warning", "Deployment is in progress. Closing now may result in incomplete setup. Exit anyway?", "Yes", "No");
            if (!answer) return;
        }

        if (_deployerProcess != null && !_deployerProcess.HasExited)
        {
            _deployerProcess.Kill();
        }

        await Shell.Current.GoToAsync("..");
    }
}
