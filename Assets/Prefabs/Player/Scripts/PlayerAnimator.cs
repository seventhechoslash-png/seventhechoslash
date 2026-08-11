using UnityEngine;

/// <summary>
/// Handles: all Animator parameter updates.
/// Reads from PlayerState every frame and pushes to Animator.
/// </summary>
public class PlayerAnimator : MonoBehaviour
{
    [Header("Animator Params")]
    public string didJumpBool = "didJump";

    private Animator animator;
    private PlayerState state;

    private void Awake()
    {
        state    = GetComponent<PlayerState>();
        animator = GetComponent<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    private void LateUpdate()
    {
        if (animator == null) return;

        AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
        if (st.IsName("Death")) return;

        // Sliding outranks crouching, or both bools fire and the states fight.
        bool isCrouching = (state.crouchHeld || state.isCrouchBlocking)
                           && state.isGrounded && !state.isSliding;
        bool allowRun    = !state.isAttacking && !state.isBlocking && !state.isCrouchBlocking;
        bool isRunning   = allowRun && Mathf.Abs(state.moveInput.x) > 0.1f
                           && state.isGrounded && !isCrouching;

        SetBool("isGrounded",          state.isGrounded);
        SetBool("isCrouching",         isCrouching);
        SetBool("isRunning",           isRunning);
        SetBool("isBlocking",          state.isBlocking);
        SetBool("isCrouchGuard",       state.isCrouchBlocking);
        SetBool("isVerticalAttacking", state.isVerticalAttacking);
        SetBool("isDashing",           state.isDashing);
        SetBool("isSliding",           state.isSliding);
        SetBool(didJumpBool,           state.didJump);
        SetFloat("yVelocity",          state.velocity.y);
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private void SetBool(string param, bool value)
    {
        if (string.IsNullOrEmpty(param)) return;
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

    private void SetFloat(string param, float value)
    {
        if (string.IsNullOrEmpty(param)) return;
        for (int i = 0; i < animator.parameterCount; i++)
        {
            var p = animator.GetParameter(i);
            if (p.name == param && p.type == AnimatorControllerParameterType.Float)
            {
                animator.SetFloat(param, value);
                return;
            }
        }
    }
}