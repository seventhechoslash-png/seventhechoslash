using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class MovingPlatform : MonoBehaviour
{
    public float moveDistance = 2f;
    public float speed = 1f;

    public Vector2 Velocity { get; private set; }

    private Rigidbody2D rb;
    private Vector2 startPos;
    private Vector2 lastPos;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        // These 3 lines fix the vibration
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.useFullKinematicContacts = true;
    }

    private void Start()
    {
        startPos = rb.position;
        lastPos = rb.position;
        Velocity = Vector2.zero;
    }

    private void FixedUpdate()
    {
        float x = Mathf.Sin(Time.fixedTime * speed) * moveDistance;
        Vector2 target = new Vector2(startPos.x + x, startPos.y);

        Velocity = (target - lastPos) / Time.fixedDeltaTime;
        rb.MovePosition(target);
        lastPos = target;
    }
}