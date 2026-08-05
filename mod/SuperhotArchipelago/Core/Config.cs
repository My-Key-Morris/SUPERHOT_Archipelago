using MelonLoader;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Connection settings, read from MelonLoader's own preferences system rather than a
    /// custom in-game UI/console command (simplest thing that could work for a first
    /// real test -- no need to design and wire up in-game text input). Confirmed real
    /// API against the actual MelonLoader.dll shipped with this game install (decompiled
    /// to check): MelonPreferences.CreateCategory(string, string?) and
    /// MelonPreferences_Category.CreateEntry&lt;T&gt;(string, T, ...), both returning
    /// objects with a settable/gettable .Value.
    ///
    /// First run creates/updates UserData/MelonPreferences.cfg with empty defaults under
    /// a [SuperhotArchipelago] section -- this is NOT a per-mod file, MelonLoader shares
    /// one preferences file across every installed mod unless a mod explicitly asks for
    /// its own (we don't). Edit that section (server, slot, and optionally password) and
    /// restart SUPERHOT to connect. Editing it while the game is running also works
    /// without a restart, because Server/Slot/Password.OnEntryValueChanged is wired up
    /// in Mod.cs to retrigger Connect().
    ///
    /// NOTE (found by an actual test run): MelonPreferences_Category.CreateEntry(...)
    /// does not write anything to disk by itself -- confirmed by decompiling
    /// MelonLoader.dll, MelonPreferences_Category.cs's SaveToFile() is a separate method
    /// that has to be called explicitly. A real test run confirmed this the hard way:
    /// the mod loaded and logged normally, but closing the game afterward left no
    /// [SuperhotArchipelago] section in MelonPreferences.cfg at all, because nothing had
    /// ever called SaveToFile(). Calling it once here after creating the entries
    /// guarantees the section exists after the very first run, instead of depending on
    /// MelonLoader happening to persist it at some other point (shutdown, a
    /// settings-changed hook, etc.) that evidently doesn't fire here.
    /// </summary>
    public static class Config
    {
        private static MelonPreferences_Category _category = null!;

        public static MelonPreferences_Entry<string> Server { get; private set; } = null!;
        public static MelonPreferences_Entry<string> Slot { get; private set; } = null!;
        public static MelonPreferences_Entry<string> Password { get; private set; } = null!;

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

            _category.SaveToFile(printmsg: false);
        }

        public static bool IsConfigured => Server.Value != "" && Slot.Value != "";

        // Kept category around (rather than a local variable in Load()) so Core/ArchipelagoConnectApp.cs
        // can persist Server/Slot/Password changes made through the in-game connect screen the
        // same way Load() does for the very first run -- CreateEntry(...) alone doesn't write
        // anything to disk by itself (see Load()'s own comment/NOTES.md for the real bug that
        // caught), and that's just as true for a later .Value change as it is for the first.
        public static void Save()
        {
            _category.SaveToFile(printmsg: false);
        }
    }
}
