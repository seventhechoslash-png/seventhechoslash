using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Electrocution + blast death effect, shared by ALL enemies (Stalker + Prowler).
/// Sequence:
///   1. Enemy flickers white/blue as if electrocuted (arcs flashing)
///   2. Bright blast flash + shockwave ring + screen flash
///   3. Enemy breaks into ember/ash fragments that fly out then fall under gravity
///   4. Ashes land on ground and continue to smolder/glow for a few seconds
///   5. Ashes fade out and the enemy object is destroyed
/// Pure sprite technique (no particle systems) so it's URP-safe and reliable
/// on enemies with offset Graphics children (the Prowler).
/// CutType kept for compatibility; not used by this effect.
/// </summary>
public class EnemyDeathEffect : MonoBehaviour
{
    public enum CutType { Horizontal, Vertical }

    [Header("Electrocution")]
    [Tooltip("How long the enemy shakes/flickers with electric arcs before blasting.")]
    public float electrocuteDuration = 0.18f;
    [Tooltip("How fast the sprite flickers between electric tints.")]
    public float flickerSpeed = 0.04f;
    [Tooltip("How much the sprite jitters while electrocuted.")]
    public float jitterAmount = 0.06f;
    public Color electricColorA = new Color(0.6f, 0.85f, 1f, 1f);  // pale blue
    public Color electricColorB = new Color(1f, 1f, 1f, 1f);       // white
    [Tooltip("Number of little electric arc lines flashing around the body.")]
    public int arcCount = 5;
    public Color arcColor = new Color(0.7f, 0.9f, 1f, 1f);

    [Header("Blast")]
    public float blastFlashDuration = 0.12f;
    public Color blastColor = new Color(0.8f, 0.95f, 1f, 1f);
    public float shockwaveMaxScale = 5f;
    public float shockwaveDuration = 0.35f;

    [Header("Screen Flash")]
    [Range(0f, 1f)] public float screenFlashIntensity = 0.4f;
    public float screenFlashDuration = 0.12f;

    [Header("Ash Fragments")]
    [Tooltip("How many ember/ash pieces the enemy breaks into.")]
    public int ashCount = 14;
    [Tooltip("Initial outward burst speed of the ashes.")]
    public float ashBurstSpeed = 7f;
    public float ashGravity = 18f;
    [Tooltip("Bounce damping when an ash hits the ground (0 = no bounce).")]
    [Range(0f, 0.8f)] public float ashBounce = 0.25f;
    public float ashMinSize = 0.12f;
    public float ashMaxSize = 0.32f;
    public Color ashHotColor = new Color(1f, 0.55f, 0.15f, 1f);   // glowing ember orange
    public Color ashCoolColor = new Color(0.15f, 0.15f, 0.18f, 1f); // burnt dark ash

    [Header("Ash Smolder")]
    [Tooltip("How long ashes glow/smolder on the ground before fading.")]
    public float smolderDuration = 2.5f;
    [Tooltip("How fast the ember glow pulses while smoldering.")]
    public float smolderPulseSpeed = 6f;

    [Header("Ground")]
    public LayerMask groundLayer;

    private SpriteRenderer enemySprite;
    private Transform graphics;
    private Texture2D dotTex;

    void Awake()
    {
        graphics = transform.Find("Graphics");
        if (graphics != null)
            enemySprite = graphics.GetComponent<SpriteRenderer>();
        if (enemySprite == null)
            enemySprite = GetComponentInChildren<SpriteRenderer>();

        dotTex = GenerateSoftDot(48);

        if (groundLayer.value == 0)
        {
            int idx = LayerMask.NameToLayer("Ground");
            if (idx >= 0) groundLayer = 1 << idx;
        }
    }

    public void PlayDeath(CutType cut)
    {
        StartCoroutine(DeathSequence());
    }

