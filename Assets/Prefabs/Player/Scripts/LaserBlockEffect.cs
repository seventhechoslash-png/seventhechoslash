using System.Collections;
using UnityEngine;

/// <summary>
/// Attach to a child GameObject on the Player (e.g., "KatanaBlockVFX").
/// Spawns a katana sword-strike spark + radial flash when a laser is blocked.
/// Works with URP - uses procedural textures, no legacy shader dependency.
/// </summary>
public class LaserBlockEffect : MonoBehaviour
{
    // ─── Inspector Settings ─────────────────────────────────────────────────

    [Header("Spark Burst (at contact point)")]
    [Tooltip("Color of the main spark burst - white/silver for steel, gold for powered")]
    public Color sparkColor = new Color(1f, 0.95f, 0.7f, 1f);   // warm white/gold
    public Color sparkCoreColor = new Color(1f, 1f, 1f, 1f);      // pure white core
    [Range(10, 60)]
    public int sparkCount = 28;
    [Range(0.05f, 0.4f)]
    public float sparkLifetime = 0.18f;
    [Range(2f, 12f)]
    public float sparkSpeed = 7f;

    [Header("Radial Flash (sword shine ring)")]
    public Color flashColor = new Color(0.9f, 0.95f, 1f, 0.85f); // icy blue-white
    [Range(0.05f, 0.5f)]
    public float flashDuration = 0.22f;
    [Range(0.2f, 3f)]
    public float flashMaxScale = 1.6f;

    [Header("Screen Glow (optional Camera flash)")]
    [Tooltip("If assigned, briefly brightens the screen on block")]
    public Camera mainCamera;
    [Range(0f, 1f)]
    public float screenFlashIntensity = 0.35f;
    [Range(0.05f, 0.3f)]
    public float screenFlashDuration = 0.12f;

    [Header("Audio")]
    [Tooltip("Optional: sword-block/clang sound")]
    public AudioClip blockSoundClip;
    [Range(0f, 1f)]
    public float blockSoundVolume = 0.8f;

    // ─── Private ────────────────────────────────────────────────────────────

    private ParticleSystem sparkParticles;
    private ParticleSystem glowParticles;
    private SpriteRenderer flashRing;
    private AudioSource audioSource;
    private Texture2D softCircleTex;

    // Screen flash overlay
    private GameObject screenFlashObj;
    private SpriteRenderer screenFlashRenderer;

    void Awake()
    {
        softCircleTex = GenerateSoftCircle(64);
        BuildSparkParticles();
        BuildGlowParticles();
        BuildFlashRing();
        BuildScreenFlash();
        SetupAudio();
    }

    // ─── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Call this when the laser hits a guarding player.
    /// hitPoint = world-space position of contact.
    /// </summary>
    public void PlayBlockEffect(Vector2 hitPoint)
    {
        transform.position = hitPoint;

        sparkParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        glowParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        sparkParticles.Play();
        glowParticles.Play();

        StopAllCoroutines();
        StartCoroutine(DoFlashRing());

        if (screenFlashRenderer != null)
            StartCoroutine(DoScreenFlash());

        if (blockSoundClip != null)
            audioSource.PlayOneShot(blockSoundClip, blockSoundVolume);
    }

    // ─── Build VFX pieces ───────────────────────────────────────────────────

