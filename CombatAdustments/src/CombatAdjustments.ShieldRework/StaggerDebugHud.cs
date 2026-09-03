using HarmonyLib;
using TMPro;
using UnityEngine;

namespace CombatAdjustments.ShieldRework;

/// <summary>
/// Optional always-on stagger fill readout under the HUD stagger bar (for balance testing).
/// </summary>
internal static class StaggerDebugHud
{
    private const string RootName = "CombatAdjustments_StaggerDebug";
    private static bool _visible;
    private static TextMeshProUGUI? _label;

    internal static bool Visible => _visible;

    internal static void Toggle(Terminal.ConsoleEventArgs? args = null)
    {
        SetVisible(!_visible, args);
    }

    internal static void SetVisible(bool visible, Terminal.ConsoleEventArgs? args = null)
    {
        if (visible && !CanUseHudOverlay(out var reason))
        {
            _visible = false;
            args?.Context?.AddString(reason);
            return;
        }

        _visible = visible;
        if (_visible)
        {
            if (!EnsureLabel())
            {
                _visible = false;
                args?.Context?.AddString(
                    "<color=yellow>Stagger HUD unavailable here.</color> Use <color=orange>shieldstagger</color> / <color=orange>sstagger</color> on dedicated server.");
                return;
            }
        }
        else if (_label != null)
        {
            _label.gameObject.SetActive(false);
        }

        args?.Context?.AddString(_visible
            ? "Stagger HUD: <color=orange>on</color> (current / total under stagger bar)"
            : "Stagger HUD: off");
    }

    private static bool CanUseHudOverlay(out string message)
    {
        if (IsHeadlessServerContext())
        {
            message =
                "<color=yellow>Stagger HUD is client-only.</color> Dedicated server has no player HUD — use <color=orange>shieldstagger</color> / <color=orange>sstagger</color>.";
            return false;
        }

        if (Hud.instance == null)
        {
            message = "<color=yellow>Stagger HUD requires the in-game HUD (not available from console-only session).</color>";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static bool IsHeadlessServerContext()
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer())
            return false;

        return Player.m_localPlayer == null;
    }

    internal static void Refresh(Hud hud, Player player)
    {
        if (!_visible || hud == null || player == null)
            return;

        if (!EnsureLabel(hud))
            return;

        _label.gameObject.SetActive(true);

        // Keep the bar itself visible while debugging so empty fill is still readable.
        Animator? animator = Traverse.Create(hud).Field("m_staggerAnimator").GetValue<Animator>();
        animator?.SetBool("Visible", true);

        float total = Traverse.Create((Character)player).Method("GetStaggerTreshold").GetValue<float>();
        float current = Traverse.Create((Character)player).Field("m_staggerDamage").GetValue<float>();
        float grant = ShieldStats.GrantForEquippedShield(player);

        if (grant > 0f)
            _label.text = $"{current:0.#} / {total:0.#}  (+{grant:0})";
        else
            _label.text = $"{current:0.#} / {total:0.#}";
    }

    private static bool EnsureLabel(Hud? hud = null)
    {
        hud ??= Hud.instance;
        if (hud == null)
            return false;

        if (_label != null)
        {
            if (_label)
                return true;
            _label = null;
        }

        GuiBar? progress = Traverse.Create(hud).Field("m_staggerProgress").GetValue<GuiBar>();
        if (progress == null)
            return false;

        Transform parent = progress.transform;
        Transform? existing = parent.Find(RootName);
        GameObject root;
        if (existing != null)
        {
            root = existing.gameObject;
            _label = root.GetComponent<TextMeshProUGUI>();
            if (_label == null)
                _label = root.AddComponent<TextMeshProUGUI>();
        }
        else
        {
            root = new GameObject(RootName, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            _label = root.AddComponent<TextMeshProUGUI>();
        }

        var rt = (RectTransform)root.transform;
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(280f, 28f);
        rt.anchoredPosition = new Vector2(0f, -6f);

        TMP_Text? styleSource = Traverse.Create(hud).Field("m_staminaText").GetValue<TMP_Text>()
            ?? Traverse.Create(hud).Field("m_healthText").GetValue<TMP_Text>();
        if (styleSource != null)
        {
            _label.font = styleSource.font;
            _label.fontSharedMaterial = styleSource.fontSharedMaterial;
            _label.fontSize = Mathf.Max(14f, styleSource.fontSize * 0.85f);
        }
        else if (_label.font == null)
        {
            return false;
        }
        else
        {
            _label.fontSize = 16f;
        }

        _label.alignment = TextAlignmentOptions.Center;
        _label.color = new Color(1f, 0.65f, 0.2f, 1f); // stagger-bar orange
        _label.textWrappingMode = TextWrappingModes.NoWrap;
        _label.raycastTarget = false;
        ApplyOutline(_label);
        return true;
    }

    private static void ApplyOutline(TextMeshProUGUI label)
    {
        if (label.font == null)
            return;

        try
        {
            label.outlineWidth = 0.2f;
            label.outlineColor = new Color32(0, 0, 0, 200);
        }
        catch
        {
            // TMP outline requires a fully initialized font/material (missing on headless / console-only).
        }
    }
}

[HarmonyPatch(typeof(Hud), "UpdateStagger")]
internal static class Hud_UpdateStagger_Patch
{
    private static void Postfix(Hud __instance, Player player)
    {
        StaggerDebugHud.Refresh(__instance, player);
    }
}
