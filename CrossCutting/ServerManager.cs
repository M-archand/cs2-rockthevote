﻿using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace cs2_rockthevote
{
    public static class ServerManager
    {
        public static CCSPlayerController[] ValidPlayers(bool considerBots = false)
        {
            return [.. Utilities.GetPlayers()
                .Where(x => x.ReallyValid(considerBots))
                .Where(x => !x.IsHLTV)
                .Where(x => considerBots || !x.IsBot)];
        }

        public static int ValidPlayerCount(bool considerBots = false)
        {
            return ValidPlayers(considerBots).Length;
        }
    }
}
