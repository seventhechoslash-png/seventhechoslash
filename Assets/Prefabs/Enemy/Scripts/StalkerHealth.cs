using UnityEngine;
using System.Collections;

/// <summary>
/// Stalker health pool.
///
/// NOTE: this class previously had no health at all — TakeDamage() went straight
/// to Die(), so any hit was fatal. It now holds a real pool so the parry counter
/// can damage without instantly killing.
///
/// Defaults are set so katanaDamage == maxHealth, preserving the original
/// one-hit-kill behaviour until you lower katanaDamage.
/// </summary>
public class StalkerHealth : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 60f;
    [Tooltip("Damage per katana hit. Equal to maxHealth = one-hit kill (original behaviour).")]
    public float katanaDamage = 60f;

    [Header("Hurt Reaction")]
    public Color hurtFlashColor = Color.red;
    public float hurtFlashDuration = 0.12f;

    [Header("Debug")]
    public bool logDamage = false;

    private float currentHealth;
    private bool isDead;
    private SpriteRenderer sr;
    private Coroutine flashRoutine;

    public float CurrentHealth => currentHealth;
    public bool IsDead => isDead;

    void Awake()
    {
        currentHealth = maxHealth;

        Transform g = transform.Find("Graphics");
        if (g != null) sr = g.GetComponent<SpriteRenderer>();
        if (sr == null) sr = GetComponentInChildren<SpriteRenderer>();
    }

    // Fallback when no cut type provided
    public void TakeDamage()
    {
        TakeDamage(EnemyDeathEffect.CutType.Horizontal);
    }

    /// <summary>Katana hit — deals katanaDamage.</summary>
    public void TakeDamage(EnemyDeathEffect.CutType cut)
    {
        TakeDamage(katanaDamage, cut);
    }

    /// <summary>Full damage entry point. Used by the katana and the parry counter.</summary>
    public void TakeDamage(float amount, EnemyDeathEffect.CutType cut)
    {
        if (isDead) return;

        currentHealth -= amount;

        if (logDamage)
            Debug.Log($"[Stalker] took {amount} — hp {currentHealth}/{maxHealth}");

        if (currentHealth <= 0f)
        {
            Die(cut);
            return;
        }

        if (sr != null)
        {
            if (flashRoutine != null) StopCoroutine(flashRoutine);
            flashRoutine = StartCoroutine(HurtFlash());
        }
    }

    IEnumerator HurtFlash()
    {
        Color original = Color.white;
        sr.color = hurtFlashColor;
        yield return new WaitForSecondsRealtime(hurtFlashDuration);
        sr.color = original;
        flashRoutine = null;
    }

    private void Die(EnemyDeathEffect.CutType cut)
    {
        if (isDead) return;
        isDead = true;

        EnemyDeathEffect effect = GetComponent<EnemyDeathEffect>();
        if (effect != null)
            effect.PlayDeath(cut);
        else
            Destroy(gameObject);
    }
}