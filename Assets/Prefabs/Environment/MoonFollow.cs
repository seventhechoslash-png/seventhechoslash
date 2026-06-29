using UnityEngine;

/// <summary>
/// Keeps the moon always visible by following the camera,
/// with a very slight parallax drift so it feels distant but not glued.
/// Attach to the Moon GameObject.
/// </summary>
public class MoonFollow : MonoBehaviour
{
    [Header("Camera")]
    public Camera targetCamera;

    [Header("Screen Position")]
    [Tooltip("Where the moon sits on screen. (0,0)=center, positive x=right, positive y=up. In viewport-ish offset units.")]
    public Vector2 screenOffset = new Vector2(6f, 4f);

    [Header("Parallax Drift")]
    [Tooltip("How much the moon drifts as the camera moves. 0 = perfectly glued, 0.05 = tiny drift.")]
    [Range(0f, 0.3f)]
    public float driftFactor = 0.04f;

    [Header("Depth")]
    [Tooltip("Z distance — keep it positive so the moon stays behind everything.")]
    public float zDepth = 10f;

    private Vector3 startCamPos;

    void Start()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera != null) startCamPos = targetCamera.transform.position;
    }

    void LateUpdate()
    {
        if (targetCamera == null) return;

        Vector3 camPos = targetCamera.transform.position;

        // Base position = camera + screen offset
        // Drift = small fraction of how far camera has moved from start
        Vector3 drift = (camPos - startCamPos) * driftFactor;

        Vector3 target = new Vector3(
            camPos.x + screenOffset.x - drift.x,
            camPos.y + screenOffset.y - drift.y,
            zDepth
        );

        transform.position = target;
    }
}