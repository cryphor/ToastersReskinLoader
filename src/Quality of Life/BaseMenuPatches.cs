// Base-game UX fixes:
//   1. Allow All-Chat / Team-Chat in LockerRoom phase and with PlayerRole.None.
//   2. ESC closes secondary menus (Settings, Mods, ServerBrowser, etc.) when they
//      are open in either Playing or LockerRoom phase.
//   3. Right-click a chat message to copy its plain text to clipboard.
//   4. Chat messages are made drag-selectable by swapping the underlying Label
//      for a read-only TextField (the only UI Toolkit control with native
//      text selection in this Unity version).

using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;

namespace ToasterReskinLoader.QoL
{
    internal static class BaseMenuPatches
    {
        // Each Harmony patch below is gated on a per-feature config flag so the
        // QoL section's toggles take effect without unpatching/re-patching.
        // When a flag is off, the prefix/postfix returns immediately and the
        // original game behavior runs untouched.
        private static bool CfgChatAnyPhase   => QoLRunner.Instance?.Config?.enableChatAnyPhase ?? true;
        private static bool CfgChatDragSelect => QoLRunner.Instance?.Config?.enableChatDragSelect ?? true;
        private static bool CfgInlineFilters  => QoLRunner.Instance?.Config?.enableInlineServerBrowserFilters ?? true;

        // ---- ESC handling for base game secondary menus ---------------------
        //
        // The vanilla pause-action handler only operates on the pause menu and
        // only while phase == Playing. When a secondary menu is visible we
        // route ESC to that menu's own "close" event so its controller can
        // restore the previous view correctly (e.g. Settings -> MainMenu in
        // lobby, Settings -> PauseMenu in game).
        //
        // Returns true if a secondary menu was closed (ESC was consumed).
        public static bool TryCloseTopmostSecondaryMenu()
        {
            try
            {
                var ui = MonoBehaviourSingleton<UIManager>.Instance;
                if (ui == null) return false;

                // Order matters: nested popups before their parents.
                if (ui.Identity != null && ui.Identity.IsVisible) { EventManager.TriggerEvent("Event_OnIdentityClickClose"); return true; }
                if (ui.Appearance != null && ui.Appearance.IsVisible) { EventManager.TriggerEvent("Event_OnAppearanceClickClose"); return true; }
                if (ui.Friends != null && ui.Friends.IsVisible) { EventManager.TriggerEvent("Event_OnFriendsClickClose"); return true; }
                if (ui.NewServer != null && ui.NewServer.IsVisible) { EventManager.TriggerEvent("Event_OnNewServerClickClose"); return true; }
                if (ui.ServerBrowser != null && ui.ServerBrowser.IsVisible) { EventManager.TriggerEvent("Event_OnServerBrowserClickClose"); return true; }
                if (ui.Settings != null && ui.Settings.IsVisible) { EventManager.TriggerEvent("Event_OnSettingsClickClose"); return true; }
                if (ui.Mods != null && ui.Mods.IsVisible) { EventManager.TriggerEvent("Event_OnModsClickClose"); return true; }
                if (ui.PlayerMenu != null && ui.PlayerMenu.IsVisible) { EventManager.TriggerEvent("Event_OnPlayerMenuClickBack"); return true; }
                if (ui.Play != null && ui.Play.IsVisible) { EventManager.TriggerEvent("Event_OnPlayClickClose"); return true; }
            }
            catch (Exception e) { Debug.LogWarning("[PPKB] ESC menu close failed: " + e.Message); }
            return false;
        }

        // ---- Chat-open relaxations -----------------------------------------
        //
        // Vanilla `OnAllChatActionPerformed` / `OnTeamChatActionPerformed` gate
        // on `Phase == Playing`. That means clients in the lobby (LockerRoom)
        // can't open chat. We replace the guard with one that just blocks when
        // another interactive view (menu, popup) is up.

