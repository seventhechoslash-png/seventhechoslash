// ============================================================
//  DreamWindVFX.cs  –  Seventh Echo  (v4)
//  3 layers: thin cyan streaks + tiny dot sparkles + slow motes
//  No billboard particles = no squares ever.
// ============================================================
using UnityEngine;

public class DreamWindVFX : MonoBehaviour
{
    public static DreamWindVFX Instance { get; private set; }

    [Header("Player")]
    public Transform   player;
    public Rigidbody2D playerRb;

    [Header("Wind Feel")]
    public float baseWindSpeed        = 4f;
    [Range(0f,1f)]
    public float playerSpeedInfluence = 0.3f;

    [Header("Gust Timing")]
    public float gustIntervalMin    = 5f;
    public float gustIntervalMax    = 12f;
    public float gustStrength       = 5f;
    public float gustDuration       = 1.4f;
    public float dashSpeedThreshold = 14f;

    private ParticleSystem _streaks;
    private ParticleSystem _motes;
    private ParticleSystem _sparkles;

    private ParticleSystem.VelocityOverLifetimeModule _streaksVel;
    private ParticleSystem.VelocityOverLifetimeModule _motesVel;
    private ParticleSystem.VelocityOverLifetimeModule _sparklesVel;

    private float _alpha=0f, _fadeTarget=0f, _fadeDur=0.8f;
    private bool  _fading=false;
    private float _gustTimer, _nextGust, _gustCur, _gustTarget;
    private bool  _gustActive;

    // cyan-white streaks, soft lavender motes, bright lavender sparkles
    static readonly Color CStreak = new Color(0.75f, 0.92f, 1.00f);
    static readonly Color CMote   = new Color(0.80f, 0.70f, 1.00f);
    static readonly Color CSpark  = new Color(0.85f, 0.75f, 1.00f);

    const float AStreak = 1.00f;
    const float AMote   = 1.00f;
    const float ASpark  = 1.00f;

