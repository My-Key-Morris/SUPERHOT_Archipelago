using System.Collections.Generic;
using System.IO;
using System.Linq;
using MelonLoader;
using Newtonsoft.Json.Linq;

namespace SuperhotArchipelago.Core
{
    public class LevelEntry
    {
        public int Order;
        public string SceneName = "";
        public string DisplayName = "";

        // Real bug found by an actual playthrough: several of these levels reuse the
        // same Unity scene for genuinely different story beats (see the _caveats in
        // data/levels.json -- "piCyberSpace#1_E" x2, "LevelTest#77 HackerRoom" x2,
        // "TheyAreYourTools_C_2" x3), so SceneName above cannot be used to uniquely
        // identify a level at runtime -- looking it up would silently resolve to
        // whichever duplicate happened to load last. LevelId fixes this: it mirrors the
        // real game's own LevelInfo.ID, which LevelSetup.LoadStoryLevels() assigns as a
        // straight index into the Story/Level XML in document order (confirmed by
        // decompiling LevelSetup.cs: AddLevelInfo(list[i], i)) -- i.e. genuinely unique
        // per level instance, duplicates included, and stable across runs.
        //
        // NOT "Order - 1". A real playtest proved that formula wrong -- completing
        // "Kick" (order 1) reported as "Dark Alley Complete" (order 2), and unlocking
        // "Cage Fight" (order 8) visually lit up "Jump" (order 6) in the hub instead.
        // Root cause, confirmed by extracting the real GameData Story/Level XML directly
        // (it's stored as plain readable text inside SH_Data/resources.assets): the
        // real document has 49 <Level> elements total, not our 34 -- LoadStoryLevels()
        // assigns IDs over ALL of them with no filtering, including "SHMenu" and many
        // "_SEGWAYSTUB" dialogue-interlude entries we deliberately exclude from our own
        // catalog. That makes the real ID sequence skip around relative to our 1-34
        // order, sometimes by several positions. LevelId below is read directly from
        // levels.json's "gameId" field (the real extracted index for each entry), not
        // computed, specifically so this can never silently drift out of sync again.
        public int LevelId;

        // Whether this level has an in-level secret console (TerminalActivator,
        // confirmed via decompile). Extracted from the real game's own data -- every
        // level has either 0 or 1, never more (see levels.json's _source note) -- so
        // this is a plain bool rather than a count.
        public bool HasSecret;
    }

    /// <summary>
    /// Loads data/levels.json (shipped next to the mod DLL -- copy of
    /// apworld/superhot/data/levels.json) and reproduces the same id scheme the Python
    /// world uses in Items.py/Locations.py, so this mod can compute the same location
    /// and item codes without a second source of truth for the numbers themselves.
    ///
    /// IMPORTANT: BASE_ID and ITEM_ID_OFFSET below MUST be kept in sync by hand with
    /// apworld/superhot/Items.py -- there's no automated check tying the Python and C#
    /// copies together. If those ever drift, checks/items will silently map to the wrong
    /// level.
    /// </summary>
    public static class LevelCatalog
    {
        public const long BaseId = 3891000;
        public const long ItemIdOffset = 10000;

        // MUST be kept in sync by hand with apworld/superhot/Locations.py's
        // SECRET_LOCATION_OFFSET -- a secret location's real Archipelago id is
        // BaseId + SecretLocationIdOffset + entry.Order, distinct from both the level's
        // own complete-location id (BaseId + entry.Order) and any item id.
        public const long SecretLocationIdOffset = 20000;

        // MUST be kept in sync by hand with apworld/superhot/Items.py's
        // WHITE_SPACE_ITEM_NAME/WHITE_SPACE_ITEM_ID_OFFSET -- the pool's one filler item
        // (real, explicit user request: don't reuse "Level Access: X" as filler, since a
        // player receiving several would read them as real unlocks). Not a level, so it
        // has no LevelEntry of its own -- see ItemManager.ApplyItem for how this id is
        // recognized and handled as a deliberate no-op instead of an "unknown item".
        public const long WhiteSpaceItemId = BaseId + ItemIdOffset + 100;

        public static List<LevelEntry> Levels { get; private set; } = new();
        public static Dictionary<long, LevelEntry> LocationIdToLevel { get; private set; } = new();
        public static Dictionary<long, LevelEntry> ItemIdToLevel { get; private set; } = new();

