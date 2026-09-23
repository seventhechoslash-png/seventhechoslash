using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays Echo Shards (and optionally Vestiges) on screen.
/// Subscribes to CurrencyManager events — no polling, zero Update() overhead.
/// Attach to your HUD Canvas. Assign shardText (required) and shardIcon (optional).
/// </summary>
public class CurrencyHUD : MonoBehaviour
{
    [Header("Echo Shards")]
    [Tooltip("TMP text that shows the shard count.")]
    public TMP_Text shardText;
    [Tooltip("Optional shard icon Image — will pulse on collection.")]
    public Image    shardIcon;

    [Header("Pop Animation")]
    [Tooltip("Scale the text jumps to when a shard is collected.")]
    public float popScale    = 1.4f;
    [Tooltip("How long the pop lasts (seconds).")]
    public float popDuration = 0.25f;
    [Tooltip("Colour the text flashes to on collection.")]
    public Color popColor    = new Color(0.55f, 0.9f, 1f); // icy blue flash
    [Tooltip("Normal text colour.")]
    public Color normalColor = Color.white;

    // ── internals ──────────────────────────────────────────────────────
    private Vector3     baseTextScale;
    private Coroutine   popRoutine;

    // ──────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (shardText == null)
        {
            Debug.LogError("[CurrencyHUD] Shard Text is not assigned! Drag a TMP_Text into the Inspector.");
            return;
        }

        baseTextScale    = shardText.transform.localScale;
        shardText.color  = normalColor;
    }

    void OnEnable()
    {
        CurrencyManager.OnEchoShardsChanged += UpdateShards;
        // Immediately sync to current value (handles scene loads / HUD re-enable)
        if (CurrencyManager.Instance != null)
            UpdateShards(CurrencyManager.Instance.EchoShards);
        else
            SetShardDisplay(0, animate: false);
    }

    void OnDisable()
    {
        CurrencyManager.OnEchoShardsChanged -= UpdateShards;
    }

    // ── shard update ───────────────────────────────────────────────────

    void UpdateShards(int newTotal)
    {
        SetShardDisplay(newTotal, animate: true);
    }

    void SetShardDisplay(int amount, bool animate)
    {
        if (shardText == null) return;
        shardText.text = amount.ToString();

        if (animate)
        {
            if (popRoutine != null) StopCoroutine(popRoutine);
            popRoutine = StartCoroutine(PopRoutine());
        }
    }

    // ── pop animation ──────────────────────────────────────────────────

    IEnumerator PopRoutine()
    {
        float half = popDuration * 0.5f;

        // Scale UP + colour flash
        float elapsed = 0f;
        while (elapsed < half)
        {
            float t = elapsed / half;
            shardText.transform.localScale = Vector3.Lerp(baseTextScale, baseTextScale * popScale, t);
            shardText.color = Color.Lerp(normalColor, popColor, t);
            if (shardIcon != null)
                shardIcon.transform.localScale = Vector3.Lerp(Vector3.one, Vector3.one * 1.2f, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Scale DOWN back to normal
        elapsed = 0f;
        while (elapsed < half)
        {
            float t = elapsed / half;
            shardText.transform.localScale = Vector3.Lerp(baseTextScale * popScale, baseTextScale, t);
            shardText.color = Color.Lerp(popColor, normalColor, t);
            if (shardIcon != null)
                shardIcon.transform.localScale = Vector3.Lerp(Vector3.one * 1.2f, Vector3.one, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Snap to final values
        shardText.transform.localScale = baseTextScale;
        shardText.color = normalColor;
        if (shardIcon != null)
            shardIcon.transform.localScale = Vector3.one;

        popRoutine = null;
    }
}