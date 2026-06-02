using UnityEngine;

public enum PlatformMoveType
{
    Horizontal,
    Vertical,
    Diagonal,
    Circle
}

[RequireComponent(typeof(Rigidbody2D))]
public class MovingPlatform : MonoBehaviour
{
    [Header("Movement Type")]
    public PlatformMoveType moveType = PlatformMoveType.Horizontal;

    [Header("Speed & Distance")]
    public float moveSpeed    = 2f;
    public float moveDistance = 4f;   // ignored for Circle

    [Header("Circle Settings")]
    public float circleRadius = 3f;
    public float circleSpeed  = 1f;   // revolutions per second

    [Header("Diagonal Settings")]
    [Range(-89f, 89f)]
    public float diagonalAngleDegrees = 45f;

    [Header("Easing & Pause")]
    public AnimationCurve easeCurve    = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public float          pauseDuration = 0.3f;

    [Header("Start Offset")]
    [Tooltip("Start mid-way through the path instead of at point A")]
    public bool startAtCenter = false;

    // ── public so PlayerMovement can read it ──
    public Vector2 Velocity { get; private set; }

    // ── internals ──
    private Rigidbody2D rb;
    private Vector2 pointA, pointB;
    private float   t          = 0f;
    private bool    goingToB   = true;
    private float   pauseTimer = 0f;
    private float   circleAngle = 0f;
    private Vector2 circleCenter;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.bodyType                 = RigidbodyType2D.Kinematic;
        rb.gravityScale             = 0f;
        rb.interpolation            = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode   = CollisionDetectionMode2D.Continuous;
        rb.useFullKinematicContacts = true;
    }

    void Start()
    {
        Vector2 origin = rb.position;

        switch (moveType)
        {
            case PlatformMoveType.Horizontal:
                pointA = origin - Vector2.right * (moveDistance * 0.5f);
                pointB = origin + Vector2.right * (moveDistance * 0.5f);
                break;

            case PlatformMoveType.Vertical:
                pointA = origin - Vector2.up * (moveDistance * 0.5f);
                pointB = origin + Vector2.up * (moveDistance * 0.5f);
                break;

            case PlatformMoveType.Diagonal:
                float rad = diagonalAngleDegrees * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;
                pointA = origin - dir * (moveDistance * 0.5f);
                pointB = origin + dir * (moveDistance * 0.5f);
                break;

            case PlatformMoveType.Circle:
                circleCenter = origin;
                circleAngle  = 0f;
                break;
        }

        if (startAtCenter)
            t = 0.5f;
    }

    void FixedUpdate()
    {
        if (moveType == PlatformMoveType.Circle)
        {
            HandleCircle();
            return;
        }

        HandleLinear();
    }

    void HandleLinear()
    {
        if (pauseTimer > 0f)
        {
            pauseTimer -= Time.fixedDeltaTime;
            Velocity = Vector2.zero;
            return;
        }

        t += (moveSpeed / moveDistance) * Time.fixedDeltaTime;

        Vector2 prev   = rb.position;
        float   curved = easeCurve.Evaluate(Mathf.Clamp01(t));
        Vector2 next   = goingToB
            ? Vector2.Lerp(pointA, pointB, curved)
            : Vector2.Lerp(pointB, pointA, curved);

        rb.MovePosition(next);
        Velocity = (next - prev) / Time.fixedDeltaTime;

        if (t >= 1f)
        {
            t          = 0f;
            goingToB   = !goingToB;
            pauseTimer = pauseDuration;
        }
    }

    void HandleCircle()
    {
        circleAngle += circleSpeed * 360f * Time.fixedDeltaTime;

        float rad  = circleAngle * Mathf.Deg2Rad;
        Vector2 prev = rb.position;
        Vector2 next = circleCenter + new Vector2(
            Mathf.Cos(rad) * circleRadius,
            Mathf.Sin(rad) * circleRadius
        );

        rb.MovePosition(next);
        Velocity = (next - prev) / Time.fixedDeltaTime;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Vector2 origin = Application.isPlaying
            ? (Vector2)transform.position
            : (Vector2)transform.position;

        switch (moveType)
        {
            case PlatformMoveType.Horizontal:
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(origin - Vector2.right * (moveDistance * 0.5f),
                                origin + Vector2.right * (moveDistance * 0.5f));
                break;

            case PlatformMoveType.Vertical:
                Gizmos.color = Color.green;
                Gizmos.DrawLine(origin - Vector2.up * (moveDistance * 0.5f),
                                origin + Vector2.up * (moveDistance * 0.5f));
                break;

            case PlatformMoveType.Diagonal:
                Gizmos.color = Color.yellow;
                float rad = diagonalAngleDegrees * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;
                Gizmos.DrawLine(origin - dir * (moveDistance * 0.5f),
                                origin + dir * (moveDistance * 0.5f));
                break;

            case PlatformMoveType.Circle:
                Gizmos.color = Color.magenta;
                UnityEditor.Handles.color = Color.magenta;
                UnityEditor.Handles.DrawWireDisc(origin, Vector3.forward, circleRadius);
                break;
        }
    }
#endif
}