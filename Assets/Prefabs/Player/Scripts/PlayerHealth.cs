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

    [Header("Flash Effect")]
    public SpriteRenderer spriteRenderer;
    public float flashDuration = 0.1f;

    [Header("Hurt Reference")]
    [Tooltip("ChestPoint transform — hits above this trigger UpperHurt, below trigger MiddleHurt")]
    public Transform chestPoint;

    private Rigidbody2D rb;
    private Animator animator;
    private bool isKnocked;
    private bool isDead;

    void Start()
    {
        currentHealth = maxHealth;
        rb            = GetComponent<Rigidbody2D>();
        animator      = GetComponentInChildren<Animator>();

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

        // Determine if hit came from above or below the chest
        // hitDirection.y > 0 means enemy hit upward (player was hit from below/middle)
        // hitDirection.y < 0 means hit came downward (upper body hit like Stalker laser)
        // We also check the chest world Y as fallback

        bool isUpperHit = hitDirection.y < 0f; // downward force = upper body hit

        // If hitDirection is mostly horizontal (like Prowler leap),
        // use ChestPoint Y to decide based on enemy position
        if (Mathf.Abs(hitDirection.y) < 0.3f && chestPoint != null)
        {
            // Horizontal hit — Prowler is ground level so it's always middle
            isUpperHit = false;
        }

        if (isUpperHit)
            animator.SetTrigger("upperHurt");
        else
            animator.SetTrigger("middleHurt");
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