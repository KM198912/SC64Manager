using Microsoft.Extensions.DependencyInjection;

namespace NetGui;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
        InitializeDirectories();
	}

    private void InitializeDirectories()
    {
        try
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string baseDir = Path.Combine(documents, "SC64Manager");
            string[] subDirs = { "roms", "firmware", "binaries" };

            foreach (var sub in subDirs)
            {
                string path = Path.Combine(baseDir, sub);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }
        }
        catch { /* Best effort */ }
    }

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}