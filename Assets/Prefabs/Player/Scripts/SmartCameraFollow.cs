using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// Adjusts Cinemachine camera offset so the player can see platforms below them.
/// Raycasts downward from player feet — if a platform is below and not immediately
/// underfoot, the camera pans down to show it.
/// </summary>
public class SmartCameraFollow : MonoBehaviour
{
    [Header("References")]
    public Transform player;

    [Header("Raycast")]
    public LayerMask groundLayer;
    public float     raycastMaxDistance   = 12f;  // How far below to look
    public float     minHeightThreshold   = 1.5f; // Ignore platforms within this distance (you're on them)

    [Header("Camera Offset")]
    public float maxLookDownOffset  = -4f;   // Max downward camera shift
    public float lerpSpeed          = 3f;    // How fast camera adjusts

    [Header("Debug")]
    public bool showGizmos = true;

    // ── internals ──
    private CinemachineCamera    vcam;
    private CinemachinePositionComposer composer;
    private float targetOffsetY = 0f;

    void Start()
    {
        vcam = FindAnyObjectByType<CinemachineCamera>(); // FindAnyObjectByType — not deprecated
        if (vcam != null)
            composer = vcam.GetComponent<CinemachinePositionComposer>();

        if (player == null)
            player = GameObject.FindWithTag("Player")?.transform;
    }

    void LateUpdate()
    {
        if (player == null || composer == null) return;

        // Raycast straight down from player feet
        Vector2 origin = player.position;
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, raycastMaxDistance, groundLayer);

        if (hit.collider != null && hit.distance > minHeightThreshold)
        {
            // There's a platform below — shift camera down proportionally
            float fraction = Mathf.InverseLerp(minHeightThreshold, raycastMaxDistance, hit.distance);
            targetOffsetY  = Mathf.Lerp(0f, maxLookDownOffset, fraction);
        }
        else
        {
            // On ground or nothing below — reset
            targetOffsetY = 0f;
        }

        // Smoothly apply
        Vector3 current = composer.TargetOffset;
        float   newY    = Mathf.Lerp(current.y, targetOffsetY, lerpSpeed * Time.deltaTime);
        composer.TargetOffset = new Vector3(current.x, newY, current.z);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!showGizmos || player == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(player.position, player.position + Vector3.down * raycastMaxDistance);
    }
#endif
}