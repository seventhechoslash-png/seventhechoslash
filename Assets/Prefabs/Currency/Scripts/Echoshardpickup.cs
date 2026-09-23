using System.Collections;
using UnityEngine;

/// <summary>
/// Echo Shard pickup with crackling electricity VFX.
/// No rotation. Lightning arcs drawn via LineRenderer, redrawn every frame for flicker.
/// </summary>
public class EchoShardPickup : MonoBehaviour
{
    [Header("Value")]
    public int shardValue = 1;

    [Header("Arc Animation")]
    public float arcHeight   = 1.4f;
    public float arcDuration = 0.38f;
    public float bounceDrop  = 0.35f;
    public float bounceDur   = 0.22f;

    [Header("Attraction")]
    public float attractRadius = 4f;
    public float attractSpeed  = 9f;
    public float collectRadius = 0.4f;

    [Header("Idle Float")]
    [Tooltip("Height of the up/down bob while idle.")]
    public float floatAmplitude = 0.12f;
    [Tooltip("Speed of the idle bob cycle.")]
    public float floatSpeed     = 2f;

    [Header("Idle Glow Pulse")]
    [Tooltip("Crystal subtly brightens and dims — keep this small (0.05–0.15).")]
    public float glowPulseAmount = 0.1f;
    public float glowPulseSpeed  = 4f;

    [Header("Electricity")]
    [Tooltip("Number of simultaneous lightning arcs around the shard.")]
    [Range(1, 6)]
    public int arcCount = 3;
    [Tooltip("Points per arc — more = smoother zigzag.")]
    [Range(3, 10)]
    public int arcPoints = 6;
    [Tooltip("Radius of the electricity field around the crystal.")]
    public float electricRadius = 0.28f;
    [Tooltip("How often arcs redraw (seconds). Lower = faster flicker.")]
    public float arcRedrawInterval = 0.04f;
    [Tooltip("Main electricity colour — bright electric blue/white.")]
    public Color electricColor     = new Color(0.55f, 0.85f, 1f, 1f);
    [Tooltip("Secondary colour for variety.")]
    public Color electricColorAlt  = new Color(1f, 1f, 1f, 0.85f);
    [Tooltip("Line width of each arc.")]
    public float arcWidth = 0.025f;
    [Tooltip("Chance each arc is hidden per redraw for flicker (0 = always on, 0.4 = 40% chance hidden).")]
    [Range(0f, 0.8f)]
    public float arcFlickerChance = 0.35f;

    [Header("Visuals")]
    public string sortingLayerName = "Default";
    public int    sortingOrder     = 10;

    [Header("VFX References (optional)")]
    public ParticleSystem sparkleVFX;
    public ParticleSystem collectBurstVFX;

    // ── internals ──────────────────────────────────────────────────────────
    private SpriteRenderer  sr;
    private Transform       player;
    private bool            settled   = false;
    private bool            collected = false;
    private Vector3         baseScale;
    private Vector3         landPos;
    private float           floatTimer;

    // Lightning
    private LineRenderer[]  lightningArcs;
    private float           arcTimer;

