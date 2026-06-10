using CounterStrikeSharp.API;

namespace cs2_rockthevote.Core
{
    public class MapCooldown : IPluginDependency<Plugin, Config>
    {
        List<string> mapsOnCoolDown = new();
        private GeneralConfig _generalConfig = new();
        public event EventHandler<Map[]>? EventCooldownRefreshed;

        // Backup file (next to maplist.txt) so the cooldown survives a restart, null until OnLoad
        private string? _cooldownFilePath;

        public MapCooldown(MapLister mapLister)
        {
            // Each time the maps load (i.e. on map start), refresh our list
            mapLister.EventMapsLoaded += (sender, maps) =>
            {
                // Skip until OnLoad has restored the backup, else we'd overwrite it with an empty list
                if (_cooldownFilePath is not null)
                    RegisterCurrentMap();

                EventCooldownRefreshed?.Invoke(this, maps);
            };
        }

        public void OnConfigParsed(Config config)
        {
            _generalConfig = config.General;
        }

        public void OnLoad(Plugin plugin)
        {
            // Lives in plugin root directory
            _cooldownFilePath = Path.GetFullPath(Path.Combine(plugin.ModulePath, "../mapcooldown.txt"));

            LoadCooldownFromFile();
            RegisterCurrentMap();
        }

        // Adds the current map, drops the oldest past the limit, and saves to disk
        private void RegisterCurrentMap()
        {
            var current = Server.MapName?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(current))
                return;

            int maxEntries = _generalConfig.MapsInCoolDown;

            // If cooldown is disabled, keep only the current map
            if (maxEntries <= 0)
            {
                mapsOnCoolDown.Clear();
                mapsOnCoolDown.Add(current);
            }
            else
            {
                // Skip if it's already the newest entry, so a reload doesn't duplicate it
                if (mapsOnCoolDown.Count == 0 || mapsOnCoolDown[^1] != current)
                    mapsOnCoolDown.Add(current);

                // Cycle the oldest maps back into rotation until within the limit
                while (mapsOnCoolDown.Count > maxEntries)
                    mapsOnCoolDown.RemoveAt(0);
            }

            SaveCooldownToFile();
        }

        // Loads the saved cooldown maps from disk, trimmed to the configured limit
        private void LoadCooldownFromFile()
        {
            if (string.IsNullOrEmpty(_cooldownFilePath) || !File.Exists(_cooldownFilePath))
                return;

            try
            {
                var restored = File.ReadAllLines(_cooldownFilePath)
                    .Select(x => x.Trim().ToLowerInvariant())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();

                int maxEntries = _generalConfig.MapsInCoolDown;
                if (maxEntries > 0 && restored.Count > maxEntries)
                    restored = restored.Skip(restored.Count - maxEntries).ToList();

                mapsOnCoolDown = restored;
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"[RTV] Failed to load map cooldown file: {ex.Message}");
            }
        }

        // Writes the current cooldown window to disk, one base map name per line
        private void SaveCooldownToFile()
        {
            if (string.IsNullOrEmpty(_cooldownFilePath))
                return;

            try
            {
                File.WriteAllLines(_cooldownFilePath, mapsOnCoolDown);
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"[RTV] Failed to save map cooldown file: {ex.Message}");
            }
        }

        public bool IsMapInCooldown(string map)
        {
            if (string.IsNullOrEmpty(map))
                return false;

            // Grab the base map name (everything before the first space or parenthesis)
            // E.g. "surf_beginner (T1, Staged)" -> "surf_beginner"
            var baseName = map;
            var idx = map.IndexOf(' ');
            if (idx > 0)
                baseName = map[..idx];

            // Compare lowercase
            var lowerName = baseName.Trim().ToLowerInvariant();

            // Always exclude the current map
            var current = Server.MapName?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(current) && lowerName == current)
                return true;

            return mapsOnCoolDown.Contains(lowerName);
        }
    }
}
