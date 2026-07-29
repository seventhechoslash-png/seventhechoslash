// ============================================================
//  PlayerDustFX.cs  –  Seventh Echo   (v2)
//
//  Rock-dust system for the player. Three behaviors:
//    1. Dash scuff  – heel puffs kicked backward while dashing
//    2. Small landing – tight quick puff at the feet
//    3. Heavy landing – wide burst + grit chips (auto-detected
//       by fall height, no manual triggers needed)
//
//  v2 CHANGES:
//    - Dust particles now use 4 irregular Perlin-noise puff
//      shapes (random per particle) instead of perfect circles.
//      No more "tiny white balls".
//    - Every particle spawns with a random rotation and slowly
//      rotates over its lifetime.
//    - New "Dust Sharpness" slider: 0 = soft blurry cloud,
//      1 = crisp defined puff. Tune in Inspector.
//    - Dash emits small overlapping clusters so the trail reads
//      as flowing scuff dust instead of a row of dots.
//
//  SETUP:
//    1. Add this component to the Player ROOT GameObject
//       (same object as PlayerMovement / PlayerState).
//    2. Press Play. Everything builds itself in code.
//       URP-safe material, no sprites needed.
//
//  NOTE: Texture changes (Dust Sharpness) are baked at Awake,
//  so tweak the slider, then re-enter Play mode to see it.
// ============================================================

using UnityEngine;

public class PlayerDustFX : MonoBehaviour
{
    [Header("World Scale")]
    [Tooltip("Master multiplier for all sizes, offsets and speeds. Tune this FIRST if dust looks too small or too big.")]
    public float worldScale = 3f;

    [Header("Dust Look")]
    [Tooltip("0 = soft blurry cloud, 1 = crisp sharp puff. Applied at Play start.")]
    [Range(0f, 1f)] public float dustSharpness = 0.55f;

    [Header("Rendering")]
    public string sortingLayerName = "Default";
    [Tooltip("Set higher than the player's Order in Layer so dust renders in front. Set lower to render behind.")]
    public int sortingOrder = 15;

    [Header("Colors (rock dust)")]
    public Color dustColor = new Color(0.80f, 0.78f, 0.88f, 0.45f); // pale grey-lavender
    public Color gritColor = new Color(0.42f, 0.41f, 0.50f, 0.95f); // dark stone chips

    [Header("Landing Detection")]
    [Tooltip("Falls shorter than this produce NO dust (walking over bumps).")]
    public float minFallForDust = 1.0f;
    [Tooltip("Falls taller than this trigger the HEAVY landing. Falls in between trigger the small landing.")]
    public float heavyFallHeight = 5.0f;

    [Header("1. Dash Scuff")]
    [Tooltip("Seconds between heel emissions while dashing.")]
    public float dashEmitInterval = 0.045f;
    [Tooltip("Puffs emitted per tick. They overlap into a flowing trail.")]
    public int   dashClusterSize  = 2;
    public float dashDustSize     = 0.38f;
    public float dashDustLifetime = 0.45f;

    [Header("2. Small Landing")]
    public int   smallPuffCount    = 6;
    public float smallDustSize     = 0.35f;
    public float smallDustLifetime = 0.40f;

    [Header("3. Heavy Landing")]
    public int   heavyPuffCount    = 14;
    public float heavyDustSize     = 0.55f;
    public float heavyDustLifetime = 0.65f;
    [Tooltip("Small hard rock chips that pop out and fall with gravity. This is what sells 'solid stone'.")]
    public int   gritCount         = 8;
    public float gritSize          = 0.10f;
    public float gritLifetime      = 0.55f;

    // ── Internals ──
    private PlayerState        state;
    private Rigidbody2D        rb;
    private CapsuleCollider2D  capsule;

    private ParticleSystem psDust;   // soft irregular dust
    private ParticleSystem psGrit;   // hard chips with gravity

    private bool  wasGrounded = true;
    private float peakY;
    private float dashTimer;

