using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum BuildingType { WoodTower, StoneTower, Mine, Farm, WoodenWall, StoneWall, LumberMill }
public enum TerrainType { Water, Sand, Grass, StoneHill, StoneMountain, Buildings, Exfil }
public enum TileHighlightType { None, Movement, Attack, Build, Path, Rescue }

public class HexTile : MonoBehaviour
{
    [Header("Identity")] public Vector2Int gridPosition; public TerrainType terrainType;
    [Header("Adjacency")] public List<HexTile> neighbors = new List<HexTile>(6);
    [Header("Occupancy")] public UnitInstance occupyingUnit;
    [Header("Movement")] public bool isWalkable = true; public int movementCost = 1; public float heightOffset = 0.187f;
    [Header("Combat Modifiers")] public int defenseBonus; public int attackBonus;
    [Header("Visuals")] public bool isHighlighted; public Material movementHighlightMaterial; public Material pathPreviewHighlightMaterial; public Material attackHighlightMaterial; public Material buildHighlightMaterial;public Material rescueHighlightMaterial;
    [Header("Fog of War")] public GameObject fogInstance; public bool isRevealed;
    [Range(0.01f, 0.6f)] public float hiddenDarkness = 0.01f;
    public int foodLeftOnTile;
    [Header("Worker Sites")]
    [Tooltip("Set by MapManager from the tile prefab name (grass-forest). Lumber mill sites.")] public bool isForest;
    [Tooltip("Set by MapManager from the tile prefab name (grass-hill). Not farmable.")] public bool isHill;
    [Tooltip("A worker has claimed this tile for a farm / lumber mill.")] public bool workerSiteClaimed;
    /// <summary>Flat, open grass: valid farm ground.</summary>
    public bool IsFarmable => terrainType == TerrainType.Grass && !isForest && !isHill && !workerSiteClaimed && isWalkable;
    /// <summary>Forested grass: valid lumber mill ground.</summary>
    public bool IsLumberable => isForest && !workerSiteClaimed && isWalkable;
    [Header("Click Feedback")]
    [SerializeField, Min(0f)] private float clickBounceHeight = 0.25f;
    [SerializeField, Min(0.01f)] private float clickBounceDuration = 0.3f;
    [Header("Farming Visuals")] [Range(0f, 1f)] public float depletedDarkness = 0.5f;
    public bool IsOccupied => occupyingUnit != null;
    public bool IsExtractionPoint = false;
    public bool CanEnter(UnitInstance unit) => isWalkable && !IsOccupied;
    public bool CanEnter() => isWalkable && !IsOccupied;
    public void SetUnit(UnitInstance unit) { occupyingUnit = unit; isWalkable = false; }
    public void RemoveUnit() { occupyingUnit = null; isWalkable = true; }

    private Vector3 restingLocalPosition;
    private Coroutine clickBounceCoroutine;

    private void Awake()
    {
        restingLocalPosition = transform.localPosition;
    }

    public void Reveal()
    {
        if (isRevealed) return;

        isRevealed = true;
        RemoveTargetMaterial();
        if (fogInstance != null)
        {
            Destroy(fogInstance);
            fogInstance = null;
        }

        if (occupyingUnit != null) occupyingUnit.OnTileRevealed();
    }
    public void RemoveTargetMaterial()
    {
        Renderer renderer = GetComponent<Renderer>();
        if (renderer == null) return;

        // Get current shared/instantiated materials
        Material[] currentMaterials = renderer.sharedMaterials;
        List<Material> updatedMaterials = new List<Material>();
        bool materialFound = false;

        string targetName = "Shader Graphs_Hidden_2";

        foreach (Material mat in currentMaterials)
        {
            // Check if material matches the target name (ignoring "(Instance)" suffixes)
            if (mat != null && mat.name.StartsWith(targetName))
            {
                materialFound = true;
                continue; // Skip adding this material to remove it
            }

            updatedMaterials.Add(mat);
        }

        // Reassign the trimmed material array back to the renderer
        if (materialFound)
        {
            renderer.sharedMaterials = updatedMaterials.ToArray();
            //Debug.Log($"Successfully removed '{targetName}' from {gameObject.name}.");
        }
    }
    public void Hide()
    {
        isRevealed = false;
        SetDarkness(hiddenDarkness);
        if (occupyingUnit != null) occupyingUnit.OnTileHidden();
    }

    public void SetDarkness(float darkness)
    {
        MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
        foreach (Renderer tileRenderer in GetComponentsInChildren<Renderer>(true))
        {
            bool supportsDarkness = false;
            foreach (Material material in tileRenderer.sharedMaterials)
            {
                if (material != null && material.HasProperty("_darkness"))
                {
                    supportsDarkness = true;
                    break;
                }
            }
            if (!supportsDarkness) continue;

            tileRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat("_darkness", darkness);
            tileRenderer.SetPropertyBlock(propertyBlock);
        }
    }

    public void Highlight(TileHighlightType type)
    {
        Material material = type switch
        {
            TileHighlightType.Attack => attackHighlightMaterial,
            TileHighlightType.Build => buildHighlightMaterial,
            TileHighlightType.Path => pathPreviewHighlightMaterial != null ? pathPreviewHighlightMaterial : movementHighlightMaterial,
            TileHighlightType.Rescue => rescueHighlightMaterial,
            _ => movementHighlightMaterial
        };
        if (material == null) return;
        Renderer tileRenderer = GetComponent<Renderer>();
        if (tileRenderer == null) return;
        List<Material> materials = new List<Material>(tileRenderer.sharedMaterials);
        RemoveHighlightMaterials(materials);
        materials.Add(material);
        tileRenderer.materials = materials.ToArray();
        isHighlighted = true;
    }

    public void ClearHighlight()
    {
        Renderer tileRenderer = GetComponent<Renderer>();
        if (tileRenderer == null) return;
        List<Material> materials = new List<Material>(tileRenderer.sharedMaterials);
        RemoveHighlightMaterials(materials);
        tileRenderer.materials = materials.ToArray();
        isHighlighted = false;
    }

    private void RemoveHighlightMaterials(List<Material> materials)
    {
        materials.Remove(movementHighlightMaterial); materials.Remove(pathPreviewHighlightMaterial);
        materials.Remove(attackHighlightMaterial); materials.Remove(buildHighlightMaterial); materials.Remove(rescueHighlightMaterial);
    }

    public void OnTilePressed()
    {
        if (clickBounceCoroutine != null)
        {
            StopCoroutine(clickBounceCoroutine);
            transform.localPosition = restingLocalPosition;
        }

        clickBounceCoroutine = StartCoroutine(PlayClickBounce());
        CombatManager.Instance?.SelectedTile(this);
    } 

    private IEnumerator PlayClickBounce()
    {
        float elapsed = 0f;
        while (elapsed < clickBounceDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / clickBounceDuration);
            float height = Mathf.Sin(progress * Mathf.PI) * clickBounceHeight;
            transform.localPosition = restingLocalPosition + Vector3.up * height;
            yield return null;
        }

        transform.localPosition = restingLocalPosition;
        clickBounceCoroutine = null;
    }
}
