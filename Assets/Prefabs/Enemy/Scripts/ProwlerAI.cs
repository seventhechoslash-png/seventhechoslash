using UnityEngine;
using System.Collections;

public class ProwlerAI : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 2.5f;
    public float chaseSpeed = 5f;

    [Header("Detection & Attack")]
    public float detectionRange = 10f;
    public float attackRange = 5f;
    public float attackCooldown = 2f;
    public float leapSpeed = 6f;
    public float leapDuration = 0.4f;
    public int damage = 15;

    [Header("References")]
    public Transform player;
    public Transform graphics;
    public Animator animator;

    [Header("Ground")]
    public LayerMask groundLayer;

    [Header("Debug")]
    public bool showGizmos = true;

    private Rigidbody2D rb;
    private int direction = 1;
    private bool isGrounded = false;
    private bool wasAtEdge = false;
    private float turnCooldown = 0f;
    private bool attackCoroutineRunning = false;
    private float lastAttackTime = -99f;
    private bool isLeaping = false;
    private float leapVelocity = 0f;

    private enum State { Patrol, Chase, Attack, Cooldown }
    private State currentState = State.Patrol;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        if (graphics == null)
            graphics = transform.Find("Graphics");

        if (animator == null && graphics != null)
            animator = graphics.GetComponent<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }

        if (groundLayer.value == 0)
        {
            int idx = LayerMask.NameToLayer("Ground");
            if (idx >= 0) groundLayer = 1 << idx;
        }
    }

    bool IsPlayerInFacingDirection()
    {
        if (player == null || graphics == null) return false;
        float dirToPlayer = player.position.x - GetSpritePosition().x;
        bool facingRight = graphics.localScale.x > 0;
        bool playerIsRight = dirToPlayer > 0;
        return facingRight == playerIsRight;
    }

    float GetDistanceToPlayer()
    {
        if (player == null) return float.MaxValue;
        return Vector2.Distance(GetSpritePosition(),
            new Vector2(player.position.x, player.position.y));
    }

    void Update()
    {
        if (player == null) return;
        if (attackCoroutineRunning) return;

        float dist = GetDistanceToPlayer();
        bool canSeePlayer = IsPlayerInFacingDirection();

        // ── WITHIN ATTACK RANGE ───────────────────────────────────────────
        // Always react regardless of facing — turn and attack/cooldown
        if (dist <= attackRange)
        {
            FacePlayer(); // always turn to face when close

            if (Time.time > lastAttackTime + attackCooldown)
            {
                currentState = State.Attack;
                StartCoroutine(AttackSequence());
            }
            else
            {
                currentState = State.Cooldown;
            }
        }
        // ── OUTSIDE ATTACK RANGE — facing-only detection ──────────────────
        else if (canSeePlayer && dist <= detectionRange)
        {
            currentState = State.Chase;
            FacePlayer();
        }
        // ── OUT OF RANGE OR BEHIND — patrol normally ──────────────────────
        else
        {
            currentState = State.Patrol;
        }

        // Keep isWalking animator in sync
        if (animator != null)
            animator.SetBool("isWalking", currentState == State.Patrol || currentState == State.Chase);
    }

    void FixedUpdate()
    {
        CheckGrounded();

        if (attackCoroutineRunning)
        {
            if (isLeaping)
                rb.linearVelocity = new Vector2(leapVelocity, rb.linearVelocity.y);
            else
                rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            return;
        }

        switch (currentState)
        {
            case State.Patrol:
                PatrolMovement();
                break;
            case State.Chase:
                ChaseMovement();
                break;
            case State.Attack:
            case State.Cooldown:
                rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
                break;
        }
    }

    void PatrolMovement()
    {
        if (!isGrounded) return;

        if (turnCooldown > 0f)
            turnCooldown -= Time.fixedDeltaTime;

        bool edgeAhead = IsEdgeAheadInDirection(GetActualDirection());
        bool wallAhead = IsWallAhead();

        if ((edgeAhead || wallAhead) && !wasAtEdge && turnCooldown <= 0f)
        {
            direction *= -1;
            wasAtEdge = true;
            turnCooldown = 0.5f;
        }
        else if (!edgeAhead && !wallAhead)
        {
            wasAtEdge = false;
        }

        rb.linearVelocity = new Vector2(direction * walkSpeed, rb.linearVelocity.y);
        UpdateFacing();
    }

    void ChaseMovement()
    {
        if (!isGrounded || player == null) return;

        float prowlerX = GetSpritePosition().x;
        float playerX = player.position.x;
        float rawDir = prowlerX > playerX ? -1f : 1f;

        if (IsEdgeAheadInDirection(rawDir))
        {
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            FacePlayer();
            return;
        }

        rb.linearVelocity = new Vector2(rawDir * chaseSpeed, rb.linearVelocity.y);
        FacePlayer();
    }

    IEnumerator AttackSequence()
    {
        attackCoroutineRunning = true;
        lastAttackTime = Time.time;

        // Hard stop before attacking
        rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);

        if (animator != null)
        {
            animator.SetBool("isWalking", false);
            animator.speed = 1f;
            animator.SetTrigger("Attack");
        }

        yield return new WaitForSeconds(0.1f);

        float prowlerX = GetSpritePosition().x;
        float playerX = player.position.x;
        float rawDir = prowlerX > playerX ? -1f : 1f;
        leapVelocity = rawDir * leapSpeed;

        isLeaping = true;
        float leapTimer = 0f;
        bool hitLanded = false;

        while (leapTimer < leapDuration)
        {
            if (!hitLanded && GetDistanceToPlayer() < attackRange)
            {
                PlayerHealth ph = player != null ? player.GetComponent<PlayerHealth>() : null;
                if (ph != null)
                {
                    Vector2 hitDir = (player.position - transform.position).normalized;
                    ph.TakeDamage(damage, hitDir);
                    hitLanded = true;
                }
            }
            leapTimer += Time.deltaTime;
            yield return null;
        }

        // Hard stop — kills ALL velocity, no sliding
        isLeaping = false;
        leapVelocity = 0f;
        rb.linearVelocity = Vector2.zero;

        yield return new WaitForSeconds(0.5f);

        // Force back to crawl animation cleanly
        if (animator != null)
        {
            animator.ResetTrigger("Attack");
            animator.Play("ProwlerCrawl", 0, 0f);
            animator.SetBool("isWalking", true);
        }

        yield return new WaitForSeconds(0.1f);

        attackCoroutineRunning = false;
        currentState = State.Patrol;
    }

    void FacePlayer()
    {
        if (player == null || graphics == null) return;
        float dir = player.position.x - GetSpritePosition().x;
        if (Mathf.Abs(dir) < 0.01f) return;
        Vector3 s = graphics.localScale;
        s.x = Mathf.Abs(s.x) * (dir > 0 ? 1 : -1);
        graphics.localScale = s;
    }

    void UpdateFacing()
    {
        if (graphics == null) return;
        float actualDir = GetActualDirection();
        Vector3 s = graphics.localScale;
        s.x = Mathf.Abs(s.x) * (actualDir > 0 ? 1 : -1);
        graphics.localScale = s;
    }

    void CheckGrounded()
    {
        Vector2 spritePos = GetSpritePosition();
        RaycastHit2D hit = Physics2D.Raycast(
            spritePos + Vector2.up * 0.5f,
            Vector2.down, 5f, groundLayer);
        isGrounded = hit.collider != null && hit.distance < 2f;
    }

    bool IsEdgeAheadInDirection(float dir)
    {
        Vector2 spritePos = GetSpritePosition();
        RaycastHit2D groundBelow = Physics2D.Raycast(
            spritePos + Vector2.up, Vector2.down, 20f, groundLayer);
        if (groundBelow.collider == null) return false;

        Vector2 aheadOrigin = new Vector2(
            spritePos.x + (dir * 2f),
            groundBelow.point.y + 2f);
        RaycastHit2D aheadHit = Physics2D.Raycast(
            aheadOrigin, Vector2.down, 4f, groundLayer);
        return aheadHit.collider == null;
    }

    bool IsWallAhead()
    {
        Vector2 spritePos = GetSpritePosition();
        float actualDir = GetActualDirection();
        RaycastHit2D hit = Physics2D.Raycast(
            spritePos + Vector2.up * 0.5f,
            new Vector2(actualDir, 0), 1.5f, groundLayer);
        return hit.collider != null;
    }

    Vector2 GetSpritePosition()
    {
        if (graphics != null)
            return new Vector2(graphics.position.x, graphics.position.y);
        return transform.position;
    }

    float GetActualDirection()
    {
        if (Mathf.Abs(rb.linearVelocity.x) < 0.01f) return direction;
        return rb.linearVelocity.x > 0 ? 1f : -1f;
    }

    void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;
        Vector2 pos = GetSpritePosition();
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(pos, detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(pos, attackRange);
    }
}