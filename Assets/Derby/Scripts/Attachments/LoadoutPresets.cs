using UnityEngine;

namespace CarWreck.Derby
{
    /// <summary>
    /// Saved builds: <see cref="Count"/> slots per vehicle, each a full <see cref="Loadout"/> kept in PlayerPrefs under "carwreck.preset.v1.&lt;vehicleKey&gt;.&lt;index&gt;".
    /// Same format, version handling and "unknown ids are kept, not dropped" rule as the current loadout (see <see cref="LoadoutStore"/>).
    /// </summary>
    public static class LoadoutPresets
    {
        public const int Count = 3;
        public const string KeyPrefix = "carwreck.preset.v1.";

        public static string KeyFor(string vehicleKey, int index) => $"{KeyPrefix}{vehicleKey}.{index}";

        public static LoadoutStore.ParseResult Load(string vehicleKey, int index, out Loadout preset)
        {
            if (index < 0 || index >= Count) { preset = new Loadout(vehicleKey); return LoadoutStore.ParseResult.Missing; }
            return LoadoutStore.TryParse(PlayerPrefs.GetString(KeyFor(vehicleKey, index), ""), vehicleKey, out preset);
        }

        public static bool Exists(string vehicleKey, int index) => index >= 0 && index < Count && PlayerPrefs.HasKey(KeyFor(vehicleKey, index));

        public static void Save(string vehicleKey, int index, Loadout loadout)
        {
            if (index < 0 || index >= Count || loadout == null) return;
            var copy = loadout.Clone(); copy.vehicleKey = vehicleKey;
            PlayerPrefs.SetString(KeyFor(vehicleKey, index), LoadoutStore.ToJson(copy));
            PlayerPrefs.Save();
        }

        public static void Delete(string vehicleKey, int index) { if (index >= 0 && index < Count) PlayerPrefs.DeleteKey(KeyFor(vehicleKey, index)); }
    }
}
