using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;

namespace cs2_rockthevote
{
    public static class PermissionUtility
    {
        public static string[] Parse(string? permissions)
        {
            return string.IsNullOrWhiteSpace(permissions)
                ? Array.Empty<string>()
                : [.. permissions
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(permission => !string.IsNullOrWhiteSpace(permission))
                    .Distinct(StringComparer.Ordinal)];
        }

        public static bool HasAny(CCSPlayerController player, IReadOnlyCollection<string> permissions)
        {
            if (permissions.Count == 0)
                return true;

            foreach (string permission in permissions)
            {
                if (AdminManager.PlayerHasPermissions(player, permission))
                    return true;
            }

            return false;
        }
    }
}
