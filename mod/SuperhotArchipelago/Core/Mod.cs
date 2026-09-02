using System;
using MelonLoader;

// Matches the strings in SH_Data/app.info exactly.
[assembly: MelonInfo(typeof(SuperhotArchipelago.Core.Mod), "SuperhotArchipelago", "0.1.1", "Michael")]
[assembly: MelonGame("SUPERHOT_Team", "SUPERHOT")]

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Mod entry point. Lifecycle: OnEarlyInitializeMelon -> OnInitializeMelon -> OnLateInitializeMelon,
    /// then OnUpdate every frame. Level completion is detected via Harmony patches (see ../Patches/),
    /// not OnSceneWasLoaded, since SUPERHOT reuses scene names across multiple story beats.
    /// </summary>
    public class Mod : MelonMod
    {
        public static ArchipelagoConnection? Connection { get; private set; }
        public static LocationManager? Locations { get; private set; }
        public static ItemManager? Items { get; private set; }

        // Static so Harmony patches (static classes/methods, no MelonMod instance of their own)
        // can still log through the same MelonLoader console.
        public static MelonLogger.Instance? Log { get; private set; }

        // Static self-reference so ArchipelagoConnectApp.cs (no MelonMod instance of its own)
        // can reach TryConnect() via ApplyConnectionSettingsAndConnect.
        private static Mod? _instance;

        // Set during ApplyConnectionSettingsAndConnect's/SetEnabled's Config.*.Value writes so the
        // OnEntryValueChanged auto-reconnect doesn't also fire a redundant connect/disconnect.
        private static bool _suppressAutoReconnect;

        // Lets players switch between Archipelago and vanilla play without reinstalling the mod.
        // Every gating/overlay/report patch checks this (directly or via LevelAccessGuard.ShouldBlock)
        // and no-ops when false; see Patches/ArchipelagoModeTogglePatch.cs for the hub toggle.
        public static bool IsEnabled => Config.Enabled.Value;

        // IsEnabled only reflects config intent, not whether a connection actually succeeded (a
        // reconnect can fail silently and leave the toggle on). The save-data resync blocks in
        // OnSceneWasLoaded need this instead, so they don't run against a null Session and
        // overwrite real save data with false "not completed" reads.
        private static bool IsFullyConnected => Connection != null && Connection.IsConnected;

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

            if (!Config.Enabled.Value)
            {
                LoggerInstance.Msg("Not connecting yet: Archipelago mode is turned off. Click " +
                    "the AP MODE icon on the hub's main screen to turn it back on.");
            }
            else if (Config.IsConfigured)
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
            Config.Enabled.OnEntryValueChanged.Subscribe((_, _) => ApplyEnabledChange());
        }

        private void TryConnect()
        {
            if (_suppressAutoReconnect || !Config.Enabled.Value || !Config.IsConfigured)
            {
                return;
            }

            LoggerInstance.Msg($"Connecting to '{Config.Server.Value}' as '{Config.Slot.Value}'...");
            Connection?.Connect(Config.Server.Value, Config.Slot.Value,
                Config.Password.Value == "" ? null : Config.Password.Value);
        }

        // Called by the hub's AP MODE button. Suppresses the OnEntryValueChanged auto-apply
        // the same way ApplyConnectionSettingsAndConnect does, so one click doesn't also
        // trigger a redundant connect/disconnect.
        public static void SetEnabled(bool enabled)
        {
            if (_instance == null)
            {
                return;
            }

            _suppressAutoReconnect = true;
            try
            {
                Config.Enabled.Value = enabled;
                Config.Save();
            }
            finally
            {
                _suppressAutoReconnect = false;
            }

            ApplyEnabledChange();
        }

        // Shared by SetEnabled (hub button) and the OnEntryValueChanged subscription (a live
        // MelonPreferences.cfg edit). Turning off drops any live connection rather than just
        // stopping local gating; turning back on reconnects, which replays this slot's full
        // item history so UnlockState ends up correct with no extra bookkeeping.
        private static void ApplyEnabledChange()
        {
            if (_suppressAutoReconnect)
            {
                return;
            }

            if (Config.Enabled.Value)
            {
                Log?.Msg("Archipelago mode turned ON.");
                _instance?.TryConnect();
            }
            else
            {
                Log?.Msg("Archipelago mode turned OFF -- SUPERHOT will play like vanilla until it's turned back on.");
                Connection?.Disconnect();
            }
        }

        // Called when the player submits the in-game connect screen. Sets Server/Slot/Password
        // and connects once under _suppressAutoReconnect, so one button press doesn't become
        // three sequential connect attempts.
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

                // Submitting this screen also turns Archipelago mode back on if it was off,
                // otherwise TryConnect() below would silently no-op.
                Config.Enabled.Value = true;

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
            // Each call wrapped individually rather than one try/catch around the whole method,
            // so a step that throws is identified by name in the log instead of just "somewhere
            // in OnUpdate" -- added after "Round 48"'s cold-boot freeze investigation showed how
            // easily a silent failure here (or in a Postfix a step calls into) can be mistaken
            // for a hang with zero diagnostic trail.
            RunStep("Items.ProcessQueue", () => Items?.ProcessQueue());
            RunStep("Locations.ProcessPendingNotifications", () => Locations?.ProcessPendingNotifications());
            RunStep("NotificationLog.FlushPendingPopups", NotificationLog.FlushPendingPopups);
            RunStep("PopupOverlay.Update", PopupOverlay.Update);
            RunStep("PopupOverlay.DebugHotkey", CheckPopupDebugHotkey);
            RunStep("ConnectionButtonPatch.RefreshLabel", Patches.ConnectionButtonPatch.RefreshLabel);
            RunStep("ArchipelagoModeTogglePatch.RefreshLabel", Patches.ArchipelagoModeTogglePatch.RefreshLabel);
        }

        // Debug aid for live-tuning PopupOverlay's look (see PopupTuning.cs's own doc comment
        // for why this exists): F9 tears down the current Canvas, reloads PopupTuning.json from
        // disk, and shows a sample string covering mixed case/digits/punctuation, so edits to
        // that file can be eyeballed in-game without a rebuild + redeploy + restart per change.
        private static void CheckPopupDebugHotkey()
        {
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F9))
            {
                PopupOverlay.ReloadTuning();
                PopupOverlay.Show("LOCKED: 'Sample Level 42' needs an Archipelago item to unlock");
            }
        }

        private void RunStep(string name, Action step)
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"[debug] OnUpdate step '{name}' threw: {ex}");
            }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            // Debug aid for confirming levels.json's duplicate-scene-name caveats against
            // a real playthrough's actual scene order.
            LoggerInstance.Msg($"Scene loaded: {sceneName} (buildIndex {buildIndex})");

            // Wrapped in try/catch so one bad section (or an exception from a Postfix a section
            // calls into, e.g. HubUnlockPatch) can't silently abort the rest of scene-load
            // handling -- added after "Round 48"'s cold-boot freeze investigation, which spent
            // a long time ruling out exactly this kind of silent failure before finding the
            // real cause (see PopupOverlay.EnsureView's comment).
            try
            {
                RunSceneWasLoadedBody(sceneName);
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"OnSceneWasLoaded('{sceneName}') threw: {ex}");
            }
        }

        private void RunSceneWasLoadedBody(string sceneName)
        {
            // storyFinished is left alone to behave exactly like vanilla -- do not force it
            // false to keep HubUnlockPatch running, since that also breaks the native MODS
            // folder's own unlock check. See Patches/StoryLevelsUnlockPatch.cs for the real fix.

            // Resyncs each tracked secret-bearing level's native save flag (SceneFileName +
            // "1unlocked") to match IsSecretCompleted() on every scene load, since SUPERHOT's
            // native "all secrets" achievement check reads that flag directly and ignores
            // Archipelago's own tracking -- stale true flags from earlier playthroughs would
            // otherwise trigger it early. Safe unconditionally: this runs before any
            // in-level interaction, so it can't race with a genuine find.
            if (IsEnabled && IsFullyConnected && SaveManager.Instance != null && Locations != null)
            {
                foreach (LevelEntry entry in LevelCatalog.Levels)
                {
                    if (!entry.HasSecret)
                    {
                        continue;
                    }

                    bool actuallyCompleted = Locations.IsSecretCompleted(entry.LevelId);
                    SaveManager.Instance.SetValue(entry.SceneName + "1unlocked", actuallyCompleted);
                }
            }

            // Last-resort net for load paths none of the launch-time gates catch (e.g. "22 -
            // Hacker"'s native ending forcing a restart into "25 - Fall"): if the scene that
            // just loaded resolves to one of our tracked, still-locked levels, kick back to
            // the hub. May flash the wrong level briefly, but guarantees nothing locked stays playable.
            //
            // Real bug found live (first-ever cold-boot test of PopupOverlay): LevelSetup
            // .CurrentLevelInfo, when the scene that just loaded IS "SHMenu" itself, resolves
            // forward (via LevelAccessGuard's ResolveToTrackedLevel) to the catalog's first
            // real level, which isn't the raw scene asked about -- so ShouldBlock's untracked-
            // passthrough branch fires unconditionally ("Return to hub to continue") every time
            // the hub loads. LaunchLevelAppTunnels("SHMenu", ...) then tries to tunnel from the
            // hub to itself, which never completes -- confirmed via a stuck black screen (the
            // native tunnel-transition effect frozen mid-warp) on every single boot, logged as
            // "Safety net: scene 'SHMenu' resolved to a locked level" going back to Aug 9. This
            // was previously misjudged as a harmless quirk (ruled out only as a cause of the
            // "Round 45" click regression, never actually confirmed benign on its own). Skipping
            // the whole net for "SHMenu" is correct: this check exists to catch a real LEVEL
            // scene slipping through locked, and the hub is never itself a locked level.
            LevelInfo current = LevelSetup.CurrentLevelInfo;
            if (sceneName != "SHMenu" && current != null && LevelAccessGuard.ShouldBlock(current, out string blockMessage))
            {
                PopupOverlay.Show(blockMessage);
                LoggerInstance.Msg($"Safety net: scene '{sceneName}' resolved to a locked level " +
                    "that no launch-time gate caught -- kicking back to hub.");

                if (SHGUI.current != null)
                {
                    SHGUI.current.LaunchLevelAppTunnels("SHMenu", false);
                }
            }

            // TextManager recreates its own text views on every scene load (see
            // PopupOverlay.OnSceneLoaded's doc comment for why this class matches that).
            PopupOverlay.OnSceneLoaded();
        }
    }
}
