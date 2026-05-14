using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ToasterReskinLoader;

// Vanilla bug (b323): UIHUDController only calls UIHUD.SetUnits() inside the
// Event_OnUnitsChanged handler, which fires when the user toggles the setting.
// At startup the saved value is never applied, so the speed label keeps the
// UXML default ("KPH") even when the saved unit is Imperial. Postfix on
// Event_Everyone_OnPlayerBodySpawned (where vanilla also calls Show/SetStamina)
// applies the saved value each time the local player spawns.
public static class PatchUnitsLabelInit
{
    [HarmonyPatch(typeof(UIHUDController), "Event_Everyone_OnPlayerBodySpawned")]
    class PatchPlayerBodySpawned
    {
        [HarmonyPostfix]
        static void Postfix(UIHUDController __instance, Dictionary<string, object> message)
        {
            try
            {
                PlayerBody playerBody = (PlayerBody)message["playerBody"];
                if (!playerBody.Player.IsLocalPlayer) return;

                var field = typeof(UIHUDController).GetField(
                    "uiHud",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (field?.GetValue(__instance) is UIHUD ui)
                {
                    string label = SettingsManager.Units == Units.Imperial ? "MPH" : "KPH";
                    ui.SetUnits(label);
                }
            }
            catch (Exception e)
            {
                Plugin.LogError($"PatchUnitsLabelInit failed: {e}");
            }
        }
    }
}
