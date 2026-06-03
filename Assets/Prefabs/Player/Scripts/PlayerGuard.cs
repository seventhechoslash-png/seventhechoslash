using UnityEngine;

/// <summary>
/// Attach to Player root.
/// Reads IsBlocking directly from PlayerMovement — no duplicate input handling.
/// Also sets isCrouchGuard on the Animator automatically.
/// </summary>
public class PlayerGuard : MonoBehaviour
{
    [Header("References")]
    public LaserBlockEffect blockEffect;

    private PlayerMovement playerMovement;
    private Animator animator;

    // Public so StalkerAI can check it
    public bool IsGuarding => playerMovement != null && playerMovement.IsBlocking;

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

        // isCrouching is already set by PlayerMovement — just read it
        bool isCrouching = animator.GetBool("isCrouching");
        bool isBlocking  = playerMovement.IsBlocking;

        // CrouchGuard = blocking while crouching
        SetBoolIfExists("isCrouchGuard", isBlocking && isCrouching);
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

    private void SetBoolIfExists(string param, bool value)
    {
        if (animator == null) return;
        for (int i = 0; i < animator.parameterCount; i++)
        {
            var p = animator.GetParameter(i);
            if (p.name == param && p.type == AnimatorControllerParameterType.Bool)
            {
                animator.SetBool(param, value);
                return;
            }
        }
    }
}
