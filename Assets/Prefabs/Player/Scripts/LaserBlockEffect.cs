using System.Collections;
using UnityEngine;

/// <summary>
/// Attach to KatanaBlockVFX (child of Player).
/// Uses SpriteRenderer-based sparks — no particle system, fully URP safe.
/// Same technique as DashAfterimage.
/// </summary>
public class LaserBlockEffect : MonoBehaviour
{
    [Header("Spark Settings — BLOCK (silver)")]
    [Tooltip("Cool silver. Slightly blue-white reads as steel rather than gold.")]
    public Color sparkColor = new Color(0.82f, 0.87f, 0.95f, 1f);
    [Range(5, 20)]
    public int sparkCount = 12;
    [Range(0.05f, 0.4f)]
    public float sparkLifetime = 0.2f;
    [Range(1f, 10f)]
    public float sparkSpeed = 5f;
    [Range(0.05f, 0.5f)]
    public float sparkSize = 0.12f;

    [Header("Flash Ring — BLOCK (silver)")]
    public Color flashColor = new Color(0.88f, 0.92f, 1f, 1f);
    [Range(0.05f, 0.4f)]
    public float flashDuration = 0.18f;
    [Range(0.3f, 3f)]
    public float flashMaxScale = 1.2f;

    [Header("Screen Flash")]
    [Range(0f, 1f)]
    public float screenFlashIntensity = 0.3f;
    [Range(0.05f, 0.3f)]
    public float screenFlashDuration = 0.1f;

    [Header("PARRY — bright silver, everything scaled up")]
    [Tooltip("Near-white with a cold edge. This is the money shot.")]
    public Color parrySparkColor = new Color(1f, 1f, 1f, 1f);
    public Color parryFlashColor = new Color(0.96f, 0.98f, 1f, 1f);
    [Tooltip("Spark count multiplier on a parry.")]
    [Range(1f, 3f)]
    public float parrySparkCountMultiplier = 2f;
    [Tooltip("Spark speed and size multiplier on a parry.")]
    [Range(1f, 3f)]
    public float parrySparkForceMultiplier = 1.6f;
    [Tooltip("Flash ring size multiplier on a parry.")]
    [Range(1f, 3f)]
    public float parryRingScaleMultiplier = 1.8f;
    [Tooltip("Screen flash intensity multiplier on a parry.")]
    [Range(1f, 3f)]
    public float parryScreenFlashMultiplier = 2f;

    [Header("Audio")]
    public AudioClip blockSoundClip;
    [Range(0f, 1f)]
    public float blockSoundVolume = 0.8f;
    [Tooltip("Higher, brighter ring for a parry. Falls back to blockSoundClip if empty.")]
    public AudioClip parrySoundClip;
    [Range(0f, 1f)]
    public float parrySoundVolume = 1f;
    [Tooltip("Pitch applied to the parry sound. Above 1 sounds sharper / more metallic.")]
    [Range(0.5f, 2f)]
    public float parrySoundPitch = 1.25f;

    // ── Pool ──────────────────────────────────────────────────────────────────
    private const int PoolSize = 20;
    private GameObject[] sparkPool;
    private SpriteRenderer[] sparkRenderers;
    private int poolIndex = 0;

    // ── Flash ring ────────────────────────────────────────────────────────────
    private GameObject flashRingObj;
    private SpriteRenderer flashRingRenderer;

    // ── Screen flash ──────────────────────────────────────────────────────────
    private GameObject screenFlashObj;
    private SpriteRenderer screenFlashRenderer;

    // ── Audio ─────────────────────────────────────────────────────────────────
    private AudioSource audioSource;

    // ── Shared texture ────────────────────────────────────────────────────────
    private Texture2D dotTexture;
    private Sprite dotSprite;

