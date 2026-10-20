using UnityEngine;

namespace CarWreck.Derby
{
    /// <summary>Saves one loadout per vehicle in PlayerPrefs as JSON under "carwreck.loadout.v1.&lt;vehicleKey&gt;". Switching bodies restores that body's own build.</summary>
    public static class LoadoutStore
    {
        public const string KeyPrefix = "carwreck.loadout.v1.";

        public enum ParseResult { Ok, Missing, Corrupt, UnknownVersion }

        public static string KeyFor(string vehicleKey) => KeyPrefix + vehicleKey;

        public static string ToJson(Loadout l) { l.Normalize(); return JsonUtility.ToJson(l); }

        /// <summary>Parses saved JSON. Anything unreadable or from another version yields an empty loadout for the vehicle plus a result code the UI can show.</summary>
        public static ParseResult TryParse(string json, string vehicleKey, out Loadout loadout)
        {
            loadout = new Loadout(vehicleKey);
            if (string.IsNullOrEmpty(json)) return ParseResult.Missing;
            Loadout parsed;
            try { parsed = JsonUtility.FromJson<Loadout>(json); }
            catch (System.ArgumentException) { return ParseResult.Corrupt; }
            if (parsed == null) return ParseResult.Corrupt;
            if (parsed.version != Loadout.CurrentVersion) return ParseResult.UnknownVersion;
            parsed.vehicleKey = vehicleKey;
            parsed.Normalize();
            loadout = parsed;
            return ParseResult.Ok;
        }

        public static ParseResult Load(string vehicleKey, out Loadout loadout) =>
            TryParse(PlayerPrefs.GetString(KeyFor(vehicleKey), ""), vehicleKey, out loadout);

        public static void Save(Loadout l)
        {
            PlayerPrefs.SetString(KeyFor(l.vehicleKey), ToJson(l));
            PlayerPrefs.Save();
        }

        public static void Delete(string vehicleKey) { PlayerPrefs.DeleteKey(KeyFor(vehicleKey)); }
    }
}
