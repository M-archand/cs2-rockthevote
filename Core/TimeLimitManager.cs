using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;

namespace cs2_rockthevote.Core
{
    public class TimeLimitManager(GameRules gameRules) : IPluginDependency<Plugin, Config>
    {
        private GameRules _gameRules = gameRules;
        private ConVar? _timeLimit;
        private float _timelimitStartTime = 0f;
        private bool _timelimitStarted = false;
        private decimal TimeLimitValue => (decimal)(_timeLimit?.GetPrimitiveValue<float>() ?? 0F) * 60M;
        public bool UnlimitedTime => TimeLimitValue <= 0;

        public decimal TimePlayed
        {
            get
            {
                if (_gameRules.WarmupRunning || !_timelimitStarted)
                    return 0;

                return (decimal)(Server.CurrentTime - _timelimitStartTime);
            }
        }

        public decimal TimeRemaining
        {
            get
            {
                if (UnlimitedTime || TimePlayed > TimeLimitValue)
                    return 0;

                return TimeLimitValue - TimePlayed;
            }

            set => _timeLimit!.SetValue((float)value);
        }

        void LoadCvar()
        {
            _timeLimit = ConVar.Find("mp_timelimit");
        }

        public void OnMapStart(string map)
        {
            LoadCvar();
            _timelimitStartTime = 0f;
            _timelimitStarted = false;
        }

        public void OnLoad(Plugin plugin)
        {
            LoadCvar();
            plugin.RegisterEventHandler<EventRoundStart>((ev, info) =>
            {
                if (!_timelimitStarted)
                {
                    bool hasPlayers = Utilities.GetPlayers().Any(p =>
                        p.IsValid && !p.IsBot && !p.IsHLTV &&
                        p.Connected == PlayerConnectedState.Connected);

                    if (hasPlayers)
                    {
                        _timelimitStartTime = Server.CurrentTime;
                        _timelimitStarted = true;
                    }
                }
                return HookResult.Continue;
            });
        }
    }
}
