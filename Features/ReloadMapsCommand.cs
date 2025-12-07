using System.IO;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    public partial class Plugin
    {
        [ConsoleCommand("css_reloadmaps", "Reloads the map list from maplist.txt.")]
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        public void OnReloadMapsCommand(CCSPlayerController? player, CommandInfo command)
        {
            _reloadMapsCommand.CommandHandler(player, command);
        }
    }

    public class ReloadMapsCommand : IPluginDependency<Plugin, Config>
    {
        private readonly MapLister _mapLister;
        private readonly ILogger<ReloadMapsCommand> _logger;
        private GeneralConfig _generalConfig = new();

        public ReloadMapsCommand(MapLister mapLister, ILogger<ReloadMapsCommand> logger)
        {
            _mapLister = mapLister;
            _logger = logger;
        }

        public void OnConfigParsed(Config config)
        {
            _generalConfig = config.General;
        }

        public void CommandHandler(CCSPlayerController? player, CommandInfo command)
        {
            string permission = _generalConfig.AdminPermission;
            bool requiresPermission = !string.IsNullOrWhiteSpace(permission);

            if (player != null && requiresPermission && !AdminManager.PlayerHasPermissions(player, permission))
            {
                command.ReplyToCommand($"[RTV] {ChatColors.Red}You do not have the correct permission to execute this command.");
                return;
            }

            try
            {
                _mapLister.LoadMaps();
                command.ReplyToCommand($"[RTV] {ChatColors.Lime}Map list reloaded successfully.");
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "maplist.txt not found while running css_reloadmaps.");
                command.ReplyToCommand($"[RTV] {ChatColors.Red}maplist.txt not found.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload map list via css_reloadmaps.");
                command.ReplyToCommand($"[RTV] {ChatColors.Red}Failed to reload map list. Check server logs for details.");
            }
        }
    }
}
