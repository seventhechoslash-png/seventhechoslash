// ============================================================
//  DespairAI.cs  –  Seventh Echo
//
//  Robed staff enemy. Patrols platforms, chases the player on
//  sight, melee attacks in range, reacts to parries.
//
//  IMPORTANT CHANGE vs the previous version:
//  The old script measured distances from graphics.position but
//  drew gizmos from transform.position + graphicsOffset. Those are
//  two different points, so the gizmos did not show what the AI
//  actually used. graphicsOffsetX/Y are GONE. Everything - logic
//  and gizmos alike - now measures from OriginX()/OriginY(), the
//  collider centre. What you see is what the AI uses.
//
//  SETUP:
//    1. Add to Despair ROOT GameObject.
//    2. Rigidbody2D (Gravity Scale 1, Freeze Rotation Z).
//    3. CapsuleCollider2D sized to the body.
//    4. Graphics child: SpriteRenderer + Animator.
//    5. EnemyDeathEffect on the root.
//    6. Player GameObject tagged "Player".
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
    private Collider2D        playerCol;

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
    [Tooltip("Vertical tolerance for spotting. Absolute - above or below.")]
    public float maxVerticalDetection = 3f;
    [Tooltip("Once aggroed it chases until the player is FARTHER than this.\nKeep well above detectionRange or aggro flickers at the boundary.")]
    public float loseAggroRange = 18f;
    [Tooltip("Seconds it keeps hunting after the player leaves loseAggroRange.")]
    public float aggroMemoryDuration = 5f;
    [Tooltip("Chase speed. Usually a little faster than walkSpeed.")]
    public float chaseSpeed = 3.2f;
    [Tooltip("Stop at ledges and walls while chasing instead of walking off.")]
    public bool stopAtLedges = true;
    [Tooltip("Stand still between swings when the player is in range.")]
    public bool holdGroundWhenInRange = true;

    [Header("Attack")]
    public float attackRange    = 2.5f;
    public float attackCooldown = 1.2f;
    public float attackDamage   = 10f;
    [Tooltip("Vertical reach, ABSOLUTE - blocks swings at a player far above OR below.")]
    public float maxVerticalAttackDistance = 2.0f;
    public float attackAnimDuration = 0.8f;

    [Tooltip("Delay before damage lands, as a fraction of the attack anim.\nIGNORED when useAnimationEventForDamage is ON.")]
    [Range(0f, 1f)] public float damageTimingFraction = 0.5f;

    [Tooltip("ON  = damage fires from an Animation Event calling DealMeleeDamage() (survives retiming).\nOFF = damage fires on a timer at damageTimingFraction.")]
    public bool useAnimationEventForDamage = false;

    [Header("Health")]
    public float maxHealth    = 60f;
    [Tooltip("Damage per katana hit. Equal to maxHealth = one-hit kill.")]
    public float katanaDamage = 60f;

    [Header("Knockback")]
    public float knockbackForce    = 4f;
    public float knockbackDuration = 0.15f;

    [Header("Parry Knockback (parry only)")]
    public float parryKnockbackForce = 8f;
    [Tooltip("MUST match the DespairKnockback clip length = frameCount / sampleRate.")]
    public float parryKnockbackDuration = 0.47f;
    public bool  playParryKnockbackAnim = true;

    [Header("Debug")]
    [Tooltip("Logs aggro changes.")]
    public bool logAggro = false;
    [Tooltip("Live on-screen readout of dx / dy against the attack thresholds.")]
    public bool showRangeDebug = true;
    [Tooltip("Prints the same numbers to the CONSOLE every logInterval seconds.\nUse this if the on-screen text is not visible.")]
    public bool logRangeToConsole = true;
    public float logInterval = 0.5f;

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
    private float baseColOffsetX;
    private float logTimer;

    // ═════════════════════════════════════════════════════════
    //  ONE MEASUREMENT ORIGIN
    //  Every distance check AND every gizmo uses these. If the
    //  gizmo looks wrong, the AI is wrong, and vice versa.
    // ═════════════════════════════════════════════════════════
    private float OriginX() => col != null ? col.bounds.center.x : transform.position.x;
    private float OriginY() => col != null ? col.bounds.center.y : transform.position.y;

    /// <summary>Bottom of the collider - used for ground rays only.</summary>
    private float FeetY() => col != null ? col.bounds.min.y : transform.position.y;

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
        if (p != null)
        {
            player    = p.transform;
            playerCol = p.GetComponent<Collider2D>();
            if (playerCol == null) playerCol = p.GetComponentInChildren<Collider2D>();
        }
        else Debug.LogWarning("[DespairAI] No GameObject tagged 'Player'.");

        currentHealth = maxHealth;
        spawnPos      = transform.position;

        if (col != null) baseColOffsetX = col.offset.x;

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

        // Knockback outranks isAttacking - a parry always lands mid-swing.
        if (knockbackRemaining > 0f)
        {
            knockbackRemaining -= Time.deltaTime;
            return;
        }

        if (isAttacking)
        {
            attackAnimRemaining -= Time.deltaTime;
            if (attackAnimRemaining <= 0f) isAttacking = false;
            return;
        }

        UpdateAggro();
        LogRange();

        if (isAggro && player != null)
        {
            if (InAttackRange())
            {
                if (attackCooldownRemaining <= 0f)
                {
                    StartAttack();
                    return;
                }

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
    //  DISTANCE TO PLAYER
    // ─────────────────────────────────────────────────────────
    // Measure CENTRE to CENTRE. player.position is the root pivot, which on a
    // platformer sits at the FEET, while OriginY() is Despair's chest. Comparing
    // those two made dy read ~2 while standing on the same floor, and ~0 while
    // standing on his head - so he refused to swing at ground level and only
    // attacked when you jumped on him.
    private float PlayerX() => playerCol != null ? playerCol.bounds.center.x : player.position.x;
    private float PlayerY() => playerCol != null ? playerCol.bounds.center.y : player.position.y;

    private float DistX() => Mathf.Abs(PlayerX() - OriginX());
    private float DistY() => Mathf.Abs(PlayerY() - OriginY());

    // ─────────────────────────────────────────────────────────
    //  AGGRO
    // ─────────────────────────────────────────────────────────
    private void UpdateAggro()
    {
        if (player == null) { isAggro = false; return; }

        float dx = DistX();
        float dy = DistY();

        if (!isAggro)
        {
            if (dx <= detectionRange && dy <= maxVerticalDetection)
            {
                isAggro = true;
                aggroMemoryRemaining = aggroMemoryDuration;
                if (logAggro) Debug.Log($"[Despair] spotted player at {dx:F1}m - chasing");
            }
            return;
        }

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
                if (logAggro) Debug.Log($"[Despair] lost player at {dx:F1}m - back to patrol");
            }
        }
    }

    /// <summary>Force aggro - used when damaged so it fights back if hit from range.</summary>
    public void AlertToPlayer()
    {
        isAggro = true;
        aggroMemoryRemaining = aggroMemoryDuration;
    }

    // ─────────────────────────────────────────────────────────
    //  CHASE / HOLD / PATROL
    // ─────────────────────────────────────────────────────────
    private void Chase()
    {
        int dir = PlayerX() > OriginX() ? 1 : -1;
        facingDir = dir;
        Flip(facingDir);

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
            Flip(PlayerX() > OriginX() ? 1 : -1);

        if (anim != null) anim.SetBool(AnimWalk, false);
    }

    private void Patrol()
    {
        if (OriginX() > spawnPos.x + patrolDistance) facingDir = -1;
        if (OriginX() < spawnPos.x - patrolDistance) facingDir =  1;

        if (!GroundAhead(facingDir)) facingDir = -facingDir;
        if (WallAhead(facingDir))    facingDir = -facingDir;

        rb.linearVelocity = new Vector2(facingDir * walkSpeed, rb.linearVelocity.y);
        Flip(facingDir);
        if (anim != null) anim.SetBool(AnimWalk, true);
    }

    // ─────────────────────────────────────────────────────────
    //  GROUND / WALL PROBES
    // ─────────────────────────────────────────────────────────
    private bool GroundAhead(int dir)
    {
        Vector2 origin = new Vector2(OriginX() + dir * edgeRayOffsetX, FeetY());
        return Physics2D.Raycast(origin, Vector2.down, edgeRayLength, groundLayer);
    }

    private bool WallAhead(int dir)
    {
        return Physics2D.Raycast(
            new Vector2(OriginX(), FeetY() + 0.5f),
            new Vector2(dir, 0f), wallRayLength, groundLayer);
    }

    // ─────────────────────────────────────────────────────────
    //  ATTACK
    // ─────────────────────────────────────────────────────────
    /// <summary>Range only, ignores cooldown. dy is ABSOLUTE now - the old
    /// signed check only rejected players ABOVE, never below.</summary>
    private bool InAttackRange()
    {
        if (player == null) return false;
        return DistX() <= attackRange && DistY() <= maxVerticalAttackDistance;
    }

    private void StartAttack()
    {
        isAttacking             = true;
        attackAnimRemaining     = attackAnimDuration;
        attackCooldownRemaining = attackCooldown;

        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        Flip(PlayerX() > OriginX() ? 1 : -1);

        if (anim != null)
        {
            anim.SetBool(AnimWalk, false);
            anim.SetTrigger(AnimAttack);
        }

        damageDealtThisSwing = false;

        if (!useAnimationEventForDamage)
            StartCoroutine(DelayedDamage(attackAnimDuration * damageTimingFraction));
    }

    private IEnumerator DelayedDamage(float delay)
    {
        yield return new WaitForSeconds(delay);
        DealMeleeDamage();
    }

    /// <summary>Called by an Animation Event on the attack clip, or by the timer.</summary>
    public void DealMeleeDamage()
    {
        if (player == null || isDead) return;
        if (damageDealtThisSwing) return;
        damageDealtThisSwing = true;

        // Slightly generous on X so a player edging away still gets clipped.
        if (DistX() > attackRange * 1.15f) return;
        if (DistY() > maxVerticalAttackDistance) return;

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
    public void TakeDamage(EnemyDeathEffect.CutType cutType)
    {
        TakeDamage(katanaDamage, player != null ? player.position : transform.position);
    }

    public void TakeDamage(float amount, Vector3 sourcePosition)
    {
        if (isDead) return;
        AlertToPlayer();

        currentHealth -= amount;

        float kbDir = transform.position.x > sourcePosition.x ? 1f : -1f;
        rb.linearVelocity  = new Vector2(kbDir * knockbackForce, rb.linearVelocity.y);
        knockbackRemaining = knockbackDuration;

        if (currentHealth <= 0f) Die();
    }

    /// <summary>Called by PlayerGuard on a successful parry ONLY.</summary>
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

        Flip(kbDir > 0f ? -1 : 1);   // shoved away, still facing the player

        if (anim != null)
        {
            anim.SetBool(AnimWalk, false);
            anim.ResetTrigger(AnimAttack);
            if (playParryKnockbackAnim) anim.SetTrigger(AnimKnockback);
        }

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
    private void Flip(int dir)
    {
        if (graphics == null) return;

        if (sr != null) sr.flipX = false;

        Vector3 s = graphics.localScale;
        s.x = Mathf.Abs(s.x) * dir;
        graphics.localScale = s;

        if (col != null)
        {
            Vector2 o = col.offset;
            o.x = baseColOffsetX * dir;
            col.offset = o;
        }
    }

    // ─────────────────────────────────────────────────────────
    //  DEBUG READOUT
    // ─────────────────────────────────────────────────────────
    private void LogRange()
    {
        if (!logRangeToConsole || player == null || isDead) return;

        logTimer -= Time.deltaTime;
        if (logTimer > 0f) return;
        logTimer = Mathf.Max(0.1f, logInterval);

        float dx = DistX();
        float dy = DistY();

        Debug.Log(
            $"[Despair] dx={dx:F2}/{attackRange:F2} {(dx <= attackRange ? "OK" : "FAR")}  " +
            $"dy={dy:F2}/{maxVerticalAttackDistance:F2} {(dy <= maxVerticalAttackDistance ? "OK" : "FAR")}  " +
            $"| aggro={isAggro} inRange={InAttackRange()} cd={attackCooldownRemaining:F2} " +
            $"attacking={isAttacking} kb={knockbackRemaining:F2}  " +
            $"| me=({OriginX():F2},{OriginY():F2}) plr=({PlayerX():F2},{PlayerY():F2}) " +
            $"{(playerCol != null ? "collider" : "PIVOT-FALLBACK")}  " +
            $"| ground={GroundAhead(facingDir)} wall={WallAhead(facingDir)}");
    }

    private void OnGUI()
    {
        if (!showRangeDebug || player == null || isDead) return;

        float dx = DistX();
        float dy = DistY();

        bool okX = dx <= attackRange;
        bool okY = dy <= maxVerticalAttackDistance;

        var style = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        style.normal.textColor = (okX && okY) ? Color.green : Color.yellow;

        // Fixed screen position. Camera.main returns null unless a camera is
        // tagged MainCamera, which previously pushed this label off-screen.
        GUI.Label(new Rect(12f, 12f, 700f, 90f),
            $"dx {dx:F2} / {attackRange:F2} {(okX ? "OK" : "FAR")}\n" +
            $"dy {dy:F2} / {maxVerticalAttackDistance:F2} {(okY ? "OK" : "FAR")}\n" +
            $"aggro={isAggro}  cd={attackCooldownRemaining:F2}\n" +
            $"me.y {OriginY():F2}  player.y {PlayerY():F2}  " +
            $"{(playerCol != null ? "collider" : "PIVOT-FALLBACK")}", style);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Uses the SAME origin as the AI, so the gizmo can never lie again.
        float ox = Application.isPlaying || col != null
            ? (col != null ? col.bounds.center.x : transform.position.x)
            : transform.position.x;
        float oy = col != null ? col.bounds.center.y : transform.position.y;
        Vector3 o = new Vector3(ox, oy, 0f);

        Gizmos.color = Color.red;                                   // attack
        Gizmos.DrawWireCube(o, new Vector3(attackRange * 2f, maxVerticalAttackDistance * 2f, 0f));

        Gizmos.color = new Color(1f, 0.92f, 0.2f, 1f);              // detection
        Gizmos.DrawWireCube(o, new Vector3(detectionRange * 2f, maxVerticalDetection * 2f, 0f));

        Gizmos.color = new Color(1f, 0.45f, 0f, 1f);                // lose aggro
        Gizmos.DrawWireCube(o, new Vector3(loseAggroRange * 2f, maxVerticalDetection * 2f, 0f));

        Gizmos.color = Color.yellow;                                // patrol bounds
        Vector3 sp = Application.isPlaying ? spawnPos : transform.position;
        Gizmos.DrawLine(new Vector3(sp.x - patrolDistance, oy, 0f),
                        new Vector3(sp.x + patrolDistance, oy, 0f));

        int dir = Application.isPlaying ? facingDir : 1;
        float fy = col != null ? col.bounds.min.y : transform.position.y;

        Gizmos.color = Color.cyan;                                  // edge ray
        Gizmos.DrawRay(new Vector2(ox + dir * edgeRayOffsetX, fy), Vector2.down * edgeRayLength);

        Gizmos.color = Color.blue;                                  // wall ray
        Gizmos.DrawRay(new Vector2(ox, fy + 0.5f), new Vector2(dir, 0f) * wallRayLength);
    }
#endif
}