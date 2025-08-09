using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
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

        private string[] _permission = ["@css/root"];
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
            _permission = string.IsNullOrWhiteSpace(_config.Permission)
            ? Array.Empty<string>()
            : [.. _config.Permission
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.Ordinal)];

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

            if (_permission.Length > 0)
            {
                bool allowed = _permission.Any(perm => AdminManager.PlayerHasPermissions(player, perm));
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