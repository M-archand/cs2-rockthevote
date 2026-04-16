using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using cs2_rockthevote.Core;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace cs2_rockthevote
{
    public partial class Plugin
    {
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        public void OnVoteExtendRoundTimeCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || command == null)
                return;

            // If "Permission" is blank or whitespace, allow everyone. Otherwise enforce it
            string perm = Config.VoteExtend.Permission;
            bool hasPerm = string.IsNullOrWhiteSpace(perm) || AdminManager.PlayerHasPermissions(player, perm);

            if (!hasPerm)
            {
                command.ReplyToCommand(_localizer.LocalizeWithPrefix("general.incorrect.permission"));
                return;
            }

            _voteExtendRoundTime.CommandHandler(player, command);
        }

        public HookResult EventPlayerDisconnectExtend(EventPlayerDisconnect @event, GameEventInfo @eventInfo)
        {
            var player = @event.Userid;
            if (player != null)
            {
                _voteExtendRoundTime.PlayerDisconnected(player);
            }
            return HookResult.Continue;
        }
    }

    public class VoteExtendRoundTimeCommand(TimeLimitManager timeLimitManager, ExtendRoundTimeManager extendRoundTimeManager, GameRules gameRules, IStringLocalizer stringLocalizer, PluginState pluginState, ILogger<VoteExtendRoundTimeCommand> logger) : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<VoteExtendRoundTimeCommand> _logger = logger;
        private TimeLimitManager _timeLimitManager = timeLimitManager;
        private ExtendRoundTimeManager _extendRoundTimeManager = extendRoundTimeManager;
        private readonly GameRules _gameRules = gameRules;
        private StringLocalizer _localizer = new(stringLocalizer, "extendtime.prefix");
        private PluginState _pluginState = pluginState;
        private VoteExtendConfig _voteExtendConfig = new();
        private GeneralConfig _generalConfig = new();
        private CCSPlayerController? _initiatingPlayer;
        private DateTime _cooldownEndTime;
        private Plugin? _plugin;
        private Timer? _cooldownTimer;
        private bool _isCooldownActive = false;

        public void CommandHandler(CCSPlayerController player, CommandInfo commandInfo)
        {
            _initiatingPlayer = player;

            if (!_voteExtendConfig.Enabled)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("extendtime.disbled"));
                return;
            }
            if (_generalConfig.MaxMapExtensions > 0 && _pluginState.MapExtensionCount >= _generalConfig.MaxMapExtensions)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("extendtime.max-extensions-reached", _generalConfig.MaxMapExtensions));
                return;
            }
            if (_isCooldownActive)
            {
                double secondsLeft = Math.Max(0, (_cooldownEndTime - DateTime.UtcNow).TotalSeconds);
                int secondsInt = (int)Math.Ceiling(secondsLeft);

                player.PrintToChat(_localizer.LocalizeWithPrefix("rtv.cooldown", secondsInt));
                return;
            }
            if (_pluginState.DisableCommands || !_voteExtendConfig.Enabled)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.disabled"));
                return;
            }
            if (_gameRules.WarmupRunning)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.validation.warmup"));
                return;
            }
            if (_pluginState.RtvVoteHappening || _pluginState.ExtendTimeVoteHappening)
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("general.vote-in-progress"));
                return;
            }
            if (!_timeLimitManager.UnlimitedTime)
            {
                if (!_voteExtendConfig.EnablePanorama)
                {
                    _extendRoundTimeManager.StartExtendVote(_voteExtendConfig);
                }
                else if (_voteExtendConfig.EnablePanorama)
                {
                    PanoramaVote.Init();
                    Server.ExecuteCommand("sv_allow_votes 1");
                    Server.ExecuteCommand("sv_vote_allow_in_warmup 1");
                    Server.ExecuteCommand("sv_vote_allow_spectators 1");
                    Server.ExecuteCommand("sv_vote_count_spectator_votes 1");
                    _pluginState.ExtendTimeVoteHappening = true;
                    if (_voteExtendConfig.EnableCountdown && _voteExtendConfig.CountdownType == "chat")
                    {
                        _extendRoundTimeManager.ChatCountdown(_voteExtendConfig.VoteDuration);
                    }
                    _extendRoundTimeManager.VoteCountdown();

                    PanoramaVote.SendYesNoVoteToAll(
                        _voteExtendConfig.VoteDuration,
                        player.Slot, // player.Slot Header = Vote by: playerName. VoteConstants.VOTE_CALLER_SERVER Header = Vote by: Server
                        "#SFUI_vote_passed_nextlevel_extend",
                        _localizer.Localize("extendtime.ui-question", _generalConfig.RoundTimeExtension),
                        VoteResultCallback,
                        VoteHandlerCallback
                    );
                }
                else
                {
                    player.PrintToChat(_localizer.LocalizeWithPrefix("extendtime.notapplicable"));
                }
            }
            else
            {
                player.PrintToChat(_localizer.LocalizeWithPrefix("extendtime.notapplicable"));
            }
        }

        private bool VoteResultCallback(YesNoVoteInfo info)
        {
            int requiredYesVotes = (int)Math.Ceiling(info.num_clients * (_voteExtendConfig.VotePercentage / 100.0));
            _pluginState.ExtendTimeVoteHappening = false;
            _extendRoundTimeManager.KillTimer();
            ActivateCooldown();

            Server.ExecuteCommand("sv_allow_votes 0");
            Server.ExecuteCommand("sv_vote_allow_in_warmup 0");
            Server.ExecuteCommand("sv_vote_allow_spectators 0");
            Server.ExecuteCommand("sv_vote_count_spectator_votes 0");

            if (info.yes_votes >= requiredYesVotes)
            {
                Server.PrintToChatAll($"{_localizer.LocalizeWithPrefix("extendtime.vote-ended.passed2", _generalConfig.RoundTimeExtension)}");
                _extendRoundTimeManager.ExtendRoundTime(_generalConfig.RoundTimeExtension);
                _pluginState.MapExtensionCount++;
                return true;
            }
            else
                return false;
        }

        private void VoteHandlerCallback(YesNoVoteAction action, int param1, int param2)
        {
            switch (action)
            {
                case YesNoVoteAction.VoteAction_Start:
                    Server.PrintToChatAll($"{_localizer.LocalizeWithPrefix("extendtime.vote-started", _initiatingPlayer!.PlayerName)}");
                    break;

                case YesNoVoteAction.VoteAction_Vote:
                    try
                    {
                        var vc = PanoramaVote.VoteController;
                        if (vc != null)
                        {
                            if (!vc.IsValid)
                                return;

                            int potentialVotes = vc.PotentialVotes;

                            if (vc.VoteOptionCount.Length <= (int)CastVote.VOTE_OPTION2)
                                return;

                            int yesVotes = vc.VoteOptionCount[(int)CastVote.VOTE_OPTION1];
                            int noVotes = vc.VoteOptionCount[(int)CastVote.VOTE_OPTION2];
                            int requiredYesVotes = (int)Math.Ceiling(potentialVotes * (_voteExtendConfig.VotePercentage / 100.0));

                            // Early cancel if the vote can no longer pass
                            if ((potentialVotes - noVotes) < requiredYesVotes)
                            {
                                Server.NextFrame(() =>
                                {
                                    try
                                    {
                                        PanoramaVote.EndVote(YesNoVoteEndReason.VoteEnd_Cancelled, overrideFailCode: 0);
                                        _pluginState.ExtendTimeVoteHappening = false;
                                        ActivateCooldown();
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError($"Error during vote cancellation: {ex.Message}");
                                    }
                                });
                                return;
                            }

                            // Early pass if enough yes votes are already in
                            if (yesVotes >= requiredYesVotes)
                            {
                                Server.NextFrame(() =>
                                {
                                    try
                                    {
                                        PanoramaVote.EndVote(YesNoVoteEndReason.VoteEnd_AllVotes);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError($"Error during early vote pass: {ex.Message}");
                                    }
                                });
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing vote: {Message}", ex.Message);
                    }
                    break;

                case YesNoVoteAction.VoteAction_End:
                    if ((YesNoVoteEndReason)param1 == YesNoVoteEndReason.VoteEnd_Cancelled)
                    {
                        Server.PrintToChatAll($"{_localizer.LocalizeWithPrefix("extendtime.vote-ended.failed2")}");
                    }
                    else if ((YesNoVoteEndReason)param1 == YesNoVoteEndReason.VoteEnd_TimeUp)
                    {
                        Server.PrintToChatAll($"{_localizer.LocalizeWithPrefix("extendtime.vote-ended-no-votes")}");
                    }
                    break;
            }
        }

        private void ActivateCooldown()
        {
            _isCooldownActive = true;

            _cooldownTimer = _plugin?.AddTimer(_voteExtendConfig.CooldownDuration, () =>
            {
                _isCooldownActive = false;
            }, TimerFlags.STOP_ON_MAPCHANGE
            );

            _cooldownEndTime = DateTime.UtcNow.AddSeconds(_voteExtendConfig.CooldownDuration);
        }

        public void KillTimer()
        {
            _isCooldownActive = false;
            _cooldownEndTime = DateTime.MinValue;
            _cooldownTimer?.Kill();
            _cooldownTimer = null;
        }

        public void PlayerDisconnected(CCSPlayerController? player)
        {
            if (player?.UserId == null)
                return;

            if (!_voteExtendConfig.EnablePanorama)
                _extendRoundTimeManager?.RemoveVote(player.UserId.Value);
            else
                PanoramaVote.RemovePlayerFromVote(player.Slot);
        }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
        }

        public void OnConfigParsed(Config config)
        {
            _voteExtendConfig = config.VoteExtend;
            _generalConfig = config.General;
        }
        
        public void OnMapStart(string map)
        {
            KillTimer();
        }
    }
}