    // ─────────────────────────────────────────────────────────
    private void Awake()
    {
        state   = GetComponent<PlayerState>();
        rb      = GetComponent<Rigidbody2D>();
        capsule = GetComponent<CapsuleCollider2D>();

        if (state == null)
        {
            Debug.LogError("[PlayerDustFX] No PlayerState found on this GameObject. " +
                           "Add PlayerDustFX to the Player root.");
            enabled = false;
            return;
        }

        BuildParticleSystems();
        peakY = transform.position.y;
    }

    private void Update()
    {
        bool grounded = state.isGrounded;

        // Track the highest point reached while airborne
        if (!grounded)
            peakY = Mathf.Max(peakY, transform.position.y);

        // Landing edge: airborne → grounded
        if (grounded && !wasGrounded)
            OnLanded();

        if (grounded)
            peakY = transform.position.y;

        wasGrounded = grounded;

        HandleDashDust();
    }

    // ─────────────────────────────────────────────────────────
    //  LANDINGS
    // ─────────────────────────────────────────────────────────
    private void OnLanded()
    {
        float fallHeight = peakY - transform.position.y;
        if (fallHeight < minFallForDust) return;

        if (fallHeight >= heavyFallHeight)
            EmitHeavyLanding();
        else
            EmitSmallLanding();
    }

    private void EmitSmallLanding()
    {
        Vector2 feet = FeetPosition();

        for (int i = 0; i < smallPuffCount; i++)
        {
            float offX = Random.Range(-0.35f, 0.35f) * worldScale;
            float dir  = Mathf.Sign(offX == 0f ? Random.Range(-1f, 1f) : offX);

            EmitDust(
                pos:      new Vector3(feet.x + offX, feet.y + 0.05f * worldScale, 0f),
                vel:      new Vector3(dir * Random.Range(0.8f, 2.0f) * worldScale,
                                      Random.Range(0.4f, 1.4f) * worldScale, 0f),
                size:     smallDustSize * worldScale * Random.Range(0.8f, 1.2f),
                lifetime: smallDustLifetime * Random.Range(0.85f, 1.15f)
            );
        }
    }

    private void EmitHeavyLanding()
    {
        Vector2 feet = FeetPosition();

        // Wide low dust wave spreading left + right along the ground
        for (int i = 0; i < heavyPuffCount; i++)
        {
            float offX = Random.Range(-0.9f, 0.9f) * worldScale;
            float dir  = Mathf.Sign(offX == 0f ? Random.Range(-1f, 1f) : offX);

            EmitDust(
                pos:      new Vector3(feet.x + offX, feet.y + 0.05f * worldScale, 0f),
                vel:      new Vector3(dir * Random.Range(2.0f, 4.5f) * worldScale,
                                      Random.Range(0.3f, 1.8f) * worldScale, 0f),
                size:     heavyDustSize * worldScale * Random.Range(0.75f, 1.25f),
                lifetime: heavyDustLifetime * Random.Range(0.85f, 1.15f)
            );
        }

        // Grit chips popping out and falling back down
        for (int i = 0; i < gritCount; i++)
        {
            float dir = Random.value < 0.5f ? -1f : 1f;

            var ep = new ParticleSystem.EmitParams
            {
                position      = new Vector3(feet.x + Random.Range(-0.3f, 0.3f) * worldScale,
                                            feet.y + 0.08f * worldScale, 0f),
                velocity      = new Vector3(dir * Random.Range(1.2f, 3.5f) * worldScale,
                                            Random.Range(2.0f, 4.5f) * worldScale, 0f),
                startSize     = gritSize * worldScale * Random.Range(0.7f, 1.4f),
                startLifetime = gritLifetime * Random.Range(0.8f, 1.2f),
                startColor    = gritColor,
                rotation      = Random.Range(0f, 360f)
            };
            psGrit.Emit(ep, 1);
        }
    }

