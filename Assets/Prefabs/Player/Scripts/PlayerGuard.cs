using UnityEngine;

/// <summary>
/// Attach to Player root.
/// Reads IsBlocking and IsCrouchBlocking directly from PlayerMovement.
/// No duplicate input handling needed.
/// </summary>
public class PlayerGuard : MonoBehaviour
{
    [Header("References")]
    public LaserBlockEffect blockEffect;

    private PlayerMovement playerMovement;
    private Animator animator;

    // Public so StalkerAI can check it
    public bool IsGuarding => playerMovement != null && 
        (playerMovement.IsBlocking || playerMovement.IsCrouchBlocking);

    void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
        animator = GetComponent<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        if (blockEffect == null)
            blockEffect = GetComponentInChildren<LaserBlockEffect>();
    }

    void Update()
    {
        if (animator == null || playerMovement == null) return;

        // isCrouchGuard is now set directly by PlayerMovement in UpdateAnimator
        // Nothing extra needed here
    }

    /// <summary>
    /// Called by StalkerAI when laser hits player.
    /// Returns true if blocked — also triggers the katana spark VFX.
    /// </summary>
    public bool TryBlockDamage(Vector2 hitPoint)
    {
        if (!IsGuarding) return false;

        if (blockEffect != null)
            blockEffect.PlayBlockEffect(hitPoint);

        return true;
    }
}
