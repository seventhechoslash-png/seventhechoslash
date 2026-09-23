using UnityEngine;
using System;

/// <summary>
/// Singleton that tracks Echo Shards and Vestiges.
/// Persists across scenes. Saves to PlayerPrefs automatically.
/// Subscribe to OnEchoShardsChanged / OnVestigesChanged for HUD updates.
/// </summary>
public class CurrencyManager : MonoBehaviour
{
    // ── Singleton ──────────────────────────────────────────────────────
    public static CurrencyManager Instance { get; private set; }

    // ── Events (HUD subscribes to these) ──────────────────────────────
    public static event Action<int> OnEchoShardsChanged;
    public static event Action<int> OnVestigesChanged;

    // ── PlayerPrefs keys ──────────────────────────────────────────────
    private const string KEY_SHARDS   = "EchoShards";
    private const string KEY_VESTIGES = "Vestiges";

    // ── State ─────────────────────────────────────────────────────────
    private int echoShards;
    private int vestiges;

    public int EchoShards  => echoShards;
    public int Vestiges    => vestiges;

    // ──────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
    }

    // ── Public API ────────────────────────────────────────────────────

    public void AddShards(int amount)
    {
        if (amount <= 0) return;
        echoShards += amount;
        Save();
        OnEchoShardsChanged?.Invoke(echoShards);
    }

    public void AddVestiges(int amount)
    {
        if (amount <= 0) return;
        vestiges += amount;
        Save();
        OnVestigesChanged?.Invoke(vestiges);
    }

    /// <summary>Returns true if the purchase succeeded.</summary>
    public bool SpendShards(int amount)
    {
        if (echoShards < amount) return false;
        echoShards -= amount;
        Save();
        OnEchoShardsChanged?.Invoke(echoShards);
        return true;
    }

    /// <summary>Returns true if the purchase succeeded.</summary>
    public bool SpendVestiges(int amount)
    {
        if (vestiges < amount) return false;
        vestiges -= amount;
        Save();
        OnVestigesChanged?.Invoke(vestiges);
        return true;
    }

    /// <summary>Wipe all currency — call on new game.</summary>
    public void ResetAll()
    {
        echoShards = 0;
        vestiges   = 0;
        Save();
        OnEchoShardsChanged?.Invoke(echoShards);
        OnVestigesChanged?.Invoke(vestiges);
    }

    // ── Persistence ───────────────────────────────────────────────────

    void Save()
    {
        PlayerPrefs.SetInt(KEY_SHARDS,   echoShards);
        PlayerPrefs.SetInt(KEY_VESTIGES, vestiges);
        PlayerPrefs.Save();
    }

    void Load()
    {
        echoShards = PlayerPrefs.GetInt(KEY_SHARDS,   0);
        vestiges   = PlayerPrefs.GetInt(KEY_VESTIGES, 0);
    }
}