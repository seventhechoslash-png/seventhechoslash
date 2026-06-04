using System.Collections;
using UnityEngine;

/// <summary>
/// Attach to Stalker root.
/// Sprite-based death dissolve — no particle system, fully URP safe.
/// Freeze frame → white flash → sprite fragments fly outward → dark purple dissolve.
/// </summary>
public class StalkerDeathEffect : MonoBehaviour
{
    [Header("Fragment Settings")]
    [Tooltip("How many sprite copies fly outward on death.")]
    public int fragmentCount = 8;
    [Tooltip("How fast fragments fly outward.")]
    public float fragmentSpeed = 4f;
    [Tooltip("How long fragments take to dissolve.")]
    public float fragmentLifetime = 1.4f;

    [Header("Colors")]
    public Color fragmentTint = new Color(0.4f, 0f, 0.6f, 1f);   // dark purple
    public Color flashColor   = new Color(0.6f, 0f, 1f, 1f);      // bright purple void

    [Header("Freeze Frame")]
    public float freezeDuration = 0.25f;

    [Header("Void Ring")]
    public float ringMaxScale  = 3f;
    public float ringDuration  = 0.5f;

    [Header("Screen Flash")]
    [Range(0f, 1f)]
    public float screenFlashIntensity = 0.4f;
    public float screenFlashDuration  = 0.15f;

    // ── Private ───────────────────────────────────────────────────────────
    private SpriteRenderer stalkerSprite;
    private Transform graphics;

    void Awake()
    {
        graphics = transform.Find("Graphics");
        if (graphics != null)
            stalkerSprite = graphics.GetComponent<SpriteRenderer>();
        if (stalkerSprite == null)
            stalkerSprite = GetComponentInChildren<SpriteRenderer>();
    }

    /// <summary>
    /// Call this instead of Destroy — it runs the effect then destroys.
    /// </summary>
    public void PlayDeathEffect()
    {
        StartCoroutine(DeathSequence());
    }

    IEnumerator DeathSequence()
    {
        if (stalkerSprite == null)
        {
            Destroy(gameObject);
            yield break;
        }

        // Disable AI and collider immediately so it stops acting
        var ai = GetComponent<StalkerAI>();
        if (ai != null) ai.enabled = false;

        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        var rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;

        // ── Freeze Frame ──────────────────────────────────────────────────
        // Flash sprite to white
        stalkerSprite.color = Color.white;
        yield return new WaitForSeconds(freezeDuration);

        // ── Screen Flash ──────────────────────────────────────────────────
        StartCoroutine(DoScreenFlash());

        // ── Spawn fragments ───────────────────────────────────────────────
        Sprite currentSprite = stalkerSprite.sprite;
        Vector3 spawnPos     = stalkerSprite.transform.position;
        Vector3 spawnScale   = stalkerSprite.transform.lossyScale;
        string sortingLayer  = stalkerSprite.sortingLayerName;
        int sortingOrder     = stalkerSprite.sortingOrder;

        for (int i = 0; i < fragmentCount; i++)
        {
            float angle = (360f / fragmentCount) * i + Random.Range(-15f, 15f);
            Vector2 dir = new Vector2(
                Mathf.Cos(angle * Mathf.Deg2Rad),
                Mathf.Sin(angle * Mathf.Deg2Rad)
            );
            float speed = Random.Range(fragmentSpeed * 0.5f, fragmentSpeed);
            float scale = Random.Range(0.4f, 0.9f);

            StartCoroutine(AnimateFragment(
                currentSprite, spawnPos, spawnScale * scale,
                dir, speed, sortingLayer, sortingOrder
            ));
        }

        // ── Void ring ─────────────────────────────────────────────────────
        StartCoroutine(DoVoidRing(spawnPos, sortingLayer, sortingOrder));

        // Hide original sprite
        stalkerSprite.color = new Color(0f, 0f, 0f, 0f);

        // Wait for fragments to finish
        yield return new WaitForSeconds(fragmentLifetime);

        Destroy(gameObject);
    }

