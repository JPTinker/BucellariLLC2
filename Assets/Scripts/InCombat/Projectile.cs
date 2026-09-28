using System.Collections;
using UnityEngine;

/// <summary>
/// Purely visual projectile (arrow, magic missile) flown from an attacker to a
/// point on its target. Damage is resolved up front by UnitInstance.Attack; the
/// target's hit reaction is timed to land when this does. Model should face +Z.
/// </summary>
public class Projectile : MonoBehaviour
{
    [SerializeField] private float speed = 12f;
    [Tooltip("Peak height of the flight arc above the straight line. ~1 for arrows, 0 for a straight magic missile.")]
    [SerializeField] private float arcHeight = 0f;

    [Header("Impact")]
    [Tooltip("Optional effect spawned where the projectile lands (sparks, magic burst).")]
    [SerializeField] private GameObject impactEffectPrefab;
    [SerializeField] private float impactEffectLifetime = 2f;
    [Tooltip("Child particle systems are detached on impact and given this long to fade out instead of popping.")]
    [SerializeField] private float trailFadeTime = 0.5f;

    public float GetFlightTime(Vector3 start, Vector3 end)
    {
        return Vector3.Distance(start, end) / Mathf.Max(0.01f, speed);
    }

    public void Launch(Vector3 start, Vector3 end)
    {
        transform.position = start;
        Vector3 initialDirection = GetPoint(start, end, 0.05f) - start;
        if (initialDirection.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(initialDirection);

        StartCoroutine(FlyRoutine(start, end));
    }

    private IEnumerator FlyRoutine(Vector3 start, Vector3 end)
    {
        float duration = GetFlightTime(start, end);
        float elapsed = 0f;
        Vector3 previous = start;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            Vector3 position = GetPoint(start, end, Mathf.Clamp01(elapsed / duration));

            // Point along the direction of travel so arrows nose over on the arc.
            Vector3 direction = position - previous;
            if (direction.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(direction);

            transform.position = position;
            previous = position;
            yield return null;
        }

        Impact(end);
    }

    private Vector3 GetPoint(Vector3 start, Vector3 end, float t)
    {
        Vector3 point = Vector3.Lerp(start, end, t);
        point.y += arcHeight * 4f * t * (1f - t);
        return point;
    }

    private void Impact(Vector3 point)
    {
        if (impactEffectPrefab != null)
            Destroy(Instantiate(impactEffectPrefab, point, transform.rotation), impactEffectLifetime);

        foreach (ParticleSystem particles in GetComponentsInChildren<ParticleSystem>())
        {
            if (particles.gameObject == gameObject) continue;
            particles.transform.SetParent(null, true);
            particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(particles.gameObject, trailFadeTime);
        }

        foreach (TrailRenderer trail in GetComponentsInChildren<TrailRenderer>())
        {
            if (trail.gameObject == gameObject) continue;
            trail.transform.SetParent(null, true);
            trail.emitting = false;
            Destroy(trail.gameObject, trail.time);
        }

        Destroy(gameObject);
    }
}
