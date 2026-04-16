using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Commands;
using CS2MenuManager.API.Enum;
using CS2MenuManager.API.Class; 
using cs2_rockthevote.Core;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    public partial class Plugin
    {
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        public void OnVotemap(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || command == null)
                return;

            // If "Permission" is blank or whitespace, allow everyone. Otherwise enforce it
            string perm = Config.Votemap.Permission;
            bool hasPerm = string.IsNullOrWhiteSpace(perm) || AdminManager.PlayerHasPermissions(player, perm);

            if (!hasPerm)
            {
                command.ReplyToCommand(_localizer.LocalizeWithPrefix("general.incorrect.permission"));
                return;
            }
            
            string map = command.GetArg(1).Trim().ToLower();
            _votemapManager.CommandHandler(player, map);
        }

        public HookResult EventPlayerDisconnectVotemap(EventPlayerDisconnect @event, GameEventInfo @eventInfo)
        {
            var player = @event.Userid;
            if (player != null)
            {
                _votemapManager.PlayerDisconnected(player);
            }
            return HookResult.Continue;
        }
    }

    public class VotemapCommand : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<VotemapCommand> _logger;
        private StringLocalizer _localizer;
        private VotemapConfig _config = new();
        private EndOfMapVote _endOfMapVote;
        private GameRules _gamerules;
        private ChangeMapManager _changeMapManager;
        private PluginState _pluginState;
        private MapCooldown _mapCooldown;
        private MapLister _mapLister;
        private Plugin? _plugin;
        private Dictionary<string, AsyncVoteManager> VotedMaps = new Dictionary<string, AsyncVoteManager>();

        public VotemapCommand(MapLister mapLister, GameRules gamerules, IStringLocalizer stringLocalizer, ChangeMapManager changeMapManager, PluginState pluginState, MapCooldown mapCooldown, EndOfMapVote endOfMapVote, ILogger<VotemapCommand> logger)
        {
            _mapLister = mapLister;
            _gamerules = gamerules;
            _localizer = new StringLocalizer(stringLocalizer, "votemap.prefix");
            _changeMapManager = changeMapManager;
            _pluginState = pluginState;
            _mapCooldown = mapCooldown;
            _endOfMapVote = endOfMapVote;
            _logger = logger;
        }

        public void OnMapStart(string map)
        {
            VotedMaps.Clear();
        }

        public void OnConfigParsed(Config config)
        {
            _config = config.Votemap;
        }

        public void PlayerDisconnected(CCSPlayerController player)
        {
            int userId = player.UserId!.Value;
            foreach (var map in VotedMaps)
                map.Value.RemoveVote(userId);
        }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
        }

        public void CommandHandler(CCSPlayerController? player, string map)
        {
            if (player is null)
                return;

            if (_pluginState.DisableCommands || !_config.Enabled)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.disabled"));
                return;
            }

            if (_gamerules.WarmupRunning)
            {
                if (!_config.EnabledInWarmup)
                {
                    player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.warmup"));
                    return;
                }
            }
            else if (_config.MinRounds > 0 && _config.MinRounds > _gamerules.TotalRoundsPlayed)
            {
                player!.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.minimum-rounds", _config.MinRounds));
                return;
            }

            if (ServerManager.ValidPlayerCount() < _config!.MinPlayers)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.minimum-players", _config!.MinPlayers));
                return;
            }

            map = map.ToLower().Trim();

            if (string.IsNullOrEmpty(map))
            {
                var title = _localizer.Localize("general.choose.map");

                // Resolve the menu type from config, fallback to default if necessary
                var key = _config.MenuType?.Trim() ?? "";
                var menuType = MenuManager.MenuTypesList.TryGetValue(key, out var resolvedType)
                    ? resolvedType
                    : MenuTypeManager.GetDefaultMenu();

                var menu = MenuManager.MenuByType(menuType, title, _plugin!);

                foreach (var m in _mapLister.Maps!.Where(x => x.Name != Server.MapName))
                {
                    bool isCooldown = _mapCooldown.IsMapInCooldown(m.Name);
                    string label = isCooldown ? $"{ChatColors.Grey}{m.Name}" : m.Name;
                    string chosen = m.Name;

                    menu.AddItem(label, (p, _) =>
                    {
                        if (isCooldown)
                        {
                            p.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.map-played-recently"));
                            return;
                        }

                        AddVote(p, chosen);
                    }, isCooldown ? DisableOption.DisableShowNumber : DisableOption.None);
                }

                menu.Display(player, 0);
                return;
            }

            var exact = _mapLister.GetExactMapName(map);
            if (exact is null)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.invalid-map"));
                return;
            }
            if (_mapCooldown.IsMapInCooldown(exact))
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.map-played-recently"));
                return;
            }

            AddVote(player, exact);
        }

        public void AddVote(CCSPlayerController player, string map)
        {
            if (map == Server.MapName)
            {
                player!.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.current-map"));
                return;
            }

            if (_mapCooldown.IsMapInCooldown(map))
            {
                player!.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.map-played-recently"));
                return;
            }

            if (!_mapLister.Maps!.Any(x => x.Name.Equals(map, StringComparison.OrdinalIgnoreCase)))
            {
                player!.PrintToChat(_localizer.LocalizeWithPrefix("general.invalid-map"));
                return;
            }

            var userId = player.UserId!.Value;
            if (!VotedMaps.ContainsKey(map))
                VotedMaps.Add(map, new AsyncVoteManager(_config.VotePercentage));

            var voteManager = VotedMaps[map];
            VoteResult result = voteManager.AddVote(userId);
            switch (result.Result)
            {
                case VoteResultEnum.Added:
                    Server.PrintToChatAll($"{_localizer.LocalizeWithPrefix("votemap.player-voted", player.PlayerName, map)} {_localizer.Localize("general.votes-needed", result.VoteCount, result.RequiredVotes)}");
                    break;
                case VoteResultEnum.AlreadyAddedBefore:
                    player.PrintToChat($"{_localizer.LocalizeWithPrefix("votemap.already-voted", map)} {_localizer.Localize("general.votes-needed", result.VoteCount, result.RequiredVotes)}");
                    break;
                case VoteResultEnum.VotesAlreadyReached:
                    player.PrintToChat(_localizer.LocalizeWithPrefix("votemap.disabled"));
                    break;
                case VoteResultEnum.VotesReached:
                    Server.PrintToChatAll($"{_localizer.LocalizeWithPrefix("votemap.player-voted", player.PlayerName, map)} {_localizer.Localize("general.votes-needed", result.VoteCount, result.RequiredVotes)}");
                    _changeMapManager.ScheduleMapChange(map, prefix: "votemap.prefix");
                    if (_config.ChangeMapImmediately)
                        _changeMapManager.ChangeNextMap();
                    else
                        Server.PrintToChatAll(_localizer.LocalizeWithPrefix("general.changing-map-next-round", map));
                    break;
            }
        }
    }
}
