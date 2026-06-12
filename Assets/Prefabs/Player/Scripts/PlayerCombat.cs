using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles: horizontal attack (F), vertical attack (V), block, crouch block.
/// Writes combat state to PlayerState.
/// </summary>
public class PlayerCombat : MonoBehaviour
{
    [Header("Attack")]
    public string attackTrigger = "attack";

    [Header("Vertical Attack")]
    public string verticalAttackTrigger = "verticalAttack";
    [Tooltip("Normalized time (0-1) at which hold-V freezes the animation.")]
    public float verticalAttackHoldFrameTime = 0.08f;

    // ── Components ──
    private Animator animator;
    private PlayerInputActions input;
    private PlayerState state;

    // ── Internal input flags ──
    private bool attackPressed;
    private bool verticalAttackPressed;
    private bool verticalAttackHeld;
    private bool blockHeld;

    private void Awake()
    {
        state    = GetComponent<PlayerState>();
        input    = new PlayerInputActions();

        animator = GetComponent<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    private void OnEnable()
    {
        input.Player.Enable();
        try { input.Player.Attack.performed += OnAttack; } catch { }
        try
        {
            input.Player.Block.performed += OnBlockPerformed;
            input.Player.Block.canceled  += OnBlockCanceled;
        } catch { }
        try
        {
            input.Player.VerticalAttack.performed += OnVerticalAttack;
            input.Player.VerticalAttack.canceled  += OnVerticalAttackCanceled;
        } catch { }
    }

    private void OnDisable()
    {
        try { input.Player.Attack.performed -= OnAttack; } catch { }
        try
        {
            input.Player.Block.performed -= OnBlockPerformed;
            input.Player.Block.canceled  -= OnBlockCanceled;
        } catch { }
        try
        {
            input.Player.VerticalAttack.performed -= OnVerticalAttack;
            input.Player.VerticalAttack.canceled  -= OnVerticalAttackCanceled;
        } catch { }
        input.Player.Disable();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            attackPressed         = false;
            verticalAttackPressed = false;
            verticalAttackHeld    = false;
            blockHeld             = false;
            state.isVerticalAttackHolding = false;
            if (animator != null) animator.speed = 1f;
        }
    }

    private void FixedUpdate()
    {
        if (animator != null)
        {
            AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
            state.isVerticalAttacking = st.IsName("VerticalAttack");
            state.isAttacking = st.IsName("Attack") || st.IsName("SitAttack") || state.isVerticalAttacking;
        }

        // Standing block: G held, NOT crouching
        state.isBlocking = blockHeld && state.isGrounded && !state.isAttacking
                           && !state.isDashing && !state.crouchHeld;

        // Crouch block: G + C held
        state.isCrouchBlocking = blockHeld && state.crouchHeld && state.isGrounded
                                  && !state.isAttacking && !state.isDashing;

        HandleAttack();

        attackPressed         = false;
        verticalAttackPressed = false;
    }

    private void HandleAttack()
    {
        if (animator == null) return;
        if (state.isBlocking || state.isCrouchBlocking) return;

        if (state.isVerticalAttacking)
        {
            AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
            float normalizedTime = st.normalizedTime % 1f;

            if (verticalAttackHeld &&
                normalizedTime >= verticalAttackHoldFrameTime &&
                normalizedTime < verticalAttackHoldFrameTime + 0.05f &&
                !state.isVerticalAttackHolding)
            {
                animator.speed = 0f;
                state.isVerticalAttackHolding = true;
            }

            if (state.isVerticalAttackHolding && !verticalAttackHeld)
            {
                animator.speed = 1f;
                state.isVerticalAttackHolding = false;
            }

            return;
        }

        if (animator.speed == 0f) animator.speed = 1f;
        if (state.isAttacking) return;

        if (verticalAttackPressed)
        {
            animator.ResetTrigger(verticalAttackTrigger);
            animator.SetTrigger(verticalAttackTrigger);
            attackPressed = false;
            return;
        }

        if (!attackPressed) return;
        animator.ResetTrigger(attackTrigger);
        animator.SetTrigger(attackTrigger);
    }

    // ── Input Callbacks ───────────────────────────────────────────────────
    private void OnAttack(InputAction.CallbackContext ctx)
    {
        attackPressed = true;
        if (animator != null && !state.isAttacking && !state.isBlocking && !state.isCrouchBlocking)
        {
            animator.ResetTrigger(attackTrigger);
            animator.SetTrigger(attackTrigger);
        }
    }

    private void OnBlockPerformed(InputAction.CallbackContext ctx) => blockHeld = true;
    private void OnBlockCanceled(InputAction.CallbackContext ctx)  => blockHeld = false;

    private void OnVerticalAttack(InputAction.CallbackContext ctx)
    {
        verticalAttackPressed = true;
        verticalAttackHeld    = true;
    }

    private void OnVerticalAttackCanceled(InputAction.CallbackContext ctx)
        => verticalAttackHeld = false;
}
