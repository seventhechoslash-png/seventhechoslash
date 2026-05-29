using UnityEngine;

public class StalkerLaser : MonoBehaviour
{
    [Header("Laser Layers")]
    public LineRenderer coreRenderer;
    public LineRenderer glowRenderer;
    public LineRenderer outerRenderer;

    [Header("Core Settings")]
    public float coreWidth = 0.04f;
    public Color coreColor = new Color(1f, 0.9f, 0.9f, 1f);

    [Header("Glow Settings")]
    public float glowWidth = 0.12f;
    public Color glowColor = new Color(1f, 0.1f, 0.1f, 0.9f);

    [Header("Outer Settings")]
    public float outerWidth = 0.35f;
    public Color outerColor = new Color(1f, 0f, 0f, 0.12f);

    [Header("Pulse")]
    public float pulseSpeed = 14f;
    public float pulseAmount = 0.18f;

    [Header("Charge Flicker")]
    public float chargeFlickerSpeed = 28f;

    private bool isCharging = false;
    private bool isFiring = false;
    private float time;

    void Awake()
    {
        SetupRenderer(coreRenderer, coreWidth, coreColor);
        SetupRenderer(glowRenderer, glowWidth, glowColor);
        SetupRenderer(outerRenderer, outerWidth, outerColor);
        SetAll(false);
    }

    void Update()
    {
        time += Time.deltaTime;

        if (isCharging)
        {
            float flicker = Mathf.PerlinNoise(time * chargeFlickerSpeed, 0f);
            bool show = flicker > 0.38f;
            if (coreRenderer) coreRenderer.enabled = show;
            if (glowRenderer) glowRenderer.enabled = show;
            if (outerRenderer) outerRenderer.enabled = false;
        }
        else if (isFiring)
        {
            float pulse = 1f + Mathf.Sin(time * pulseSpeed) * pulseAmount;

            if (coreRenderer)
            {
                coreRenderer.startWidth = coreWidth * pulse;
                coreRenderer.endWidth = coreWidth * pulse;
            }
            if (glowRenderer)
            {
                glowRenderer.startWidth = glowWidth * pulse;
                glowRenderer.endWidth = glowWidth * pulse;
            }
            if (outerRenderer)
            {
                outerRenderer.startWidth = outerWidth * pulse;
                outerRenderer.endWidth = outerWidth * pulse;
            }
        }
    }

    public void SetPositions(Vector3 from, Vector3 to)
    {
        SetRendererPositions(coreRenderer, from, to);
        SetRendererPositions(glowRenderer, from, to);
        SetRendererPositions(outerRenderer, from, to);
    }

    public void StartCharging()
    {
        isCharging = true;
        isFiring = false;
        if (coreRenderer) coreRenderer.enabled = true;
        if (glowRenderer) glowRenderer.enabled = true;
        if (outerRenderer) outerRenderer.enabled = false;
    }

    public void StartFiring()
    {
        isCharging = false;
        isFiring = true;
        SetAll(true);
    }

    public void StopLaser()
    {
        isCharging = false;
        isFiring = false;
        SetAll(false);
    }

    void SetAll(bool state)
    {
        if (coreRenderer) coreRenderer.enabled = state;
        if (glowRenderer) glowRenderer.enabled = state;
        if (outerRenderer) outerRenderer.enabled = state;
    }

    void SetupRenderer(LineRenderer lr, float width, Color color)
    {
        if (lr == null) return;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.startColor = color;
        lr.endColor = color;
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
    }

    void SetRendererPositions(LineRenderer lr, Vector3 from, Vector3 to)
    {
        if (lr == null || !lr.enabled) return;
        lr.SetPosition(0, from);
        lr.SetPosition(1, to);
    }
}