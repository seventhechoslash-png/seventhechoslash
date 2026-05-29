using UnityEngine;

public class CameraRaiseRock : MonoBehaviour
{
    public float cameraRaiseAmount = 3f;

    private CameraFollowXY cameraScript;

    private void Start()
    {
        cameraScript = Camera.main.GetComponent<CameraFollowXY>();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            cameraScript.SetCameraOffset(cameraRaiseAmount);
        }
    }
}