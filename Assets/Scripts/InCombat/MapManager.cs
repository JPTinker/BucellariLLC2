using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally generates a hex map. Elevation is sampled with Perlin noise and
/// mapped to a terrain type via ordered bands (low = water, high = mountain, etc).
/// Spawns HexTile prefabs, applies the matching visual from the TileDatabase, and
/// wires up every tile's neighbor list so pathfinding works immediately after.
///
/// This script is responsible for: generating the map, placing the player's team,
/// spawning enemies in nearby clusters (no stacking - each enemy gets its own tile),
/// marking extraction points, and spawning villagers to be rescued. Rescue logic
/// itself (a unit carrying up to 2 rescued villagers) lives on UnitInstance, not here.
/// </summary>
public class MapManager : MonoBehaviour
{
    public static MapManager Instance { get; private set; }
    private static readonly Vector2Int ExfilTilePosition = new Vector2Int(0, 0);
    private const int VillagerEdgeMargin = 2;

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

    public GameObject Cloud;
    [SerializeField] private float fogHeight = 0.25f;

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

    private readonly Dictionary<TerrainType, List<GameObject>> prefabsByTerrain = new();
    private GameStateManager gameStateManager;

    [Header("Spawn Settings")]
    [SerializeField] private GameObject spawnVfxPrefab;
    public List<UnitData> AvailableEnemyArchetypes;

    [Header("Enemy Groups")]
    [Tooltip("How many enemies land together per cluster. Each enemy still gets its own tile - no stacking.")]
    public int enemyGroupSize = 3;
    [Tooltip("Total number of enemies spawned at the start of combat.")]
    public int enemySpawnCount = 20;

    [Header("Villagers / Rescue")]
    [Tooltip("Archetypes used when spawning rescuable villagers. Rescue logic itself lives on UnitInstance.")]
    public List<UnitData> villagerArchetypes;
    public int villagerSpawnCount = 5;

