using UnityEngine;

/// <summary>
/// Attach to Player root. Reads IsBlocking / IsCrouchBlocking from PlayerMovement.
///
/// Owns three things:
///   1. Parry detection — a block landing in the first few frames of raising
///      guard counts as a parry.
///   2. Block registration — the single entry point that plays VFX and starts
///      the pushback, so every damage path behaves identically.
///   3. Parry counter-damage — a parried attacker takes the hit instead.
///
/// All damage paths funnel into RegisterBlock():
///   ProwlerAI / StalkerAI  -> TryBlockDamage(hitPoint, attacker)
///   DespairAI              -> PlayerHealth.TakeDamage(dmg, dir, attacker)
/// </summary>
public class PlayerGuard : MonoBehaviour
{
    [Header("References")]
    public LaserBlockEffect blockEffect;

    [Header("Parry Window")]
    [Tooltip("Seconds after raising guard during which a block counts as a parry.\nEnemy wind-ups are long (Despair lands damage 0.4s after the swing starts),\nso this needs to cover your reaction time PLUS the remaining wind-up.\nTurn on Log Guard Events and read the reported timing to tune it exactly.")]
    [Range(0.02f, 1.5f)]
    public float parryWindow = 0.45f;

    [Header("Parry Counter-Attack")]
    [Tooltip("A successful parry damages the attacker instead of you.")]
    public bool parryDamagesAttacker = true;

    [Tooltip("Damage dealt to the attacker on a successful parry.\nCompare against each enemy's maxHealth (all default to 60).")]
    public float parryCounterDamage = 25f;

    [Tooltip("Which death animation a parry kill plays.")]
    public EnemyDeathEffect.CutType parryCutType = EnemyDeathEffect.CutType.Horizontal;

    [Header("Guard Pushback")]
    [Tooltip("Slide backwards when an attack is blocked. Tuned on PlayerHealth.")]
    public bool slideOnBlock = true;

    [Header("Debug")]
    [Tooltip("Logs the EXACT time your guard had been up when each hit landed.\nIf it says 'block' with 0.42s, set parryWindow above 0.42.")]
    public bool logGuardEvents = true;
    [Tooltip("Draws a live on-screen readout of the parry window while playing.")]
    public bool showOnScreenDebug = true;

    private PlayerMovement playerMovement;
    private PlayerHealth playerHealth;
    private Animator animator;

    private float guardRaisedTime = -999f;
    private bool wasGuarding;

    // ── Debug readout ──
    private string lastResult = "-";
    private float  lastResultTime = -999f;
    private float  lastHeldFor;

    // Public so StalkerAI / ProwlerAI can check it
    public bool IsGuarding => playerMovement != null &&
        (playerMovement.IsBlocking || playerMovement.IsCrouchBlocking);

    /// <summary>
    /// True while the parry window is open. Uses unscaled time because HitStop
    /// sets timeScale to 0, which would otherwise stall the window mid-freeze.
    /// </summary>
    public bool IsParryWindowOpen =>
        IsGuarding && (Time.unscaledTime - guardRaisedTime) <= parryWindow;

    void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
        playerHealth   = GetComponent<PlayerHealth>();

