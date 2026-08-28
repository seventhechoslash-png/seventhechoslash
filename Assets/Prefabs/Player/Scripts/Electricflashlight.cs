// ============================================================
//  ElectricFlashLight.cs  –  Seventh Echo
//
//  Throws real light into the scene when lightning fires, so the
//  discharge illuminates the characters and background instead of
//  just being drawn on top of them. This is usually the difference
//  between VFX that sits IN a scene and VFX that floats above it.
//
//  Requires URP with the 2D Renderer (Light2D). Your sprites already
//  use Sprite-Lit-Default, so this should be available.
//
//  SETUP: add to the Player ROOT. PlayerGuard drives it.
// ============================================================

using UnityEngine;
using UnityEngine.Rendering.Universal;
using System.Collections;

public class ElectricFlashLight : MonoBehaviour
{
    [Header("Light")]
    [Tooltip("Peak brightness. Keep it punchy - this is a discharge, not a lamp.")]
    public float peakIntensity = 3.5f;
    [Tooltip("How far the light reaches.")]
    public float outerRadius = 6f;
    public float innerRadius = 0.5f;
    [ColorUsage(true, true)]
    public Color lightColor = new Color(0.7f, 0.85f, 1f, 1f);

    [Header("Flicker")]
    [Tooltip("Brightness over the flash. Sharp attack, ragged decay.")]
    public AnimationCurve envelope = new AnimationCurve(
        new Keyframe(0f,    0f),
        new Keyframe(0.05f, 1f),
        new Keyframe(0.18f, 0.35f),
        new Keyframe(0.30f, 0.75f),
        new Keyframe(0.55f, 0.20f),
        new Keyframe(1f,    0f));
    [Tooltip("Random flicker layered on the envelope. 0 = smooth, 0.5 = strobing.")]
    [Range(0f, 1f)] public float flickerAmount = 0.35f;
    [Tooltip("Flicker changes this many times per second.")]
    public float flickerRate = 30f;

    [Header("Sorting")]
    [Tooltip("Which sorting layers this light affects. Empty = Default only.")]
    public string[] targetSortingLayers = { "Default" };

    private Light2D light2D;
    private Transform lightT;
    private Coroutine running;

    void Awake()
    {
        GameObject go = new GameObject("ElectricFlashLight_FX");
        lightT = go.transform;

        light2D = go.AddComponent<Light2D>();
        light2D.lightType    = Light2D.LightType.Point;
        light2D.color        = lightColor;
        light2D.intensity    = 0f;
        light2D.pointLightOuterRadius = outerRadius;
        light2D.pointLightInnerRadius = innerRadius;

        go.SetActive(false);
    }

    void OnDestroy()
    {
        if (lightT != null) Destroy(lightT.gameObject);
    }

    /// <summary>Flash light at a world position for a duration.</summary>
    public void Flash(Vector3 position, float duration)
    {
        if (light2D == null) return;

        lightT.position = position;
        lightT.gameObject.SetActive(true);

        if (running != null) StopCoroutine(running);
        running = StartCoroutine(FlashRoutine(duration));
    }

    private IEnumerator FlashRoutine(float duration)
    {
        float elapsed = 0f;
        float nextFlicker = 0f;
        float flick = 1f;

        light2D.color = lightColor;
        light2D.pointLightOuterRadius = outerRadius;
        light2D.pointLightInnerRadius = innerRadius;

        // Unscaled, so a HitStop freeze does not hold the light at full blast.
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            if (elapsed >= nextFlicker)
            {
                nextFlicker = elapsed + (1f / Mathf.Max(1f, flickerRate));
                flick = 1f - Random.Range(0f, flickerAmount);
            }

            float env = envelope != null && envelope.length > 0
                ? envelope.Evaluate(t)
                : Mathf.Sin(t * Mathf.PI);

            light2D.intensity = Mathf.Max(0f, peakIntensity * env * flick);

            yield return null;
        }

        light2D.intensity = 0f;
        lightT.gameObject.SetActive(false);
        running = null;
    }
}