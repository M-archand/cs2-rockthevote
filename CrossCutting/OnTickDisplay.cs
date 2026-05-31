using System.Text;
using static CounterStrikeSharp.API.Core.Listeners;

namespace cs2_rockthevote.CrossCutting
{
    public class OnTickDisplay : IPluginDependency<Plugin, Config>
    {
        private readonly PluginState _pluginState;
        private readonly EndMapVoteManager _endMap;
        private readonly RockTheVoteCommand _rtv;
        private readonly ExtendRoundTimeManager _voteExtend;
        private readonly StringLocalizer _localizer;
        private readonly StringBuilder _hudBuilder = new();
        private GeneralConfig _generalConfig = new();
        private EndOfMapConfig _endMapConfig = new();
        private VoteExtendConfig _voteExtendConfig = new();
        private NominateConfig _nomConfig = new();
        private RtvConfig _rtvConfig = new();

        public OnTickDisplay(PluginState pluginState, StringLocalizer localizer, EndMapVoteManager endMap, RockTheVoteCommand rtv, ExtendRoundTimeManager voteExtend)
        {
            _pluginState = pluginState;
            _localizer = localizer;
            _endMap = endMap;
            _rtv = rtv;
            _voteExtend = voteExtend;
        }

        public void OnConfigParsed(Config config)
        {
            _generalConfig = config.General;
            _endMapConfig = config.EndOfMapVote;
            _voteExtendConfig = config.VoteExtend;
            _rtvConfig = config.Rtv;
        }

        public void OnLoad(Plugin plugin)
        {
            if (_endMapConfig.CountdownType == "hud" || _rtvConfig.CountdownType == "hud" || _voteExtendConfig.CountdownType == "hud" || _endMapConfig.MenuType == "HudMenu" || _nomConfig.MenuType == "HudMenu")
            {
                plugin.RegisterListener<OnTick>(PlayerOnTick);
            }
        }

        public void Unload(Plugin plugin)
        {
            plugin.RemoveListener<OnTick>(PlayerOnTick);
        }

        public void PlayerOnTick()
        {
            // Only shown while a vote is running
            if (!_pluginState.EofVoteHappening && !_pluginState.ExtendTimeVoteHappening && !_pluginState.RtvVoteHappening)
                return;

            // Resolve the valid-player set once per tick, each call allocates a fresh array.
            var players = ServerManager.ValidPlayers();

            // EndMapVote HUD Countdown. Don't show if EnabledHudMenu true, otherwise this would be covered by the map list
            if (_endMapConfig.EnableCountdown && _endMapConfig.CountdownType == "hud" && _pluginState.EofVoteHappening && _endMapConfig.MenuType != "HudMenu")
            {
                string countdown = _localizer.Localize("emv.hud.timer", _endMap.TimeLeft);
                foreach (var player in players)
                    player.PrintToCenter(countdown);
            }

            // RTV HUD Countdown
            if (_rtvConfig.EnableCountdown && _rtvConfig.CountdownType == "hud" && _pluginState.RtvVoteHappening)
            {
                string countdown = _localizer.Localize("general.hud-countdown", _rtv.TimeLeft);
                foreach (var player in players)
                    player.PrintToCenter(countdown);
            }

            // VoteExtend HUD Countdown
            if (_voteExtendConfig.EnableCountdown && _voteExtendConfig.CountdownType == "hud" && _pluginState.ExtendTimeVoteHappening)
            {
                string countdown = _localizer.Localize("general.hud-countdown", _voteExtend.TimeLeft);
                foreach (var player in players)
                    player.PrintToCenter(countdown);
            }

            // HUD map vote list
            if (_endMapConfig.MenuType == "HudMenu" && _pluginState.EofVoteHappening)
            {
                _hudBuilder.Clear();
                _hudBuilder.Append($"<b><font color='yellow'>{_localizer.Localize("emv.hud.timer", _endMap.TimeLeft)}</font></b>");

                int idx = 1;
                const string header = "<br><font color='yellow'>!{0}</font> {1} <font color='lime'>({2})</font>";
                foreach (var kv in _endMap.SortedTopVotes)
                {
                    _hudBuilder.AppendFormat(header, idx++, kv.Key, kv.Value);
                }

                var hud = _hudBuilder.ToString();
                foreach (var player in players)
                {
                    if (player.UserId == null)
                        continue;

                    var userId = player.UserId!.Value;
                    if (_generalConfig.HideHudAfterVote && _endMap.VotedPlayers.Contains(userId))
                        continue;

                    player.PrintToCenterHtml(hud);
                }
            }
        }
        
    }
}
