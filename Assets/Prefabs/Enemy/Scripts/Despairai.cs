// ============================================================
//  DespairAI.cs  –  Seventh Echo
//
//  Robed staff enemy. Walks platforms, turns at edges,
//  melee attacks when player is in range.
//  Same architecture as ProwlerAI. No hurt/dead animations.
//
//  SETUP:
//    1. Add to Despair ROOT GameObject.
//    2. Add Rigidbody2D (Gravity Scale 1, Freeze Rotation Z).
//    3. Add CapsuleCollider2D sized to body.
//    4. Graphics child: SpriteRenderer + Animator.
//    5. Add EnemyDeathEffect to root (same as Prowler).
//    6. Tag Player GameObject as "Player".
// ============================================================

using UnityEngine;
using System.Collections;

public class DespairAI : MonoBehaviour
{
    // ── References ──
    private Rigidbody2D       rb;
    private CapsuleCollider2D col;
    private Transform         graphics;
    private SpriteRenderer    sr;
    private Animator          anim;
    private Transform         player;

    [Header("Movement")]
    public float walkSpeed      = 2.5f;
    public float patrolDistance = 6f;

    [Header("Edge & Wall Detection")]
    public float edgeRayOffsetX = 0.4f;
    public float edgeRayLength  = 1.2f;
    public float wallRayLength  = 0.4f;
    public LayerMask groundLayer;

    [Header("Detection & Chase")]
    [Tooltip("Horizontal distance at which Despair first spots the player.")]
    public float detectionRange = 9f;
    [Tooltip("Vertical tolerance for spotting. Stops it aggroing on players on far-off platforms.")]
    public float maxVerticalDetection = 3f;
    [Tooltip("Once aggroed it chases until the player is FARTHER than this.\nMake it well above detectionRange or aggro flickers at the edge.")]
    public float loseAggroRange = 18f;
    [Tooltip("Seconds it keeps hunting after the player leaves loseAggroRange.")]
    public float aggroMemoryDuration = 5f;
    [Tooltip("Chase speed. Usually a bit faster than patrol walkSpeed.")]
    public float chaseSpeed = 3.2f;
    [Tooltip("Stop at ledges and walls while chasing instead of walking off.")]
    public bool stopAtLedges = true;
    [Tooltip("Stand still between swings when the player is in range, instead of drifting back into patrol.")]
    public bool holdGroundWhenInRange = true;
    [Tooltip("Log aggro state changes to the Console.")]
    public bool logAggro = false;

    [Header("Attack")]
    public float attackRange               = 2.5f;
    public float attackCooldown            = 1.8f;
    public float attackDamage              = 10f;
    public float maxVerticalAttackDistance = 2.0f;
    public float attackAnimDuration        = 0.8f;

    [Tooltip("Delay before damage is dealt (fraction of attack anim). 0.5 = halfway through the swing.\nIGNORED when useAnimationEventForDamage is ON.")]
    [Range(0f, 1f)] public float damageTimingFraction = 0.5f;

    [Tooltip("ON  = damage fires from an Animation Event on the attack clip (survives retiming).\nOFF = damage fires on a timer at damageTimingFraction (breaks when you retime).\nTurn ON after adding the DealMeleeDamage event to DespairAttack.anim.")]
    public bool useAnimationEventForDamage = false;

    [Header("Health")]
    public float maxHealth       = 60f;
    [Tooltip("Damage received per katana hit. Set to maxHealth for one-hit kill.")]
    public float katanaDamage    = 60f;

    [Header("Knockback")]
    public float knockbackForce    = 4f;
    public float knockbackDuration = 0.15f;

    [Header("Parry Knockback (parry only)")]
    [Tooltip("Stronger shove used ONLY when the player parries. Normal katana hits use the values above.")]
    public float parryKnockbackForce = 8f;
    [Tooltip("MUST match the DespairKnockback clip length = frameCount / sampleRate.")]
    public float parryKnockbackDuration = 0.47f;
    [Tooltip("Fires the 'knockback' animator trigger on a parry.")]
    public bool playParryKnockbackAnim = true;

    [Header("Graphics Offsets (read from Inspector)")]
    public float graphicsOffsetX = -10.17f;
    public float graphicsOffsetY = -1.43f;

