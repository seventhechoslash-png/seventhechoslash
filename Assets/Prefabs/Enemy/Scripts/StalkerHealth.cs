using UnityEngine;

public class StalkerHealth : MonoBehaviour
{
    // Default cut type when killed without a specified slash (fallback)
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