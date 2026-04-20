using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NetGui.Models;

namespace NetGui.Services;

public class GamesDbMetadata
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? BoxArtUrl { get; set; }
}

public class GamesDbService
{
    private readonly HttpClient _http;
    private readonly SettingsService _settings;
    private readonly Action<string>? _log;
    private const string BaseUrl = "https://api.thegamesdb.net";

    public GamesDbService(SettingsService settings, Action<string>? log = null)
    {
        _http = new HttpClient();
        _settings = settings;
        _log = log;
    }

    private void Log(string msg) => _log?.Invoke($"GDB: {msg}");

    public async Task<GamesDbMetadata?> GetGameMetadataAsync(string romId, string romName)
    {
        string apiKey = _settings.GamesDbApiKey;
        if (string.IsNullOrEmpty(apiKey)) return null;

        Log($"Attempting metadata resolution for [{romId}] / {romName}");

        // 1. Try searching by ROM ID first (Very accurate on TGDB)
        var meta = await FetchMetadataInternalAsync(romId, apiKey);
        if (meta != null && !string.IsNullOrEmpty(meta.BoxArtUrl)) 
        {
            Log($"Success (Art Found) via ROM ID search for [{romId}]");
            return meta;
        }

        // 2. Fall back to cleaned rom name
        string searchName = CleanRomName(romName);
        Log($"ROM ID search failed or missing art. Trying fallback with cleaned name: {searchName}");
        return await FetchMetadataInternalAsync(searchName, apiKey);
    }

    private async Task<GamesDbMetadata?> FetchMetadataInternalAsync(string query, string apiKey)
    {
        try
        {
            // Search by Name/ID for N64 (Platform ID 3)
            // Note: 'overview' is NOT included by default in v1.1, must be explicitly requested.
            string url = $"{BaseUrl}/v1.1/Games/ByGameName?apikey={apiKey}&name={Uri.EscapeDataString(query)}&filter%5Bplatform%5D=3&include=boxart&fields=overview";
            
            var response = await _http.GetFromJsonAsync<GdbRoot>(url);
            if (response?.Data?.Games == null || response.Data.Games.Count == 0)
            {
                Log($"Query '{query}' returned 0 results.");
                return null;
            }

            // Intelligence Pass: Scour results and pick the one with the best description 
            // (Avoids variants like "Player's Choice" that often have empty overviews)
            var game = response.Data.Games
                .OrderByDescending(g => g.Description?.Length ?? 0)
                .ThenBy(g => g.Title?.Length ?? 0)
                .FirstOrDefault();

            if (game == null) return null;
            
            Log($"Best Match: {game.Title} (ID: {game.Id}), Description: {(!string.IsNullOrEmpty(game.Description) ? "FOUND" : "MISSING")}");
            
            var meta = new GamesDbMetadata
            {
                Title = game.Title,
                Description = game.Description
            };

            // Resolve Box Art
            if (response.Include?.Boxart?.Data != null)
            {
                string gameIdKey = game.Id.ToString();
                Log($"Searching box art for key '{gameIdKey}' in {response.Include.Boxart.Data.Count} entries...");

                if (response.Include.Boxart.Data.TryGetValue(gameIdKey, out var artList))
                {
                    Log($"Found {artList.Count} art entries for this game.");
                    var frontArt = artList.FirstOrDefault(a => a.Side == "front");
                    if (frontArt != null)
                    {
                        string? baseArtUrl = response.Data?.BaseUrl?.Medium ?? "https://cdn.thegamesdb.net/images/medium/";
                        
                        meta.BoxArtUrl = baseArtUrl + frontArt.Filename;
                        Log($"Resolved BoxArtUrl: {meta.BoxArtUrl}");
                    }
                    else
                    {
                        Log("No 'front' side box art found in list.");
                    }
                }
                else
                {
                    Log($"Key '{gameIdKey}' not found in include.boxart.data.");
                }
            }
            else
            {
                Log("No 'include.boxart' data found in response.");
            }

            return meta;
        }
        catch (Exception ex)
        {
            Log($"API Error: {ex.Message}");
            return null;
        }
    }

    private string CleanRomName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        // 1. Remove Extension
        if (name.Contains('.')) name = name.Substring(0, name.LastIndexOf('.'));

        // 2. Remove all contents within [] and ()
        // Example: Turok - Dinosaur Hunter (Germany) (Rev 2) -> Turok - Dinosaur Hunter
        var result = new StringBuilder();
        int depth = 0;
        foreach (char c in name)
        {
            if (c == '(' || c == '[') depth++;
            else if (c == ')' || c == ']') depth = Math.Max(0, depth - 1);
            else if (depth == 0) result.Append(c);
        }

        return result.ToString().Trim();
    }

    public async Task<bool> TestConnectionAsync()
    {
        string apiKey = _settings.GamesDbApiKey;
        if (string.IsNullOrEmpty(apiKey)) return false;

        try
        {
            // Simple platform list request to verify key
            var url = $"{BaseUrl}/v1/Platforms?apikey={apiKey}";
            var response = await _http.GetAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // JSON Model Classes
    private class GdbRoot
    {
        [JsonPropertyName("data")] public GdbData? Data { get; set; }
        [JsonPropertyName("include")] public GdbInclude? Include { get; set; }
    }

    private class GdbData
    {
        [JsonPropertyName("games")] public List<GdbGame>? Games { get; set; }
        [JsonPropertyName("base_url")] public GdbBaseUrl? BaseUrl { get; set; }
    }

    private class GdbGame
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("game_title")] public string? Title { get; set; }
        [JsonPropertyName("overview")] public string? Description { get; set; }
    }

    private class GdbBaseUrl
    {
        [JsonPropertyName("medium")] public string? Medium { get; set; }
        [JsonPropertyName("original")] public string? Original { get; set; }
    }

    private class GdbInclude
    {
        [JsonPropertyName("boxart")] public GdbBoxArtContainer? Boxart { get; set; }
    }

    private class GdbBoxArtContainer
    {
        [JsonPropertyName("data")] public Dictionary<string, List<GdbBoxArt>>? Data { get; set; }
    }

    private class GdbBoxArt
    {
        [JsonPropertyName("side")] public string? Side { get; set; }
        [JsonPropertyName("filename")] public string? Filename { get; set; }
    }
}