        animator = GetComponent<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (blockEffect == null)
            blockEffect = GetComponentInChildren<LaserBlockEffect>();
    }

    void Update()
    {
        // Rising edge of the guard opens the parry window.
        bool guarding = IsGuarding;

        if (guarding && !wasGuarding)
        {
            guardRaisedTime = Time.unscaledTime;
            if (logGuardEvents) Debug.Log("[Guard] raised — parry window open");
        }

        wasGuarding = guarding;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ENTRY POINTS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Legacy overload — no attacker, so no counter-damage.</summary>
    public bool TryBlockDamage(Vector2 hitPoint)
    {
        return TryBlockDamage(hitPoint, null);
    }

    /// <summary>
    /// Called by ProwlerAI and StalkerAI before they apply damage.
    /// Returns true if blocked, which makes them skip TakeDamage entirely.
    /// </summary>
    public bool TryBlockDamage(Vector2 hitPoint, GameObject attacker)
    {
        if (!IsGuarding) return false;

        RegisterBlock(hitPoint, attacker);
        return true;
    }

    /// <summary>
    /// The single place a successful block is handled. Safe from any path.
    /// </summary>
    public void RegisterBlock(Vector2 hitPoint, GameObject attacker = null)
    {
        bool isParry = IsParryWindowOpen;

        if (blockEffect != null)
        {
            if (isParry) blockEffect.PlayParryEffect(hitPoint);
            else         blockEffect.PlayBlockEffect(hitPoint);
        }

        if (slideOnBlock && playerHealth != null)
            playerHealth.BeginGuardSlide(isParry);

        if (isParry)
        {
            // Consume the window so holding guard can't chain parries off one press.
            guardRaisedTime = -999f;

            if (parryDamagesAttacker && attacker != null)
                ApplyParryCounter(attacker);
        }

        // ── Timing diagnostic ──
        float heldFor = Time.unscaledTime - guardRaisedTime;
        lastHeldFor    = heldFor;
        lastResult     = isParry ? "PARRY" : "BLOCK";
        lastResultTime = Time.unscaledTime;

        if (logGuardEvents)
        {
            string who = attacker != null ? attacker.name : "unknown";
            if (isParry)
            {
                Debug.Log($"<color=cyan>[Guard] PARRY</color> vs {who} — guard was up {heldFor:F3}s (window {parryWindow:F2}s)");
            }
            else
            {
                float missedBy = heldFor - parryWindow;
                Debug.Log($"[Guard] block vs {who} — guard was up {heldFor:F3}s, " +
                          $"window is {parryWindow:F2}s. MISSED BY {missedBy:F3}s. " +
                          $"Set Parry Window above {heldFor:F2} to parry this attack.");
            }
        }
    }

    void OnGUI()
    {
        if (!showOnScreenDebug) return;

        GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };

        bool open = IsParryWindowOpen;
        style.normal.textColor = open ? Color.cyan : (IsGuarding ? Color.yellow : Color.gray);

        string status = !IsGuarding ? "guard down"
                      : open        ? $"PARRY WINDOW OPEN  ({parryWindow - (Time.unscaledTime - guardRaisedTime):F2}s left)"
                                    : "guarding (window closed)";

        GUI.Label(new Rect(12, 12, 700, 30), $"Guard: {status}", style);

        if (Time.unscaledTime - lastResultTime < 2f)
        {
            style.normal.textColor = lastResult == "PARRY" ? Color.cyan : Color.white;
            GUI.Label(new Rect(12, 40, 700, 30),
                      $"Last hit: {lastResult}  (guard was up {lastHeldFor:F3}s / window {parryWindow:F2}s)", style);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PARRY COUNTER-DAMAGE
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mirrors the dispatch order KatanaHitbox uses, so a parry kills exactly
    /// what a katana swing would. Searches parents too, since the collider that
    /// reached us may be a child of the enemy root.
    /// </summary>
    private void ApplyParryCounter(GameObject attacker)
    {
        // ── Despair — real health pool, takes a damage amount and knockback ──
        DespairAI despair = attacker.GetComponent<DespairAI>()
                         ?? attacker.GetComponentInParent<DespairAI>();
        if (despair != null)
        {
            despair.TakeParryCounter(parryCounterDamage, transform.position);
            if (logGuardEvents)
                Debug.Log($"[Parry] {parryCounterDamage} dmg -> DespairAI");
            return;
        }

        // ── Stalker ──
        StalkerHealth stalker = attacker.GetComponent<StalkerHealth>()
                             ?? attacker.GetComponentInParent<StalkerHealth>();
        if (stalker != null)
        {
            stalker.TakeDamage(parryCounterDamage, parryCutType);
            if (logGuardEvents)
                Debug.Log($"[Parry] {parryCounterDamage} dmg -> Stalker");
            return;
        }

        // ── Prowler ──
        ProwlerAI prowler = attacker.GetComponent<ProwlerAI>()
                         ?? attacker.GetComponentInParent<ProwlerAI>();
        if (prowler != null)
        {
            prowler.TakeDamage(parryCounterDamage, parryCutType, transform.position);
            if (logGuardEvents)
                Debug.Log($"[Parry] {parryCounterDamage} dmg -> Prowler");
            return;
        }

        if (logGuardEvents)
            Debug.Log($"[Parry] no known damage API on {attacker.name}");
    }
}