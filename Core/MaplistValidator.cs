using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace cs2_rockthevote
{
    public class WorkshopMapValidator(MapLister mapLister, ILogger<WorkshopMapValidator> logger) : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<WorkshopMapValidator> _logger = logger;
        private GeneralConfig _config = new();
        private readonly MapLister _mapLister = mapLister;
        private bool validated = false;
        private const string SteamApiUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";

        public void OnConfigParsed(Config config)
        {
            _config = config.General;
        }

        public void OnMapStart(string map)
        {
            if (!validated && _config.EnableMapValidation)
                _ = ValidateAllMapsAsync();
            validated = true;
        }

        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler())
        {
            DefaultRequestHeaders =
            {
                { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)" }
            }
        };

        // Default maps that don't need to be checked
        private static readonly HashSet<string> _defaultMaps = new(StringComparer.OrdinalIgnoreCase)
        {
            "ar_baggage","ar_pool_day","ar_shoots","de_anubis","de_ancient","de_brewery","de_dust2",
            "de_dogtown","de_inferno","de_mirage","de_nuke","de_overpass","de_vertigo","de_basalt",
            "de_palais","de_train","de_whistle","de_edin","de_grail","de_jura","de_whistle","cs_italy",
            "cs_office","cs_agency"
        };

        private async Task ValidateAllMapsAsync()
        {
            var maps = _mapLister.Maps
                ?.Where(m => !_defaultMaps.Contains(m.Name))
                .ToList();

            if (maps == null || maps.Count == 0)
            {
                _logger.LogInformation("[Map-Checker] No maps to validate");
                return;
            }

            var workshopMaps = new List<(Map Map, ulong PublishedFileId)>();
            foreach (var map in maps)
            {
                if (!ulong.TryParse(map.Id, out var publishedFileId))
                {
                    _logger.LogInformation($"[Map-Checker] could not parse ID for \"{map.Name}\": \"{map.Id}\"");
                    continue;
                }

                workshopMaps.Add((map, publishedFileId));
            }

            if (workshopMaps.Count == 0)
            {
                _logger.LogInformation("[Map-Checker] No workshop maps to validate");
                return;
            }

            if (string.IsNullOrWhiteSpace(_config.SteamApiKey))
            {
                await ValidateWithHtmlChecksAsync(workshopMaps).ConfigureAwait(false);
                return;
            }

            await ValidateWithSteamApiAsync(workshopMaps).ConfigureAwait(false);
        }

        private static async Task<bool> DoesWorkshopItemExistAsync(ulong publishedFileId)
        {
            // Fetches the map page HTML
            var url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={publishedFileId}";
            var html = await _httpClient.GetStringAsync(url).ConfigureAwait(false);

            // If Steam shows its error banner, we'll assume it doesn’t exist
            return !html.Contains("There was a problem accessing the item");
        }

        private async Task ValidateWithHtmlChecksAsync(List<(Map Map, ulong PublishedFileId)> workshopMaps)
        {
            foreach (var (map, publishedFileId) in workshopMaps)
            {
                try
                {
                    bool exists = await DoesWorkshopItemExistAsync(publishedFileId).ConfigureAwait(false);
                    if (!exists)
                        await HandleMissingMapAsync(map, publishedFileId).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[Map-Checker] ERROR checking {map.Name}: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false); // 1 request per second without an API key to avoid rate limiting
            }
        }

        private async Task ValidateWithSteamApiAsync(List<(Map Map, ulong PublishedFileId)> workshopMaps)
        {
            foreach (var batch in workshopMaps.Chunk(50))
            {
                try
                {
                    var payload = new List<KeyValuePair<string, string>>
                    {
                        new("key", _config.SteamApiKey),
                        new("itemcount", batch.Length.ToString())
                    };

                    for (int i = 0; i < batch.Length; i++)
                    {
                        payload.Add(new($"publishedfileids[{i}]", batch[i].PublishedFileId.ToString()));
                    }

                    using var content = new FormUrlEncodedContent(payload);
                    using var response = await _httpClient.PostAsync(SteamApiUrl, content).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError($"[Map-Checker] ERROR validating map batch via Steam Web API: {response.StatusCode} ({response.ReasonPhrase})");
                        continue;
                    }

                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var apiResponse = JsonSerializer.Deserialize<PublishedFileDetailsResponse>(json);
                    var details = apiResponse?.Response?.PublishedFileDetails;

                    if (details == null)
                    {
                        _logger.LogError("[Map-Checker] Unexpected response while validating workshop maps via Steam Web API");
                        continue;
                    }

                    foreach (var detail in details)
                    {
                        if (detail.Result == 1)
                            continue;

                        if (!ulong.TryParse(detail.PublishedFileId, out var detailId))
                        {
                            _logger.LogError($"[Map-Checker] Unexpected publishedfileid value in Steam Web API response: \"{detail.PublishedFileId}\"");
                            continue;
                        }

                        foreach (var missing in batch.Where(b => b.PublishedFileId == detailId))
                        {
                            await HandleMissingMapAsync(missing.Map, missing.PublishedFileId).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[Map-Checker] ERROR validating map batch via Steam Web API: {ex.Message}");
                }
            }
        }

        private async Task HandleMissingMapAsync(Map map, ulong publishedFileId)
        {
            _logger.LogWarning($"[Map-Checker] ⚠️ {map.Name} (WorkshopID {publishedFileId}) does not exist!");

            if (string.IsNullOrEmpty(_config.DiscordWebhook))
                return;

            var discordMessage = new
            {
                content = $"⚠️ [RockTheVote] ⚠️\n{map.Name}\nWorkshopID: {publishedFileId}\ndoes not exist on the workshop!"
            };

            string json = JsonSerializer.Serialize(discordMessage);
            using var discordContent = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                await _httpClient.PostAsync(_config.DiscordWebhook, discordContent).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Map-Checker] Failed to send Discord webhook: {ex.Message}");
            }
        }

        private sealed class PublishedFileDetailsResponse
        {
            [JsonPropertyName("response")]
            public SteamApiResponse? Response { get; set; }
        }

        private sealed class SteamApiResponse
        {
            [JsonPropertyName("result")]
            public int Result { get; set; }

            [JsonPropertyName("resultcount")]
            public int ResultCount { get; set; }

            [JsonPropertyName("publishedfiledetails")]
            public List<PublishedFileDetail> PublishedFileDetails { get; set; } = [];
        }

        private sealed class PublishedFileDetail
        {
            [JsonPropertyName("result")]
            public int Result { get; set; }

            [JsonPropertyName("publishedfileid")]
            public string PublishedFileId { get; set; } = "";
        }
    }
}