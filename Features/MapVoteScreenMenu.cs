using CounterStrikeSharp.API.Core;
using CS2MenuManager.API.Class;
using CS2MenuManager.API.Menu;

namespace cs2_rockthevote
{
    public static class MapVoteScreenMenu
    {
        public static void Open(Plugin plugin, CCSPlayerController player, List<string> voteOptions, Action<CCSPlayerController, string> onOptionSelected, string title)
        {
            var screenCfg = plugin.Config.ScreenMenu;
            var menu = MenuManager.CreateMenu<ScreenMenu>(title, plugin);

            menu.ShowResolutionsOption = screenCfg.EnabledResolutionOption;
            menu.ExitButton = screenCfg.EnabledExitOption;

            for (int i = 0; i < voteOptions.Count; i++)
            {
                int index = i;
                //string optionText = $"!{index + 1} {voteOptions[i]}";
                string optionText = $"{voteOptions[i]}";
                menu.AddItem(optionText, (p, o) =>
                {
                    onOptionSelected(p, voteOptions[index]);
                });
            }

            MenuManager.OpenMenu(player, menu, 0, (p, m) => new ScreenMenuInstance(p, m));
        }

        public static void Prime(BasePlugin plugin, CCSPlayerController player)
        {
            var menu = MenuManager.CreateMenu<ScreenMenu>(" ", plugin);
            menu.ExitButton = false;
            menu.ShowResolutionsOption = false;
            menu.Size = 1;

            // Add an invisible dummy item
            menu.AddItem(" ", (p, o) => { });

            MenuManager.OpenMenu(player, menu, 0, (p, m) => new ScreenMenuInstance(p, m));
            MenuManager.CloseActiveMenu(player);
        }

        public static void Close(BasePlugin plugin, CCSPlayerController player)
        {
            MenuManager.CloseActiveMenu(player);
        }
    }
}