    // ── Animator hashes ──
    private static readonly int AnimWalk      = Animator.StringToHash("walk");
    private static readonly int AnimAttack    = Animator.StringToHash("attack");
    private static readonly int AnimKnockback = Animator.StringToHash("knockback");

    // ── State ──
    private float currentHealth;
    private float attackCooldownRemaining;
    private float knockbackRemaining;
    private float attackAnimRemaining;
    private bool  isDead;
    private bool  isAttacking;
    private bool  damageDealtThisSwing;
    private bool  isAggro;
    private float aggroMemoryRemaining;
    private int   facingDir = 1;
    private Vector3 spawnPos;

    // ─────────────────────────────────────────────────────────
    private void Awake()
    {
        rb  = GetComponent<Rigidbody2D>();
        col = GetComponent<CapsuleCollider2D>();

        Transform g = transform.Find("Graphics");
        if (g != null)
        {
            graphics = g;
            sr       = g.GetComponent<SpriteRenderer>();
            anim     = g.GetComponent<Animator>();
        }
        else Debug.LogError("[DespairAI] No 'Graphics' child found.");

        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;
        else Debug.LogWarning("[DespairAI] No GameObject tagged 'Player'.");

        currentHealth = maxHealth;
        spawnPos      = transform.position;

        if (groundLayer.value == 0)
        {
            int idx = LayerMask.NameToLayer("Ground");
            if (idx >= 0) groundLayer = 1 << idx;
        }
    }

    private void Update()
    {
        if (isDead) return;

        attackCooldownRemaining = Mathf.Max(0f, attackCooldownRemaining - Time.deltaTime);

        // Knockback is checked BEFORE isAttacking. A parry always lands mid-swing,
        // and the old order let isAttacking swallow it until the swing finished.
        if (knockbackRemaining > 0f)
        {
            knockbackRemaining -= Time.deltaTime;
            return;
        }

        if (isAttacking)
        {
            attackAnimRemaining -= Time.deltaTime;
            if (attackAnimRemaining <= 0f)
                isAttacking = false;
            return;
        }

        UpdateAggro();

        if (isAggro && player != null)
        {
            if (InAttackRange())
            {
                if (CanAttackPlayer())
                {
                    StartAttack();
                    return;
                }

                // On cooldown but the player is right there — hold the line.
                if (holdGroundWhenInRange)
                {
                    HoldPosition();
                    return;
                }
            }

            Chase();
            return;
        }

        Patrol();
    }

    // ─────────────────────────────────────────────────────────
    //  SPRITE POSITION (uses Graphics child world pos)
    // ─────────────────────────────────────────────────────────
    // Use collider center for physics-based checks (edge/wall detection)
    private float ColX() => col != null ? col.bounds.center.x : transform.position.x;
    private float ColY() => col != null ? col.bounds.min.y : transform.position.y;

    // Use sprite position for player-facing checks (attack range, facing)
    private float SpriteX() => graphics != null ? graphics.position.x : transform.position.x;
    private float SpriteY() => graphics != null ? graphics.position.y : transform.position.y;

    // ─────────────────────────────────────────────────────────
    //  AGGRO
    // ─────────────────────────────────────────────────────────
    private void UpdateAggro()
    {
        if (player == null) { isAggro = false; return; }

        float dx = Mathf.Abs(player.position.x - SpriteX());
        float dy = Mathf.Abs(player.position.y - SpriteY());

        if (!isAggro)
        {
            if (dx <= detectionRange && dy <= maxVerticalDetection)
            {
                isAggro = true;
                aggroMemoryRemaining = aggroMemoryDuration;
                if (logAggro) Debug.Log($"[Despair] spotted player at {dx:F1}m — chasing");
            }
            return;
        }

        // Already hunting. Only a long escape breaks it, not the detection range.
        if (dx <= loseAggroRange)
        {
            aggroMemoryRemaining = aggroMemoryDuration;
        }
        else
        {
            aggroMemoryRemaining -= Time.deltaTime;
            if (aggroMemoryRemaining <= 0f)
            {
                isAggro = false;
                if (logAggro) Debug.Log($"[Despair] lost player at {dx:F1}m — back to patrol");
            }
        }
    }

    /// <summary>Force aggro — used when damaged, so it fights back if hit from range.</summary>
    public void AlertToPlayer()
    {
        isAggro = true;
        aggroMemoryRemaining = aggroMemoryDuration;
    }

