using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The single place a unit's colour scheme is applied to a model. Used by both the in-battle
/// UnitInstance and the UI portrait renderer so they can never drift apart.
/// Archetype material for the scheme wins; otherwise the base texture is swapped on per-model material copies.
/// </summary>
public static class UnitColorSchemeApplier
{
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

    /// <param name="schemeSource">The UnitInstance (on the model or its prefab) that holds the scheme textures.</param>
    public static void Apply(GameObject model, UnitData archetype, UnitColorScheme scheme, UnitInstance schemeSource)
    {
        if (model == null || archetype == null) return;

        Material selected = archetype.GetSchemeMaterial(scheme);
        if (selected != null)
        {
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = renderer.sharedMaterials;
                if (mats == null || mats.Length == 0) { renderer.sharedMaterial = selected; continue; }
                for (int i = 0; i < mats.Length; i++) mats[i] = selected;
                renderer.sharedMaterials = mats;
            }
            return;
        }

        Texture2D texture = schemeSource != null ? schemeSource.GetSchemeTexture(scheme) : null;
        if (texture == null) return;

        var copies = new Dictionary<Material, Material>();
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;

            Material[] mats = renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                Material source = mats[i];
                if (source == null) continue;

                int property = source.HasProperty(BaseMapId) ? BaseMapId : source.HasProperty(MainTexId) ? MainTexId : -1;
                if (property < 0) continue;

                if (!copies.TryGetValue(source, out Material copy))
                {
                    copy = new Material(source);
                    copy.SetTexture(property, texture);
                    copies[source] = copy;
                }
                mats[i] = copy;
            }
            renderer.sharedMaterials = mats;
        }
    }
}
