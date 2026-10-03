using UnityEngine;

/// <summary>
/// Hovl "Magic effects pack" prefabs played on combat events. Loaded from
/// Resources/CombatVfxConfig by UnitInstance; any slot may be left empty.
/// </summary>
[CreateAssetMenu(fileName = "CombatVfxConfig", menuName = "Combat/VFX Config")]
public class CombatVfxConfig : ScriptableObject
{
    [Header("Prefabs")]
    public GameObject hit;
    [Tooltip("Played instead of Hit when the blow is at/above Heavy Hit Fraction of max health.")]
    public GameObject heavyHit;
    public GameObject wound;
    public GameObject death;
    public GameObject fortify;
    public GameObject levelUp;
    [Tooltip("After-action screen: played at each exfil pad as the squad beams out.")]
    public GameObject teleport;

    [Header("Tuning")]
    [Range(0f, 1f)] public float heavyHitFraction = 0.35f;
    public float hitHeight = 1f;
    public float lifetime = 2.5f;
    public float scale = 1f;

    private static CombatVfxConfig cached;
    private static bool loaded;

    public static CombatVfxConfig Load()
    {
        if (!loaded)
        {
            cached = Resources.Load<CombatVfxConfig>("CombatVfxConfig");
            loaded = true;
        }
        return cached;
    }

    /// <param name="parent">Optional: follow this transform (auras, shields).</param>
    public void Spawn(GameObject prefab, Transform at, bool follow)
    {
        if (prefab == null || at == null) return;

        Vector3 pos = at.position + Vector3.up * hitHeight;
        GameObject go = Instantiate(prefab, pos, Quaternion.identity, follow ? at : null);
        go.transform.localScale *= scale;
        if (!follow) PlayOnce(go);
        Destroy(go, lifetime);
    }

    /// Hovl prefabs loop by default, re-firing their burst every 'duration' until destroyed.
    /// One-shot effects (hits, deaths) must fire once.
    private static void PlayOnce(GameObject go)
    {
        foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            ps.Play(false);
        }
    }

    /// <summary>Spawns at a fixed world point, independent of any unit (used on death).</summary>
    public void SpawnAt(GameObject prefab, Vector3 worldPos, bool raise = true)
    {
        if (prefab == null) return;
        GameObject go = Instantiate(prefab, worldPos + (raise ? Vector3.up * hitHeight : Vector3.zero), Quaternion.identity);
        go.transform.localScale *= scale;
        PlayOnce(go);
        Destroy(go, lifetime);
    }
}
