using System.Collections;
using UnityEngine;

/// <summary>
/// Attach to Player root.
/// Spawns pure black silhouette afterimage ghosts during dash.
/// No particle system needed — reads SpriteRenderer directly.
/// </summary>
public class DashAfterimage : MonoBehaviour
{
    [Header("Afterimage Settings")]
    [Tooltip("How often a ghost is spawned during dash (seconds). Lower = more ghosts.")]
    public float spawnInterval = 0.04f;

    [Tooltip("How long each ghost takes to fully fade out.")]
    public float fadeDuration = 0.18f;

    [Tooltip("Starting alpha of each ghost. 1 = fully opaque black.")]
    [Range(0.3f, 1f)]
    public float startAlpha = 0.75f;

    [Tooltip("Sorting order for ghosts — should be BEHIND player.")]
    public int sortingOrder = -1;

    // ── Private ──────────────────────────────────────────────────────────────

    private PlayerMovement playerMovement;
    private SpriteRenderer playerSprite;
    private bool wasBlocking = false;

    // Pool of reusable ghost objects to avoid GC spikes
    private const int PoolSize = 12;
    private GameObject[] pool;
    private SpriteRenderer[] poolRenderers;
    private int poolIndex = 0;

    private bool isSpawning = false;
    private Coroutine spawnCoroutine;

    void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();

        // Find the player's main SpriteRenderer (inside Graphics child)
        Transform graphics = transform.Find("Graphics");
        if (graphics != null)
            playerSprite = graphics.GetComponent<SpriteRenderer>();
        if (playerSprite == null)
            playerSprite = GetComponentInChildren<SpriteRenderer>();

        BuildPool();
    }

    void BuildPool()
    {
        pool = new GameObject[PoolSize];
        poolRenderers = new SpriteRenderer[PoolSize];

        for (int i = 0; i < PoolSize; i++)
        {
            var go = new GameObject("DashGhost_" + i);
            go.transform.SetParent(transform.parent); // sibling of player, not child
            go.SetActive(false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.color = new Color(0f, 0f, 0f, 0f);
            sr.sortingLayerName = playerSprite != null ? playerSprite.sortingLayerName : "Default";
            sr.sortingOrder = sortingOrder;

            pool[i] = go;
            poolRenderers[i] = sr;
        }
    }

    void Update()
    {
        if (playerMovement == null) return;

        bool isDashing = playerMovement.IsDashing;

        if (isDashing && !isSpawning)
        {
            isSpawning = true;
            spawnCoroutine = StartCoroutine(SpawnLoop());
        }
        else if (!isDashing && isSpawning)
        {
            isSpawning = false;
            if (spawnCoroutine != null)
                StopCoroutine(spawnCoroutine);
        }
    }

    IEnumerator SpawnLoop()
    {
        while (isSpawning)
        {
            SpawnGhost();
            yield return new WaitForSeconds(spawnInterval);
        }
    }

    void SpawnGhost()
    {
        if (playerSprite == null || playerSprite.sprite == null) return;

        // Grab next pool slot
        GameObject ghost = pool[poolIndex];
        SpriteRenderer sr = poolRenderers[poolIndex];
        poolIndex = (poolIndex + 1) % PoolSize;

        // Stop any existing fade on this ghost
        StopCoroutine("FadeGhost"); // won't error if not running

        // Match player's exact position, scale, and sprite
        ghost.transform.position = playerSprite.transform.position;
        ghost.transform.rotation = playerSprite.transform.rotation;
        ghost.transform.localScale = playerSprite.transform.lossyScale;

        sr.sprite = playerSprite.sprite;
        sr.flipX = playerSprite.flipX;
        sr.sortingLayerName = playerSprite.sortingLayerName;
        sr.sortingOrder = playerSprite.sortingOrder - 1;
        sr.color = new Color(0f, 0f, 0f, startAlpha);

        ghost.SetActive(true);

        StartCoroutine(FadeGhost(sr, ghost));
    }

    IEnumerator FadeGhost(SpriteRenderer sr, GameObject ghost)
    {
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            if (sr == null) yield break;

            float t = elapsed / fadeDuration;
            // Ease out — fades quickly at first then slows
            float alpha = Mathf.Lerp(startAlpha, 0f, Mathf.Pow(t, 0.6f));
            sr.color = new Color(0f, 0f, 0f, alpha);

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (sr != null) sr.color = new Color(0f, 0f, 0f, 0f);
        if (ghost != null) ghost.SetActive(false);
    }
}
