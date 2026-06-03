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
    [SerializeField] private float positionSmoothSpeed = 25f;

    [Header("Pose Shadow")]
    [Tooltip("How squashed the shadow is vertically. 0.12 = very flat.")]
    [SerializeField] private float poseScaleY = 0.13f;
    [Tooltip("The Graphics child transform of the player that holds the SpriteRenderer.")]
    [SerializeField] private Transform playerGraphics;

    private SpriteRenderer playerSprite;
    private Vector3 baseScale;
    private float currentAlpha;
    private bool wasGrounded;
    private float impactMultiplier = 1f;

    private Vector3 smoothedShadowPos;
    private bool shadowInitialized = false;

    void Start()
    {
        baseScale = transform.localScale;
        currentAlpha = 0f;
        if (sr == null) sr = GetComponent<SpriteRenderer>();

        // Find the player's SpriteRenderer for pose matching
        if (playerGraphics != null)
            playerSprite = playerGraphics.GetComponent<SpriteRenderer>();

        if (playerSprite == null && player != null)
        {
            Transform g = player.Find("Graphics");
            if (g != null) playerSprite = g.GetComponent<SpriteRenderer>();
        }

        if (playerSprite == null && player != null)
            playerSprite = player.GetComponentInChildren<SpriteRenderer>();
    }

    void LateUpdate()
    {
        // Mirror player's current sprite pose every frame
        if (playerSprite != null && playerSprite.sprite != null)
        {
	sr.sprite = playerSprite.sprite;
	sr.flipX  = playerSprite.flipX;
	sr.flipX  = playerGraphics != null 
    ? playerGraphics.lossyScale.x < 0f 
    : playerSprite.flipX;
            sr.color  = new Color(0f, 0f, 0f, currentAlpha); // pure black
        }

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
                float behindOffset  = -dir * shadowOffsetAmount;

                Vector3 targetPos = new Vector3(
                    player.position.x + dynamicOffset + behindOffset,
                    centerHit.point.y + 0.02f,
                    0f
                );

                if (!shadowInitialized)
                {
                    smoothedShadowPos  = targetPos;
                    shadowInitialized  = true;
                }

                smoothedShadowPos = Vector3.Lerp(
                    smoothedShadowPos,
                    targetPos,
                    Time.deltaTime * positionSmoothSpeed
                );

                transform.position = smoothedShadowPos;
                transform.rotation = Quaternion.identity;

                float t           = Mathf.Clamp01(distance / maxDistance);
                float scaleFactor = Mathf.Lerp(1f, 1f - shrinkAmount, t);
                float speed       = Mathf.Abs(rb.linearVelocity.x);
                float stretch     = Mathf.Lerp(1f, runStretch, speed / 6f);

                // Use player's actual world scale X, flattened to poseScaleY
                float playerWorldScaleX = playerSprite != null
                    ? playerSprite.transform.lossyScale.x
                    : baseScale.x;

                float finalScaleX = Mathf.Abs(playerWorldScaleX) * scaleFactor * stretch * impactMultiplier;
                float finalScaleY = Mathf.Abs(playerWorldScaleX) * poseScaleY * impactMultiplier;

                // Flip direction matches player facing
		float facingDir = playerGraphics != null && playerGraphics.lossyScale.x < 0f ? -1f : 1f;
		transform.localScale = new Vector3(
    		finalScaleX * facingDir,
    		finalScaleY,
    		1f
		);

                float halfWidth = Mathf.Abs(playerWorldScaleX) * 0.5f;
                RaycastHit2D leftHit  = Physics2D.Raycast(new Vector2(player.position.x - halfWidth, player.position.y + rayHeight), Vector2.down, rayHeight * 2f, groundLayer);
                RaycastHit2D rightHit = Physics2D.Raycast(new Vector2(player.position.x + halfWidth, player.position.y + rayHeight), Vector2.down, rayHeight * 2f, groundLayer);

                float support = 0f;
                if (leftHit.collider != null) support += 0.5f;
                if (rightHit.collider != null) support += 0.5f;

                currentAlpha = Mathf.Lerp(currentAlpha, visibleAlpha * support, Time.deltaTime * fadeSpeed);
            }
        }

        if (!isGrounded)
        {
            shadowInitialized = false;
            FadeOut();
        }

        impactMultiplier = Mathf.Lerp(impactMultiplier, 1f, Time.deltaTime * impactRecoverSpeed);
        wasGrounded      = isGrounded;
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
        c.a     = a;
        sr.color = c;
    }
}
