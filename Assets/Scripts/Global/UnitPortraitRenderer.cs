using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders a unit's 3D ModelPrefab into a transparent portrait sprite using a hidden "photo booth"
/// (disabled camera + lights on a spare layer, parked far from the map). Results are cached per look.
/// </summary>
public static class UnitPortraitRenderer
{
    private const int BoothLayer = 31;
    private const int Resolution = 256;
    private static readonly Vector3 BoothOrigin = new Vector3(0f, -1000f, 0f);

    private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
    private static GameObject _booth;
    private static Camera _camera;
    private static RuntimeAnimatorController _menuController;

    // Lives in Assets/Resources so it can be loaded at runtime in builds.
    private static RuntimeAnimatorController MenuController =>
        _menuController != null ? _menuController : (_menuController = Resources.Load<RuntimeAnimatorController>("MenuChar"));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Cache.Clear();
        _booth = null;
        _camera = null;
    }

    /// <summary>Rendered portrait for this unit, or null when it can't be rendered (caller falls back to the hand-drawn icon).</summary>
    public static Sprite GetPortrait(Unit unit)
    {
        if (unit == null || unit.Archetype == null || unit.Archetype.ModelPrefab == null)
            return null;

        string key = $"{IdOf(unit.Archetype)}|{(int)unit.ColorScheme}|" +
                     $"{IdOf(unit.WeaponPrefab)}|{IdOf(unit.EquipmentPrefab)}";

        if (Cache.TryGetValue(key, out Sprite cached) && cached != null)
            return cached;

        Sprite sprite = Render(unit);
        if (sprite != null) Cache[key] = sprite;
        return sprite;
    }

    // Object.GetInstanceID() is obsolete in Unity 6.5; GetEntityId() replaces it (and isn't an int).
    private static string IdOf(Object obj) => obj != null ? obj.GetEntityId().ToString() : "0";

    private static Sprite Render(Unit unit)
    {
        EnsureBooth();
        UnitData archetype = unit.Archetype;

        // Instantiate under an inactive parent so gameplay Awake/Start never run on the booth copy.
        var holder = new GameObject("PortraitHolder");
        holder.SetActive(false);
        holder.transform.SetParent(_booth.transform, false);

        GameObject model = Object.Instantiate(archetype.ModelPrefab, holder.transform, false);
        model.transform.localPosition = Vector3.zero;
        // Models face +Z and the camera sits on the -Z side, so turn them around to face it.
        // (The camera's own 25 degree yaw keeps this a three-quarter view.)
        model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        // Read scheme data and hand anchors off the UnitInstance, then strip all gameplay behaviour.
        UnitInstance unitInstance = model.GetComponent<UnitInstance>();
        GameObject rightHand = unitInstance != null ? unitInstance.rightHand : null;
        GameObject leftHand = unitInstance != null ? unitInstance.leftHand : null;

        // Colour first: the scheme textures live on the UnitInstance, which is destroyed just below.
        UnitColorSchemeApplier.Apply(model, archetype, unit.ColorScheme, unitInstance);

        foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (MonoBehaviour mb in model.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null) Object.DestroyImmediate(mb);

        if (unit.WeaponPrefab != null && rightHand != null)
            Object.Instantiate(unit.WeaponPrefab, rightHand.transform, false);
        if (unit.EquipmentPrefab != null && leftHand != null)
            Object.Instantiate(unit.EquipmentPrefab, leftHand.transform, false);

        SetLayerRecursive(model, BoothLayer);
        foreach (ParticleSystem ps in model.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);
        holder.SetActive(true);

        // Portraits pose with the menu controller instead of the unit's combat animator.
        Animator animator = model.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            if (MenuController != null) animator.runtimeAnimatorController = MenuController;
            animator.Update(0f);
        }

        Sprite result = Capture(model);

        // Materials copied by ApplyScheme belong to the booth copy only.
        Object.Destroy(holder);
        return result;
    }

    private static Sprite Capture(GameObject model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return null;

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);

        // Bust crop: frame the top portion of the model.
        float cropHeight = Mathf.Max(bounds.size.y * 0.55f, 0.01f);
        Vector3 focus = new Vector3(bounds.center.x, bounds.max.y - cropHeight * 0.5f, bounds.center.z);

        Quaternion view = Quaternion.Euler(8f, 25f, 0f); // slight three-quarter turn, looking a bit down
        _camera.orthographic = true;
        _camera.orthographicSize = Mathf.Max(cropHeight, bounds.size.x * 0.6f) * 0.55f;
        _camera.transform.rotation = view;
        _camera.transform.position = focus + view * Vector3.back * 10f;
        _camera.nearClipPlane = 0.1f;
        _camera.farClipPlane = 30f;

        var rt = RenderTexture.GetTemporary(Resolution, Resolution, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 4;
        RenderTexture previous = RenderTexture.active;
        try
        {
            _camera.targetTexture = rt;
            _camera.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false)
            {
                name = "UnitPortrait",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, Resolution, Resolution), new Vector2(0.5f, 0.5f), 100f);
        }
        finally
        {
            _camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    private static void EnsureBooth()
    {
        if (_booth != null && _camera != null) return;

        _booth = new GameObject("UnitPortraitBooth") { hideFlags = HideFlags.HideAndDontSave };
        _booth.transform.position = BoothOrigin;
        Object.DontDestroyOnLoad(_booth);

        var camGo = new GameObject("PortraitCamera");
        camGo.transform.SetParent(_booth.transform, false);
        _camera = camGo.AddComponent<Camera>();
        _camera.enabled = false; // rendered manually
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _camera.cullingMask = 1 << BoothLayer;
        _camera.allowHDR = false;

        CreateLight("KeyLight", new Vector3(40f, -35f, 0f), 1.1f, Color.white);
        CreateLight("FillLight", new Vector3(20f, 140f, 0f), 0.5f, new Color(0.75f, 0.85f, 1f));
    }

    private static void CreateLight(string name, Vector3 euler, float intensity, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_booth.transform, false);
        go.transform.rotation = Quaternion.Euler(euler);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.color = color;
        light.shadows = LightShadows.None;
        light.cullingMask = 1 << BoothLayer;
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;
    }
}
