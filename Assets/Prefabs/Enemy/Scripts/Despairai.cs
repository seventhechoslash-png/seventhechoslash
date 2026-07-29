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

    [Header("Attack")]
    public float attackRange               = 2.5f;
    public float attackCooldown            = 1.8f;
    public float attackDamage              = 10f;
    public float maxVerticalAttackDistance = 2.0f;
    public float attackAnimDuration        = 0.8f;

    [Tooltip("Delay before damage is dealt (fraction of attack anim). 0.5 = halfway through the swing.")]
    [Range(0f, 1f)] public float damageTimingFraction = 0.5f;

    [Header("Health")]
    public float maxHealth       = 60f;
    [Tooltip("Damage received per katana hit. Set to maxHealth for one-hit kill.")]
    public float katanaDamage    = 60f;

    [Header("Knockback")]
    public float knockbackForce    = 4f;
    public float knockbackDuration = 0.15f;

    [Header("Graphics Offsets (read from Inspector)")]
    public float graphicsOffsetX = -10.17f;
    public float graphicsOffsetY = -1.43f;

    // ── Animator hashes ──
    private static readonly int AnimWalk   = Animator.StringToHash("walk");
    private static readonly int AnimAttack = Animator.StringToHash("attack");

    // ── State ──
    private float currentHealth;
    private float attackCooldownRemaining;
    private float knockbackRemaining;
    private float attackAnimRemaining;
    private bool  isDead;
    private bool  isAttacking;
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

        if (isAttacking)
        {
            attackAnimRemaining -= Time.deltaTime;
            if (attackAnimRemaining <= 0f)
                isAttacking = false;
            return;
        }

        if (knockbackRemaining > 0f)
        {
            knockbackRemaining -= Time.deltaTime;
            return;
        }

        if (player != null && CanAttackPlayer())
        {
            StartAttack();
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
    //  PATROL
    // ─────────────────────────────────────────────────────────
    private void Patrol()
    {
        // Turn at patrol limits
        if (ColX() > spawnPos.x + graphicsOffsetX + patrolDistance) facingDir = -1;
        if (ColX() < spawnPos.x + graphicsOffsetX - patrolDistance) facingDir =  1;

        // Edge detection — cast from collider bottom, ahead of movement
        Vector2 edgeOrigin = new Vector2(
            ColX() + facingDir * edgeRayOffsetX, ColY());
        bool groundAhead = Physics2D.Raycast(
            edgeOrigin, Vector2.down, edgeRayLength, groundLayer);
        if (!groundAhead) facingDir = -facingDir;

        // Wall detection
        bool wallAhead = Physics2D.Raycast(
            new Vector2(ColX(), ColY() + 0.5f),
            new Vector2(facingDir, 0f), wallRayLength, groundLayer);
        if (wallAhead) facingDir = -facingDir;

        rb.linearVelocity = new Vector2(facingDir * walkSpeed, rb.linearVelocity.y);
        Flip(facingDir);
        if (anim != null) anim.SetBool(AnimWalk, true);
    }

    // ─────────────────────────────────────────────────────────
    //  ATTACK
    // ─────────────────────────────────────────────────────────
    private bool CanAttackPlayer()
    {
        if (attackCooldownRemaining > 0f) return false;
        float dx = Mathf.Abs(player.position.x - SpriteX());
        float dy = player.position.y - SpriteY();
        if (dy > maxVerticalAttackDistance) return false;
        return dx <= attackRange;
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

        // Delay damage to sync with the staff strike frame
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
        float dx = Mathf.Abs(player.position.x - SpriteX());
        float dy = Mathf.Abs(player.position.y - SpriteY());
        if (dx > attackRange * 1.1f || dy > maxVerticalAttackDistance) return;

        PlayerHealth ph = player.GetComponent<PlayerHealth>();
        if (ph != null)
        {
            Vector2 hitDir = (player.position - transform.position).normalized;
            ph.TakeDamage((int)attackDamage, hitDir);
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

    public void TakeDamage(float amount, Vector3 sourcePosition)
    {
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
