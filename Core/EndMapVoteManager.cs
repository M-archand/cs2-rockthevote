using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using CS2MenuManager.API.Class;
using CS2MenuManager.API.Menu;
using cs2_rockthevote.Core;
using System.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Drawing;

namespace cs2_rockthevote
{
    public class EndMapVoteManager : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<EndMapVoteManager> _logger;
        private ILogger _debugLogger = NullLogger<EndMapVoteManager>.Instance;
        private readonly MapLister _mapLister;
        private readonly ExtendRoundTimeManager _extendRoundTimeManager;
        private readonly TimeLimitManager _timeLimitManager;
        private readonly ChangeMapManager _changeMapManager;
        private readonly NominationCommand _nominationManager;
        private readonly StringLocalizer _localizer;
        private readonly PluginState _pluginState;
        private readonly MapCooldown _mapCooldown;
        private readonly GameRules _gameRules;
        private Timer? Timer;
        private Timer? _nextVoteTimer;
        private Timer? _chatMapChoiceTimer;
        private Timer? _ignoreWinConditionsPollTimer;
        private readonly List<Timer> _hintOuterTimers = new();
        private readonly List<CEnvInstructorHint> _hintEntities = new();
        List<string> mapsElected = new();
        private readonly Dictionary<int, string> _playerVotes = new();
        private readonly HashSet<int> _revoteMenuOpen = new();
        private readonly List<string> _currentVoteOptions = new();
        private int _canVote = 0;
        private bool _activeVoteIsRtv = false;
        private Plugin? _plugin;

        public int TimeLeft { get; private set; } = -1;
        public int MaxOptionsHud { get; private set; } = 6;
        public ISet<int> VotedPlayers { get; private set; } = new HashSet<int>();
        public Dictionary<string,int> Votes { get; private set; } = new();
        public IReadOnlyDictionary<string, int> CurrentVotes => Votes;

        private List<KeyValuePair<string, int>> _sortedTopVotes = new();
        public IReadOnlyList<KeyValuePair<string, int>> SortedTopVotes => _sortedTopVotes;

        private void RebuildSortedTopVotes()
        {
            _sortedTopVotes = Votes
                .OrderByDescending(x => x.Value)
                .Take(MaxOptionsHud)
                .ToList();
        }

        private GeneralConfig _generalConfig = new();
        private EndOfMapConfig _endMapConfig = new();
        private RtvConfig _rtvConfig = new();

