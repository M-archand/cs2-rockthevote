using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CS2MenuManager.API.Menu;
using CS2MenuManager.API.Class;
using CS2MenuManager.API.Enum;
using cs2_rockthevote.Core;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    public partial class Plugin
    {
        [GameEventHandler(HookMode.Pre)]
        public HookResult EventPlayerDisconnectNominate(EventPlayerDisconnect @event, GameEventInfo @eventInfo)
        {
            var player = @event.Userid;
            if (player != null)
            {
                _nominationManager.PlayerDisconnected(player);
            }
            return HookResult.Continue;
        }
    }

    public class NominationCommand : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<NominationCommand> _logger;
        Dictionary<int, List<string>> Nominations = new();
        private ChatMenu? _nominationMenu;
        private NominateConfig _nomConfig = new();
        private GeneralConfig _generalConfig = new();
        private GameRules _gamerules;
        private StringLocalizer _localizer;
        private PluginState _pluginState;
        private MapCooldown _mapCooldown;
        private MapLister _mapLister;
        private Plugin? _plugin;
        private string[] _permission = Array.Empty<string>();

        public NominationCommand(MapLister mapLister, GameRules gamerules, StringLocalizer localizer, PluginState pluginState, MapCooldown mapCooldown, ILogger<NominationCommand> logger)
        {
            _mapLister = mapLister;
            _mapLister.EventMapsLoaded += OnMapsLoaded;
            _gamerules = gamerules;
            _localizer = localizer;
            _pluginState = pluginState;
            _mapCooldown = mapCooldown;
            _logger = logger;
            _mapCooldown.EventCooldownRefreshed += OnMapsLoaded;
        }

        public void OnMapStart(string map)
        {
            Nominations.Clear();
        }
        
        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
        }

        public void OnConfigParsed(Config config)
        {
            _nomConfig = config.Nominate;
            _generalConfig = config.General;
            
            // Parse permission
            _permission = string.IsNullOrWhiteSpace(_nomConfig.Permission)
                ? Array.Empty<string>()
                : [.. _nomConfig.Permission
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal)];

            // Register dynamic commands
            if (string.IsNullOrWhiteSpace(_generalConfig.NominateCommand))
                return;

            Server.NextFrame(() =>
            {
                foreach (var alias in _generalConfig.NominateCommand.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    _plugin?.AddCommand(alias, "Nominate a map to appear in the vote.", ExecuteCommand);
                }
            });
        }

        private void ExecuteCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid)
                return;

            // Check permission
            if (_permission.Length > 0)
            {
                bool allowed = _permission.Any(perm => AdminManager.PlayerHasPermissions(player, perm));
                if (!allowed)
                {
                    command.ReplyToCommand(_localizer.LocalizeWithPrefix("general.incorrect.permission"));
                    return;
                }
            }

            CommandHandler(player, command.GetArg(1)?.Trim().ToLower() ?? "");
        }

        public void OnMapsLoaded(object? sender, Map[] maps)
        {
            _nominationMenu = new ChatMenu(_localizer.Localize("nominate.title"), _plugin!);

            foreach (var map in _mapLister.Maps!.Where(x => !GetBaseMapName(x.Name).Equals(Server.MapName, StringComparison.OrdinalIgnoreCase)))
            {
                bool isCooldown = _mapCooldown.IsMapInCooldown(map.Name);
                string displayName = isCooldown ? $"{ChatColors.Grey}{map.Name}" : map.Name;

                var item = _nominationMenu.AddItem(displayName, (player, _) =>
                {
                    Nominate(player, map.Name);
                });

                if (isCooldown)
                    item.DisableOption = DisableOption.DisableShowNumber;
            }
        }

        public void CommandHandler(CCSPlayerController? player, string map)
        {
            if (player == null)
                return;

            if (_pluginState.DisableCommands || !_nomConfig.Enabled || _pluginState.EofVoteHappening)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.disabled"));
                return;
            }

            if (_gamerules.WarmupRunning && !_nomConfig.EnabledInWarmup)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.warmup"));
                return;
            }
            
            int userId = player.UserId!.Value;
            int existingCount = Nominations.TryGetValue(userId, out var userNoms) ? userNoms.Count : 0;
            if (_nomConfig.NominateLimit > 0 && existingCount >= _nomConfig.NominateLimit)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("nominate.limit", _nomConfig.NominateLimit));
                return;
            }

            var mapName = map.Trim();

            if (string.IsNullOrEmpty(mapName))
            {
                var title = _localizer.Localize("nominate.title");
                var key = _nomConfig.MenuType?.Trim() ?? "";
                var menuType = MenuManager.MenuTypesList.TryGetValue(key, out var resolvedType)
                    ? resolvedType
                    : MenuTypeManager.GetDefaultMenu();

                var menu = MenuManager.MenuByType(menuType, title, _plugin!);

                foreach (var m in _mapLister.Maps!
                            .Where(x => !GetBaseMapName(x.Name).Equals(Server.MapName, StringComparison.OrdinalIgnoreCase)))
                {
                    bool isCooldown = _mapCooldown.IsMapInCooldown(m.Name);
                    string label = isCooldown ? $"{ChatColors.Grey}{m.Name}" : m.Name;
                    string chosen = m.Name;

                    menu.AddItem(label, (p, _) =>
                    {
                        Nominate(p, chosen);
                    }, isCooldown ? DisableOption.DisableShowNumber : DisableOption.None);
                }

                menu.Display(player, 0);
                return;
            }

            var resolved = ResolveMapNameOrPrompt(player, mapName, _localizer);
            if (resolved == null)
                return;

            Nominate(player, resolved);
        }

        /*
        public void OpenScreenMenu(CCSPlayerController player)
        {
            // Build the list of map names, skipping the current map and the ones on cool down
            var voteOptions = _mapLister.Maps!
                .Where(m => !GetBaseMapName(m.Name)
                       .Equals(Server.MapName, StringComparison.OrdinalIgnoreCase)
                    && !_mapCooldown.IsMapInCooldown(m.Name))
                .Select(m => m.Name)
                .ToList();

            // Guard: nothing to nominate
            if (voteOptions.Count == 0)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("An error occured."));
                _logger.LogError("[Nominate] An error occured while using the !nominate command with ScreenMenu, no maps could be found.");
                return;
            }

            // Once the list is built, we open the menu on the next frame
            Server.NextFrame(() =>
                MapVoteScreenMenu.Open(
                    _plugin!,
                    player,
                    voteOptions,
                    (p, mapName) => CommandHandler(p, mapName),
                    _localizer.Localize("nominate.title")
            ));
        }
        */

        public void Nominate(CCSPlayerController player, string map)
        {
            var userId  = player.UserId!.Value;
            var mapName = map.Trim();
            var baseName = GetBaseMapName(mapName);

            // Ensure per-player list exists
            if (!Nominations.TryGetValue(userId, out var userNoms))
            {
                userNoms = new List<string>();
                Nominations[userId] = userNoms;
            }

            // Respect warmup here too (so menu + chat behave the same)
            if (_gamerules.WarmupRunning && !_nomConfig.EnabledInWarmup)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.warmup"));
                return;
            }

            // Enforce per-player nomination limit
            if (userNoms.Count >= _nomConfig.NominateLimit)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("nominate.limit", _nomConfig.NominateLimit));
                return;
            }

            // Prevent nominating the same map multiple times by this player
            if (userNoms.Contains(mapName, StringComparer.OrdinalIgnoreCase))
            {
                int voteCount = Nominations.Values
                    .SelectMany(v => v)
                    .Count(m => m.Equals(mapName, StringComparison.OrdinalIgnoreCase));

                player.PrintToChat(_localizer.LocalizeWithPrefix("nominate.already-nominated", mapName, voteCount));
                return;
            }

            // Can't nominate the current map
            if (baseName.Equals(Server.MapName, StringComparison.OrdinalIgnoreCase))
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.current-map"));
                return;
            }

            // Can't nominate a map on cooldown
            if (_mapCooldown.IsMapInCooldown(baseName))
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.map-played-recently"));
                return;
            }

            // ✅ All validations passed — record the nomination now
            userNoms.Add(mapName);

            int totalVotes = Nominations.Values
                .SelectMany(v => v)
                .Count(m => m.Equals(mapName, StringComparison.OrdinalIgnoreCase));

            Server.PrintToChatAll(_localizer.LocalizeWithPrefix("nominate.nominated", player.PlayerName, mapName, totalVotes));
        }

        private void ShowMultipleMatchesMenu(CCSPlayerController player, List<string> matchingMaps)
        {
            var menu = new ChatMenu(_localizer.Localize("nominate.multiple-maps"), _plugin!);

            foreach (var name in matchingMaps)
            {
                menu.AddItem(name, (p, _) => Nominate(p, name));
            }

            menu.Display(player, 0);
        }

        // Attempt to resolve the user's text into exactly one map. If 0 matches -> send "invalid" and return null.
        // If > 1 matches -> show the chat based menu and return null. Otherwise -> return the single map name.
        private string? ResolveMapNameOrPrompt(CCSPlayerController player, string input, StringLocalizer localizer)
        {
            // Exact match
            var exact = _mapLister.GetExactMapName(input);
            if (exact is not null)
                return exact;

            // Find all "contains" matches
            var matches = _mapLister.GetMatchingMapNames(input);

            // No matches found
            if (matches.Count == 0)
            {
                player.PrintToChat(localizer.LocalizeWithPrefix("general.invalid-map"));
                return null;
            }
            // Found more than 1 match
            if (matches.Count > 1)
            {
                ShowMultipleMatchesMenu(player, matches);
                return null;
            }

            // Exactly one
            return matches[0];
        }

        private string GetBaseMapName(string displayName)
        {
            var idx = displayName.IndexOf(" (", StringComparison.Ordinal);
            return idx >= 0
                ? displayName.Substring(0, idx)
                : displayName;
        }

        public List<string> NominationWinners()
        {
            if (Nominations.Count == 0)
                return new List<string>();

            var rawNominations = Nominations
                .Select(x => x.Value)
                .Aggregate((acc, x) => acc.Concat(x).ToList());

            return [.. rawNominations
                .Distinct()
                .Select(map => new KeyValuePair<string, int>(map, rawNominations.Count(x => x == map)))
                .OrderByDescending(x => x.Value)
                .Select(x => x.Key)];
        }

        public void PlayerDisconnected(CCSPlayerController player)
        {
            int userId = player.UserId!.Value;
            Nominations.Remove(userId);
        }
    }
}