    void BuildSparkParticles()
    {
        var go = new GameObject("BlockSparks");
        go.transform.SetParent(transform, false);

        sparkParticles = go.AddComponent<ParticleSystem>();
        sparkParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = sparkParticles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.05f;
        main.startLifetime = sparkLifetime;
        main.startSpeed = new ParticleSystem.MinMaxCurve(sparkSpeed * 0.5f, sparkSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.14f);
        main.startColor = new ParticleSystem.MinMaxGradient(sparkCoreColor, sparkColor);
        main.gravityModifier = 0.4f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = sparkCount + 10;

        var emission = sparkParticles.emission;
        emission.enabled = true;
        var burst = new ParticleSystem.Burst(0f, sparkCount);
        emission.SetBursts(new ParticleSystem.Burst[] { burst });

        var shape = sparkParticles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 75f;
        shape.radius = 0.01f;

        // Color fade out at end of lifetime
        var colorOverLifetime = sparkParticles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(sparkCoreColor, 0f),
                new GradientColorKey(sparkColor, 0.4f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.6f, 0.5f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(grad);

        // Size shrink over lifetime
        var sizeOverLifetime = sparkParticles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        var sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 1f);
        sizeCurve.AddKey(0.4f, 0.7f);
        sizeCurve.AddKey(1f, 0f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // Velocity over lifetime (required: all 3 axes same mode to avoid URP warning)
        var vel = sparkParticles.velocityOverLifetime;
        vel.enabled = true;
        var drag = new AnimationCurve();
        drag.AddKey(0f, 1f);
        drag.AddKey(1f, 0.1f);
        vel.x = new ParticleSystem.MinMaxCurve(1f, drag);
        vel.y = new ParticleSystem.MinMaxCurve(1f, drag);
        vel.z = new ParticleSystem.MinMaxCurve(1f, drag);

        // Renderer — procedural soft circle texture
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.material = MakeSoftCircleMaterial(sparkColor);
    }

    void BuildGlowParticles()
    {
        // Bigger, slower "bloom" particles that linger
        var go = new GameObject("BlockGlow");
        go.transform.SetParent(transform, false);

        glowParticles = go.AddComponent<ParticleSystem>();
        glowParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = glowParticles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.05f;
        main.startLifetime = sparkLifetime * 2.2f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        main.startColor = flashColor;
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 12;

        var emission = glowParticles.emission;
        emission.enabled = true;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 8) });

        var shape = glowParticles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.05f;

        var colorOverLifetime = glowParticles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(flashColor, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(grad);

        var sizeOverLifetime = glowParticles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        var sc = new AnimationCurve();
        sc.AddKey(0f, 0.3f);
        sc.AddKey(0.3f, 1f);
        sc.AddKey(1f, 0.5f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sc);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.material = MakeSoftCircleMaterial(flashColor);
    }

    void BuildFlashRing()
    {
        // A sprite ring that expands outward and fades — the "sword shine" radial burst
        var go = new GameObject("BlockFlashRing");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;

        flashRing = go.AddComponent<SpriteRenderer>();
        flashRing.sprite = Sprite.Create(
            softCircleTex,
            new Rect(0, 0, softCircleTex.width, softCircleTex.height),
            new Vector2(0.5f, 0.5f),
            100f
        );
        flashRing.color = new Color(flashColor.r, flashColor.g, flashColor.b, 0f);
        flashRing.sortingOrder = 10;

        // Use URP's Sprite-Lit-Default or Unlit/Transparent depending on your setup
        Material mat = new Material(Shader.Find("Sprites/Default"));
        if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
            mat = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default"));
        flashRing.material = mat;

        go.transform.localScale = Vector3.zero;
    }

    void BuildScreenFlash()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null) return;

        screenFlashObj = new GameObject("ScreenFlash");
        screenFlashObj.transform.SetParent(mainCamera.transform, false);

        // Place in front of camera in world space
        float camZ = mainCamera.nearClipPlane + 0.1f;
        screenFlashObj.transform.localPosition = new Vector3(0, 0, camZ);

        screenFlashRenderer = screenFlashObj.AddComponent<SpriteRenderer>();

        // Solid white 4x4 texture
        Texture2D whiteTex = new Texture2D(4, 4);
        Color[] pixels = new Color[16];
        for (int i = 0; i < 16; i++) pixels[i] = Color.white;
        whiteTex.SetPixels(pixels);
        whiteTex.Apply();

        screenFlashRenderer.sprite = Sprite.Create(
            whiteTex,
            new Rect(0, 0, 4, 4),
            new Vector2(0.5f, 0.5f),
            1f
        );

        // Scale to fill the viewport
        float height = 2f * mainCamera.orthographicSize;
        float width = height * mainCamera.aspect;
        screenFlashObj.transform.localScale = new Vector3(width * 1.2f, height * 1.2f, 1f);

        screenFlashRenderer.color = new Color(1f, 1f, 1f, 0f);
        screenFlashRenderer.sortingOrder = 100;

        Material mat = new Material(Shader.Find("Sprites/Default"));
        screenFlashRenderer.material = mat;
    }

    void SetupAudio()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
    }

    // ─── Coroutines ─────────────────────────────────────────────────────────

    IEnumerator DoFlashRing()
    {
        flashRing.transform.localScale = Vector3.zero;
        float elapsed = 0f;

        while (elapsed < flashDuration)
        {
            float t = elapsed / flashDuration;

            // Scale: quick expand then hold
            float scale = Mathf.SmoothStep(0f, flashMaxScale, Mathf.Pow(t, 0.35f));
            flashRing.transform.localScale = Vector3.one * scale;

            // Alpha: spike then fade
            float alpha = t < 0.15f
                ? Mathf.Lerp(0f, 1f, t / 0.15f)
                : Mathf.Lerp(1f, 0f, (t - 0.15f) / 0.85f);
            flashRing.color = new Color(flashColor.r, flashColor.g, flashColor.b, alpha);

            elapsed += Time.deltaTime;
            yield return null;
        }

        flashRing.color = new Color(flashColor.r, flashColor.g, flashColor.b, 0f);
        flashRing.transform.localScale = Vector3.zero;
    }

    IEnumerator DoScreenFlash()
    {
        float elapsed = 0f;
        while (elapsed < screenFlashDuration)
        {
            float t = elapsed / screenFlashDuration;
            float alpha = t < 0.2f
                ? Mathf.Lerp(0f, screenFlashIntensity, t / 0.2f)
                : Mathf.Lerp(screenFlashIntensity, 0f, (t - 0.2f) / 0.8f);
            screenFlashRenderer.color = new Color(1f, 1f, 1f, alpha);
            elapsed += Time.deltaTime;
            yield return null;
        }
        screenFlashRenderer.color = new Color(1f, 1f, 1f, 0f);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    Texture2D GenerateSoftCircle(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float t = Mathf.Clamp01(1f - dist / radius);
                // Smooth falloff: bright core, soft edges
                float alpha = Mathf.Pow(t, 1.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return tex;
    }

    Material MakeSoftCircleMaterial(Color tintColor)
    {
        // Try URP Particles/Additive first, fallback to legacy
        Shader sh = Shader.Find("Particles/Standard Unlit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");

        var mat = new Material(sh);
        mat.mainTexture = softCircleTex;

        // Additive blending for the glow/spark effect
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One); // Additive
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.renderQueue = 3000;

        mat.color = tintColor;
        return mat;
    }
}