    void Awake()
    {
        dotTexture = GenerateSoftDot(32);
        dotSprite  = Sprite.Create(
            dotTexture,
            new Rect(0, 0, 32, 32),
            new Vector2(0.5f, 0.5f),
            100f
        );

        BuildSparkPool();
        BuildFlashRing();
        BuildScreenFlash();
        SetupAudio();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Normal block — small silver spark.</summary>
    public void PlayBlockEffect(Vector2 hitPoint)
    {
        Play(hitPoint, false);
    }

    /// <summary>Successful parry — bright silver burst, everything scaled up.</summary>
    public void PlayParryEffect(Vector2 hitPoint)
    {
        Play(hitPoint, true);
    }

    private void Play(Vector2 hitPoint, bool isParry)
    {
        transform.position = hitPoint;

        Color sCol = isParry ? parrySparkColor : sparkColor;
        Color fCol = isParry ? parryFlashColor : flashColor;

        int   count = isParry
            ? Mathf.RoundToInt(sparkCount * parrySparkCountMultiplier)
            : sparkCount;
        float force = isParry ? parrySparkForceMultiplier : 1f;
        float ring  = isParry ? flashMaxScale * parryRingScaleMultiplier : flashMaxScale;
        float scr   = isParry
            ? Mathf.Clamp01(screenFlashIntensity * parryScreenFlashMultiplier)
            : screenFlashIntensity;

        StopAllCoroutines();
        StartCoroutine(DoSparks(hitPoint, sCol, count, force));
        StartCoroutine(DoFlashRing(hitPoint, fCol, ring));
        StartCoroutine(DoScreenFlash(scr));

        AudioClip clip = isParry
            ? (parrySoundClip != null ? parrySoundClip : blockSoundClip)
            : blockSoundClip;

        if (clip != null)
        {
            audioSource.pitch = isParry ? parrySoundPitch : 1f;
            audioSource.PlayOneShot(clip, isParry ? parrySoundVolume : blockSoundVolume);
        }
    }

    // ── Spark burst ───────────────────────────────────────────────────────────

    IEnumerator DoSparks(Vector2 origin, Color col, int count, float forceMul)
    {
        count = Mathf.Min(count, PoolSize);

        for (int i = 0; i < count; i++)
        {
            GameObject go = sparkPool[poolIndex];
            SpriteRenderer sr = sparkRenderers[poolIndex];
            poolIndex = (poolIndex + 1) % PoolSize;

            float size = sparkSize * forceMul;

            go.transform.position = origin;
            go.transform.localScale = Vector3.one * size;
            sr.color = new Color(col.r, col.g, col.b, 1f);
            go.SetActive(true);

            // Random direction — full 360 degrees, weighted upward
            float angle = Random.Range(-160f, -20f); // mostly upward arc
            Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            float speed = Random.Range(sparkSpeed * 0.5f, sparkSpeed) * forceMul;

            StartCoroutine(AnimateSpark(go, sr, dir, speed, col, size));
        }
        yield break;
    }

    IEnumerator AnimateSpark(GameObject go, SpriteRenderer sr, Vector2 direction,
                             float speed, Color col, float size)
    {
        float elapsed  = 0f;
        Vector2 pos    = go.transform.position;
        float gravity  = -8f;

        while (elapsed < sparkLifetime)
        {
            float t = elapsed / sparkLifetime;

            // Move with gravity
            pos += direction * speed * Time.deltaTime;
            pos.y += gravity * elapsed * Time.deltaTime;
            go.transform.position = pos;

            // Shrink and fade
            float alpha = Mathf.Lerp(1f, 0f, Mathf.Pow(t, 0.5f));
            float scale = Mathf.Lerp(size, size * 0.2f, t);
            sr.color = new Color(col.r, col.g, col.b, alpha);
            go.transform.localScale = Vector3.one * scale;

            elapsed += Time.deltaTime;
            yield return null;
        }

        sr.color = new Color(col.r, col.g, col.b, 0f);
        go.SetActive(false);
    }

    // ── Flash ring ────────────────────────────────────────────────────────────

    IEnumerator DoFlashRing(Vector2 origin, Color col, float maxScale)
    {
        flashRingObj.transform.position = origin;
        flashRingObj.SetActive(true);
        flashRingObj.transform.localScale = Vector3.zero;

        float elapsed = 0f;
        while (elapsed < flashDuration)
        {
            float t     = elapsed / flashDuration;
            float scale = Mathf.SmoothStep(0f, maxScale, Mathf.Pow(t, 0.3f));
            float alpha = t < 0.15f
                ? Mathf.Lerp(0f, 1f, t / 0.15f)
                : Mathf.Lerp(1f, 0f, (t - 0.15f) / 0.85f);

            flashRingObj.transform.localScale = Vector3.one * scale;
            flashRingRenderer.color = new Color(col.r, col.g, col.b, alpha);

            elapsed += Time.deltaTime;
            yield return null;
        }

        flashRingObj.SetActive(false);
    }

    // ── Screen flash ──────────────────────────────────────────────────────────

    IEnumerator DoScreenFlash(float intensity)
    {
        if (screenFlashRenderer == null) yield break;

        screenFlashObj.SetActive(true);
        float elapsed = 0f;

        while (elapsed < screenFlashDuration)
        {
            float t     = elapsed / screenFlashDuration;
            float alpha = t < 0.2f
                ? Mathf.Lerp(0f, intensity, t / 0.2f)
                : Mathf.Lerp(intensity, 0f, (t - 0.2f) / 0.8f);
            screenFlashRenderer.color = new Color(1f, 1f, 1f, alpha);
            elapsed += Time.deltaTime;
            yield return null;
        }

        screenFlashRenderer.color = new Color(1f, 1f, 1f, 0f);
        screenFlashObj.SetActive(false);
    }

    // ── Build helpers ─────────────────────────────────────────────────────────

    void BuildSparkPool()
    {
        sparkPool      = new GameObject[PoolSize];
        sparkRenderers = new SpriteRenderer[PoolSize];

        for (int i = 0; i < PoolSize; i++)
        {
            var go = new GameObject("Spark_" + i);
            go.transform.SetParent(transform.parent);
            go.SetActive(false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = dotSprite;
            sr.color        = new Color(sparkColor.r, sparkColor.g, sparkColor.b, 0f);
            sr.sortingOrder = 10;

            sparkPool[i]      = go;
            sparkRenderers[i] = sr;
        }
    }

    void BuildFlashRing()
    {
        flashRingObj = new GameObject("BlockFlashRing");
        flashRingObj.transform.SetParent(transform.parent);
        flashRingObj.SetActive(false);

        flashRingRenderer         = flashRingObj.AddComponent<SpriteRenderer>();
        flashRingRenderer.sprite  = dotSprite;
        flashRingRenderer.color   = new Color(flashColor.r, flashColor.g, flashColor.b, 0f);
        flashRingRenderer.sortingOrder = 9;
    }

    void BuildScreenFlash()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        screenFlashObj = new GameObject("ScreenFlash");
        screenFlashObj.transform.SetParent(cam.transform, false);

        float camZ = cam.nearClipPlane + 0.1f;
        screenFlashObj.transform.localPosition = new Vector3(0, 0, camZ);

        // White 4x4 texture
        Texture2D whiteTex  = new Texture2D(4, 4);
        Color[] pixels      = new Color[16];
        for (int i = 0; i < 16; i++) pixels[i] = Color.white;
        whiteTex.SetPixels(pixels);
        whiteTex.Apply();

        Sprite whiteSprite = Sprite.Create(whiteTex, new Rect(0,0,4,4), new Vector2(0.5f,0.5f), 1f);

        screenFlashRenderer        = screenFlashObj.AddComponent<SpriteRenderer>();
        screenFlashRenderer.sprite = whiteSprite;
        screenFlashRenderer.color  = new Color(1f, 1f, 1f, 0f);
        screenFlashRenderer.sortingOrder = 100;

        float height = 2f * cam.orthographicSize;
        float width  = height * cam.aspect;
        screenFlashObj.transform.localScale = new Vector3(width * 1.2f, height * 1.2f, 1f);
        screenFlashObj.SetActive(false);
    }

    void SetupAudio()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
    }

    // ── Texture generator ─────────────────────────────────────────────────────

    Texture2D GenerateSoftDot(int size)
    {
        var tex    = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius   = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist  = Vector2.Distance(new Vector2(x, y), center);
                float t     = Mathf.Clamp01(1f - dist / radius);
                float alpha = Mathf.Pow(t, 1.8f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return tex;
    }

    void OnDestroy()
    {
        if (flashRingObj   != null) Destroy(flashRingObj);
        if (screenFlashObj != null) Destroy(screenFlashObj);
        if (sparkPool      != null)
            foreach (var go in sparkPool)
                if (go != null) Destroy(go);
    }
}