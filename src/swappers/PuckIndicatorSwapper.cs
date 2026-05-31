using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ToasterReskinLoader.swappers;

/// <summary>
/// Puck indicator
/// </summary>
public static class PuckIndicatorSwapper
{
    // ── State ──────────────────────────────────────────────────────────
    private static bool               _applied;
    private static VisualElement      _overlay;
    private static VisualElement      _arrow;       // the little triangle
    private static PuckIndicatorTicker _ticker;
    private static Camera              _cam;

    // Smoothing history (previous-frame values for lerp)
    private static Vector2  _smoothPos;
    private static float    _smoothRot;
    private static float    // current displayed opacity (lerps toward target)
                            _smoothAlpha;
    private static float    _targetAlpha;

    // ── Public API ─────────────────────────────────────────────────────

    public static void ApplyAll()
    {
        if (ReskinProfileManager.currentProfile.puckIndicatorEnabled)
            Apply();
        else
            Remove();
    }

    public static void Apply()
    {
        if (_applied) return;
        Plugin.Log("PuckIndicatorSwapper.Apply()");

        try
        {
            BuildOverlay();
            EnsureTicker();
            _smoothAlpha   = 0f;
            _targetAlpha   = 0f;
            _smoothPos     = new Vector2(Screen.width * 0.5f, 0f);
            _smoothRot     = 0f;
            _applied = true;
        }
        catch (Exception ex)
        {
            Plugin.LogError($"PuckIndicatorSwapper.Apply: {ex}");
        }
    }

    public static void Remove()
    {
        if (!_applied) return;
        Plugin.Log("PuckIndicatorSwapper.Remove()");

        try
        {
            _arrow?.RemoveFromHierarchy();
            _overlay?.RemoveFromHierarchy();
            _arrow = null;
            _overlay = null;

            if (_ticker != null)
            {
                UnityEngine.Object.Destroy(_ticker);
                _ticker = null;
            }

            _applied = false;
            _cam     = null;
        }
        catch (Exception ex)
        {
            Plugin.LogError($"PuckIndicatorSwapper.Remove: {ex}");
        }
    }

    // ── Overlay build ──────────────────────────────────────────────────

    private static void BuildOverlay()
    {
        var ui = UIManager.Instance;
        if (ui == null || ui.RootVisualElement == null) return;

        _overlay = new VisualElement { name = "PuckIndicatorOverlay" };
        _overlay.style.position  = Position.Absolute;
        _overlay.style.left      = 0;
        _overlay.style.top       = 0;
        _overlay.style.right     = 0;
        _overlay.style.bottom    = 0;
        _overlay.style.overflow  = Overflow.Visible;
        _overlay.pickingMode    = PickingMode.Ignore;

        // ── Arrow ──────────────────────────────────────────────────────
        // We build a proper right-pointing triangle using the
        // transparent-border trick, then rotate the whole element.
        //
        //      ▲
        //     ◀▶   ← the "point" of the arrow faces right by default
        //      ▼
        //
        // A tall thin rectangle with only the right border coloured
        // produces a clean CSS triangle suitable for rotation.
        _arrow = new VisualElement { name = "PuckArrow" };
        _arrow.style.position       = Position.Absolute;
        _arrow.pickingMode         = PickingMode.Ignore;
        _arrow.style.overflow      = Overflow.Hidden;
        _arrow.style.width          = 20;      // overridden each frame
        _arrow.style.height         = 20;      // overridden each frame
        // Transparent on three sides → coloured triangle on the 4th
        _arrow.style.borderTopColor     = new StyleColor(new Color(0, 0, 0, 0));
        _arrow.style.borderBottomColor  = new StyleColor(new Color(0, 0, 0, 0));
        _arrow.style.borderLeftColor    = new StyleColor(new Color(0, 0, 0, 0));
        _arrow.style.borderRightColor   = new StyleColor(Color.white); // overridden
        _arrow.style.borderTopWidth     = 10;
        _arrow.style.borderBottomWidth  = 10;
        _arrow.style.borderLeftWidth    = 0;
        _arrow.style.borderRightWidth   = 14;

        _overlay.Add(_arrow);
        ui.RootVisualElement.Add(_overlay);
    }

