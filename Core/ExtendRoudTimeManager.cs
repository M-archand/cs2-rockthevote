using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using cs2_rockthevote.Core;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System;
using System.Data;

namespace cs2_rockthevote
{
    public class ExtendRoundTimeManager(IStringLocalizer stringLocalizer, PluginState pluginState, TimeLimitManager timeLimitManager, GameRules gameRules, ILogger<ExtendRoundTimeManager> logger) : IPluginDependency<Plugin, Config>
    {
        private readonly StringLocalizer _localizer = new(stringLocalizer, "extendtime.prefix");
        private readonly ILogger<ExtendRoundTimeManager> _logger = logger;

        private PluginState _pluginState = pluginState;
        private TimeLimitManager _timeLimitManager = timeLimitManager;
        private Timer? Timer;
        private GameRules _gameRules = gameRules;

        private readonly Dictionary<int, string> Votes = new();
        public int TimeLeft { get; private set; } = -1;

        private EndOfMapConfig _config = new();
        private VoteExtendConfig _voteExtendConfig = new();
        private GeneralConfig _generalConfig = new();

        private int _canVote = 0;
        private Plugin? _plugin;

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
        }

        public void OnMapStart(string map)
        {
            Votes.Clear();
            TimeLeft = 0;
            KillTimer();
        }

        public void OnConfigParsed(Config config)
        {
            _config = config.EndOfMapVote;
            _voteExtendConfig = config.VoteExtend;
            _generalConfig = config.General;
        }

        public void ExtendTimeVoted(CCSPlayerController player, string voteResponse)
        {
            int userId = player.UserId!.Value;

            if (Votes.ContainsKey(userId))
                return;

            Votes[userId] = voteResponse;
            player.PrintToCenter(_localizer.LocalizeWithPrefix("extendtime.you-voted", voteResponse));

            if (Votes.Count >= _canVote)
                ExtendTimeVote();
        }

        public void KillTimer()
        {
            TimeLeft = -1;
            if (Timer is not null)
            {
                Timer!.Kill();
                Timer = null;
            }
        }

        public void RemoveVote(int userId)
        {
            Votes.Remove(userId);
        }

        public static void PrintCenterTextAll(string text)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player.IsValid)
                {
                    player.PrintToCenter(text);
                }
            }
        }

        public void ChatCountdown(int secondsLeft)
        {
            if (!_pluginState.ExtendTimeVoteHappening || !_voteExtendConfig.EnableCountdown || _voteExtendConfig.CountdownType != "chat")
                return;

            string text = _localizer.LocalizeWithPrefix("general.chat-countdown", secondsLeft);
            foreach (var player in ServerManager.ValidPlayers())
                player.PrintToChat(text);

            int nextSecondsLeft = secondsLeft - _voteExtendConfig.ChatCountdownInterval;
            if (nextSecondsLeft <= 0)
                return;

            _plugin?.AddTimer(
                _voteExtendConfig.ChatCountdownInterval, () =>
                {
                    try
                    {
                        ChatCountdown(nextSecondsLeft);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Extend-time ChatCountdown timer callback failed: {ex.Message}");
                    }
                }, TimerFlags.STOP_ON_MAPCHANGE
            );
        }

        public void ExtendTimeVote()
        {
            bool mapEnd = _config is EndOfMapConfig;
            KillTimer();

            int minutesToExtend = _generalConfig.RoundTimeExtension;

            var grouped = Votes.Values.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());

            int totalVotes = Votes.Count;

            if (grouped.Count == 0)
            {
                Server.PrintToChatAll(_localizer.LocalizeWithPrefix("extendtime.vote-ended-no-votes"));
                return;
            }

            int maxVotes = grouped.Values.Max();
            var potentialWinners = grouped.Where(x => x.Value == maxVotes).ToList();
            var rnd = new Random();
            var winner = potentialWinners[rnd.Next(potentialWinners.Count)];

            decimal percent = totalVotes > 0 ? (decimal)winner.Value / totalVotes * 100M : 0;

            if (maxVotes > 0)
            {
                if (winner.Key == "No")
                {
                    Server.PrintToChatAll(_localizer.LocalizeWithPrefix("extendtime.vote-ended.failed", percent, totalVotes));
                }
                else
                {
                    Server.PrintToChatAll(_localizer.LocalizeWithPrefix("extendtime.vote-ended.passed", minutesToExtend, percent, totalVotes));
                }
            }
            else
            {
                Server.PrintToChatAll(_localizer.LocalizeWithPrefix("extendtime.vote-ended-no-votes"));
            }

            if (winner.Key == "Yes")
            {
                ExtendRoundTime(minutesToExtend, _timeLimitManager, _gameRules);
                _pluginState.MapExtensionCount++;
            }

            _pluginState.ExtendTimeVoteHappening = false;
        }

        public void StartExtendVote(VoteExtendConfig config)
        {
            Votes.Clear();
            _pluginState.ExtendTimeVoteHappening = true;
            _voteExtendConfig = config;
            
            if (_voteExtendConfig.EnableCountdown && _voteExtendConfig.CountdownType == "chat")
            {
                ChatCountdown(_voteExtendConfig.VoteDuration);
            }

            _canVote = ServerManager.ValidPlayerCount();

            ChatMenu menu = new(_localizer.Localize("extendtime.hud.menu-title"));

            var answers = new List<string>() { "Yes", "No" };

            foreach (var answer in answers)
            {
                menu.AddMenuOption(answer, (player, option) => {
                    ExtendTimeVoted(player, answer);
                    MenuManager.CloseActiveMenu(player);
                });
            }

            foreach (var player in ServerManager.ValidPlayers())
                MenuManager.OpenChatMenu(player, menu);

            VoteCountdown();
        }

        public void VoteCountdown()
        {
            TimeLeft = _voteExtendConfig.VoteDuration;

            Timer = _plugin!.AddTimer(1.0f, () =>
                {
                    if (TimeLeft <= 0 && !_voteExtendConfig.EnablePanorama)
                        ExtendTimeVote();
                    else
                        TimeLeft--;
                },
                TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE
            );
        }

        public bool ExtendRoundTime(int minutesToExtendBy)
        {
            return ExtendRoundTime(minutesToExtendBy, _timeLimitManager, _gameRules);
        }

        public bool ExtendRoundTime(int minutesToExtendBy, TimeLimitManager timeLimitManager, GameRules gameRules)
        {
            try
            {
                int timePlayed = (int)timeLimitManager.TimePlayed;
                int currentRemainingSeconds = timeLimitManager.UnlimitedTime
                    ? Math.Max(0, gameRules.RoundTime - timePlayed)
                    : (int)Math.Max(0, Math.Ceiling(timeLimitManager.TimeRemaining));
                int newRemainingSeconds = currentRemainingSeconds + (minutesToExtendBy * 60);
                int newTotalSeconds = timePlayed + newRemainingSeconds;

                gameRules.RoundTime = newTotalSeconds;

                var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First();
                Utilities.SetStateChanged(gameRulesProxy, "CCSGameRulesProxy", "m_pGameRules");
                
                if (!timeLimitManager.UnlimitedTime)
                    timeLimitManager.TimeRemaining = newTotalSeconds / 60M;
                _pluginState.MapChangeScheduled = false;
                _pluginState.EofVoteHappening = false;
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Something went wrong when updating the round time: {ex.Message}");
                return false;
            }
        }
    }
}