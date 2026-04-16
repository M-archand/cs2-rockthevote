using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace cs2_rockthevote
{
    public enum MapChangeTrigger
    {
        RoundStart,
        MatchEnd,
        IgnoredWinConditions
    }

    public partial class Plugin
    {
        public HookResult OnRoundStartMapChanger(EventRoundStart @event, GameEventInfo info)
        {
            _changeMapManager.ChangeNextMap(MapChangeTrigger.RoundStart);
            return HookResult.Continue;
        }
    }

    public class ChangeMapManager : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<ChangeMapManager> _logger;
        private ILogger _debugLogger = NullLogger<ChangeMapManager>.Instance;
        private Plugin? _plugin;
        private StringLocalizer _localizer;
        private PluginState _pluginState;
        private MapLister _mapLister;

        public string? NextMap { get; private set; } = null;
        private string _prefix = DEFAULT_PREFIX;
        private const string DEFAULT_PREFIX = "rtv.prefix";
        private MapChangeTrigger _changeTrigger = MapChangeTrigger.RoundStart;

        private Map[] _maps = new Map[0];
        private Config? _config;

        private Timer? _pendingMapChangeTimer;
        private Timer? _winPanelDelayTimer;
        private Timer? _mapChangeVerifyTimer;

        public ChangeMapManager(StringLocalizer localizer, PluginState pluginState, MapLister mapLister, ILogger<ChangeMapManager> logger)
        {
            _logger = logger;
            _localizer = localizer;
            _pluginState = pluginState;
            _mapLister = mapLister;
            _mapLister.EventMapsLoaded += OnMapsLoaded;
        }

        public void OnMapsLoaded(object? sender, Map[] maps)
        {
            _maps = maps;
        }


        public void ScheduleMapChange(string map, MapChangeTrigger trigger = MapChangeTrigger.RoundStart, string prefix = DEFAULT_PREFIX)
        {
            NextMap = map;
            _prefix = prefix;
            _pluginState.MapChangeScheduled = true;
            _changeTrigger = trigger;

            _debugLogger.LogInformation(
                "[MapChange] Scheduled. map={Map} trigger={Trigger} prefix={Prefix} currentMap={CurrentMap}",
                map,
                trigger,
                prefix,
                Server.MapName
            );
        }

        public void OnMapStart(string _map)
        {
            NextMap = null;
            _prefix = DEFAULT_PREFIX;
            _changeTrigger = MapChangeTrigger.RoundStart;

            _pendingMapChangeTimer?.Kill();
            _pendingMapChangeTimer = null;
            _winPanelDelayTimer?.Kill();
            _winPanelDelayTimer = null;
            _mapChangeVerifyTimer?.Kill();
            _mapChangeVerifyTimer = null;
        }

        public bool ChangeNextMap(MapChangeTrigger trigger)
        {
            if (!_pluginState.MapChangeScheduled)
                return false;

            if (trigger != _changeTrigger)
            {
                _debugLogger.LogInformation(
                    "[MapChange] Ignored trigger mismatch. requested={RequestedTrigger} scheduled={ScheduledTrigger} map={Map}",
                    trigger,
                    _changeTrigger,
                    NextMap
                );
                return false;
            }

            return ChangeNextMap();
        }

        public bool ChangeNextMap()
        {
            if (!_pluginState.MapChangeScheduled)
                return false;

            if (_pendingMapChangeTimer is not null)
            {
                _debugLogger.LogInformation(
                    "[MapChange] Already pending. map={Map} trigger={Trigger}",
                    NextMap,
                    _changeTrigger
                );
                return true;
            }

            Map? map = _maps.FirstOrDefault(x => string.Equals(x.Name, NextMap, StringComparison.OrdinalIgnoreCase));
            if (map == null)
            {
                _debugLogger.LogWarning("[MapChange] Could not resolve map object. map={Map}", NextMap);
                return false;
            }

            Server.PrintToChatAll(_localizer.LocalizeWithPrefixInternal(_prefix, "general.changing-map", map.Name));

            string mapBefore = Server.MapName ?? string.Empty;
            float delaySeconds = _changeTrigger == MapChangeTrigger.IgnoredWinConditions ? 0.0F : 3.0F;

            if (delaySeconds <= 0)
            {
                _debugLogger.LogInformation(
                    "[MapChange] Executing immediately. map={Map} trigger={Trigger} currentMap={CurrentMap}",
                    map.Name,
                    _changeTrigger,
                    mapBefore
                );
                ExecuteMapChangeCommand(map, mapBefore);
                return true;
            }

            _debugLogger.LogInformation(
                "[MapChange] Arming change timer. map={Map} trigger={Trigger} delaySeconds={DelaySeconds} currentMap={CurrentMap}",
                map.Name,
                _changeTrigger,
                delaySeconds,
                mapBefore
            );

            _pendingMapChangeTimer = _plugin?.AddTimer(delaySeconds, () =>
            {
                try
                {
                    _debugLogger.LogInformation(
                        "[MapChange] Delayed change timer fired. map={Map} trigger={Trigger} currentMap={CurrentMap}",
                        map.Name,
                        _changeTrigger,
                        Server.MapName
                    );
                    _pendingMapChangeTimer = null;
                    ExecuteMapChangeCommand(map, mapBefore);
                }
                catch (Exception ex)
                {
                    _pendingMapChangeTimer = null;
                    _debugLogger.LogError(
                        ex,
                        "[MapChange] Delayed change timer callback failed. map={Map} trigger={Trigger} currentMap={CurrentMap} message={Message}",
                        map.Name,
                        _changeTrigger,
                        Server.MapName,
                        ex.Message
                    );
                }
            }, TimerFlags.STOP_ON_MAPCHANGE);

            return true;
        }

        private void ExecuteMapChangeCommand(Map map, string mapBefore)
        {
            try
            {
                _pluginState.MapChangeScheduled = false;

                _debugLogger.LogInformation(
                    "[MapChange] Evaluating command path. map={Map} mapId={MapId} previousMap={PreviousMap}",
                    map.Name,
                    map.Id,
                    mapBefore
                );

                bool mapNameIsValid = Server.IsMapValid(map.Name);

                _debugLogger.LogInformation(
                    "[MapChange] IsMapValid evaluated. map={Map} result={IsValid}",
                    map.Name,
                    mapNameIsValid
                );

                if (mapNameIsValid)
                {
                    _debugLogger.LogInformation(
                        "[MapChange] Executing changelevel. map={Map} previousMap={PreviousMap}",
                        map.Name,
                        mapBefore
                    );
                    Server.ExecuteCommand($"changelevel {map.Name}");
                }
                else if (map.Id is not null)
                {
                    _debugLogger.LogInformation(
                        "[MapChange] Executing host_workshop_map. map={Map} workshopId={WorkshopId} previousMap={PreviousMap}",
                        map.Name,
                        map.Id,
                        mapBefore
                    );
                    Server.ExecuteCommand($"host_workshop_map {map.Id}");
                }
                else
                {
                    _debugLogger.LogInformation(
                        "[MapChange] Executing ds_workshop_changelevel. map={Map} previousMap={PreviousMap}",
                        map.Name,
                        mapBefore
                    );
                    Server.ExecuteCommand($"ds_workshop_changelevel {map.Name}");
                }

                StartMapChangeVerifyTimer(map, mapBefore);
            }
            catch (Exception ex)
            {
                _debugLogger.LogError(
                    ex,
                    "[MapChange] ExecuteMapChangeCommand failed. map={Map} mapId={MapId} previousMap={PreviousMap} message={Message}",
                    map.Name,
                    map.Id,
                    mapBefore,
                    ex.Message
                );
                throw;
            }
        }

        private void StartMapChangeVerifyTimer(Map map, string mapBefore)
        {
            // Create 45s verification timer. If we’re still on same map as when we started, pick random fallback map and try again
            _mapChangeVerifyTimer?.Kill();
            _mapChangeVerifyTimer = _plugin?.AddTimer(45.0F, () =>
            {
                try
                {
                    string current = Server.MapName ?? string.Empty;

                    if (string.Equals(current, mapBefore, StringComparison.OrdinalIgnoreCase))
                    {
                        _debugLogger.LogWarning(
                            "[MapChange] Verify timer found unchanged map. requestedMap={RequestedMap} currentMap={CurrentMap}",
                            map.Name,
                            current
                        );

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
                            _debugLogger.LogWarning(
                                "[MapChange] Fallback selection failed. currentMap={CurrentMap} requestedMap={RequestedMap}",
                                current,
                                map.Name
                            );
                            Server.PrintToConsole("[RTV] Fallback map selection failed: no candidates available.");
                            return;
                        }

                        var random = new Random();
                        var fallback = candidates[random.Next(candidates.Count)];

                        _debugLogger.LogWarning(
                            "[MapChange] Attempting fallback map. fallbackMap={FallbackMap} previousRequestedMap={RequestedMap} currentMap={CurrentMap}",
                            fallback.Name,
                            map.Name,
                            current
                        );

                        Server.PrintToChatAll(_localizer.LocalizeWithPrefixInternal(_prefix, "general.changing-map", fallback.Name));

                        if (Server.IsMapValid(fallback.Name))
                        {
                            _debugLogger.LogInformation("[MapChange] Executing fallback changelevel. map={Map}", fallback.Name);
                            Server.ExecuteCommand($"changelevel {fallback.Name}");
                        }
                        else if (fallback.Id is not null)
                        {
                            _debugLogger.LogInformation(
                                "[MapChange] Executing fallback host_workshop_map. map={Map} workshopId={WorkshopId}",
                                fallback.Name,
                                fallback.Id
                            );
                            Server.ExecuteCommand($"host_workshop_map {fallback.Id}");
                        }
                        else
                        {
                            _debugLogger.LogInformation("[MapChange] Executing fallback ds_workshop_changelevel. map={Map}", fallback.Name);
                            Server.ExecuteCommand($"ds_workshop_changelevel {fallback.Name}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _debugLogger.LogError(ex, "[MapChange] Fallback verify timer failed: {Message}", ex.Message);
                    Server.PrintToConsole($"[RTV] Fallback map change check error: {ex.Message}");
                }
            }, TimerFlags.STOP_ON_MAPCHANGE); // auto-kill if map did change
        }

        public void OnConfigParsed(Config config)
        {
            _config = config;
            _debugLogger = config.General.DebugLogging ? _logger : NullLogger<ChangeMapManager>.Instance;
        }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
            plugin.RegisterEventHandler<EventCsWinPanelMatch>((ev, info) =>
            {
                if (_pluginState.MapChangeScheduled && _changeTrigger == MapChangeTrigger.MatchEnd)
                {
                    var delay = (_config?.EndOfMapVote.DelayToChangeInTheEnd ?? 0) - 3.0F;
                    if (delay < 0)
                        delay = 0;

                    _debugLogger.LogInformation(
                        "[MapChange] Match-end win panel received. map={Map} delaySeconds={DelaySeconds}",
                        NextMap,
                        delay
                    );

                    _winPanelDelayTimer?.Kill();
                    _winPanelDelayTimer = _plugin?.AddTimer(delay, () =>
                    {
                        _winPanelDelayTimer = null;
                        ChangeNextMap(MapChangeTrigger.MatchEnd);
                    }, TimerFlags.STOP_ON_MAPCHANGE);
                }
                return HookResult.Continue;
            });
        }
    }
}
