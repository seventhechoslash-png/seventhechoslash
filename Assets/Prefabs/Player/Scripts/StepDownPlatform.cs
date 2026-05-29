using System.Collections;
using UnityEngine;

public class StepDownPlatform : MonoBehaviour
{
    [Header("Drop Settings")]
    [SerializeField] private float dropDistance = 0.35f;
    [SerializeField] private float dropTime = 0.12f;
    [SerializeField] private float topNormalMinY = 0.65f; // only trigger when stepped on from above

    private Vector3 startPos;
    private bool triggered;

    private void Awake()
    {
        startPos = transform.position;
    }

    private void OnCollisionEnter2D(Collision2D col)
    {
        if (triggered) return;
        if (!col.collider.CompareTag("Player")) return;

        // Only when player lands on top
        bool fromTop = false;
        for (int i = 0; i < col.contactCount; i++)
        {
            if (col.GetContact(i).normal.y >= topNormalMinY)
            {
                fromTop = true;
                break;
            }
        }

        if (!fromTop) return;

        triggered = true;
        StartCoroutine(Drop());
    }

    private IEnumerator Drop()
    {
        Vector3 endPos = startPos + Vector3.down * dropDistance;

        float t = 0f;
        while (t < dropTime)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / dropTime);
            transform.position = Vector3.Lerp(startPos, endPos, a);
            yield return null;
        }

        transform.position = endPos;
    }
}