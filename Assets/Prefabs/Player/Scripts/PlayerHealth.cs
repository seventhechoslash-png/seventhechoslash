using UnityEngine;
using System.Collections;

public class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    public int maxHealth = 100;
    private int currentHealth;

    [Header("Knockback")]
    public float knockbackForce = 10f;
    public float knockbackDuration = 0.15f;

    [Header("Guard Slide")]
    [Tooltip("How far the player slides back when blocking an attack.")]
    public float guardSlideForce = 7f;
    [Tooltip("How long the guard slide lasts.")]
    public float guardSlideDuration = 0.2f;
    [Tooltip("Damage reduction while blocking (0 = no damage, 0.5 = half damage, 1 = full damage).")]
    [Range(0f, 1f)]
    public float blockDamageReduction = 0f;

    [Header("Flash Effect")]
    public SpriteRenderer spriteRenderer;
    public float flashDuration = 0.1f;

    [Header("Hurt Reference")]
    [Tooltip("ChestPoint transform — hits above this trigger UpperHurt, below trigger MiddleHurt")]
    public Transform chestPoint;

    private Rigidbody2D rb;
    private Animator animator;
    private PlayerState state;
    private bool isKnocked;
    private bool isDead;

    void Start()
    {
        currentHealth = maxHealth;
        rb            = GetComponent<Rigidbody2D>();
        animator      = GetComponentInChildren<Animator>();
        state         = GetComponent<PlayerState>();

        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        // Auto-find ChestPoint if not assigned
        if (chestPoint == null)
        {
            Transform g = transform.Find("Graphics");
            if (g != null) chestPoint = g.Find("ChestPoint");
        }

        if (HealthUI.Instance != null)
            HealthUI.Instance.UpdateHealth(currentHealth, maxHealth);
    }

    public void TakeDamage(int damage, Vector2 hitDirection)
    {
        if (isKnocked) return;
        if (isDead) return;

        // ── GUARD SLIDE: blocking any attack ──
        if (state != null && (state.isBlocking || state.isCrouchBlocking))
        {
            // Reduced damage while blocking (default 0 = no damage)
            int blockedDamage = Mathf.RoundToInt(damage * blockDamageReduction);
            if (blockedDamage > 0)
            {
                currentHealth -= blockedDamage;
                if (HealthUI.Instance != null)
                    HealthUI.Instance.UpdateHealth(currentHealth, maxHealth);
            }

            // Slide backward away from the attacker
            if (rb != null)
                StartCoroutine(GuardSlide(hitDirection));

            // Small camera shake on block (lighter than a real hit)
            if (CameraShake.Instance != null)
                CameraShake.Instance.Shake(0.08f, 0.1f);

            // Block flash — white instead of red
            StartCoroutine(BlockFlash());

            if (currentHealth <= 0)
                Die();

            return; // Don't apply normal damage/knockback/hurt animation
        }

        // ── NORMAL HIT (not blocking) ──
        currentHealth -= damage;
        Debug.Log("Player Hit! Health: " + currentHealth);

        // Trigger correct hurt animation based on hit origin Y vs ChestPoint Y
        TriggerHurtAnimation(hitDirection);

        if (rb != null)
            StartCoroutine(ApplyKnockback(hitDirection));

        StartCoroutine(Flash());

        if (CameraShake.Instance != null)
            CameraShake.Instance.Shake(0.2f, 0.25f);

        if (HealthUI.Instance != null)
            HealthUI.Instance.UpdateHealth(currentHealth, maxHealth);

        if (currentHealth <= 0)
            Die();
    }

    void TriggerHurtAnimation(Vector2 hitDirection)
    {
        if (animator == null) return;

        bool isUpperHit = hitDirection.y < 0f;

        if (Mathf.Abs(hitDirection.y) < 0.3f && chestPoint != null)
            isUpperHit = false;

        if (isUpperHit)
            animator.SetTrigger("upperHurt");
        else
            animator.SetTrigger("middleHurt");
    }

    IEnumerator GuardSlide(Vector2 hitDirection)
    {
        isKnocked = true;

        // Slide AWAY from the hit source (opposite of hit direction)
        float slideDir = hitDirection.x >= 0f ? -1f : 1f;

        rb.linearVelocity = new Vector2(slideDir * guardSlideForce, 0f);

        float elapsed = 0f;
        while (elapsed < guardSlideDuration)
        {
            // Decelerate smoothly over the slide duration
            float t = elapsed / guardSlideDuration;
            float speed = Mathf.Lerp(guardSlideForce, 0f, t);
            rb.linearVelocity = new Vector2(slideDir * speed, rb.linearVelocity.y);

            elapsed += Time.deltaTime;
            yield return null;
        }

        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        isKnocked = false;
    }

    IEnumerator ApplyKnockback(Vector2 dir)
    {
        isKnocked = true;
        rb.linearVelocity = Vector2.zero;
        rb.AddForce(dir.normalized * knockbackForce, ForceMode2D.Impulse);
        yield return new WaitForSeconds(knockbackDuration);
        isKnocked = false;
    }

    IEnumerator Flash()
    {
        if (spriteRenderer == null) yield break;
        spriteRenderer.color = Color.red;
        yield return new WaitForSeconds(flashDuration);
        spriteRenderer.color = Color.white;
    }

    IEnumerator BlockFlash()
    {
        if (spriteRenderer == null) yield break;
        spriteRenderer.color = new Color(0.8f, 0.85f, 1f); // subtle cool white
        yield return new WaitForSeconds(flashDuration * 0.7f);
        spriteRenderer.color = Color.white;
    }

    void Die()
    {
        if (isDead) return;
        isDead = true;
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