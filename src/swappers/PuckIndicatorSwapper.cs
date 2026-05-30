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

        // ── Is puck on-screen? ─────────────────────────────────────────
        // WorldToViewportPoint returns 0..1 range for points in front
        // of and within the frustum.  z < 0 means behind the camera.
        Vector3 vp = cam.WorldToViewportPoint(puckPos);
        bool inFront = vp.z > 0f;
        bool onFront = inFront && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f;

        if (onFront)
        {
            // Puck is within the camera frustum — no indicator needed.
            FadeOut();
            return;
        }

        // ── Compute angle from camera to puck ──────────────────────────
        Vector3 toPuck   = (puckPos - cam.transform.position).normalized;
        Vector3 camFwd   = cam.transform.forward;
        float   dotFwd   = Vector3.Dot(toPuck, camFwd);
        bool    behindCam = dotFwd < 0f;

        // Yaw angle in degrees: 0 = straight ahead, - = leftward, + = rightward.
        // Horizontal angle from camera to puck projected onto the camera plane.
        float yaw;
        if (behindCam)
        {
            yaw = Mathf.Atan2(
                      Vector3.Dot(toPuck, cam.transform.right),
                      -Vector3.Dot(toPuck, camFwd))
                 * Mathf.Rad2Deg;
        }
        else
        {
            yaw = Mathf.Atan2(
                      Vector3.Dot(toPuck, cam.transform.right),
                      Vector3.Dot(toPuck, camFwd))
                 * Mathf.Rad2Deg;
        }

        // ── Map yaw/pitch to a screen-edge position ────────────────────
        // Convert angles to normalised coords in [-1,1] where (±1,±1)
        // are the corners of the screen.
        float hfov = cam.fieldOfView * Mathf.Max(cam.aspect, 0.01f);
        float vfov = cam.fieldOfView;

        // Normalise so that ~half-FOV → ±1
        float nx = Mathf.Clamp(yaw / (hfov * 0.55f), -1f, 1f);

        // Vertical axis: world-space height delta with deadzone.
        // Positive heightDelta (puck above camera) → positive ny →
        //   arrow at TOP of screen (screen Y increases downward in
        //   Unity UI, so +ny → +Y → bottom... wait, in Unity UI
        //   Toolkit top=0 is the TOP, so +Y goes DOWN.
        //   A puck ABOVE the camera should show the arrow at the
        //   TOP of the screen (small Y), so positive heightDelta
        //   must produce NEGATIVE ny.
        float heightDelta = puckPos.y - cam.transform.position.y;
        float heightDeadzone = 0.4f;
        float effectiveHeight = Mathf.Max(0f, Mathf.Abs(heightDelta) - heightDeadzone)
                                * Mathf.Sign(heightDelta);
        // Negate: puck above camera (positive delta) → ny negative → arrow at top
        float nyRaw = -effectiveHeight * 6f / (vfov * 0.55f);
        float ny = Mathf.Clamp(nyRaw, -1f, 1f);

        // Map normalised coords to a point on the screen rectangle.
        //  nx = -1 → left edge, nx = +1 → right edge, etc.
        //  We use a "ray from center" approach: cast a ray from
        //  screen centre in direction (nx, ny) and find where it
        //  hits the inset border.
        Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 dir    = new Vector2(nx, ny);
        float margin   = profile.puckIndicatorEdgeMargin;
        Vector2 targetPos;   // pixel position of arrow centre
        float   targetRot;   // clockwise degrees (0 = pointing right)

        if (Mathf.Abs(dir.x) < 0.001f && Mathf.Abs(dir.y) < 0.001f)
        {
            // Degenerate: puck is nearly dead-center horizontally.
            // Place arrow on the correct vertical edge based on ny.
            // ny < 0 → puck above → top edge; ny > 0 → puck below → bottom edge.
            bool puckAbove = ny < 0f;
            targetPos = new Vector2(centre.x, puckAbove ? margin : Screen.height - margin);
            targetRot = puckAbove ? -90f : 90f;
        }
        else
        {
            // Normalise direction so the longer component is ±1
            float absX = Mathf.Abs(dir.x);
            float absY = Mathf.Abs(dir.y);
            float scale = Mathf.Max(absX, absY);
            Vector2 ndir = dir / scale; // one component is ±1, the other is in [-1,1]

            // The screen rectangle half-size (inset by margin)
            float rectHalfW = Screen.width  * 0.5f - margin;
            float rectHalfH = Screen.height * 0.5f - margin;

            // Scale so we hit the nearer edge
            float t;
            if (Mathf.Abs(ndir.x) > Mathf.Abs(ndir.y))
                t = rectHalfW / Mathf.Abs(ndir.x);
            else
                t = rectHalfH / Mathf.Abs(ndir.y);

            targetPos = centre + ndir * t;

            // Clamp to be safe
            targetPos.x = Mathf.Clamp(targetPos.x, margin, Screen.width  - margin);
            targetPos.y = Mathf.Clamp(targetPos.y, margin, Screen.height - margin);

            // Arrow rotation: the triangle points right (0°) by default,
            // so we rotate by the *opposite* of the direction from centre
            // to puck, i.e. the arrow tip faces toward the puck.
            // But we want the arrow on the edge with its tip pointing
            // *inward* toward the puck, so the arrow should point from
            // the edge toward the puck = opposite of ndir.
            // Point from the edge snappoint toward the puck's actual
            // direction (same as centre→edge direction, extended outward).
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
