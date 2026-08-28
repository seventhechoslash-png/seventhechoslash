// ============================================================
//  ElectricSlashArc.cs  –  Seventh Echo
//
//  Lightning that traces the SWING ARC of a katana slash, rather
//  than firing as a straight bolt. Several nested crescents sweep
//  from up-behind, over the head, down and forward through the
//  ground - following the blade path.
//
//  The arc is DRAWN PROGRESSIVELY over sweepDuration, so the
//  electricity chases the blade instead of appearing all at once.
//
//  SETUP: add to the Player ROOT. PlayerGuard drives it.
// ============================================================

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ElectricSlashArc : MonoBehaviour
{
    [Header("Pivot")]
    [Tooltip("Where the swing rotates around, local to the player and flipped with facing.\nRoughly the shoulders / hands.")]
    public Vector2 pivotOffset = new Vector2(0.0f, 1.7f);

    [Header("Arc Geometry")]
    [Tooltip("Number of nested crescents. 2-3 reads best.")]
    [Range(1, 6)] public int arcCount = 3;
    [Tooltip("Radius of the innermost arc. Roughly 0.8x character height.")]
    public float innerRadius = 2.2f;
    [Tooltip("Radius of the outermost arc. Roughly 1.2x character height, so the\ntop of the sweep clears the head and the bottom reaches the ground.")]
    public float outerRadius = 3.2f;
    [Tooltip("Points sampled along each arc. More = smoother crescent.")]
    [Range(8, 64)] public int arcResolution = 30;

    [Header("Sweep Angles (degrees, 0 = forward, 90 = straight up)")]
    [Tooltip("Where the swing STARTS. 150 = up and behind the head.")]
    public float startAngle = 140f;
    [Tooltip("Where the swing ENDS. -65 = down and forward into the ground.")]
    public float endAngle = -80f;

    [Header("Timing")]
    [Tooltip("Seconds for the arc to sweep from start to end. Match your V swing.")]
    public float sweepDuration = 0.22f;
    [Tooltip("Seconds the completed arc lingers before fading out.")]
    public float holdDuration = 0.12f;
    [Tooltip("How much of the TAIL fades behind the leading edge.\n0 = whole arc stays lit, 1 = only a short lick follows the blade.")]
    [Range(0f, 1f)] public float trailFalloff = 0.55f;
    [Tooltip("Times per second the jagged detail is regenerated.")]
    public float crackleRate = 30f;

    [Header("Lightning Detail")]
    [Tooltip("How far points deviate from the clean arc.")]
    public float jitter = 0.18f;
    [Tooltip("Smooth Perlin wobble along the arc, so it snakes rather than fuzzes.")]
    [Range(0f, 1f)] public float curl = 0.45f;
    public float curlFrequency = 0.35f;

    [Header("Branches")]
    [Tooltip("Small forks flying off the arc.")]
    [Range(0, 20)] public int branchCount = 6;
    public float branchLength = 0.7f;
    [Range(2, 8)] public int branchSegments = 4;

    [Header("Colour — HDR values with Bloom on")]
    [ColorUsage(true, true)] public Color coreColor  = new Color(3.0f, 3.0f, 3.2f, 1f);
    [ColorUsage(true, true)] public Color midColor   = new Color(0.6f, 1.6f, 3.0f, 1f);
    [ColorUsage(true, true)] public Color outerColor = new Color(1.4f, 0.5f, 2.6f, 1f);

    [Header("Widths")]
    public float coreWidth = 0.05f;
    public float midWidthScale = 2.4f;
    public float outerWidthScale = 5.0f;

    [Header("Rendering")]
    public int sortingOrder = 34;
    public string sortingLayerName = "Default";

    // ── Internals ──
    private Material lineMat;
    private Transform holder;
    private readonly List<LineRenderer> pool = new List<LineRenderer>();
    private Coroutine running;
    private Transform graphicsT;

    void Awake()
    {
        lineMat = MakeAdditiveMaterial();
        holder  = new GameObject("ElectricSlashArc_FX").transform;

        Transform g = transform.Find("Graphics");
        graphicsT = g != null ? g : transform;
    }

    void OnDestroy()
    {
        if (holder != null) Destroy(holder.gameObject);
    }

    // ═════════════════════════════════════════════════════════
    //  PUBLIC ENTRY
    // ═════════════════════════════════════════════════════════

    /// <summary>Sweep an electric arc along the katana's swing path.</summary>
    public void Slash()
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(SlashRoutine());
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

    private IEnumerator SlashRoutine()
    {
        float facing = graphicsT != null ? Mathf.Sign(graphicsT.localScale.x) : 1f;

        float total = sweepDuration + holdDuration;
        float elapsed = 0f;
        float nextCrackle = 0f;
        float progress = 0f;

        while (elapsed < total)
        {
            elapsed += Time.unscaledDeltaTime;

            // Leading edge of the sweep, eased so it accelerates like a swing.
            float rawT = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, sweepDuration));
            progress = rawT * rawT * (3f - 2f * rawT);   // smoothstep

            if (elapsed >= nextCrackle)
            {
                nextCrackle = elapsed + (1f / Mathf.Max(1f, crackleRate));
                Build(facing, progress);
            }

            // Fade the whole thing during the hold phase.
            float fade = 1f;
            if (elapsed > sweepDuration && holdDuration > 0f)
                fade = 1f - Mathf.Clamp01((elapsed - sweepDuration) / holdDuration);

            SetPoolAlpha(fade);

            yield return null;
        }

        HideAll();
        running = null;
    }

    // ═════════════════════════════════════════════════════════
    //  BUILD
    // ═════════════════════════════════════════════════════════

    private void Build(float facing, float progress)
    {
        HideAll();
        int index = 0;

        Vector3 pivot = transform.position + new Vector3(pivotOffset.x * facing, pivotOffset.y, 0f);

        // Leading edge angle, swept from start toward end.
        float a0 = startAngle;
        float a1 = Mathf.Lerp(startAngle, endAngle, progress);

        // Trail: only part of the swept arc stays lit behind the blade.
        float span = a1 - a0;
        float tailStart = a0 + span * trailFalloff;

        for (int a = 0; a < arcCount; a++)
        {
            float rt = arcCount == 1 ? 0.5f : (float)a / (arcCount - 1);
            float radius = Mathf.Lerp(innerRadius, outerRadius, rt);

            Vector3[] pts = BuildArc(pivot, radius, tailStart, a1, facing, a * 37f);
            if (pts.Length < 2) continue;

            // Outer arcs slightly thinner, so the crescent has depth.
            float w = coreWidth * Mathf.Lerp(1f, 0.65f, rt);

            DrawLine(index++, pts, outerColor, w * outerWidthScale, sortingOrder);
            DrawLine(index++, pts, midColor,   w * midWidthScale,   sortingOrder + 1);
            DrawLine(index++, pts, coreColor,  w,                   sortingOrder + 2);

            // Branches flying off this arc.
            int per = Mathf.Max(0, branchCount / arcCount);
            for (int b = 0; b < per; b++)
            {
                int at = Random.Range(1, pts.Length);
                Vector3 from = pts[at];

                // Fling outward from the pivot, roughly tangential.
                Vector3 outward = (from - pivot).normalized;
                Vector3 tangent = new Vector3(-outward.y, outward.x, 0f) * (Random.value < 0.5f ? 1f : -1f);
                Vector3 dir = (outward * Random.Range(0.4f, 1f) + tangent * Random.Range(0f, 0.7f)).normalized;

                Vector3[] br = BuildBranch(from, dir, branchLength * Random.Range(0.5f, 1f));

                DrawLine(index++, br, outerColor, w * outerWidthScale * 0.5f, sortingOrder);
                DrawLine(index++, br, midColor,   w * midWidthScale * 0.5f,   sortingOrder + 1);
                DrawLine(index++, br, coreColor,  w * 0.55f,                  sortingOrder + 2);
            }
        }
    }

    /// <summary>Sample a jagged arc between two angles.</summary>
    private Vector3[] BuildArc(Vector3 pivot, float radius, float fromDeg, float toDeg,
                               float facing, float seed)
    {
        int steps = Mathf.Max(2, arcResolution);
        List<Vector3> pts = new List<Vector3>(steps + 1);

        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            float deg = Mathf.Lerp(fromDeg, toDeg, t);

            // Facing flips the sweep so it mirrors correctly.
            float rad = deg * Mathf.Deg2Rad * facing;
            if (facing < 0f) rad = Mathf.PI - (deg * Mathf.Deg2Rad);

            // Smooth Perlin wobble on the radius, plus fine jitter.
            float n = Mathf.PerlinNoise(seed + t * (steps * curlFrequency), seed * 0.31f) - 0.5f;
            float r = radius + n * 2f * curl + Random.Range(-jitter, jitter);

            pts.Add(new Vector3(pivot.x + Mathf.Cos(rad) * r,
                                pivot.y + Mathf.Sin(rad) * r, 0f));
        }

        return pts.ToArray();
    }

    private Vector3[] BuildBranch(Vector3 from, Vector3 dir, float length)
    {
        int steps = Mathf.Max(2, branchSegments);
        Vector3[] pts = new Vector3[steps + 1];
        Vector3 pos = from;
        Vector2 d = dir;

        pts[0] = pos;
        float step = length / steps;

        for (int i = 1; i <= steps; i++)
        {
            d = (d + Random.insideUnitCircle * 0.5f).normalized;
            pos += (Vector3)(d * step);
            pts[i] = pos;
        }

        return pts;
    }

    // ═════════════════════════════════════════════════════════
    //  RENDER PLUMBING
    // ═════════════════════════════════════════════════════════

    private void DrawLine(int index, Vector3[] pts, Color col, float width, int order)
    {
        LineRenderer lr = GetLine(index);

        lr.positionCount = pts.Length;
        lr.SetPositions(pts);

        // Taper toward the leading edge so the arc looks like it is being cut.
        lr.widthCurve = AnimationCurve.EaseInOut(0f, 0.35f, 1f, 1f);
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
            GameObject go = new GameObject($"SlashArc_{pool.Count}");
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

#if UNITY_EDITOR
    [Header("Editor Preview")]
    [Tooltip("Draw the arc path in the Scene view so pivot, radius and angles can be\ntuned visually without entering Play mode.")]
    public bool previewGizmo = true;

    private void OnDrawGizmosSelected()
    {
        if (!previewGizmo) return;

        Transform g = transform.Find("Graphics");
        float facing = g != null ? Mathf.Sign(g.localScale.x) : 1f;

        Vector3 pivot = transform.position + new Vector3(pivotOffset.x * facing, pivotOffset.y, 0f);

        // Pivot marker
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(pivot, 0.12f);

        // Inner and outer sweeps, plus the mid arc
        for (int a = 0; a < 3; a++)
        {
            float rt = a / 2f;
            float radius = Mathf.Lerp(innerRadius, outerRadius, rt);

            Gizmos.color = a == 1
                ? new Color(0.4f, 1f, 0.4f, 1f)          // mid arc, bright
                : new Color(0.4f, 1f, 0.4f, 0.35f);      // bounds, faint

            Vector3 prev = Vector3.zero;
            int steps = 40;

            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float deg = Mathf.Lerp(startAngle, endAngle, t);

                float rad = deg * Mathf.Deg2Rad * facing;
                if (facing < 0f) rad = Mathf.PI - (deg * Mathf.Deg2Rad);

                Vector3 pt = new Vector3(pivot.x + Mathf.Cos(rad) * radius,
                                         pivot.y + Mathf.Sin(rad) * radius, 0f);

                if (i > 0) Gizmos.DrawLine(prev, pt);
                prev = pt;
            }
        }

        // Start and end markers, so sweep direction is unambiguous
        float r0 = startAngle * Mathf.Deg2Rad * facing;
        if (facing < 0f) r0 = Mathf.PI - (startAngle * Mathf.Deg2Rad);
        float r1 = endAngle * Mathf.Deg2Rad * facing;
        if (facing < 0f) r1 = Mathf.PI - (endAngle * Mathf.Deg2Rad);

        float rMid = (innerRadius + outerRadius) * 0.5f;

        Gizmos.color = Color.cyan;   // START
        Gizmos.DrawWireSphere(pivot + new Vector3(Mathf.Cos(r0) * rMid, Mathf.Sin(r0) * rMid, 0f), 0.18f);

        Gizmos.color = Color.red;    // END
        Gizmos.DrawWireSphere(pivot + new Vector3(Mathf.Cos(r1) * rMid, Mathf.Sin(r1) * rMid, 0f), 0.18f);
    }
#endif

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