// Quality of Life tab — small fixes + a handful of opt-in features kept
// from PoncePlayerInput. Single scrollable page with headers/separators
// (no sub-tabs); the section is small enough that tabs add nothing.
//
// What this surface configures:
//   - Arena visual disable (props/lights/skybox/particles + ambient audio)
//   - Goalie wide-view camera (IFeelLeftOut)
//   - In-game dev console (toggle + backtick to open)
//   - Debug logging
// What's always-on (documented at the bottom, no toggles):
//   - ESC closes secondary menus, chat in any phase, drag-select chat,
//     inline server-browser filters, custom :emoji: rendering.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ToasterReskinLoader.QoL;
using UnityEngine;
using UnityEngine.UIElements;

namespace ToasterReskinLoader.ui.sections;

public static class PlayerQoLSection
{
    public static void CreateSection(VisualElement contentScrollViewContent)
    {
        var description = UITools.CreateConfigurationLabel(
            "Small fixes from the PoncePlayerInput mod, plus optional arena disable and an in-game dev console.");
        description.style.marginBottom = 12;
        description.style.whiteSpace = WhiteSpace.Normal;
        contentScrollViewContent.Add(description);

        var runner = QoLRunner.Instance;
        if (runner == null)
        {
            var warn = UITools.CreateConfigurationLabel(
                "QoL runtime is not initialized yet. Reopen this menu after the game finishes loading.");
            warn.style.color = new Color(1f, 0.7f, 0.4f);
            warn.style.whiteSpace = WhiteSpace.Normal;
            contentScrollViewContent.Add(warn);
            return;
        }

        var cfg = runner.Config;

        // ── Arena visuals ──────────────────────────────────────────────────
        Header(contentScrollViewContent, "Arena Visuals");
        Note(contentScrollViewContent,
            "Disable parts of the arena scenery. The full blackout toggle destroys the arena GameObject on the next server join.");

        ToggleRow(contentScrollViewContent, "Disable all arena visuals",
            cfg.disableArenaVisuals,
            v => { cfg.disableArenaVisuals = v; runner.SaveAndRefresh(); ToasterReskinLoader.QoL.ArenaVisuals.ApplyState(cfg); });

        ToggleRow(contentScrollViewContent, "Disable arena props", cfg.disableArenaProps,
            v => { cfg.disableArenaProps = v; runner.SaveAndRefresh(); ToasterReskinLoader.QoL.ArenaVisuals.ApplyPartial(cfg); });
        ToggleRow(contentScrollViewContent, "Disable arena lights", cfg.disableArenaLights,
            v => { cfg.disableArenaLights = v; runner.SaveAndRefresh(); ToasterReskinLoader.QoL.ArenaVisuals.ApplyPartial(cfg); });
        ToggleRow(contentScrollViewContent, "Disable custom skybox", cfg.disableArenaSkybox,
            v => { cfg.disableArenaSkybox = v; runner.SaveAndRefresh(); ToasterReskinLoader.QoL.ArenaVisuals.ApplyPartial(cfg); });
        ToggleRow(contentScrollViewContent, "Disable arena particles", cfg.disableArenaParticles,
            v => { cfg.disableArenaParticles = v; runner.SaveAndRefresh(); ToasterReskinLoader.QoL.ArenaVisuals.ApplyPartial(cfg); });

        var audioRow = UITools.CreateConfigurationRow();
        audioRow.Add(UITools.CreateConfigurationLabel("Arena ambient audio volume"));
        var audioSlider = UITools.CreateConfigurationSlider(0f, 1f, cfg.arenaAudioVolume, 300);
        audioSlider.RegisterCallback<ChangeEvent<float>>(evt =>
        {
            cfg.arenaAudioVolume = evt.newValue;
            runner.SaveAndRefresh();
            ToasterReskinLoader.QoL.ArenaVisuals.ApplyPartial(cfg);
        });
        audioRow.Add(audioSlider);
        contentScrollViewContent.Add(audioRow);

        // ── Dev console ────────────────────────────────────────────────────
        Separator(contentScrollViewContent);
        Header(contentScrollViewContent, "Dev Console");
        Note(contentScrollViewContent,
            "Press backtick to toggle an in-game developer console with live log feed, filters, search, and a command input.");

        ToggleRow(contentScrollViewContent, "Enable dev console", cfg.enableDevConsole,
            v => { cfg.enableDevConsole = v; runner.SaveAndRefresh(); });

        // ── Base-game UX patches ───────────────────────────────────────────
        Separator(contentScrollViewContent);
        Header(contentScrollViewContent, "Base-game UX patches");
        Note(contentScrollViewContent,
            "Small Harmony patches that polish the vanilla menu and chat behavior. Each can be turned off independently.");

        ToggleRow(contentScrollViewContent, "ESC closes secondary menus", cfg.enableEscCloseMenus,
            v => { cfg.enableEscCloseMenus = v; runner.SaveAndRefresh(); });

        ToggleRow(contentScrollViewContent, "Open chat in any game phase", cfg.enableChatAnyPhase,
            v => { cfg.enableChatAnyPhase = v; runner.SaveAndRefresh(); });

        ToggleRow(contentScrollViewContent, "Drag-select and right-click-copy chat lines", cfg.enableChatDragSelect,
            v => { cfg.enableChatDragSelect = v; runner.SaveAndRefresh(); });

        ToggleRow(contentScrollViewContent, "Inline server browser filters", cfg.enableInlineServerBrowserFilters,
            v =>
            {
                cfg.enableInlineServerBrowserFilters = v;
                runner.SaveAndRefresh();
                if (v) BaseMenuPatches.ReapplyInlineFiltersForCurrent();
                else   BaseMenuPatches.UndoInlineFiltersForCurrent();
            });
    }

    // ─────────────────────────────── helpers ────────────────────────────────

    private static void Header(VisualElement parent, string text)
    {
        var h = new Label($"<b>{text}</b>");
        h.style.fontSize = 20;
        h.style.color = Color.white;
        h.style.marginTop = 4;
        h.style.marginBottom = 8;
        parent.Add(h);
    }

    private static void Note(VisualElement parent, string text)
    {
        var n = UITools.CreateConfigurationLabel(text);
        n.style.marginBottom = 8;
        n.style.fontSize = 13;
        n.style.color = new Color(0.7f, 0.7f, 0.7f);
        n.style.whiteSpace = WhiteSpace.Normal;
        parent.Add(n);
    }

    private static void Separator(VisualElement parent)
    {
        var sep = new VisualElement();
        sep.style.height = 1;
        sep.style.backgroundColor = new Color(0.4f, 0.4f, 0.4f);
        sep.style.marginTop = 16;
        sep.style.marginBottom = 16;
        parent.Add(sep);
    }

    private static VisualElement ToggleRow(VisualElement parent, string label, bool initial, Action<bool> onChange)
    {
        var row = UITools.CreateConfigurationRow();
        row.Add(UITools.CreateConfigurationLabel(label));
        var t = UITools.CreateConfigurationCheckbox(initial);
        t.RegisterCallback<ChangeEvent<bool>>(evt => onChange(evt.newValue));
        row.Add(t);
        parent.Add(row);
        return row;
    }

}
