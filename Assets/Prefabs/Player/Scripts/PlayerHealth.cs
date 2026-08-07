using UnityEngine;
using System.Collections;

/// <summary>
/// Health, damage, hurt animations, and the knockback / guard slide.
///
/// Slides are driven from FixedUpdate (not a frame-time coroutine) and set
/// state.isKnocked so PlayerMovement backs off and stops erasing them.
///
/// Guard slides are started by PlayerGuard.TryBlockDamage(), because the
/// enemies short-circuit and never call TakeDamage() when a block succeeds.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    public int maxHealth = 100;

    [Header("Hit Knockback (unblocked)")]
    [Tooltip("Peak slide speed in units/sec. This is a SPEED now, not a force.")]
    public float hitKnockbackSpeed = 7f;
    public float hitKnockbackDuration = 0.18f;

    [Header("Guard Slide (standing block)")]
    public float guardSlideSpeed = 5f;
    public float guardSlideDuration = 0.14f;

    [Header("Guard Slide (crouch block)")]
    [Tooltip("Crouched stance is braced, so less slide usually reads better.")]
    public float crouchGuardSlideSpeed = 2.8f;
    public float crouchGuardSlideDuration = 0.10f;

    [Header("Parry")]
    [Tooltip("Multiplier on guard slide when the block lands inside the parry window. 0 = plant your feet.")]
    [Range(0f, 1f)]
    public float parrySlideMultiplier = 0f;
    [Tooltip("Parry freeze is longer than a normal block — that's what sells it.")]
    public float parryFreezeDuration = 0.12f;

    [Header("Guard Damage")]
    [Tooltip("Chip damage taken even on a successful block. 0 = perfect block.")]
    public int guardChipDamage = 0;
    [Tooltip("Chip damage on a parry. Should normally stay 0.")]
    public int parryChipDamage = 0;

    [Header("Slide Shape")]
    [Tooltip("Y: 1 = full speed, 0 = stopped. X: normalized 0-1 across the slide.")]
    public AnimationCurve slideFalloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Hit Stop")]
    public bool useHitStop = true;
    public float hitFreezeDuration = 0.07f;
    public float guardFreezeDuration = 0.05f;

    [Header("Flash Effect")]
    public SpriteRenderer spriteRenderer;
    public float flashDuration = 0.1f;
    [Tooltip("Sprite tint when hit unblocked.")]
    public Color hitFlashColor = Color.red;
    [Tooltip("Subtle cool silver on a normal block.")]
    public Color blockFlashColor = new Color(0.80f, 0.85f, 0.95f, 1f);
    [Tooltip("Bright silver shine on a parry.")]
    public Color parryFlashColor = new Color(1f, 1f, 1f, 1f);
    [Tooltip("Parry shine lasts longer and pulses twice.")]
    public float parryFlashDuration = 0.22f;

    [Header("Hurt Reference")]
    [Tooltip("ChestPoint transform — hits above this trigger UpperHurt, below trigger MiddleHurt")]
    public Transform chestPoint;

    // ── Components ──
    private Rigidbody2D rb;
    private Animator animator;
    private PlayerState state;
    private PlayerGuard guard;
    private Transform graphics;

    // ── Slide state ──
    private float slideTimer;
    private float slideDuration;
    private float slideSpeed;
    private float slideDirX;

    private int currentHealth;
    private bool isDead;

    public bool IsKnocked => slideTimer > 0f;
    public int CurrentHealth => currentHealth;

    /// <summary>+1 if facing right, -1 if facing left.</summary>
    private float Facing => graphics != null ? Mathf.Sign(graphics.localScale.x) : 1f;

    // ═══════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ═══════════════════════════════════════════════════════════════════════

    void Awake()
    {
        rb       = GetComponent<Rigidbody2D>();
        state    = GetComponent<PlayerState>();
        guard    = GetComponent<PlayerGuard>();
        animator = GetComponentInChildren<Animator>();

        Transform g = transform.Find("Graphics");
        graphics = g != null
            ? g
            : (GetComponentInChildren<SpriteRenderer>() != null
                ? GetComponentInChildren<SpriteRenderer>().transform
                : transform);
    }

    void Start()
    {
        currentHealth = maxHealth;

        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (chestPoint == null && graphics != null)
            chestPoint = graphics.Find("ChestPoint");

        if (HealthUI.Instance != null)
            HealthUI.Instance.UpdateHealth(currentHealth, maxHealth);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DAMAGE — called by enemies only when the hit is NOT blocked
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Legacy 2-arg entry point. Parry counter-damage won't fire via this.</summary>
    public void TakeDamage(int damage, Vector2 hitDirection)
    {
        TakeDamage(damage, hitDirection, null);
    }

    /// <summary>
    /// Pass the attacker so a parry can counter-damage it.
    /// </summary>
    public void TakeDamage(int damage, Vector2 hitDirection, GameObject attacker)
    {
        if (IsKnocked) return;   // brief invulnerability during the slide
        if (isDead) return;

        // ── GUARD CHECK LIVES HERE ──
        // Despairai calls TakeDamage() directly without ever asking PlayerGuard.
        // Checking here means no enemy — present or future — can bypass the block.
        if (guard != null && guard.IsGuarding)
        {
            Vector2 hitPoint = (Vector2)transform.position - hitDirection.normalized * 0.5f;
            guard.RegisterBlock(hitPoint, attacker);
            return;   // no damage, no red flash, no hurt animation
        }

        currentHealth -= damage;
        Debug.Log($"Player HIT — dmg={damage} hp={currentHealth}");

        TriggerHurtAnimation(hitDirection);

        // Push away from the attacker. hitDirection points attacker -> player.
        float dirX = Mathf.Approximately(hitDirection.x, 0f)
            ? -Facing
            : Mathf.Sign(hitDirection.x);

        BeginSlide(dirX, hitKnockbackSpeed, hitKnockbackDuration);

        StartFlash(FlashKind.Hit);

        if (useHitStop && HitStop.Instance != null)
            HitStop.Instance.Freeze(hitFreezeDuration);

        if (CameraShake.Instance != null)
            CameraShake.Instance.Shake(0.2f, 0.25f);

        if (HealthUI.Instance != null)
            HealthUI.Instance.UpdateHealth(currentHealth, maxHealth);

        if (currentHealth <= 0)
            Die();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  GUARD — called by PlayerGuard.TryBlockDamage()
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Starts the block pushback. Called by PlayerGuard, never by enemies.
    /// Direction is opposite of facing, because you face the attacker when you
    /// block and ProwlerAI passes the player's own position as its hitPoint.
    /// </summary>
    public void BeginGuardSlide(bool isParry)
    {
        if (isDead) return;
        if (IsKnocked) return;

        bool crouching = state != null && state.isCrouchBlocking;

        float speed = crouching ? crouchGuardSlideSpeed : guardSlideSpeed;
        float dur   = crouching ? crouchGuardSlideDuration : guardSlideDuration;

        // A parry plants your feet — that contrast is what makes it feel earned.
        if (isParry) speed *= parrySlideMultiplier;

        if (speed > 0.01f)
            BeginSlide(-Facing, speed, dur);

        int chip = isParry ? parryChipDamage : guardChipDamage;
        if (chip > 0)
        {
            currentHealth -= chip;
            if (HealthUI.Instance != null)
                HealthUI.Instance.UpdateHealth(currentHealth, maxHealth);
        }

        StartFlash(isParry ? FlashKind.Parry : FlashKind.Block);

        if (useHitStop && HitStop.Instance != null)
            HitStop.Instance.Freeze(isParry ? parryFreezeDuration : guardFreezeDuration);

        if (CameraShake.Instance != null)
        {
            if (isParry) CameraShake.Instance.Shake(0.14f, 0.16f);
            else         CameraShake.Instance.Shake(0.08f, 0.10f);
        }

        Debug.Log($"Player {(isParry ? "PARRIED" : "GUARDED")} — crouch={crouching} hp={currentHealth}");

        if (currentHealth <= 0)
            Die();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SLIDE DRIVER
    // ═══════════════════════════════════════════════════════════════════════

    private void BeginSlide(float dirX, float speed, float duration)
    {
        if (rb == null) return;

        slideDirX     = Mathf.Approximately(dirX, 0f) ? 1f : Mathf.Sign(dirX);
        slideSpeed    = speed;
        slideDuration = Mathf.Max(0.01f, duration);
        slideTimer    = slideDuration;

        // PlayerMovement pins Y with FreezePositionY when idle on ground.
        // Release it or the slide can't be pushed off a ledge properly.
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        if (state != null) state.isKnocked = true;
    }

    void FixedUpdate()
    {
        if (slideTimer <= 0f) return;

        slideTimer -= Time.fixedDeltaTime;

        float t = 1f - Mathf.Clamp01(slideTimer / slideDuration);   // 0 -> 1
        float x = slideDirX * slideSpeed * slideFalloff.Evaluate(t);

        rb.linearVelocity = new Vector2(x, rb.linearVelocity.y);

        if (slideTimer <= 0f)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            if (state != null) state.isKnocked = false;
        }
    }

    public void CancelSlide()
    {
        slideTimer = 0f;
        if (state != null) state.isKnocked = false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ANIMATION / FX
    // ═══════════════════════════════════════════════════════════════════════

    void TriggerHurtAnimation(Vector2 hitDirection)
    {
        if (animator == null) return;

        bool isUpperHit = hitDirection.y < 0f;

        if (Mathf.Abs(hitDirection.y) < 0.3f && chestPoint != null)
            isUpperHit = false;

        animator.SetTrigger(isUpperHit ? "upperHurt" : "middleHurt");
    }

    private enum FlashKind { Hit, Block, Parry }

    private Coroutine flashRoutine;

    private void StartFlash(FlashKind kind)
    {
        if (spriteRenderer == null) return;
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine(kind));
    }

    IEnumerator FlashRoutine(FlashKind kind)
    {
        switch (kind)
        {
            case FlashKind.Hit:
                spriteRenderer.color = hitFlashColor;
                yield return new WaitForSecondsRealtime(flashDuration);
                break;

            case FlashKind.Block:
                spriteRenderer.color = blockFlashColor;
                yield return new WaitForSecondsRealtime(flashDuration * 0.7f);
                break;

            case FlashKind.Parry:
                // Double pulse — bright, dip, bright again. Reads as a metallic ring.
                float half = parryFlashDuration * 0.25f;
                spriteRenderer.color = parryFlashColor;
                yield return new WaitForSecondsRealtime(half);
                spriteRenderer.color = blockFlashColor;
                yield return new WaitForSecondsRealtime(half * 0.6f);
                spriteRenderer.color = parryFlashColor;
                yield return new WaitForSecondsRealtime(half);
                break;
        }

        spriteRenderer.color = Color.white;
        flashRoutine = null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DEATH
    // ═══════════════════════════════════════════════════════════════════════

    void Die()
    {
        if (isDead) return;
        isDead = true;
        CancelSlide();

        Debug.Log("Player Dead");

        if (animator != null)
            animator.SetBool("isDead", true);

        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null) movement.enabled = false;

        PlayerCombat combat = GetComponent<PlayerCombat>();
        if (combat != null) combat.enabled = false;

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale   = 3f;
            rb.constraints    = RigidbodyConstraints2D.FreezeRotation;
        }
    }
}