        // Block chat opening only when a modal text-input menu is up. Exclude
        // background views like MainMenu/Play that stay visible in LockerRoom
        // phase, and transient gameplay views like Team/Position select.
        private static bool ChatShouldBeBlocked()
        {
            try
            {
                // Dev console is a focusable text input — chat keys typed inside it
                // are user input, not chat-open requests.
                if (DevConsole.Instance != null && DevConsole.Instance.IsOpen) return true;

                var ui = MonoBehaviourSingleton<UIManager>.Instance;
                if (ui == null) return false;
                if (ui.Settings != null && ui.Settings.IsVisible) return true;
                if (ui.Mods != null && ui.Mods.IsVisible) return true;
                if (ui.PauseMenu != null && ui.PauseMenu.IsVisible) return true;
                if (ui.ServerBrowser != null && ui.ServerBrowser.IsVisible) return true;
                if (ui.NewServer != null && ui.NewServer.IsVisible) return true;
                if (ui.Identity != null && ui.Identity.IsVisible) return true;
                if (ui.Appearance != null && ui.Appearance.IsVisible) return true;
                if (ui.PlayerMenu != null && ui.PlayerMenu.IsVisible) return true;
                if (ui.Friends != null && ui.Friends.IsVisible) return true;
            }
            catch { }
            return false;
        }

        private static void ForceOpenChat(UIChat chat, bool teamChat)
        {
            if (chat == null) return;
            try
            {
                // Chat already capturing input — don't re-StartInput, that would
                // wipe whatever the user is typing.
                if (chat.IsFocused) return;

                if (!chat.IsVisible) chat.Show();

                // TeamSelect / PositionSelect / other LockerRoom overlays sit on
                // top of chat, so even after Show() the textfield is visually
                // behind them. Lift the chat view to the top of its siblings.
                var view = AccessTools.Field(typeof(UIView), "view")?.GetValue(chat) as VisualElement;
                view?.BringToFront();

                chat.StartInput(isTeamChat: teamChat);
            }
            catch (Exception e) { Debug.LogWarning("[PPKB] ForceOpenChat failed: " + e.Message); }
        }

        [HarmonyPatch(typeof(UIManager), "OnAllChatActionPerformed")]
        private static class AllChat_AllowAnyPhase
        {
            private static bool Prefix(UIManager __instance)
            {
                if (!CfgChatAnyPhase) return true; // run original
                try
                {
                    if (ChatShouldBeBlocked()) return false;
                    ForceOpenChat(__instance?.Chat, teamChat: false);
                    return false;
                }
                catch { }
                return true;
            }
        }

        [HarmonyPatch(typeof(UIManager), "OnTeamChatActionPerformed")]
        private static class TeamChat_AllowAnyPhase
        {
            private static bool Prefix(UIManager __instance)
            {
                if (!CfgChatAnyPhase) return true; // run original
                try
                {
                    if (ChatShouldBeBlocked()) return false;
                    ForceOpenChat(__instance?.Chat, teamChat: true);
                    return false;
                }
                catch { }
                return true;
            }
        }

        // ---- Selectable chat text + right-click copy -----------------------
        //
        // UI Toolkit's TextElement (Label's base) supports selection via the
        // `selection` interface (Unity 2023+). Enabling `isSelectable` lets the
        // user drag-highlight chat text. We also keep a right-click handler
        // that copies the line's plain text to the system clipboard.

        // Enable click-to-copy AND drag-select on chat lines.
        //
        // The chat panel sits as a non-interactive HUD overlay: its parent
        // chain has pickingMode=Ignore so pointer events fall through to the
        // game. To make text interactive we walk the WHOLE ancestor chain from
        // the chat view up to its panel root and flip every Ignore to Position.
        //
        // Selection is enabled best-effort via TextElement.selection — if it
        // doesn't take, the user can still left-click a line to copy the whole
        // message (or right-click for the same).

        private static bool _chatPickingOpened;
        private static void OpenChatPickingPath(UIChat chat)
        {
            if (_chatPickingOpened || chat == null) return;
            try
            {
                var view = AccessTools.Field(typeof(UIView), "view")?.GetValue(chat) as VisualElement;
                if (view == null) return;
                // Walk all the way up to the panel root and unblock pointer events.
                var cur = view;
                while (cur != null)
                {
                    if (cur.pickingMode == PickingMode.Ignore) cur.pickingMode = PickingMode.Position;
                    cur = cur.parent;
                }
                // Also flip any known internal containers explicitly.
                foreach (var name in new[] { "chat", "scrollView", "messages", "padding" })
                {
                    var ve = AccessTools.Field(typeof(UIChat), name)?.GetValue(chat) as VisualElement;
                    if (ve != null && ve.pickingMode == PickingMode.Ignore)
                        ve.pickingMode = PickingMode.Position;
                }
                _chatPickingOpened = true;
            }
            catch (Exception e) { Debug.LogWarning("[PPKB] OpenChatPickingPath failed: " + e.Message); }
        }