    private static void EnsureTicker()
    {
        if (_ticker != null) return;
        var go = UIManager.Instance?.gameObject;
        if (go == null) return;

        _ticker = go.GetComponent<PuckIndicatorTicker>();
        if (_ticker == null)
            _ticker = go.AddComponent<PuckIndicatorTicker>();
    }

    // ── Per-frame tick ─────────────────────────────────────────────────

    internal static void Tick()
    {
        if (!_applied || _overlay == null || _arrow == null) return;

        var profile = ReskinProfileManager.currentProfile;
        if (profile == null || !profile.puckIndicatorEnabled)
        {
            _overlay.style.display = DisplayStyle.None;
            return;
        }
        _overlay.style.display = DisplayStyle.Flex;

        // ── Camera ─────────────────────────────────────────────────────
        Camera cam = FindCamera();
        if (cam == null) { FadeOut(); return; }

        // ── Puck ───────────────────────────────────────────────────────
        Puck puck = FindActivePuck();
        if (puck == null || puck.gameObject == null) { FadeOut(); return; }
        Vector3 puckPos = puck.transform.position;

        // ── Direction from camera to puck ───────────────────────────────
        Vector3 toPuck = (puckPos - cam.transform.position);

        // Project onto camera axes
        float dotFwd  = Vector3.Dot(toPuck, cam.transform.forward);
        float dotRight = Vector3.Dot(toPuck, cam.transform.right);
        float dotUp   = Vector3.Dot(toPuck, cam.transform.up);

        bool behindCam = dotFwd < 0f;

        // Perspective division using |dotFwd| as the denominator.
        // Using abs ensures behind-camera pucks map to the correct
        // screen edge (e.g. puck behind+right → arrow on right edge).
        float fwdAbs = Mathf.Abs(dotFwd);
        // Guard against zero (puck exactly at camera position)
        if (fwdAbs < 0.0001f) fwdAbs = 0.0001f;

        float perspX = dotRight / fwdAbs;
        float perspY = dotUp   / fwdAbs;

        // Normalise by half-FOV tangent so ±1 = screen edge.
        float halfVFovTan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfHFovTan = halfVFovTan * cam.aspect;

        float nx = perspX / halfHFovTan;
        float ny = perspY / halfVFovTan;

        // On-screen check (only for in-front pucks)
        bool onScreen = !behindCam && Mathf.Abs(nx) < 1f && Mathf.Abs(ny) < 1f;
        if (onScreen)
        {
            FadeOut();
            return;
        }

        // ── Map to screen-edge position ────────────────────────────────
        // Unity UI: Y=0 is TOP.  +ny = puck above camera frustum →
        // arrow should be at top (small Y), so flip Y.
        Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        float margin = profile.puckIndicatorEdgeMargin;
        Vector2 dir = new Vector2(nx, -ny);

        Vector2 targetPos;
        float targetRot;

        if (Mathf.Abs(dir.x) < 0.001f && Mathf.Abs(dir.y) < 0.001f)
        {
            // Degenerate: puck is on the camera's forward axis.
            // Use world-space height delta to pick top vs bottom edge.
            float heightDelta = puckPos.y - cam.transform.position.y;
            if (heightDelta > 0f)
            {
                // Puck above → arrow at top, pointing up
                targetPos = new Vector2(centre.x, margin);
                targetRot = -90f;
            }
            else
            {
                // Puck below (or level) → arrow at bottom, pointing down
                targetPos = new Vector2(centre.x, Screen.height - margin);
                targetRot = 90f;
            }
        }
        else
        {
            // Ray from centre toward puck, find where it hits the
            // inset screen rectangle.
            float absX = Mathf.Abs(dir.x);
            float absY = Mathf.Abs(dir.y);
            float scale = Mathf.Max(absX, absY);
            Vector2 ndir = dir / scale;

            float rectHalfW = Screen.width  * 0.5f - margin;
            float rectHalfH = Screen.height * 0.5f - margin;

            float t;
            if (Mathf.Abs(ndir.x) > Mathf.Abs(ndir.y))
                t = rectHalfW / Mathf.Abs(ndir.x);
            else
                t = rectHalfH / Mathf.Abs(ndir.y);

            targetPos = centre + ndir * t;

            targetPos.x = Mathf.Clamp(targetPos.x, margin, Screen.width  - margin);
            targetPos.y = Mathf.Clamp(targetPos.y, margin, Screen.height - margin);

            // Arrow points from edge inward toward the puck
            targetRot = Mathf.Atan2(-ndir.y, -ndir.x) * Mathf.Rad2Deg;
        }

        // ── Smoothing ──────────────────────────────────────────────────
        float lerpT = 1f - Mathf.Exp(-22f * Time.deltaTime); // ~93% in 100ms
        _smoothPos.x = Mathf.Lerp(_smoothPos.x, targetPos.x, lerpT);
        _smoothPos.y = Mathf.Lerp(_smoothPos.y, targetPos.y, lerpT);

        // Rotation lerp across 360° wrap
        float rotDiff = Mathf.DeltaAngle(_smoothRot, targetRot);
        _smoothRot = Mathf.LerpAngle(_smoothRot, _smoothRot + rotDiff, lerpT);

        _targetAlpha = 1f;
        _smoothAlpha = Mathf.Lerp(_smoothAlpha, _targetAlpha, lerpT);

        // ── Apply to the VisualElement ─────────────────────────────────
        float size    = profile.puckIndicatorArrowSize;
        Color baseCol = profile.puckIndicatorArrowColor;
        baseCol.a     = profile.puckIndicatorOpacity * _smoothAlpha;

        float halfW = size * 0.5f;
        float halfH = size * 0.35f;   // make it slightly wider than tall

        _arrow.style.left              = _smoothPos.x - halfH;
        _arrow.style.top               = _smoothPos.y - halfW;
        _arrow.style.width             = halfH;
        _arrow.style.height            = size;
        _arrow.style.rotate            = new Rotate(_smoothRot);
        _arrow.style.borderRightColor  = new StyleColor(baseCol);
        _arrow.style.borderTopWidth    = halfW;
        _arrow.style.borderBottomWidth = halfW;
        _arrow.style.borderLeftWidth   = 0;
        _arrow.style.borderRightWidth  = halfH * 1.5f;
        _arrow.style.display           = DisplayStyle.Flex;

        // ── Elevation indicator ────────────────────────────────────────
        // (hooked up separately if we add a label later — for now
        //  the colour already adjusts via opacity.)
    }

