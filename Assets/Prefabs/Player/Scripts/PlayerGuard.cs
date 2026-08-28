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

    [Header("Guard Impact Point")]
    [Tooltip("Where block/parry sparks appear. Drag a child marker at sword/chest height.\nLeft empty, the collider CENTRE is used - never the root pivot, which sits at\nthe feet and is why sparks appeared on the ground.")]
    public Transform guardPoint;
    [Tooltip("Ignore the hit point the enemy passes in. Enemies send player.position,\nwhich is the root pivot at ground level - almost never where a block lands.")]
    public bool overrideEnemyHitPoint = true;
    [Tooltip("Push the impact point this far toward the attacker, so the spark sits\nbetween the two bodies rather than inside yours.")]
    public float guardPointForwardOffset = 0.6f;
    [Tooltip("Extra height above the collider centre. Only used when guardPoint is empty.")]
    public float guardPointHeightOffset = 0.2f;

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

    [Header("Riposte (parry -> vertical attack)")]
    [Tooltip("Seconds after a successful parry during which pressing V counts as a riposte.")]
    [Range(0.1f, 2f)]
    public float riposteWindow = 0.7f;
    [Tooltip("Lightning strikes the enemy you parried. If it died, strikes in front of the player.")]
    public bool riposteStrikesParriedEnemy = true;
    [Tooltip("Fallback strike distance ahead of the player when there is no target.")]
    public float riposteFallbackDistance = 2f;
    [Tooltip("Extra damage dealt to the parried enemy on a riposte. 0 = VFX only.")]
    public float riposteBonusDamage = 0f;

    [Header("Riposte Aura (EWGF-style)")]
    [Tooltip("Charge crackles on the player/sword BEFORE the bolt leaves.")]
    public bool auraOnPlayer = true;
    [Tooltip("Keep this SHORT. A long player aura lingers after the bolt has left\nand reads as the lightning coming back to you.")]
    public float playerAuraDuration = 0.20f;
    [Tooltip("Lead time between the charge appearing and the bolt firing.\nThis is what makes the discharge read as leaving the sword.")]
    public float chargeLeadTime = 0.07f;
    [Tooltip("Wrap the PARRIED ENEMY too - the electricity crawls over their body.")]
    public bool auraOnEnemy = true;
    public float enemyAuraDuration = 0.8f;
    [Tooltip("Delay AFTER the bolt fires before the enemy lights up - the travel time.")]
    public float enemyAuraDelay = 0.06f;
    [Header("Slash Arc")]
    [Tooltip("Trace the electricity along the katana SWING ARC instead of firing a straight bolt.")]
    public bool useSlashArc = true;
    [Tooltip("Also fire the straight bolt. Usually off when the arc is on - two shapes fight.")]
    public bool useStraightBolt = false;

    [Header("Enemy Shock Reaction")]
    [Tooltip("Flash the struck enemy's sprite so it reads as being electrified.")]
    public bool enemyShockFlash = true;
    [ColorUsage(true, true)]
    public Color enemyShockColor = new Color(2.2f, 2.6f, 3.4f, 1f);
    public float enemyShockFlashDuration = 0.10f;
    [Tooltip("Number of bright/normal pulses while the enemy is being shocked.")]
    [Range(1, 8)] public int enemyShockPulses = 4;

    [Header("Scene Lighting")]
    [Tooltip("Throw real light into the scene when the bolt fires, so the discharge\nilluminates both characters instead of only being drawn over them.")]
    public bool flashSceneLight = true;
    public float lightFlashDuration = 0.35f;

    [Tooltip("Enemy bolts run top-to-bottom THROUGH the body rather than wreathing around it.")]
    public ElectricAura.AuraMode enemyAuraMode = ElectricAura.AuraMode.ThroughBody;
    [Tooltip("Bolts on the enemy. More than the player reads as being overwhelmed.")]
    public int enemyBoltCount = 7;

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

    // ── Riposte ──
    private PlayerState playerState;
    private RiposteLightning lightning;
    private ElectricAura playerAura;
    private ElectricFlashLight flashLight;
    private ElectricSlashArc slashArc;
    private float lastParryTime = -999f;
    private GameObject lastParriedAttacker;
    private bool wasVerticalAttacking;
    private Transform graphics;
    private Collider2D ownCollider;

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

        ownCollider = GetComponent<Collider2D>();
        if (ownCollider == null) ownCollider = GetComponentInChildren<Collider2D>();

        if (guardPoint == null)
        {
            Transform found = transform.Find("Graphics/KatanaTip") ?? transform.Find("KatanaTip");
            if (found != null) guardPoint = found;
        }

        playerState = GetComponent<PlayerState>();
        lightning   = GetComponent<RiposteLightning>();
        playerAura  = GetComponent<ElectricAura>();
        flashLight  = GetComponent<ElectricFlashLight>();
        slashArc    = GetComponent<ElectricSlashArc>();

        // Read facing straight off the sprite so this does not depend on
        // PlayerMovement exposing a Facing accessor.
        Transform g = transform.Find("Graphics");
        if (g != null) graphics = g;
        else
        {
            SpriteRenderer srr = GetComponentInChildren<SpriteRenderer>();
            graphics = srr != null ? srr.transform : transform;
        }

        if (lightning == null)
            Debug.LogWarning("[PlayerGuard] No RiposteLightning component found on the player. " +
                             "Add it to enable the parry -> vertical attack lightning.");
    }

    /// <summary>True while a parry riposte can still be triggered.</summary>
    public bool IsRiposteWindowOpen =>
        (Time.unscaledTime - lastParryTime) <= riposteWindow;

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

        CheckRiposte();
    }

    /// <summary>
    /// Watches for the vertical attack starting inside the riposte window.
    /// Reads PlayerState.isVerticalAttacking, so PlayerCombat needs no changes.
    /// </summary>
    private void CheckRiposte()
    {
        if (playerState == null) return;

        bool vertical = playerState.isVerticalAttacking;

        // Rising edge only - one riposte per swing.
        if (vertical && !wasVerticalAttacking && IsRiposteWindowOpen)
            FireRiposte();

        wasVerticalAttacking = vertical;
    }

    private void FireRiposte()
    {
        lastParryTime = -999f;   // consume the window

        Vector3 target;

        if (riposteStrikesParriedEnemy && lastParriedAttacker != null)
        {
            target = lastParriedAttacker.transform.position;
        }
        else
        {
            float facing = graphics != null ? Mathf.Sign(graphics.localScale.x) : 1f;
            target = transform.position + Vector3.right * facing * riposteFallbackDistance;
        }

        StartCoroutine(RiposteSequence(target, lastParriedAttacker));

        if (riposteBonusDamage > 0f && lastParriedAttacker != null)
            ApplyParryCounterDamage(lastParriedAttacker, riposteBonusDamage);

        if (logGuardEvents)
            Debug.Log($"<color=cyan>[Guard] RIPOSTE</color> lightning at {target}");
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

        Vector3 fxPoint = overrideEnemyHitPoint
            ? ResolveGuardPoint(attacker)
            : (Vector3)hitPoint;

        if (blockEffect != null)
        {
            if (isParry) blockEffect.PlayParryEffect(fxPoint);
            else         blockEffect.PlayBlockEffect(fxPoint);
        }

        if (slideOnBlock && playerHealth != null)
            playerHealth.BeginGuardSlide(isParry);

        if (isParry)
        {
            // Consume the window so holding guard can't chain parries off one press.
            guardRaisedTime = -999f;

            lastParryTime       = Time.unscaledTime;
            lastParriedAttacker = attacker;

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

    /// <summary>
    /// Where the block visually happened. Enemies pass their own idea of a hit
    /// point, but ProwlerAI sends player.position and DespairAI derives from
    /// transform.position - both the root pivot at the feet. This computes it
    /// from the collider instead, nudged toward the attacker.
    /// </summary>
    private Vector3 ResolveGuardPoint(GameObject attacker)
    {
        Vector3 basePos;

        if (guardPoint != null)
        {
            basePos = guardPoint.position;
        }
        else if (ownCollider != null)
        {
            basePos = ownCollider.bounds.center + Vector3.up * guardPointHeightOffset;
        }
        else
        {
            basePos = transform.position + Vector3.up * 1.5f;
        }

        // Nudge toward the attacker so the spark sits between the two bodies.
        if (attacker != null && guardPointForwardOffset != 0f)
        {
            Vector3 dir = attacker.transform.position - basePos;
            dir.z = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                basePos += dir.normalized * guardPointForwardOffset;
        }
        else if (guardPointForwardOffset != 0f)
        {
            // No attacker reference - fall back to facing.
            float facing = graphics != null ? Mathf.Sign(graphics.localScale.x) : 1f;
            basePos += Vector3.right * facing * guardPointForwardOffset;
        }

        basePos.z = 0f;
        return basePos;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PARRY COUNTER-DAMAGE
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mirrors the dispatch order KatanaHitbox uses, so a parry kills exactly
    /// what a katana swing would. Searches parents too, since the collider that
    /// reached us may be a child of the enemy root.
    /// </summary>
    /// <summary>
    /// Three beats, in order, so the discharge reads as leaving the sword:
    ///   1. charge crackles on the player
    ///   2. bolt fires from the katana toward the enemy
    ///   3. enemy's body conducts it
    /// Firing these simultaneously is what made it look like the lightning
    /// travelled to the enemy and then came back.
    /// </summary>
    private System.Collections.IEnumerator RiposteSequence(Vector3 target, GameObject attacker)
    {
        // ── Beat 1: charge on the player / sword ──
        if (auraOnPlayer && playerAura != null)
            playerAura.Play(transform, playerAuraDuration);

        if (chargeLeadTime > 0f)
            yield return new WaitForSecondsRealtime(chargeLeadTime);

        // ── Beat 2: the electricity follows the blade ──
        if (useSlashArc && slashArc != null)
            slashArc.Slash();

        if (useStraightBolt && lightning != null)
            lightning.Strike(target);

        // Light the scene from the impact point, not the player - the discharge
        // is brightest where it lands.
        if (flashSceneLight && flashLight != null)
            flashLight.Flash(target, lightFlashDuration);

        if (enemyAuraDelay > 0f)
            yield return new WaitForSecondsRealtime(enemyAuraDelay);

        // ── Beat 3: the enemy conducts ──
        if (enemyShockFlash && attacker != null)
            StartCoroutine(ShockFlash(attacker));

        if (auraOnEnemy && attacker != null)
            yield return EnemyAura(attacker.transform);
    }

    /// <summary>
    /// Pulses the enemy's sprite bright blue-white, so the body visibly reacts
    /// to the current instead of only having arcs drawn over it.
    /// </summary>
    private System.Collections.IEnumerator ShockFlash(GameObject enemy)
    {
        SpriteRenderer sr = enemy.GetComponent<SpriteRenderer>();
        if (sr == null) sr = enemy.GetComponentInChildren<SpriteRenderer>();
        if (sr == null) yield break;

        Color original = sr.color;
        float half = enemyShockFlashDuration * 0.5f;

        for (int i = 0; i < enemyShockPulses; i++)
        {
            if (sr == null) yield break;

            sr.color = enemyShockColor;
            yield return new WaitForSecondsRealtime(half);

            if (sr == null) yield break;

            sr.color = original;
            yield return new WaitForSecondsRealtime(half);
        }

        if (sr != null) sr.color = original;
    }

    /// <summary>
    /// Enemies rarely carry their own ElectricAura, so one is added on demand
    /// and configured to match the player's, then left on the enemy for reuse.
    /// </summary>
    private System.Collections.IEnumerator EnemyAura(Transform enemy)
    {
        if (enemy == null) yield break;

        ElectricAura aura = enemy.GetComponent<ElectricAura>();
        if (aura == null)
        {
            aura = enemy.gameObject.AddComponent<ElectricAura>();

            aura.CopyTuningFrom(playerAura);

            // The enemy is being shocked, not charging a punch — no hand burst.
            aura.showFocusBurst = false;
        }

        // Re-applied every riposte so Inspector tweaks take effect immediately.
        aura.CopyTuningFrom(playerAura);
        aura.mode           = enemyAuraMode;
        aura.boltCount      = enemyBoltCount;
        aura.showFocusBurst = false;

        aura.Play(enemy, enemyAuraDuration);
    }

    private void ApplyParryCounter(GameObject attacker)
    {
        ApplyParryCounterDamage(attacker, parryCounterDamage);
    }

    private void ApplyParryCounterDamage(GameObject attacker, float damage)
    {
        // ── Despair — real health pool, takes a damage amount and knockback ──
        DespairAI despair = attacker.GetComponent<DespairAI>()
                         ?? attacker.GetComponentInParent<DespairAI>();
        if (despair != null)
        {
            despair.TakeParryCounter(damage, transform.position);
            if (logGuardEvents)
                Debug.Log($"[Parry] {damage} dmg -> DespairAI");
            return;
        }

        // ── Stalker ──
        StalkerHealth stalker = attacker.GetComponent<StalkerHealth>()
                             ?? attacker.GetComponentInParent<StalkerHealth>();
        if (stalker != null)
        {
            stalker.TakeDamage(damage, parryCutType);
            if (logGuardEvents)
                Debug.Log($"[Parry] {damage} dmg -> Stalker");
            return;
        }

        // ── Prowler ──
        ProwlerAI prowler = attacker.GetComponent<ProwlerAI>()
                         ?? attacker.GetComponentInParent<ProwlerAI>();
        if (prowler != null)
        {
            prowler.TakeDamage(damage, parryCutType, transform.position);
            if (logGuardEvents)
                Debug.Log($"[Parry] {damage} dmg -> Prowler");
            return;
        }

        if (logGuardEvents)
            Debug.Log($"[Parry] no known damage API on {attacker.name}");
    }
}