        [HarmonyPatch(typeof(UIChat), "AddChatMessage")]
        private static class Chat_MakeSelectable_Postfix
        {
            private static void Postfix(UIChat __instance, ChatMessage chatMessage)
            {
                if (!CfgChatDragSelect) return;
                try
                {
                    OpenChatPickingPath(__instance);

                    var messages = AccessTools.Field(typeof(UIChat), "messages")?.GetValue(__instance) as VisualElement;
                    if (messages == null || messages.childCount == 0) return;
                    var child = messages[messages.childCount - 1];
                    if (child == null) return;

                    child.pickingMode = PickingMode.Position;

                    var labels = child.Query<Label>().ToList();
                    foreach (var lbl in labels)
                    {
                        lbl.focusable = true;
                        lbl.pickingMode = PickingMode.Position;
                        try
                        {
                            lbl.selection.isSelectable = true;
                            lbl.selection.doubleClickSelectsWord = true;
                            lbl.selection.tripleClickSelectsLine = true;
                        }
                        catch { }

                        // Left- OR right-click copies the whole message line.
                        var copyTarget = lbl;
                        lbl.RegisterCallback<PointerDownEvent>(evt =>
                        {
                            try
                            {
                                if (evt.button != 0 && evt.button != 1) return;
                                GUIUtility.systemCopyBuffer = StripRichText(copyTarget.text ?? "");
                                evt.StopPropagation();
                            }
                            catch { }
                        });
                    }
                }
                catch (Exception e) { Debug.LogWarning("[PPKB] selection-enable failed: " + e.Message); }
            }
        }

        private static string StripRichText(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            // Quick and good-enough: drop everything inside <...> tags.
            var sb = new System.Text.StringBuilder(s.Length);
            bool inTag = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            return sb.ToString();
        }

        // ---- Server browser: inline filters at the bottom -------------------
        //
        // Vanilla shows a separate full-screen Filters popup behind a [FILTERS]
        // button. Reparenting that popup as-is keeps the screen-overlay USS so
        // it ends up positioned off-screen. Instead, harvest its child controls
        // (search field, max-ping field, toggles) into a fresh compact row
        // appended at the bottom of the server browser. The original popup is
        // hidden entirely. Controls retain their wired-up callbacks because we
        // only reparent the elements themselves.

        private static UIServerBrowser _inlinedFor;
        // Tracks (child, originalParent) for every control we yanked out of the
        // base-game filters popup so we can put them back on toggle-off.
        private static readonly System.Collections.Generic.List<(VisualElement child, VisualElement originalParent)> _movedControls =
            new System.Collections.Generic.List<(VisualElement, VisualElement)>();
        // BuildCell hides each control's internal label. Track them so undo
        // can un-hide.
        private static readonly System.Collections.Generic.List<VisualElement> _hiddenInnerLabels =
            new System.Collections.Generic.List<VisualElement>();
        // StyleInputField / StyleToggleBox apply inline style overrides that
        // would persist after re-parenting back to the popup. Track every
        // styled element so undo can clear the inline overrides and let the
        // base-game USS reassert.
        private static readonly System.Collections.Generic.List<VisualElement> _styledOverlays =
            new System.Collections.Generic.List<VisualElement>();
        // Hover callbacks attached to the moved REFRESH / NEW SERVER buttons.
        // Without tracking, every InlineFilters cycle would add a new pair of
        // handlers — N toggles in a session = N ticks per hover.
        private static readonly System.Collections.Generic.Dictionary<VisualElement,
            (EventCallback<MouseEnterEvent> enter, EventCallback<MouseLeaveEvent> leave)> _buttonHoverCallbacks =
            new System.Collections.Generic.Dictionary<VisualElement,
                (EventCallback<MouseEnterEvent>, EventCallback<MouseLeaveEvent>)>();
        // BuildCell clears each control's `label` text property to suppress
        // the inner label that would otherwise read "Search Text Field" /
        // "Show Full Toggle" / etc. inside our row. Capture the original
        // text here so undo can restore it — without this, the vanilla
        // popup ends up with blank labels next to every control.
        private static readonly System.Collections.Generic.List<(VisualElement ctrl, string originalLabel)> _originalLabels =
            new System.Collections.Generic.List<(VisualElement, string)>();
        private static VisualElement _hiddenFiltersButton;
        private static VisualElement _hiddenFiltersPopup;

