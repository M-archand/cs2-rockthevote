using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace cs2_rockthevote
{
    public partial class Plugin
    {
        [GameEventHandler(HookMode.Post)]
        public HookResult OnRoundEndMapChanger(EventRoundEnd @event, GameEventInfo info)
        {
            _changeMapManager.ChangeNextMap();
            return HookResult.Continue;
        }

        [GameEventHandler(HookMode.Post)]
        public HookResult OnRoundStartMapChanger(EventRoundStart @event, GameEventInfo info)
        {
            _changeMapManager.ChangeNextMap();
            return HookResult.Continue;
        }
    }

    public class ChangeMapManager : IPluginDependency<Plugin, Config>
    {
        private Plugin? _plugin;
        private StringLocalizer _localizer;
        private PluginState _pluginState;
        private MapLister _mapLister;

        public string? NextMap { get; private set; } = null;
        private string _prefix = DEFAULT_PREFIX;
        private const string DEFAULT_PREFIX = "rtv.prefix";
        private bool _mapEnd = false;

        private Map[] _maps = new Map[0];
        private Config? _config;

        private Timer? _mapChangeVerifyTimer;

        public ChangeMapManager(StringLocalizer localizer, PluginState pluginState, MapLister mapLister)
        {
            _localizer = localizer;
            _pluginState = pluginState;
            _mapLister = mapLister;
            _mapLister.EventMapsLoaded += OnMapsLoaded;
        }

        public void OnMapsLoaded(object? sender, Map[] maps)
        {
            _maps = maps;
        }


        public void ScheduleMapChange(string map, bool mapEnd = false, string prefix = DEFAULT_PREFIX)
        {
            NextMap = map;
            _prefix = prefix;
            _pluginState.MapChangeScheduled = true;
            _mapEnd = mapEnd;
        }

        public void OnMapStart(string _map)
        {
            NextMap = null;
            _prefix = DEFAULT_PREFIX;

            _mapChangeVerifyTimer?.Kill();
            _mapChangeVerifyTimer = null;
        }

        public bool ChangeNextMap(bool mapEnd = false)
        {
            if (mapEnd != _mapEnd)
                return false;

            if (!_pluginState.MapChangeScheduled)
                return false;

            Map? map = _maps.FirstOrDefault(x => string.Equals(x.Name, NextMap, StringComparison.OrdinalIgnoreCase));
            if (map == null)
            {
                Server.PrintToChatAll($"[RTV Debug] Could not resolve map object for '{NextMap}'");
                return false;
            }

            _pluginState.MapChangeScheduled = false;

            Server.PrintToChatAll(_localizer.LocalizeWithPrefixInternal(_prefix, "general.changing-map", map.Name));

            string mapBefore = Server.MapName ?? string.Empty;


            _plugin?.AddTimer(3.0F, () =>
            {
                if (Server.IsMapValid(map.Name))
                {
                    Server.ExecuteCommand($"changelevel {map.Name}");
                }
                else if (map.Id is not null)
                {
                    Server.ExecuteCommand($"host_workshop_map {map.Id}");
                }
                else
                {
                    Server.ExecuteCommand($"ds_workshop_changelevel {map.Name}");
                }

                // Create 45s verification timer. If we’re still on the same map as when we started, pick a random fallback map and try again
                _mapChangeVerifyTimer?.Kill();
                _mapChangeVerifyTimer = _plugin?.AddTimer(45.0F, () =>
                {
                    try
                    {
                        string current = Server.MapName ?? string.Empty;

                        if (string.Equals(current, mapBefore, StringComparison.OrdinalIgnoreCase))
                        {
                            var candidates = _maps
                                .Where(m =>
                                    !string.Equals(m.Name, current, StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(m.Name, map.Name, StringComparison.OrdinalIgnoreCase))
                                .ToList();

                            if (candidates.Count == 0)
                            {
                                candidates = _maps
                                    .Where(m => !string.Equals(m.Name, current, StringComparison.OrdinalIgnoreCase))
                                    .ToList();
                            }

                            if (candidates.Count == 0)
                            {
                                Server.PrintToConsole("[RTV] Fallback map selection failed: no candidates available.");
                                return;
                            }

                            var random = new Random();
                            var fallback = candidates[random.Next(candidates.Count)];

                            Server.PrintToChatAll(_localizer.LocalizeWithPrefixInternal(_prefix, "general.changing-map", fallback.Name));

                            if (Server.IsMapValid(fallback.Name))
                            {
                                Server.ExecuteCommand($"changelevel {fallback.Name}");
                            }
                            else if (fallback.Id is not null)
                            {
                                Server.ExecuteCommand($"host_workshop_map {fallback.Id}");
                            }
                            else
                            {
                                Server.ExecuteCommand($"ds_workshop_changelevel {fallback.Name}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Server.PrintToConsole($"[RTV] Fallback map change check error: {ex.Message}");
                    }
                }, TimerFlags.STOP_ON_MAPCHANGE); // auto-kill if the map did change
            });

            return true;
        }

        public void OnConfigParsed(Config config)
        {
            _config = config;
        }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
            plugin.RegisterEventHandler<EventCsWinPanelMatch>((ev, info) =>
            {
                if (_pluginState.MapChangeScheduled)
                {
                    var delay = (_config?.EndOfMapVote.DelayToChangeInTheEnd ?? 0) - 3.0F;
                    if (delay < 0)
                        delay = 0;

                    _plugin?.AddTimer(delay, () =>
                    {
                        ChangeNextMap(true);
                    });
                }
                return HookResult.Continue;
            });
        }
    }
}
