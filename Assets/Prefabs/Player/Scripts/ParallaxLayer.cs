using UnityEngine;

public class ParallaxLayer : MonoBehaviour
{
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private float parallaxMultiplier = 0.5f;

    private float startX;
    private float cameraStartX;

    private void Start()
    {
        startX = transform.position.x;
        cameraStartX = cameraTransform.position.x;
    }

    private void LateUpdate()
    {
        float cameraDeltaX = cameraTransform.position.x - cameraStartX;
        float targetX = startX + cameraDeltaX * parallaxMultiplier;

        Vector3 pos = transform.position;
        pos.x = targetX;
        transform.position = pos;
    }
}
