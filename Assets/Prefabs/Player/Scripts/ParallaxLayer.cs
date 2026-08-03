using UnityEngine;

/// <summary>
/// Moves a background layer a fraction of the camera's horizontal travel.
///
/// The DefaultExecutionOrder attribute is the important part: a large positive
/// value guarantees this LateUpdate runs AFTER CinemachineBrain has already
/// moved the camera this frame. Without it, the layer reads a stale camera
/// position and desyncs from the foreground by one frame.
/// </summary>
[DefaultExecutionOrder(10000)]
public class ParallaxLayer : MonoBehaviour
{
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private float parallaxMultiplier = 0.5f;

    [Tooltip("0 = locked to the camera with no lag (recommended). Only raise this if you genuinely need lag. Frame-rate independent.")]
    [Range(0f, 0.95f)]
    [SerializeField] private float smoothing = 0f;

    [Tooltip("Snap the layer to whole pixels to kill sub-pixel shimmer. Set to the sprite's Pixels Per Unit, or 0 to disable.")]
    [SerializeField] private float pixelsPerUnit = 0f;

    private float startX;
    private float cameraStartX;
    private float currentX;

    private void Start()
    {
        if (cameraTransform == null)
        {
            Debug.LogError($"[ParallaxLayer] {name} has no cameraTransform assigned.", this);
            enabled = false;
            return;
        }

        startX       = transform.position.x;
        cameraStartX = cameraTransform.position.x;
        currentX     = startX;
    }

    private void LateUpdate()
    {
        float cameraDeltaX = cameraTransform.position.x - cameraStartX;
        float targetX      = startX + cameraDeltaX * parallaxMultiplier;

        if (smoothing > 0f)
        {
            // Frame-rate independent exponential smoothing.
            // The original Lerp(target, current, smoothing) moved a fixed
            // percentage per FRAME, so the lag changed with framerate.
            float t  = 1f - Mathf.Pow(smoothing, Time.deltaTime * 60f);
            currentX = Mathf.Lerp(currentX, targetX, t);
        }
        else
        {
            currentX = targetX;
        }

        float finalX = currentX;

        if (pixelsPerUnit > 0f)
            finalX = Mathf.Round(finalX * pixelsPerUnit) / pixelsPerUnit;

        Vector3 pos = transform.position;
        pos.x = finalX;
        transform.position = pos;
    }
}