    private static void FadeOut()
    {
        float lerpT = 1f - Mathf.Exp(-18f * Time.deltaTime);
        _targetAlpha = 0f;
        _smoothAlpha = Mathf.Lerp(_smoothAlpha, _targetAlpha, lerpT);

        if (_smoothAlpha < 0.01f)
            _arrow.style.display = DisplayStyle.None;
    }

    // ── Camera helper ─────────────────────────────────────────────────

    /// <summary>
    /// Returns the camera component that belongs to the local gameplay
    /// player.  Falls back to Camera.main.
    /// </summary>
    private static Camera FindCamera()
    {
        try
        {
            if (_cam != null && _cam.enabled) return _cam;

            var lp = PlayerManager.Instance?.GetLocalPlayer();
            if (lp != null && lp.gameObject != null)
            {
                var c = lp.gameObject.GetComponentInChildren<Camera>(true);
                if (c != null && c.enabled) { _cam = c; return c; }
            }

            _cam = Camera.main;
            return _cam;
        }
        catch { return null; }
    }

    // ── Puck helper ───────────────────────────────────────────────────

    private static Puck FindActivePuck()
    {
        try
        {
            if (PuckManager.Instance == null) return null;
            var list = PuckManager.Instance.GetPucks();
            if (list == null) return null;

            foreach (var p in list)
                if (p != null && p.gameObject != null && p.gameObject.activeInHierarchy)
                    return p;
            return null;
        }
        catch { return null; }
    }

    // ── Helper MonoBehaviour ───────────────────────────────────────────

    internal class PuckIndicatorTicker : MonoBehaviour
    {
        private void LateUpdate()
        {
            try { Tick(); }
            catch (Exception ex)
            {
                // Don't spam — only log once per session
                if (_applied)
                {
                    _applied = false;
                    Plugin.LogError($"PuckIndicatorSwapper tick: {ex}");
                }
            }
        }
    }
}