    // ── lifecycle ──────────────────────────────────────────────────────────

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr == null)
            sr = GetComponentInChildren<SpriteRenderer>(true);

        if (sr == null) { Debug.LogError($"[EchoShard] No SpriteRenderer on {name}!"); return; }

        var unlitMat = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
        if (unlitMat != null) sr.material = unlitMat;

        sr.sortingLayerName = sortingLayerName;
        sr.sortingOrder     = sortingOrder;
        sr.color            = Color.white;

        baseScale  = transform.localScale;
        floatTimer = Random.Range(0f, Mathf.PI * 2f);

        BuildLightningArcs();
    }

    void Start()
    {
        var playerGO = GameObject.FindGameObjectWithTag("Player");
        if (playerGO != null) player = playerGO.transform;

        float scatterX = Random.Range(-1.4f, 1.4f);
        StartCoroutine(ArcRoutine(scatterX));
    }

    // ── lightning arc setup ────────────────────────────────────────────────

    void BuildLightningArcs()
    {
        lightningArcs = new LineRenderer[arcCount];

        // Shared material for all arcs — additive-style bright line
        var mat = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));

        for (int i = 0; i < arcCount; i++)
        {
            var go = new GameObject($"LightArc_{i}");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace   = false;   // positions are local to the shard
            lr.positionCount   = arcPoints;
            lr.startWidth      = arcWidth;
            lr.endWidth        = arcWidth * 0.4f;
            lr.numCapVertices  = 2;
            lr.material        = mat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows  = false;

            // Sort in front of the crystal sprite
            lr.sortingLayerName = sortingLayerName;
            lr.sortingOrder     = sortingOrder + 1;

            // Start hidden — activated when shard settles
            lr.enabled = false;

            lightningArcs[i] = lr;
        }
    }

    void RedrawLightningArcs()
    {
        for (int i = 0; i < lightningArcs.Length; i++)
        {
            var lr = lightningArcs[i];
            if (lr == null) continue;

            // Flicker: randomly hide some arcs
            lr.enabled = (Random.value > arcFlickerChance);
            if (!lr.enabled) continue;

            // Pick alternating / random colour
            Color col = (i % 2 == 0) ? electricColor : electricColorAlt;
            col.a = Random.Range(0.6f, 1f);
            lr.startColor = col;
            lr.endColor   = new Color(col.r, col.g, col.b, 0.1f);

            // Random start and end angles around the shard
            float startAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float spanAngle  = Random.Range(80f, 220f) * Mathf.Deg2Rad;
            float endAngle   = startAngle + spanAngle;

            Vector3 startPt = new Vector3(
                Mathf.Cos(startAngle) * electricRadius,
                Mathf.Sin(startAngle) * electricRadius, 0f);
            Vector3 endPt = new Vector3(
                Mathf.Cos(endAngle) * electricRadius,
                Mathf.Sin(endAngle) * electricRadius, 0f);

            // Zigzag points between start and end
            for (int p = 0; p < arcPoints; p++)
            {
                float t       = (float)p / (arcPoints - 1);
                Vector3 along = Vector3.Lerp(startPt, endPt, t);

                if (p > 0 && p < arcPoints - 1)
                {
                    // Perpendicular direction for the zigzag offset
                    Vector3 dir  = (endPt - startPt).normalized;
                    Vector3 perp = new Vector3(-dir.y, dir.x, 0f);
                    float   off  = Random.Range(-electricRadius * 0.6f, electricRadius * 0.6f);
                    along += perp * off;
                }
                lr.SetPosition(p, along);
            }
        }
    }

    void HideAllArcs()
    {
        if (lightningArcs == null) return;
        foreach (var lr in lightningArcs)
            if (lr != null) lr.enabled = false;
    }

    // ── arc + bounce ───────────────────────────────────────────────────────

    IEnumerator ArcRoutine(float scatterX)
    {
        Vector3 startPos = transform.position;
        landPos = new Vector3(startPos.x + scatterX, startPos.y, startPos.z);

        // Phase 1: arc through air
        float elapsed = 0f;
        while (elapsed < arcDuration)
        {
            float t  = elapsed / arcDuration;
            float px = Mathf.Lerp(startPos.x, landPos.x, t);
            float py = Mathf.Lerp(startPos.y, landPos.y, t) + Mathf.Sin(t * Mathf.PI) * arcHeight;
            transform.position = new Vector3(px, py, startPos.z);
            elapsed += Time.deltaTime;
            yield return null;
        }
        transform.position = landPos;

        // Phase 2: bounce
        Vector3 bounceTop = landPos + new Vector3(0f, bounceDrop, 0f);
        float   halfDur   = bounceDur * 0.5f;

        elapsed = 0f;
        while (elapsed < halfDur)
        {
            transform.position = Vector3.Lerp(landPos, bounceTop, elapsed / halfDur);
            elapsed += Time.deltaTime;
            yield return null;
        }
        elapsed = 0f;
        while (elapsed < halfDur)
        {
            transform.position = Vector3.Lerp(bounceTop, landPos, elapsed / halfDur);
            elapsed += Time.deltaTime;
            yield return null;
        }
        transform.position = landPos;

        settled = true;

        if (sparkleVFX != null) sparkleVFX.Play();
    }

    // ── update ────────────────────────────────────────────────────────────

    void Update()
    {
        if (collected || sr == null) return;

        if (settled)
        {
            floatTimer += Time.deltaTime;

            // Bob up and down — no rotation
            float floatY = Mathf.Sin(floatTimer * floatSpeed) * floatAmplitude;
            Vector3 currentPos = new Vector3(landPos.x, landPos.y + floatY, landPos.z);
            transform.position = currentPos;

            // Subtle brightness pulse (white → slightly brighter white)
            float glow  = 1f + Mathf.Sin(floatTimer * glowPulseSpeed) * glowPulseAmount;
            sr.color    = new Color(glow, glow, glow, 1f);

            // Scale pulse (very subtle)
            float pulse = 1f + Mathf.Sin(floatTimer * 2.8f) * 0.05f;
            transform.localScale = baseScale * pulse;

            // Redraw lightning arcs on timer
            arcTimer -= Time.deltaTime;
            if (arcTimer <= 0f)
            {
                arcTimer = arcRedrawInterval + Random.Range(-0.01f, 0.01f);
                RedrawLightningArcs();
            }

            // Attract to player
            if (player != null)
            {
                float dist = Vector2.Distance(transform.position, player.position);
                if (dist < attractRadius)
                {
                    landPos = Vector3.MoveTowards(landPos, player.position, attractSpeed * Time.deltaTime);
                }
                if (dist < collectRadius)
                    Collect();
            }
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (collected || !settled) return;
        if (other.CompareTag("Player"))
            Collect();
    }

    // ── collection ─────────────────────────────────────────────────────────

    void Collect()
    {
        if (collected) return;
        collected = true;

        HideAllArcs();
        if (sparkleVFX != null) sparkleVFX.Stop();
        if (collectBurstVFX != null)
        {
            collectBurstVFX.transform.SetParent(null);
            collectBurstVFX.Play();
            Destroy(collectBurstVFX.gameObject, 2f);
        }

        Debug.Log($"[EchoShard] Collected! +{shardValue} shards");
        CurrencyManager.Instance?.AddShards(shardValue);
        Destroy(gameObject);
    }
}