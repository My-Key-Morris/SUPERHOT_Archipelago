using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;

namespace SuperhotArchipelago.Core
{
    public class LevelEntry
    {
        public int Order;
        public string SceneName = "";
        public string DisplayName = "";

        // Several levels reuse the same Unity scene for different story beats, so SceneName
        // can't uniquely identify a level at runtime. LevelId mirrors the real game's
        // LevelInfo.ID instead (a stable per-instance index), read directly from levels.json's
        // "gameId" field rather than computed as "Order - 1", since the real Story/Level XML
        // has 49 entries (including SHMenu and segway-stub interludes we exclude), so the ID
        // sequence doesn't line up with our 1-34 order.
        public int LevelId;

        // Whether this level has an in-level secret console (TerminalActivator). Every level
        // has either 0 or 1, never more, so this is a plain bool rather than a count.
        public bool HasSecret;
    }

    /// <summary>
    /// Loads data/levels.json (a copy of apworld/superhot/data/levels.json) and reproduces
    /// the same id scheme the Python world uses in Items.py/Locations.py.
    /// BaseId and ItemIdOffset below MUST be kept in sync by hand with apworld/superhot/Items.py --
    /// no automated check ties the two together, and drift silently maps checks/items to the wrong level.
    /// </summary>
    public static class LevelCatalog
    {
        public const long BaseId = 3891000;
        public const long ItemIdOffset = 10000;

        // MUST be kept in sync by hand with apworld/superhot/Locations.py's SECRET_LOCATION_OFFSET.
        public const long SecretLocationIdOffset = 20000;

        // MUST be kept in sync by hand with apworld/superhot/Items.py's WHITE_SPACE_ITEM_ID_OFFSET.
        // The pool's one filler item, not a level, so it has no LevelEntry -- see
        // ItemManager.ApplyItem for how this id is handled as a deliberate no-op.
        public const long WhiteSpaceItemId = BaseId + ItemIdOffset + 100;

        public static List<LevelEntry> Levels { get; private set; } = new();
        public static Dictionary<long, LevelEntry> LocationIdToLevel { get; private set; } = new();
        public static Dictionary<long, LevelEntry> ItemIdToLevel { get; private set; } = new();

        // Reverse lookup for a level's *secret* location id, distinct from LocationIdToLevel's
        // main completion range. Lets LocationManager.OnConnected tell "main check for level X"
        // apart from "secret check for level X" when rebuilding history from a raw location id.
        public static Dictionary<long, LevelEntry> SecretLocationIdToLevel { get; private set; } = new();

        // The real join key. Keyed by the real game's LevelInfo.ID (LevelEntry.LevelId), which
        // is unique per level instance even when SceneName repeats.
        public static Dictionary<int, LevelEntry> LevelIdToLevel { get; private set; } = new();

        public static void Load(ManualLogSource log)
        {
            string path = Path.Combine(Path.GetDirectoryName(typeof(LevelCatalog).Assembly.Location)!, "data", "levels.json");
            if (!File.Exists(path))
            {
                log.LogError($"levels.json not found at '{path}' -- location/item id mapping will be empty.");
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

                LevelIdToLevel[entry.LevelId] = entry;
            }

            log.LogInfo($"LevelCatalog loaded {Levels.Count} levels from '{path}'.");
        }

        /// <summary>
        /// This mod's own short display name for an item id, if recognized (a level access
        /// item or the White Space filler) -- null otherwise. Drops AP's "Level Access: "
        /// prefix to avoid truncation in notification text; callers should fall back to the
        /// AP-provided name when this returns null.
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