        private static void TrackMove(VisualElement child)
        {
            if (child?.parent != null) _movedControls.Add((child, child.parent));
        }

        // Reset all the inline style properties InlineFilters / StyleInputField
        // / StyleToggleBox / BuildCell touched so the control falls back to its
        // base-game USS look once it's re-parented to the original popup.
        private static void ClearInlineOverrides(VisualElement ve)
        {
            if (ve == null) return;
            try
            {
                ve.style.width = StyleKeyword.Null;
                ve.style.height = StyleKeyword.Null;
                ve.style.minWidth = StyleKeyword.Null;
                ve.style.minHeight = StyleKeyword.Null;
                ve.style.flexGrow = StyleKeyword.Null;
                ve.style.flexShrink = StyleKeyword.Null;
                ve.style.flexBasis = StyleKeyword.Null;
                ve.style.backgroundColor = StyleKeyword.Null;
                ve.style.color = StyleKeyword.Null;
                ve.style.unityTextAlign = StyleKeyword.Null;
                ve.style.fontSize = StyleKeyword.Null;
                ve.style.unityFontStyleAndWeight = StyleKeyword.Null;
                ve.style.marginLeft = StyleKeyword.Null;
                ve.style.marginRight = StyleKeyword.Null;
                ve.style.marginTop = StyleKeyword.Null;
                ve.style.marginBottom = StyleKeyword.Null;
                ve.style.paddingLeft = StyleKeyword.Null;
                ve.style.paddingRight = StyleKeyword.Null;
                ve.style.paddingTop = StyleKeyword.Null;
                ve.style.paddingBottom = StyleKeyword.Null;
                ve.style.borderTopWidth = StyleKeyword.Null;
                ve.style.borderBottomWidth = StyleKeyword.Null;
                ve.style.borderLeftWidth = StyleKeyword.Null;
                ve.style.borderRightWidth = StyleKeyword.Null;
                ve.style.borderTopColor = StyleKeyword.Null;
                ve.style.borderBottomColor = StyleKeyword.Null;
                ve.style.borderLeftColor = StyleKeyword.Null;
                ve.style.borderRightColor = StyleKeyword.Null;
            }
            catch { }
        }

