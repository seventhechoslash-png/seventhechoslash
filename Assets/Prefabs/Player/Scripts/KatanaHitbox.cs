using UnityEngine;

public class KatanaHitbox : MonoBehaviour
{
    [Header("Cut Type")]
    [Tooltip("Horizontal for the F slash hitbox, Vertical for the V slash hitbox.")]
    public EnemyDeathEffect.CutType cutType = EnemyDeathEffect.CutType.Horizontal;

    [Header("Hit Stop")]
    [Tooltip("Freeze-frame duration when the katana connects (seconds, real time).")]
    public float hitStopDuration = 0.1f;
    [Tooltip("Only freeze once per swing so multi-frame overlap doesn't stack.")]
    public bool oneFreezePerSwing = true;

    private Collider2D hitCollider;
    private bool hasFrozenThisSwing = false;

    private void Awake()
    {
        hitCollider = GetComponent<Collider2D>();
        if (hitCollider != null)
            hitCollider.enabled = false;
    }

    public void EnableHitbox()
    {
        hasFrozenThisSwing = false; // reset at the start of each swing
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
        bool hitSomething = false;

        // Stalker
        StalkerHealth stalker = other.GetComponent<StalkerHealth>();
        if (stalker != null)
        {
            stalker.TakeDamage(cutType);
            hitSomething = true;
        }
        else
        {
            // Prowler
            ProwlerAI prowler = other.GetComponent<ProwlerAI>();
            if (prowler != null)
            {
                prowler.TakeDamage(cutType);
                hitSomething = true;
            }
        }

        // Trigger the hit pause on impact
        if (hitSomething)
            TriggerHitStop();
    }

    private void TriggerHitStop()
    {
        if (oneFreezePerSwing && hasFrozenThisSwing) return;
        hasFrozenThisSwing = true;

        if (HitStop.Instance != null)
            HitStop.Instance.Freeze(hitStopDuration);
    }
}