    IEnumerator AnimateFragment(
        Sprite sprite, Vector3 startPos, Vector3 startScale,
        Vector2 direction, float speed,
        string sortingLayer, int sortingOrder)
    {
        // Create fragment object
        var go = new GameObject("StalkerFragment");
        go.transform.position   = startPos;
        go.transform.localScale = startScale;

        var sr           = go.AddComponent<SpriteRenderer>();
        sr.sprite        = sprite;
        sr.sortingLayerName = sortingLayer;
        sr.sortingOrder  = sortingOrder + 1;
        sr.color         = fragmentTint;

        float elapsed = 0f;
        Vector3 pos   = startPos;

        // Slight random rotation spin
        float rotSpeed = Random.Range(-180f, 180f);

        while (elapsed < fragmentLifetime)
        {
            float t = elapsed / fragmentLifetime;

            // Move outward, slow down over time
            float currentSpeed = Mathf.Lerp(speed, 0f, t);
            pos += (Vector3)(direction * currentSpeed * Time.deltaTime);
            go.transform.position = pos;

            // Spin
            go.transform.Rotate(0f, 0f, rotSpeed * Time.deltaTime);

            // Shrink
            float scaleT = Mathf.Lerp(1f, 0f, Mathf.Pow(t, 0.7f));
            go.transform.localScale = startScale * scaleT;

            // Fade to dark purple then transparent
            float alpha = Mathf.Lerp(0.9f, 0f, Mathf.Pow(t, 0.5f));
            sr.color = new Color(fragmentTint.r, fragmentTint.g, fragmentTint.b, alpha);

            elapsed += Time.deltaTime;
            yield return null;
        }

        Destroy(go);
    }

    IEnumerator DoVoidRing(Vector3 pos, string sortingLayer, int sortingOrder)
    {
        // Build a soft circle texture
        Texture2D tex = GenerateSoftCircle(64);
        Sprite ringSprite = Sprite.Create(
            tex,
            new Rect(0, 0, 64, 64),
            new Vector2(0.5f, 0.5f),
            100f
        );

        var go           = new GameObject("VoidRing");
        go.transform.position = pos;

        var sr           = go.AddComponent<SpriteRenderer>();
        sr.sprite        = ringSprite;
        sr.sortingLayerName = sortingLayer;
        sr.sortingOrder  = sortingOrder + 2;

        float elapsed = 0f;

        while (elapsed < ringDuration)
        {
            float t     = elapsed / ringDuration;
            float scale = Mathf.Lerp(0f, ringMaxScale, Mathf.Pow(t, 0.3f));
            float alpha = t < 0.2f
                ? Mathf.Lerp(0f, 1f, t / 0.2f)
                : Mathf.Lerp(1f, 0f, (t - 0.2f) / 0.8f);

            go.transform.localScale = Vector3.one * scale;
            sr.color = new Color(flashColor.r, flashColor.g, flashColor.b, alpha);

            elapsed += Time.deltaTime;
            yield return null;
        }

        Destroy(go);
    }

    IEnumerator DoScreenFlash()
    {
        Camera cam = Camera.main;
        if (cam == null) yield break;

        var go = new GameObject("DeathScreenFlash");
        go.transform.SetParent(cam.transform, false);
        go.transform.localPosition = new Vector3(0, 0, cam.nearClipPlane + 0.1f);

        Texture2D whiteTex = new Texture2D(4, 4);
        Color[] pixels = new Color[16];
        for (int i = 0; i < 16; i++) pixels[i] = new Color(0.4f, 0f, 0.6f, 1f);
        whiteTex.SetPixels(pixels);
        whiteTex.Apply();

        Sprite whiteSprite = Sprite.Create(whiteTex, new Rect(0,0,4,4), new Vector2(0.5f,0.5f), 1f);

        var sr        = go.AddComponent<SpriteRenderer>();
        sr.sprite     = whiteSprite;
        sr.sortingOrder = 100;
        sr.color      = new Color(0.4f, 0f, 0.6f, 0f);

        float height  = 2f * cam.orthographicSize;
        float width   = height * cam.aspect;
        go.transform.localScale = new Vector3(width * 1.2f, height * 1.2f, 1f);

        float elapsed = 0f;
        while (elapsed < screenFlashDuration)
        {
            float t     = elapsed / screenFlashDuration;
            float alpha = t < 0.2f
                ? Mathf.Lerp(0f, screenFlashIntensity, t / 0.2f)
                : Mathf.Lerp(screenFlashIntensity, 0f, (t - 0.2f) / 0.8f);
            sr.color = new Color(0.4f, 0f, 0.6f, alpha);
            elapsed += Time.deltaTime;
            yield return null;
        }

        Destroy(go);
    }

    Texture2D GenerateSoftCircle(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius   = size / 2f;

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dist  = Vector2.Distance(new Vector2(x, y), center);
                float t     = Mathf.Clamp01(1f - dist / radius);
                float alpha = Mathf.Pow(t, 1.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }

        tex.Apply();
        return tex;
    }
}
