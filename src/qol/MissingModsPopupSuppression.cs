// Per-server suppression of the vanilla "MODS REQUIRED" popup.
//
// Vanilla flow (decompiled from UIPopupManagerController and the OK-click
// handler in ConnectionManagerController):
//   * Event_OnReconnectionStateChanged sees Phase=AwaitingMods +
//     ClientRequiredModIds changed + PendingModIds.Length == 0, and calls
//     UIPopupManager.ShowPopup("missingMods", "MODS REQUIRED", ...).
//   * Clicking OK on the popup runs the load-bearing step:
//         missing-readiness = ClientRequiredModIds \ ReadyMods
//         missing-enabling  = ClientRequiredModIds \ missing-readiness \ EnabledMods
//         GlobalStateManager.SetReconnectionState({ pendingReadinessModIds,
//                                                   pendingEnablingModIds })
//     This kicks off the mod download/enable state machine. Without OK,
//     the connection is stuck — which is why a naive "skip ShowPopup"
//     breaks joins. Closing the popup with X instead clears the
//     reconnection state entirely.
//
// What we add:
//   * A "Don't show this popup again for this server" toggle is injected
//     into the popup. Checking it records ip:port + a stable hash of the
//     ClientRequiredModIds for that server.
//   * On future ShowPopup("missingMods", ...) calls, we look up the
//     current endpoint + mod-id list. If they match a stored entry, we
//     skip the popup AND emulate the OK-click logic directly so the
//     reconnect flow proceeds unattended. Any change to the server's
//     required mod set invalidates the entry — the popup re-appears and
//     the user must consent again.

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;

namespace ToasterReskinLoader.qol;

internal static class MissingModsPopupSuppression
{
    private const string PopupName = "missingMods";
    private const string ToggleName = "ToasterMissingModsDontShow";

    private static Dictionary<string, string> Store =>
        QoLRunner.Instance?.Config?.trustedServerMods;

    // ─────────────────────── management UI surface ────────────────────────

