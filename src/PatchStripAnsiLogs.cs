using System;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace ToasterReskinLoader;

// Strips ANSI color escape sequences from Puck's logger output.
// Puck's LogManager.LogHandler wraps every log line in ESC[..m..ESC[0m and the per-tag
// Logger struct bakes a colored prefix into every message. Terminals/log viewers without VT
// processing render these as literal "←[90m" garbage. We can't disable it via config — it's
// hardcoded — so we patch the two color helpers to return empty strings and strip any ANSI
// already embedded in incoming format strings.
public static class PatchStripAnsiLogs
{
    private const char Esc = '';
    private static readonly Regex AnsiRegex = new Regex("\\[[0-9;]*m", RegexOptions.Compiled);

    public static void Apply(Harmony harmony)
    {
        try
        {
            var handlerType = AccessTools.Inner(typeof(LogManager), "LogHandler");
            if (handlerType == null)
            {
                Plugin.LogError("PatchStripAnsiLogs: LogManager+LogHandler not found; skipping.");
                return;
            }

            var getColor = AccessTools.Method(handlerType, "GetColor");
            var getReset = AccessTools.Method(handlerType, "GetReset");
            var logFormat = AccessTools.Method(handlerType, "LogFormat");

            var emptyPrefix = new HarmonyMethod(typeof(PatchStripAnsiLogs), nameof(EmptyStringPrefix));
            var stripPrefix = new HarmonyMethod(typeof(PatchStripAnsiLogs), nameof(StripFormatPrefix));

            if (getColor != null) harmony.Patch(getColor, prefix: emptyPrefix);
            if (getReset != null) harmony.Patch(getReset, prefix: emptyPrefix);
            if (logFormat != null) harmony.Patch(logFormat, prefix: stripPrefix);

            // Also kill the white tag color used by the per-Logger struct prefix for any
            // Logger constructed AFTER this point. Existing static Logger instances already
            // have their prefix baked in; StripFormatPrefix handles those at log time.
            var loggerGetColor = AccessTools.Method(typeof(global::Logger), "GetColor");
            var loggerGetReset = AccessTools.Method(typeof(global::Logger), "GetReset");
            if (loggerGetColor != null) harmony.Patch(loggerGetColor, prefix: emptyPrefix);
            if (loggerGetReset != null) harmony.Patch(loggerGetReset, prefix: emptyPrefix);

            Plugin.Log("PatchStripAnsiLogs: applied.");
        }
        catch (Exception e)
        {
            Plugin.LogError($"PatchStripAnsiLogs: failed to apply: {e.Message}");
        }
    }

    public static bool EmptyStringPrefix(ref string __result)
    {
        __result = string.Empty;
        return false;
    }

    public static bool StripFormatPrefix(ref string format, object[] args)
    {
        if (!string.IsNullOrEmpty(format) && format.IndexOf(Esc) >= 0)
        {
            format = AnsiRegex.Replace(format, string.Empty);
        }
        if (args != null)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] is string s && !string.IsNullOrEmpty(s) && s.IndexOf(Esc) >= 0)
                {
                    args[i] = AnsiRegex.Replace(s, string.Empty);
                }
            }
        }
        return true;
    }
}
