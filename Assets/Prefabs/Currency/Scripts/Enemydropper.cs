using UnityEngine;

/// <summary>
/// Attach to any enemy. Call OnEnemyDeath() from Die() to drop Echo Shards
/// immediately at the moment of death — NOT delayed by the death-effect sequence.
/// </summary>
public class EnemyDropper : MonoBehaviour
{
    [Header("Echo Shard Drop")]
    public GameObject echoShardPrefab;
    [Min(0)] public int   minShards  = 1;
    [Min(0)] public int   maxShards  = 3;
    public float          dropSpread = 0.8f;

    [Header("Vestige Drop (boss / rare)")]
    public GameObject vestigePrefab;
    [Range(0f, 1f)] public float vestigeDropChance = 0f;
    [Min(1)]        public int   vestigeCount      = 1;

    /// <summary>
    /// Call this from Die() the moment the enemy is killed.
    /// Drops shards at the enemy's current position immediately.
    /// </summary>
    public void OnEnemyDeath()
    {
        if (!Application.isPlaying) return;

        Vector3 pos = transform.position;
        Debug.Log($"[EnemyDropper] {gameObject.name} died — dropping at {pos}");

        if (echoShardPrefab != null)
            DropSpawner.QueueDrop(pos, echoShardPrefab, minShards, maxShards, dropSpread);
        else
            Debug.LogWarning($"[EnemyDropper] Echo Shard Prefab is NULL on {gameObject.name}!");

        if (vestigePrefab != null && Random.value <= vestigeDropChance)
            DropSpawner.QueueDrop(pos, vestigePrefab, vestigeCount, vestigeCount, dropSpread * 0.5f);
    }

    // OnDestroy is intentionally NOT used for drops.
    // Reason: EnemyDeathEffect delays Destroy() by ~4 seconds (smolder sequence),
    // so OnDestroy fires far too late — the player has already moved away.
}