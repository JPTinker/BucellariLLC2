using System.Collections.Generic;
using UnityEngine;

public enum BuildingType { WoodTower, StoneTower, Mine, Farm, WoodenWall, StoneWall }
public enum TerrainType { Water, Sand, Grass, StoneHill, StoneMountain, Buildings }
public enum TileHighlightType { None, Movement, Attack, Build, Path }

public class HexTile : MonoBehaviour
{
    [Header("Identity")] public Vector2Int gridPosition; public TerrainType terrainType;
    [Header("Adjacency")] public List<HexTile> neighbors = new List<HexTile>(6);
    [Header("Occupancy")] public UnitInstance occupyingUnit;
    [Header("Movement")] public bool isWalkable = true; public int movementCost = 1; public float heightOffset = 0.187f;
    [Header("Combat Modifiers")] public int defenseBonus; public int attackBonus;
    [Header("Visuals")] public bool isHighlighted; public Material movementHighlightMaterial; public Material pathPreviewHighlightMaterial; public Material attackHighlightMaterial; public Material buildHighlightMaterial;
    [Header("Fog of War")] public GameObject fogInstance; public bool isRevealed; public int foodLeftOnTile;
    [Header("Farming Visuals")] [Range(0f, 1f)] public float depletedDarkness = 0.5f;
    public bool IsOccupied => occupyingUnit != null;

    public bool CanEnter(UnitInstance unit) => isWalkable && !IsOccupied;
    public bool CanEnter() => isWalkable && !IsOccupied;
    public void SetUnit(UnitInstance unit) { occupyingUnit = unit; isWalkable = false; }
    public void RemoveUnit() { occupyingUnit = null; isWalkable = true; }
    public void Reveal() { if (isRevealed) return; isRevealed = true; if (fogInstance != null) fogInstance.SetActive(false); if (occupyingUnit != null) occupyingUnit.OnTileRevealed(); }
    public void Hide() { isRevealed = false; if (fogInstance != null) fogInstance.SetActive(true); if (occupyingUnit != null) occupyingUnit.OnTileHidden(); }

    public void Highlight(TileHighlightType type)
    {
        Material material = type switch
        {
            TileHighlightType.Attack => attackHighlightMaterial,
            TileHighlightType.Build => buildHighlightMaterial,
            TileHighlightType.Path => pathPreviewHighlightMaterial != null ? pathPreviewHighlightMaterial : movementHighlightMaterial,
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
        materials.Remove(attackHighlightMaterial); materials.Remove(buildHighlightMaterial);
    }

    public void OnTilePressed()
    {
        CombatManager.Instance?.SelectedTile(this);
    } 
}
