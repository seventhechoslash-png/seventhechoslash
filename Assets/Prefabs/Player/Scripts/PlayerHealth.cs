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

    private Rigidbody2D rb;
    private bool isKnocked;
    private bool isDead;

    void Start()
    {
        currentHealth = maxHealth;
        rb = GetComponent<Rigidbody2D>();

        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (HealthUI.Instance != null)
            HealthUI.Instance.UpdateHealth(currentHealth, maxHealth);
    }

    public void TakeDamage(int damage, Vector2 hitDirection)
    {
        if (isKnocked) return;
        if (isDead) return;

        currentHealth -= damage;
        Debug.Log("Player Hit! Health: " + currentHealth);

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

        Animator anim = GetComponentInChildren<Animator>();
        if (anim != null)
            anim.SetBool("isDead", true);

        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null)
            movement.enabled = false;

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            // Keep Dynamic so gravity pulls body to ground
            rb.gravityScale = 3f;
            // Only freeze rotation so body doesnt spin
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        }
    }
}