using UnityEngine;

public class CameraRestoreRock : MonoBehaviour
{
    private CameraFollowXY cameraScript;

    private void Start()
    {
        cameraScript = Camera.main.GetComponent<CameraFollowXY>();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            cameraScript.ResetCameraOffset();
        }
    }
}