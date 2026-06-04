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

    [Header("References")]
    public Transform player;
    public Transform graphics;
    public Animator animator;

    [Header("Ground")]
    public LayerMask groundLayer;

    private Rigidbody2D rb;
    private bool isDead = false;
    private float lastAttackTime = -99f;
    private Vector3 patrolOrigin;
    private float patrolDir = 1f;
    private bool attackRunning = false;

    private enum State { Patrol, Chase, Attack, Idle }
    private State state = State.Patrol;

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

    void Update()
    {
        if (isDead || player == null) return;
        if (attackRunning) return;

        float distX = Mathf.Abs(transform.position.x - player.position.x);

        if (distX <= attackRange && Time.time >= lastAttackTime + attackCooldown)
        {
            state = State.Attack;
            StartCoroutine(AttackRoutine());
        }
        else if (distX <= attackRange)
        {
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

        if (animator != null)
            animator.SetBool("isWalking", state == State.Patrol || state == State.Chase);
    }

    void FixedUpdate()
    {
        if (isDead) return;

        switch (state)
        {
            case State.Patrol:
                // Unlock X for movement
                rb.constraints = RigidbodyConstraints2D.FreezeRotation;
                DoPatrol();
                break;

            case State.Chase:
                // Unlock X for movement
                rb.constraints = RigidbodyConstraints2D.FreezeRotation;
                DoChase();
                break;

            case State.Idle:
                // FREEZE X — player cannot push the Prowler while it's idle/waiting
                rb.constraints = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                break;

            case State.Attack:
                if (!attackRunning)
                {
                    // Freeze if attack coroutine hasn't started yet
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

        // Freeze during windup
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

        // Unlock X for leap
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        float leapDir = player.position.x > transform.position.x ? 1f : -1f;
        float timer = 0f;
        bool hitDealt = false;

        while (timer < leapDuration)
        {
            rb.linearVelocity = new Vector2(leapDir * leapSpeed, rb.linearVelocity.y);

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

        // Hard stop + freeze after leap
        rb.constraints = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        yield return new WaitForSeconds(0.2f);

        // Idle
        state = State.Idle;
        if (animator != null)
        {
            animator.SetBool("isWalking", false);
            animator.SetBool("isIdle", true);
        }

        yield return new WaitForSeconds(idleDuration);

        // Back to chase — unlock X
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
        float distFromOrigin = transform.position.x - patrolOrigin.x;
        if (distFromOrigin >= patrolDistance)  patrolDir = -1f;
        if (distFromOrigin <= -patrolDistance) patrolDir =  1f;

        rb.linearVelocity = new Vector2(patrolDir * walkSpeed, rb.linearVelocity.y);
        FaceTarget(transform.position.x + patrolDir);
    }

    void DoChase()
    {
        float dir = player.position.x > transform.position.x ? 1f : -1f;
        rb.linearVelocity = new Vector2(dir * chaseSpeed, rb.linearVelocity.y);
        FaceTarget(player.position.x);
    }

    void FaceTarget(float targetX)
    {
        if (graphics == null) return;
        float dir = targetX - transform.position.x;
        if (Mathf.Abs(dir) < 0.01f) return;
        Vector3 s = graphics.localScale;
        s.x = Mathf.Abs(s.x) * (dir > 0 ? 1f : -1f);
        graphics.localScale = s;
    }

    public void TakeDamage()
    {
        if (isDead) return;
        isDead = true;
        StopAllCoroutines();
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.linearVelocity = Vector2.zero;
        Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(
            transform.position + Vector3.left  * patrolDistance,
            transform.position + Vector3.right * patrolDistance);
    }
}
