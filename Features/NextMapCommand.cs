using CounterStrikeSharp.API.Core;

namespace cs2_rockthevote.Features
{
    public class NextMapCommand(ChangeMapManager changeMapManager, StringLocalizer stringLocalizer) : IPluginDependency<Plugin, Config>
    {
        private ChangeMapManager _changeMapManager = changeMapManager;
        private StringLocalizer _stringLocalizer = stringLocalizer;

        public void CommandHandler(CCSPlayerController? player)
        {
            string text;
            if (_changeMapManager.NextMap is not null)
                text = _stringLocalizer.LocalizeWithPrefix("nextmap", _changeMapManager.NextMap);
            else
                text = _stringLocalizer.LocalizeWithPrefix("nextmap.decided-by-vote");

            player?.PrintToChat(text);
        }

        public void OnLoad(Plugin plugin)
        {
            plugin.AddCommand("nextmap", "Shows nextmap when defined", (player, info) =>
            {
                CommandHandler(player);
            });
        }
    }
}
