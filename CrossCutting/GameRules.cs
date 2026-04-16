using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using System;

namespace cs2_rockthevote
{
    public class GameRules : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<GameRules> _logger;
        CCSGameRules? _gameRules = null;
        private Plugin? _plugin;
        private ConVar? _roundTimeCvar;
        private float _fallbackRoundStartTime;
        private int _fallbackRoundTimeSeconds;
        private ILogger _debugLogger = NullLogger<GameRules>.Instance;

        public GameRules(ILogger<GameRules> logger)
        {
            _logger = logger;
        }

        private CCSGameRules? ResolveGameRules()
        {
            _gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                .FirstOrDefault()?.GameRules;
            return _gameRules;
        }

        public void SetGameRules() => ResolveGameRules();

        private void LoadRoundTimeCvar()
        {
            _roundTimeCvar = ConVar.Find("mp_roundtime");
            _fallbackRoundTimeSeconds = (int)MathF.Round((_roundTimeCvar?.GetPrimitiveValue<float>() ?? 0f) * 60f);
        }

        private CCSGameRules? GetValidGameRules(bool refresh = false)
        {
            try
            {
                var gameRules = refresh ? ResolveGameRules() : (_gameRules ?? ResolveGameRules());
                if (gameRules == null)
                {
                    gameRules = ResolveGameRules();
                    if (gameRules == null)
                    {
                        _gameRules = null;
                        return null;
                    }
                }

                _gameRules = gameRules;
                return gameRules;
            }
            catch (InvalidOperationException ex)
            {
                _gameRules = null;
                _debugLogger.LogError(ex, "[GameRules] InvalidOperation while resolving gamerules. refresh={Refresh} message={Message}", refresh, ex.Message);
                return null;
            }
        }

        public void SetGameRulesAsync()
        {
            _gameRules = null;
            _plugin?.AddTimer(1.0f, SetGameRules, TimerFlags.STOP_ON_MAPCHANGE);
        }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
            LoadRoundTimeCvar();
            _fallbackRoundStartTime = 0f;
            SetGameRulesAsync();
            plugin.RegisterEventHandler<EventRoundStart>(OnRoundStart);
            plugin.RegisterEventHandler<EventRoundAnnounceWarmup>(OnAnnounceWarmup);
        }

        public void OnConfigParsed(Config config)
        {
            _debugLogger = config.General.DebugLogging ? _logger : NullLogger<GameRules>.Instance;
        }

        public float GameStartTime => GetValidGameRules()?.GameStartTime ?? 0;
        public float RoundStartTime => GetValidGameRules()?.RoundStartTime ?? 0;

        public bool TryGetRoundTiming(out float roundTime, out float roundStartTime)
        {
            var gameRules = GetValidGameRules(refresh: true);
            if (gameRules != null)
            {
                roundTime = gameRules.RoundTime;
                roundStartTime = gameRules.RoundStartTime;
                _fallbackRoundTimeSeconds = (int)MathF.Round(roundTime);
                _fallbackRoundStartTime = roundStartTime;
                return roundTime > 0 && roundStartTime > 0;
            }

            roundTime = _fallbackRoundTimeSeconds;
            roundStartTime = _fallbackRoundStartTime;

            if (roundTime > 0 && roundStartTime > 0)
            {
                _debugLogger.LogWarning(
                    "[GameRules] TryGetRoundTiming using fallback timing. roundTime={RoundTime} roundStartTime={RoundStartTime}",
                    roundTime,
                    roundStartTime
                );
                return true;
            }

            _debugLogger.LogWarning(
                "[GameRules] TryGetRoundTiming failed. gamerules unavailable and fallback timing invalid. roundTime={RoundTime} roundStartTime={RoundStartTime}",
                roundTime,
                roundStartTime
            );
            return false;
        }

        public void OnMapStart(string map)
        {
            LoadRoundTimeCvar();
            _fallbackRoundStartTime = Server.CurrentTime;
            SetGameRulesAsync();
        }


        public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
        {
            LoadRoundTimeCvar();
            _fallbackRoundStartTime = Server.CurrentTime;
            SetGameRules();
            return HookResult.Continue;
        }

        public HookResult OnAnnounceWarmup(EventRoundAnnounceWarmup @event, GameEventInfo info)
        {
            LoadRoundTimeCvar();
            _fallbackRoundStartTime = Server.CurrentTime;
            SetGameRules();
            return HookResult.Continue;
        }

        public bool WarmupRunning => GetValidGameRules()?.WarmupPeriod ?? false;

        public int TotalRoundsPlayed => GetValidGameRules()?.TotalRoundsPlayed ?? 0;
        public int RoundTime
        {
            get => GetValidGameRules()?.RoundTime ?? 0;
            set
            {
                var gameRules = GetValidGameRules();
                _fallbackRoundTimeSeconds = value;
                if (gameRules != null)
                    gameRules.RoundTime = value;
            }
        }
    }
}
