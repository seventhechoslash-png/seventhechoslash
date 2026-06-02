using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BreakableRock : MonoBehaviour
{
    [Header("Break Timing")]
    public float standTime = 1f;

    [Header("Respawn")]
    public float respawnDelay       = 3f;
    public float reassembleDuration = 0.6f;

    [Header("Shake")]
    public float maxShakeAmount = 0.18f;
    public float shakeFrequency = 28f;

    [Header("Pieces")]
    [Range(3, 8)]
    public int   pieceCount        = 5;
    [Tooltip("Piece size as fraction of rock. Keep this small — 0.08 to 0.15 for large rocks")]
    [Range(0.04f, 0.3f)]
    public float pieceSizeFraction = 0.1f;
    public float pieceMinSpeed     = 2.5f;
    public float pieceMaxSpeed     = 6.5f;
    public float pieceGravity      = 6f;
    public float pieceFadeDuration = 0.7f;
    [Range(0f, 1f)]
    public float pieceUpwardBias   = 0.55f;

    [Header("Camera Nudge (optional)")]
    public CameraFollowXY cameraFollow;
    public float cameraShakeOffset   = -0.4f;
    public float cameraShakeDuration = 0.18f;

    [Header("Audio (optional)")]
    public AudioClip breakSound;
    public AudioClip reassembleSound;
    [Range(0f, 1f)]
    public float breakVolume      = 0.8f;
    [Range(0f, 1f)]
    public float reassembleVolume = 0.7f;

    private float          timer     = 0f;
    private bool           breaking  = false;
    private float          lastOnTop = -999f;
    private Vector3        originPos;
    private SpriteRenderer sr;
    private Collider2D[]   cols;
    private float          rockWidth = 3f;
    private PlayerMovement playerMovement;

    private class PieceData
    {
        public GameObject go;
        public Vector3    brokenPos;
        public Vector3    targetPos;
        public Quaternion brokenRot;
        public Vector3    brokenScale;
    }
    private List<PieceData> activePieces = new List<PieceData>();

    void Awake()
    {
        originPos = transform.position;
        cols      = GetComponentsInChildren<Collider2D>();
        sr        = GetComponent<SpriteRenderer>();
        if (sr == null) sr = GetComponentInChildren<SpriteRenderer>();

        // Use sprite bounds for width
        if (sr != null && sr.bounds.size.x > 0.1f)
            rockWidth = sr.bounds.size.x;
        else
            foreach (var c in cols)
                if (c.bounds.size.x > rockWidth)
                    rockWidth = c.bounds.size.x;

        if (cameraFollow == null)
            cameraFollow = Camera.main?.GetComponent<CameraFollowXY>();
    }

    void Start()
    {
        var go = GameObject.FindWithTag("Player");
        if (go != null)
            playerMovement = go.GetComponent<PlayerMovement>();
        else
            Debug.LogError("[BR] NO PLAYER FOUND - check Player tag!");
    }

    void Update()
    {
        if (breaking || playerMovement == null) return;

        bool onTop = IsPlayerOnMe();

        if (onTop)
        {
            lastOnTop  = Time.time;
            timer     += Time.deltaTime;

            float p  = Mathf.Clamp01(timer / standTime);
            float sh = maxShakeAmount * p * p;
            transform.position = originPos + new Vector3(
                Mathf.Sin(Time.time * shakeFrequency) * sh,
                Mathf.Sin(Time.time * shakeFrequency * 0.7f) * sh * 0.4f,
                0f);

            if (timer >= standTime && !breaking)
            {
                breaking = true;
                StartCoroutine(BreakAndRespawn());
            }
        }
        else
        {
            transform.position = originPos;
            if (Time.time - lastOnTop > 0.5f)
                timer = 0f;
        }
    }

    bool IsPlayerOnMe()
    {
        if (!playerMovement.IsGrounded) return false;
        Collider2D ground = playerMovement.GroundCollider;
        if (ground == null) return false;
        foreach (var col in cols)
            if (ground == col) return true;
        return false;
    }

    IEnumerator BreakAndRespawn()
    {
        transform.position = originPos;

        foreach (var col in cols)    col.enabled = false;
        foreach (var s in GetComponentsInChildren<SpriteRenderer>()) s.enabled = false;

        activePieces.Clear();
        SpawnPieces();

        if (breakSound != null)
            AudioSource.PlayClipAtPoint(breakSound, transform.position, breakVolume);
        if (cameraFollow != null)
            StartCoroutine(CameraNudge());

        // Wait for pieces to fly out
        yield return new WaitForSeconds(pieceFadeDuration);

        // Freeze pieces in place with full alpha
        foreach (var pd in activePieces)
        {
            if (pd.go == null) continue;
            pd.brokenPos   = pd.go.transform.position;
            pd.brokenRot   = pd.go.transform.rotation;
            pd.brokenScale = pd.go.transform.localScale;
            var psr = pd.go.GetComponent<SpriteRenderer>();
            if (psr != null) psr.color = new Color(psr.color.r, psr.color.g, psr.color.b, 1f);
        }

        // Wait remaining respawn delay
        float remaining = respawnDelay - pieceFadeDuration;
        if (remaining > 0f) yield return new WaitForSeconds(remaining);

        // Reassemble
        if (reassembleSound != null)
            AudioSource.PlayClipAtPoint(reassembleSound, transform.position, reassembleVolume);

        foreach (var pd in activePieces)
        {
            if (pd.go == null) continue;
            pd.targetPos = originPos + new Vector3(
                Random.Range(-rockWidth * 0.2f, rockWidth * 0.2f),
                Random.Range(-0.1f, 0.3f), 0f);
        }

        float elapsed = 0f;
        while (elapsed < reassembleDuration)
        {
            elapsed += Time.deltaTime;
            float t      = elapsed / reassembleDuration;
            float curved = 1f - Mathf.Pow(1f - t, 3f);

            foreach (var pd in activePieces)
            {
                if (pd.go == null) continue;
                var psr = pd.go.GetComponent<SpriteRenderer>();
                pd.go.transform.position   = Vector3.Lerp(pd.brokenPos, pd.targetPos, curved);
                pd.go.transform.rotation   = Quaternion.Slerp(pd.brokenRot, Quaternion.identity, curved);
                pd.go.transform.localScale = Vector3.Lerp(pd.brokenScale, Vector3.zero, curved);
                if (psr != null)
                {
                    float alpha = t < 0.5f
                        ? Mathf.Lerp(0.4f, 1f, t / 0.5f)
                        : Mathf.Lerp(1f, 0f, (t - 0.5f) / 0.5f);
                    psr.color = new Color(psr.color.r, psr.color.g, psr.color.b, alpha);
                }
            }

            foreach (var s in GetComponentsInChildren<SpriteRenderer>())
            {
                s.enabled = true;
                s.color   = new Color(s.color.r, s.color.g, s.color.b, elapsed / reassembleDuration);
            }

            yield return null;
        }

        // Fully restored
        foreach (var pd in activePieces)
            if (pd.go != null) Destroy(pd.go);
        activePieces.Clear();

        foreach (var s in GetComponentsInChildren<SpriteRenderer>())
        {
            s.enabled = true;
            s.color   = new Color(s.color.r, s.color.g, s.color.b, 1f);
        }

        foreach (var col in cols) col.enabled = true;

        transform.position = originPos;
        timer     = 0f;
        lastOnTop = -999f;
        breaking  = false;
    }

    void SpawnPieces()
    {
        if (sr == null || sr.sprite == null) return;

        // Piece size is a small fraction of rock width — feels like fragments, not copies
        // Also clamp max piece world size so huge rocks don't spawn huge pieces
        float rawSize = rockWidth * pieceSizeFraction;
        float sz      = Mathf.Min(rawSize, 0.8f); // never bigger than 0.8 world units

        for (int i = 0; i < pieceCount; i++)
        {
            float f   = pieceCount > 1 ? (float)i / (pieceCount - 1) : 0.5f;
            float rad = Mathf.Lerp(-100f, 100f, f) * Mathf.Deg2Rad;

            Vector2 dir = new Vector2(
                Mathf.Sin(rad),
                Mathf.Cos(rad) * pieceUpwardBias + (1f - pieceUpwardBias)
            ).normalized;

            // Spawn within the rock footprint so pieces come FROM the rock
            Vector3 spawnPos = originPos + new Vector3(
                Random.Range(-rockWidth * 0.25f, rockWidth * 0.25f),
                Random.Range(0f, rockWidth * 0.15f), 0f);

            var piece = new GameObject("Piece_" + i);
            piece.transform.position   = spawnPos;
            piece.transform.rotation   = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

            // Random size variation but kept small
            float sizeVariance         = Random.Range(0.6f, 1.1f);
            piece.transform.localScale = Vector3.one * sz * sizeVariance;

            var psr              = piece.AddComponent<SpriteRenderer>();
            psr.sprite           = sr.sprite;
            psr.sortingLayerName = sr.sortingLayerName;
            psr.sortingOrder     = sr.sortingOrder + 1;

            var pd = new PieceData { go = piece };
            activePieces.Add(pd);

            float speed = Random.Range(pieceMinSpeed, pieceMaxSpeed);
            StartCoroutine(AnimatePieceOut(pd, dir * speed));
        }
    }

    IEnumerator AnimatePieceOut(PieceData pd, Vector2 vel)
    {
        if (pd.go == null) yield break;

        var   psr     = pd.go.GetComponent<SpriteRenderer>();
        float elapsed = 0f;
        float spin    = Random.Range(-360f, 360f);

        while (elapsed < pieceFadeDuration)
        {
            if (pd.go == null) yield break;
            elapsed += Time.deltaTime;
            vel     += Vector2.down * pieceGravity * Time.deltaTime;
            pd.go.transform.position += (Vector3)(vel * Time.deltaTime);
            pd.go.transform.Rotate(0f, 0f, spin * Time.deltaTime);
            if (psr != null)
                psr.color = new Color(psr.color.r, psr.color.g, psr.color.b, 1f);
            yield return null;
        }
    }

    IEnumerator CameraNudge()
    {
        cameraFollow.SetCameraOffset(cameraShakeOffset);
        yield return new WaitForSeconds(cameraShakeDuration);
        cameraFollow.ResetCameraOffset();
    }
}