    // ─────────────────────────────────────────────────────────
    //  DASH SCUFF
    // ─────────────────────────────────────────────────────────
    private void HandleDashDust()
    {
        if (!state.isDashing || !state.isGrounded) return;

        dashTimer -= Time.deltaTime;
        if (dashTimer > 0f) return;
        dashTimer = dashEmitInterval;

        // Backward = opposite of actual horizontal velocity.
        // Using velocity (not input) so it works regardless of axis quirks.
        float vx = rb != null ? rb.linearVelocity.x : 0f;
        if (Mathf.Abs(vx) < 0.05f) return;
        float back = -Mathf.Sign(vx);

        Vector2 feet = FeetPosition();

        // Emit a small cluster of overlapping puffs so the trail
        // reads as flowing scuff dust, not a row of dots.
        for (int i = 0; i < dashClusterSize; i++)
        {
            EmitDust(
                pos: new Vector3(
                    feet.x + back * Random.Range(0.05f, 0.30f) * worldScale,
                    feet.y + Random.Range(0.02f, 0.12f) * worldScale, 0f),
                vel: new Vector3(
                    back * Random.Range(1.2f, 3.2f) * worldScale,
                    Random.Range(0.3f, 1.3f) * worldScale, 0f),
                size:     dashDustSize * worldScale * Random.Range(0.65f, 1.35f),
                lifetime: dashDustLifetime * Random.Range(0.75f, 1.25f)
            );
        }
    }

    // ─────────────────────────────────────────────────────────
    //  SHARED DUST EMIT (random shape + random rotation)
    // ─────────────────────────────────────────────────────────
    private void EmitDust(Vector3 pos, Vector3 vel, float size, float lifetime)
    {
        var ep = new ParticleSystem.EmitParams
        {
            position      = pos,
            velocity      = vel,
            startSize     = size,
            startLifetime = lifetime,
            startColor    = dustColor,
            rotation      = Random.Range(0f, 360f)
        };
        psDust.Emit(ep, 1);
    }

    // ─────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────
    private Vector2 FeetPosition()
    {
        if (capsule != null)
            return new Vector2(capsule.bounds.center.x, capsule.bounds.min.y);
        return transform.position;
    }

    // ─────────────────────────────────────────────────────────
    //  PARTICLE SYSTEM CONSTRUCTION (all in code, URP-safe)
    // ─────────────────────────────────────────────────────────
    private void BuildParticleSystems()
    {
        // Dust: 2x2 atlas of 4 irregular noise puffs, random per particle
        psDust = CreateSystem("DustFX_Dust", MakeDustAtlas(256), gravity: 0.05f,
                              grow: true, useAtlas: true, rotateOverLife: true);

        // Grit: single small hard chip shape
        psGrit = CreateSystem("DustFX_Grit", MakeNoisePuff(32, 0.8f, 12.34f), gravity: 1.3f,
                              grow: false, useAtlas: false, rotateOverLife: true);
    }

    private ParticleSystem CreateSystem(string name, Texture2D tex, float gravity,
                                        bool grow, bool useAtlas, bool rotateOverLife)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        // Unity 6: must fully stop & clear before configuring modules
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake     = false;
        main.loop            = false;
        main.maxParticles    = 300;
        main.gravityModifier = gravity;
        main.startSpeed      = 0f; // velocities come from EmitParams

        // Emission fully manual
        var emission = ps.emission;
        emission.enabled = false;

        var shape = ps.shape;
        shape.enabled = false;

