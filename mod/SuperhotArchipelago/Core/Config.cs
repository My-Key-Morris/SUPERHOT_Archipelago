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

            _category.SaveToFile(printmsg: false);
        }

        public static bool IsConfigured => Server.Value != "" && Slot.Value != "";

        // Category kept around (rather than a local in Load()) so ArchipelagoConnectApp.cs can
        // persist in-game connect-screen changes the same way, since CreateEntry alone never
        // writes to disk.
        public static void Save()
        {
            _category.SaveToFile(printmsg: false);
        }
    }
}
