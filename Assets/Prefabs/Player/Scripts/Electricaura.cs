// ============================================================
//  ElectricAura.cs  –  Seventh Echo
//
//  Recursive branching lightning that clings to a character.
//
//  v2: the previous version drew arcs along an ellipse, which read
//  as cartoon rings. Real lightning is a FORKING TREE - each branch
//  spawns smaller branches that taper as they go. This builds that
//  tree recursively, renders it in three stacked colour passes
//  (wide violet halo, cyan mid, thin white core), and scatters
//  spark motes around it.
//
//  FOR THE GLOW: enable Bloom on a URP Global Volume and keep the
//  HDR colours below at intensity > 1. Without bloom this looks
//  flat no matter what the parameters say.
//
//  SETUP: add to the Player ROOT. PlayerGuard drives it.
// ============================================================

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ElectricAura : MonoBehaviour
{
    public enum AuraMode
    {
        Outline,      // bolts root on the silhouette and radiate outward
        ThroughBody   // bolts root along the TOP edge and run down through the body
    }

    [Header("Mode")]
    [Tooltip("Outline = electricity wreathes around the body.\nThroughBody = bolts run top-to-bottom THROUGH the body.")]
    public AuraMode mode = AuraMode.Outline;

    [Header("Through Body Mode")]
    [Tooltip("Bolt length as a multiple of body HEIGHT. Above 1 exits past the feet.")]
    public float throughBodyLengthScale = 1.2f;
    [Tooltip("How much bolts drift sideways as they travel down. 0 = dead vertical.")]
    [Range(0f, 1f)] public float lateralWander = 0.30f;
    [Tooltip("Keep bolts inside the body silhouette instead of wandering off it.")]
    public bool clampToBody = true;
    public float clampPadding = 0.18f;
    [Tooltip("Pulls forks back toward vertical so bolts stay parallel.\n0 = forks splay freely, 1 = forks run straight down.")]
    [Range(0f, 1f)] public float downwardForkBias = 0.6f;

    [Header("Bolt Tree")]
    [Tooltip("Root bolts growing off the body at once.")]
    public int boltCount = 5;
    [Tooltip("How far a root bolt travels, in world units.")]
    public float boltLength = 1.6f;
    [Tooltip("Distance between points along a bolt. Smaller = finer detail.")]
    public float stepLength = 0.14f;
    [Tooltip("How much each step wanders. 0 = straight, 1 = chaotic.")]
    [Range(0f, 1f)] public float waviness = 0.45f;

    [Header("Forking")]
    [Tooltip("Levels of branching. 3 gives branches-of-branches-of-branches.")]
    [Range(0, 5)] public int maxDepth = 3;
    [Tooltip("Chance per step to fork a new branch.")]
    [Range(0f, 1f)] public float forkChance = 0.14f;
    [Tooltip("Child branch length as a fraction of its parent.")]
    [Range(0.2f, 0.9f)] public float forkLengthScale = 0.55f;
    [Tooltip("Max angle a fork deviates from its parent, degrees.")]
    public float forkAngle = 55f;

    [Header("Placement")]
    [Tooltip("Bolts root on the collider outline, padded by this.")]
    public float outlinePadding = 0.05f;
    [Tooltip("0 = bolts shoot straight out from the body, 1 = fully random direction.")]
    [Range(0f, 1f)] public float outwardBias = 0.7f;

    [Header("Focus Burst (striking hand)")]
    public bool showFocusBurst = true;
    [Tooltip("Normalised offset from centre. (0.5, 0.2) = half way to the right edge.")]
    public Vector2 focusOffset = new Vector2(0.5f, 0.2f);
    public int focusBolts = 4;
    [Tooltip("Focus bolts are this fraction of normal length - short and dense.")]
    [Range(0.2f, 1f)] public float focusLengthScale = 0.6f;

    [Header("Colour — use HDR values above 1 with Bloom on")]
    [ColorUsage(true, true)] public Color coreColor  = new Color(3.0f, 3.0f, 3.2f, 1f);
    [ColorUsage(true, true)] public Color midColor   = new Color(0.6f, 1.6f, 3.0f, 1f);
    [ColorUsage(true, true)] public Color outerColor = new Color(1.4f, 0.5f, 2.6f, 1f);
    [Tooltip("Fraction of bolts tinted with outerColor instead of midColor, for mixed hues.")]
    [Range(0f, 1f)] public float violetMix = 0.35f;

    [Header("Widths")]
    public float coreWidth = 0.035f;
    public float midWidthScale = 2.4f;
    public float outerWidthScale = 5.5f;
    [Tooltip("Width multiplier per branch level. 0.55 = each fork is a bit over half as thick.")]
    [Range(0.2f, 0.9f)] public float depthWidthFalloff = 0.55f;

    [Header("Sparks")]
    public int sparkCount = 14;
    public float sparkSize = 0.06f;
    public float sparkSpread = 1.3f;

    [Header("Timing")]
    [Tooltip("Times per second the whole tree is regenerated.")]
    public float crackleRate = 26f;
    [Range(0f, 1f)] public float fadeOutFraction = 0.35f;

    [Header("Rendering")]
    public int sortingOrder = 32;
    public string sortingLayerName = "Default";

    // ── Internals ──
    private struct Path
    {
        public Vector3[] pts;
        public int depth;
        public bool violet;
    }

    private Material lineMat;
    private Transform holder;
    private readonly List<LineRenderer> pool = new List<LineRenderer>();
    private readonly List<Path> paths = new List<Path>();
    private Bounds currentBounds;
    private Coroutine running;

    void Awake()
    {
        lineMat = MakeAdditiveMaterial();
        holder  = new GameObject("ElectricAura_FX").transform;
    }

    void OnDestroy()
    {
        if (holder != null) Destroy(holder.gameObject);
    }

    // ═════════════════════════════════════════════════════════
    //  PUBLIC ENTRY  (unchanged - PlayerGuard needs no edits)
    // ═════════════════════════════════════════════════════════

    public void Play(Transform target, float duration)
    {
        if (target == null) return;
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(AuraRoutine(target, duration));
    }

    /// <summary>
    /// Copy every tuning field from another aura. Used for runtime-created
    /// auras on enemies so ALL tuning lives on one Inspector component.
    /// Deliberately does NOT copy mode or boltCount - the caller sets those.
    /// </summary>
    public void CopyTuningFrom(ElectricAura src)
    {
        if (src == null) return;

        boltLength             = src.boltLength;
        stepLength             = src.stepLength;
        waviness               = src.waviness;

        maxDepth               = src.maxDepth;
        forkChance             = src.forkChance;
        forkLengthScale        = src.forkLengthScale;
        forkAngle              = src.forkAngle;

        outlinePadding         = src.outlinePadding;
        outwardBias            = src.outwardBias;

        throughBodyLengthScale = src.throughBodyLengthScale;
        lateralWander          = src.lateralWander;
        clampToBody            = src.clampToBody;
        clampPadding           = src.clampPadding;

        coreColor              = src.coreColor;
        midColor               = src.midColor;
        outerColor             = src.outerColor;
        violetMix              = src.violetMix;

        coreWidth              = src.coreWidth;
        midWidthScale          = src.midWidthScale;
        outerWidthScale        = src.outerWidthScale;
        depthWidthFalloff      = src.depthWidthFalloff;

        sparkCount             = src.sparkCount;
        sparkSize              = src.sparkSize;
        sparkSpread            = src.sparkSpread;

        crackleRate            = src.crackleRate;
        fadeOutFraction        = src.fadeOutFraction;

        sortingOrder           = src.sortingOrder;
        sortingLayerName       = src.sortingLayerName;
    }

    public void Stop()
    {
        if (running != null) StopCoroutine(running);
        running = null;
        HideAll();
    }

    // ═════════════════════════════════════════════════════════
    //  DRIVER
    // ═════════════════════════════════════════════════════════

    private IEnumerator AuraRoutine(Transform target, float duration)
    {
        float elapsed = 0f;
        float nextCrackle = 0f;

        // Unscaled, so a HitStop freeze does not stall the crackle.
        while (elapsed < duration && target != null)
        {
            elapsed += Time.unscaledDeltaTime;

            if (elapsed >= nextCrackle)
            {
                nextCrackle = elapsed + (1f / Mathf.Max(1f, crackleRate));
                Rebuild(GetBounds(target));
            }

            float remaining = 1f - (elapsed / duration);
            float fade = fadeOutFraction <= 0f ? 1f
                       : Mathf.Clamp01(remaining / fadeOutFraction);
            SetPoolAlpha(fade);

            yield return null;
        }

        HideAll();
        running = null;
    }

    private Bounds GetBounds(Transform target)
    {
        Collider2D c = target.GetComponent<Collider2D>() ?? target.GetComponentInChildren<Collider2D>();
        if (c != null) return c.bounds;

        SpriteRenderer sr = target.GetComponent<SpriteRenderer>() ?? target.GetComponentInChildren<SpriteRenderer>();
        if (sr != null) return sr.bounds;

        return new Bounds(target.position, Vector3.one);
    }

    // ═════════════════════════════════════════════════════════
    //  TREE GENERATION
    // ═════════════════════════════════════════════════════════

    private void Rebuild(Bounds b)
    {
        HideAll();
        paths.Clear();
        currentBounds = b;

        float rx = b.extents.x + outlinePadding;
        float ry = b.extents.y + outlinePadding;
        Vector3 c = b.center;

        if (mode == AuraMode.ThroughBody)
        {
            // Roots spread across the TOP edge, travelling straight down
            // through the silhouette so the body is conducting the charge.
            float len = b.size.y * throughBodyLengthScale;

            for (int i = 0; i < boltCount; i++)
            {
                // Even spread with jitter, so bolts cover the width instead of clumping.
                float t = (i + Random.Range(0.15f, 0.85f)) / boltCount;
                float x = Mathf.Lerp(b.min.x, b.max.x, t);

                Vector3 root = new Vector3(x, b.max.y + outlinePadding, 0f);
                Vector2 dir = new Vector2(Random.Range(-lateralWander, lateralWander), -1f).normalized;

                Grow(root, dir, len, 0, Random.value < violetMix);
            }
        }
        else
        {
            // Root bolts anchored around the silhouette, radiating outward.
            for (int i = 0; i < boltCount; i++)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                Vector3 root = new Vector3(c.x + Mathf.Cos(ang) * rx,
                                           c.y + Mathf.Sin(ang) * ry, 0f);

                Vector2 outward = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Vector2 dir = Vector2.Lerp(Random.insideUnitCircle.normalized, outward, outwardBias).normalized;

                Grow(root, dir, boltLength, 0, Random.value < violetMix);
            }
        }

        // Dense short burst at the hand. Outline mode only - a body being
        // conducted through has no charging fist.
        if (showFocusBurst && mode == AuraMode.Outline)
        {
            Vector3 focus = c + new Vector3(focusOffset.x * b.extents.x * 2f,
                                            focusOffset.y * b.extents.y * 2f, 0f);

            for (int i = 0; i < focusBolts; i++)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Grow(focus, dir, boltLength * focusLengthScale, 0, Random.value < violetMix);
            }
        }

        Render(c);
    }

    /// <summary>Random-walk a branch forward, forking as it goes.</summary>
    private void Grow(Vector3 pos, Vector2 dir, float length, int depth, bool violet)
    {
        int steps = Mathf.Max(3, Mathf.RoundToInt(length / Mathf.Max(0.02f, stepLength)));

        List<Vector3> pts = new List<Vector3>(steps + 1) { pos };
        Vector2 d = dir.normalized;

        for (int i = 0; i < steps; i++)
        {
            // Momentum plus deviation - this is what makes it wander like
            // lightning instead of zig-zagging like a sawtooth.
            d = (d + Random.insideUnitCircle * waviness).normalized;
            pos += (Vector3)(d * stepLength);

            // Keep the bolt inside the body so it reads as passing THROUGH it.
            if (clampToBody && mode == AuraMode.ThroughBody)
            {
                pos.x = Mathf.Clamp(pos.x,
                    currentBounds.min.x - clampPadding,
                    currentBounds.max.x + clampPadding);
            }

            pts.Add(pos);

            if (depth < maxDepth && Random.value < forkChance)
            {
                float a = Random.Range(-forkAngle, forkAngle) * Mathf.Deg2Rad;
                Vector2 fd = new Vector2(
                    d.x * Mathf.Cos(a) - d.y * Mathf.Sin(a),
                    d.x * Mathf.Sin(a) + d.y * Mathf.Cos(a));

                // In ThroughBody mode, bias forks back downward so the set stays
                // roughly parallel instead of sprawling into a bush.
                if (mode == AuraMode.ThroughBody)
                    fd = Vector2.Lerp(fd, Vector2.down, downwardForkBias).normalized;

                Grow(pos, fd, length * forkLengthScale, depth + 1, violet);
            }
        }

        paths.Add(new Path { pts = pts.ToArray(), depth = depth, violet = violet });
    }

    // ═════════════════════════════════════════════════════════
    //  RENDER
    // ═════════════════════════════════════════════════════════

    private void Render(Vector3 centre)
    {
        int index = 0;

        foreach (Path p in paths)
        {
            float w = coreWidth * Mathf.Pow(depthWidthFalloff, p.depth);
            Color mid = p.violet ? outerColor : midColor;

            // Three stacked passes: wide soft halo, mid hue, thin hot core.
            DrawLine(index++, p.pts, outerColor, w * outerWidthScale, sortingOrder);
            DrawLine(index++, p.pts, mid,        w * midWidthScale,   sortingOrder + 1);
            DrawLine(index++, p.pts, coreColor,  w,                   sortingOrder + 2);
        }

        // Spark motes drifting around the body.
        for (int s = 0; s < sparkCount; s++)
        {
            Vector3 a = centre + (Vector3)(Random.insideUnitCircle * sparkSpread);
            Vector3 bpt = a + (Vector3)(Random.insideUnitCircle.normalized * sparkSize);

            Color col = Random.value < violetMix ? outerColor : midColor;
            DrawLine(index++, new[] { a, bpt }, col, sparkSize * 0.9f, sortingOrder + 1);
        }
    }

    private void DrawLine(int index, Vector3[] pts, Color col, float width, int order)
    {
        LineRenderer lr = GetLine(index);

        lr.positionCount = pts.Length;
        lr.SetPositions(pts);

        // Taper toward the tip - thick at the root, hairline at the end.
        lr.widthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.25f);
        lr.widthMultiplier = width;

        lr.startColor = col;
        lr.endColor   = col;
        lr.sortingOrder = order;
        lr.sortingLayerName = sortingLayerName;
        lr.gameObject.SetActive(true);
    }

    private LineRenderer GetLine(int index)
    {
        while (pool.Count <= index)
        {
            GameObject go = new GameObject($"Bolt_{pool.Count}");
            go.transform.SetParent(holder, false);

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.material = lineMat;
            lr.useWorldSpace = true;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 4;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            go.SetActive(false);
            pool.Add(lr);
        }

        return pool[index];
    }

    private void SetPoolAlpha(float a)
    {
        foreach (LineRenderer lr in pool)
        {
            if (!lr.gameObject.activeSelf) continue;
            Color s = lr.startColor; s.a = a; lr.startColor = s;
            Color e = lr.endColor;   e.a = a; lr.endColor   = e;
        }
    }

    private void HideAll()
    {
        foreach (LineRenderer lr in pool)
            lr.gameObject.SetActive(false);
    }

    private Material MakeAdditiveMaterial()
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (sh == null) sh = Shader.Find("Sprites/Default");

        Material m = new Material(sh);

        if (m.HasProperty("_Surface"))  m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend"))    m.SetFloat("_Blend", 1f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        if (m.HasProperty("_ZWrite"))   m.SetFloat("_ZWrite", 0f);

        m.renderQueue = 3000;
        return m;
    }
}