    /// <summary>Fired when a unit successfully reaches an extraction point and leaves the map.</summary>
    public event Action<UnitInstance> UnitExtracted;



    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BuildTerrainDatabase();
        Generate();
        gameStateManager = GameStateManager.Instance;
        placeUnits();
        SpawnEnemyWave(enemySpawnCount);
        SpawnVillagers(villagerSpawnCount);
        InitializeRevealState();
    }

    public UnitInstance SpawnEnemyUnit(HexTile tile, bool isEnemy, UnitData data = null)
    {
        // 1. Guard Clause: Tile Check
        if (tile == null)
        {
            Debug.LogWarning("[MapGenerator] Cannot spawn unit: no tile provided.");
            return null;
        }

        // 2. Guard Clause: Tile Occupancy Check
        if (!tile.CanEnter())
        {
            Debug.LogWarning($"[MapGenerator] Tile at {tile.name} cannot be entered.");
            return null;
        }

        // 3. Guard Clause: Prefab Check
        if (AvailableEnemyArchetypes == null || AvailableEnemyArchetypes.Count == 0)
        {
            Debug.LogError("No UnitData archetypes assigned in GameStateManager!");
            return null;
        }

        UnitData enemyData = data != null ? data : AvailableEnemyArchetypes[UnityEngine.Random.Range(0, AvailableEnemyArchetypes.Count)];
        GameObject targetPrefab = enemyData.ModelPrefab;
        if (targetPrefab == null)
        {
            Debug.LogError("[MapGenerator] Spawn failed: no valid unit prefab specified.");
            return null;
        }

        // 4. Instantiate & Component Verification
        GameObject spawnedObj = Instantiate(targetPrefab, tile.transform.position, Quaternion.identity);
        if (!spawnedObj.TryGetComponent<UnitInstance>(out var instance))
        {
            Debug.LogError($"[MapGenerator] Prefab '{targetPrefab.name}' is missing a UnitInstance component!");
            Destroy(spawnedObj);
            return null;
        }

        // 5. Initialize Unit State & Grid Mapping
        instance.isEnemy = isEnemy;
        instance.Initialize(enemyData);
        instance.PlaceOnTile(tile);

        // 6. Spawn Visual Juice / Particle Effects
        PlaySpawnEffects(tile.transform.position);

        return instance;
    }

    private void PlaySpawnEffects(Vector3 position)
    {
        if (spawnVfxPrefab != null)
        {
            GameObject vfx = Instantiate(spawnVfxPrefab, position, Quaternion.identity);
            Destroy(vfx, 2.0f);
        }
    }

    private void BuildTerrainDatabase()
    {
        prefabsByTerrain.Clear();

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

            if (!prefabsByTerrain.TryGetValue(tile.terrainType, out var list))
            {
                list = new List<GameObject>();
                prefabsByTerrain[tile.terrainType] = list;
            }
            list.Add(prefab);
        }

        foreach (var band in terrainBands)
        {
            if (!prefabsByTerrain.ContainsKey(band.terrain))
            {
                Debug.LogError($"MapGenerator: terrainBands references terrain {band.terrain}, " +
                                "but no prefab in tileGameObjectDatabase has that terrainType assigned!");
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

                Vector2Int gridPosition = new Vector2Int(x, y);
                TerrainType terrain = gridPosition == ExfilTilePosition
                    ? TerrainType.Exfil
                    : PickTerrain(noiseValue);
                SpawnTile(gridPosition, terrain);
            }
        }

        ConnectAllNeighbors();
        MarkExtractionPoints();
    }

    // ---------------------------------------------------------------------
    // Enemy clusters: groups land near each other, but never share a tile.
    // ---------------------------------------------------------------------
    public List<UnitInstance> SpawnEnemyWave(int totalCount)
    {
        List<UnitInstance> spawned = new List<UnitInstance>();
        List<HexTile> validTiles = GetEnemyEdgeSpawnTiles();

        if (validTiles.Count == 0)
        {
            Debug.LogWarning("[MapGenerator] No unoccupied tiles available for spawning enemies.");
            return spawned;
        }

        while (spawned.Count < totalCount && validTiles.Count > 0)
        {
            // Pick an anchor tile to seed a new enemy cluster as far from players as possible.
            HexTile anchorTile = GetFarthestEnemyAnchor(validTiles);
            validTiles.Remove(anchorTile);

            if (!anchorTile.CanEnter()) continue;

            // Gather the anchor plus its still-free neighbors as landing spots for this cluster.
            // Each spot hosts exactly one enemy - no stacking.
            List<HexTile> groupSpots = new List<HexTile> { anchorTile };
            foreach (HexTile neighbor in anchorTile.neighbors)
            {
                if (groupSpots.Count >= enemyGroupSize) break;
                if (neighbor != null && neighbor.CanEnter() && !groupSpots.Contains(neighbor))
                    groupSpots.Add(neighbor);
            }

            int groupTarget = Mathf.Min(enemyGroupSize, groupSpots.Count, totalCount - spawned.Count);

            for (int i = 0; i < groupTarget; i++)
            {
                HexTile spot = groupSpots[i];
                UnitInstance enemy = SpawnEnemyUnit(spot, isEnemy: true);
                if (enemy != null)
                {
                    spawned.Add(enemy);
                    validTiles.Remove(spot); // tile is now occupied, remove from the pool
                }
            }
        }

        Debug.Log($"[MapGenerator] Spawned enemy wave of {spawned.Count} in clusters of up to {enemyGroupSize}.");
        return spawned;
    }

    private List<HexTile> GetEnemyEdgeSpawnTiles()
    {
        List<HexTile> candidates = new List<HexTile>();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                bool isEdge = x == 0 || x == width - 1 || y == 0 || y == height - 1;
                if (isEdge)
                {
                    HexTile tile = GetTile(new Vector2Int(x, y));
                    if (tile != null && tile.CanEnter())
                    {
                        candidates.Add(tile);
                    }
                }
            }
        }

        return candidates;
    }

    private List<HexTile> GetValidSpawnTiles()
    {
        List<HexTile> candidates = new List<HexTile>();

        foreach (HexTile tile in tileMap.Values)
        {
            if (tile == null || !tile.CanEnter())
                continue;

            Vector2Int position = tile.gridPosition;
            bool isWithinEdgeMargin =
                position.x < VillagerEdgeMargin ||
                position.x >= width - VillagerEdgeMargin ||
                position.y < VillagerEdgeMargin ||
                position.y >= height - VillagerEdgeMargin;

            if (!isWithinEdgeMargin)
                candidates.Add(tile);
        }

        return candidates;
    }

    private HexTile GetFarthestEnemyAnchor(List<HexTile> candidates)
    {
        HexTile farthestTile = null;
        int farthestDistance = int.MinValue;
        UnitInstance[] units = FindObjectsByType<UnitInstance>();

        foreach (HexTile candidate in candidates)
        {
            int nearestPlayerDistance = int.MaxValue;
            foreach (UnitInstance player in units)
            {
                if (player == null || player.Faction != UnitFaction.Player || player.currentTile == null)
                    continue;

                int distance = HexCoordinates.GetDistance(
                    candidate.gridPosition,
                    player.currentTile.gridPosition);
                nearestPlayerDistance = Mathf.Min(nearestPlayerDistance, distance);
            }

            if (nearestPlayerDistance > farthestDistance)
            {
                farthestDistance = nearestPlayerDistance;
                farthestTile = candidate;
            }
        }

        return farthestTile;
    }

    private void placeUnits()
    {
        if (gameStateManager == null)
        {
            Debug.LogError("MapGenerator: GameStateManager instance is null. Cannot place units.");
            return;
        }

        gameStateManager.EnsurePlayerHasTeam();
        List<Unit> playerTeam = gameStateManager.ActiveTeam;
        Vector2Int[] playerSpawnPositions =
        {
            new Vector2Int(0, 1),
            new Vector2Int(1, 1),
            new Vector2Int(1, 0),
            new Vector2Int(2, 0),
            new Vector2Int(0, 2)
        };

        int playerCount = Mathf.Min(playerTeam.Count, playerSpawnPositions.Length);
        for (int count = 0; count < playerCount; count++)
        {
            Unit unit = playerTeam[count];
            Vector2Int preferredPosition = playerSpawnPositions[count];
            Debug.Log($"Placing player unit: {unit.UnitName} at starting position {preferredPosition}");

            HexTile tile = GetPlayerSpawnTile(preferredPosition);

            if (tile != null)
            {
                if (unit == null || unit.Archetype == null)
                {
                    Debug.LogError("MapGenerator: player unit is missing its archetype.");
                    continue;
                }

                GameObject unitPrefab = unit.Archetype.ModelPrefab;
                if (unitPrefab == null)
                {
                    Debug.LogError($"MapGenerator: {unit.UnitName} has no ModelPrefab assigned.");
                    continue;
                }

                GameObject spawnedUnit = Instantiate(unitPrefab, tile.transform.position, Quaternion.identity);
                UnitInstance instance = spawnedUnit.GetComponent<UnitInstance>();
                if (instance == null)
                {
                    Debug.LogError($"MapGenerator: {unitPrefab.name} has no UnitInstance component.");
                    Destroy(spawnedUnit);
                    continue;
                }
                instance.Initialize(unit);
                instance.PlaceOnTile(tile);
            }
            else
            {
                Debug.LogWarning($"No valid player spawn tile found near {preferredPosition} for unit {unit.UnitName}");
            }
        }
    }

    private HexTile GetPlayerSpawnTile(Vector2Int preferredPosition)
    {
        HexTile preferredTile = GetTile(preferredPosition);
        if (preferredTile != null && preferredTile.CanEnter())
            return preferredTile;

        HexTile closestTile = null;
        int closestDistance = int.MaxValue;
        foreach (HexTile tile in tileMap.Values)
        {
            if (tile == null || !tile.CanEnter())
                continue;

            int distance = HexCoordinates.GetDistance(preferredPosition, tile.gridPosition);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestTile = tile;
            }
        }

        return closestTile;
    }

    // ---------------------------------------------------------------------
    // Extraction: a unit must stand adjacent to the fixed exfil tile.
    // ---------------------------------------------------------------------
    private void MarkExtractionPoints()
    {
        HexTile exfilTile = GetTile(ExfilTilePosition);
        if (exfilTile == null)
        {
            Debug.LogWarning($"MapGenerator: exfil tile {ExfilTilePosition} has no matching tile.");
            return;
        }

        exfilTile.IsExtractionPoint = true;
        exfilTile.isWalkable = false;
    }

    /// <summary>Call when a unit attempts to leave the battle from its current tile.</summary>
    public bool TryExtractUnit(UnitInstance unit)
    {
        if (unit == null || unit.currentTile == null) return false;

        HexTile exfilTile = GetTile(ExfilTilePosition);
        if (exfilTile == null ||
            HexCoordinates.GetDistance(unit.currentTile.gridPosition, ExfilTilePosition) != 1)
        {
            Debug.Log($"[MapGenerator] {unit.name} is not adjacent to the exfil tile.");
            return false;
        }

        unit.currentTile.RemoveUnit();
        unit.currentTile = null;
        Debug.Log($"[MapGenerator] {unit.name} extracted safely.");
        UnitExtracted?.Invoke(unit);
        Destroy(unit.gameObject);
        return true;
    }

    // ---------------------------------------------------------------------
    // Villagers: this script only spawns them. Rescuing (a unit carrying up
    // to 2 villagers) is handled entirely by UnitInstance.
    // ---------------------------------------------------------------------
    public List<UnitInstance> SpawnVillagers(int count)
    {
        List<UnitInstance> spawnedVillagers = new List<UnitInstance>();

        if (villagerArchetypes == null || villagerArchetypes.Count == 0)
        {
            Debug.LogError("[MapGenerator] No villager archetypes assigned.");
            return spawnedVillagers;
        }

        List<HexTile> validTiles = GetValidSpawnTiles();
        int spawnAmount = Mathf.Min(count, validTiles.Count);

        for (int i = 0; i < spawnAmount; i++)
        {
            int tileIndex = UnityEngine.Random.Range(0, validTiles.Count);
            HexTile tile = validTiles[tileIndex];
            validTiles.RemoveAt(tileIndex);

            UnitData villagerData = villagerArchetypes[UnityEngine.Random.Range(0, villagerArchetypes.Count)];
            UnitInstance villager = SpawnVillagerUnit(tile, villagerData);
            if (villager != null) spawnedVillagers.Add(villager);
        }

        Debug.Log($"[MapGenerator] Spawned {spawnedVillagers.Count} villagers to rescue.");
        return spawnedVillagers;
    }

    private UnitInstance SpawnVillagerUnit(HexTile tile, UnitData data)
    {
        if (tile == null || !tile.CanEnter() || data == null || data.ModelPrefab == null) return null;

        GameObject spawnedObj = Instantiate(data.ModelPrefab, tile.transform.position, Quaternion.identity);
        if (!spawnedObj.TryGetComponent<UnitInstance>(out var instance))
        {
            Debug.LogError($"[MapGenerator] Villager prefab '{data.ModelPrefab.name}' is missing a UnitInstance component!");
            Destroy(spawnedObj);
            return null;
        }

        instance.Initialize(data);
        instance.Faction = UnitFaction.Villager;
        instance.PlaceOnTile(tile);
        return instance;
    }

    private TerrainType PickTerrain(float noiseValue)
    {
        foreach (var band in terrainBands)
        {
            if (noiseValue <= band.maxHeight)
                return band.terrain;
        }
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
        tile.isRevealed = false;
        tile.SetDarkness(tile.hiddenDarkness);
        CreateFog(tile);
        tileObj.name = $"Tile_{gridPosition.x}_{gridPosition.y}";

        tileMap[gridPosition] = tile;
    }

    private void CreateFog(HexTile tile)
    {
        if (Cloud == null)
        {
            Clouds cloud = FindAnyObjectByType<Clouds>(FindObjectsInactive.Include);
            if (cloud != null) Cloud = cloud.gameObject;
        }

        if (Cloud == null) return;

        GameObject fog = Instantiate(
            Cloud,
            tile.transform.position + Vector3.up * fogHeight,
            Quaternion.identity,
            tile.transform);
        fog.name = $"Fog_{tile.gridPosition.x}_{tile.gridPosition.y}";
        fog.transform.localPosition = Vector3.up * fogHeight;
        fog.SetActive(true);
        tile.fogInstance = fog;
    }

    private int GetRevealRadius(UnitInstance unit)
    {
        return Mathf.Max(0, unit.visibilityRange);
    }

    private void InitializeRevealState()
    {
        UnitInstance[] units = FindObjectsByType<UnitInstance>();
        foreach (UnitInstance unit in units)
        {
            if (unit != null) unit.OnTileHidden();
        }

        foreach (UnitInstance unit in units)
        {
            if (unit == null || unit.currentTile == null) continue;

            if (unit.Faction == UnitFaction.Player || unit.Faction == UnitFaction.Villager)
                RevealAround(unit.currentTile, GetRevealRadius(unit));
        }
    }

    public void RevealAroundUnit(UnitInstance unit)
    {
        if (unit == null || unit.currentTile == null) return;

        if (unit.Faction == UnitFaction.Player || unit.Faction == UnitFaction.Villager)
            RevealAround(unit.currentTile, GetRevealRadius(unit));
    }

    private void RevealAround(HexTile center, int radius)
    {
        foreach (HexTile tile in tileMap.Values)
        {
            if (tile != null &&
                HexCoordinates.GetDistance(center.gridPosition, tile.gridPosition) <= radius)
            {
                tile.Reveal();
            }
        }
    }

    private GameObject GetPrefabForTerrain(TerrainType terrain)
    {
        if (!prefabsByTerrain.TryGetValue(terrain, out var matchingPrefabs) || matchingPrefabs.Count == 0)
            return null;

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