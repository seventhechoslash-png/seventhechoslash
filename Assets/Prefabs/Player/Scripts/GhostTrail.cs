using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GhostTrail : MonoBehaviour
{
    [Header("Trail Settings")]
    public float ghostInterval = 0.04f;
    public float ghostLifetime = 0.35f;
    public int maxGhosts = 8;
    public Color ghostTint = new Color(0.05f, 0.05f, 0.05f, 0.55f);
    public Material ghostMaterial;

    [Header("Scale")]
    public float scaleMultiplier = 1.01f;

    private SpriteRenderer characterSR;
    private bool isRunning = false;
    private Queue<GameObject> ghostPool = new Queue<GameObject>();

    void Start()
    {
        characterSR = GetComponent<SpriteRenderer>();
        PrewarmPool();
    }

    void PrewarmPool()
    {
        for (int i = 0; i < maxGhosts; i++)
        {
            GameObject g = CreateGhostObject();
            g.SetActive(false);
            ghostPool.Enqueue(g);
        }
    }

    GameObject CreateGhostObject()
    {
        GameObject ghost = new GameObject("GhostTrail_Ghost");
        SpriteRenderer sr = ghost.AddComponent<SpriteRenderer>();
        sr.material = ghostMaterial;
        sr.sortingLayerName = characterSR.sortingLayerName;
        sr.sortingOrder = characterSR.sortingOrder - 1;
        return ghost;
    }

    public void StartTrail()
    {
        if (!isRunning)
        {
            isRunning = true;
            StartCoroutine(SpawnGhosts());
        }
    }

    public void StopTrail()
    {
        isRunning = false;
    }

    IEnumerator SpawnGhosts()
    {
        while (isRunning)
        {
            SpawnGhost();
            yield return new WaitForSeconds(ghostInterval);
        }
    }

    void SpawnGhost()
    {
        GameObject ghost;

        if (ghostPool.Count > 0)
            ghost = ghostPool.Dequeue();
        else
            ghost = CreateGhostObject();

        ghost.transform.position = transform.position;
        ghost.transform.rotation = transform.rotation;
        ghost.transform.localScale = transform.localScale * scaleMultiplier;
        ghost.SetActive(true);

        SpriteRenderer ghostSR = ghost.GetComponent<SpriteRenderer>();
        ghostSR.sprite = characterSR.sprite;
        ghostSR.flipX = characterSR.flipX;
        ghostSR.color = ghostTint;

        StartCoroutine(FadeAndReturn(ghost, ghostSR));
    }

    IEnumerator FadeAndReturn(GameObject ghost, SpriteRenderer ghostSR)
    {
        float elapsed = 0f;
        Color startColor = ghostTint;

        while (elapsed < ghostLifetime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / ghostLifetime;
            float alpha = Mathf.Lerp(startColor.a, 0f, t);
            ghostSR.color = new Color(startColor.r, startColor.g, startColor.b, alpha);
            ghost.transform.position += Vector3.left * (0.5f * Time.deltaTime);
            yield return null;
        }

        ghost.SetActive(false);
        ghostPool.Enqueue(ghost);
    }
}