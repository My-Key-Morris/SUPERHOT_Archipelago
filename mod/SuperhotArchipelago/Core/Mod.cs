using MelonLoader;

// CONFIRMED against the real install's SH_Data/app.info, which literally contains these
// two strings on their own lines.
[assembly: MelonInfo(typeof(SuperhotArchipelago.Core.Mod), "SuperhotArchipelago", "0.1.1", "Michael")]
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
        // writes, and SetEnabled's one, below, so the OnEntryValueChanged-driven
        // auto-apply (still wired up for the original "hand-edit MelonPreferences.cfg"
        // workflow) doesn't fire a second, redundant connect/disconnect attempt for one
        // user action -- only the explicit call at the end of each of those methods
        // actually connects/disconnects.
        private static bool _suppressAutoReconnect;

        // Real, explicit user request: a way to switch between playing through
        // Archipelago and playing normally without uninstalling/reinstalling the mod.
        // Every patch that gates level access, overlays hub visuals, or reports checks
        // reads this (directly or via LevelAccessGuard.ShouldBlock, which every gating
        // patch already funnels through) and skips its own work entirely when false, so
        // SUPERHOT plays exactly like vanilla -- see Patches/ArchipelagoModeTogglePatch.cs
        // for the hub button that flips this, and NOTES.md for the full list of what
        // turning this off actually touches.
        public static bool IsEnabled => Config.Enabled.Value;

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

        // Called by Patches/ArchipelagoModeTogglePatch.cs's hub button. Same
        // explicit-set-then-apply shape as ApplyConnectionSettingsAndConnect below, and
        // for the same reason: suppresses the OnEntryValueChanged-driven path so this one
        // click doesn't also trigger a second, redundant connect/disconnect attempt.
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

        // Shared by SetEnabled above (the hub button) and the OnEntryValueChanged
        // subscription in OnInitializeMelon (a direct MelonPreferences.cfg edit while the
        // game is running -- same parallel path Server/Slot/Password already support).
        // Real, explicit user request: turning this off should actually drop any live
        // connection, not just stop enforcing gates locally while the socket stays open
        // -- and turning it back on should reconnect (which replays this slot's full item
        // history, per Archipelago.MultiClient.Net's own AllItems handling, so
        // UnlockState.cs ends up exactly where it left off with no extra bookkeeping
        // needed here).
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

                // Real, explicit user request, found while designing the AP MODE toggle:
                // submitting this screen is a clear enough signal of intent that it should
                // also flip Archipelago mode back on if it was off -- otherwise TryConnect()
                // below silently no-ops on its own !Config.Enabled.Value check, and a player
                // who forgot they'd turned it off would see nothing happen with no obvious
                // reason why.
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
            // Network item receives happen off Unity's main thread; ItemManager queues
            // them and this drains the queue once per frame on the main thread, where
            // it's safe to touch Unity/game state.
            Items?.ProcessQueue();

            // Scout results (see LocationManager.cs's own comment on _pendingNotifications)
            // resolve off Unity's main thread -- this is what turns them into real
            // NotificationLog entries on the main thread, same shape as Items.ProcessQueue()
            // above.
            Locations?.ProcessPendingNotifications();

            // See NotificationLog.cs's own comment on _pendingPopups for why popups
            // aren't dispatched the instant they're queued -- this is what actually
            // flushes them once it's safe to.
            NotificationLog.FlushPendingPopups();

            // Real bug report: the hub's ARCHIPELAGO button sometimes showed OFFLINE while
            // actually connected -- its label is only recomputed once, when the hub's root
            // view is built (see Patches/ConnectionButtonPatch.cs's Round 17 doc for why
            // that isn't reliably re-triggered just by connecting and backing out of the
            // connect screen). Refreshing it here every frame instead means it can never
            // drift out of sync with the real connection state again, cheap no-op included
            // (early-returns immediately if the button isn't currently on screen).
            Patches.ConnectionButtonPatch.RefreshLabel();

            // Same reasoning as the line above, for the AP MODE toggle button -- its
            // state can also change from outside its own click handler (a direct
            // MelonPreferences.cfg edit while running), so it's re-derived every frame
            // rather than only right after creation.
            Patches.ArchipelagoModeTogglePatch.RefreshLabel();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            // Debug aid for resolving levels.json's duplicate-scene-name caveats -- a
            // real playthrough's log of this is the ground truth for whether
            // "TheyAreYourTools_C_2" etc. really do recur, and in what order.
            LoggerInstance.Msg($"Scene loaded: {sceneName} (buildIndex {buildIndex})");

            // Real bug found by a live playtest of the AP mode toggle: Patches/HubUnlockPatch.cs
            // (per-level lock/color/badge visuals) was silently never running at all -- not a
            // regression from the toggle feature itself, but a pre-existing, latent issue this
            // was the first time anyone actually verified HubUnlockPatch live in a running game
            // in a while. Root cause, confirmed by reading this exact save file directly:
            // "storyFinished" was already true on disk, almost certainly written before
            // Patches/StoryFinishedSuppressPatch.cs ever existed to stop it (that patch only
            // blocks *new* writes of true -- it does nothing about a value already saved).
            // Confirmed via decompile (see StoryFinishedSuppressPatch.cs's own docstring):
            // piOsMenu's "storylevels" case only calls piOsMenu.LockUnfinishedLevels() -- the
            // method HubUnlockPatch.cs hooks -- when storyFinished is false. With it stuck true,
            // that method (and therefore HubUnlockPatch's entire Postfix) never gets called at
            // all, leaving every level exactly as native's own last real pass left it -- which,
            // combined with this save's "highestfinishedLevel" also already being maxed out from
            // extensive past testing, meant every level rendered as native "already finished"
            // (clean text, white) instead of AP's own scrambled/locked look. The actual launch
            // gate (LevelAccessGuard.ShouldBlock, called independently at click time) was never
            // affected by any of this -- it doesn't depend on LockUnfinishedLevels() running at
            // all -- which is exactly why genuinely locked levels still correctly refused to
            // launch even while looking wrongly unlocked.
            //
            // Fixed by actively resetting the flag (not just suppressing future writes) the
            // moment it's found true while Archipelago mode is on -- scoped to IsEnabled so
            // vanilla play (mode off) is never touched: a player genuinely finishing the game
            // vanilla should still get the real ending behavior. SetValue(false) here passes
            // straight through StoryFinishedSuppressPatch's own Prefix untouched (it only
            // intercepts writes of true), so this doesn't fight that patch.
            if (IsEnabled && SaveManager.Instance != null && SaveManager.Instance.GetValueAs("storyFinished", false))
            {
                LoggerInstance.Msg("Found 'storyFinished' already true on disk (likely saved before this " +
                    "mod suppressed it) -- resetting to false so the hub's per-level lock pass can run again.");
                SaveManager.Instance.SetValue("storyFinished", false);
            }

            // Real bug report: SUPERHOT's own "all secrets found" Steam achievement
            // (TerminalActivator.CheckAllSecretsAchievement(), confirmed via decompile)
            // kept firing on nearly every new secret found this run, despite the player
            // genuinely not having found all of them in this Archipelago run. Root
            // cause, confirmed via decompile of TerminalActivator.OnActivate and
            // LevelInfo.SecretsFound: that check reads each of the game's 34 levels' own
            // native per-secret save flag (SceneFileName + "1unlocked" -- "1" because
            // every level has either 0 or 1 secrets, never more, see
            // LevelCatalog.LevelEntry.HasSecret's own comment) directly, completely
            // independent of Archipelago's own tracking. On a save file with a long
            // testing history (same root problem as the storyFinished fix above), most
            // of those flags were already true from earlier playthroughs unrelated to
            // the current AP run, so finding almost any new secret this run made the
            // native "all levels' own flags happen to be true" check pass by accident.
            // The hub's own "CRACKED!" badge already ignores these stale flags entirely
            // (Patches/HubUnlockPatch.cs reads LocationManager.IsSecretCompleted()
            // instead of trusting them) -- but this native achievement check isn't
            // something the mod patches at all, so nothing was correcting the
            // underlying save data it reads. Fixed the same way as storyFinished:
            // actively resync every tracked secret-bearing level's native flag to match
            // IsSecretCompleted() on every scene load (both directions -- also fixes a
            // secret genuinely checked earlier in this multiworld from ever displaying
            // as "not cracked" on a fresh local save), so this native check -- and any
            // other native code reading the same save keys -- can never see a stale
            // "found" state that doesn't match the current run. Safe to do unconditionally
            // here: this only ever runs once per fresh scene load, before any in-level
            // interaction, so it can never race with or revert a genuine find made
            // during the level visit that's ending right as this runs.
            if (IsEnabled && SaveManager.Instance != null && Locations != null)
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