    // ─────────────────────────────────────────────────────────
    //  CHASE / HOLD
    // ─────────────────────────────────────────────────────────
    private void Chase()
    {
        int dir = player.position.x > SpriteX() ? 1 : -1;
        facingDir = dir;
        Flip(facingDir);

        // Don't chase off a cliff or into a wall.
        if (stopAtLedges && (!GroundAhead(dir) || WallAhead(dir)))
        {
            HoldPosition();
            return;
        }

        rb.linearVelocity = new Vector2(dir * chaseSpeed, rb.linearVelocity.y);
        if (anim != null) anim.SetBool(AnimWalk, true);
    }

    private void HoldPosition()
    {
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        if (player != null)
            Flip(player.position.x > SpriteX() ? 1 : -1);

        if (anim != null) anim.SetBool(AnimWalk, false);
    }

    // ─────────────────────────────────────────────────────────
    //  GROUND / WALL PROBES
    // ─────────────────────────────────────────────────────────
    private bool GroundAhead(int dir)
    {
        Vector2 origin = new Vector2(ColX() + dir * edgeRayOffsetX, ColY());
        return Physics2D.Raycast(origin, Vector2.down, edgeRayLength, groundLayer);
    }

    private bool WallAhead(int dir)
    {
        return Physics2D.Raycast(
            new Vector2(ColX(), ColY() + 0.5f),
            new Vector2(dir, 0f), wallRayLength, groundLayer);
    }

    // ─────────────────────────────────────────────────────────
    //  PATROL
    // ─────────────────────────────────────────────────────────
    private void Patrol()
    {
        // Turn at patrol limits
        if (ColX() > spawnPos.x + graphicsOffsetX + patrolDistance) facingDir = -1;
        if (ColX() < spawnPos.x + graphicsOffsetX - patrolDistance) facingDir =  1;

        if (!GroundAhead(facingDir)) facingDir = -facingDir;
        if (WallAhead(facingDir))    facingDir = -facingDir;

        rb.linearVelocity = new Vector2(facingDir * walkSpeed, rb.linearVelocity.y);
        Flip(facingDir);
        if (anim != null) anim.SetBool(AnimWalk, true);
    }

    // ─────────────────────────────────────────────────────────
    //  ATTACK
    // ─────────────────────────────────────────────────────────
    /// <summary>Range only — ignores cooldown. Used to decide whether to hold ground.</summary>
    private bool InAttackRange()
    {
        float dx = Mathf.Abs(player.position.x - SpriteX());
        float dy = player.position.y - SpriteY();
        if (dy > maxVerticalAttackDistance) return false;
        return dx <= attackRange;
    }

    private bool CanAttackPlayer()
    {
        if (attackCooldownRemaining > 0f) return false;
        return InAttackRange();
    }

    private void StartAttack()
    {
        isAttacking             = true;
        attackAnimRemaining     = attackAnimDuration;
        attackCooldownRemaining = attackCooldown;

        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        float dir = player.position.x > SpriteX() ? 1f : -1f;
        Flip((int)dir);

        if (anim != null)
        {
            anim.SetBool(AnimWalk, false);
            anim.SetTrigger(AnimAttack);
        }

        // Damage source: Animation Event (preferred) or the legacy timer.
        damageDealtThisSwing = false;

        if (!useAnimationEventForDamage)
            StartCoroutine(DelayedDamage(attackAnimDuration * damageTimingFraction));
    }

    private IEnumerator DelayedDamage(float delay)
    {
        yield return new WaitForSeconds(delay);
        DealMeleeDamage();
    }

    // Called by Animation Event on DespairAttack clip at the hit frame
    public void DealMeleeDamage()
    {
        if (player == null) return;
        if (isDead) return;

        // Guards against double damage if both the Animation Event and the timer
        // are somehow live, and against multi-frame event retriggers.
        if (damageDealtThisSwing) return;
        damageDealtThisSwing = true;
        float dx = Mathf.Abs(player.position.x - SpriteX());
        float dy = Mathf.Abs(player.position.y - SpriteY());
        if (dx > attackRange * 1.1f || dy > maxVerticalAttackDistance) return;

        PlayerHealth ph = player.GetComponent<PlayerHealth>();
        if (ph != null)
        {
            Vector2 hitDir = (player.position - transform.position).normalized;
            ph.TakeDamage((int)attackDamage, hitDir, gameObject);
        }
    }

