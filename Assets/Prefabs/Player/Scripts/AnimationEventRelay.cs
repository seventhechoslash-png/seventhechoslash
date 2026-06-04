using UnityEngine;

public class AnimationEventRelay : MonoBehaviour
{
    [Header("Horizontal Attack (F)")]
    public KatanaHitbox katanaHitbox;

    [Header("Vertical Attack (V)")]
    public KatanaHitbox verticalKatanaHitbox;

    // ── Horizontal F attack ──
    public void EnableHitbox()  => katanaHitbox?.EnableHitbox();
    public void DisableHitbox() => katanaHitbox?.DisableHitbox();

    // ── Vertical V attack ──
    public void EnableVerticalHitbox()  => verticalKatanaHitbox?.EnableHitbox();
    public void DisableVerticalHitbox() => verticalKatanaHitbox?.DisableHitbox();
}