    // ─────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        BuildStreaks();
        BuildMotes();
        BuildSparkles();
        SetAllAlpha(0f);
        _nextGust = Random.Range(gustIntervalMin, gustIntervalMax);
    }

    void Update()
    {
        DoFade();
        if (_alpha <= 0.01f) return;
        DoGust();
        DriveVelocity();
    }

    void LateUpdate()
    {
        Follow(_motes);
        Follow(_sparkles);
    }

    void Follow(ParticleSystem ps)
    {
        if (ps == null || player == null) return;
        ps.transform.position = Vector3.Lerp(
            ps.transform.position,
            new Vector3(player.position.x, player.position.y, 0f),
            Time.deltaTime * 5f);
    }

    // ══ LAYER 1 — WIND STREAKS ════════════════════════════════
    // Thin cyan-white horizontal lines. Stretch mode = never squares.
    void BuildStreaks()
    {
        _streaks = Make("WindStreaks");
        var m = _streaks.main;
        m.duration        = 5f;
        m.loop            = true;
        m.startLifetime   = MM(0.3f, 1.8f);
        m.startSpeed      = MM(5f, 18f);
        m.startSize       = MM(0.008f, 0.04f);   // very thin
        m.startColor      = AC(CStreak, AStreak);
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.maxParticles    = 280;
        m.gravityModifier = 0f;

        var e1 = _streaks.emission; e1.rateOverTime = 75f;

        var s = _streaks.shape;
        s.enabled   = true;
        s.shapeType = ParticleSystemShapeType.SingleSidedEdge;
        s.radius    = 14f;
        s.position  = new Vector3(16f, 0f, 0f);
        s.rotation  = new Vector3(0f, 0f, 90f);

        _streaksVel         = _streaks.velocityOverLifetime;
        _streaksVel.enabled = true;
        _streaksVel.space   = ParticleSystemSimulationSpace.World;
        SV(_streaksVel, -6f, -18f, -1.0f, 1.0f);

        AlphaLife(_streaks, CStreak, new[]{0f,0.05f,0.5f,0.95f,1f},
                                     new[]{0f,1f,   1f,  1f,   0f});

        var n = _streaks.noise;
        n.enabled          = true;
        n.strength         = 1.0f;
        n.frequency        = 0.5f;
        n.scrollSpeed      = 0.4f;
        n.octaveCount      = 2;
        n.octaveMultiplier = 0.5f;
        n.quality          = ParticleSystemNoiseQuality.Medium;

        var r = _streaks.GetComponent<ParticleSystemRenderer>();
        r.renderMode    = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.10f;
        r.lengthScale   = 3.5f;
        r.sortingOrder  = 3;
        r.material      = MakeMat(CStreak);

        _streaks.Play();
    }

    // ══ LAYER 2 — DUST MOTES ══════════════════════════════════
    // Tiny slow drifting specks. Stretch at near-zero speed = tiny dots.
    void BuildMotes()
    {
        _motes = Make("DustMotes");
        var m = _motes.main;
        m.duration        = 5f;
        m.loop            = true;
        m.startLifetime   = MM(2f, 6f);
        m.startSpeed      = MM(1.0f, 3.0f);
        m.startSize       = MM(0.06f, 0.14f);
        m.startColor      = AC(CMote, AMote);
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.maxParticles    = 200;
        m.gravityModifier = -0.01f;

        var e2 = _motes.emission; e2.rateOverTime = 40f;

        var s = _motes.shape;
        s.enabled   = true;
        s.shapeType = ParticleSystemShapeType.Box;
        s.scale     = new Vector3(32f, 16f, 1f);

        _motesVel         = _motes.velocityOverLifetime;
        _motesVel.enabled = true;
        _motesVel.space   = ParticleSystemSimulationSpace.World;
        SV(_motesVel, -0.5f, -1.5f, -0.1f, 0.2f);

        AlphaLife(_motes, CMote, new[]{0f,0.2f,0.8f,1f},
                                  new[]{0f,1f,  1f,  0f});

        var n = _motes.noise;
        n.enabled     = true;
        n.strength    = 0.4f;
        n.frequency   = 0.15f;
        n.scrollSpeed = 0.08f;
        n.quality     = ParticleSystemNoiseQuality.Low;

        var r = _motes.GetComponent<ParticleSystemRenderer>();
        r.renderMode    = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.03f;
        r.lengthScale   = 1.0f;
        r.sortingOrder  = 2;
        r.material      = MakeMat(CMote);

        _motes.Play();
    }

    // ══ LAYER 3 — SPARKLES ════════════════════════════════════
    // Same as motes but brighter, slightly faster — twinkling feel.
    void BuildSparkles()
    {
        _sparkles = Make("Sparkles");
        var m = _sparkles.main;
        m.duration        = 5f;
        m.loop            = true;
        m.startLifetime   = MM(1f, 3.5f);
        m.startSpeed      = MM(1.0f, 3.5f);
        m.startSize       = MM(0.05f, 0.12f);
        m.startColor      = AC(CSpark, ASpark);
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.maxParticles    = 160;
        m.gravityModifier = -0.015f;

        var e3 = _sparkles.emission; e3.rateOverTime = 30f;

        var s = _sparkles.shape;
        s.enabled   = true;
        s.shapeType = ParticleSystemShapeType.Box;
        s.scale     = new Vector3(22f, 10f, 1f);

        _sparklesVel         = _sparkles.velocityOverLifetime;
        _sparklesVel.enabled = true;
        _sparklesVel.space   = ParticleSystemSimulationSpace.World;
        SV(_sparklesVel, -0.3f, -1.2f, -0.15f, 0.25f);

        // Sparkles flicker — quick fade in/out
        AlphaLife(_sparkles, CSpark, new[]{0f,0.15f,0.5f,0.85f,1f},
                                      new[]{0f,1f,   0.6f,1f,   0f});

        var sOL = _sparkles.sizeOverLifetime;
        sOL.enabled = true;
        var c = new AnimationCurve();
        c.AddKey(0f,   0f);
        c.AddKey(0.3f, 1f);
        c.AddKey(0.7f, 0.7f);
        c.AddKey(1f,   0f);
        sOL.size = new ParticleSystem.MinMaxCurve(1f, c);

        var n = _sparkles.noise;
        n.enabled     = true;
        n.strength    = 0.35f;
        n.frequency   = 0.2f;
        n.scrollSpeed = 0.1f;

        var r = _sparkles.GetComponent<ParticleSystemRenderer>();
        r.renderMode    = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.02f;   // near zero = tiny dot
        r.lengthScale   = 1.0f;
        r.sortingOrder  = 1;
        r.material      = MakeMat(CSpark);

        _sparkles.Play();
    }

    // ══ RUNTIME ═══════════════════════════════════════════════

    void DoFade()
    {
        if (!_fading) return;
        _alpha = Mathf.MoveTowards(_alpha, _fadeTarget, Time.deltaTime / _fadeDur);
        SetAllAlpha(_alpha);
        if (Mathf.Approximately(_alpha, _fadeTarget)) _fading = false;
    }

    void DoGust()
    {
        _gustTimer += Time.deltaTime;
        float spd = playerRb ? Mathf.Abs(playerRb.linearVelocity.x) : 0f;
        if (!_gustActive && _gustTimer >= _nextGust)   TriggerGust(gustStrength);
        if (!_gustActive && spd >= dashSpeedThreshold) TriggerGust(gustStrength * 2f);
        _gustCur = Mathf.Lerp(_gustCur, _gustTarget, Time.deltaTime * (_gustActive ? 9f : 4f));
        if (_gustActive && _gustTimer >= _nextGust + gustDuration)
        {
            _gustActive=false; _gustTarget=0f;
            _nextGust = _gustTimer + Random.Range(gustIntervalMin, gustIntervalMax);
        }
    }

    void DriveVelocity()
    {
        float spd  = playerRb ? Mathf.Abs(playerRb.linearVelocity.x) : 0f;
        float wind = (baseWindSpeed + spd * playerSpeedInfluence + _gustCur) * _alpha;
        SV(_streaksVel,  -wind*1.2f, -wind*1.9f, -1.0f,  1.0f);
        SV(_motesVel,    -wind*0.2f, -wind*0.5f, -0.1f,  0.2f);
        SV(_sparklesVel, -wind*0.3f, -wind*0.6f, -0.15f, 0.25f);
    }

    void SetAllAlpha(float a)
    {
        SetPS(_streaks,  CStreak, AStreak * a);
        SetPS(_motes,    CMote,   AMote   * a);
        SetPS(_sparkles, CSpark,  ASpark  * a);
    }

    void SetPS(ParticleSystem ps, Color c, float a)
    {
        if (ps==null) return;
        var main = ps.main; main.startColor = AC(c, a);
    }

    // ══ PUBLIC ════════════════════════════════════════════════

    public void FadeIn(float dur=0.8f)  { _fadeTarget=1f; _fadeDur=dur; _fading=true; }
    public void FadeOut(float dur=1.2f) { _fadeTarget=0f; _fadeDur=dur; _fading=true; }

    public void TriggerGust(float strength)
    { _gustActive=true; _gustTarget=strength; _gustTimer=_nextGust; }

    // ══ HELPERS ═══════════════════════════════════════════════

    ParticleSystem Make(string n)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;
        return go.AddComponent<ParticleSystem>();
    }

    static ParticleSystem.MinMaxCurve MM(float a, float b) =>
        new ParticleSystem.MinMaxCurve(a, b);

    static Color AC(Color c, float a) { c.a=a; return c; }

    static void SV(ParticleSystem.VelocityOverLifetimeModule v,
                   float xMin, float xMax, float yMin, float yMax)
    {
        var z = new ParticleSystem.MinMaxCurve(0f, 0f);
        v.x=z; v.y=z; v.z=z;
        v.x = new ParticleSystem.MinMaxCurve(xMin, xMax);
        v.y = new ParticleSystem.MinMaxCurve(yMin, yMax);
        v.z = new ParticleSystem.MinMaxCurve(0f,   0f);
    }

    void AlphaLife(ParticleSystem ps, Color col, float[] times, float[] alphas)
    {
        var mod = ps.colorOverLifetime;
        mod.enabled = true;
        var g  = new Gradient();
        var ck = new GradientColorKey[]
        {
            new GradientColorKey(col, 0f),
            new GradientColorKey(col, 1f)
        };
        var ak = new GradientAlphaKey[times.Length];
        for (int i=0; i<times.Length; i++)
            ak[i] = new GradientAlphaKey(alphas[i], times[i]);
        g.SetKeys(ck, ak);
        mod.color = new ParticleSystem.MinMaxGradient(g);
    }

    // Creates a material with a procedural round soft-circle texture
    // so particles always look like dots — never squares.
    Material MakeMat(Color tint)
    {
        // 32x32 soft circle texture baked at runtime
        int size = 32;
        var tex  = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float half = size * 0.5f;
        for (int y=0; y<size; y++)
        for (int x=0; x<size; x++)
        {
            float dx   = (x - half) / half;
            float dy   = (y - half) / half;
            float dist = Mathf.Sqrt(dx*dx + dy*dy);
            float a    = Mathf.Clamp01(1f - dist);
            a          = a * a;   // smooth falloff
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();

        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                 ?? Shader.Find("Particles/Standard Unlit")
                 ?? Shader.Find("Sprites/Default");

        var mat = new Material(sh);
        if (mat.HasProperty("_BaseMap"))    mat.SetTexture("_BaseMap",    tex);
        if (mat.HasProperty("_MainTex"))    mat.SetTexture("_MainTex",    tex);
        if (mat.HasProperty("_Surface"))    mat.SetFloat("_Surface",      1f);
        if (mat.HasProperty("_SrcBlend"))   mat.SetFloat("_SrcBlend",     (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend"))   mat.SetFloat("_DstBlend",     (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.color        = new Color(tint.r, tint.g, tint.b, 1f);
        mat.renderQueue  = 3000;
        return mat;
    }
}
