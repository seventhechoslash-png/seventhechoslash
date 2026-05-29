using UnityEngine;

public class StalkerHealth : MonoBehaviour
{
    [Header("Death Effect")]
    public GameObject deathExplosionPrefab; // Assign your VFX prefab here

    public void TakeDamage()
    {
        Die();
    }

    private void Die()
    {
        if (deathExplosionPrefab != null)
            Instantiate(deathExplosionPrefab, transform.position, Quaternion.identity);

        Destroy(gameObject);
    }
}