using UnityEngine;

/// <summary>
/// Shared state container attached to Player root.
/// PlayerMovement, PlayerCombat, and PlayerAnimator all read/write this.
/// No logic here — pure data only.
/// </summary>
public class PlayerState : MonoBehaviour
{
    // ── Movement State ─────────────────────────────────────────────────────
    public bool isGrounded;
    public bool didJump;
    public bool isDashing;
    public bool isCrouching;
    [Tooltip("True during a crouch-slide (dash + crouch). Capped by slideMaxDuration.")]
    public bool isSliding;
    public Vector2 velocity;

    // ── Combat State ───────────────────────────────────────────────────────
    public bool isAttacking;
    public bool isVerticalAttacking;
    public bool isVerticalAttackHolding;
    public bool isBlocking;
    public bool isCrouchBlocking;

    // ── Damage State ─────────────────────────────────────────
    public bool isKnocked;

    // ── Ground Info ────────────────────────────────────────────────────────
    public Collider2D groundCollider;
    public Vector2 groundVelocity;
    public Vector2 groundNormal = Vector2.up;

    // ── Input (written by Combat, read by Movement) ────────────────────────
    public bool crouchHeld;
    public bool jumpPressed;
    public bool jumpHeld;
    public Vector2 moveInput;
}