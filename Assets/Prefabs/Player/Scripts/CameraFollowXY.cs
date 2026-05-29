using UnityEngine;

public class CameraFollowXY : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform player;

    [Header("X Follow")]
    [SerializeField] private float xSmoothTime = 0.15f;

    [Header("Y Follow")]
    [SerializeField] private float ySmoothTime = 0.3f;

    [Header("Y Thresholds")]
    [SerializeField] private float upwardThreshold = 0.8f;
    [SerializeField] private float downwardThreshold = 1.2f;

    private float xVelocity;
    private float yVelocity;

    private float cameraAnchorY;
    private float highestPlayerY;

    // NEW: temporary camera offset
    private float extraYOffset = 0f;

    private void Awake()
    {
        if (player == null)
            player = GameObject.FindGameObjectWithTag("Player")?.transform;
        if (player == null) return;

        if (player.GetComponent<Camera>() != null)
        {
            player = null;
            return;
        }

        xVelocity = 0f;
        yVelocity = 0f;

        cameraAnchorY = player.position.y;
        highestPlayerY = player.position.y;

        transform.position = new Vector3(player.position.x, player.position.y, -10f);
    }

    private void LateUpdate()
    {
        if (player == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null)
            {
                player = go.transform;

                xVelocity = 0f;
                yVelocity = 0f;

                cameraAnchorY = player.position.y;
                highestPlayerY = player.position.y;

                transform.position = new Vector3(player.position.x, cameraAnchorY, -10f);
            }
        }

        if (player == null) return;

        Vector3 pos = transform.position;

        pos.x = Mathf.SmoothDamp(pos.x, player.position.x, ref xVelocity, xSmoothTime);

        if (player.position.y > highestPlayerY)
            highestPlayerY = player.position.y;

        if (highestPlayerY > cameraAnchorY + upwardThreshold)
            cameraAnchorY = highestPlayerY - upwardThreshold;

        float playerBelowCamera = cameraAnchorY - player.position.y;

        if (playerBelowCamera > downwardThreshold)
        {
            cameraAnchorY = player.position.y + downwardThreshold;
            highestPlayerY = player.position.y;
        }

        pos.y = Mathf.SmoothDamp(pos.y, cameraAnchorY + extraYOffset, ref yVelocity, ySmoothTime);
        pos.z = -10f;

        transform.position = pos;
    }

    // ===== CAMERA CONTROL FUNCTIONS =====

    public void SetCameraOffset(float offset)
    {
        extraYOffset = offset;
    }

    public void ResetCameraOffset()
    {
        extraYOffset = 0f;
    }
}