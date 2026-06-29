using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles: movement, jump, dash, ground check, physics, facing.
/// Reads combat state from PlayerState to know when to lock movement.
/// </summary>
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 6f;
    public float jumpForce = 12f;

    [Header("Jump Feel")]
    public float fallMultiplier = 2.2f;
    public float jumpCutMultiplier = 2.0f;
    public float apexDownBoost = 0.5f;

    [Header("Air Control")]
    [Range(0f, 1f)] public float airControlLerp = 0.20f;

    [Header("Ground Check")]
    public LayerMask groundLayer;
    public float groundCheckDistance = 0.16f;
    [Range(0f, 1f)] public float minGroundNormalY = 0.35f;

    [Header("Coyote Time")]
    public float groundedGraceTime = 0.18f;
    public float jumpGroundedMaxRelativeYSpeed = 3.0f;
    public float coyoteGroundedMaxRelativeYSpeed = 3.0f;

    [Header("Input Drift Fix")]
    public float inputDeadzone = 0.1f;
    public bool forceStopXWhenIdle = true;
    public float idleStopVelEpsilon = 0.2f;

    [Header("Slope Stick")]
    public bool stopSlidingWhenIdle = true;

    [Header("Physics Material")]
    public PhysicsMaterial2D noFrictionMaterial;

    [Header("Movement Lock")]
    public bool lockMovementDuringAttack = true;
    public bool lockMovementDuringBlock = true;

    [Header("Dash")]
    public float dashSpeed = 18f;
    public float dashMinDuration = 0.4f;
    public float dashCooldown = 0.8f;
    public float doubleTapWindow = 0.25f;

    [Header("Debug")]
    public bool debugGround = false;

    // ── Components ──
    private Rigidbody2D rb;
    private CapsuleCollider2D capsule;
    private Transform graphics;
    private PlayerInputActions input;
    private PlayerState state;

    // ── Internal movement state ──
    private float lastGroundedTime;
    private float dashDirection = 0f;
    private float dashCooldownRemaining = 0f;
    private float dashTimeRemaining = 0f;
    private bool prevRightHeld = false;
    private bool prevLeftHeld = false;
    private float lastRightTapTime = -999f;
    private float lastLeftTapTime = -999f;

    // ── Public accessors ──
    public bool IsGrounded          => state.isGrounded;
    public bool IsDashing           => state.isDashing;
    public bool IsBlocking          => state.isBlocking;
    public bool IsCrouchBlocking    => state.isCrouchBlocking;
    public bool IsVerticalAttacking => state.isVerticalAttacking;
    public Collider2D GroundCollider => state.groundCollider;

    private void Awake()
    {
        rb      = GetComponent<Rigidbody2D>();
        capsule = GetComponent<CapsuleCollider2D>();
        state   = GetComponent<PlayerState>();

        Transform graphicsChild = transform.Find("Graphics");
        graphics = graphicsChild != null
            ? graphicsChild
            : GetComponentInChildren<SpriteRenderer>()?.transform ?? transform;

        input = new PlayerInputActions();
        rb.linearDamping = 0f;
        rb.constraints   = RigidbodyConstraints2D.FreezeRotation;

        if (groundLayer.value == 0)
        {
            int idx = LayerMask.NameToLayer("Ground");
            if (idx >= 0) groundLayer = 1 << idx;
        }

        if (noFrictionMaterial != null && capsule != null)
            capsule.sharedMaterial = noFrictionMaterial;
    }

    private void OnEnable()
    {
        input.Player.Enable();
        input.Player.Move.performed   += OnMove;
        input.Player.Move.canceled    += OnMoveCancel;
        input.Player.Jump.performed   += OnJump;
        input.Player.Jump.canceled    += OnJumpCancel;
        input.Player.Crouch.performed += OnCrouch;
        input.Player.Crouch.canceled  += OnCrouchCancel;
    }

    private void OnDisable()
    {
        input.Player.Move.performed   -= OnMove;
        input.Player.Move.canceled    -= OnMoveCancel;
        input.Player.Jump.performed   -= OnJump;
        input.Player.Jump.canceled    -= OnJumpCancel;
        input.Player.Crouch.performed -= OnCrouch;
        input.Player.Crouch.canceled  -= OnCrouchCancel;
        input.Player.Disable();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            state.moveInput   = Vector2.zero;
            state.jumpPressed = false;
            state.jumpHeld    = false;
            state.crouchHeld  = false;
            prevRightHeld     = false;
            prevLeftHeld      = false;
        }
    }

    private void Update()
    {
        bool rightHeld = state.moveInput.x > 0.5f;
        bool leftHeld  = state.moveInput.x < -0.5f;

        if (rightHeld && !prevRightHeld)
        {
            if (Time.time - lastRightTapTime <= doubleTapWindow) TryStartDash(1f);
            else lastRightTapTime = Time.time;
        }

        if (leftHeld && !prevLeftHeld)
        {
            if (Time.time - lastLeftTapTime <= doubleTapWindow) TryStartDash(-1f);
            else lastLeftTapTime = Time.time;
        }

        prevRightHeld = rightHeld;
        prevLeftHeld  = leftHeld;
    }

    private void FixedUpdate()
    {
        CheckGrounded();

        if (dashCooldownRemaining > 0f)
            dashCooldownRemaining = Mathf.Max(0f, dashCooldownRemaining - Time.fixedDeltaTime);

        HandleDash();
        HandleMovement();
        HandleJump();
        ApplyBetterGravity();

        state.velocity    = rb.linearVelocity;
        state.jumpPressed = false;
    }

    private void TryStartDash(float dir)
    {
        if (state.isDashing) return;
        if (!state.isGrounded) return;
        if (dashCooldownRemaining > 0f) return;
        if (state.crouchHeld) return;
        if (lockMovementDuringAttack && state.isAttacking) return;
        if (lockMovementDuringBlock && (state.isBlocking || state.isCrouchBlocking)) return;

        dashDirection         = dir;
        state.isDashing       = true;
        dashTimeRemaining     = dashMinDuration;
        dashCooldownRemaining = dashCooldown;
    }

    private void HandleDash()
    {
        if (!state.isDashing) return;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        if (state.isAttacking)
        {
            state.isDashing   = false;
            dashTimeRemaining = 0f;
            return;
        }

        if (!state.isGrounded)
        {
            state.isDashing = false;
            return;
        }

        dashTimeRemaining -= Time.fixedDeltaTime;

        float rawX        = state.moveInput.x;
        bool stillHolding = (dashDirection > 0f && rawX > 0.1f) ||
                            (dashDirection < 0f && rawX < -0.1f);

        if (dashTimeRemaining > 0f || stillHolding)
            rb.linearVelocity = new Vector2(dashDirection * dashSpeed, 0f);
        else
            state.isDashing = false;
    }

    private void CheckGrounded()
    {
        if (capsule == null)
        {
            state.isGrounded     = false;
            state.groundNormal   = Vector2.up;
            state.groundCollider = null;
            state.groundVelocity = Vector2.zero;
            return;
        }

        RaycastHit2D hit = Physics2D.CapsuleCast(
            capsule.bounds.center, capsule.bounds.size,
            capsule.direction, 0f,
            Vector2.down, groundCheckDistance, groundLayer
        );

        bool validSurface = hit.collider != null && hit.normal.y >= minGroundNormalY;

        if (validSurface)
        {
            Vector2 groundVel = GetGroundVelocity(hit.collider, hit.rigidbody);
            float relativeY   = rb.linearVelocity.y - groundVel.y;

            if (relativeY <= jumpGroundedMaxRelativeYSpeed)
            {
                state.isGrounded     = true;
                lastGroundedTime     = Time.time;
                state.didJump        = false;
                state.groundNormal   = hit.normal;
                state.groundCollider = hit.collider;
                state.groundVelocity = groundVel;
                return;
            }
        }

        float relY       = rb.linearVelocity.y - state.groundVelocity.y;
        bool withinGrace = (Time.time - lastGroundedTime) <= groundedGraceTime;
        state.isGrounded = withinGrace && relY <= coyoteGroundedMaxRelativeYSpeed;

        if (state.isGrounded)
            state.didJump = false;
        else
        {
            state.groundNormal   = Vector2.up;
            state.groundCollider = null;
            state.groundVelocity = Vector2.zero;
        }
    }

    private Vector2 GetGroundVelocity(Collider2D hitCollider, Rigidbody2D hitRb)
    {
        if (hitCollider == null) return Vector2.zero;
        MovingPlatform mp = hitCollider.GetComponent<MovingPlatform>()
                         ?? hitCollider.GetComponentInParent<MovingPlatform>();
        if (mp != null) return mp.Velocity;
        if (hitRb != null) return hitRb.linearVelocity;
        return Vector2.zero;
    }

    private void HandleMovement()
    {
        if (state.isDashing) return;

        if (lockMovementDuringBlock && (state.isBlocking || state.isCrouchBlocking))
        {
            rb.linearVelocity = new Vector2(state.groundVelocity.x, rb.linearVelocity.y);
            return;
        }

        if (lockMovementDuringAttack && state.isAttacking)
        {
            rb.linearVelocity = new Vector2(state.groundVelocity.x, rb.linearVelocity.y);
            return;
        }

        float rawX = state.moveInput.x;
        if (Mathf.Abs(rawX) < inputDeadzone) rawX = 0f;

        float targetX = ((state.crouchHeld || state.isCrouchBlocking) && state.isGrounded)
            ? 0f
            : rawX * moveSpeed;

        if (state.isGrounded)
        {
            float finalX = targetX + state.groundVelocity.x;
            rb.linearVelocity = new Vector2(finalX, rb.linearVelocity.y);

            if (stopSlidingWhenIdle && rawX == 0f)
            {
                rb.linearVelocity = new Vector2(state.groundVelocity.x, state.groundVelocity.y);

                bool onMovingPlatform = state.groundCollider != null &&
                    state.groundCollider.GetComponentInParent<MovingPlatform>() != null;

                rb.constraints = onMovingPlatform
                    ? RigidbodyConstraints2D.FreezeRotation
                    : RigidbodyConstraints2D.FreezePositionY | RigidbodyConstraints2D.FreezeRotation;
            }
            else
            {
                rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            }

            if (forceStopXWhenIdle && rawX == 0f &&
                Mathf.Abs(rb.linearVelocity.x - state.groundVelocity.x) <= idleStopVelEpsilon)
                rb.linearVelocity = new Vector2(state.groundVelocity.x, rb.linearVelocity.y);
        }
        else
        {
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            float newX     = Mathf.Lerp(rb.linearVelocity.x, targetX, airControlLerp);
            rb.linearVelocity = new Vector2(newX, rb.linearVelocity.y);
        }

        if (graphics != null && rawX != 0f)
        {
            Vector3 s = graphics.localScale;
            s.x = Mathf.Sign(rawX) * Mathf.Abs(s.x);
            graphics.localScale = s;
        }
    }

    private void HandleJump()
    {
        if (!state.jumpPressed) return;
        if (state.isBlocking || state.isCrouchBlocking) return;

        if (state.isDashing)
        {
            state.isDashing   = false;
            dashTimeRemaining = 0f;
        }

        float relativeY   = rb.linearVelocity.y - state.groundVelocity.y;
        bool withinGrace  = (Time.time - lastGroundedTime) <= groundedGraceTime;
        bool canUseCoyote = withinGrace && relativeY <= coyoteGroundedMaxRelativeYSpeed;

        if (!(state.isGrounded || canUseCoyote)) return;
        if (state.crouchHeld) return;
        if (lockMovementDuringAttack && state.isAttacking) return;

        rb.constraints       = RigidbodyConstraints2D.FreezeRotation;
        rb.linearVelocity    = new Vector2(rb.linearVelocity.x, jumpForce);
        state.isGrounded     = false;
        state.didJump        = true;
        state.groundCollider = null;
        state.groundVelocity = Vector2.zero;
        state.groundNormal   = Vector2.up;
    }

    private void ApplyBetterGravity()
    {
        if (state.isDashing) return;

        if (rb.linearVelocity.y < 0f)
            rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (fallMultiplier - 1f) * Time.fixedDeltaTime;
        else if (rb.linearVelocity.y > 0f && !state.jumpHeld)
            rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (jumpCutMultiplier - 1f) * Time.fixedDeltaTime;
        else if (Mathf.Abs(rb.linearVelocity.y) < 0.1f && !state.isGrounded)
            rb.linearVelocity += Vector2.down * apexDownBoost * Time.fixedDeltaTime;
    }

    // ── Input Callbacks ───────────────────────────────────────────────────
    private void OnMove(InputAction.CallbackContext ctx)         => state.moveInput = ctx.ReadValue<Vector2>();
    private void OnMoveCancel(InputAction.CallbackContext ctx)   => state.moveInput = Vector2.zero;
    private void OnJump(InputAction.CallbackContext ctx)         { state.jumpPressed = true; state.jumpHeld = true; }
    private void OnJumpCancel(InputAction.CallbackContext ctx)   => state.jumpHeld = false;
    private void OnCrouch(InputAction.CallbackContext ctx)       => state.crouchHeld = true;
    private void OnCrouchCancel(InputAction.CallbackContext ctx) => state.crouchHeld = false;

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        CapsuleCollider2D cap = GetComponent<CapsuleCollider2D>();
        if (cap == null) return;
        Gizmos.color = Color.green;
        Vector2 origin  = cap.bounds.center;
        Vector2 size    = cap.bounds.size;
        Vector2 castEnd = origin + Vector2.down * groundCheckDistance;
        Gizmos.DrawWireCube(origin, size);
        Gizmos.DrawWireCube(castEnd, size);
        Gizmos.DrawLine(new Vector2(origin.x - size.x * 0.5f, origin.y), new Vector2(castEnd.x - size.x * 0.5f, castEnd.y));
        Gizmos.DrawLine(new Vector2(origin.x + size.x * 0.5f, origin.y), new Vector2(castEnd.x + size.x * 0.5f, castEnd.y));
    }
#endif
}