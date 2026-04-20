using Microsoft.Maui.Storage;

namespace NetGui.Services;

public class SettingsService
{
    private const string RomPathKey = "default_rom_path";
    private const string FirmwarePathKey = "default_firmware_path";
    private const string SelectedPortKey = "last_selected_port";
    private const string DeployerPathKey = "sc64_deployer_path";
    private const string GamesDbApiKeyKey = "gamesdb_api_key";
    private const string FallbackEnabledKey = "metadata_fallback_enabled";

    private string GetDefaultPath(string subFolder) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SC64Manager", subFolder);

    public string GamesDbApiKey
    {
        get => Preferences.Default.Get(GamesDbApiKeyKey, string.Empty);
        set => Preferences.Default.Set(GamesDbApiKeyKey, value);
    }

    public bool MetadataFallbackEnabled
    {
        get => Preferences.Default.Get(FallbackEnabledKey, false);
        set => Preferences.Default.Set(FallbackEnabledKey, value);
    }

    public string DefaultRomPath
    {
        get => Preferences.Default.Get(RomPathKey, GetDefaultPath("roms"));
        set => Preferences.Default.Set(RomPathKey, value);
    }

    public string DefaultFirmwarePath
    {
        get => Preferences.Default.Get(FirmwarePathKey, GetDefaultPath("firmware"));
        set => Preferences.Default.Set(FirmwarePathKey, value);
    }

    public string LastSelectedPort
    {
        get => Preferences.Default.Get(SelectedPortKey, string.Empty);
        set => Preferences.Default.Set(SelectedPortKey, value);
    }

    public string DeployerPath
    {
        get
        {
            var saved = Preferences.Default.Get(DeployerPathKey, string.Empty);
            if (!string.IsNullOrEmpty(saved) && File.Exists(saved)) return saved;

            // 1. Check centralized binaries folder
            string binPath = Path.Combine(GetDefaultPath("binaries"), "sc64deployer.exe");
            if (File.Exists(binPath)) return binPath;

            // 2. Fallback to auto-discovery in cache
            string cachePath = Path.Combine(FileSystem.CacheDirectory, "Updates", "deployer", "sc64deployer.exe");
            if (File.Exists(cachePath)) return cachePath;

            return string.Empty;
        }
        set => Preferences.Default.Set(DeployerPathKey, value);
    }
}
