using UnityEngine;

public class StalkerHealth : MonoBehaviour
{
    public void TakeDamage()
    {
        Die();
    }

    private void Die()
    {
        StalkerDeathEffect effect = GetComponent<StalkerDeathEffect>();
        if (effect != null)
            effect.PlayDeathEffect();
        else
            Destroy(gameObject);
    }
}
