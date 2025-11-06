# CS2 RockTheVote (RTV)
General purpose map voting plugin.

# Features
- Reads from a custom maplist
- RTV Command (Using !rtv in chat, or with Panorama system that is built into CS2 [F1 = Yes, F2 = No])
- End of Map Vote. Supports map cooldown, map extension option.
- Nominate command (nominate a map to appear in the map vote). Partial name matching, and conflicting map name resolution (surf_beginner, surf_beginner2). Limit 1 per player.

  ![nominate](https://github.com/user-attachments/assets/6ac056bc-9842-4422-ac0d-c7cd814b3ba6)
  
- Supports workshop maps, and custom map names. E.g. "surf_beginner (T1, Staged)"
- Nextmap command. Prints the next map to chat.
- Translated (Google Translate, ymmv)
- Extend command. !extend 10 extends map by 10 minutes (flag restricted)
- Vote Extend command. !ve/!votextend starts a vote to extend the current map (flag restricted)
- Map Chooser command. !mapmenu opens a menu with your map list, selected map is changed to immediately (flag restricted)
- Optional sound alert when map vote or !rtv starts (configurable sound)
- Optional hud alert
  
  ![hudalert](https://github.com/user-attachments/assets/23c35f20-b4f0-4122-b241-287b44efdb27)
  
- Optional chat/hud vote countdown

 ![hudcountdown](https://github.com/user-attachments/assets/e1034f3c-340a-4d88-8d8a-96526f333fad)
 ![chatcountdown](https://github.com/user-attachments/assets/803826a1-665b-4ab7-9e38-fbb0e8d702be)

- Panorama Vote (F1 = Yes, F2 = No) for !rtv & !voteextend

![panoramavote](https://github.com/user-attachments/assets/31ebe223-225f-4cef-812e-3bf6c56e590d)
![voteextend](https://github.com/user-attachments/assets/5cfd9a5f-36a5-4a11-ae26-3e74d5387251)
- ChatMenu/CenterHtmlMenu/WasdMenu/ConsoleMenu for EndOfMapVote/!nominate/!votemap (ScreenMenu temporarily removed)

![wasdmenu](https://github.com/user-attachments/assets/1df185bf-4313-4010-81de-98111ae383dc)
![chatmenu](https://github.com/user-attachments/assets/8d7e9ee8-b26e-47b1-89d8-ced96b13a392)
![hudmenu](https://github.com/user-attachments/assets/0fd37e45-bf7f-4f97-9b7b-7fab92352392)

- Maplist Validator. Send to error log or Discord when a map is no longer available on the workshop.

  ![MapDiscordWebhook](https://github.com/user-attachments/assets/eaf8d706-abd1-4258-a7a3-b9cb44500802)
  ![WorkshopMapLog](https://github.com/user-attachments/assets/2f65dd9d-1ee9-4217-a753-81358973df2e)
- !maps command. List all maps available in the console.
  
![mapscommand](https://github.com/user-attachments/assets/d4ab1377-0b29-45b6-bdaa-06b6a7664751)



## Requirements
[CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) (Tested on v337)

[CS2MenuManager](https://github.com/schwarper/CS2MenuManager) (Tested on v39)

# Installation
- Download the latest release from https://github.com/M-archand/cs2-rockthevote/releases
- Extract the .zip file into `addons/counterstrikesharp/plugins`
- Update the maplist.example.txt to inlcude your desired maps and then rename it to maplist.txt

## [ Configuration ]
- A config file will be created in `addons/counterstrikesharp/configs/plugins/RockTheVote` the first time you load the plugin.
- Changes in the config file will require you to reload the plugin or restart the server (changing the map won't work).
- Maps that will be used in RTV/nominate/votemap/end of map vote are located in addons/counterstrikesharp/plugins/RockTheVote/maplist.txt

```json
{
  "ConfigVersion": 19,
  "Rtv": {
    "Enabled": true,
    "EnabledInWarmup": false,
    "EnablePanorama": true, # true = use built in Panorama voting system (F1 = Yes, F2 = No). False = use !rtv in chat
    "MinPlayers": 0,
    "MinRounds": 0,
    "ChangeAtRoundEnd": false, # false = use MapChangeDelay value below. true = wait until round end to change the map
    "MapChangeDelay": 5, # The delay in seconds after the rtv map vote has passed before the map is changed. 0 = immediate
    "SoundEnabled": false, # true = play a sound when the end of map vote starts.
    "SoundPath": "sounds/vo/announcer/cs2_classic/felix_broken_fang_pick_1_map_tk01.vsnd_c",
    "MapsToShow": 6, # How many maps to show in the resulting map vote if the rtv passes
    "RtvVoteDuration": 60, # How long the rtv vote lasts
    "MapVoteDuration": 60, # How long the resulting map vote will last
    "CooldownDuration": 180, # How many seconds must pass before another !rtv can be initiated
    "MapStartDelay": 180, # How many seconds must pass after the map starts before an !rtv can be called
    "VotePercentage": 51, # Percentage of votes required to pass the vote
    "EnableCountdown": true, # Whether the chat/hud countdown is enabled
    "CountdownType": "chat", # chat = prints to chat on an interval how much time is left in the vote. hud = persistent alert on the hud counting down as each second passes
    "ChatCountdownInterval": 15 # If CountdownType = chat, how often we print to chat how much time is remaining to vote
  },
  "EndOfMapVote": {
    "Enabled": true,
    "MapsToShow": 6, # How many maps to show in the vote. If IncludeExtendCurrentMap = true, the extension option takes up 1 slot
    "MenuType": "WasdnMenu", # The menu that will be used to show the vote. Options = ChatMenu, CenterHtmlMenu, WasdMenu, ConsoleMenu
    "ChangeMapImmediately": false, # false = change when the map ends. true = change as soon as the VoteDuration ends
    "VoteDuration": 150, # How long the map vote will last (this must be smaller than TriggerSecondsBeforeEnd)
    "SoundEnabled": false, # true = play a sound when the end of map vote starts.
    "SoundPath": "sounds/vo/announcer/cs2_classic/felix_broken_fang_pick_1_map_tk01.vsnd_c", # Filepath of the sound you want to be played
    "TriggerSecondsBeforeEnd": 180, # When the End of Map Vote will be triggered (this must be higher than VoteDuration)
    "TriggerRoundsBeforeEnd": 0, # What round the vote is trigger on (use 0 for game modes like surf/bhop/etc or it'll never appear)
    "DelayToChangeInTheEnd": 0, # How long the mvp screen shows at the end if ChangeMapImmediately = false
    "IncludeExtendCurrentMap": true, # Include an option to extend the current map
    "EnableCountdown": false, # Whether the chat/hud countdown is enabled
    "CountdownType": "chat", # chat = prints to chat on an interval how much time is left in the vote. hud = persistent alert on the hud counting down as each second passes
    "ChatCountdownInterval": 30 # If CountdownType = chat, how often we print to chat how much time is remaining to vote
    "EnableHint": true # Shows a message in the center of the screen for 5 seconds notifying that the map vote has started
  },
  "Nominate": {
    "Enabled": true,
    "EnabledInWarmup": true,
    "MenuType": "WasdnMenu", # The menu that will be used to show the vote. Options = ChatMenu, CenterHtmlMenu, WasdMenu, ConsoleMenu
    "NominateLimit": 1, # How many maps a single player can nominate per map vote
    "Permission": "" # empty = anyone can use. "@css/vip" = only vip's can use (any perm allowed)
  },
  "Votemap": {
    "Enabled": false,
    "MenuType": "WasdnMenu", # The menu that will be used to show the vote. Options = ChatMenu, CenterHtmlMenu, WasdMenu, ConsoleMenu
    "VotePercentage": 50, # Percentage of votes required to pass the vote
    "ChangeMapImmediately": true,
    "EnabledInWarmup": false,
    "MinPlayers": 0,
    "MinRounds": 0,
    "Permission": "@css/vip" # empty = anyone can use. "@css/vip" = only vip's can use (any perm allowed)
  },
  "VoteExtend": {
    "Enabled": false,
    "EnablePanorama": true, # true = use built in Panorama voting system (F1 = Yes, F2 = No). False = use !ve in chat
    "VoteDuration": 60, # How long the vote will last
    "VotePercentage": 50, # Percentage of votes required to pass the vote
    "CooldownDuration": 180, # How many seconds must pass before another !ve can be called
    "EnableCountdown": true,
    "CountdownType": "chat", # chat = prints to chat on an interval how much time is left in the vote. hud = persistent alert on the hud counting down as each second passes
    "ChatCountdownInterval": 15, # If CountdownType = chat, how often we print to chat how much time is remaining to vote
    "Permission": "@css/vip" # empty = anyone can use. "@css/vip" = only vip's can use (any perm allowed)
  },
  "MapChooser": {
    "Command": "mapmenu,mm", The command used to open the menu, multiple can be set
    "MenuType": "WasdMenu", # The menu that will be used to show the vote. Options = ChatMenu, CenterHtmlMenu, WasdMenu, ConsoleMenu
    "Permission": "@css/root,@css/changemap" # empty = anyone can use. "@css/vip" = only vip's can use (any perm allowed)
  },
  "General": {
    "MaxMapExtensions": 2,
    "RoundTimeExtension": 15, # How long the extension will be in minutes for !VoteExtend or End of Map Vote extension
    "MapsInCoolDown": 3, # How many recent maps that won't appear again in the End of Map Vote/can't be nominated.
    "HideHudAfterVote": true, # Only applicable in MenuType = HudMenu. true = closes the hud after the player has voted
    "RandomStartMap": false, # true = a random map will be used when the server restarts. false = will use whatever you set in your startup command
    "IncludeSpectator": true, # true = spectators can vote (only applicable to !rtv). false = spectators can't vote
  	"IncludeAFK": false, # true = AFK players are included in the vote count (only applicable to !rtv). false = AFK players aren't included in the vote count
  	"AFKCheckInterval": 60, # how often an AFK check occurs in seconds (compares players coordinates between current and last check, also run again when the vote is initiated)
    "DiscordWebhook": "" # blank = no alert. Discord Webhook added will alert you to any workshop maps in your maplist.txt that are no longer on the workshop
  }
}
```
  
# Adding workshop maps
```
surf_beginner:3070321829
surf_nyx (T1, Linear):3129698096
de_dust2
```

# Roadmap
- [ ] Automatically remove invalid workshop maps (currently only sends notification)
- [ ] !extend max extension value
- [ ] Add vote percentage required for winning map (e.g. must receive 25% of the vote)
- [ ] Add vote runnoff (e.g. 2nd stage of voting between 2 maps if minimum vote percentage not achieved for a map)
- [ ] Add !revote to allow players to change their vote

# Translations
| Language             |
| -------------------- |
| English              |
| French               |
| Spanish              |
| Ukrainian            |
| Turkish              |
| Latvian              |
| Hungarian            |
| Polish               |
| Russian              |
| Portuguese (BR)      |
| Chinese (Simplified) |

![GitHub Downloads](https://img.shields.io/github/downloads/M-archand/cs2-rockthevote/total?style=for-the-badge)
