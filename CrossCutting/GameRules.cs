using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;

namespace cs2_rockthevote
{
    public class GameRules : IPluginDependency<Plugin, Config>
    {
        CCSGameRules? _gameRules = null;
        private Plugin? _plugin;

        public void SetGameRules() => _gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;

        public void SetGameRulesAsync()
        {
            _gameRules = null;
            _plugin?.AddTimer(1.0f, SetGameRules, TimerFlags.STOP_ON_MAPCHANGE);
        }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
            SetGameRulesAsync();
            plugin.RegisterEventHandler<EventRoundStart>(OnRoundStart);
            plugin.RegisterEventHandler<EventRoundAnnounceWarmup>(OnAnnounceWarmup);
        }

        public float GameStartTime => _gameRules?.GameStartTime ?? 0;
        public float RoundStartTime => _gameRules?.RoundStartTime ?? 0;

        public void OnMapStart(string map)
        {
            SetGameRulesAsync();
        }


        public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
        {
            SetGameRules();
            return HookResult.Continue;
        }

        public HookResult OnAnnounceWarmup(EventRoundAnnounceWarmup @event, GameEventInfo info)
        {
            SetGameRules();
            return HookResult.Continue;
        }

        public bool WarmupRunning => _gameRules?.WarmupPeriod ?? false;

        public int TotalRoundsPlayed => _gameRules?.TotalRoundsPlayed ?? 0;
        public int RoundTime
        {
            get => _gameRules?.RoundTime ?? 0;
            set => _gameRules!.RoundTime = value;
        }
    }
}
