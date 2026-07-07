using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    public class RandomStartMapManager(MapLister mapLister, ChangeMapManager changeMapManager, ILogger<RandomStartMapManager> logger) : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<RandomStartMapManager> _logger = logger;
        private readonly MapLister _mapLister = mapLister;
        private readonly ChangeMapManager _changeMapManager = changeMapManager;
        private bool _firstMapStart = true;
        private GeneralConfig _generalConfig = new();
        private Plugin? _plugin;

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
        }

        public void OnConfigParsed(Config config)
        {
            _generalConfig = config.General;
        }

        public void OnMapStart(string currentMap)
        {
            // Only run on the very first map start, and only if enabled in config
            if (!_generalConfig.RandomStartMap || !_firstMapStart)
                return;

            _firstMapStart = false;

            // Build a list of maps
            var candidates = _mapLister.Maps?
                .Where(m => !string.Equals(m.Name, currentMap, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidates == null || candidates.Count == 0)
                return;

            // Pick a random map
            var pick = candidates[new Random().Next(candidates.Count)];

            // Route through ChangeMapManager as it gives IsMapValid fallback + verify-retry
            _changeMapManager.ScheduleMapChange(pick.Name);
            _changeMapManager.ChangeNextMap();
        }
    }
}