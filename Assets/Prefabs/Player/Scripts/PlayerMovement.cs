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

    [Header("Slide (dash + crouch)")]
    [Tooltip("Max slide length in seconds. This is the hard cap on crouch-dashing.")]
    public float slideMaxDuration = 2.5f;
    [Tooltip("Starting slide speed. Near dashSpeed so the handoff is seamless.")]
    public float slideStartSpeed = 16f;
    [Tooltip("Speed over the slide. 1 = full, 0 = stopped.")]
    public AnimationCurve slideFalloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    [Tooltip("Delay before another slide can start.")]
    public float slideCooldown = 0.35f;
    [Tooltip("Releasing crouch ends the slide immediately.")]
    public bool releaseCrouchEndsSlide = true;
    [Tooltip("Crouch starts a slide only at this speed or above.\nSits BETWEEN moveSpeed (6) and dashSpeed (18) so normal running never slides.")]
    public float slideEntrySpeedThreshold = 12f;
    [Tooltip("After a slide, crouch must be RELEASED before another slide can start.\nStops dash-into-slide chaining while C is held down.")]
    public bool requireCrouchReleaseBeforeNextSlide = true;

    [Header("Dash Jump")]
    [Tooltip("Dash keeps running through the jump as long as the same direction is held.")]
    public bool dashContinuesThroughJump = true;
    [Tooltip("1 = forward jump speed is EXACTLY dash speed. Lower/raise only to tune feel.")]
    public float airDashSpeedMultiplier = 1f;
    [Tooltip("If false, walking/dashing off a ledge (no jump) cancels the dash like before.")]
    public bool dashContinuesOffLedge = false;

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
    private float slideTimeRemaining = 0f;
    private float slideDirection = 0f;
    private float slideCooldownRemaining = 0f;
    private bool  slideConsumed = false;
    private bool prevRightHeld = false;
    private bool prevLeftHeld = false;
    private float lastRightTapTime = -999f;
    private float lastLeftTapTime = -999f;

    // ── Public accessors ──
    public bool IsGrounded          => state.isGrounded;
    public bool IsDashing           => state.isDashing;
    public bool IsAirDashing        => state.isDashing && !state.isGrounded;
    public bool IsSliding           => state.isSliding;
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

        if (slideCooldownRemaining > 0f)
            slideCooldownRemaining = Mathf.Max(0f, slideCooldownRemaining - Time.fixedDeltaTime);

        // Re-arm the slide only once crouch has actually been let go.
        if (!state.crouchHeld) slideConsumed = false;

        TryStartSlideFromCrouch();
        HandleSlide();
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
        if (state.isSliding) return;               // no dashing out of a slide
        if (state.crouchHeld && slideConsumed) return;  // must stand up first
        if (state.isKnocked) return;
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

    /// <summary>True while the raw move input is still pushing in the dash direction.</summary>
    private bool HoldingDashDirection()
    {
        float rawX = state.moveInput.x;
        return (dashDirection > 0f && rawX >  0.1f) ||
               (dashDirection < 0f && rawX < -0.1f);
    }

    private void HandleDash()
    {
        if (!state.isDashing) return;

        // Crouch mid-dash hands off to the slide.
        if (state.crouchHeld && CanStartSlide())
        {
            StartSlide(dashDirection);
            return;
        }
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        if (state.isAttacking)
        {
            state.isDashing   = false;
            dashTimeRemaining = 0f;
            return;
        }

        bool stillHolding = HoldingDashDirection();
        dashTimeRemaining -= Time.fixedDeltaTime;

        // ── Airborne part of the dash (dash jump) ──
        if (!state.isGrounded)
        {
            bool allowedInAir = dashContinuesThroughJump &&
                                (state.didJump || dashContinuesOffLedge);

            if (!allowedInAir || (!stillHolding && dashTimeRemaining <= 0f))
            {
                state.isDashing = false;
                return;
            }

            // Lock X to dash speed, leave Y alone so the jump arc is untouched.
            rb.linearVelocity = new Vector2(
                dashDirection * dashSpeed * airDashSpeedMultiplier,
                rb.linearVelocity.y
            );
            return;
        }

        // ── Grounded dash (unchanged) ──
        if (dashTimeRemaining > 0f || stillHolding)
            rb.linearVelocity = new Vector2(dashDirection * dashSpeed, 0f);
        else
            state.isDashing = false;
    }

    // ─────────────────────────────────────────────────────────
    //  SLIDE  (dash + crouch)
    // ─────────────────────────────────────────────────────────
    private bool CanStartSlide()
    {
        if (state.isSliding) return false;
        if (slideCooldownRemaining > 0f) return false;
        if (state.isKnocked || state.isAttacking) return false;
        if (requireCrouchReleaseBeforeNextSlide && slideConsumed) return false;
        return true;
    }

    /// <summary>
    /// Speed-based entry, so it works mid-air after a dash jump and a frame
    /// after the dash itself has ended while momentum is still high.
    /// </summary>
    private void TryStartSlideFromCrouch()
    {
        if (!state.crouchHeld) return;
        if (!CanStartSlide()) return;

        float vx = rb.linearVelocity.x;
        if (Mathf.Abs(vx) < slideEntrySpeedThreshold) return;

        StartSlide(Mathf.Sign(vx));
    }

    private void StartSlide(float dir)
    {
        if (Mathf.Approximately(dir, 0f)) dir = FacingSign();

        slideDirection     = Mathf.Sign(dir);
        slideTimeRemaining = slideMaxDuration;

        state.isSliding = true;
        state.isDashing = false;      // slide replaces the dash
        dashTimeRemaining = 0f;

        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    private void HandleSlide()
    {
        if (!state.isSliding) return;

        if (releaseCrouchEndsSlide && !state.crouchHeld) { EndSlide(); return; }
        if (state.isAttacking || state.isKnocked)        { EndSlide(); return; }

        slideTimeRemaining -= Time.fixedDeltaTime;

        if (slideTimeRemaining <= 0f) { EndSlide(); return; }

        float t     = 1f - Mathf.Clamp01(slideTimeRemaining / slideMaxDuration);
        float speed = slideStartSpeed * slideFalloff.Evaluate(t);

        // Direction is LOCKED to slideDirection — input cannot turn you mid-slide.
        rb.linearVelocity = new Vector2(slideDirection * speed, rb.linearVelocity.y);
    }

    private void EndSlide()
    {
        if (!state.isSliding) return;

        state.isSliding        = false;
        slideTimeRemaining     = 0f;
        slideCooldownRemaining = slideCooldown;
        slideConsumed          = true;   // cleared when crouch is released

        // Kill momentum so it settles into crouch instead of gliding on.
        rb.linearVelocity = new Vector2(state.groundVelocity.x, rb.linearVelocity.y);
    }

    private float FacingSign()
    {
        return graphics != null ? Mathf.Sign(graphics.localScale.x) : 1f;
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
        // KNOCKBACK GATE — PlayerHealth owns horizontal velocity while sliding.
        // Without this line every slide is erased in the same physics step.
        if (state.isKnocked) return;

        if (state.isDashing) return;
        if (state.isSliding) return;   // HandleSlide owns X and facing while sliding

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

        float relativeY   = rb.linearVelocity.y - state.groundVelocity.y;
        bool withinGrace  = (Time.time - lastGroundedTime) <= groundedGraceTime;
        bool canUseCoyote = withinGrace && relativeY <= coyoteGroundedMaxRelativeYSpeed;

        if (!(state.isGrounded || canUseCoyote)) return;
        if (state.crouchHeld) return;
        if (lockMovementDuringAttack && state.isAttacking) return;

        // ── The jump is definitely happening from here on ──

        bool keepDash = state.isDashing &&
                        dashContinuesThroughJump &&
                        HoldingDashDirection();

        if (state.isDashing && !keepDash)
        {
            state.isDashing   = false;
            dashTimeRemaining = 0f;
        }

        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        // Dash jump launches at dash speed. Normal jump keeps whatever run speed it had.
        float launchX = keepDash
            ? dashDirection * dashSpeed * airDashSpeedMultiplier
            : rb.linearVelocity.x;

        rb.linearVelocity    = new Vector2(launchX, jumpForce);
        state.isGrounded     = false;
        state.didJump        = true;
        state.groundCollider = null;
        state.groundVelocity = Vector2.zero;
        state.groundNormal   = Vector2.up;
    }

    private void ApplyBetterGravity()
    {
        // Grounded dash still skips the gravity shaping (original behavior).
        // An air dash must NOT skip it, or the jump arc floats.
        if (state.isDashing && state.isGrounded) return;

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