        public EndMapVoteManager
        (
            MapLister mapLister,
            ChangeMapManager changeMapManager,
            NominationCommand nominationManager,
            StringLocalizer localizer,
            PluginState pluginState,
            MapCooldown mapCooldown,
            ExtendRoundTimeManager extendRoundTimeManager,
            TimeLimitManager timeLimitManager,
            GameRules gameRules,
            ILogger<EndMapVoteManager> logger
        )
        {
            _mapLister = mapLister;
            _changeMapManager = changeMapManager;
            _nominationManager = nominationManager;
            _localizer = localizer;
            _pluginState = pluginState;
            _mapCooldown = mapCooldown;
            _extendRoundTimeManager = extendRoundTimeManager;
            _timeLimitManager = timeLimitManager;
            _gameRules = gameRules;
            _logger = logger;
        }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
            _plugin.AddCommand("revote", "Re-open the active map vote menu.", OnRevoteCommand);
        }

        public void OnConfigParsed(Config config)
        {
            _generalConfig = config.General;
            _endMapConfig = config.EndOfMapVote;
            _rtvConfig = config.Rtv;
            _debugLogger = _generalConfig.DebugLogging ? _logger : NullLogger<EndMapVoteManager>.Instance;
            //_screenConfig = config.ScreenMenu;

            if (!uint.TryParse(_endMapConfig.SoundPath, out _) && !SoundEventHelper.IsFullVolume(_endMapConfig.SoundVolume))
            {
                _logger.LogWarning("EndOfMapVote: To modify the sound volume (any value aside from 1) you need to use the soundevent_hash rather than the sound path");
            }

            // Check to make sure VoteDuration isn't >= TriggerSecondsBeforeEnd, if it is, use a fallback
            if (_endMapConfig.VoteDuration >= _endMapConfig.TriggerSecondsBeforeEnd)
            {
                var original = _endMapConfig.VoteDuration;
                var adjusted = Math.Max(1, _endMapConfig.TriggerSecondsBeforeEnd - 5);
                _endMapConfig.VoteDuration = adjusted;

                _logger.LogError(
                    $"EndOfMapVote config invalid: VoteDuration ({_endMapConfig.VoteDuration}s) must be less than " +
                    $"TriggerSecondsBeforeEnd ({_endMapConfig.TriggerSecondsBeforeEnd}s). Automatically adjusting VoteDuration to {adjusted}s.",
                    original,
                    config.EndOfMapVote.TriggerSecondsBeforeEnd,
                    adjusted
                );
            }
        }

        public void OnMapStart(string map)
        {
            Votes.Clear();
            _playerVotes.Clear();
            _revoteMenuOpen.Clear();
            _currentVoteOptions.Clear();
            _sortedTopVotes = new();
            TimeLeft = 0;
            mapsElected.Clear();
            KillTimer();
            KillNextVoteTimer();
            KillIgnoreWinConditionsPollTimer();
            KillHintEntities();
        }

        public void Unload(Plugin plugin)
        {
            CloseAllActiveMenus();
            _revoteMenuOpen.Clear();
            KillTimer();
            KillNextVoteTimer();
            KillChatMapChoiceTimer();
            KillIgnoreWinConditionsPollTimer();
            KillHintEntities();
        }

        private void KillIgnoreWinConditionsPollTimer()
        {
            _ignoreWinConditionsPollTimer?.Kill();
            _ignoreWinConditionsPollTimer = null;
        }

        private void KillNextVoteTimer()
        {
            _nextVoteTimer?.Kill();
            _nextVoteTimer = null;
        }

        private void KillChatMapChoiceTimer()
        {
            _chatMapChoiceTimer?.Kill();
            _chatMapChoiceTimer = null;
        }

        private void KillHintEntities()
        {
            bool hadHints = _hintOuterTimers.Count > 0 || _hintEntities.Count > 0;

            foreach (var t in _hintOuterTimers)
            {
                try { t.Kill(); }
                catch (Exception ex) { _debugLogger.LogError(ex, "[Hint] Failed to kill outer hint timer"); }
            }
            _hintOuterTimers.Clear();

            foreach (var entity in _hintEntities)
            {
                try
                {
                    if (entity.IsValid)
                        entity.AcceptInput("Kill");
                }
                catch (Exception ex) { _debugLogger.LogError(ex, "[Hint] Failed to kill hint entity"); }
            }
            _hintEntities.Clear();

            // Restore instructor convar state directly since killing the timers above skips their cleanup
            if (hadHints)
            {
                Server.ExecuteCommand("sv_gameinstructor_disable true");
                foreach (var snapshot in ServerManager.ValidPlayers())
                {
                    int slot = snapshot.Slot;
                    try
                    {
                        var live = Utilities.GetPlayerFromSlot(slot);
                        if (live is not null && live.ReallyValid())
                            live.ReplicateConVar("sv_gameinstructor_enable", "false");
                    }
                    catch (Exception ex) { _debugLogger.LogError(ex, "[Hint] Failed to restore instructor convar. slot={Slot}", slot); }
                }
            }
        }

        private bool ShouldPrintChatMapChoices()
        {
            return string.Equals(_endMapConfig.MenuType?.Trim(), "ChatMenu", StringComparison.OrdinalIgnoreCase)
                && _endMapConfig.ChatMapChoiceReminder
                && _endMapConfig.ChatMapChoiceInterval > 0;
        }

        private void PrintChatMapChoices()
        {
            if (_currentVoteOptions.Count == 0)
                return;

            Server.PrintToChatAll(_localizer.Localize("emv.hud.menu-title"));

            for (int i = 0; i < _currentVoteOptions.Count; i++)
            {
                string option = _currentVoteOptions[i];
                int voteCount = Votes.TryGetValue(option, out int currentVotes) ? currentVotes : 0;
                Server.PrintToChatAll($" {ChatColors.Lime}!{i + 1} {ChatColors.Default}- {ChatColors.Yellow}({ChatColors.Orange}{voteCount}{ChatColors.Yellow}) {ChatColors.Default}- {option}");
            }

            if (_endMapConfig.EnableRevote)
                Server.PrintToChatAll(_localizer.Localize("emv.revote"));
        }

        private void StartChatMapChoiceReminder()
        {
            KillChatMapChoiceTimer();

            if (_plugin == null || !ShouldPrintChatMapChoices())
                return;

            _chatMapChoiceTimer = _plugin.AddTimer(_endMapConfig.ChatMapChoiceInterval, () =>
            {
                if (!_pluginState.EofVoteHappening || TimeLeft <= 0 || !ShouldPrintChatMapChoices())
                {
                    KillChatMapChoiceTimer();
                    return;
                }

                PrintChatMapChoices();
            }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }

        public void ScheduleNextVote()
        {
            KillNextVoteTimer();

            _nextVoteTimer = _plugin?.AddTimer(1.0F, () =>
            {
                if (_timeLimitManager.UnlimitedTime)
                    return;

                float timeRemainingSeconds = (float)Math.Max(0, (double)_timeLimitManager.TimeRemaining);
                if (timeRemainingSeconds <= _endMapConfig.TriggerSecondsBeforeEnd)
                {
                    KillNextVoteTimer();
                    _pluginState.EofVoteHappening = false;
                    _changeMapManager.OnMapStart(Server.MapName);
                    StartVote(isRtv: false);
                }
            }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }

        private void StartIgnoreWinConditionsPoll(string winnerMapName)
        {
            KillIgnoreWinConditionsPollTimer();

            _debugLogger.LogInformation(
                "[RTV.MapChange] Arming plugin-owned end-of-map poll (deferring creation to next frame). map={Map} currentMap={CurrentMap}",
                winnerMapName,
                Server.MapName
            );

            Server.NextFrame(() =>
            {
                _debugLogger.LogInformation("[RTV.MapChange] NextFrame: creating poll timer now. map={Map}", winnerMapName);
                CreateIgnoreWinConditionsPollTimer(winnerMapName);
            });
        }

        private void CreateIgnoreWinConditionsPollTimer(string winnerMapName)
        {
            _ignoreWinConditionsPollTimer = _plugin?.AddTimer(1.0F, () =>
            {
                try { _debugLogger.LogInformation("[RTV.MapChange] Poll tick entry. map={Map}", winnerMapName); }
                catch { }

                try
                {
                    if (!_pluginState.MapChangeScheduled)
                    {
                        _debugLogger.LogInformation("[RTV.MapChange] Poll tick: change no longer scheduled, stopping. map={Map}", winnerMapName);
                        KillIgnoreWinConditionsPollTimer();
                        return;
                    }

                    _debugLogger.LogInformation("[RTV.MapChange] Poll tick: computing remaining seconds. map={Map}", winnerMapName);
                    int remainingSeconds = ComputeRemainingSecondsForIgnoreWinConditions();
                    _debugLogger.LogInformation(
                        "[RTV.MapChange] Poll tick: computed. remainingSeconds={Remaining} map={Map}",
                        remainingSeconds,
                        winnerMapName
                    );

                    if (remainingSeconds <= 3)
                    {
                        _debugLogger.LogInformation(
                            "[RTV.MapChange] Plugin-owned trigger fired. remainingSeconds={Remaining} map={Map}",
                            remainingSeconds,
                            winnerMapName
                        );
                        KillIgnoreWinConditionsPollTimer();
                        _changeMapManager.ChangeNextMap(MapChangeTrigger.IgnoredWinConditions);
                    }
                }
                catch (Exception ex)
                {
                    _debugLogger.LogError(ex, "[RTV.MapChange] Plugin-owned poll callback failed. map={Map}", winnerMapName);
                }
            }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }

        private int ComputeRemainingSecondsForIgnoreWinConditions()
        {
            if (!_timeLimitManager.UnlimitedTime)
            {
                float remaining = (float)Math.Max(0, (double)_timeLimitManager.TimeRemaining);
                return (int)Math.Floor(remaining);
            }

            float roundTime = _gameRules.RoundTime;
            float gameStartTime = _gameRules.GameStartTime;
            float elapsed = Server.CurrentTime - gameStartTime;
            return (int)Math.Floor(roundTime - elapsed);
        }

        private void OnRevoteCommand(CCSPlayerController? player, CommandInfo _)
        {
            if (player == null || !player.IsValid)
                return;

            if (!_pluginState.EofVoteHappening || Timer is null || _currentVoteOptions.Count == 0)
                return;

            if (!_endMapConfig.EnableRevote)
                return;

            // Already showing a revote menu for this player - their vote is active, ignore repeat requests
            if (!_revoteMenuOpen.Add(player.Slot))
                return;

            int menuTimeLeft = Math.Max(TimeLeft, 1);
            DisplayRevoteMenu(player, _currentVoteOptions, menuTimeLeft, _activeVoteIsRtv);
        }

        // Revote always uses a frozen WASD menu with no exit button, forcing the player to make a selection
        private void DisplayRevoteMenu(CCSPlayerController player, IEnumerable<string> voteOptions, int durationSeconds, bool isRtv)
        {
            if (_plugin == null || !player.IsValid)
                return;

            var title = _localizer.Localize("emv.hud.menu-title");
            var menu = new WasdMenu(title, _plugin)
            {
                ExitButton = false,
                WasdMenu_FreezePlayer = true
            };

            foreach (var option in voteOptions)
            {
                var chosen = option;
                menu.AddItem(chosen, (p, _) =>
                {
                    _revoteMenuOpen.Remove(p.Slot);

                    MapVoted(p, chosen, isRtv, allowRevote: true);
                });
            }

            menu.Display(player, durationSeconds);
        }

        private void DisplayVoteMenu(CCSPlayerController player, IEnumerable<string> voteOptions, int durationSeconds, bool isRtv, bool allowRevote)
        {
            if (_plugin == null || !player.IsValid)
                return;

            var title = _localizer.Localize("emv.hud.menu-title");
            var key = _endMapConfig.MenuType?.Trim() ?? "";
            var menuType = MenuManager.MenuTypesList.TryGetValue(key, out var resolvedType)
                ? resolvedType
                : MenuTypeManager.GetDefaultMenu();

            var menu = MenuManager.MenuByType(menuType, title, _plugin);
            if (menu is ChatMenu)
                menu.ExitButton = false;

            foreach (var option in voteOptions)
            {
                var chosen = option;
                menu.AddItem(chosen, (p, _) =>
                {
                    MapVoted(p, chosen, isRtv, allowRevote);
                });
            }

            menu.Display(player, durationSeconds);
        }

        public void MapVoted(CCSPlayerController player, string mapName, bool isRtv, bool allowRevote = false)
        {
            if (!player.IsValid)
                return;

            if (!_pluginState.EofVoteHappening || Timer is null)
                return;

            if (!Votes.ContainsKey(mapName))
                return;

            var slot = player.Slot;
            bool canRevote = _endMapConfig.EnableRevote && allowRevote;

            if (_playerVotes.TryGetValue(slot, out var previousMap))
            {
                if (!canRevote)
                    return;

                if (string.Equals(previousMap, mapName, StringComparison.Ordinal))
                    return;

                if (Votes.TryGetValue(previousMap, out int previousVotes) && previousVotes > 0)
                {
                    Votes[previousMap] = previousVotes - 1;
                }
            }
            else
            {
                VotedPlayers.Add(slot);
            }

            _playerVotes[slot] = mapName;
            Votes[mapName] += 1;
            RebuildSortedTopVotes();
            player.PrintToChat(_localizer.LocalizeWithPrefix("emv.you-voted", mapName));
            if (_endMapConfig.EnableRevote)
                player.PrintToChat(_localizer.LocalizeWithPrefix("emv.revote"));

            // Keep the vote open for the full timer when revotes are enabled.
            if (!_endMapConfig.EnableRevote && Votes.Values.Sum() >= _canVote)
                EndVote(isRtv);
        }

        public void KillTimer()
        {
            TimeLeft = -1;
            KillChatMapChoiceTimer();
            KillHintEntities();
            if (Timer is not null)
            {
                Timer!.Kill();
                Timer = null;
            }
        }

        public static IList<T> Shuffle<T>(Random rng, IList<T> array)
        {
            int n = array.Count;
            while (n > 1)
            {
                int k = rng.Next(n--);
                (array[k], array[n]) = (array[n], array[k]);
            }
            return array;
        }

        public void PrintCenterTextAll(string text)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player.IsValid)
                {
                    player.PrintToCenter(text);
                }
            }
        }

        public void ChatCountdown(int secondsLeft)
        {
            if (!_pluginState.EofVoteHappening || !_endMapConfig.EnableCountdown || _endMapConfig.CountdownType != "chat")
                return;

            string text = _localizer.LocalizeWithPrefix("general.chat-countdown", secondsLeft);
            foreach (var player in ServerManager.ValidPlayers())
                player.PrintToChat(text);

            int next = secondsLeft - _endMapConfig.ChatCountdownInterval;
            if (next > 0)
            {
                _plugin?.AddTimer(
                    _endMapConfig.ChatCountdownInterval, () =>
                    {
                        try
                        {
                            ChatCountdown(next);
                        }
                        catch (Exception ex)
                        {
                            _plugin.Logger.LogError($"ChatCountdown timer callback failed: {ex.Message}");
                        }
                    }, TimerFlags.STOP_ON_MAPCHANGE
                );
            }
        }

        private void DisplayGameHintForAll(IEnumerable<CCSPlayerController> targets, float seconds = 5f)
        {
            Server.ExecuteCommand("sv_gameinstructor_disable false");

            string text = _localizer.Localize("emv.vote-started");

            foreach (var player in targets)
            {
                if (player == null || !player.IsValid) continue;

                int slot = player.Slot;
                player.ReplicateConVar("sv_gameinstructor_enable", "true");

                var outerTimer = new Timer(0.25f, () =>
                {
                    try
                    {
                        var live = Utilities.GetPlayerFromSlot(slot);
                        if (live is null || !live.ReallyValid())
                            return;

                        ShowHudInstructorHint(
                            controller: live,
                            text: text,
                            seconds: seconds,
                            iconOnScreen: "",
                            iconOffScreen: "",
                            bindingCmd: "use_binding",
                            color: Color.FromArgb(255, 255, 0, 0)
                        );
                    }
                    catch (Exception ex)
                    {
                        _debugLogger.LogError(ex, "[Hint] Outer timer ShowHudInstructorHint failed. slot={Slot}", slot);
                    }
                }, TimerFlags.STOP_ON_MAPCHANGE);
                _hintOuterTimers.Add(outerTimer);
            }

            // Capture slots, not raw controllers, so the late ReplicateConVar cleanup re-validates each handle.
            var targetSlots = targets.Where(p => p != null && p.IsValid).Select(p => p.Slot).ToList();
            var cleanupTimer = new Timer(seconds, () =>
            {
                Server.ExecuteCommand("sv_gameinstructor_disable true");
                foreach (var s in targetSlots)
                {
                    try
                    {
                        var live = Utilities.GetPlayerFromSlot(s);
                        if (live is null || !live.ReallyValid())
                            continue;
                        live.ReplicateConVar("sv_gameinstructor_enable", "false");
                    }
                    catch (Exception ex)
                    {
                        _debugLogger.LogError(ex, "[RTV.Hint] Cleanup ReplicateConVar failed. slot={Slot}", s);
                    }
                }
            }, TimerFlags.STOP_ON_MAPCHANGE);
            _hintOuterTimers.Add(cleanupTimer);
        }

        private void ShowHudInstructorHint(CCSPlayerController controller, string text, float seconds, string iconOnScreen, string iconOffScreen, string bindingCmd, Color color, float iconHeightOffset = 0f)
        {
            if (!controller.IsValid)
                return;

            var pawn = controller.PlayerPawn?.Value;
            if (pawn is null || !pawn.IsValid)
                return;

            var hint = Utilities.CreateEntityByName<CEnvInstructorHint>("env_instructor_hint");
            if (hint is null) return;

            hint.Static = true;

            hint.Caption = text;
            hint.Timeout = (int)MathF.Max(0, seconds);
            hint.Icon_Onscreen = iconOnScreen;
            hint.Icon_Offscreen = iconOffScreen;
            hint.Binding = bindingCmd;
            hint.Color = color;

            hint.IconOffset = iconHeightOffset;
            hint.Range = 0f;
            hint.NoOffscreen = false;
            hint.ForceCaption = false;

            hint.DispatchSpawn();
            _hintEntities.Add(hint);

            try
            {
                hint.AcceptInput("ShowHint", pawn, pawn);
            }
            catch (Exception ex)
            {
                _debugLogger.LogError(ex, "[RTV.Hint] AcceptInput ShowHint failed");
                return;
            }

            if (seconds > 0)
                RemoveEntity(hint, seconds + 0.25f);

            int controllerSlot = controller.Slot;
            var convarResetTimer = _plugin?.AddTimer(5f, () =>
            {
                try
                {
                    Server.ExecuteCommand("sv_gameinstructor_disable true");
                    var live = Utilities.GetPlayerFromSlot(controllerSlot);
                    if (live is not null && live.ReallyValid())
                        live.ReplicateConVar("sv_gameinstructor_enable", "false");
                }
                catch (Exception ex)
                {
                    _debugLogger.LogError(ex, "[RTV.Hint] Cleanup timer failed. slot={Slot}", controllerSlot);
                }
            }, TimerFlags.STOP_ON_MAPCHANGE);
            if (convarResetTimer != null)
                _hintOuterTimers.Add(convarResetTimer);
        }

        private void RemoveEntity(CEnvInstructorHint entity, float time = 0.0f)
        {
            if (time == 0.0f)
            {
                if (entity.IsValid)
                {
                    entity.AcceptInput("Kill");
                }
            }
            else if (time > 0.0f)
            {
                var removeTimer = new Timer(time, () =>
                {
                    try
                    {
                        if (entity.IsValid)
                        {
                            entity.AcceptInput("Kill");
                            _hintEntities.Remove(entity);
                        }
                    }
                    catch (Exception ex)
                    {
                        _debugLogger.LogError(ex, "[RTV.Hint] RemoveEntity timer failed");
                    }
                }, TimerFlags.STOP_ON_MAPCHANGE);
                _hintOuterTimers.Add(removeTimer);
            }
        }

        public void StartVote(bool isRtv)
        {
            KillNextVoteTimer();
            if (_pluginState.EofVoteHappening)
                return;

            VotedPlayers.Clear();
            _playerVotes.Clear();
            _revoteMenuOpen.Clear();
            _currentVoteOptions.Clear();
            _activeVoteIsRtv = isRtv;

            if (_rtvConfig.EnablePanorama)
            {
                Server.ExecuteCommand("sv_allow_votes 0");
                Server.ExecuteCommand("sv_vote_allow_in_warmup 0");
                Server.ExecuteCommand("sv_vote_allow_spectators 0");
                Server.ExecuteCommand("sv_vote_count_spectator_votes 0");
            }

            Votes.Clear();
            _pluginState.EofVoteHappening = true;

            int maxExt = _generalConfig.MaxMapExtensions;
            bool unlimited = maxExt <= 0;  // treat 0 or negative as unlimited

            bool canShowExtendOption = !isRtv
                && _endMapConfig.IncludeExtendCurrentMap
                && (unlimited || _pluginState.MapExtensionCount < maxExt);

            int mapsToShow = !isRtv
                ? (_endMapConfig.MapsToShow == 0 ? MaxOptionsHud : _endMapConfig.MapsToShow)
                : (_rtvConfig.MapsToShow == 0 ? MaxOptionsHud : _rtvConfig.MapsToShow);

            // Cap for CenterHtmlMenu (HUD) pages
            if (string.Equals(_endMapConfig.MenuType?.Trim(), "CenterHtmlMenu", StringComparison.Ordinal)
                && mapsToShow > MaxOptionsHud)
            {
                mapsToShow = MaxOptionsHud;
            }

            int mapOptionsCount = canShowExtendOption ? mapsToShow - 1 : mapsToShow;

            // Get map list
            var mapsScrambled = Shuffle(new Random(), _mapLister.Maps!.Select(x => x.Name)
                .Where(x => x != Server.MapName && !_mapCooldown.IsMapInCooldown(x)).ToList());

            mapsElected = [.. _nominationManager.NominationWinners().Concat(mapsScrambled).Distinct()];

            // Create vote list
            List<string> voteOptions = new();
            foreach (var map in mapsElected.Take(mapOptionsCount))
            {
                Votes[map] = 0;
                voteOptions.Add(map);
            }

            if (canShowExtendOption)
            {
                string extendOption = _localizer.Localize("extendtime.list-name");
                Votes[extendOption] = 0;
                voteOptions.Add(extendOption);
            }

            _currentVoteOptions.AddRange(voteOptions);
            RebuildSortedTopVotes();
            _canVote = ServerManager.ValidPlayerCount();
            int voteDuration = isRtv ? _rtvConfig.MapVoteDuration : _endMapConfig.VoteDuration;
            TimeLeft = voteDuration;

            var players = ServerManager.ValidPlayers()
                .Where(p => p != null && p.IsValid)
                .ToList();

            foreach (var snapshot in players)
            {
                int slot = snapshot.Slot;
                try
                {
                    var live = Utilities.GetPlayerFromSlot(slot);
                    if (live is null || !live.ReallyValid())
                        continue;

                    DisplayVoteMenu(live, _currentVoteOptions, voteDuration, isRtv, allowRevote: false);

                    if (_endMapConfig.SoundEnabled)
                    {
                        SoundEventHelper.PlaySound(live, _endMapConfig.SoundPath, _endMapConfig.SoundVolume);
                    }
                }
                catch (Exception ex)
                {
                    _debugLogger.LogError(ex, "[RTV.EndMapVote] StartVote per-player setup failed. slot={Slot}", slot);
                }
            }

            if (_endMapConfig.MenuType != "ChatMenu")
                Server.PrintToChatAll(_localizer.LocalizeWithPrefix("emv.vote-started"));

            if (_endMapConfig.EnableHint)
            {
                var hintType = string.IsNullOrWhiteSpace(_endMapConfig.HintType)
                    ? "GameHint"
                    : _endMapConfig.HintType.Trim();
                if (string.Equals(hintType, "csay", StringComparison.OrdinalIgnoreCase))
                {
                    var message = _localizer.Localize("emv.vote-started").Replace("\"", "'");
                    Server.ExecuteCommand($"css_csay {message}");
                }
                else
                {
                    DisplayGameHintForAll(players, seconds: 5f);
                }
            }

            StartChatMapChoiceReminder();
            ChatCountdown(voteDuration);

            float voteDeadline = Server.CurrentTime + voteDuration;
            Timer = _plugin?.AddTimer(1.0F, () =>
            {
                TimeLeft = (int)Math.Ceiling(Math.Max(0.0, voteDeadline - Server.CurrentTime));
                if (TimeLeft <= 0)
                {
                    _debugLogger.LogInformation("[RTV.EndMapVote] Vote-tick timer reached deadline. Deferring EndVote to next frame. isRtv={IsRtv}", isRtv);
                    
                    Server.NextFrame(() => EndVote(isRtv));
                }
            }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }

        private void CloseAllActiveMenus()
        {
            foreach (var snapshot in ServerManager.ValidPlayers())
            {
                int slot = snapshot.Slot;
                try
                {
                    var live = Utilities.GetPlayerFromSlot(slot);
                    if (live is null || !live.ReallyValid())
                        continue;

                    MenuManager.CloseActiveMenu(live);
                }
                catch (Exception ex)
                {
                    _debugLogger.LogError(ex, "[RTV.EndMapVote] CloseActiveMenu failed. slot={Slot}", slot);
                }
            }
        }

        public void EndVote(bool isRtv)
        {
            CloseAllActiveMenus();

            KillTimer();
            _currentVoteOptions.Clear();
            _revoteMenuOpen.Clear();

            bool ignoreRoundWinConditions =
                ConVar.Find("mp_ignore_round_win_conditions")?.GetPrimitiveValue<bool>() == true;

            MapChangeTrigger trigger;
            if (isRtv)
                trigger = MapChangeTrigger.RoundStart;
            else if (_endMapConfig.ChangeMapImmediately)
                trigger = MapChangeTrigger.IgnoredWinConditions;
            else if (ignoreRoundWinConditions)
                trigger = MapChangeTrigger.IgnoredWinConditions;
            else
                trigger = MapChangeTrigger.MatchEnd;
            string extendOption = _localizer.Localize("extendtime.list-name");

            decimal totalVotes = Votes.Select(x => x.Value).Sum();
            KeyValuePair<string, int> winner;
            Random rnd = new();

            if (totalVotes == 0)
            {
                // No votes cast, pick a random map(not the extend).
                var candidateMaps = Votes.Keys.Where(x => x != extendOption).ToList();
                if (candidateMaps.Count == 0)
                    candidateMaps = Votes.Keys.ToList();
                string chosen = candidateMaps[rnd.Next(candidateMaps.Count)];
                winner = new KeyValuePair<string, int>(chosen, 0);
            }
            else
            {
                int maxVotes = Votes.Values.Max();
                var tiedMaps = Votes.Where(kv => kv.Value == maxVotes).Select(kv => kv.Key).ToList();
                string chosenKey = tiedMaps[rnd.Next(tiedMaps.Count)];
                winner = new KeyValuePair<string,int>(chosenKey, maxVotes);
            }

            decimal percent = totalVotes > 0 ? winner.Value / totalVotes * 100M : 0;

            _debugLogger.LogInformation(
                "[RTV.EndMapVote] Vote ended. isRtv={IsRtv} winner={Winner} winnerVotes={WinnerVotes} totalVotes={TotalVotes} percent={Percent} trigger={Trigger} changeImmediately={ChangeImmediately} currentMap={CurrentMap}",
                isRtv,
                winner.Key,
                winner.Value,
                totalVotes,
                percent,
                trigger,
                !isRtv ? _endMapConfig.ChangeMapImmediately : !_rtvConfig.ChangeAtRoundEnd,
                Server.MapName
            );

            Server.PrintToChatAll(_localizer.LocalizeWithPrefix("emv.vote-ended", winner.Key, percent, totalVotes));

            if (winner.Key == extendOption)
            {
                int maxExt = _generalConfig.MaxMapExtensions;
                bool unlimited = maxExt <= 0;

                if (unlimited || _pluginState.MapExtensionCount < maxExt)
                {
                    bool success = _extendRoundTimeManager.ExtendRoundTime(_generalConfig.RoundTimeExtension);
                    if (success)
                    {
                        _debugLogger.LogInformation(
                            "[RTV.EndMapVote] Extend won and applied. minutes={Minutes} mapExtensionsUsed={ExtensionsUsed}",
                            _generalConfig.RoundTimeExtension,
                            _pluginState.MapExtensionCount + 1
                        );
                        Server.PrintToChatAll(_localizer.LocalizeWithPrefix("extendtime.vote-ended.passed", _generalConfig.RoundTimeExtension, percent, totalVotes));
                        _pluginState.MapExtensionCount++;
                    }
                    else
                    {
                        _debugLogger.LogWarning("[RTV.EndMapVote] Extend won but apply failed. minutes={Minutes}", _generalConfig.RoundTimeExtension);
                        Server.PrintToChatAll(_localizer.LocalizeWithPrefix("extendtime.vote-ended.failed", percent, totalVotes));
                    }
                }

                ScheduleNextVote();
            }
            else
            {
                _changeMapManager.ScheduleMapChange(winner.Key, trigger: trigger);

                if (!isRtv)
                {
                    if (_endMapConfig.ChangeMapImmediately)
                    {
                        _debugLogger.LogInformation("[EndMapVote] Changing map immediately from end-of-map vote. winner={Winner}", winner.Key);
                        _changeMapManager.ChangeNextMap();
                    }
                    else if (ignoreRoundWinConditions)
                    {
                        StartIgnoreWinConditionsPoll(winner.Key);
                    }
                    else
                    {
                        if (trigger == MapChangeTrigger.RoundStart)
                            Server.PrintToChatAll(_localizer.LocalizeWithPrefix("general.changing-map-next-round", winner.Key));
                    }
                }
                else
                {
                    if (!_rtvConfig.ChangeAtRoundEnd)
                    {
                        var delay = _rtvConfig.MapChangeDelay;
                        if (delay <= 0) // Immediate
                        {
                            _debugLogger.LogInformation("[RTV.EndMapVote] RTV map vote changing immediately. winner={Winner}", winner.Key);
                            _changeMapManager.ChangeNextMap();
                        }
                        else // Timer for MapChangeDelay seconds
                        {
                            _debugLogger.LogInformation(
                                "[EndMapVote] RTV map vote armed delayed map change. winner={Winner} delaySeconds={DelaySeconds}",
                                winner.Key,
                                delay
                            );
                            _plugin?.AddTimer(delay, () =>
                            {
                                _changeMapManager.ChangeNextMap();
                            }, TimerFlags.STOP_ON_MAPCHANGE);
                        }
                    }
                    else
                    {
                        Server.PrintToChatAll(_localizer.LocalizeWithPrefix("general.changing-map-next-round", winner.Key));
                    }
                }
            }

            _pluginState.EofVoteHappening = false;
        }
    }
}
