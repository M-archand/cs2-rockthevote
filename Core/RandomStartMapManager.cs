using CounterStrikeSharp.API;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    public class RandomStartMapManager(MapLister mapLister, ILogger<RandomStartMapManager> logger) : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<RandomStartMapManager> _logger = logger;
        private readonly MapLister _mapLister = mapLister;
        private bool _firstMapStart = true;
        private Timer? _timerChangeMap;
        private GeneralConfig _generalConfig = new();
        private Plugin? _plugin;

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
        }

        public void Unload(Plugin plugin)
        {
            _timerChangeMap?.Kill();
            _timerChangeMap = null;
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

            // Schedule our map change for 3s from now (1s didn't seem to work)
            _timerChangeMap?.Kill();
            _timerChangeMap = _plugin?.AddTimer(3.0f, () =>
            {
                if (!string.IsNullOrEmpty(pick.Id) && ulong.TryParse(pick.Id, out var wsID))
                {
                    // Workshop map by ID (E.g. 3129698096)
                    Server.ExecuteCommand($"host_workshop_map {wsID}");
                }
                else
                {
                    // Local map by name (E.g. de_dust2)
                    Server.ExecuteCommand($"changelevel {pick.Name}");
                }
            });
        }
    }
}