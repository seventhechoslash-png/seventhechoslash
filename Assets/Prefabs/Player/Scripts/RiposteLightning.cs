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

    [Header("Bolt Shape")]
    [Tooltip("How far above the target the bolt starts.")]
    public float strikeHeight = 7f;
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

    void Awake()
    {
        lineMat = MakeUrpLineMaterial();

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

    /// <summary>Strike lightning down onto a world position.</summary>
    public void Strike(Vector3 target)
    {
        if (boltPrefab != null)
        {
            GameObject go = Instantiate(boltPrefab, target, Quaternion.identity);
            if (prefabLifetime > 0f) Destroy(go, prefabLifetime);
        }
        else
        {
            StopAllCoroutines();
            StartCoroutine(BoltRoutine(target));
        }

        if (strikeSound != null)
            audioSource.PlayOneShot(strikeSound, strikeVolume);

        if (shakeCamera && CameraShake.Instance != null)
            CameraShake.Instance.Shake(shakeDuration, shakeMagnitude);
    }

    // ═════════════════════════════════════════════════════════
    //  PROCEDURAL BOLT
    // ═════════════════════════════════════════════════════════

    private IEnumerator BoltRoutine(Vector3 target)
    {
        int flickers = Mathf.Max(1, flickerCount);
        float step = duration / flickers;

        for (int f = 0; f < flickers; f++)
        {
            BuildBolt(target);

            // Fade the whole thing out over the last portion.
            float t = (float)f / flickers;
            float alpha = Mathf.Lerp(1f, 0.25f, t);
            SetPoolAlpha(alpha);

            yield return new WaitForSecondsRealtime(step);
        }

        HideAll();
    }

    private void BuildBolt(Vector3 target)
    {
        HideAll();

        Vector3 top = target + Vector3.up * strikeHeight;
        int index = 0;

        // Main bolt: glow pass behind, core pass in front.
        Vector3[] main = MakeJaggedPath(top, target, segments, jaggedness);

        DrawLine(index++, main, glowColor, boltWidth * glowWidthScale, sortingOrder);
        DrawLine(index++, main, coreColor, boltWidth,                  sortingOrder + 1);

        // Branches fork off random points along the main bolt.
        for (int b = 0; b < branchCount; b++)
        {
            int startIdx = Random.Range(2, Mathf.Max(3, main.Length - 2));
            Vector3 start = main[startIdx];

            float dir = Random.value < 0.5f ? -1f : 1f;
            Vector3 end = start + new Vector3(
                dir * Random.Range(branchLength * 0.4f, branchLength),
                -Random.Range(branchLength * 0.3f, branchLength * 0.9f), 0f);

            Vector3[] branch = MakeJaggedPath(start, end, branchSegments, jaggedness * 0.7f);

            DrawLine(index++, branch, glowColor, boltWidth * glowWidthScale * branchWidthScale, sortingOrder);
            DrawLine(index++, branch, coreColor, boltWidth * branchWidthScale,                  sortingOrder + 1);
        }
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
        lr.endWidth   = width * 0.55f;   // taper toward the ground
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
