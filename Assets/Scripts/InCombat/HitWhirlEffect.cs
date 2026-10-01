using System.Collections;
using UnityEngine;

/// <summary>
/// Scaffold for a "whirl" hit reaction: spins a visual root about Y and/or spawns a swirl VFX.
/// Optional - UnitInstance calls Play() from PlayHitReaction only if this component is present.
/// Assign visualRoot to a child of the unit (never the movement root) so it doesn't fight
/// movement or the Animator's Hit_A/Hit_B clips.
/// </summary>
public class HitWhirlEffect : MonoBehaviour
{
    [Tooltip("Child transform to spin. Leave empty to skip the spin and only spawn the VFX.")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private float duration = 0.45f;
    [Tooltip("Full turns for a light hit; heavy hits add extra turns.")]
    [SerializeField] private float revolutions = 1f;
    [SerializeField] private float heavyHitExtraRevolutions = 1f;
    [SerializeField] private float heavyHitDamagePercent = 0.35f;
    [SerializeField] private AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Optional swirl particle prefab spawned at the unit (auto-destroyed).")]
    [SerializeField] private GameObject whirlVfxPrefab;
    [SerializeField] private float vfxLifetime = 1.5f;

    private Coroutine routine;
    private Quaternion baseLocalRotation;
    private bool hasBase;

    /// <param name="damagePercent">Damage taken as a fraction of max health (0-1).</param>
    public void Play(float damagePercent)
    {
        if (!isActiveAndEnabled) return;

        if (whirlVfxPrefab != null)
            Destroy(Instantiate(whirlVfxPrefab, transform.position, Quaternion.identity), vfxLifetime);

        if (visualRoot == null) return;

        if (routine != null)
        {
            StopCoroutine(routine);
            visualRoot.localRotation = baseLocalRotation;
        }
        routine = StartCoroutine(SpinRoutine(damagePercent));
    }

    private IEnumerator SpinRoutine(float damagePercent)
    {
        if (!hasBase)
        {
            baseLocalRotation = visualRoot.localRotation;
            hasBase = true;
        }

        float turns = revolutions + (damagePercent >= heavyHitDamagePercent ? heavyHitExtraRevolutions : 0f);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = curve.Evaluate(Mathf.Clamp01(elapsed / duration));
            visualRoot.localRotation = baseLocalRotation * Quaternion.Euler(0f, 360f * turns * t, 0f);
            yield return null;
        }

        visualRoot.localRotation = baseLocalRotation;
        routine = null;
    }

    private void OnDisable()
    {
        if (routine != null && visualRoot != null)
            visualRoot.localRotation = baseLocalRotation;
        routine = null;
    }
}
