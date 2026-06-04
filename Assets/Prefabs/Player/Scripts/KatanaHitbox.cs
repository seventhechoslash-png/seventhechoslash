using UnityEngine;

public class KatanaHitbox : MonoBehaviour
{
    private Collider2D hitCollider;

    private void Awake()
    {
        hitCollider = GetComponent<Collider2D>();
        if (hitCollider != null)
            hitCollider.enabled = false;
    }

    public void EnableHitbox()
    {
        if (hitCollider != null)
            hitCollider.enabled = true;
    }

    public void DisableHitbox()
    {
        if (hitCollider != null)
            hitCollider.enabled = false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Hit Stalker
        StalkerHealth stalker = other.GetComponent<StalkerHealth>();
        if (stalker != null)
        {
            stalker.TakeDamage();
            return;
        }

        // Hit Prowler
        ProwlerAI prowler = other.GetComponent<ProwlerAI>();
        if (prowler != null)
        {
            prowler.TakeDamage();
            return;
        }
    }
}