    // Sorted snapshot of the ip:port keys the user has trusted. Used by
    // the QoL settings page to list and individually forget entries.
    internal static List<string> SnapshotKeys()
    {
        var s = Store;
        if (s == null) return new List<string>();
        var list = new List<string>(s.Keys);
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    // How many mod IDs are in the trusted fingerprint for the given key.
    // Fingerprints are sorted-comma-joined; an empty string is zero mods.
    internal static int CountModsFor(string key)
    {
        var s = Store;
        if (s == null || string.IsNullOrEmpty(key)) return 0;
        if (!s.TryGetValue(key, out string v) || string.IsNullOrEmpty(v)) return 0;
        return v.Split(',').Length;
    }

    internal static void Remove(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        var runner = QoLRunner.Instance;
        var s = runner?.Config?.trustedServerMods;
        if (s == null) return;
        if (s.Remove(key)) runner.SaveAndRefresh();
    }

    internal static void RemoveAll()
    {
        var runner = QoLRunner.Instance;
        var s = runner?.Config?.trustedServerMods;
        if (s == null || s.Count == 0) return;
        s.Clear();
        runner.SaveAndRefresh();
    }

    // ─────────────────────────── helpers ──────────────────────────────────

    private static string CurrentEndpointKey()
    {
        try
        {
            var ep = GlobalStateManager.ConnectionState.LastConnection?.EndPoint;
            return ep == null ? null : ep.ipAddress + ":" + ep.port;
        }
        catch { return null; }
    }

    // Sorted + joined so the order of ClientRequiredModIds doesn't matter
    // for equality, and a single string roundtrips cleanly through JSON.
    private static string FingerprintModIds(string[] modIds)
    {
        if (modIds == null || modIds.Length == 0) return "";
        var copy = (string[])modIds.Clone();
        Array.Sort(copy, StringComparer.Ordinal);
        return string.Join(",", copy);
    }

    private static bool IsTrustedForCurrentServer(string[] requiredModIds)
    {
        var store = Store;
        if (store == null) return false;
        string key = CurrentEndpointKey();
        if (string.IsNullOrEmpty(key)) return false;
        if (!store.TryGetValue(key, out string saved)) return false;
        return saved == FingerprintModIds(requiredModIds);
    }

    private static void RememberTrust(string[] requiredModIds)
    {
        var runner = QoLRunner.Instance;
        var store = runner?.Config?.trustedServerMods;
        if (store == null) return;
        string key = CurrentEndpointKey();
        if (string.IsNullOrEmpty(key)) return;
        store[key] = FingerprintModIds(requiredModIds);
        runner.SaveAndRefresh();
    }

    private static void ForgetTrust()
    {
        var runner = QoLRunner.Instance;
        var store = runner?.Config?.trustedServerMods;
        if (store == null) return;
        string key = CurrentEndpointKey();
        if (string.IsNullOrEmpty(key)) return;
        if (store.Remove(key)) runner.SaveAndRefresh();
    }

    // Emulate the OK-click branch of UIPopupManagerController.Event_OnPopupClickOk
    // for "missingMods": compute the pending readiness/enabling sets and
    // push them into the reconnection state. This is the call that
    // actually starts the mod download/enable flow — skipping it leaves
    // the connection hung in AwaitingMods.
    private static void EmulateOkClick(string[] requiredModIds)
    {
        try
        {
            if (GlobalStateManager.ReconnectionState.Phase != ReconnectionPhase.AwaitingMods) return;
            var enabled = ModManager.EnabledMods.Select(m => m.Id).ToArray();
            var ready   = ModManager.ReadyMods.Select(m => m.Id).ToArray();
            var pendingReadiness = requiredModIds.Except(ready).ToArray();
            var pendingEnabling  = requiredModIds.Except(pendingReadiness).Except(enabled).ToArray();
            GlobalStateManager.SetReconnectionState(new Dictionary<string, object>
            {
                { "pendingReadinessModIds", pendingReadiness },
                { "pendingEnablingModIds", pendingEnabling },
            });
        }
        catch (Exception e) { Debug.LogWarning("[QoL] MissingMods OK-emulation failed: " + e.Message); }
    }

    // ─────────────────────────── patches ──────────────────────────────────

    [HarmonyPatch(typeof(UIPopupManager), nameof(UIPopupManager.ShowPopup))]
    private static class ShowPopup_AutoConfirm_Prefix
    {
        private static bool Prefix(string name)
        {
            if (name != PopupName) return true;
            try
            {
                var required = GlobalStateManager.ReconnectionState.ClientRequiredModIds;
                if (!IsTrustedForCurrentServer(required)) return true; // show normally
                EmulateOkClick(required);
                return false; // suppress visible popup; OK was simulated above
            }
            catch (Exception e)
            {
                Debug.LogWarning("[QoL] MissingMods auto-confirm failed: " + e.Message);
                return true; // fall back to vanilla behavior
            }
        }
    }

    [HarmonyPatch(typeof(PopupMissingModsPopupContent), nameof(PopupMissingModsPopupContent.Initialize))]
    private static class Popup_Initialize_Postfix
    {
        private static void Postfix(PopupMissingModsPopupContent __instance)
        {
            try
            {
                var root = __instance?.VisualElement;
                if (root == null) return;
                if (root.Q<Toggle>(ToggleName) != null) return;

                // Read the popup's required mod IDs off its private
                // steamWorkshopItems array — that's what we need to
                // fingerprint when the user opts in.
                var itemsField = AccessTools.Field(typeof(PopupMissingModsPopupContent), "steamWorkshopItems");
                var items = itemsField?.GetValue(__instance) as SteamWorkshopItem[];
                string[] modIds = items?.Select(i => i.Id).ToArray() ?? Array.Empty<string>();

                bool initiallyTrusted = IsTrustedForCurrentServer(modIds);

                var toggle = new Toggle("Don't show this popup again for this server")
                {
                    name = ToggleName,
                    value = initiallyTrusted,
                };
                toggle.style.marginTop = 10;
                toggle.style.fontSize = 13;
                ToasterReskinLoader.ui.UITools.StyleConfigCheckboxBox(toggle);
                toggle.RegisterCallback<AttachToPanelEvent>(_ =>
                {
                    var lbl = toggle.Q<Label>(className: "unity-toggle__label");
                    if (lbl != null) lbl.style.fontSize = 13;
                });
                toggle.RegisterCallback<ChangeEvent<bool>>(evt =>
                {
                    if (evt.newValue) RememberTrust(modIds);
                    else              ForgetTrust();
                });

                var hint = new Label(
                    "Skipping is per-server. If this server changes its required mods, "
                    + "the popup will return so you can review the new set.");
                hint.style.fontSize = 11;
                hint.style.color = new Color(0.7f, 0.7f, 0.7f);
                hint.style.marginTop = 2;
                hint.style.whiteSpace = WhiteSpace.Normal;

                root.Add(toggle);
                root.Add(hint);
            }
            catch (Exception e) { Debug.LogWarning("[QoL] MissingModsPopup inject failed: " + e.Message); }
        }
    }
}
