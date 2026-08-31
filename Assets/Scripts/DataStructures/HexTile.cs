using System.Collections.Generic;
using UnityEngine;

public enum BuildingType
{
    WoodTower,
    StoneTower,
    Mine,
    Farm,
    WoodenWall,
    StoneWall
}
public enum TerrainType
{
    Water,
    Sand,
    Grass,
    StoneHill,
    StoneMountain,
    Buildings,

}

public enum TileHighlightType
{
    None,
    Movement,
    Attack,
    Build
}

public class HexTile : MonoBehaviour
{
    [Header("Identity")]
    public Vector2Int gridPosition;
    public TerrainType terrainType;

    [Header("Adjacency")]
    public List<HexTile> neighbors = new List<HexTile>(6);

    [Header("Occupancy")]
    public UnitInstance occupyingUnit;

    [Header("Movement")]
    public bool isWalkable = true;
    public int movementCost = 1;
    public float heightOffset = 0.187f;

    [Header("Combat Modifiers")]
    public int defenseBonus = 0;
    public int attackBonus = 0;

    [Header("Visuals")]
    public bool isHighlighted;
    public Material movementHighlightMaterial;
    public Material attackHighlightMaterial;
    public Material buildHighlightMaterial;  

    [Header("Fog of War")]
    public GameObject fogInstance;
    public bool isRevealed = false;
    private TileHighlightType currentHighlight = TileHighlightType.None;
    public int foodLeftOnTile = 0;

    [Header("Farming Visuals")]
    [Range(0f, 1f)]
    public float depletedDarkness = 0.5f;
    public bool IsOccupied => occupyingUnit != null;

    
    public bool CanEnter(UnitInstance unit)
    {
        if (!isWalkable) return false;
        if (IsOccupied) return false;

        return true;
    }

    public bool CanEnter()
    {
        if (!isWalkable) return false;
        if (IsOccupied) return false;

        return true;
    }

    public void SetUnit(UnitInstance unit)
    {
        occupyingUnit = unit;
        isWalkable = false;
    }

    public void RemoveUnit()
    {
        defenseBonus = 0;
        attackBonus = 0;
        //occupyingUnit = null;
        isWalkable = true;
    }

    // ---------------------------
    // Fog API
    // ---------------------------

    public void Reveal()
    {
        if (isRevealed) return;

        isRevealed = true;

        if (fogInstance != null)
            fogInstance.SetActive(false);

        if (occupyingUnit != null)
            occupyingUnit.OnTileRevealed();
    }

    public void Hide()
    {
        isRevealed = false;

        if (fogInstance != null)
            fogInstance.SetActive(true);

        if (occupyingUnit != null)
            occupyingUnit.OnTileHidden();
    }


    // ---------------------------
    // Highlighting
    // ---------------------------

    public void Highlight(TileHighlightType type)
    {
        if (!isRevealed) return;
        if (type == TileHighlightType.Movement && !isWalkable)
            return;

        Material highlightMat = type switch
        {
            TileHighlightType.Attack   => attackHighlightMaterial,
            TileHighlightType.Build    => buildHighlightMaterial,
            _                          => movementHighlightMaterial
        };

        if (highlightMat == null) return;

        Renderer renderer = GetComponent<Renderer>();
        if (renderer == null) return;

        List<Material> materials = new List<Material>(renderer.sharedMaterials);
        if (!materials.Contains(highlightMat))
        {
            materials.Add(highlightMat);
            renderer.materials = materials.ToArray();
        }

        currentHighlight = type;
    }

    public void ClearHighlight()
    {
        Renderer renderer = GetComponent<Renderer>();
        if (renderer == null) return;

        List<Material> materials = new List<Material>(renderer.sharedMaterials);

        materials.Remove(movementHighlightMaterial);
        materials.Remove(attackHighlightMaterial);

        renderer.materials = materials.ToArray();
        currentHighlight = TileHighlightType.None;
    }

    public void OnTilePressed()
    {
        Debug.Log($"HexTile at {gridPosition} clicked.");
        //GetComponent<TileBounceAnimation>()?.Play();
    }
}