    IEnumerator DeathSequence()
    {
        if (enemySprite == null || enemySprite.sprite == null)
        {
            Destroy(gameObject);
            yield break;
        }

        // Capture sprite info before disabling
        Sprite sprite      = enemySprite.sprite;
        string sortLayer   = enemySprite.sortingLayerName;
        int sortOrder      = enemySprite.sortingOrder;
        Vector3 worldPos   = enemySprite.transform.position;
        Vector3 worldScale = enemySprite.transform.lossyScale;
        bool flipX         = enemySprite.flipX;
        Bounds bounds      = enemySprite.bounds;

        // Disable AI + physics + collider
        foreach (var mb in GetComponents<MonoBehaviour>())
            if (mb != this) mb.enabled = false;
        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;
        var rb = GetComponent<Rigidbody2D>();
        if (rb != null) { rb.linearVelocity = Vector2.zero; rb.simulated = false; }

        // Hide original, make a ghost we control
        enemySprite.enabled = false;
        var ghostGO = new GameObject("ElectroGhost");
        ghostGO.transform.position   = worldPos;
        ghostGO.transform.localScale = worldScale;
        var ghost = ghostGO.AddComponent<SpriteRenderer>();
        ghost.sprite           = sprite;
        ghost.flipX            = flipX;
        ghost.sortingLayerName = sortLayer;
        ghost.sortingOrder     = sortOrder;
        ghost.color            = Color.white;

        // ── Phase 1: Electrocution ──
        float t = 0f;
        float flickerTimer = 0f;
        bool useA = true;
        List<GameObject> arcs = new List<GameObject>();
        for (int i = 0; i < arcCount; i++)
        {
            var arc = new GameObject("Arc");
            var asr = arc.AddComponent<SpriteRenderer>();
            asr.sprite = Sprite.Create(dotTex, new Rect(0,0,48,48), new Vector2(0.5f,0.5f), 100f);
            asr.sortingLayerName = sortLayer;
            asr.sortingOrder = sortOrder + 3;
            asr.color = arcColor;
            arc.transform.localScale = new Vector3(0.08f, Random.Range(0.4f, 0.9f), 1f);
            arcs.Add(arc);
        }

        while (t < electrocuteDuration)
        {
            flickerTimer += Time.deltaTime;
            if (flickerTimer >= flickerSpeed)
            {
                flickerTimer = 0f;
                useA = !useA;
                ghost.color = useA ? electricColorA : electricColorB;

                // jitter the body
                ghostGO.transform.position = worldPos + new Vector3(
                    Random.Range(-jitterAmount, jitterAmount),
                    Random.Range(-jitterAmount, jitterAmount), 0f);

                // reposition arcs randomly around the body
                foreach (var arc in arcs)
                {
                    arc.transform.position = worldPos + new Vector3(
                        Random.Range(-bounds.extents.x, bounds.extents.x),
                        Random.Range(-bounds.extents.y, bounds.extents.y), 0f);
                    arc.transform.rotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
                    var asr = arc.GetComponent<SpriteRenderer>();
                    asr.enabled = Random.value > 0.3f; // flicker on/off
                }
            }
            t += Time.deltaTime;
            yield return null;
        }

        foreach (var arc in arcs) Destroy(arc);

        // ── Phase 2: Blast ──
        StartCoroutine(DoScreenFlash());
        StartCoroutine(Shockwave(worldPos, sortLayer, sortOrder + 2));

        // bright blast flash on the body then hide it
        ghost.color = blastColor;
        yield return new WaitForSeconds(blastFlashDuration);
        Destroy(ghostGO);

        // ── Phase 3 + 4: Ash fragments burst, fall, smolder ──
        float groundY = FindGroundY(worldPos, bounds.extents.y);
        List<Coroutine> ashRoutines = new List<Coroutine>();
        for (int i = 0; i < ashCount; i++)
        {
            Vector3 spawn = worldPos + new Vector3(
                Random.Range(-bounds.extents.x * 0.5f, bounds.extents.x * 0.5f),
                Random.Range(-bounds.extents.y * 0.5f, bounds.extents.y * 0.5f), 0f);
            StartCoroutine(AshFragment(spawn, groundY, sortLayer, sortOrder + 1));
        }

        // Wait for the ashes to finish smoldering before destroying enemy object
        yield return new WaitForSeconds(smolderDuration + 1.5f);

        Destroy(gameObject);
    }

    float FindGroundY(Vector3 from, float halfHeight)
    {
        RaycastHit2D hit = Physics2D.Raycast(from, Vector2.down, 30f, groundLayer);
        if (hit.collider != null) return hit.point.y;
        return from.y - halfHeight; // fallback
    }