        // Reverses the changes InlineFilters made: parent each moved control
        // back to its original wrapper, un-hide its built-in label, drop our
        // inline overrides, drop the strip, and re-show the vanilla filters
        // button. Called by the Player QoL section when the toggle is turned
        // off mid-session.
        public static void UndoInlineFiltersForCurrent()
        {
            var browser = _inlinedFor;
            _inlinedFor = null;
            if (browser == null) return;
            try
            {
                var serverBrowser = AccessTools.Field(typeof(UIServerBrowser), "serverBrowser")?.GetValue(browser) as VisualElement;

                // 0) Unregister the button hover callbacks so the vanilla
                //    [REFRESH] / [NEW SERVER] buttons don't keep our handlers
                //    after they're put back in the original popup.
                foreach (var kv in _buttonHoverCallbacks)
                {
                    if (kv.Key == null) continue;
                    try { kv.Key.UnregisterCallback(kv.Value.enter); } catch { }
                    try { kv.Key.UnregisterCallback(kv.Value.leave); } catch { }
                }
                _buttonHoverCallbacks.Clear();

                // 1) Restore label text BuildCell blanked + un-hide the
                //    label child element BuildCell collapsed. Without the
                //    text restore the vanilla popup ends up with empty rows.
                foreach (var (ctrl, originalLabel) in _originalLabels)
                {
                    if (ctrl == null) continue;
                    try
                    {
                        var labelProp = ctrl.GetType().GetProperty("label",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                        labelProp?.SetValue(ctrl, originalLabel ?? "");
                    }
                    catch { }
                }
                _originalLabels.Clear();

                foreach (var lbl in _hiddenInnerLabels)
                {
                    if (lbl == null) continue;
                    try { lbl.style.display = DisplayStyle.Flex; } catch { }
                }
                _hiddenInnerLabels.Clear();

                // 2) Clear inline style overrides we applied + on each moved
                //    control's descendants so USS can fully take over again.
                foreach (var v in _styledOverlays)
                {
                    ClearInlineOverrides(v);
                    if (v != null)
                    {
                        try
                        {
                            var input = v.Q(className: "unity-base-text-field__input")
                                     ?? v.Q(className: "unity-text-field__input")
                                     ?? v.Q(className: "unity-base-field__input");
                            ClearInlineOverrides(input);
                            var checkmark = v.Q(className: "unity-toggle__checkmark");
                            ClearInlineOverrides(checkmark);
                        }
                        catch { }
                    }
                }
                _styledOverlays.Clear();

                // 3) Reparent every yanked control back to where we found it.
                foreach (var (child, parent) in _movedControls)
                {
                    if (child == null || parent == null) continue;
                    try
                    {
                        child.RemoveFromHierarchy();
                        parent.Add(child);
                    }
                    catch { }
                }
                _movedControls.Clear();

                // 4) Drop our inline strip and re-show the vanilla [FILTERS]
                //    button so the user can re-open the popup.
                if (serverBrowser != null)
                {
                    var strip = serverBrowser.Q<VisualElement>("PPKB_InlineFilters");
                    strip?.RemoveFromHierarchy();
                }

                if (_hiddenFiltersButton != null) _hiddenFiltersButton.style.display = StyleKeyword.Null;
                _hiddenFiltersButton = null;

                // 5) Reset the popup's display override so clicking [FILTERS]
                //    works again. The base-game ShowFilters/HideFilters patches
                //    handle the actual visibility toggling.
                if (_hiddenFiltersPopup != null) _hiddenFiltersPopup.style.display = StyleKeyword.Null;
                _hiddenFiltersPopup = null;
            }
            catch (Exception e) { Debug.LogWarning("[QoL] UndoInlineFiltersForCurrent failed: " + e.Message); }
        }

        // Triggers the inline-filter logic against whichever UIServerBrowser
        // is currently open. Used when the user toggles the feature back on
        // mid-session.
        public static void ReapplyInlineFiltersForCurrent()
        {
            try
            {
                var ui = MonoBehaviourSingleton<UIManager>.Instance;
                if (ui?.ServerBrowser == null) return;
                if (!ui.ServerBrowser.IsVisible) return;
                _inlinedFor = null; // force re-run on the same instance
                InlineFilters(ui.ServerBrowser);
            }
            catch (Exception e) { Debug.LogWarning("[QoL] ReapplyInlineFiltersForCurrent failed: " + e.Message); }
        }

        // Match the server-list row style: dark uniform row, bold uppercase
        // label on the left, control right-aligned, comfortable padding.
        private static readonly Color BrowserRowBg    = new Color(61f / 255f, 61f / 255f, 61f / 255f, 1f);
        private static readonly Color BrowserRowHover = new Color(80f / 255f, 80f / 255f, 80f / 255f, 1f);

        // Plays the base-game UI tick sound when the user hovers the row and
        // brightens the row background. The leave handler restores it. The
        // _lastTickedRow throttle keeps the tick from re-firing when the
        // mouse jiggles inside the same row's children.
        private static VisualElement _lastTickedRow;
        private static void AddRowHoverEffects(VisualElement row)
        {
            row.RegisterCallback<MouseEnterEvent>(_ =>
            {
                row.style.backgroundColor = new StyleColor(BrowserRowHover);
                if (_lastTickedRow != row)
                {
                    _lastTickedRow = row;
                    try { MonoBehaviourSingleton<UIManager>.Instance?.PlaySelectSound(); } catch { }
                }
            });
            row.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                row.style.backgroundColor = new StyleColor(BrowserRowBg);
                if (_lastTickedRow == row) _lastTickedRow = null;
            });
        }

