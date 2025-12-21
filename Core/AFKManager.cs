using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace cs2_rockthevote
{
    public partial class Plugin
    {
        [GameEventHandler(HookMode.Pre)]
        public HookResult EventPlayerSpawn(EventPlayerSpawn @event, GameEventInfo @eventInfo)
        {
            var player = @event.Userid;
            if (player.ReallyValid())
            {
                _afkManager.InitializeLastOrigins(player!);
            }
            return HookResult.Continue;
        }
    }

    public class AFKManager : IPluginDependency<Plugin, Config>
    {
        private Plugin? _plugin;
        private GeneralConfig _generalConfig = new();
        private readonly Dictionary<uint, Vector> _lastOrigin = new();
        private readonly HashSet<uint> _afkPlayers = new();
        private Timer? _timer;
        private DateTime _lastCheckUtc = DateTime.MinValue;

        public AFKManager() { }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
        }

        public void OnConfigParsed(Config config)
        {
            _generalConfig = config.General;

            if (_generalConfig.IncludeAFK)
            {
                KillAFKTimer();
                return;
            }

            //If config reloads mid map, apply changes immediately
            if (_timer != null) RestartAfkTimer();
        }

        public void OnMapStart(string map)
        {
            _afkPlayers.Clear();
            _lastOrigin.Clear();

            if (_generalConfig.IncludeAFK)
            {
                KillAFKTimer();
                return;
            }

            Server.NextFrame(RestartAfkTimer);
        }

        private void RestartAfkTimer()
        {
            KillAFKTimer();

            _timer = _plugin!.AddTimer(
                _generalConfig.AFKCheckInterval,
                CheckAllPlayers,
                TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE
            );
        }

        public void InitializeLastOrigins(CCSPlayerController player, float delaySeconds = 1.0f)
        {
            if (_plugin == null) return;

            _plugin.AddTimer(delaySeconds, () =>
            {
                // player might have disconnected or switched; re-check validity
                if (player == null || !player.IsValid || !player.ReallyValid())
                    return;
                
                _afkPlayers.Remove(player.Index);

                var origin = player.PlayerPawn.Value?.CBodyComponent?.SceneNode?.AbsOrigin;
                if (origin != null)
                    _lastOrigin[player.Index] = new Vector(origin.X, origin.Y, origin.Z);
            });

            Server.PrintToConsole($"[AFKManager] Checked position for player: {player.PlayerName}. Position: {player.PlayerPawn.Value?.CBodyComponent?.SceneNode?.AbsOrigin}");
        }

        public void CheckAllPlayers()
        {
            foreach (var player in Utilities.GetPlayers().Where(p => p.ReallyValid()))
            {
                var origin = player.PlayerPawn.Value?.CBodyComponent?.SceneNode?.AbsOrigin;
                if (origin == null) continue;

                var current = new Vector(origin.X, origin.Y, origin.Z);
                var idx = player.Index;

                if (_lastOrigin.TryGetValue(idx, out var last))
                {
                    float dx = current.X - last.X;
                    float dy = current.Y - last.Y;
                    float dz = current.Z - last.Z;
                    float distSq = dx * dx + dy * dy + dz * dz;

                    if (distSq <= 1.0f)
                        _afkPlayers.Add(idx);
                    else
                    {
                        _afkPlayers.Remove(idx);
                        _lastOrigin[idx] = current;
                    }
                }
                else
                {
                    _lastOrigin[idx] = current;
                }
            }

            _lastCheckUtc = DateTime.UtcNow;
        }

        public void KillAFKTimer()
        {
            _timer?.Kill();
            _timer = null;
            _afkPlayers.Clear();
        }

        public bool MarkPlayerActive(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || !player.ReallyValid())
                return false;

            _afkPlayers.Remove(player.Index);

            var origin = player.PlayerPawn.Value?.CBodyComponent?.SceneNode?.AbsOrigin;
            if (origin != null)
                _lastOrigin[player.Index] = new Vector(origin.X, origin.Y, origin.Z);

            return true;
        }

        public bool IsAfk(CCSPlayerController player) => _afkPlayers.Contains(player.Index);
    }
}