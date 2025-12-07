using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    public partial class Plugin
    {
        [ConsoleCommand("css_maps",   "Displays the available maps in console")]
        [ConsoleCommand("css_maplist","Displays the available maps in console")]
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        public void OnMaplistCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null) return;
            _maplistManager.CommandHandler(player);
        }
    }

    public class MaplistCommand : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<RockTheVoteCommand> _logger;
        private readonly StringLocalizer _localizer;
        private readonly MapLister _mapLister;

        public MaplistCommand(MapLister mapLister, StringLocalizer localizer, ILogger<RockTheVoteCommand> logger)
        {
            _mapLister = mapLister;
            _localizer = localizer;
            _logger = logger;
        }

        public void CommandHandler(CCSPlayerController? player)
        {
            if (player != null)
            try
            {
                var maps = _mapLister.Maps;
                if (maps == null || maps.Length == 0)
                {
                    player.PrintToChat($" {ChatColors.LightRed}[MapList]{ChatColors.Default} The map list is empty!");
                    return;
                }

                // Tell them to check console
                player.PrintToChat($" {ChatColors.LightRed}[MapList]{ChatColors.Default} Maps have been printed to the console.");

                player.PrintToConsole("====================================");
                player.PrintToConsole("             Server Map List");
                player.PrintToConsole($"             Total Maps: {maps.Length}");
                player.PrintToConsole("====================================");

                foreach (var map in maps)
                {
                    if (string.IsNullOrWhiteSpace(map.Name)) continue;
                    player.PrintToConsole(map.Name);
                }

                player.PrintToConsole("====================================");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[MapList ]An error occurred while reading the map list: {ex.Message}");
            }
        }
    }
}