using UnityEngine;

/// <summary>
/// Despair health — routes all katana hits through DespairAI.TakeDamage
/// so the 80% block system applies correctly.
///
/// PARRY NOTE: KatanaHitbox should call DespairAI.TakeParryCounter()
/// directly for parry hits, which bypasses the block roll entirely.
///
/// Previous version called Die() directly, which skipped the block system.
/// </summary>
public class DespairHealth : MonoBehaviour
{
    public void TakeDamage()
    {
        TakeDamage(EnemyDeathEffect.CutType.Horizontal);
    }

    public void TakeDamage(EnemyDeathEffect.CutType cut)
    {
        // Route through DespairAI so the block roll fires.
        DespairAI ai = GetComponent<DespairAI>();
        if (ai != null)
        {
            ai.TakeDamage(cut);
        }
        else
        {
            // Fallback: no DespairAI present — die directly.
            Debug.LogWarning("[DespairHealth] DespairAI not found. Falling back to direct death.");
            Die(cut);
        }
    }

    private void Die(EnemyDeathEffect.CutType cut)
    {
        GetComponent<EnemyDropper>()?.OnEnemyDeath();
        EnemyDeathEffect effect = GetComponent<EnemyDeathEffect>();
        if (effect != null)
            effect.PlayDeath(cut);
        else
            Destroy(gameObject);
    }
}