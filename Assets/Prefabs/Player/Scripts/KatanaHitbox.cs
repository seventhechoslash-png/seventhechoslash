using UnityEngine;

public class KatanaHitbox : MonoBehaviour
{
    private Collider2D hitCollider;

    private void Awake()
    {
        hitCollider = GetComponent<Collider2D>();
        if (hitCollider != null)
            hitCollider.enabled = false; // Off by default
    }

    // Called by Animation Event at the START of the swing frame
    public void EnableHitbox()
    {
        if (hitCollider != null)
            hitCollider.enabled = true;
    }

    // Called by Animation Event at the END of the swing frame
    public void DisableHitbox()
    {
        if (hitCollider != null)
            hitCollider.enabled = false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        StalkerHealth enemy = other.GetComponent<StalkerHealth>();
        if (enemy != null)
        {
            enemy.TakeDamage();
        }
    }
}