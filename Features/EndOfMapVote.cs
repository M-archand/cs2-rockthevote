using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using cs2_rockthevote.Core;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    public class EndOfMapVote(TimeLimitManager timeLimit, MaxRoundsManager maxRounds, PluginState pluginState, GameRules gameRules, EndMapVoteManager voteManager, ILogger<EndOfMapVote> logger) : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<EndOfMapVote> _logger = logger;
        private TimeLimitManager _timeLimit = timeLimit;
        private MaxRoundsManager _maxRounds = maxRounds;
        private PluginState _pluginState = pluginState;
        private GameRules _gameRules = gameRules;
        private EndMapVoteManager _voteManager = voteManager;
        private EndOfMapConfig _config = new();
        private Timer? _timer;
        private Plugin? _plugin;

        bool CheckMaxRounds()
        {
            //Server.PrintToChatAll($"Remaining rounds {_maxRounds.RemainingRounds}, remaining wins: {_maxRounds.RemainingWins}, triggerBefore {_config.TriggerRoundsBeforeEnd}");
            if (_maxRounds.UnlimitedRounds)
                return false;

            if (_maxRounds.RemainingRounds <= _config.TriggerRoundsBeforeEnd)
                return true;

            return _maxRounds.CanClinch && _maxRounds.RemainingWins <= _config.TriggerRoundsBeforeEnd;
        }


        bool CheckTimeLeft()
        {
            return !_timeLimit.UnlimitedTime && _timeLimit.TimeRemaining <= _config.TriggerSecondsBeforeEnd;
        }

        public void StartVote()
        {
            KillTimer();
            _voteManager.StartVote(isRtv: false);
            /*if (_config.Enabled)
            {
                if (_config.MenuType == "ScreenMenu" && PanoramaVote.IsVoteInProgress())
                {
                    PanoramaVote.EndVote(YesNoVoteEndReason.VoteEnd_Cancelled, overrideFailCode: 0);
                    _plugin?.AddTimer(
                        3.5f, () =>
                        {
                            try
                            {
                                _voteManager.StartVote(isRtv: false);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Vote start timer callback failed");
                            }
                        }, TimerFlags.STOP_ON_MAPCHANGE
                    );
                }
                else
                {
                    _voteManager.StartVote(isRtv: false);
                }
            }*/
        }

        public void OnMapStart(string map)
        {
            KillTimer();
        }

        void KillTimer()
        {
            _timer?.Kill();
            _timer = null;
        }

        void RestartTimer()
        {
            KillTimer();

            if (_plugin is null || _timeLimit.UnlimitedTime || !_config.Enabled)
                return;

            if (_gameRules?.WarmupRunning == true || _pluginState.DisableCommands)
                return;

            _timer = _plugin.AddTimer(1.0F, () =>
            {
                if (_gameRules is not null && !_gameRules.WarmupRunning && !_pluginState.DisableCommands && _timeLimit.TimeRemaining > 0)
                {
                    if (CheckTimeLeft())
                        StartVote();
                }
            }, TimerFlags.REPEAT);
        }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;

            plugin.RegisterEventHandler<EventRoundStart>((ev, info) =>
            {
                RestartTimer();

                if (!_pluginState.DisableCommands && !_gameRules.WarmupRunning && CheckMaxRounds() && _config.Enabled)
                    StartVote();

                return HookResult.Continue;
            });


            plugin.RegisterEventHandler<EventRoundAnnounceMatchStart>((ev, info) =>
            {
                RestartTimer();
                return HookResult.Continue;
            });
        }

        public void OnConfigParsed(Config config)
        {
            _config = config.EndOfMapVote;
        }
    }
}
