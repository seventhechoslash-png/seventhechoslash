using UnityEngine;
using System.Collections;

/// <summary>
/// Prowler Beast AI — patrol, chase, leap attack, idle, repeat.
/// ALL position logic uses transform.position (root).
/// Graphics child is only used for sprite flipping.
/// </summary>
public class ProwlerAI : MonoBehaviour
{
    [Header("Patrol")]
    public float walkSpeed = 2.5f;
    public float patrolDistance = 6f;   // how far it walks before turning

    [Header("Chase")]
    public float chaseSpeed = 5f;
    public float detectionRange = 10f;

    [Header("Attack")]
    public float attackRange = 3f;      // horizontal distance to trigger leap
    public float attackCooldown = 2f;
    public float leapSpeed = 14f;
    public float leapDuration = 0.3f;
    public float idleDuration = 1f;     // sit idle after attack before chasing again
    public int damage = 15;

    [Header("References")]
    public Transform player;
    public Transform graphics;          // child with SpriteRenderer — for flipping only
    public Animator animator;

    [Header("Ground")]
    public LayerMask groundLayer;

    // ── Private ───────────────────────────────────────────────────────────
    private Rigidbody2D rb;
    private bool isDead = false;
    private float lastAttackTime = -99f;

    // Patrol
    private Vector3 patrolOrigin;
    private float patrolDir = 1f;

    // State
    private enum State { Patrol, Chase, Attack, Idle }
    private State state = State.Patrol;
    private bool attackRunning = false;

    // ── Init ──────────────────────────────────────────────────────────────
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        patrolOrigin = transform.position;

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
    }

    // ── Update — state decisions ──────────────────────────────────────────
    void Update()
    {
        if (isDead || player == null) return;
        if (attackRunning) return; // attack coroutine owns everything during attack

        // Horizontal distance only — avoids Y offset issues completely
        float distX = Mathf.Abs(transform.position.x - player.position.x);

        if (distX <= attackRange && Time.time >= lastAttackTime + attackCooldown)
        {
            state = State.Attack;
            StartCoroutine(AttackRoutine());
        }
        else if (distX <= attackRange)
        {
            // In range but on cooldown — stand still facing player
            state = State.Idle;
            FaceTarget(player.position.x);
        }
        else if (distX <= detectionRange)
        {
            state = State.Chase;
        }
        else
        {
            state = State.Patrol;
        }

        // Animator
        if (animator != null)
            animator.SetBool("isWalking", state == State.Patrol || state == State.Chase);
    }

    // ── FixedUpdate — movement ────────────────────────────────────────────
    void FixedUpdate()
    {
        if (isDead || attackRunning) return;

        switch (state)
        {
            case State.Patrol: DoPatrol(); break;
            case State.Chase:  DoChase();  break;
            case State.Idle:
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                break;
        }
    }

    // ── Patrol ────────────────────────────────────────────────────────────
    void DoPatrol()
    {
        float distFromOrigin = transform.position.x - patrolOrigin.x;

        // Turn around at patrol boundaries
        if (distFromOrigin >= patrolDistance)  patrolDir = -1f;
        if (distFromOrigin <= -patrolDistance) patrolDir =  1f;

        rb.linearVelocity = new Vector2(patrolDir * walkSpeed, rb.linearVelocity.y);
        FaceTarget(transform.position.x + patrolDir);
    }

    // ── Chase ─────────────────────────────────────────────────────────────
    void DoChase()
    {
        float dir = player.position.x > transform.position.x ? 1f : -1f;
        rb.linearVelocity = new Vector2(dir * chaseSpeed, rb.linearVelocity.y);
        FaceTarget(player.position.x);
    }

    // ── Attack coroutine ──────────────────────────────────────────────────
    IEnumerator AttackRoutine()
    {
        attackRunning = true;
        lastAttackTime = Time.time;

        // Stop and face player
        rb.linearVelocity = Vector2.zero;
        FaceTarget(player.position.x);
        state = State.Attack;

        // Trigger attack animation
        if (animator != null)
        {
            animator.SetBool("isWalking", false);
            animator.SetBool("isIdle", false);
            animator.ResetTrigger("Attack");
            animator.SetTrigger("Attack");
        }

        // Brief wind-up pause
        yield return new WaitForSeconds(0.2f);

        // Leap toward player
        float leapDir = player.position.x > transform.position.x ? 1f : -1f;
        float timer = 0f;
        bool hitDealt = false;

        while (timer < leapDuration)
        {
            rb.linearVelocity = new Vector2(leapDir * leapSpeed, rb.linearVelocity.y);

            // Deal damage once when close enough
            if (!hitDealt)
            {
                float dx = Mathf.Abs(transform.position.x - player.position.x);
                if (dx < attackRange * 0.75f)
                {
                    PlayerGuard guard = player.GetComponent<PlayerGuard>();
                    bool blocked = guard != null && guard.TryBlockDamage(player.position);
                    if (!blocked)
                    {
                        PlayerHealth ph = player.GetComponent<PlayerHealth>();
                        if (ph != null)
                        {
                            Vector2 hitDir = (player.position - transform.position).normalized;
                            ph.TakeDamage(damage, hitDir);
                        }
                    }
                    hitDealt = true;
                }
            }

            timer += Time.deltaTime;
            yield return null;
        }

        // Hard stop
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        // Short land recovery
        yield return new WaitForSeconds(0.2f);

        // Enter idle — prowler sits and recovers like a real predator
        state = State.Idle;
        if (animator != null)
        {
            animator.SetBool("isWalking", false);
            animator.SetBool("isIdle", true);
        }

        yield return new WaitForSeconds(idleDuration);

        // Done — back to chasing
        if (animator != null)
        {
            animator.SetBool("isIdle", false);
            animator.SetBool("isWalking", true);
        }

        FaceTarget(player.position.x);
        state = State.Chase;
        attackRunning = false;
    }

    // ── Facing ────────────────────────────────────────────────────────────
    void FaceTarget(float targetX)
    {
        if (graphics == null) return;
        float dir = targetX - transform.position.x;
        if (Mathf.Abs(dir) < 0.01f) return;
        Vector3 s = graphics.localScale;
        s.x = Mathf.Abs(s.x) * (dir > 0 ? 1f : -1f);
        graphics.localScale = s;
    }

    // ── Death ─────────────────────────────────────────────────────────────
    public void TakeDamage()
    {
        if (isDead) return;
        isDead = true;
        StopAllCoroutines();
        rb.linearVelocity = Vector2.zero;
        Destroy(gameObject);
    }

    // ── Gizmos ────────────────────────────────────────────────────────────
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(
            transform.position + Vector3.left * patrolDistance,
            transform.position + Vector3.right * patrolDistance);
    }
}
