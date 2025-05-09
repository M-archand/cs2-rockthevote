using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using cs2_rockthevote.Core;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text;
using static CounterStrikeSharp.API.Core.Listeners;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace cs2_rockthevote
{
    public class ExtendRoundTimeManager(IStringLocalizer stringLocalizer, PluginState pluginState, TimeLimitManager timeLimitManager, GameRules gameRules, ILogger<ExtendRoundTimeManager> logger) : IPluginDependency<Plugin, Config>
    {
        const int MAX_OPTIONS_HUD_MENU = 6;
        private readonly StringLocalizer _localizer = new(stringLocalizer, "extendtime.prefix");
        private readonly ILogger<ExtendRoundTimeManager> _logger = logger;

        private PluginState _pluginState = pluginState;
        private TimeLimitManager _timeLimitManager = timeLimitManager;
        private Timer? Timer;
        private GameRules _gameRules = gameRules;

        Dictionary<string, int> Votes = new();
        int timeLeft = -1;

        private IEndOfMapConfig? _config = null;
        private VoteExtendConfig _voteExtendConfig = new();

        private int _canVote = 0;
        private Plugin? _plugin;

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
            plugin.RegisterListener<OnTick>(VoteDisplayTick);
        }

        public void OnMapStart(string map)
        {
            Votes.Clear();
            timeLeft = 0;
            KillTimer();
        }

        public void OnConfigParsed(Config config)
        {
            _config = config.EndOfMapVote;
            _voteExtendConfig = config.VoteExtend;
        }

        public void ExtendTimeVoted(CCSPlayerController player, string voteResponse)
        {
            Votes[voteResponse] += 1;
            player.PrintToCenter(_localizer.LocalizeWithPrefix("extendtime.you-voted", voteResponse));
            if (Votes.Select(x => x.Value).Sum() >= _canVote)
            {
                ExtendTimeVote();
            }
        }

        void KillTimer()
        {
            timeLeft = -1;
            if (Timer is not null)
            {
                Timer!.Kill();
                Timer = null;
            }
        }

        void PrintCenterTextAll(string text)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player.IsValid)
                {
                    player.PrintToCenter(text);
                }
            }
        }

        public void VoteDisplayTick()
        {
            if (timeLeft < 0)
                return;

            int index = 1;
            StringBuilder stringBuilder = new();
            stringBuilder.AppendFormat($"{_localizer.Localize("extendtime.hud.hud-timer", timeLeft)}");
            if (!_config!.HudMenu)
                foreach (var kv in Votes.OrderByDescending(x => x.Value).Take(MAX_OPTIONS_HUD_MENU).Where(x => x.Value > 0))
                {
                    stringBuilder.AppendFormat($"<br>{kv.Key} <font color='green'>({kv.Value})</font>");
                }
            else
                foreach (var kv in Votes.Take(MAX_OPTIONS_HUD_MENU))
                {
                    stringBuilder.AppendFormat($"<br><font color='yellow'>!{index++}</font> {kv.Key} <font color='green'>({kv.Value})</font>");
                }

            foreach (CCSPlayerController player in ServerManager.ValidPlayers())
            {
                if (_voteExtendConfig.EnableCountdown)
                {
                    if (_voteExtendConfig.HudCountdown)
                    {
                        player.PrintToCenter(stringBuilder.ToString());
                    }
                    else
                    {
                        player.PrintToChat(stringBuilder.ToString());
                    }
                }
            }
        }

        void ExtendTimeVote()
        {
            bool mapEnd = _config is EndOfMapConfig;
            KillTimer();

            var minutesToExtend = _config?.RoundTimeExtension ?? 15;

            decimal maxVotes = Votes.Select(x => x.Value).Max();
            IEnumerable<KeyValuePair<string, int>> potentialWinners = Votes.Where(x => x.Value == maxVotes);
            Random rnd = new();
            KeyValuePair<string, int> winner = potentialWinners.ElementAt(rnd.Next(0, potentialWinners.Count()));

            decimal totalVotes = Votes.Select(x => x.Value).Sum();
            decimal percent = totalVotes > 0 ? winner.Value / totalVotes * 100M : 0;

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

            if (winner.Key == "No")
            {
                PrintCenterTextAll(_localizer.Localize("extendtime.hud.finished", "not be extended."));
            }
            else
            {
                ExtendRoundTime(minutesToExtend, _timeLimitManager, _gameRules);
                PrintCenterTextAll(_localizer.Localize("extendtime.hud.finished", "be extended."));
            }

            _pluginState.ExtendTimeVoteHappening = false;
        }

        public void StartExtendVote(VoteExtendConfig config)
        {
            Votes.Clear();
            _pluginState.ExtendTimeVoteHappening = true;
            _voteExtendConfig = config;

            _canVote = ServerManager.ValidPlayerCount();

            ChatMenu menu = new(_localizer.Localize("extendtime.hud.menu-title"));

            var answers = new List<string>() { "Yes", "No" };

            foreach (var answer in answers)
            {
                Votes[answer] = 0;
                menu.AddMenuOption(answer, (player, option) => {
                    ExtendTimeVoted(player, answer);
                    MenuManager.CloseActiveMenu(player);
                });
            }

            foreach (var player in ServerManager.ValidPlayers())
                MenuManager.OpenChatMenu(player, menu);

            timeLeft = _voteExtendConfig.VoteDuration;
            Timer = _plugin!.AddTimer(1.0F, () =>
            {
                if (timeLeft <= 0)
                {
                    ExtendTimeVote();
                }
                else
                    timeLeft--;
            }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }
        public bool ExtendRoundTime(int minutesToExtendBy)
        {
            return ExtendRoundTime(minutesToExtendBy, _timeLimitManager, _gameRules);
        }

        public bool ExtendRoundTime(int minutesToExtendBy, TimeLimitManager timeLimitManager, GameRules gameRules)
        {
            try
            {
                int timePlayed = (int)(Server.CurrentTime - gameRules.GameStartTime);
                
                gameRules.RoundTime += minutesToExtendBy * 60;
                
                var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First();
                Utilities.SetStateChanged(gameRulesProxy, "CCSGameRulesProxy", "m_pGameRules");
                
                int newRemainingSeconds = gameRules.RoundTime - timePlayed;
                
                _timeLimitManager.TimeRemaining = newRemainingSeconds / 60M;
                _pluginState.MapChangeScheduled = false;
                _pluginState.EofVoteHappening = false;
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Something went wrong when updating the round time: {message}", ex.Message);
                return false;
            }
        }
    }
}