using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using static CounterStrikeSharp.API.Core.Listeners;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using CS2MenuManager.API.Class;
using CounterStrikeSharp.API.Modules.Events;

namespace cs2_rockthevote
{
    public class PluginDependencyInjection : IPluginServiceCollection<Plugin>
    {
        public void ConfigureServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddLogging();
            var di = new DependencyManager<Plugin, Config>();
            di.LoadDependencies(typeof(Plugin).Assembly);
            di.AddIt(serviceCollection);
            serviceCollection.AddScoped<StringLocalizer>();
        }
    }

    public partial class Plugin(DependencyManager<Plugin, Config> dependencyManager,
        NominationCommand nominationManager,
        ChangeMapManager changeMapManager,
        VotemapCommand voteMapManager,
        RockTheVoteCommand rtvManager,
        ExtendRoundTimeCommand extendRoundTime,
        VoteExtendRoundTimeCommand voteExtendRoundTime,
        TimeLeftCommand timeLeft,
        MaplistCommand maplistManager,
        AFKManager afkManager,
        PluginState pluginState,
        IStringLocalizer stringLocalizer,
        ILogger<Plugin> logger) : BasePlugin, IPluginConfig<Config>
    {
        public override string ModuleName => "RockTheVote";
        public override string ModuleVersion => "2.1.2";
        public override string ModuleAuthor => "abnerfs (Updated by Marchand)";

        private readonly DependencyManager<Plugin, Config> _dependencyManager = dependencyManager;
        private readonly NominationCommand _nominationManager = nominationManager;
        private readonly ChangeMapManager _changeMapManager = changeMapManager;
        private readonly VotemapCommand _votemapManager = voteMapManager;
        private readonly RockTheVoteCommand _rtvManager = rtvManager;
        private readonly AFKManager _afkManager = afkManager;
        private readonly ExtendRoundTimeCommand _extendRoundTime = extendRoundTime;
        private readonly VoteExtendRoundTimeCommand _voteExtendRoundTime = voteExtendRoundTime;
        private readonly TimeLeftCommand _timeLeft = timeLeft;
        private readonly MaplistCommand _maplistManager = maplistManager;
        private readonly StringLocalizer _localizer = new(stringLocalizer, "rtv.prefix");
        private readonly ILogger<Plugin> _logger = logger;
        private readonly PluginState _pluginState = pluginState;
        private bool _hasMenuManager = false;


        public Config Config { get; set; } = new Config();

        public string Localize(string prefix, string key, params object[] values)
        {
            return $"{Localizer[prefix]} {Localizer[key, values]}";
        }

        public override void Load(bool hotReload)
        {
            _dependencyManager.OnPluginLoad(this);
            RegisterListener<OnMapStart>(_dependencyManager.OnMapStart);

            RegisterEventHandler<EventVoteCast>((ev, info) =>
            {
                PanoramaVote.VoteCast(ev);
                return HookResult.Continue;
            });
        }
        
        public override void OnAllPluginsLoaded(bool hotReload)
        {
            // Check for CS2MenuManager installation
            try
            {
                var dummy = MenuManager.MenuTypesList;
                _hasMenuManager = true;
            }
            catch (Exception)
            {
                _hasMenuManager = false;
                Server.PrintToConsole("CS2MenuManager API not found! It is required to use RockTheVote. Download it from here: https://github.com/schwarper/CS2MenuManager");
                Logger.LogWarning("CS2MenuManager API not found! It is required to use RockTheVote. Download it from here: https://github.com/schwarper/CS2MenuManager");
            }

            if (_hasMenuManager)
                 Server.PrintToConsole("CS2MenuManager API detected; RTV menu features enabled!");
        }
        
        public override void Unload(bool hotReload)
        {
            RemoveListener<OnMapStart>(_dependencyManager.OnMapStart);
        }

        public void OnConfigParsed(Config config)
        {
            Config = config;
            _dependencyManager.OnConfigParsed(config);

            if (config.Version < Config.Version)
                Logger.LogWarning("Configuration version mismatch (Expected: {ExpectedVersion} | Current: {CurrentVersion})", Config.Version, config.Version);
        }

        /*
        [GameEventHandler]
        public HookResult OnClientSay(EventPlayerChat @event, GameEventInfo info)
        {
            var player = Utilities.GetPlayerFromUserid(@event.Userid);

            if (player == null || !player.IsValid || player.IsBot || string.IsNullOrEmpty(@event.Text))
                return HookResult.Continue;

            string message = @event.Text.Trim();

            if (message.StartsWith("!") && message.Length == 2 && char.IsDigit(message[1]))
            {
                int key = message[1] - '0';

                var menu = CS2MenuManager.API.Class.MenuManager.GetActiveMenu(player);
                if (menu != null && _pluginState.EofVoteHappening && Config.ScreenMenu.EnableChatHelper == true)
                {
                    CS2MenuManager.API.Class.MenuManager.OnKeyPress(player, key);
                    return HookResult.Handled;
                }
            }

            return HookResult.Continue;
        }
        */

        [ConsoleCommand("css_reloadrtv", "Reloads the RTV config.")]
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        public void ReloadCommand(CCSPlayerController? player, CommandInfo command)
        {
            string permission = "@css/root";

            if (player != null && !AdminManager.PlayerHasPermissions(player, permission))
            {
                command?.ReplyToCommand($"[RTV] {ChatColors.Red}You do not have the correct permission to execute this command.");
                return;
            }
            
            try
            {
                Config.Reload();
                command.ReplyToCommand($"[RTV] {ChatColors.Lime}Configuration reloaded successfully!");
            }
            catch (Exception ex)
            {
                command.ReplyToCommand($"Failed to reload configuration: {ex.Message}");
            }
        }
    }
}