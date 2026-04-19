using System.Net.Http.Json;
using System.Text.Json;
using System.IO.Compression;

namespace NetGui.Services;

public record UpdateAsset(string Name, string Url);

public class UpdateService
{
    private readonly HttpClient _httpClient;
    private const string GitHubApiUrl = "https://api.github.com/repos/Polprzewodnikowy/SummerCart64/releases/latest";

    public UpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SC64Manager/1.0");
    }

    public async Task<(UpdateAsset? deployer, UpdateAsset? firmware)> GetLatestReleaseAssetsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(GitHubApiUrl);
            if (!response.IsSuccessStatusCode) return (null, null);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("assets", out var assets)) return (null, null);

            UpdateAsset? deployer = null;
            UpdateAsset? firmware = null;

            bool is64Bit = Environment.Is64BitOperatingSystem;

            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                string url = asset.GetProperty("browser_download_url").GetString() ?? "";

                // Logic for Deployer
                if (name.StartsWith("sc64-deployer-windows") && name.EndsWith(".zip"))
                {
                    bool is32BitAsset = name.Contains("32bit");
                    if (is64Bit && !is32BitAsset) deployer = new UpdateAsset(name, url);
                    else if (!is64Bit && is32BitAsset) deployer = new UpdateAsset(name, url);
                }

                // Logic for Firmware
                if (name.StartsWith("sc64-firmware") && name.EndsWith(".bin"))
                {
                    firmware = new UpdateAsset(name, url);
                }
            }

            return (deployer, firmware);
        }
        catch { return (null, null); }
    }

    public async Task<string?> DownloadAndExtractAsync(UpdateAsset asset, string targetDir)
    {
        try
        {
            if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
            Directory.CreateDirectory(targetDir);

            string zipPath = Path.Combine(targetDir, asset.Name);
            var data = await _httpClient.GetByteArrayAsync(asset.Url);
            await File.WriteAllBytesAsync(zipPath, data);

            ZipFile.ExtractToDirectory(zipPath, targetDir);
            
            // Find the EXE in the extracted files
            return Directory.GetFiles(targetDir, "*.exe", SearchOption.AllDirectories).FirstOrDefault();
        }
        catch { return null; }
    }

    public async Task<string?> DownloadFirmwareAsync(UpdateAsset asset, string targetDir)
    {
        try
        {
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
            
            string filePath = Path.Combine(targetDir, asset.Name);
            var data = await _httpClient.GetByteArrayAsync(asset.Url);
            await File.WriteAllBytesAsync(filePath, data);
            
            return filePath;
        }
        catch { return null; }
    }
}