        // Fade out over lifetime
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.85f, 0.35f),
                    new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        // Dust grows as it disperses; grit stays solid
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        AnimationCurve curve = grow
            ? AnimationCurve.EaseInOut(0f, 0.7f, 1f, 1.35f)
            : AnimationCurve.Linear(0f, 1f, 1f, 0.9f);
        sol.size = new ParticleSystem.MinMaxCurve(1f, curve);

        // Slow random spin so puffs feel alive, not stamped
        if (rotateOverLife)
        {
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-60f * Mathf.Deg2Rad, 60f * Mathf.Deg2Rad);
        }

        // Random puff shape per particle via 2x2 texture atlas
        if (useAtlas)
        {
            var tsa = ps.textureSheetAnimation;
            tsa.enabled   = true;
            tsa.numTilesX = 2;
            tsa.numTilesY = 2;
            tsa.animation = ParticleSystemAnimationType.WholeSheet;
            // Pick a random frame and STAY on it (no animation)
            tsa.startFrame    = new ParticleSystem.MinMaxCurve(0f, 1f);
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            tsa.cycleCount    = 1;
        }

        // Renderer + URP-safe material
        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode       = ParticleSystemRenderMode.Billboard;
        rend.sortingLayerName = sortingLayerName;
        rend.sortingOrder     = sortingOrder;
        rend.material         = MakeUrpParticleMaterial(tex);

        return ps;
    }

    private Material MakeUrpParticleMaterial(Texture2D tex)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default"); // last resort

        Material mat = new Material(shader);

        // Force transparent alpha-blend surface (URP)
        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f); // 1 = Transparent
            mat.SetFloat("_Blend", 0f);   // 0 = Alpha
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
        }

        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        mat.mainTexture = tex;

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);

        return mat;
    }

    // ─────────────────────────────────────────────────────────
    //  TEXTURE GENERATION
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 2x2 atlas containing 4 different irregular dust puffs.
    /// Each particle randomly picks one tile.
    /// </summary>
    private Texture2D MakeDustAtlas(int atlasSize)
    {
        int tile = atlasSize / 2;
        Texture2D atlas = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, false);
        atlas.wrapMode = TextureWrapMode.Clamp;

        float[] seeds = { 3.7f, 17.2f, 42.9f, 88.5f };
        int s = 0;
        for (int ty = 0; ty < 2; ty++)
        {
            for (int tx = 0; tx < 2; tx++)
            {
                Texture2D puff = MakeNoisePuff(tile, dustSharpness, seeds[s++]);
                atlas.SetPixels(tx * tile, ty * tile, tile, tile, puff.GetPixels());
                Destroy(puff);
            }
        }

        atlas.Apply();
        return atlas;
    }

    /// <summary>
    /// Irregular dust puff: radial falloff whose edge radius is
    /// warped by Perlin noise (ragged silhouette) and whose interior
    /// density varies with a second noise layer (clumpy body).
    /// 'sharpness' 0..1 controls edge crispness.
    /// </summary>
    private Texture2D MakeNoisePuff(int size, float sharpness, float seed)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        float half = size * 0.5f;

        // Sharper = edge fade band gets narrower and starts later
        float edgeStart = Mathf.Lerp(0.25f, 0.62f, sharpness);
        float alphaPow  = Mathf.Lerp(2.2f, 1.15f, sharpness);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half + 0.5f) / half;   // -1..1
                float dy = (y - half + 0.5f) / half;
                float dist = Mathf.Sqrt(dx * dx + dy * dy); // 0..~1.41

                // Ragged silhouette: edge radius warped by angular noise
                float angle = Mathf.Atan2(dy, dx);
                float edgeNoise = Mathf.PerlinNoise(
                    seed + Mathf.Cos(angle) * 1.7f + 5f,
                    seed + Mathf.Sin(angle) * 1.7f + 5f);
                float maxR = Mathf.Lerp(0.62f, 1.0f, edgeNoise);

                float a = 1f - Mathf.InverseLerp(edgeStart * maxR, maxR, dist);
                a = Mathf.Clamp01(a);

                // Clumpy interior density
                float body = Mathf.PerlinNoise(
                    seed + x * (6f / size),
                    seed + y * (6f / size));
                a *= Mathf.Lerp(0.55f, 1f, body);

                a = Mathf.Pow(a, alphaPow);

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }

        tex.Apply();
        return tex;
    }
}