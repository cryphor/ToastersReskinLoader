// One-time migration of settings that used to live in the reskin profile but now live in the
// QoL profile (shadows, and — as later stages land — minimap, chat, gloss, team indicator).
//
// Reads the OLD values straight from reskinprofiles/ReskinProfile.json (raw JSON, since the
// typed loader no longer knows these keys) and seeds them into the QoL config the first time,
// so upgrading users don't lose their settings. Guarded by a flag in the QoL config; runs once.

using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace ToasterReskinLoader.qol;

internal static class DisplaySettingsMigration
{
    public static void Run()
    {
        try
        {
            var runner = QoLRunner.Instance;
            var cfg = runner?.Config;
            if (cfg == null || cfg.displaySettingsMigrated) return;

            string path = Path.Combine(PathManager.GameRootFolder, "reskinprofiles", "ReskinProfile.json");
            if (File.Exists(path))
            {
                var j = JObject.Parse(File.ReadAllText(path));

                // Shadows
                ReadBool(j, "crispyShadowsEnabled", v => cfg.crispyShadowsEnabled = v);
                ReadInt(j, "shadowResolution", v => cfg.shadowResolution = v);
                ReadFloat(j, "shadowDistance", v => cfg.shadowDistance = v);
                ReadInt(j, "shadowCascadeCount", v => cfg.shadowCascadeCount = v);
                ReadBool(j, "shadowSoftShadows", v => cfg.shadowSoftShadows = v);

                // (later stages: minimap / chat / gloss / team indicator keys go here)

                Plugin.Log("[QoL] Migrated display settings from the reskin profile.");
            }

            cfg.displaySettingsMigrated = true;
            runner.SaveAndRefresh();
        }
        catch (Exception e)
        {
            Plugin.LogError($"[QoL] Display settings migration failed: {e.Message}");
        }
    }

    private static void ReadBool(JObject j, string key, Action<bool> set)
    {
        if (j.TryGetValue(key, out var t) && t.Type != JTokenType.Null) set(t.Value<bool>());
    }

    private static void ReadInt(JObject j, string key, Action<int> set)
    {
        if (j.TryGetValue(key, out var t) && t.Type != JTokenType.Null) set(t.Value<int>());
    }

    private static void ReadFloat(JObject j, string key, Action<float> set)
    {
        if (j.TryGetValue(key, out var t) && t.Type != JTokenType.Null) set(t.Value<float>());
    }
}
