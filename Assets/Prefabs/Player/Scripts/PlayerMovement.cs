using UnityEngine;
using UnityEngine.InputSystem;

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
    public float idleSlopeStickVelocity = 0.05f;

    [Header("Physics Material")]
    public PhysicsMaterial2D noFrictionMaterial;

    [Header("Attack")]
    public string attackTrigger = "attack";
    public bool lockMovementDuringAttack = true;

    [Header("Block")]
    public bool lockMovementDuringBlock = true;

    [Header("Vertical Attack")]
    public string verticalAttackTrigger = "verticalAttack";
    [Tooltip("Normalized time (0-1) at which hold-V freezes the animation. Tune per spritesheet: 6fr=0.16  8fr=0.12  10fr=0.10  12fr=0.08")]
    public float verticalAttackHoldFrameTime = 0.08f;

    [Header("Animator")]
    public string didJumpBool = "didJump";

    [Header("Dash")]
    public float dashSpeed = 18f;
    public float dashMinDuration = 0.4f;
    public float dashCooldown = 0.8f;
    public float doubleTapWindow = 0.25f;
    public float dashParticleVelocityX = 15f;

    [Header("Debug")]
    public bool debugGround = false;

    // ── Components ──
    private Rigidbody2D rb;
    private CapsuleCollider2D capsule;
    private Animator animator;
    private Transform graphics;
    private PlayerInputActions input;
    private ParticleSystem dashParticles;

    // ── Input flags ──
    private Vector2 moveInput;
    private bool jumpPressed;
    private bool jumpHeld;
    private bool crouchHeld;
    private bool attackPressed;
    private bool blockHeld;
    private bool verticalAttackPressed;
    private bool verticalAttackHeld;

    // ── State ──
    private bool isGrounded;
    private bool didJump;
    private bool isAttacking;
    private bool isVerticalAttacking;
    private bool isVerticalAttackHolding;
    private bool isBlocking;
    private float lastGroundedTime;

    // ── Ground tracking ──
    private Vector2 groundNormal = Vector2.up;
    private Collider2D currentGroundCollider;
    private Rigidbody2D currentGroundRigidbody;
    private Vector2 currentGroundVelocity;

    // ── Dash ──
    private bool isDashing = false;
    private float dashDirection = 0f;
    private float dashCooldownRemaining = 0f;
    private float dashTimeRemaining = 0f;

    // ── Double tap tracking ──
    private float lastRightTapTime = -999f;
    private float lastLeftTapTime = -999f;
    private bool prevRightHeld = false;
    private bool prevLeftHeld = false;

    // ── Public accessors ──
    public bool IsGrounded => isGrounded;
    public Collider2D GroundCollider => currentGroundCollider;
    public bool IsBlocking => isBlocking;
    public bool IsVerticalAttacking => isVerticalAttacking;

    // ═══════════════════════════════════════════
    //  LIFECYCLE
    // ═══════════════════════════════════════════

    private void Awake()
    {
        rb      = GetComponent<Rigidbody2D>();
        capsule = GetComponent<CapsuleCollider2D>();

        animator = GetComponent<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        Transform graphicsChild = transform.Find("Graphics");
        if (graphicsChild != null)
            graphics = graphicsChild;
        else
        {
            SpriteRenderer sr = GetComponentInChildren<SpriteRenderer>();
            graphics = sr != null ? sr.transform : transform;
        }

        dashParticles = GetComponent<ParticleSystem>();
        if (dashParticles == null)
            dashParticles = GetComponentInChildren<ParticleSystem>();

        input = new PlayerInputActions();
        rb.linearDamping = 0f;

        if (groundLayer.value == 0)
        {
            int groundIndex = LayerMask.NameToLayer("Ground");
            if (groundIndex >= 0)
                groundLayer = 1 << groundIndex;
        }

        if (noFrictionMaterial != null && capsule != null)
            capsule.sharedMaterial = noFrictionMaterial;
    }

    private void OnEnable()
    {
        input.Player.Enable();

        input.Player.Move.performed += OnMove;
        input.Player.Move.canceled  += OnMoveCancel;

        input.Player.Jump.performed += OnJump;
        input.Player.Jump.canceled  += OnJumpCancel;

        input.Player.Crouch.performed += OnCrouch;
        input.Player.Crouch.canceled  += OnCrouchCancel;

        try { input.Player.Attack.performed += OnAttack; } catch { }

        try
        {
            input.Player.Block.performed += OnBlockPerformed;
            input.Player.Block.canceled  += OnBlockCanceled;
        }
        catch { }

        try
        {
            input.Player.VerticalAttack.performed += OnVerticalAttack;
            input.Player.VerticalAttack.canceled  += OnVerticalAttackCanceled;
        }
        catch { }
    }

    private void OnDisable()
    {
        input.Player.Move.performed -= OnMove;
        input.Player.Move.canceled  -= OnMoveCancel;

        input.Player.Jump.performed -= OnJump;
        input.Player.Jump.canceled  -= OnJumpCancel;

        input.Player.Crouch.performed -= OnCrouch;
        input.Player.Crouch.canceled  -= OnCrouchCancel;

        try { input.Player.Attack.performed -= OnAttack; } catch { }

        try
        {
            input.Player.Block.performed -= OnBlockPerformed;
            input.Player.Block.canceled  -= OnBlockCanceled;
        }
        catch { }

        try
        {
            input.Player.VerticalAttack.performed -= OnVerticalAttack;
            input.Player.VerticalAttack.canceled  -= OnVerticalAttackCanceled;
        }
        catch { }

        input.Player.Disable();
    }

    // ── Phantom walk fix ──
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) ClearAllInput();
    }

    private void ClearAllInput()
    {
        moveInput               = Vector2.zero;
        jumpPressed             = false;
        jumpHeld                = false;
        crouchHeld              = false;
        attackPressed           = false;
        blockHeld               = false;
        verticalAttackPressed   = false;
        verticalAttackHeld      = false;
        isVerticalAttackHolding = false;
        prevRightHeld           = false;
        prevLeftHeld            = false;

        // Safety: always restore animator speed on focus loss
        if (animator != null)
            animator.speed = 1f;
    }

    // ═══════════════════════════════════════════
    //  UPDATE — double tap dash detection
    // ═══════════════════════════════════════════

    private void Update()
    {
        bool rightHeld = moveInput.x > 0.5f;
        bool leftHeld  = moveInput.x < -0.5f;

        if (rightHeld && !prevRightHeld)
        {
            if (Time.time - lastRightTapTime <= doubleTapWindow)
                TryStartDash(1f);
            else
                lastRightTapTime = Time.time;
        }

        if (leftHeld && !prevLeftHeld)
        {
            if (Time.time - lastLeftTapTime <= doubleTapWindow)
                TryStartDash(-1f);
            else
                lastLeftTapTime = Time.time;
        }

        prevRightHeld = rightHeld;
        prevLeftHeld  = leftHeld;
    }

    // ═══════════════════════════════════════════
    //  FIXED UPDATE
    // ═══════════════════════════════════════════

    private void FixedUpdate()
    {
        CheckGrounded();

        // ── Resolve attack states from animator ──
        if (animator != null)
        {
            AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
            isVerticalAttacking = st.IsName("VerticalAttack");
            isAttacking         = st.IsName("Attack") || st.IsName("SitAttack") || isVerticalAttacking;
        }
        else
        {
            isAttacking         = false;
            isVerticalAttacking = false;
        }

        // Blocking: only grounded, not attacking, not dashing
        isBlocking = blockHeld && isGrounded && !isAttacking && !isDashing;

        if (dashCooldownRemaining > 0f)
            dashCooldownRemaining = Mathf.Max(0f, dashCooldownRemaining - Time.fixedDeltaTime);

        HandleDash();
        HandleMovement();
        HandleJump();
        ApplyBetterGravity();
        HandleAttack();
        UpdateAnimator();

        // Clear one-shot flags AFTER everything has consumed them
        jumpPressed           = false;
        attackPressed         = false;
        verticalAttackPressed = false;
    }

    // ═══════════════════════════════════════════
    //  DASH
    // ═══════════════════════════════════════════

    private void TryStartDash(float dir)
    {
        if (isDashing) return;
        if (!isGrounded) return;
        if (dashCooldownRemaining > 0f) return;
        if (crouchHeld) return;
        if (lockMovementDuringAttack && isAttacking) return;
        if (lockMovementDuringBlock && blockHeld) return;

        dashDirection         = dir;
        isDashing             = true;
        dashTimeRemaining     = dashMinDuration;
        dashCooldownRemaining = dashCooldown;

        if (dashParticles != null)
        {
            var vel = dashParticles.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(dir > 0f ? -dashParticleVelocityX : dashParticleVelocityX);
            vel.y = new ParticleSystem.MinMaxCurve(0f);
            vel.z = new ParticleSystem.MinMaxCurve(0f);
            dashParticles.Play();
        }
    }

    private void HandleDash()
    {
        if (!isDashing) return;

        // Stop dash immediately if attack or vertical attack fires
        if (isAttacking)
        {
            isDashing = false;
            dashTimeRemaining = 0f;
            if (dashParticles != null) dashParticles.Stop();
            return;
        }

        if (!isGrounded)
        {
            isDashing = false;
            if (dashParticles != null) dashParticles.Stop();
            return;
        }

        dashTimeRemaining -= Time.fixedDeltaTime;

        float rawX = moveInput.x;
        bool stillHolding = (dashDirection > 0f && rawX > 0.1f) ||
                            (dashDirection < 0f && rawX < -0.1f);

        if (dashTimeRemaining > 0f || stillHolding)
            rb.linearVelocity = new Vector2(dashDirection * dashSpeed, 0f);
        else
        {
            isDashing = false;
            if (dashParticles != null) dashParticles.Stop();
        }
    }

    // ═══════════════════════════════════════════
    //  GROUND CHECK
    // ═══════════════════════════════════════════

    private void CheckGrounded()
    {
        if (capsule == null)
        {
            isGrounded             = false;
            groundNormal           = Vector2.up;
            currentGroundCollider  = null;
            currentGroundRigidbody = null;
            currentGroundVelocity  = Vector2.zero;
            return;
        }

        RaycastHit2D hit = Physics2D.CapsuleCast(
            capsule.bounds.center,
            capsule.bounds.size,
            capsule.direction,
            0f,
            Vector2.down,
            groundCheckDistance,
            groundLayer
        );

        bool validSurface = hit.collider != null && hit.normal.y >= minGroundNormalY;

        if (validSurface)
        {
            Vector2 hitGroundVelocity = GetGroundVelocity(hit.collider, hit.rigidbody);
            float relativeY           = rb.linearVelocity.y - hitGroundVelocity.y;

            if (relativeY <= jumpGroundedMaxRelativeYSpeed)
            {
                isGrounded             = true;
                lastGroundedTime       = Time.time;
                didJump                = false;
                groundNormal           = hit.normal;
                currentGroundCollider  = hit.collider;
                currentGroundRigidbody = hit.rigidbody;
                currentGroundVelocity  = hitGroundVelocity;

                if (debugGround)
                    Debug.Log($"[GROUND] grounded=true hit={hit.collider.name} dist={hit.distance:F4} normal={hit.normal} playerVy={rb.linearVelocity.y:F3} groundVy={currentGroundVelocity.y:F3} relY={relativeY:F3}", this);

                return;
            }
        }

        float relativeYDuringGrace = rb.linearVelocity.y - currentGroundVelocity.y;
        bool  withinGrace          = (Time.time - lastGroundedTime) <= groundedGraceTime;
        isGrounded = withinGrace && relativeYDuringGrace <= coyoteGroundedMaxRelativeYSpeed;

        if (isGrounded)
            didJump = false;
        else
        {
            groundNormal           = Vector2.up;
            currentGroundCollider  = null;
            currentGroundRigidbody = null;
            currentGroundVelocity  = Vector2.zero;
        }

        if (debugGround)
        {
            if (hit.collider != null)
            {
                Vector2 hgv = GetGroundVelocity(hit.collider, hit.rigidbody);
                float relY  = rb.linearVelocity.y - hgv.y;
                Debug.Log($"[GROUND] grounded={isGrounded} hit={hit.collider.name} dist={hit.distance:F4} normal={hit.normal} playerVy={rb.linearVelocity.y:F3} groundVy={hgv.y:F3} relY={relY:F3}", this);
            }
            else
                Debug.Log($"[GROUND] grounded={isGrounded} hit=null playerVy={rb.linearVelocity.y:F3} groundVy={currentGroundVelocity.y:F3}", this);
        }
    }

