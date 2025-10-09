using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace cs2_rockthevote
{
    public class WorkshopMapValidator(MapLister mapLister, ILogger<WorkshopMapValidator> logger) : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<WorkshopMapValidator> _logger = logger;
        private GeneralConfig _config = new();
        private readonly MapLister _mapLister = mapLister;
        private bool validated = false;

        public void OnConfigParsed(Config config)
        {
            _config = config.General;
        }

        public void OnMapStart(string map)
        {
            if (!validated && _config.EnableWorkshopMapValidation)
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
            string serverHostname = ConVar.Find("hostname")!.StringValue;
            var toRemove = new List<Map>();
            // Grab our maps
            var maps = _mapLister.Maps
                ?.Where(m => !_defaultMaps.Contains(m.Name))
                .ToList();

            if (maps == null || maps.Count == 0)
            {
                _logger.LogInformation("[Map-Checker] No maps to validate");
                return;
            }

            foreach (var map in maps)
            {
                if (!ulong.TryParse(map.Id, out var publishedFileId))
                {
                    _logger.LogInformation($"[Map-Checker] could not parse ID for “{map.Name}”: “{map.Id}”");
                    continue;
                }

                try
                {
                    bool exists = await DoesWorkshopItemExistAsync(publishedFileId).ConfigureAwait(false);
                    await Task.Delay(100).ConfigureAwait(false); // Limits us to 10 map checks per second, should keep us from hammering Steam and getting limited...?
                    if (!exists)
                    {
                        _logger.LogWarning($"[Map-Checker] ⚠️ {map.Name} (WorkshopID {publishedFileId}) does not exist!");
                        /*
                        if (_generalConfig.RemoveInvalidMaps)
                            toRemove.Add(map);
                        */
                        if (!string.IsNullOrEmpty(_config.DiscordWebhook))
                        {
                            var discordMessage = new
                            {
                                content = $"⚠️ [RockTheVote] ⚠️\n{map.Name}\nWorkshopID: {publishedFileId}\ndoes not exist on the workshop!"
                            };
                            
                            string json = JsonSerializer.Serialize(discordMessage);
                            var discordContent = new StringContent(json,Encoding.UTF8,"application/json");

                            try
                            {
                                await _httpClient.PostAsync(_config.DiscordWebhook, discordContent).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError($"[Map-Checker] Failed to send Discord webhook: {ex.Message}");
                            }
                        }
                    }

                }
                catch (Exception ex)
                {
                    _logger.LogError($"[Map-Checker] ERROR checking {map.Name}: {ex.Message}");
                }
            }
            /*
            if (toRemove.Count > 0)
            {
                _mapLister.PruneMaps(toRemove);
                foreach (var badMap in toRemove)
                    _logger.LogInformation($"[Map-Checker] Removed invalid map: `{badMap.Name}`");
            }
            */
        }

        private static async Task<bool> DoesWorkshopItemExistAsync(ulong publishedFileId)
        {
            // Fetches the map page HTML
            var url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={publishedFileId}";
            var html = await _httpClient.GetStringAsync(url).ConfigureAwait(false);

            // If Steam shows its error banner, we'll assume it doesn’t exist
            return !html.Contains("There was a problem accessing the item");
        }
    }
}