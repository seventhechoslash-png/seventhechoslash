using UnityEngine;

public class MovingPlatformGroup : MonoBehaviour
{
    public float moveDistance = 2f;
    public float speed = 1f;

    private Vector3[] startPositions;

    void Start()
    {
        startPositions = new Vector3[transform.childCount];

        for (int i = 0; i < transform.childCount; i++)
        {
            startPositions[i] = transform.GetChild(i).position;
        }
    }

    void Update()
    {
        float offset = Mathf.Sin(Time.time * speed) * moveDistance;

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            child.position = new Vector3(
                startPositions[i].x + offset,
                startPositions[i].y,
                child.position.z
            );
        }
    }
}