private Vector2 GetGroundVelocity(Collider2D hitCollider, Rigidbody2D hitRb)
{
    if (hitCollider == null) return Vector2.zero;

    MovingPlatform movingPlatform = hitCollider.GetComponent<MovingPlatform>();
    if (movingPlatform == null) movingPlatform = hitCollider.GetComponentInParent<MovingPlatform>();
    if (movingPlatform != null) return movingPlatform.Velocity;

    if (hitRb != null) return hitRb.linearVelocity;

    return Vector2.zero;
}

    // ═══════════════════════════════════════════
    //  MOVEMENT
    // ═══════════════════════════════════════════

    private void HandleMovement()
    {
        if (isDashing) return;

        if (lockMovementDuringBlock && isBlocking)
        {
            rb.linearVelocity = new Vector2(currentGroundVelocity.x, rb.linearVelocity.y);
            return;
        }

        if (lockMovementDuringAttack && isAttacking)
        {
            rb.linearVelocity = new Vector2(currentGroundVelocity.x, rb.linearVelocity.y);
            return;
        }

        float rawX = moveInput.x;
        if (Mathf.Abs(rawX) < inputDeadzone) rawX = 0f;

        float targetX = (crouchHeld && isGrounded) ? 0f : rawX * moveSpeed;

        if (isGrounded)
        {
            float finalX = targetX + currentGroundVelocity.x;
            rb.linearVelocity = new Vector2(finalX, rb.linearVelocity.y);

            if (stopSlidingWhenIdle && rawX == 0f)
            {
                Vector2 tangent    = new Vector2(groundNormal.y, -groundNormal.x).normalized;
                float tangentSpeed = Vector2.Dot(rb.linearVelocity - currentGroundVelocity, tangent);
                rb.linearVelocity -= tangent * tangentSpeed;

                if (Mathf.Abs(rb.linearVelocity.y - currentGroundVelocity.y) < idleSlopeStickVelocity)
                    rb.linearVelocity = new Vector2(rb.linearVelocity.x, currentGroundVelocity.y);
            }

            if (forceStopXWhenIdle && rawX == 0f && Mathf.Abs(rb.linearVelocity.x - currentGroundVelocity.x) <= idleStopVelEpsilon)
                rb.linearVelocity = new Vector2(currentGroundVelocity.x, rb.linearVelocity.y);
        }
        else
        {
            float newX = Mathf.Lerp(rb.linearVelocity.x, targetX, airControlLerp);
            rb.linearVelocity = new Vector2(newX, rb.linearVelocity.y);
        }

        if (graphics != null && rawX != 0f)
        {
            Vector3 s = graphics.localScale;
            s.x = Mathf.Sign(rawX) * Mathf.Abs(s.x);
            graphics.localScale = s;
        }
    }

    // ═══════════════════════════════════════════
    //  JUMP
    // ═══════════════════════════════════════════

    private void HandleJump()
    {
        if (!jumpPressed) return;
        if (isBlocking) return;

        if (isDashing)
        {
            isDashing         = false;
            dashTimeRemaining = 0f;
            if (dashParticles != null) dashParticles.Stop();
        }

        float relativeY   = rb.linearVelocity.y - currentGroundVelocity.y;
        bool  withinGrace  = (Time.time - lastGroundedTime) <= groundedGraceTime;
        bool  canUseCoyote = withinGrace && relativeY <= coyoteGroundedMaxRelativeYSpeed;

        if (!(isGrounded || canUseCoyote)) return;
        if (crouchHeld) return;
        if (lockMovementDuringAttack && isAttacking) return;

        rb.linearVelocity      = new Vector2(rb.linearVelocity.x, jumpForce);
        isGrounded             = false;
        didJump                = true;
        currentGroundCollider  = null;
        currentGroundRigidbody = null;
        currentGroundVelocity  = Vector2.zero;
        groundNormal           = Vector2.up;
    }

    // ═══════════════════════════════════════════
    //  GRAVITY
    // ═══════════════════════════════════════════

    private void ApplyBetterGravity()
    {
        if (isDashing) return;

        if (rb.linearVelocity.y < 0f)
            rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (fallMultiplier - 1f) * Time.fixedDeltaTime;
        else if (rb.linearVelocity.y > 0f && !jumpHeld)
            rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (jumpCutMultiplier - 1f) * Time.fixedDeltaTime;
        else if (Mathf.Abs(rb.linearVelocity.y) < 0.1f && !isGrounded)
            rb.linearVelocity += Vector2.down * apexDownBoost * Time.fixedDeltaTime;
    }

    // ═══════════════════════════════════════════
    //  ATTACK
    // ═══════════════════════════════════════════

    private void HandleAttack()
    {
        if (animator == null) return;
        if (isBlocking) return;

        // ── Vertical attack hold/resume logic ──
        if (isVerticalAttacking)
        {
            AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
            float normalizedTime = st.normalizedTime % 1f;

            // Only allow freeze if:
            // 1. V is held
            // 2. Animation hasn't passed frame 2 yet
            // 3. We haven't already frozen and resumed once this attack
            if (verticalAttackHeld &&
                normalizedTime >= verticalAttackHoldFrameTime &&
                normalizedTime < verticalAttackHoldFrameTime + 0.05f && // tight window around frame 2 only
                !isVerticalAttackHolding)
            {
                animator.speed = 0f;
                isVerticalAttackHolding = true;
            }

            // V released — resume
            if (isVerticalAttackHolding && !verticalAttackHeld)
            {
                animator.speed = 1f;
                isVerticalAttackHolding = false;
            }

            return;
        }

        // Safety: restore animator speed if state already exited
        if (animator.speed == 0f)
            animator.speed = 1f;

        if (isAttacking) return;

        // Vertical attack has priority over normal attack
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

    // ═══════════════════════════════════════════
    //  ANIMATOR
    // ═══════════════════════════════════════════

    private void UpdateAnimator()
    {
        if (animator == null) return;

        AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
        if (st.IsName("Death")) return;

        bool isCrouching = crouchHeld && isGrounded;
        bool allowRun    = !(lockMovementDuringAttack && isAttacking) && !isBlocking;
        bool isRunning   = allowRun && Mathf.Abs(moveInput.x) > 0.1f && isGrounded && !isCrouching;

        SetAnimatorBoolIfExists("isGrounded",          isGrounded);
        SetAnimatorBoolIfExists("isCrouching",         isCrouching);
        SetAnimatorBoolIfExists("isRunning",           isRunning);
        SetAnimatorBoolIfExists("isBlocking",          isBlocking);
        SetAnimatorBoolIfExists("isVerticalAttacking", isVerticalAttacking);
        SetAnimatorBoolIfExists("isDashing",           isDashing);
        SetAnimatorBoolIfExists(didJumpBool,           didJump);
        SetAnimatorFloatIfExists("yVelocity",          rb.linearVelocity.y);
    }

    private void SetAnimatorBoolIfExists(string param, bool value)
    {
        if (animator == null || string.IsNullOrEmpty(param)) return;
        for (int i = 0; i < animator.parameterCount; i++)
        {
            AnimatorControllerParameter p = animator.GetParameter(i);
            if (p.name == param && p.type == AnimatorControllerParameterType.Bool)
            {
                animator.SetBool(param, value);
                return;
            }
        }
    }

    private void SetAnimatorFloatIfExists(string param, float value)
    {
        if (animator == null || string.IsNullOrEmpty(param)) return;
        for (int i = 0; i < animator.parameterCount; i++)
        {
            AnimatorControllerParameter p = animator.GetParameter(i);
            if (p.name == param && p.type == AnimatorControllerParameterType.Float)
            {
                animator.SetFloat(param, value);
                return;
            }
        }
    }

    // ═══════════════════════════════════════════
    //  INPUT CALLBACKS
    // ═══════════════════════════════════════════

    private void OnMove(InputAction.CallbackContext ctx)         => moveInput = ctx.ReadValue<Vector2>();
    private void OnMoveCancel(InputAction.CallbackContext ctx)   => moveInput = Vector2.zero;

    private void OnJump(InputAction.CallbackContext ctx)         { jumpPressed = true; jumpHeld = true; }
    private void OnJumpCancel(InputAction.CallbackContext ctx)   => jumpHeld = false;

    private void OnCrouch(InputAction.CallbackContext ctx)       => crouchHeld = true;
    private void OnCrouchCancel(InputAction.CallbackContext ctx) => crouchHeld = false;

    private void OnAttack(InputAction.CallbackContext ctx)
    {
        attackPressed = true;
        if (animator != null && !isAttacking && !isBlocking)
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
    {
        verticalAttackHeld = false;
    }

    // ═══════════════════════════════════════════
    //  EDITOR GIZMOS
    // ═══════════════════════════════════════════

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