    IEnumerator AshFragment(Vector3 start, float groundY, string sortLayer, int sortOrder)
    {
        var go = new GameObject("Ash");
        go.transform.position = start;
        float size = Random.Range(ashMinSize, ashMaxSize);
        go.transform.localScale = Vector3.one * size;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Sprite.Create(dotTex, new Rect(0,0,48,48), new Vector2(0.5f,0.5f), 100f);
        sr.sortingLayerName = sortLayer;
        sr.sortingOrder     = sortOrder;
        sr.color = ashHotColor;

        // initial outward velocity (mostly upward/outward)
        float angle = Random.Range(30f, 150f) * Mathf.Deg2Rad;
        float spd   = Random.Range(ashBurstSpeed * 0.4f, ashBurstSpeed);
        Vector2 vel = new Vector2(Mathf.Cos(angle) * Random.Range(-1f,1f) * spd,
                                  Mathf.Sin(angle) * spd);

        Vector3 pos = start;
        bool landed = false;
        float landTime = 0f;

        // ── Fly + fall under gravity until landing ──
        while (!landed)
        {
            vel.y -= ashGravity * Time.deltaTime;
            pos += (Vector3)(vel * Time.deltaTime);

            if (pos.y <= groundY)
            {
                pos.y = groundY;
                if (Mathf.Abs(vel.y) > 1f && ashBounce > 0f)
                {
                    vel.y = -vel.y * ashBounce;   // bounce
                    vel.x *= 0.6f;
                }
                else
                {
                    landed = true;
                    vel = Vector2.zero;
                }
            }

            go.transform.position = pos;
            go.transform.Rotate(0, 0, spd * 20f * Time.deltaTime);
            yield return null;
        }

        // ── Smolder on the ground: pulse between hot and cool, then fade ──
        while (landTime < smolderDuration)
        {
            float p = landTime / smolderDuration;
            // pulsing ember glow
            float pulse = (Mathf.Sin(landTime * smolderPulseSpeed) + 1f) * 0.5f;
            Color c = Color.Lerp(ashCoolColor, ashHotColor, pulse * (1f - p));
            // fade alpha over the full smolder
            c.a = Mathf.Lerp(1f, 0f, Mathf.Pow(p, 1.5f));
            sr.color = c;

            // ashes slowly shrink as they burn away
            float scl = size * Mathf.Lerp(1f, 0.5f, p);
            go.transform.localScale = Vector3.one * scl;

            landTime += Time.deltaTime;
            yield return null;
        }

        Destroy(go);
    }

    IEnumerator Shockwave(Vector3 pos, string sortLayer, int sortOrder)
    {
        var go = new GameObject("Shockwave");
        go.transform.position = pos;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Sprite.Create(dotTex, new Rect(0,0,48,48), new Vector2(0.5f,0.5f), 100f);
        sr.sortingLayerName = sortLayer;
        sr.sortingOrder     = sortOrder;

        float t = 0f;
        while (t < shockwaveDuration)
        {
            float p = t / shockwaveDuration;
            float scale = Mathf.Lerp(0.2f, shockwaveMaxScale, Mathf.Pow(p, 0.4f));
            go.transform.localScale = Vector3.one * scale;
            float a = Mathf.Lerp(0.9f, 0f, p);
            sr.color = new Color(blastColor.r, blastColor.g, blastColor.b, a);
            t += Time.deltaTime;
            yield return null;
        }
        Destroy(go);
    }

    IEnumerator DoScreenFlash()
    {
        Camera cam = Camera.main;
        if (cam == null) yield break;

        var go = new GameObject("DeathFlash");
        go.transform.SetParent(cam.transform, false);
        go.transform.localPosition = new Vector3(0, 0, cam.nearClipPlane + 0.1f);

        Texture2D wt = new Texture2D(4,4);
        Color[] px = new Color[16];
        for (int i=0;i<16;i++) px[i] = blastColor;
        wt.SetPixels(px); wt.Apply();

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Sprite.Create(wt, new Rect(0,0,4,4), new Vector2(0.5f,0.5f), 1f);
        sr.sortingOrder = 100;

        float height = 2f * cam.orthographicSize;
        float width  = height * cam.aspect;
        go.transform.localScale = new Vector3(width*1.2f, height*1.2f, 1f);

        float elapsed = 0f;
        while (elapsed < screenFlashDuration)
        {
            float p = elapsed / screenFlashDuration;
            float a = p < 0.2f ? Mathf.Lerp(0f, screenFlashIntensity, p/0.2f)
                               : Mathf.Lerp(screenFlashIntensity, 0f, (p-0.2f)/0.8f);
            sr.color = new Color(blastColor.r, blastColor.g, blastColor.b, a);
            elapsed += Time.deltaTime;
            yield return null;
        }
        Destroy(go);
    }

    Texture2D GenerateSoftDot(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Vector2 center = new Vector2(size/2f, size/2f);
        float radius = size/2f;
        for (int y=0;y<size;y++)
        for (int x=0;x<size;x++)
        {
            float dist = Vector2.Distance(new Vector2(x,y), center);
            float tt = Mathf.Clamp01(1f - dist/radius);
            tex.SetPixel(x,y,new Color(1f,1f,1f, Mathf.Pow(tt,1.6f)));
        }
        tex.Apply();
        return tex;
    }
}