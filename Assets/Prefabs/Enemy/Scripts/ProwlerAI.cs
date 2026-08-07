using UnityEngine;
using System.Collections;

public class ProwlerAI : MonoBehaviour
{
    [Header("Patrol")]
    public float walkSpeed = 2.5f;
    public float patrolDistance = 6f;

    [Header("Chase")]
    public float chaseSpeed = 5f;
    public float detectionRange = 10f;

    [Header("Attack")]
    public float attackRange = 7f;
    public float attackCooldown = 2f;
    public float leapSpeed = 14f;
    public float leapDuration = 0.3f;
    public float idleDuration = 1f;
    public int damage = 15;

    [Header("Vertical Limit")]
    [Tooltip("Player must be within this vertical distance of the Prowler sprite to be attacked. Stops attacks when player is on a platform above/below.")]
    public float maxVerticalDistance = 4f;

    [Header("References")]
    public Transform player;
    public Transform graphics;
    public Animator animator;

    [Header("Ground")]
    public LayerMask groundLayer;

    private Rigidbody2D rb;
    private bool isDead = false;
    private float lastAttackTime = -99f;
    private float patrolOriginX;
    private float patrolDir = 1f;
    private bool attackRunning = false;

    private enum State { Patrol, Chase, Attack, Idle }
    private State state = State.Patrol;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();

        if (graphics == null) graphics = transform.Find("Graphics");
        if (animator == null && graphics != null)
            animator = graphics.GetComponent<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }

        if (groundLayer.value == 0)
        {
            int idx = LayerMask.NameToLayer("Ground");
            if (idx >= 0) groundLayer = 1 << idx;
        }

        patrolOriginX = SpriteX();
    }

    // Sprite world position (what you actually see), not the offset root
    float SpriteX() => graphics != null ? graphics.position.x : transform.position.x;
    float SpriteY() => graphics != null ? graphics.position.y : transform.position.y;

    void Update()
    {
        if (isDead || player == null) return;
        if (attackRunning) return;

        float distX = Mathf.Abs(SpriteX() - player.position.x);
        float distY = Mathf.Abs(SpriteY() - player.position.y);

        // Player must be horizontally close AND on roughly the same vertical level
        bool sameLevel = distY <= maxVerticalDistance;

        if (sameLevel && distX <= attackRange && Time.time >= lastAttackTime + attackCooldown)
        {
            state = State.Attack;
            StartCoroutine(AttackRoutine());
        }
        else if (sameLevel && distX <= attackRange)
        {
            state = State.Idle;
            FaceTarget(player.position.x);
        }
        else if (sameLevel && distX <= detectionRange)
        {
            state = State.Chase;
        }
        else
        {
            state = State.Patrol;
        }

        if (animator != null)
            animator.SetBool("isWalking", state == State.Patrol || state == State.Chase);
    }

    void FixedUpdate()
    {
        if (isDead) return;

        switch (state)
        {
            case State.Patrol:
                rb.constraints = RigidbodyConstraints2D.FreezeRotation;
                DoPatrol();
                break;

            case State.Chase:
                rb.constraints = RigidbodyConstraints2D.FreezeRotation;
                DoChase();
                break;

            case State.Idle:
                rb.constraints = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                break;

            case State.Attack:
                if (!attackRunning)
                {
                    rb.constraints = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;
                    rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                }
                break;
        }
    }

    IEnumerator AttackRoutine()
    {
        attackRunning = true;
        lastAttackTime = Time.time;

        rb.constraints = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;
        rb.linearVelocity = Vector2.zero;
        FaceTarget(player.position.x);
        state = State.Attack;

        if (animator != null)
        {
            animator.SetBool("isWalking", false);
            animator.SetBool("isIdle", false);
            animator.ResetTrigger("Attack");
            animator.SetTrigger("Attack");
        }

        yield return new WaitForSeconds(0.2f);

        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        float leapDir = player.position.x > SpriteX() ? 1f : -1f;
        float timer = 0f;
        bool hitDealt = false;

        while (timer < leapDuration)
        {
            rb.linearVelocity = new Vector2(leapDir * leapSpeed, rb.linearVelocity.y);

            if (!hitDealt)
            {
                float dx = Mathf.Abs(SpriteX() - player.position.x);
                float dy = Mathf.Abs(SpriteY() - player.position.y);

                // Must be close horizontally AND vertically to land the hit
                if (dx < attackRange * 0.75f && dy <= maxVerticalDistance)
                {
                    PlayerGuard guard = player.GetComponent<PlayerGuard>();
                    bool blocked = guard != null && guard.TryBlockDamage(player.position, gameObject);
                    if (!blocked)
                    {
                        PlayerHealth ph = player.GetComponent<PlayerHealth>();
                        if (ph != null)
                        {
                            Vector2 hitDir = new Vector2(leapDir, 0f);
                            ph.TakeDamage(damage, hitDir);
                        }
                    }
                    hitDealt = true;
                }
            }

            timer += Time.deltaTime;
            yield return null;
        }

        rb.constraints = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        yield return new WaitForSeconds(0.2f);

        state = State.Idle;
        if (animator != null)
        {
            animator.SetBool("isWalking", false);
            animator.SetBool("isIdle", true);
        }

        yield return new WaitForSeconds(idleDuration);

        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        if (animator != null)
        {
            animator.SetBool("isIdle", false);
            animator.SetBool("isWalking", true);
        }

        FaceTarget(player.position.x);
        state = State.Chase;
        attackRunning = false;
    }

    void DoPatrol()
    {
        float distFromOrigin = SpriteX() - patrolOriginX;
        if (distFromOrigin >= patrolDistance)  patrolDir = -1f;
        if (distFromOrigin <= -patrolDistance) patrolDir =  1f;

        rb.linearVelocity = new Vector2(patrolDir * walkSpeed, rb.linearVelocity.y);
        FaceTarget(SpriteX() + patrolDir);
    }

    void DoChase()
    {
        float dir = player.position.x > SpriteX() ? 1f : -1f;
        rb.linearVelocity = new Vector2(dir * chaseSpeed, rb.linearVelocity.y);
        FaceTarget(player.position.x);
    }

    void FaceTarget(float targetX)
    {
        if (graphics == null) return;
        float dir = targetX - SpriteX();
        if (Mathf.Abs(dir) < 0.01f) return;
        Vector3 s = graphics.localScale;
        s.x = Mathf.Abs(s.x) * (dir > 0 ? 1f : -1f);
        graphics.localScale = s;
    }

    // Fallback when no cut type provided
    public void TakeDamage()
    {
        TakeDamage(EnemyDeathEffect.CutType.Horizontal);
    }

    public void TakeDamage(EnemyDeathEffect.CutType cut)
    {
        if (isDead) return;
        isDead = true;
        StopAllCoroutines();
        rb.linearVelocity = Vector2.zero;

        EnemyDeathEffect effect = GetComponent<EnemyDeathEffect>();
        if (effect != null)
            effect.PlayDeath(cut);
        else
            Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 c = graphics != null ? graphics.position : transform.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(c, detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(c, attackRange);
        // Vertical limit lines
        Gizmos.color = Color.green;
        Gizmos.DrawLine(c + Vector3.up * maxVerticalDistance + Vector3.left * 5f,
                        c + Vector3.up * maxVerticalDistance + Vector3.right * 5f);
        Gizmos.DrawLine(c + Vector3.down * maxVerticalDistance + Vector3.left * 5f,
                        c + Vector3.down * maxVerticalDistance + Vector3.right * 5f);
    }
}