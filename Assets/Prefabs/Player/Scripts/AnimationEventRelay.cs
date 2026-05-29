using UnityEngine;

public class AnimationEventRelay : MonoBehaviour
{
    public KatanaHitbox katanaHitbox;

    public void EnableHitbox() => katanaHitbox?.EnableHitbox();
    public void DisableHitbox() => katanaHitbox?.DisableHitbox();
}