        // Reverse lookup for a level's *secret* location id (BaseId + SecretLocationIdOffset
        // + entry.Order), distinct from LocationIdToLevel above which only covers the main
        // completion location range. Added for the Notifications feature's history resync
        // (Core/LocationManager.cs's OnConnected) -- given a raw checked location id from
        // Session.Locations.AllLocationsChecked, this is what tells "the main completion
        // check for level X" apart from "the secret check for level X" so the right log
        // text ("Sent X" vs "Sent X Secret") can be rebuilt.
        public static Dictionary<long, LevelEntry> SecretLocationIdToLevel { get; private set; } = new();

        // Kept for logging/reference only -- do NOT use this for gating decisions, see
        // LevelEntry.LevelId's comment above for why (duplicate scene names make this
        // dictionary lossy, last-one-wins). Real gating uses LevelIdToLevel instead.
        public static Dictionary<string, LevelEntry> SceneNameToLevel { get; private set; } = new();

        // The real join key. Keyed by the real game's LevelInfo.ID (== LevelEntry.LevelId
        // == Order - 1), which is unique per level instance even when SceneName repeats.
        public static Dictionary<int, LevelEntry> LevelIdToLevel { get; private set; } = new();

        public static void Load(MelonLogger.Instance log)
        {
            string path = Path.Combine(Path.GetDirectoryName(typeof(LevelCatalog).Assembly.Location)!, "data", "levels.json");
            if (!File.Exists(path))
            {
                log.Error($"levels.json not found at '{path}' -- location/item id mapping will be empty.");
                return;
            }

            JObject root = JObject.Parse(File.ReadAllText(path));
            foreach (JToken token in root["levels"]!)
            {
                var entry = new LevelEntry
                {
                    Order = token["order"]!.Value<int>(),
                    SceneName = token["id"]!.Value<string>()!,
                    DisplayName = token["name"]!.Value<string>()!,
                    LevelId = token["gameId"]!.Value<int>(),
                    HasSecret = token["hasSecret"]?.Value<bool>() ?? false,
                };
                Levels.Add(entry);

                long locationId = BaseId + entry.Order;
                long itemId = BaseId + ItemIdOffset + entry.Order;
                LocationIdToLevel[locationId] = entry;
                ItemIdToLevel[itemId] = entry;

                if (entry.HasSecret)
                {
                    long secretLocationId = BaseId + SecretLocationIdOffset + entry.Order;
                    SecretLocationIdToLevel[secretLocationId] = entry;
                }

                // NOTE: because of the duplicate scene names flagged in levels.json's
                // _caveats (e.g. "TheyAreYourTools_C_2" appearing at orders 13/16/19),
                // this dictionary can only hold one LevelEntry per scene name -- last one
                // wins. Reference/logging only -- see LevelIdToLevel for the real join key.
                SceneNameToLevel[entry.SceneName] = entry;
                LevelIdToLevel[entry.LevelId] = entry;
            }

            log.Msg($"LevelCatalog loaded {Levels.Count} levels from '{path}'.");
        }

        /// <summary>
        /// This mod's own short display name for an item id, if it's one we recognize
        /// (a level access item, or the White Space filler) -- null otherwise. Real,
        /// explicit user request: notification text (Core/LocationManager.cs's "Sent"
        /// side) was running long enough to get truncated/cut off mid-word on the AP
        /// LOG screen -- the AP-side item name for a level access item includes a
        /// "Level Access: " prefix (apworld/superhot/Items.py) that's redundant once
        /// it's already shown as a "Sent"/"Received" line, so this trades that prefix
        /// for this mod's own already-short LevelEntry.DisplayName ("29 - Train"
        /// instead of "Level Access: 29 - Train") -- same "prefer our own catalog name
        /// over AP's own dynamically-fetched one" preference already used everywhere
        /// else in this codebase (e.g. Core/ItemManager.cs's ApplyItem). Callers should
        /// fall back to the AP-provided name (e.g. ScoutedItemInfo.ItemDisplayName) when
        /// this returns null -- covers Victory or any item outside our own catalog.
        /// </summary>
        public static string? TryGetShortItemDisplayName(long itemId)
        {
            if (itemId == WhiteSpaceItemId)
            {
                return "White Space";
            }

            return ItemIdToLevel.TryGetValue(itemId, out LevelEntry? level) ? level.DisplayName : null;
        }
    }
}
