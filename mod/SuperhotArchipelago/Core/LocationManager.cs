using MelonLoader;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Maps in-game level-complete events to Archipelago location checks. Harmony
    /// patches (see ../Patches/LevelCompletePatch.cs) call CheckLocation(levelId) when
    /// LevelSetup.UnlockNextLevel() fires; this class doesn't know or care how that
    /// detection happens, it just needs the level id (the real game's LevelInfo.ID) to
    /// look up via LevelCatalog.LevelIdToLevel. Real bug found by testing: matching by
    /// scene name instead used to silently fail for levels that reuse a scene (e.g.
    /// "Cyberspace (1)" never linked to anything) -- see LevelCatalog.LevelEntry.
    /// </summary>
    public class LocationManager
    {
        private readonly ArchipelagoConnection _connection;
        private readonly MelonLogger.Instance _log;

        public LocationManager(ArchipelagoConnection connection, MelonLogger.Instance log)
        {
            _connection = connection;
            _log = log;
        }

        public void CheckLocation(int levelId)
        {
            if (_connection.Session == null || !_connection.IsConnected)
            {
                _log.Warning($"CheckLocation({levelId}) called before connecting -- ignored.");
                return;
            }

            if (!LevelCatalog.LevelIdToLevel.TryGetValue(levelId, out LevelEntry? level))
            {
                _log.Warning($"Unknown level id {levelId} -- no matching entry in levels.json. " +
                              "Either a level outside our catalog just finished, or levels.json's " +
                              "order/id list is out of sync with the real game.");
                return;
            }

            // Real, explicit user request: the final level shouldn't generate its own
            // real, checkable location at all -- "that's ending the game, and all checks
            // should be released anyways" (a real fillable check behind "beat the entire
            // game" is bad multiworld UX for anyone whose own progression depends on it).
            // apworld/superhot/Locations.py no longer creates a location for it, so
            // sending a CompleteLocationChecks for it here would just be a check for a
            // location id the server doesn't know about. Goal completion is still
            // reported below regardless, via the separate SetGoalAchieved signal.
            if (level.Order != LevelCatalog.Levels.Count)
            {
                long locationId = LevelCatalog.BaseId + level.Order;
                _connection.Session.Locations.CompleteLocationChecks(locationId);
                _log.Msg($"Sent check for '{level.DisplayName}' (level id {levelId}, location id {locationId}).");
            }

            // Real bug found by an actual test run: sending the final level's location
            // check is NOT the same as telling the server the player has won. AP's
            // "Victory" event item (apworld/superhot/Items.py) has no real network id, so
            // it never arrives as a received item for the mod to react to -- goal
            // completion is a separate signal the client has to send explicitly
            // (confirmed by decompiling Archipelago.MultiClient.Net.dll:
            // ArchipelagoSession.SetGoalAchieved(), which sends a StatusUpdatePacket with
            // ArchipelagoClientState.ClientGoal). The apworld's Rules.py gates the
            // Victory location on having the *last* level's access item, which in
            // practice means "the last level in levels.json order got completed" -- so
            // that's the trigger here, confirmed correct by an actual playthrough where
            // finishing the level in this position was in fact the game's real ending.
            if (level.Order == LevelCatalog.Levels.Count)
            {
                _connection.Session.SetGoalAchieved();
                _log.Msg($"'{level.DisplayName}' was the final level -- reported goal achieved.");
            }
        }

        /// <summary>
        /// Reports finding a level's secret console (see Patches/SecretFoundPatch.cs for
        /// the Harmony hook that detects this). Uses a distinct location id range from
        /// the level's own completion check -- see LevelCatalog.SecretLocationIdOffset --
        /// so the two are separate, independently checkable Archipelago locations, exactly
        /// matching apworld/superhot/Locations.py's secret_location_name entries.
        /// </summary>
        public void CheckSecretLocation(int levelId)
        {
            if (_connection.Session == null || !_connection.IsConnected)
            {
                _log.Warning($"CheckSecretLocation({levelId}) called before connecting -- ignored.");
                return;
            }

            if (!LevelCatalog.LevelIdToLevel.TryGetValue(levelId, out LevelEntry? level))
            {
                _log.Warning($"Unknown level id {levelId} for a secret find -- no matching entry in " +
                              "levels.json. Either a level outside our catalog has a secret we don't " +
                              "know about, or levels.json's order/id list is out of sync with the real game.");
                return;
            }

            if (!level.HasSecret)
            {
                _log.Warning($"Secret found in '{level.DisplayName}', but levels.json says this level " +
                              "has none -- sending the check anyway, but this likely means levels.json's " +
                              "hasSecret is out of sync with the real game.");
            }

            long locationId = LevelCatalog.BaseId + LevelCatalog.SecretLocationIdOffset + level.Order;
            _connection.Session.Locations.CompleteLocationChecks(locationId);
            _log.Msg($"Sent secret check for '{level.DisplayName}' (level id {levelId}, location id {locationId}).");
        }

        /// <summary>
        /// Whether this level's location check has actually been sent and confirmed --
        /// distinct from "unlocked" (see Core/UnlockState.cs), which only means the
        /// access item was received, not that the level has been played yet. Used by
        /// Patches/HubUnlockPatch.cs to tell "unlocked but not yet completed" (grey) apart
        /// from "unlocked and completed" (white) -- a real, explicit cosmetic request.
        ///
        /// Deliberately reads the live Archipelago session instead of keeping a second
        /// local set: Session.Locations.AllLocationsChecked (confirmed via decompiling
        /// Archipelago.MultiClient.Net.dll's LocationCheckHelper) is populated from the
        /// server's own ConnectedPacket on every connect/reconnect, and updated instantly
        /// -- both locally the moment CompleteLocationChecks() is called above, and via
        /// the server's RoomUpdatePacket echo. That makes it the authoritative source of
        /// truth, survives a full game restart without us tracking anything ourselves,
        /// and can't drift out of sync with what the server actually has recorded.
        /// </summary>
        public bool IsLevelCompleted(int levelId)
        {
            if (_connection.Session == null || !_connection.IsConnected)
            {
                return false;
            }

            if (!LevelCatalog.LevelIdToLevel.TryGetValue(levelId, out LevelEntry? level))
            {
                return false;
            }

            // The final level has no completion location to read here anymore (see
            // CheckLocation above) -- there's no server-authoritative "completed" signal
            // left to distinguish "unlocked but not yet played" from "unlocked and
            // played" for it, the same way level 1 has no access rule to distinguish
            // "locked" from "unlocked". Rather than invent fragile local state that
            // wouldn't survive a restart and would fight the "server is the source of
            // truth" design used everywhere else here, it's just treated as completed as
            // soon as it's unlocked -- HubUnlockPatch.cs shows it white, not grey, the
            // moment the access item is received.
            if (level.Order == LevelCatalog.Levels.Count)
            {
                return UnlockState.IsUnlocked(levelId);
            }

            long locationId = LevelCatalog.BaseId + level.Order;
            return _connection.Session.Locations.AllLocationsChecked.Contains(locationId);
        }

        /// <summary>
        /// Same idea as IsLevelCompleted, but for a level's secret console location
        /// (see CheckSecretLocation above) instead of its main completion location.
        ///
        /// Real bug found by playtesting: the hub's native "CRACKED!" badge/description
        /// text (LevelInfo.SecretsFound(), confirmed via decompile) reads straight from
        /// SUPERHOT's own save data (SaveManager key SceneFileName + secretIndex +
        /// "unlocked"), completely independent of anything Archipelago has actually
        /// tracked this run. A save file with leftover flags from an earlier/different
        /// playthrough shows "CRACKED!" for a secret the current multiworld run has never
        /// had checked -- exactly the class of bug IsLevelCompleted above already solves
        /// for main level completion, using the exact same fix: trust the live
        /// Archipelago session instead of native save state. See
        /// Patches/HubUnlockPatch.cs for where this is used to correct the hub display.
        /// </summary>
        public bool IsSecretCompleted(int levelId)
        {
            if (_connection.Session == null || !_connection.IsConnected)
            {
                return false;
            }

            if (!LevelCatalog.LevelIdToLevel.TryGetValue(levelId, out LevelEntry? level))
            {
                return false;
            }

            long locationId = LevelCatalog.BaseId + LevelCatalog.SecretLocationIdOffset + level.Order;
            return _connection.Session.Locations.AllLocationsChecked.Contains(locationId);
        }

        /// <summary>
        /// How many of the other 31 story levels (every tracked level except "34 -
        /// Free" itself) have actually had their completion check sent this run.
        /// Used by Core/LevelAccessGuard.cs to gate entry to Free, and by
        /// Patches/HubUnlockPatch.cs to show live progress on its hub button -- see
        /// both for the real, explicit user request this exists for. Deliberately
        /// excludes Free's own completion (IsLevelCompleted's special-cased "treated
        /// as completed the moment it's unlocked" for it -- see that method's own
        /// comment) so playing Free could never count toward its own requirement.
        /// </summary>
        public int CountOtherLevelsCompleted()
        {
            int count = 0;
            foreach (LevelEntry level in LevelCatalog.Levels)
            {
                if (level.Order == LevelCatalog.Levels.Count)
                {
                    continue;
                }

                if (IsLevelCompleted(level.LevelId))
                {
                    count++;
                }
            }
            return count;
        }
    }
}