        private static void StyleInputField(VisualElement field, float width)
        {
            _styledOverlays.Add(field);
            field.style.width = width;
            field.style.height = 32;
            // Outer wrapper sized only — leave background to the base-game USS
            // so the input retains its vanilla color.
            field.style.borderTopWidth = 0; field.style.borderBottomWidth = 0;
            field.style.borderLeftWidth = 0; field.style.borderRightWidth = 0;

            var input = field.Q(className: "unity-base-text-field__input")
                     ?? field.Q(className: "unity-text-field__input")
                     ?? field.Q(className: "unity-base-field__input");
            if (input != null)
            {
                // Match the input area to the row background so it blends
                // exactly like the base-game popup (no visible "input box"
                // around the value — same color as the surrounding row).
                input.style.backgroundColor = new StyleColor(BrowserRowBg);
                input.style.color = Color.white;
                input.style.unityTextAlign = TextAnchor.MiddleRight;
                input.style.fontSize = 24;
                input.style.unityFontStyleAndWeight = FontStyle.Normal;
                input.style.paddingLeft = 6;
                input.style.paddingRight = 6;
                input.style.borderTopWidth = 0; input.style.borderBottomWidth = 0;
                input.style.borderLeftWidth = 0; input.style.borderRightWidth = 0;
            }
        }

        private static void StyleToggleBox(Toggle toggle)
        {
            if (toggle == null) return;
            _styledOverlays.Add(toggle);
            var checkmark = toggle.Q(className: "unity-toggle__checkmark");
            if (checkmark != null)
            {
                checkmark.style.width = 24;
                checkmark.style.height = 24;
                checkmark.style.borderTopWidth = 2; checkmark.style.borderBottomWidth = 2;
                checkmark.style.borderLeftWidth = 2; checkmark.style.borderRightWidth = 2;
                var col = new StyleColor(new Color(1f, 1f, 1f, 0.85f));
                checkmark.style.borderTopColor = col; checkmark.style.borderBottomColor = col;
                checkmark.style.borderLeftColor = col; checkmark.style.borderRightColor = col;
            }
        }

