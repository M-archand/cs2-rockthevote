using System;
using System.Linq;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;

namespace cs2_rockthevote
{
    internal static class SoundEventHelper
    {
        private static readonly byte[] SoundVolumePackedPrefix = { 0xE9, 0x54, 0x60, 0xBD, 0x08, 0x04, 0x00 };
        private static uint _soundEventGuidCounter = 1;

        public static void PlaySound(CCSPlayerController player, string soundPath, float volume)
        {
            var recipients = new RecipientFilter(player);

            if (IsFullVolume(volume))
            {
                player.EmitSound(soundPath, recipients);
                return;
            }

            uint guid;
            if (uint.TryParse(soundPath, out var soundHash))
            {
                guid = ++_soundEventGuidCounter;
                var start = UserMessage.FromId(208);
                start.SetUInt("soundevent_guid", guid);
                start.SetUInt("soundevent_hash", soundHash);
                start.Recipients = recipients;
                start.Send();
            }
            else
            {
                guid = Convert.ToUInt32(player.EmitSound(soundPath, recipients));
            }

            var packedParams = SoundVolumePackedPrefix
                .Concat(BitConverter.GetBytes(volume))
                .ToArray();

            var volumeOverride = UserMessage.FromId(210);
            volumeOverride.SetUInt("soundevent_guid", guid);
            volumeOverride.SetBytes("packed_params", packedParams);
            volumeOverride.Recipients = recipients;
            volumeOverride.Send();
        }

        public static bool IsFullVolume(float volume)
        {
            return Math.Abs(volume - 1.0f) < 0.0001f;
        }
    }
}
