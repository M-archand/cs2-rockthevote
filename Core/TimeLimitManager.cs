using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;

namespace cs2_rockthevote.Core
{
    public class TimeLimitManager(GameRules gameRules) : IPluginDependency<Plugin, Config>
    {
        private GameRules _gameRules = gameRules;
        private ConVar? _timeLimit;
        private decimal TimeLimitValue => (decimal)(_timeLimit?.GetPrimitiveValue<float>() ?? 0F) * 60M;
        public bool UnlimitedTime => TimeLimitValue <= 0;

        public decimal TimePlayed
        {
            get
            {
                if (_gameRules.WarmupRunning)
                    return 0;

                float gameStartTime = _gameRules.GameStartTime;
                if (gameStartTime <= 0)
                    return 0;

                return (decimal)(Server.CurrentTime - gameStartTime);
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
        }

        public void OnLoad(Plugin plugin)
        {
            LoadCvar();
        }
    }
}
