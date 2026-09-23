using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Persistent singleton that safely spawns pickups queued by EnemyDropper.
/// Add this to your CurrencyManager GameObject in the scene.
/// Spawning is deferred to Update() so it never fires from OnDestroy.
/// </summary>
public class DropSpawner : MonoBehaviour
{
    public static DropSpawner Instance { get; private set; }

    private struct DropRequest
    {
        public Vector3     position;
        public GameObject  prefab;
        public int         minCount;
        public int         maxCount;
        public float       spread;
    }

    private readonly Queue<DropRequest> _queue = new Queue<DropRequest>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>Called by EnemyDropper from OnDestroy — only queues data, no Instantiate.</summary>
    public static void QueueDrop(Vector3 pos, GameObject prefab, int min, int max, float spread)
    {
        if (Instance == null || prefab == null) return;
        Instance._queue.Enqueue(new DropRequest
        {
            position = pos,
            prefab   = prefab,
            minCount = min,
            maxCount = max,
            spread   = spread
        });
    }

    void Update()
    {
        while (_queue.Count > 0)
        {
            var req   = _queue.Dequeue();
            int count = Random.Range(req.minCount, req.maxCount + 1);
            Debug.Log($"[DropSpawner] Spawning {count} x {req.prefab.name} at {req.position}");
            for (int i = 0; i < count; i++)
            {
                Vector2 offset   = Random.insideUnitCircle * req.spread;
                Vector3 spawnPos = req.position + new Vector3(offset.x, offset.y + 0.5f, 0f);
                Instantiate(req.prefab, spawnPos, Quaternion.identity);
            }
        }
    }
}