using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally generates a hex map. Elevation is sampled with Perlin noise and
/// mapped to a terrain type via ordered bands (low = water, high = mountain, etc).
/// Spawns HexTile prefabs, applies the matching visual from the TileDatabase, and
/// wires up every tile's neighbor list so pathfinding works immediately after.
/// </summary>
public class MapGenerator : MonoBehaviour
{
    [Serializable]
    public struct TerrainBand
    {
        [Tooltip("Terrain used when noise value is <= this threshold (0-1).")]
        [Range(0f, 1f)] public float maxHeight;
        public TerrainType terrain;
    }

    [Header("Setup")]
    public GameObject[] tileGameObjectDatabase;
    public float hexSize = 1f;

    [Header("Map Size")]
    public int width = 20;
    public int height = 20;

    [Header("Noise")]
    public int seed = 0;
    public float noiseScale = 0.15f;

    [Tooltip("Bands ordered from lowest terrain (e.g. Water) to highest (e.g. StoneMountain). " +
             "Each band's maxHeight must be greater than the previous one's, ending at 1.")]
    public List<TerrainBand> terrainBands = new List<TerrainBand>
    {
        new TerrainBand { maxHeight = 0.05f, terrain = TerrainType.Buildings },
        new TerrainBand { maxHeight = 0.30f, terrain = TerrainType.Water },
        new TerrainBand { maxHeight = 0.35f, terrain = TerrainType.Sand },
        new TerrainBand { maxHeight = 0.75f, terrain = TerrainType.Grass },
        new TerrainBand { maxHeight = 0.90f, terrain = TerrainType.StoneHill },
        new TerrainBand { maxHeight = 1.00f, terrain = TerrainType.StoneMountain },
    };

    private readonly Dictionary<Vector2Int, HexTile> tileMap = new Dictionary<Vector2Int, HexTile>();

    public IReadOnlyDictionary<Vector2Int, HexTile> Tiles => tileMap;

    [Header("Setup")]

    private readonly Dictionary<TerrainType, List<GameObject>> prefabsByTerrain = new();

    private void Awake()
    {
        BuildTerrainDatabase();
        Generate();
    }

    private void BuildTerrainDatabase()
    {
        prefabsByTerrain.Clear();

        Debug.Log("MapGenerator: ---- Scanning tileGameObjectDatabase ----");
        for (int i = 0; i < tileGameObjectDatabase.Length; i++)
        {
            GameObject prefab = tileGameObjectDatabase[i];
            if (prefab == null)
            {
                Debug.LogError($"MapGenerator: tileGameObjectDatabase[{i}] is null!");
                continue;
            }

            HexTile tile = prefab.GetComponent<HexTile>();
            if (tile == null)
            {
                Debug.LogError($"MapGenerator: {prefab.name} has no HexTile component!");
                continue;
            }

            bool validEnum = Enum.IsDefined(typeof(TerrainType), tile.terrainType);
            Debug.Log($"MapGenerator: [{i}] prefab '{prefab.name}' -> terrainType = {tile.terrainType} " +
                    $"(int {(int)tile.terrainType}, valid = {validEnum})");

            if (!prefabsByTerrain.TryGetValue(tile.terrainType, out var list))
            {
                list = new List<GameObject>();
                prefabsByTerrain[tile.terrainType] = list;
            }
            list.Add(prefab);
        }

        Debug.Log("MapGenerator: ---- Terrain bands ----");
        for (int i = 0; i < terrainBands.Count; i++)
        {
            var band = terrainBands[i];
            bool validEnum = Enum.IsDefined(typeof(TerrainType), band.terrain);
            Debug.Log($"MapGenerator: band[{i}] maxHeight = {band.maxHeight} -> terrain = {band.terrain} " +
                    $"(int {(int)band.terrain}, valid = {validEnum})");
        }

        Debug.Log("MapGenerator: ---- prefabsByTerrain keys ----");
        foreach (var kvp in prefabsByTerrain)
        {
            Debug.Log($"MapGenerator: key {kvp.Key} (int {(int)kvp.Key}) -> {kvp.Value.Count} prefab(s)");
        }

        // Catch missing terrain coverage immediately, before Generate() ever runs.
        foreach (var band in terrainBands)
        {
            if (!prefabsByTerrain.ContainsKey(band.terrain))
            {
                Debug.LogError($"MapGenerator: terrainBands references terrain int {(int)band.terrain} " +
                                $"(defined name: {(Enum.IsDefined(typeof(TerrainType), band.terrain) ? band.terrain.ToString() : "UNDEFINED")}), " +
                                $"but no prefab in tileGameObjectDatabase has that terrainType assigned!");
            }
        }
    }

    public void Generate()
    {
        Debug.Log($"MapGenerator: Generating map {width}x{height} with seed {seed} and noise scale {noiseScale}");
        ClearMap();

        float offsetX = seed * 10.37f;
        float offsetY = seed * 19.53f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float noiseValue = Mathf.PerlinNoise(
                    (x + offsetX) * noiseScale,
                    (y + offsetY) * noiseScale);

                TerrainType terrain = PickTerrain(noiseValue);
                SpawnTile(new Vector2Int(x, y), terrain);
            }
        }

        ConnectAllNeighbors();
    }

    private TerrainType PickTerrain(float noiseValue)
    {
        foreach (var band in terrainBands)
        {
            if (noiseValue <= band.maxHeight)
                return band.terrain;
        }

        // Fallback: last band, or Grass if none configured.
        return terrainBands.Count > 0 ? terrainBands[^1].terrain : TerrainType.Grass;
    }


    private void SpawnTile(Vector2Int gridPosition, TerrainType terrain)
    {
        GameObject prefab = GetPrefabForTerrain(terrain);
        if (prefab == null)
        {
            Debug.LogWarning($"MapGenerator: no prefab configured for {terrain} at {gridPosition}, skipping tile.");
            return;
        }

        Vector3 worldPos = HexCoordinates.OffsetToWorldPosition(gridPosition, hexSize);
        GameObject tileObj = Instantiate(prefab, worldPos, Quaternion.identity, transform);

        HexTile tile = tileObj.GetComponent<HexTile>();
        tile.gridPosition = gridPosition;
        tileObj.name = $"Tile_{gridPosition.x}_{gridPosition.y}";

        tileMap[gridPosition] = tile;
    }

    private GameObject GetPrefabForTerrain(TerrainType terrain)
    {
        if (!prefabsByTerrain.TryGetValue(terrain, out var matchingPrefabs) || matchingPrefabs.Count == 0)
            return null;

        // supports terrain-type variety, e.g. multiple grass tile variants
        return matchingPrefabs[UnityEngine.Random.Range(0, matchingPrefabs.Count)];
    }

    private void ConnectAllNeighbors()
    {
        foreach (var kvp in tileMap)
        {
            HexTile tile = kvp.Value;
            tile.neighbors.Clear();

            foreach (Vector2Int coord in HexCoordinates.GetNeighborCoordinates(kvp.Key))
            {
                if (tileMap.TryGetValue(coord, out HexTile neighborTile))
                    tile.neighbors.Add(neighborTile);
            }
        }
    }

    public HexTile GetTile(Vector2Int gridPosition)
    {
        tileMap.TryGetValue(gridPosition, out HexTile tile);
        return tile;
    }

    public void ClearMap()
    {
        foreach (var kvp in tileMap)
        {
            if (kvp.Value != null)
                Destroy(kvp.Value.gameObject);
        }
        tileMap.Clear();
    }
}