using UnityEngine;

public class ParallaxLayer : MonoBehaviour
{
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private float parallaxMultiplier = 0.5f;

    [Tooltip("Smoothing to prevent jitter at camera boundaries. Higher = smoother but laggier. 0 = no smoothing.")]
    [Range(0f, 0.95f)]
    [SerializeField] private float smoothing = 0.85f;

    private float startX;
    private float cameraStartX;
    private float currentX;

    private void Start()
    {
        startX = transform.position.x;
        cameraStartX = cameraTransform.position.x;
        currentX = startX;
    }

    private void LateUpdate()
    {
        float cameraDeltaX = cameraTransform.position.x - cameraStartX;
        float targetX = startX + cameraDeltaX * parallaxMultiplier;

        // Smooth out micro-jitter from camera confiner clamping
        currentX = Mathf.Lerp(targetX, currentX, smoothing);

        Vector3 pos = transform.position;
        pos.x = currentX;
        transform.position = pos;
    }
}