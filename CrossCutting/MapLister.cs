using CounterStrikeSharp.API;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    public class MapLister : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<MapLister> _logger;
        public Map[] Maps { get; private set; } = Array.Empty<Map>();
        public bool MapsLoaded { get; private set; } = false;
        public event EventHandler<Map[]>? EventMapsLoaded;
        private Plugin? _plugin;
        private ILogger _debugLogger = NullLogger<MapLister>.Instance;

        public MapLister(ILogger<MapLister> logger)
        {
            _logger = logger;
        }

        public void Clear()
        {
            MapsLoaded = false;
            Maps = Array.Empty<Map>();
        }

        public void LoadMaps()
        {
            Clear();

            if (_plugin is null)
            {
                _debugLogger.LogWarning("[MapLister] LoadMaps called before plugin was assigned.");
                return;
            }

            string mapsFile = Path.GetFullPath(Path.Combine(_plugin.ModulePath, "../maplist.txt"));
            string exampleFile = Path.GetFullPath(Path.Combine(_plugin.ModulePath, "../maplist.example.txt"));

            if (!File.Exists(mapsFile))
            {
                _debugLogger.LogError("[MapLister] Missing required map list file at {MapListPath}.", mapsFile);
                if (File.Exists(exampleFile))
                {
                    _debugLogger.LogInformation(
                        "[MapLister] Example map list found at {ExamplePath}. Copy or rename it to {MapListPath}.",
                        exampleFile,
                        mapsFile
                    );
                }

                Server.PrintToConsole($"[RTV] maplist.txt not found at {mapsFile}");
                EventMapsLoaded?.Invoke(this, Maps);
                return;
            }

            try
            {
                Maps = [.. File.ReadAllText(mapsFile)
                    .Replace("\r\n", "\n")
                    .Split("\n")
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith("//"))
                    .Select(mapLine =>
                    {
                        string[] args = mapLine.Split(":");
                        string mapName = args[0];
                        string? mapValue = args.Length == 2 ? args[1] : null;
                        return new Map(mapName, mapValue);
                    })];

                MapsLoaded = true;
                _debugLogger.LogInformation("[MapLister] Loaded {MapCount} maps from {MapListPath}.", Maps.Length, mapsFile);
            }
            catch (Exception ex)
            {
                Clear();
                _debugLogger.LogError(ex, "[MapLister] Failed to load map list from {MapListPath}.", mapsFile);
                Server.PrintToConsole($"[RTV] Failed to load maplist.txt: {ex.Message}");
            }

            EventMapsLoaded?.Invoke(this, Maps);
        }

        public void OnMapStart(string _map)
        {
            if (_plugin is not null)
                LoadMaps();
        }

        public void OnConfigParsed(Config config)
        {
            _debugLogger = config.General.DebugLogging ? _logger : NullLogger<MapLister>.Instance;
        }


        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
            LoadMaps();
        }

        // Returns the exact map name, or null if none found
        public string? GetExactMapName(string name)
        {
            return Maps
                .Select(m => m.Name)
                .FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        // Returns all map names that contain the given argument
        public List<string> GetMatchingMapNames(string partial)
        {
            return [.. Maps
                .Select(m => m.Name)
                .Where(n => n.Contains(partial, StringComparison.OrdinalIgnoreCase))];
        }

        // Remove maps no longer available on the workshop
        public void PruneMaps(IEnumerable<Map> toRemove)
        {
            Maps = [.. Maps.Where(m => !toRemove.Contains(m))];
        }
    }
}
