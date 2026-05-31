using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CS2MenuManager.API.Class;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    public class MapChooserCommand : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<MapChooserCommand> _logger;
        private readonly StringLocalizer _localizer;
        private readonly MapLister _mapLister;
        private Plugin? _plugin;

        private string[] _permissions = ["@css/root", "@css/admin"];
        private MapChooserConfig _config = new();

        public MapChooserCommand(StringLocalizer localizer, MapLister mapLister, ILogger<MapChooserCommand> logger)
        {
            _localizer = localizer;
            _mapLister = mapLister;
            _logger = logger;
        }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
        }

        public void OnConfigParsed(Config config)
        {
            _config = config.MapChooser;
            _permissions = _config.Permissions;

            if (string.IsNullOrWhiteSpace(_config.Command))
                return;

            Server.NextFrame(() =>
            {
                foreach (var alias in _config.Command.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    _plugin?.AddCommand(alias, "Opens the Map Chooser Menu", ExecuteCommand);
                }
            });
        }

        private void ExecuteCommand(CCSPlayerController? player, CommandInfo info)
        {
            if (player == null || !player.IsValid)
                return;

            if (_permissions.Length > 0)
            {
                bool allowed = PermissionUtility.HasAny(player, _permissions);
                if (!allowed)
                {
                    player.PrintToChat(_localizer.LocalizeWithPrefix("general.incorrect.permission"));
                    return;
                }
            }
            var maps = _mapLister.Maps;
            if (maps is null || maps.Length == 0)
                return;

            var menuType = MenuManager.MenuTypesList.TryGetValue(_config.MenuType ?? "", out var resolvedType)
                ? resolvedType
                : MenuTypeManager.GetDefaultMenu();

            var menu = MenuManager.MenuByType(menuType, _localizer.Localize("general.choose.map"), _plugin!);

            foreach (var map in maps)
            {
                menu.AddItem(map.Name, (p, _) =>
                {
                    if (p == null || !p.IsValid)
                        return;

                    MenuManager.CloseActiveMenu(p);

                    if (!string.IsNullOrEmpty(map.Id) && ulong.TryParse(map.Id, out var mapId))
                        Server.ExecuteCommand($"host_workshop_map {mapId}");
                    else
                        Server.ExecuteCommand($"changelevel {map.Name}");
                });
            }

            menu.Display(player, 0);
        }
    }
}