    // ─────────────────────────────────────────────────────────
    //  TAKE DAMAGE
    // ─────────────────────────────────────────────────────────

    // Called by KatanaHitbox (same signature as ProwlerAI/StalkerHealth)
    public void TakeDamage(EnemyDeathEffect.CutType cutType)
    {
        TakeDamage(katanaDamage, player != null ? player.position : transform.position);
    }

    /// <summary>
    /// Called by PlayerGuard on a successful parry ONLY.
    /// Interrupts the swing, applies the bigger shove, fires the knockback anim.
    /// </summary>
    public void TakeParryCounter(float amount, Vector3 sourcePosition)
    {
        if (isDead) return;

        AlertToPlayer();

        isAttacking         = false;
        attackAnimRemaining = 0f;

        currentHealth -= amount;

        float kbDir = transform.position.x > sourcePosition.x ? 1f : -1f;
        rb.linearVelocity  = new Vector2(kbDir * parryKnockbackForce, rb.linearVelocity.y);
        knockbackRemaining = parryKnockbackDuration;

        Flip(kbDir > 0f ? -1 : 1);

        if (anim != null)
        {
            anim.SetBool(AnimWalk, false);
            anim.ResetTrigger(AnimAttack);
            if (playParryKnockbackAnim)
                anim.SetTrigger(AnimKnockback);
        }

        if (currentHealth <= 0f) Die();
    }

    public void TakeDamage(float amount, Vector3 sourcePosition)
    {
        if (!isDead) AlertToPlayer();

        if (isDead) return;
        currentHealth -= amount;

        float kbDir = transform.position.x > sourcePosition.x ? 1f : -1f;
        rb.linearVelocity  = new Vector2(kbDir * knockbackForce, rb.linearVelocity.y);
        knockbackRemaining = knockbackDuration;

        if (currentHealth <= 0f) Die();
    }

    // ─────────────────────────────────────────────────────────
    //  DEATH
    // ─────────────────────────────────────────────────────────
    private void Die()
    {
        if (isDead) return;
        isDead = true;

        rb.linearVelocity = Vector2.zero;
        rb.bodyType       = RigidbodyType2D.Kinematic;
        if (col != null) col.enabled = false;

        EnemyDeathEffect deathFx = GetComponent<EnemyDeathEffect>();
        if (deathFx != null) deathFx.PlayDeath(0);
        else Destroy(gameObject, 0.5f);
    }

    // ─────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────
    private float baseColOffsetX;

    private void Start()
    {
        if (col != null) baseColOffsetX = col.offset.x;
    }

    private void Flip(int dir)
    {
        if (graphics == null) return;
        sr.flipX = false;
        Vector3 s = graphics.localScale;
        s.x = Mathf.Abs(s.x) * dir;
        graphics.localScale = s;

        // Flip collider offset to follow sprite
        if (col != null)
        {
            Vector2 o = col.offset;
            o.x = baseColOffsetX * dir;
            col.offset = o;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        float sx = transform.position.x + graphicsOffsetX;
        float sy = transform.position.y + graphicsOffsetY;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(new Vector3(sx, sy, 0f), attackRange);

        // Detection box — where Despair first spots the player
        Gizmos.color = new Color(1f, 0.92f, 0.2f, 1f);
        Gizmos.DrawWireCube(new Vector3(sx, sy, 0f),
            new Vector3(detectionRange * 2f, maxVerticalDetection * 2f, 0f));

        // Lose-aggro box — chases until the player leaves this
        Gizmos.color = new Color(1f, 0.45f, 0f, 1f);
        Gizmos.DrawWireCube(new Vector3(sx, sy, 0f),
            new Vector3(loseAggroRange * 2f, maxVerticalDetection * 2f, 0f));

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(
            new Vector3(spawnPos.x - patrolDistance, sy, 0f),
            new Vector3(spawnPos.x + patrolDistance, sy, 0f));

        int dir = Application.isPlaying ? facingDir : 1;
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(
            new Vector2(sx + dir * edgeRayOffsetX, sy),
            Vector2.down * edgeRayLength);

        Gizmos.color = Color.blue;
        Gizmos.DrawRay(
            new Vector2(sx, sy),
            new Vector2(dir, 0f) * wallRayLength);
    }
#endif
}