        private static VisualElement BuildCell(string label, VisualElement control)
        {
            var labelProp = control.GetType().GetProperty("label",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            try
            {
                // Capture the existing label text before we blank it so undo
                // can put it back ("SEARCH", "MAX PING", "SHOW FULL", ...).
                if (labelProp != null)
                {
                    var original = labelProp.GetValue(control, null) as string;
                    _originalLabels.Add((control, original));
                    labelProp.SetValue(control, "");
                }
            }
            catch { }

            var labelChild = control.Q<Label>(className: "unity-base-field__label")
                          ?? control.Q<Label>(className: "unity-toggle__label")
                          ?? control.Q<Label>(className: "unity-text-field__label");
            if (labelChild != null)
            {
                labelChild.style.display = DisplayStyle.None;
                _hiddenInnerLabels.Add(labelChild);
            }

            control.style.marginLeft = 0;
            control.style.marginRight = 0;
            control.style.marginTop = 0;
            control.style.marginBottom = 0;
            control.style.flexGrow = 0;
            control.style.flexShrink = 0;

            var cell = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.SpaceBetween,
                    height = 50,
                    minHeight = 50,
                    flexShrink = 0,
                    marginBottom = 8,
                    paddingLeft = 24,
                    paddingRight = 24,
                    backgroundColor = new StyleColor(BrowserRowBg),
                }
            };
            var lab = new Label(label)
            {
                style =
                {
                    color = Color.white,
                    fontSize = 24,
                    unityFontStyleAndWeight = FontStyle.Normal,
                    unityTextAlign = TextAnchor.MiddleLeft,
                }
            };
            cell.Add(lab);
            cell.Add(control);
            AddRowHoverEffects(cell);
            return cell;
        }

        private static VisualElement BuildColumn()
        {
            return new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexGrow = 1,
                    flexShrink = 1,
                    flexBasis = 0,
                }
            };
        }

        private static void InlineFilters(UIServerBrowser browser)
        {
            if (browser == null || browser == _inlinedFor) return;
            try
            {
                var serverBrowser = AccessTools.Field(typeof(UIServerBrowser), "serverBrowser")?.GetValue(browser) as VisualElement;
                var filters = AccessTools.Field(typeof(UIServerBrowser), "filters")?.GetValue(browser) as VisualElement;
                var filtersButton = AccessTools.Field(typeof(UIServerBrowser), "filtersButton")?.GetValue(browser) as VisualElement;
                var refreshBtn = AccessTools.Field(typeof(UIServerBrowser), "refreshButton")?.GetValue(browser) as VisualElement;
                var newServerBtn = AccessTools.Field(typeof(UIServerBrowser), "newServerButton")?.GetValue(browser) as VisualElement;
                if (serverBrowser == null || filters == null) return;

                _hiddenFiltersButton = filtersButton;
                _hiddenFiltersPopup = filters;
                if (filtersButton != null) filtersButton.style.display = DisplayStyle.None;
                filters.style.display = DisplayStyle.None;

                var search = filters.Q<VisualElement>("SearchTextField")?.Q<TextField>();
                var maxPing = filters.Q<VisualElement>("MaxPingIntegerField")?.Q<IntegerField>();
                var showFull = filters.Q<VisualElement>("ShowFullToggle")?.Q<Toggle>();
                var showEmpty = filters.Q<VisualElement>("ShowEmptyToggle")?.Q<Toggle>();
                var showPwd = filters.Q<VisualElement>("ShowPasswordProtectedToggle")?.Q<Toggle>();
                var showModded = filters.Q<VisualElement>("ShowModdedToggle")?.Q<Toggle>();
                var showUnreach = filters.Q<VisualElement>("ShowUnreachableToggle")?.Q<Toggle>();
                _movedControls.Clear();
                _hiddenInnerLabels.Clear();
                _styledOverlays.Clear();
                _originalLabels.Clear();
                // Unregister any hover callbacks left over from a previous
                // inline cycle so we don't stack multiple PlaySelectSound
                // calls per hover.
                foreach (var kv in _buttonHoverCallbacks)
                {
                    if (kv.Key == null) continue;
                    try { kv.Key.UnregisterCallback(kv.Value.enter); } catch { }
                    try { kv.Key.UnregisterCallback(kv.Value.leave); } catch { }
                }
                _buttonHoverCallbacks.Clear();

                // ---- Filter strip: two columns side-by-side ----
                var strip = new VisualElement
                {
                    name = "PPKB_InlineFilters",
                    style =
                    {
                        flexDirection = FlexDirection.Row,
                        alignItems = Align.FlexStart,
                        flexShrink = 0,
                        // Sit flush against the server-list end. The
                        // server-list ScrollView above already has its own
                        // trailing space, so any extra margin here doubles
                        // up and looks like a big empty band.
                        marginTop = 0,
                        marginBottom = 0,
                    }
                };

                var col1 = BuildColumn();
                var col2 = BuildColumn();

                if (search != null)
                {
                    TrackMove(search);
                    search.RemoveFromHierarchy();
                    StyleInputField(search, 200);
                    col1.Add(BuildCell("SEARCH", search));
                }
                if (maxPing != null)
                {
                    TrackMove(maxPing);
                    maxPing.RemoveFromHierarchy();
                    StyleInputField(maxPing, 80);
                    col1.Add(BuildCell("MAX PING", maxPing));
                }
                if (showUnreach != null)
                {
                    TrackMove(showUnreach);
                    showUnreach.RemoveFromHierarchy();
                    StyleToggleBox(showUnreach);
                    col1.Add(BuildCell("UNREACHABLE", showUnreach));
                }
                if (showModded != null) { TrackMove(showModded); showModded.RemoveFromHierarchy(); StyleToggleBox(showModded); col1.Add(BuildCell("MODDED", showModded)); }

                if (showFull != null)   { TrackMove(showFull);   showFull.RemoveFromHierarchy();   StyleToggleBox(showFull);   col2.Add(BuildCell("FULL", showFull));     }
                if (showEmpty != null)  { TrackMove(showEmpty);  showEmpty.RemoveFromHierarchy();  StyleToggleBox(showEmpty);  col2.Add(BuildCell("EMPTY", showEmpty));   }
                if (showPwd != null)    { TrackMove(showPwd);    showPwd.RemoveFromHierarchy();    StyleToggleBox(showPwd);    col2.Add(BuildCell("LOCKED", showPwd));    }

                // Fill the empty 4th slot in col2 with the REFRESH + NEW SERVER buttons.
                if (refreshBtn != null && newServerBtn != null)
                {
                    TrackMove(refreshBtn);
                    TrackMove(newServerBtn);
                    refreshBtn.RemoveFromHierarchy();
                    newServerBtn.RemoveFromHierarchy();
                    foreach (var b in new[] { refreshBtn, newServerBtn })
                    {
                        _styledOverlays.Add(b);
                        b.style.height = 50;
                        b.style.flexGrow = 1;
                        b.style.flexBasis = 0;
                        b.style.flexShrink = 1;
                        b.style.minWidth = 0;
                        b.style.marginLeft = 0;
                        b.style.marginRight = 0;
                        b.style.marginTop = 0;
                        b.style.marginBottom = 0;
                        b.style.fontSize = 24;
                        b.style.unityFontStyleAndWeight = FontStyle.Normal;
                        b.style.color = Color.white;
                        b.style.backgroundColor = new StyleColor(BrowserRowBg);
                        b.style.borderTopWidth = 0; b.style.borderBottomWidth = 0;
                        b.style.borderLeftWidth = 0; b.style.borderRightWidth = 0;
                        b.style.paddingLeft = 24; b.style.paddingRight = 24;
                        b.style.unityTextAlign = TextAnchor.MiddleLeft;

                        // Hover: brighten on enter, restore on leave. We do
                        // NOT play a tick sound here — the base-game button
                        // plays its own hover sound natively. Adding ours
                        // produced two ticks per hover. Callback refs go
                        // into _buttonHoverCallbacks so we can deregister
                        // them cleanly on undo / re-apply.
                        var btnRef = b;
                        EventCallback<MouseEnterEvent> onEnter = _ =>
                        {
                            if (!btnRef.enabledSelf) return;
                            btnRef.style.backgroundColor = new StyleColor(BrowserRowHover);
                        };
                        EventCallback<MouseLeaveEvent> onLeave = _ =>
                        {
                            btnRef.style.backgroundColor = new StyleColor(BrowserRowBg);
                        };
                        btnRef.RegisterCallback(onEnter);
                        btnRef.RegisterCallback(onLeave);
                        _buttonHoverCallbacks[btnRef] = (onEnter, onLeave);
                    }
                    refreshBtn.style.marginRight = 4;
                    newServerBtn.style.marginLeft = 4;

                    var buttonsRow = new VisualElement
                    {
                        style =
                        {
                            flexDirection = FlexDirection.Row,
                            alignItems = Align.Center,
                            justifyContent = Justify.SpaceBetween,
                            height = 50,
                            minHeight = 50,
                            flexShrink = 0,
                            marginBottom = 8,
                        }
                    };
                    buttonsRow.Add(refreshBtn);
                    buttonsRow.Add(newServerBtn);
                    col2.Add(buttonsRow);
                }

                strip.Add(col1);
                strip.Add(col2);
                // Two equal-width columns. Outer edges align with the panel
                // edges; inner gap is 8px total (4px on each column's
                // facing side) to match the row vertical gap.
                col1.style.marginLeft = 0;
                col1.style.marginRight = 4;
                col2.style.marginLeft = 4;
                col2.style.marginRight = 0;

                serverBrowser.Add(strip);

                _inlinedFor = browser;
            }
            catch (Exception e) { Debug.LogWarning("[PPKB] ServerBrowser inline-filters failed: " + e.Message); }
        }

        [HarmonyPatch(typeof(UIServerBrowser), "Show")]
        private static class ServerBrowser_Show_InlineFilters
        {
            private static void Postfix(UIServerBrowser __instance)
            {
                if (!CfgInlineFilters) return;
                InlineFilters(__instance);
            }
        }

        // Keep ShowFilters/HideFilters from re-displaying the now-hidden popup.
        // (Only when the inline-filters feature is on; otherwise let the vanilla
        // popup work as it always did.)
        [HarmonyPatch(typeof(UIServerBrowser), "ShowFilters")]
        private static class ServerBrowser_ShowFilters_NoOp
        {
            private static bool Prefix() => !CfgInlineFilters;
        }
        [HarmonyPatch(typeof(UIServerBrowser), "HideFilters")]
        private static class ServerBrowser_HideFilters_NoOp
        {
            private static bool Prefix() => !CfgInlineFilters;
        }
    }
}
