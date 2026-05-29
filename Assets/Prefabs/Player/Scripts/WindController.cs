// ============================================================
//  WindZone.cs  –  Seventh Echo
//  Place on any GameObject with a Box Collider 2D (Is Trigger).
//  Controls when DreamWindVFX fades in or out.
// ============================================================
using UnityEngine;

public class WindController : MonoBehaviour
{
    [Header("Zone Settings")]
    public string playerTag      = "Player";
    public float fadeInDuration  = 0.8f;
    public float fadeOutDuration = 1.2f;

    [Tooltip("Tick for indoor/cave areas — wind fades OUT when player enters")]
    public bool isIndoorZone = false;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;
        if (DreamWindVFX.Instance == null) return;

        if (isIndoorZone)
            DreamWindVFX.Instance.FadeOut(fadeOutDuration);
        else
            DreamWindVFX.Instance.FadeIn(fadeInDuration);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;
        if (DreamWindVFX.Instance == null) return;

        if (isIndoorZone)
            DreamWindVFX.Instance.FadeIn(fadeInDuration);
        else
            DreamWindVFX.Instance.FadeOut(fadeOutDuration);
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        var col = GetComponent<Collider2D>();
        if (col == null) return;
        Gizmos.color = isIndoorZone
            ? new Color(1f, 0.3f, 0.3f, 0.2f)
            : new Color(0.6f, 0.4f, 1f, 0.2f);
        if (col is BoxCollider2D box)
        {
            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
            Gizmos.DrawCube(box.offset, box.size);
            Gizmos.color = isIndoorZone
                ? new Color(1f, 0.3f, 0.3f, 0.9f)
                : new Color(0.6f, 0.4f, 1f, 0.9f);
            Gizmos.DrawWireCube(box.offset, box.size);
            Gizmos.matrix = old;
        }
    }

    void OnDrawGizmosSelected()
    {
        UnityEditor.Handles.color = isIndoorZone
            ? new Color(1f, 0.3f, 0.3f, 1f)
            : new Color(0.6f, 0.4f, 1f, 1f);
        UnityEditor.Handles.Label(
            transform.position + Vector3.up,
            isIndoorZone ? "NO WIND (Indoor)" : "DREAM WIND ZONE");
    }
#endif
}
