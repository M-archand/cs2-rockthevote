using CounterStrikeSharp.API.Core;
using System.Text.Json.Serialization;

namespace cs2_rockthevote
{
    public class RtvConfig
    {
        public bool Enabled { get; set; } = true;
        public bool EnabledInWarmup { get; set; } = false;
        public bool EnablePanorama { get; set; } = true;
        public int MinPlayers { get; set; } = 0;
        public int MinRounds { get; set; } = 0;
        public bool ChangeAtRoundEnd { get; set; } = false;
        public int MapChangeDelay { get; set; } = 5;
        public bool SoundEnabled { get; set; } = false;
        public string SoundPath { get; set; } = "sounds/vo/announcer/cs2_classic/felix_broken_fang_pick_1_map_tk01.vsnd_c";
        public int MapsToShow { get; set; } = 6;
        public int RtvVoteDuration { get; set; } = 60;
        public int MapVoteDuration { get; set; } = 60;
        public int CooldownDuration { get; set; } = 180;
        public int MapStartDelay { get; set; } = 180;
        public int VotePercentage { get; set; } = 51;
        public bool EnableCountdown { get; set; } = true;
        public string CountdownType { get; set; } = "chat";
        public int ChatCountdownInterval { get; set; } = 15;
    }

    public class EndOfMapConfig
    {
        public bool Enabled { get; set; } = true;
        public int MapsToShow { get; set; } = 6;
        public string MenuType { get; set; } = "WasdMenu";
        public bool ChangeMapImmediately { get; set; } = false;
        public int VoteDuration { get; set; } = 150;
        public bool SoundEnabled { get; set; } = false;
        public string SoundPath { get; set; } = "sounds/vo/announcer/cs2_classic/felix_broken_fang_pick_1_map_tk01.vsnd_c";
        public int TriggerSecondsBeforeEnd { get; set; } = 180;
        public int TriggerRoundsBeforeEnd { get; set; } = 0;
        public float DelayToChangeInTheEnd { get; set; } = 0F;
        public bool IncludeExtendCurrentMap { get; set; } = true;
        public bool EnableCountdown { get; set; } = false;
        public string CountdownType { get; set; } = "chat";
        public int ChatCountdownInterval { get; set; } = 30;
        public bool EnableHint { get; set; } = false;
    }

    public class VotemapConfig
    {
        public bool Enabled { get; set; } = false;
        public string MenuType { get; set; } = "WasdMenu";
        public int VotePercentage { get; set; } = 50;
        public bool ChangeMapImmediately { get; set; } = true;
        public bool EnabledInWarmup { get; set; } = false;
        public int MinPlayers { get; set; } = 0;
        public int MinRounds { get; set; } = 0;
        public string Permission { get; set; } = "@css/vip";
    }

    public class VoteExtendConfig
    {
        public bool Enabled { get; set; } = false;
        public bool EnablePanorama { get; set; } = true;
        public int VoteDuration { get; set; } = 60;
        public int VotePercentage { get; set; } = 50;
        public int CooldownDuration { get; set; } = 180;
        public bool EnableCountdown { get; set; } = true;
        public string CountdownType { get; set; } = "chat";
        public int ChatCountdownInterval { get; set; } = 15;
        public string Permission { get; set; } = "@css/vip";
    }

    public class NominateConfig
    {
        public bool Enabled { get; set; } = true;
        public bool EnabledInWarmup { get; set; } = true;
        public string MenuType { get; set; } = "WasdMenu";
        public int NominateLimit { get; set; } = 1;
        public string Permission { get; set; } = "";
    }

    public class MapChooserConfig
    {
        public string Command { get; set; } = "mapmenu,mm";
        public string MenuType { get; set; } = "WasdMenu";
        public string Permission { get; set; } = "@css/changemap";
    }

    public class GeneralConfig
    {
        public int MaxMapExtensions { get; set; } = 2;
        public int RoundTimeExtension { get; set; } = 15;
        public int MapsInCoolDown { get; set; } = 3;
        public bool HideHudAfterVote { get; set; } = true;
        public bool RandomStartMap { get; set; } = false;
        public bool IncludeSpectator { get; set; } = true;
        public bool IncludeAFK { get; set; } = true;
        public int AFKCheckInterval { get; set; } = 30;
        public string DiscordWebhook { get; set; } = "";
        public string NominateCommand { get; set; } = "nom,nominate";
        public bool EnableWorkshopMapValidation { get; set; } = false;
    }

    public class Config : BasePluginConfig, IBasePluginConfig
    {
        [JsonPropertyName("ConfigVersion")]
        public override int Version { get; set; } = 19;
        public RtvConfig Rtv { get; set; } = new();
        public EndOfMapConfig EndOfMapVote { get; set; } = new();
        public NominateConfig Nominate { get; set; } = new();
        public VotemapConfig Votemap { get; set; } = new();
        public VoteExtendConfig VoteExtend { get; set; } = new();
        public MapChooserConfig MapChooser { get; set; } = new();
        public GeneralConfig General { get; set; } = new();
    }
}
