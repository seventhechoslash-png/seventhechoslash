using UnityEngine;

/// <summary>
/// Despair health — identical pattern to StalkerHealth.
/// Routes death into the shared EnemyDeathEffect (electrocution + blast + ash).
/// </summary>
public class DespairHealth : MonoBehaviour
{
    public void TakeDamage()
    {
        TakeDamage(EnemyDeathEffect.CutType.Horizontal);
    }

    public void TakeDamage(EnemyDeathEffect.CutType cut)
    {
        Die(cut);
    }

    private void Die(EnemyDeathEffect.CutType cut)
    {
        EnemyDeathEffect effect = GetComponent<EnemyDeathEffect>();
        if (effect != null)
            effect.PlayDeath(cut);
        else
            Destroy(gameObject);
    }
}
