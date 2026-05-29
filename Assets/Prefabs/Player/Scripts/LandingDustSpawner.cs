// ============================================================
//  LandingDustSpawner.cs  –  Seventh Echo  (Improved version)
//  Drop-in replacement for your existing LandingDustSpawner.
//  Adds impact-strength scaling and a 2-cloud puff split.
// ============================================================
using UnityEngine;

public class LandingDustSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private LayerMask groundLayer;

    [Header("Dust Prefab")]
    [SerializeField] private GameObject dustPrefab;
    [Tooltip("Spawn a mirrored second puff (set to dustPrefab for symmetric effect)")]
    [SerializeField] private GameObject dustPrefabMirror;

    [Header("Detection")]
    [SerializeField] private float rayHeight = 5f;
    [SerializeField] private float maxDistance = 2f;

    [Header("Thresholds")]
    [Tooltip("Minimum downward speed to spawn dust at all")]
    [SerializeField] private float minImpactSpeed = 2f;
    [Tooltip("Speed at which dust is maximum size")]
    [SerializeField] private float maxImpactSpeed = 18f;
    [Tooltip("Scale range for dust based on impact speed")]
    [SerializeField] private Vector2 dustScaleRange = new Vector2(0.6f, 1.8f);

    [Header("Spread")]
    [Tooltip("How far left and right the two puffs are placed")]
    [SerializeField] private float puffSpread = 0.3f;

    private bool _wasGrounded;

    void Update()
    {
        Vector2 origin = new Vector2(player.position.x, player.position.y + rayHeight);
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, rayHeight * 2f, groundLayer);

        bool isGrounded = false;
        if (hit.collider != null)
        {
            float dist = player.position.y - hit.point.y;
            if (dist <= maxDistance) isGrounded = true;
        }

        float downSpeed = -rb.linearVelocity.y; // positive = falling
        if (!_wasGrounded && isGrounded && downSpeed >= minImpactSpeed)
        {
            SpawnDust(hit.point, downSpeed);
        }

        _wasGrounded = isGrounded;
    }

    void SpawnDust(Vector2 hitPoint, float impactSpeed)
    {
        float t = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, impactSpeed);
        float scale = Mathf.Lerp(dustScaleRange.x, dustScaleRange.y, t);

        // Left puff
        Vector3 leftPos  = new Vector3(hitPoint.x - puffSpread, hitPoint.y, 0f);
        Vector3 rightPos = new Vector3(hitPoint.x + puffSpread, hitPoint.y, 0f);

        GameObject leftPuff = Instantiate(dustPrefab, leftPos, Quaternion.identity);
        leftPuff.transform.localScale = Vector3.one * scale;

        // Right puff (mirrored)
        GameObject rightPuff = Instantiate(dustPrefabMirror ?? dustPrefab, rightPos, Quaternion.identity);
        rightPuff.transform.localScale = new Vector3(-scale, scale, scale); // flip X for mirror
    }
}

// ═══════════════════════════════════════════════════════════
//  LANDING DUST PREFAB SETUP
// ═══════════════════════════════════════════════════════════
//
//  Create a new Particle System prefab called "LandingDustPuff"
//  Save to Assets/VFX/Prefabs/
//
//  Main Module:
//    Duration          0.4       Loop ✗
//    Start Lifetime    Min 0.3   Max 0.7
//    Start Speed       Min 1.5   Max 3.5
//    Start Size        Min 0.15  Max 0.4
//    Start Color:      Gradient
//                      Left:  #8B7355 (sandy brown) α 200
//                      Right: #8B7355 α 0
//    Gravity           -0.3  (puff rises slightly then falls)
//    Max Particles     25
//    Stop Action:      Destroy  ← important!
//
//  Emission:
//    Rate over Time    0
//    Burst: Time=0  Count=Min8/Max14  Cycles=1
//
//  Shape:
//    Hemisphere   Radius 0.2
//    Rotate Y     0 (left puff) / 180 (right puff handled by script)
//
//  Velocity over Lifetime:
//    X: Min -1.5  Max -0.5   (drifts left with wind)
//    Y: Min 0.5   Max 1.5
//
//  Color over Lifetime:
//    Brown α200 → lighter brown α80 → transparent
//
//  Size over Lifetime:
//    Curve: starts at 0.4, peaks at 1.0 around 40%, fades to 0.6
//
//  Noise:
//    Enable ✓   Strength 0.6  Frequency 0.8
//
//  Renderer:
//    Billboard
//    Material: Sprites/Default  (or a soft cloud sprite)
//    Sort Order: +5  (above everything except UI)
//
// ═══════════════════════════════════════════════════════════
