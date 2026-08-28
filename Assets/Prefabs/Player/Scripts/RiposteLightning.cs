// ============================================================
//  RiposteLightning.cs  –  Seventh Echo
//
//  Procedural lightning bolt for the parry -> vertical attack riposte.
//  Builds itself in code (LineRenderers + URP-safe material), so it
//  works with no art assets. Assign boltPrefab to use your own VFX
//  instead - if that field is set, the procedural bolt is skipped.
//
//  SETUP: add to the Player ROOT (same object as PlayerGuard).
//         PlayerGuard finds it automatically.
// ============================================================

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class RiposteLightning : MonoBehaviour
{
    [Header("Custom VFX (optional)")]
    [Tooltip("If set, this prefab is spawned at the target instead of the procedural bolt.")]
    public GameObject boltPrefab;
    public float prefabLifetime = 1.5f;

    [Header("Origin")]
    [Tooltip("Where the bolt is fired FROM. Drag your katana / hitbox / hand transform here.\nLeft empty, it auto-finds a child named Katana, KatanaHitbox, Sword or Hand,\nand falls back to a point in front of the player.")]
    public Transform originPoint;
    [Tooltip("Used only when originPoint is empty - offset ahead of the player, flipped with facing.")]
    public Vector2 fallbackOriginOffset = new Vector2(0.7f, 0.3f);

    [Header("Travel Through Target")]
    [Tooltip("How far the bolt continues PAST the enemy, so it pierces rather than stopping at them.")]
    public float overshoot = 2.5f;
    [Tooltip("Seconds for the bolt to extend from the katana to full length. 0 = instant.")]
    public float travelDuration = 0.06f;

    [Header("Bolt Shape")]
    [Tooltip("Zig-zag points along the main bolt. More = jaggier.")]
    public int segments = 14;
    [Tooltip("Max sideways deviation per segment.")]
    public float jaggedness = 0.45f;
    public float boltWidth = 0.18f;

    [Header("Branches")]
    public int branchCount = 3;
    public int branchSegments = 5;
    public float branchLength = 1.6f;
    [Range(0f, 1f)] public float branchWidthScale = 0.5f;

    [Header("Look")]
    public Color coreColor = new Color(1f, 1f, 1f, 1f);
    public Color glowColor = new Color(0.55f, 0.75f, 1f, 1f);
    [Tooltip("Glow bolt is drawn behind the core at this width multiplier.")]
    public float glowWidthScale = 3f;
    public int sortingOrder = 30;
    public string sortingLayerName = "Default";

    [Header("Timing")]
    [Tooltip("Total time the bolt stays on screen.")]
    public float duration = 0.35f;
    [Tooltip("How many times the bolt re-randomises while visible. 0 = static.")]
    public int flickerCount = 4;

    [Header("Impact")]
    public bool shakeCamera = true;
    public float shakeDuration = 0.18f;
    public float shakeMagnitude = 0.22f;
    public AudioClip strikeSound;
    [Range(0f, 1f)] public float strikeVolume = 0.9f;

    // ── Internals ──
    private Material lineMat;
    private AudioSource audioSource;
    private readonly List<LineRenderer> pool = new List<LineRenderer>();
    private Transform holder;
    private Transform graphicsT;

    void Awake()
    {
        lineMat = MakeUrpLineMaterial();

        Transform g = transform.Find("Graphics");
        graphicsT = g != null ? g : transform;

        // Auto-find a blade anchor if none was assigned.
        if (originPoint == null)
        {
            string[] names = { "Katana", "KatanaHitbox", "Sword", "Hand", "KatanaTip" };
            foreach (string n in names)
            {
                Transform found = FindDeep(transform, n);
                if (found != null) { originPoint = found; break; }
            }

            if (originPoint == null)
                Debug.LogWarning("[RiposteLightning] No origin transform found. Using the " +
                                 "fallback offset. Assign Origin Point to your katana for accuracy.");
        }

        holder = new GameObject("RiposteLightning_FX").transform;
        holder.position = Vector3.zero;

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
    }

    void OnDestroy()
    {
        if (holder != null) Destroy(holder.gameObject);
    }

    // ═════════════════════════════════════════════════════════
    //  PUBLIC ENTRY POINT
    // ═════════════════════════════════════════════════════════

    /// <summary>
    /// Fire lightning FROM the katana THROUGH a world position.
    /// The bolt overshoots the target so it reads as piercing, not landing.
    /// </summary>
    public void Strike(Vector3 target)
    {
        Vector3 origin = GetOrigin();

        // Continue past the target along the same heading.
        Vector3 dir = target - origin;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.right;
        dir.z = 0f;
        Vector3 far = target + dir.normalized * overshoot;

        if (boltPrefab != null)
        {
            GameObject go = Instantiate(boltPrefab, origin, Quaternion.identity);
            if (prefabLifetime > 0f) Destroy(go, prefabLifetime);
        }
        else
        {
            StopAllCoroutines();
            StartCoroutine(BoltRoutine(origin, far));
        }

        if (strikeSound != null)
            audioSource.PlayOneShot(strikeSound, strikeVolume);

        if (shakeCamera && CameraShake.Instance != null)
            CameraShake.Instance.Shake(shakeDuration, shakeMagnitude);
    }

    // ═════════════════════════════════════════════════════════
    //  PROCEDURAL BOLT
    // ═════════════════════════════════════════════════════════

    private IEnumerator BoltRoutine(Vector3 origin, Vector3 far)
    {
        // Extend from the blade outward before settling at full length.
        if (travelDuration > 0f)
        {
            float t = 0f;
            while (t < travelDuration)
            {
                t += Time.unscaledDeltaTime;
                float reach = Mathf.Clamp01(t / travelDuration);
                BuildBolt(origin, Vector3.Lerp(origin, far, reach));
                SetPoolAlpha(1f);
                yield return null;
            }
        }

        int flickers = Mathf.Max(1, flickerCount);
        float step = duration / flickers;

        for (int f = 0; f < flickers; f++)
        {
            BuildBolt(origin, far);

            // Fade the whole thing out over the last portion.
            float t = (float)f / flickers;
            float alpha = Mathf.Lerp(1f, 0.25f, t);
            SetPoolAlpha(alpha);

            yield return new WaitForSecondsRealtime(step);
        }

        HideAll();
    }

    private void BuildBolt(Vector3 origin, Vector3 end)
    {
        HideAll();

        int index = 0;

        // Main bolt runs blade -> through the target.
        Vector3[] main = MakeJaggedPath(origin, end, segments, jaggedness);

        DrawLine(index++, main, glowColor, boltWidth * glowWidthScale, sortingOrder);
        DrawLine(index++, main, coreColor, boltWidth,                  sortingOrder + 1);

        // Branches fork off random points along the main bolt.
        for (int b = 0; b < branchCount; b++)
        {
            int startIdx = Random.Range(2, Mathf.Max(3, main.Length - 2));
            Vector3 start = main[startIdx];

            // Fork perpendicular-ish to the beam so branches splay off its sides.
            Vector3 along = (main[main.Length - 1] - main[0]).normalized;
            Vector3 perp  = new Vector3(-along.y, along.x, 0f) * (Random.value < 0.5f ? -1f : 1f);

            Vector3 bend = (perp * Random.Range(0.5f, 1f) + along * Random.Range(0.2f, 0.7f)).normalized;
            Vector3 bEnd = start + bend * Random.Range(branchLength * 0.4f, branchLength);

            Vector3[] branch = MakeJaggedPath(start, bEnd, branchSegments, jaggedness * 0.7f);

            DrawLine(index++, branch, glowColor, boltWidth * glowWidthScale * branchWidthScale, sortingOrder);
            DrawLine(index++, branch, coreColor, boltWidth * branchWidthScale,                  sortingOrder + 1);
        }
    }

    /// <summary>Resolve where the bolt fires from.</summary>
    private Vector3 GetOrigin()
    {
        if (originPoint != null) return originPoint.position;

        float facing = graphicsT != null ? Mathf.Sign(graphicsT.localScale.x) : 1f;
        return transform.position + new Vector3(fallbackOriginOffset.x * facing,
                                               fallbackOriginOffset.y, 0f);
    }

    private Vector3[] MakeJaggedPath(Vector3 from, Vector3 to, int segs, float jag)
    {
        segs = Mathf.Max(2, segs);
        Vector3[] pts = new Vector3[segs + 1];

        for (int i = 0; i <= segs; i++)
        {
            float t = (float)i / segs;
            Vector3 p = Vector3.Lerp(from, to, t);

            // Endpoints stay put; the middle wanders. Taper so the strike
            // point stays accurate on the target.
            if (i != 0 && i != segs)
            {
                float taper = Mathf.Sin(t * Mathf.PI);
                p.x += Random.Range(-jag, jag) * taper;
                p.y += Random.Range(-jag, jag) * 0.35f * taper;
            }

            pts[i] = p;
        }

        return pts;
    }

    private void DrawLine(int index, Vector3[] pts, Color col, float width, int order)
    {
        LineRenderer lr = GetLine(index);

        lr.positionCount = pts.Length;
        lr.SetPositions(pts);
        lr.startWidth = width;
        lr.endWidth   = width * 0.55f;   // taper toward the far end
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
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;
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

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform r = FindDeep(root.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }

    private Material MakeUrpLineMaterial()
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (sh == null) sh = Shader.Find("Sprites/Default");

        Material m = new Material(sh);

        // Additive-ish so the bolt reads as light, not paint.
        if (m.HasProperty("_Surface"))  m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend"))    m.SetFloat("_Blend", 1f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        if (m.HasProperty("_ZWrite"))   m.SetFloat("_ZWrite", 0f);

        m.renderQueue = 3000;
        return m;
    }
}