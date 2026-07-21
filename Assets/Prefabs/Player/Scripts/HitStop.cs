using System.Collections;
using UnityEngine;

/// <summary>
/// Global hitstop / freeze-frame manager.
/// Call HitStop.Instance.Freeze(duration) to briefly pause the game on impact.
/// Uses unscaled time so the freeze itself isn't affected by timeScale.
/// </summary>
public class HitStop : MonoBehaviour
{
    public static HitStop Instance { get; private set; }

    [Tooltip("Default freeze length if none is passed.")]
    public float defaultDuration = 0.1f;

    [Tooltip("How frozen time gets. 0 = full stop, 0.05 = extreme slow-mo.")]
    [Range(0f, 0.2f)]
    public float frozenTimeScale = 0f;

    private Coroutine running;
    private bool isFrozen = false;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>Freeze for the default duration.</summary>
    public void Freeze()
    {
        Freeze(defaultDuration);
    }

    /// <summary>Freeze for a specific duration (seconds, real time).</summary>
    public void Freeze(float duration)
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(DoFreeze(duration));
    }

    private IEnumerator DoFreeze(float duration)
    {
        // Don't stack — if already frozen, just refresh timing
        isFrozen = true;

        float originalFixedDelta = Time.fixedDeltaTime;
        Time.timeScale = frozenTimeScale;
        // keep physics step proportional so it doesn't get jumpy
        Time.fixedDeltaTime = originalFixedDelta * Mathf.Max(frozenTimeScale, 0.0001f);

        // wait in REAL time, unaffected by timeScale
        yield return new WaitForSecondsRealtime(duration);

        Time.timeScale = 1f;
        Time.fixedDeltaTime = originalFixedDelta;
        isFrozen = false;
        running = null;
    }

    void OnDisable()
    {
        // Safety: never leave the game frozen if this object is disabled mid-freeze
        if (isFrozen)
        {
            Time.timeScale = 1f;
            isFrozen = false;
        }
    }
}