using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using cs2_rockthevote.Core;
using Microsoft.Extensions.Localization;

namespace cs2_rockthevote
{
    public partial class Plugin
    {
        public void OnTimeLeft(CCSPlayerController? player, CommandInfo? command)
        {
            _timeLeft.CommandHandler(player);
        }
    }

    public class TimeLeftCommand(TimeLimitManager timeLimitManager, MaxRoundsManager maxRoundsManager, GameRules gameRules, IStringLocalizer stringLocalizer) : IPluginDependency<Plugin, Config>
    {
        private TimeLimitManager _timeLimitManager = timeLimitManager;
        private MaxRoundsManager _maxRoundsManager = maxRoundsManager;
        private readonly GameRules _gameRules = gameRules;
        private StringLocalizer _localizer = new StringLocalizer(stringLocalizer, "rtv.prefix");

        public void CommandHandler(CCSPlayerController? player)
        {
            string text;

            if (_gameRules.WarmupRunning)
            {
                if (player != null)
                    player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.warmup"));
                else
                    Server.PrintToConsole(_localizer.LocalizeWithPrefix("general.validation.warmup"));
                return;
            }

            if (!_timeLimitManager.UnlimitedTime)
            {
                if (_timeLimitManager.TimeRemaining > 1)
                {
                    TimeSpan remainingFull = TimeSpan.FromSeconds((double)_timeLimitManager.TimeRemaining);
                    string remaining = remainingFull.ToString(@"hh\:mm\:ss");
                    text = _localizer.Localize("general.timeleft", remaining);
                }
                else
                {
                    text = "Time over.";
                }
            }
            else if (!_maxRoundsManager.UnlimitedRounds)
            {
                if (_maxRoundsManager.RemainingRounds > 1)
                    text = $"Remaining Rounds: {_maxRoundsManager.RemainingRounds}";
                else
                    text = "Last round.";
            }
            else
            {
                text = "No time limit.";
            }

            if (player != null)
                player.PrintToChat(text);
            else
                Server.PrintToConsole(text);
        }
    }
}
