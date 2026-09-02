using BepInEx.Configuration;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Connection settings, stored in BepInEx's shared BepInEx/config/SuperhotArchipelago.cfg
    /// under a [SuperhotArchipelago] section. Editing it while the game runs works without a
    /// restart, since Server/Slot/Password.SettingChanged is wired up in Mod.cs to retrigger
    /// Connect(). Bind(...) doesn't need an explicit save call the way MelonPreferences'
    /// CreateEntry did -- BepInEx's ConfigFile writes to disk itself on every value change --
    /// but Save() is kept as a public no-op-if-unnecessary passthrough so callers (Mod.cs,
    /// ArchipelagoConnectApp.cs, NotificationSettingsButtonPatch.cs) don't need to change.
    /// </summary>
    public static class Config
    {
        private static ConfigFile _file = null!;

        public static ConfigEntry<string> Server { get; private set; } = null!;
        public static ConfigEntry<string> Slot { get; private set; } = null!;
        public static ConfigEntry<string> Password { get; private set; } = null!;

        // Defaults to true so upgrading an already-configured install doesn't silently stop
        // gating levels mid-run (Bind's default only applies the first time this key is seen).
        // See Mod.cs's IsEnabled/SetEnabled and the hub's AP MODE toggle button.
        public static ConfigEntry<bool> Enabled { get; private set; } = null!;

        // Per-item-classification popup toggles, all default true -- see the ARCHIPELAGO >
        // SETTINGS folder (NotificationSettingsButtonPatch.cs) and Config.ShouldNotify below.
        // These only suppress the live popup; NotificationLog's AP LOG history is unaffected.
        public static ConfigEntry<bool> NotifyProgression { get; private set; } = null!;
        public static ConfigEntry<bool> NotifyUseful { get; private set; } = null!;
        public static ConfigEntry<bool> NotifyFiller { get; private set; } = null!;
        public static ConfigEntry<bool> NotifyTrap { get; private set; } = null!;

        /// <summary>
        /// BepInEx hands each plugin its own ConfigFile instance (BaseUnityPlugin.Config,
        /// passed in from Mod.Awake()) rather than a shared static registry, so this needs the
        /// instance passed in instead of reaching for one internally the way MelonPreferences did.
        /// </summary>
        public static void Load(ConfigFile file)
        {
            _file = file;

            Server = _file.Bind(
                "SuperhotArchipelago", "Server", "",
                "Archipelago server address, e.g. archipelago.gg:38281 or localhost:38281");
            Slot = _file.Bind(
                "SuperhotArchipelago", "Slot", "",
                "Your player/slot name, matching the name in your player YAML.");
            Password = _file.Bind(
                "SuperhotArchipelago", "Password", "",
                "Room password, if the server has one set. Leave blank otherwise.");
            Enabled = _file.Bind(
                "SuperhotArchipelago", "Enabled", true,
                "Whether Archipelago mode is on. Turn off to play vanilla SUPERHOT (no " +
                "level gating, no hub overlay) without uninstalling the mod.");
            NotifyProgression = _file.Bind(
                "SuperhotArchipelago", "NotifyProgression", true,
                "Popup for progression items.");
            NotifyUseful = _file.Bind(
                "SuperhotArchipelago", "NotifyUseful", true,
                "Popup for useful items.");
            NotifyFiller = _file.Bind(
                "SuperhotArchipelago", "NotifyFiller", true,
                "Popup for normal (filler) items -- most checks.");
            NotifyTrap = _file.Bind(
                "SuperhotArchipelago", "NotifyTrap", true,
                "Popup for trap items.");
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

        // BepInEx's ConfigEntry writes through to disk on every .Value set, so this is no
        // longer strictly required the way MelonPreferences.SaveToFile was -- kept as a thin
        // passthrough so Mod.cs/ArchipelagoConnectApp.cs/NotificationSettingsButtonPatch.cs
        // don't need call-site changes.
        public static void Save()
        {
            _file.Save();
        }
    }
}
