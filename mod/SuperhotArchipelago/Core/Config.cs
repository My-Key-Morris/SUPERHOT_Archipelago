using MelonLoader;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Connection settings, stored in MelonLoader's shared UserData/MelonPreferences.cfg
    /// under a [SuperhotArchipelago] section. Editing it while the game runs works without a
    /// restart, since Server/Slot/Password.OnEntryValueChanged is wired up in Mod.cs to
    /// retrigger Connect(). CreateEntry(...) doesn't write to disk by itself, so SaveToFile()
    /// is called explicitly after creating the entries to guarantee the section exists after
    /// the first run.
    /// </summary>
    public static class Config
    {
        private static MelonPreferences_Category _category = null!;

        public static MelonPreferences_Entry<string> Server { get; private set; } = null!;
        public static MelonPreferences_Entry<string> Slot { get; private set; } = null!;
        public static MelonPreferences_Entry<string> Password { get; private set; } = null!;

        // Defaults to true so upgrading an already-configured install doesn't silently stop
        // gating levels mid-run (CreateEntry's default only applies the first time this key
        // is seen). See Mod.cs's IsEnabled/SetEnabled and the hub's AP MODE toggle button.
        public static MelonPreferences_Entry<bool> Enabled { get; private set; } = null!;

        // Per-item-classification popup toggles, all default true -- see the ARCHIPELAGO >
        // SETTINGS folder (NotificationSettingsButtonPatch.cs) and Config.ShouldNotify below.
        // These only suppress the live popup; NotificationLog's AP LOG history is unaffected.
        public static MelonPreferences_Entry<bool> NotifyProgression { get; private set; } = null!;
        public static MelonPreferences_Entry<bool> NotifyUseful { get; private set; } = null!;
        public static MelonPreferences_Entry<bool> NotifyFiller { get; private set; } = null!;
        public static MelonPreferences_Entry<bool> NotifyTrap { get; private set; } = null!;

        public static void Load()
        {
            _category = MelonPreferences.CreateCategory(
                "SuperhotArchipelago", "SUPERHOT Archipelago");

            Server = _category.CreateEntry(
                "Server", "", "Server",
                "Archipelago server address, e.g. archipelago.gg:38281 or localhost:38281");
            Slot = _category.CreateEntry(
                "Slot", "", "Slot",
                "Your player/slot name, matching the name in your player YAML.");
            Password = _category.CreateEntry(
                "Password", "", "Password",
                "Room password, if the server has one set. Leave blank otherwise.");
            Enabled = _category.CreateEntry(
                "Enabled", true, "Enabled",
                "Whether Archipelago mode is on. Turn off to play vanilla SUPERHOT (no " +
                "level gating, no hub overlay) without uninstalling the mod.");
            NotifyProgression = _category.CreateEntry(
                "NotifyProgression", true, "NotifyProgression",
                "Popup for progression items.");
            NotifyUseful = _category.CreateEntry(
                "NotifyUseful", true, "NotifyUseful",
                "Popup for useful items.");
            NotifyFiller = _category.CreateEntry(
                "NotifyFiller", true, "NotifyFiller",
                "Popup for normal (filler) items -- most checks.");
            NotifyTrap = _category.CreateEntry(
                "NotifyTrap", true, "NotifyTrap",
                "Popup for trap items.");

            _category.SaveToFile(printmsg: false);
        }

        public static bool IsConfigured => Server.Value != "" && Slot.Value != "";

        /// <summary>Whether a live popup should show for an item of this classification -- the AP LOG
        /// history is unaffected either way. See the ARCHIPELAGO > SETTINGS hub folder.</summary>
        public static bool ShouldNotify(NotificationColors.ItemClass itemClass)
        {
            return itemClass switch
            {
                NotificationColors.ItemClass.Progression => NotifyProgression.Value,
                NotificationColors.ItemClass.Useful => NotifyUseful.Value,
                NotificationColors.ItemClass.Trap => NotifyTrap.Value,
                _ => NotifyFiller.Value,
            };
        }

        // Category kept around (rather than a local in Load()) so ArchipelagoConnectApp.cs can
        // persist in-game connect-screen changes the same way, since CreateEntry alone never
        // writes to disk.
        public static void Save()
        {
            _category.SaveToFile(printmsg: false);
        }
    }
}
