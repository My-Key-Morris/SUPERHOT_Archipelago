using MelonLoader;

// CONFIRMED against the real install's SH_Data/app.info, which literally contains these
// two strings on their own lines.
[assembly: MelonInfo(typeof(SuperhotArchipelago.Core.Mod), "SuperhotArchipelago", "0.1.0", "Michael")]
[assembly: MelonGame("SUPERHOT_Team", "SUPERHOT")]

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Mod entry point. Lifecycle order: OnEarlyInitializeMelon -> OnInitializeMelon
    /// (safe to touch Unity objects) -> OnLateInitializeMelon, then OnUpdate every frame.
    /// Level completion itself is detected via Harmony patches (see ../Patches/), not
    /// OnSceneWasLoaded -- SUPERHOT reuses some scene names for multiple story beats
    /// (see levels.json's _caveats), so scene-load alone isn't a reliable "which level"
    /// signal; LevelSetup.CurrentLevelInfo at the moment of UnlockNextLevel() is.
    /// OnSceneWasLoaded doubles as a debug log and a last-resort access safety net (see
    /// its own comment below) -- everything else about actually gating access to a level
    /// lives in the Harmony patches instead.
    /// </summary>
    public class Mod : MelonMod
    {
        public static ArchipelagoConnection? Connection { get; private set; }
        public static LocationManager? Locations { get; private set; }
        public static ItemManager? Items { get; private set; }

        // Static accessor so Harmony patches (static classes/methods, no MelonMod
        // instance of their own) can still log through the same MelonLoader console.
        public static MelonLogger.Instance? Log { get; private set; }

        // Static self-reference so Core/ArchipelagoConnectApp.cs (a plain view class --
        // no MelonMod instance of its own, same reasoning as Log above) can still reach
        // the instance method TryConnect() below through ApplyConnectionSettingsAndConnect.
        private static Mod? _instance;

        // Set for the duration of ApplyConnectionSettingsAndConnect's three Config.*.Value
        // writes below, so the OnEntryValueChanged-driven auto-reconnect (still wired up
        // for the original "hand-edit MelonPreferences.cfg" workflow) doesn't fire three
        // separate, sequential, blocking connect attempts for one user action -- only the
        // explicit TryConnect() call at the end of that method actually connects.
        private static bool _suppressAutoReconnect;

        public override void OnInitializeMelon()
        {
            _instance = this;
            Log = LoggerInstance;
            LoggerInstance.Msg("SuperhotArchipelago loading.");

            Config.Load();
            LevelCatalog.Load(LoggerInstance);

            Connection = new ArchipelagoConnection(LoggerInstance);
            Locations = new LocationManager(Connection, LoggerInstance);
            Items = new ItemManager(Connection, LoggerInstance);

            if (Config.IsConfigured)
            {
                TryConnect();
            }
            else
            {
                LoggerInstance.Msg("Not connecting yet: Server/Slot not set. Click the " +
                    "ARCHIPELAGO icon on the hub's main screen to open the connection menu " +
                    "and fill them in, or edit the [SuperhotArchipelago] section of " +
                    "UserData/MelonPreferences.cfg directly (Server/Slot/Password changes " +
                    "there also trigger a reconnect automatically).");
            }

            Config.Server.OnEntryValueChanged.Subscribe((_, _) => TryConnect());
            Config.Slot.OnEntryValueChanged.Subscribe((_, _) => TryConnect());
            Config.Password.OnEntryValueChanged.Subscribe((_, _) => TryConnect());
        }

        private void TryConnect()
        {
            if (_suppressAutoReconnect || !Config.IsConfigured)
            {
                return;
            }

            LoggerInstance.Msg($"Connecting to '{Config.Server.Value}' as '{Config.Slot.Value}'...");
            Connection?.Connect(Config.Server.Value, Config.Slot.Value,
                Config.Password.Value == "" ? null : Config.Password.Value);
        }

        // Called by Core/ArchipelagoConnectApp.cs when the player submits the PASSWORD
        // field. Real, explicit user request: an in-game way to set up the connection so
        // players don't have to find and hand-edit MelonPreferences.cfg themselves (see
        // ArchipelagoConnectApp.cs's own docstring for the full design reasoning).
        // Setting Server/Slot/Password together and connecting once here
        // -- rather than just setting the three MelonPreferences_Entry.Value properties
        // directly from the UI code, which is all this method does beyond the suppression
        // and the final explicit TryConnect() -- is what keeps one button press from
        // becoming three sequential, blocking connect attempts (see _suppressAutoReconnect
        // above).
        public static void ApplyConnectionSettingsAndConnect(string server, string slot, string password)
        {
            if (_instance == null)
            {
                return;
            }

            _suppressAutoReconnect = true;
            try
            {
                Config.Server.Value = server;
                Config.Slot.Value = slot;
                Config.Password.Value = password;
                Config.Save();
            }
            finally
            {
                _suppressAutoReconnect = false;
            }

            _instance.TryConnect();
        }

        public override void OnUpdate()
        {
            // Network item receives happen off Unity's main thread; ItemManager queues
            // them and this drains the queue once per frame on the main thread, where
            // it's safe to touch Unity/game state.
            Items?.ProcessQueue();

            // Real bug report: the hub's ARCHIPELAGO button sometimes showed OFFLINE while
            // actually connected -- its label is only recomputed once, when the hub's root
            // view is built (see Patches/ConnectionButtonPatch.cs's Round 17 doc for why
            // that isn't reliably re-triggered just by connecting and backing out of the
            // connect screen). Refreshing it here every frame instead means it can never
            // drift out of sync with the real connection state again, cheap no-op included
            // (early-returns immediately if the button isn't currently on screen).
            Patches.ConnectionButtonPatch.RefreshLabel();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            // Debug aid for resolving levels.json's duplicate-scene-name caveats -- a
            // real playthrough's log of this is the ground truth for whether
            // "TheyAreYourTools_C_2" etc. really do recur, and in what order.
            LoggerInstance.Msg($"Scene loaded: {sceneName} (buildIndex {buildIndex})");

            // Last-resort safety net, added after a real playtest found a level ("25 -
            // Fall") getting force-loaded regardless of unlock state through some path
            // none of the launch-time gates (LevelGatePatch/ViaAppGatePatch/
            // DirectLevelSkipPatch/TitleCardGatePatch) caught -- root-caused to
            // "22 - Hacker"'s native ending, which is a real, deliberate SUPERHOT twist
            // (it sets storyFinished=true and detours into a credits scene, requiring an
            // actual game restart to continue -- not a bug, just not something any of our
            // gates were watching for). Rather than chase down the exact restart-time
            // load path, this reacts *after* the fact: if the scene that just loaded
            // resolves to one of our tracked, still-locked levels, kick straight back to
            // the hub. Doesn't prevent a brief flash of the wrong level the way the other
            // gates do, but guarantees nothing locked stays actually playable, even
            // through a path we don't yet fully understand.
            LevelInfo current = LevelSetup.CurrentLevelInfo;
            if (current != null && LevelAccessGuard.ShouldBlock(current, out string blockMessage))
            {
                TextManager.AddUptitleToQueue(new LocalizableText(blockMessage));
                LoggerInstance.Msg($"Safety net: scene '{sceneName}' resolved to a locked level " +
                    "that no launch-time gate caught -- kicking back to hub.");

                if (SHGUI.current != null)
                {
                    SHGUI.current.LaunchLevelAppTunnels("SHMenu", false);
                }
            }
        }
    }
}
