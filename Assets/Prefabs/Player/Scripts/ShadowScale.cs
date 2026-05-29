using UnityEngine;

public class ShadowFollow : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private SpriteRenderer sr;

    [Header("Shadow Settings")]
    [SerializeField] private float rayHeight = 5f;
    [SerializeField] private float maxDistance = 2f;
    [SerializeField] private float shrinkAmount = 0.15f;
    [SerializeField] private float runStretch = 1.15f;
    [SerializeField] private float visibleAlpha = 0.65f;
    [SerializeField] private float fadeSpeed = 10f;

    [Header("Offsets")]
    [SerializeField] private float directionOffset = 0.15f;
    [SerializeField] private float shadowOffsetAmount = 0.15f;

    [Header("Landing Impact")]
    [SerializeField] private float impactScaleBoost = 1.25f;
    [SerializeField] private float impactRecoverSpeed = 8f;

    [Header("Moving Platform Smoothing")]
    [SerializeField] private float positionSmoothSpeed = 25f; // Higher = less lag

    private Vector3 baseScale;
    private float currentAlpha;
    private bool wasGrounded;
    private float impactMultiplier = 1f;

    // Smoothed shadow position to prevent flicker on moving platforms
    private Vector3 smoothedShadowPos;
    private bool shadowInitialized = false;

    void Start()
    {
        baseScale = transform.localScale;
        currentAlpha = 0f;
        if (sr == null) sr = GetComponent<SpriteRenderer>();
    }

    // Use LateUpdate so shadow updates AFTER physics and player movement
    void LateUpdate()
    {
        Vector2 origin = new Vector2(player.position.x, player.position.y + rayHeight);
        RaycastHit2D centerHit = Physics2D.Raycast(origin, Vector2.down, rayHeight * 2f, groundLayer);

        bool isGrounded = false;

        if (centerHit.collider != null)
        {
            float distance = player.position.y - centerHit.point.y;

            if (distance <= maxDistance)
            {
                isGrounded = true;

                if (!wasGrounded && rb.linearVelocity.y <= 0f)
                    impactMultiplier = impactScaleBoost;

                float dir = Mathf.Sign(rb.linearVelocity.x);
                float dynamicOffset = Mathf.Abs(rb.linearVelocity.x) > 0.1f ? dir * directionOffset : 0f;
                float behindOffset = -dir * shadowOffsetAmount;

                // Target position from raycast
                Vector3 targetPos = new Vector3(
                    player.position.x + dynamicOffset + behindOffset,
                    centerHit.point.y + 0.02f,
                    0f
                );

                // Smooth the position to eliminate flicker on moving platforms
                if (!shadowInitialized)
                {
                    smoothedShadowPos = targetPos;
                    shadowInitialized = true;
                }

                smoothedShadowPos = Vector3.Lerp(
                    smoothedShadowPos,
                    targetPos,
                    Time.deltaTime * positionSmoothSpeed
                );

                transform.position = smoothedShadowPos;
                transform.rotation = Quaternion.identity;

                // Scale
                float t = Mathf.Clamp01(distance / maxDistance);
                float scaleFactor = Mathf.Lerp(1f, 1f - shrinkAmount, t);
                float speed = Mathf.Abs(rb.linearVelocity.x);
                float stretch = Mathf.Lerp(1f, runStretch, speed / 6f);

                float finalScaleX = baseScale.x * scaleFactor * stretch * impactMultiplier;
                float finalScaleY = baseScale.y * scaleFactor * impactMultiplier;
                transform.localScale = new Vector3(finalScaleX, finalScaleY, 1f);

                // Edge support
                float halfWidth = baseScale.x * 0.5f;
                RaycastHit2D leftHit = Physics2D.Raycast(new Vector2(player.position.x - halfWidth, player.position.y + rayHeight), Vector2.down, rayHeight * 2f, groundLayer);
                RaycastHit2D rightHit = Physics2D.Raycast(new Vector2(player.position.x + halfWidth, player.position.y + rayHeight), Vector2.down, rayHeight * 2f, groundLayer);

                float support = 0f;
                if (leftHit.collider != null) support += 0.5f;
                if (rightHit.collider != null) support += 0.5f;

                currentAlpha = Mathf.Lerp(currentAlpha, visibleAlpha * support, Time.deltaTime * fadeSpeed);
                SetAlpha(currentAlpha);
            }
        }

        if (!isGrounded)
        {
            shadowInitialized = false;
            FadeOut();
        }

        impactMultiplier = Mathf.Lerp(impactMultiplier, 1f, Time.deltaTime * impactRecoverSpeed);
        wasGrounded = isGrounded;
    }

    void FadeOut()
    {
        currentAlpha = Mathf.Lerp(currentAlpha, 0f, Time.deltaTime * fadeSpeed);
        SetAlpha(currentAlpha);
    }

    void SetAlpha(float a)
    {
        if (sr == null) return;
        Color c = sr.color;
        c.a = a;
        sr.color = c;
    }
}