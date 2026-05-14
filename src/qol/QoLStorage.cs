// File-backed storage for the QoL feature surface.
//
// Two files, both under <gameRoot>/reskinprofiles/ (alongside the visual
// reskin profile, easy to find together):
//   * QoL.json          — toggles + filters + dev-console window state
//   * ServerPrefs.json  — savedServerPasswords + trustedServerMods
//
// Reskin profiles can be shared without leaking any of the QoL surface;
// per-server credentials live in their own file so they're obviously not
// part of the shareable visual profile.
//
// Migration: on first load (QoL.json missing), if ReskinProfile.json
// contains a "playerQoL" block, we parse that block directly and split
// its fields into the two new files. ReskinProfileManager separately
// stops serializing PlayerQoL going forward, so the next ReskinProfile
// save drops it cleanly.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ToasterReskinLoader.qol;

internal static class QoLStorage
{
    private static readonly string Dir =
        Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "reskinprofiles");

    internal static readonly string QoLPath           = Path.Combine(Dir, "QoL.json");
    internal static readonly string ServerPrefsPath   = Path.Combine(Dir, "ServerPrefs.json");
    private  static readonly string LegacyProfilePath = Path.Combine(Dir, "ReskinProfile.json");

    public static QoLConfig Load()
    {
        try
        {
            Directory.CreateDirectory(Dir);

            // Step 1: migrate the legacy nested playerQoL block on first
            // run. Only triggered when the new file doesn't exist yet, so
            // we don't clobber edits the user made after the split.
            if (!File.Exists(QoLPath)) TryMigrateLegacy();

            var qol   = ReadJson<QoLProfile>(QoLPath)         ?? new QoLProfile();
            var prefs = ReadJson<ServerPrefsProfile>(ServerPrefsPath) ?? new ServerPrefsProfile();

            var cfg = qol.ToConfig();
            cfg.savedServerPasswords = prefs.SavedServerPasswords ?? new Dictionary<string, string>();
            cfg.trustedServerMods    = prefs.TrustedServerMods    ?? new Dictionary<string, string>();
            return cfg;
        }
        catch (Exception e)
        {
            Plugin.LogError($"[QoL] QoLStorage.Load failed: {e.Message}");
            return new QoLConfig();
        }
    }

    public static void Save(QoLConfig cfg)
    {
        if (cfg == null) return;
        try
        {
            Directory.CreateDirectory(Dir);

            var qol = new QoLProfile();
            qol.FromConfig(cfg);
            File.WriteAllText(QoLPath, JsonConvert.SerializeObject(qol, Formatting.Indented));

            var prefs = new ServerPrefsProfile
            {
                SavedServerPasswords = cfg.savedServerPasswords != null
                    ? new Dictionary<string, string>(cfg.savedServerPasswords)
                    : new Dictionary<string, string>(),
                TrustedServerMods = cfg.trustedServerMods != null
                    ? new Dictionary<string, string>(cfg.trustedServerMods)
                    : new Dictionary<string, string>(),
            };
            File.WriteAllText(ServerPrefsPath, JsonConvert.SerializeObject(prefs, Formatting.Indented));
        }
        catch (Exception e) { Plugin.LogError($"[QoL] QoLStorage.Save failed: {e.Message}"); }
    }

    // ────────────────────────────── helpers ───────────────────────────────

    private static T ReadJson<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Plugin.LogError($"[QoL] failed to read {path}: {e.Message}");
            return null;
        }
    }

    // Reads the legacy ReskinProfile.json as a JObject and pulls the
    // playerQoL block (and its sub-dictionaries) out. We deserialize the
    // QoL portion into QoLProfile so all the property mapping is reused;
    // SavedServerPasswords and TrustedServerMods are extracted separately
    // because QoLProfile no longer carries them.
    private static void TryMigrateLegacy()
    {
        if (!File.Exists(LegacyProfilePath)) return;
        try
        {
            var root = JObject.Parse(File.ReadAllText(LegacyProfilePath));
            var legacy = root["playerQoL"] as JObject;
            if (legacy == null) return;

            var qol = legacy.ToObject<QoLProfile>() ?? new QoLProfile();
            File.WriteAllText(QoLPath, JsonConvert.SerializeObject(qol, Formatting.Indented));

            var prefs = new ServerPrefsProfile
            {
                SavedServerPasswords = legacy["savedServerPasswords"]?.ToObject<Dictionary<string, string>>()
                    ?? new Dictionary<string, string>(),
                TrustedServerMods    = legacy["trustedServerMods"]?.ToObject<Dictionary<string, string>>()
                    ?? new Dictionary<string, string>(),
            };
            File.WriteAllText(ServerPrefsPath, JsonConvert.SerializeObject(prefs, Formatting.Indented));

            Plugin.Log($"[QoL] Migrated legacy playerQoL block out of ReskinProfile.json into " +
                       $"{Path.GetFileName(QoLPath)} + {Path.GetFileName(ServerPrefsPath)}");
        }
        catch (Exception e) { Plugin.LogError($"[QoL] legacy QoL migration failed: {e.Message}"); }
    }
}
