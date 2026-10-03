using System;
using System.Collections;
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
    private const int StartZoneSize = 3;
    private const int VillagerEdgeMargin = 2;
    private const int InitialVillagerSpawnCount = 1;
    private const int VillagerSpawnInterval = 4;
    private const int VillagersPerPlayerUnit = 2;

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
    [Tooltip("Current map size. Starts small and grows via ExpandMap() on each enemy reinforcement wave.")]
    public int width = 14;
    public int height = 14;
    [Tooltip("The map stops growing once it reaches this size.")]
    public int maxWidth = 25;
    public int maxHeight = 25;
    [Tooltip("Columns/rows added to the +x/+y edges on each expansion.")]
    public int expansionStep = 2;

    [Header("Noise")]
    [Tooltip("Pick a fresh random seed each battle. Untick to use the fixed seed below.")]
    public bool randomizeSeed = true;
    public int seed = 0;
    public float noiseScale = 0.15f;
    [Tooltip("Frequency of the second, finer noise octave.")]
    public float detailScale = 0.4f;
    [Tooltip("How much the fine octave is blended in (0 = off).")]
    [Range(0f, 1f)] public float detailWeight = 0.3f;

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
    public int enemySpawnCount = 8;

    [Header("Enemy Wave Scaling")]
    [Tooltip("Each reinforcement wave adds this fraction of base max HP (0.15 = +15% per wave).")]
    [SerializeField] private float hpScalingPerWave = 0.15f;
    [Tooltip("Each reinforcement wave adds this fraction of base attack. Attack is a small integer, so this is higher than the HP rate.")]
    [SerializeField] private float attackScalingPerWave = 0.20f;

    [Header("Villagers / Rescue")]
    [Tooltip("Archetypes used when spawning rescuable villagers. Rescue logic itself lives on UnitInstance.")]
    public List<UnitData> villagerArchetypes;
    private int villagerSpawnTarget;
    private int villagersSpawned;

    [Header("Building")]
    [Tooltip("Optional wall prefab. If empty, a simple cube is generated at runtime. A UnitInstance is added automatically if the prefab lacks one.")]
    public GameObject wallPrefab;
    [Tooltip("Settlement Materials spent per wall.")]
    public int wallMaterialCost = 5;
    [Tooltip("Settlement Materials spent per Fortify.")]
    public int fortifyMaterialCost = 2;
    public int wallMaxHealth = 12;
    public int wallDefense = 1;
    [SerializeField] private Color fallbackWallColor = new Color(0.45f, 0.33f, 0.22f);

    // Gates the villager spawn-focus tour behind the player unit spawn-focus tour,
    // so villagers only start their camera zoom-in after the players' has finished.
    private bool initialUnitSpawnTourComplete = true;

    /// <summary>Fired when a unit successfully reaches an extraction point and leaves the map.</summary>
    public event Action<UnitInstance> UnitExtracted;
    public event Action<int, int> VillagerCountChanged;



    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BuildTerrainDatabase();
        if (randomizeSeed)
            seed = UnityEngine.Random.Range(0, 10000);
        Generate();
        gameStateManager = GameStateManager.Instance;
        placeUnits();
        SpawnEnemyWave(enemySpawnCount);
        villagerSpawnTarget = GetPlayerUnitCount() * VillagersPerPlayerUnit;
        SpawnVillagers(InitialVillagerSpawnCount);
        InitializeRevealState();
    }

    private void Start()
    {
        if (CombatManager.Instance != null)
            CombatManager.Instance.OnRoundAdvanced += HandleRoundAdvanced;

        // Test generation
        Debug.Log("Generated Code: " + GenerateUnique4DigitString());
    }

    private void OnDisable()
    {
        if (CombatManager.Instance != null)
            CombatManager.Instance.OnRoundAdvanced -= HandleRoundAdvanced;
    }

    private int GetPlayerUnitCount()
    {
        int playerCount = 0;
        foreach (UnitInstance unit in FindObjectsByType<UnitInstance>())
        {
            if (unit != null && unit.Faction == UnitFaction.Player)
                playerCount++;
        }

        return playerCount;
    }

    private void HandleRoundAdvanced(int round)
    {
        if (round <= 1 || (round - 1) % VillagerSpawnInterval != 0)
            return;

        var newVillagers = SpawnVillagers(1);
        if (newVillagers.Count > 0)
        {
            RevealAround(newVillagers[0].currentTile, 0);
        }
        
    }

    public UnitInstance SpawnEnemyUnit(HexTile tile, bool isEnemy, UnitData data = null, int waveLevel = 0)
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

        instance.Initialize(enemyData);
        if (isEnemy){
            instance.Faction =  UnitFaction.Enemy;
            ApplyWaveScaling(instance, waveLevel);
        }
        string uniqueId = GenerateUnique4DigitString();
        instance.unitName = $"{enemyData.UnitName}_{uniqueId}";
        instance.gameObject.name = instance.unitName;

        instance.PlaceOnTile(tile);

        // 6. Spawn Visual Juice / Particle Effects
        //PlaySpawnEffects(tile.transform.position);

        return instance;
    }

    /// <summary>Flat per-wave bonus applied to this instance only - the shared UnitData asset is never touched.</summary>
    private void ApplyWaveScaling(UnitInstance enemy, int waveLevel)
    {
        if (waveLevel <= 0) return;

        enemy.maxHealth = Mathf.RoundToInt(enemy.maxHealth * (1f + waveLevel * hpScalingPerWave));
        enemy.currentHealth = enemy.maxHealth;
        enemy.attackPower = Mathf.RoundToInt(enemy.attackPower * (1f + waveLevel * attackScalingPerWave));
        enemy.NotifyStatsChanged();
    }

    // ---------------------------------------------------------------------
    // Building: a unit spends Materials to raise a wall on an adjacent tile.
    // Walls are UnitInstances (Faction.Structure) so enemies attack them
    // through the normal combat pipeline; they block movement while standing.
    // ---------------------------------------------------------------------

    /// <summary>Empty, revealed tiles next to the builder where a wall can go.</summary>
    public List<HexTile> GetBuildableTiles(UnitInstance builder)
    {
        List<HexTile> tiles = new List<HexTile>();
        if (builder == null || builder.currentTile == null) return tiles;

        foreach (HexTile neighbor in builder.currentTile.neighbors)
        {
            if (IsBuildable(neighbor))
                tiles.Add(neighbor);
        }
        return tiles;
    }

    private static bool IsBuildable(HexTile tile)
    {
        return tile != null && tile.isRevealed && tile.CanEnter() &&
               tile.terrainType != TerrainType.Water &&
               tile.terrainType != TerrainType.Exfil &&
               !tile.IsExtractionPoint;
    }

    /// <summary>Spawns a wall on the tile. Does not charge Materials - the caller does.</summary>
    public UnitInstance BuildWall(HexTile tile)
    {
        if (!IsBuildable(tile)) return null;

        Vector3 position = tile.transform.position + Vector3.up * tile.heightOffset;
        GameObject wallObject;
        float visualLift = 0f;

        if (wallPrefab != null)
        {
            wallObject = Instantiate(wallPrefab, position, Quaternion.identity);
        }
        else
        {
            wallObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Vector3 size = new Vector3(hexSize * 0.8f, hexSize * 0.7f, hexSize * 0.8f);
            wallObject.transform.localScale = size;
            visualLift = size.y * 0.5f;

            // Clicks should fall through to the tile underneath.
            Collider col = wallObject.GetComponent<Collider>();
            if (col != null) Destroy(col);

            Renderer wallRenderer = wallObject.GetComponent<Renderer>();
            if (wallRenderer != null) wallRenderer.material.color = fallbackWallColor;
        }

        if (!wallObject.TryGetComponent(out UnitInstance wall))
            wall = wallObject.AddComponent<UnitInstance>();

        wall.Faction = UnitFaction.Structure;
        wall.unitName = $"Wall_{GenerateUnique4DigitString()}";
        wallObject.name = wall.unitName;
        wall.maxHealth = Mathf.Max(1, wallMaxHealth);
        wall.currentHealth = wall.maxHealth;
        wall.defensePower = wallDefense;
        wall.attackPower = 0;
        wall.attackRange = 0;
        wall.movementRange = 0;
        wall.visibilityRange = 0;
        wall.maxActionsPerTurn = 0;
        wall.actionsRemaining = 0;

        wall.PlaceOnTile(tile);
        if (visualLift > 0f)
            wall.transform.position += Vector3.up * visualLift;

        PlaySpawnEffects(tile.transform.position);
        wall.NotifyStatsChanged();
        return wall;
    }

    private void PlaySpawnEffects(Vector3 position)
    {
        if (spawnVfxPrefab != null)
        {
            position += Vector3.up * 0.5f; // lift the VFX slightly above the tile
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
                GenerateTileAt(x, y);
        }

        ConnectAllNeighbors();
        MarkExtractionPoints();
    }

    // Noise depends only on (x, y, seed), so tiles added by ExpandMap() match
    // what a larger initial map would have produced.
    private void GenerateTileAt(int x, int y)
    {
        float sx = x + seed * 10.37f;
        float sy = y + seed * 19.53f;
        float noiseValue = Mathf.PerlinNoise(sx * noiseScale, sy * noiseScale);

        // Second, finer octave breaks up the single big low-frequency blob so
        // water/hills show up as several patches instead of one central lake.
        if (detailWeight > 0f)
        {
            float detail = Mathf.PerlinNoise(sx * detailScale + 137.1f, sy * detailScale + 59.7f);
            noiseValue = Mathf.Lerp(noiseValue, detail, detailWeight);
        }

        Vector2Int gridPosition = new Vector2Int(x, y);
        TerrainType terrain = gridPosition == ExfilTilePosition
            ? TerrainType.Exfil
            : PickTerrain(noiseValue);

        // Keep the player start/exfil corner dry so units can always move out.
        if (terrain == TerrainType.Water && x <= StartZoneSize && y <= StartZoneSize)
            terrain = TerrainType.Grass;
        SpawnTile(gridPosition, terrain);
    }

    /// <summary>
    /// Grows the map toward +x/+y by expansionStep (capped at maxWidth/maxHeight).
    /// New tiles are fogged and linked to the old edge. Returns false if already at max size.
    /// </summary>
    public bool ExpandMap()
    {
        int newWidth = Mathf.Min(width + expansionStep, maxWidth);
        int newHeight = Mathf.Min(height + expansionStep, maxHeight);
        if (newWidth == width && newHeight == height) return false;

        for (int y = 0; y < newHeight; y++)
        {
            for (int x = 0; x < newWidth; x++)
            {
                if (!tileMap.ContainsKey(new Vector2Int(x, y)))
                    GenerateTileAt(x, y);
            }
        }

        width = newWidth;
        height = newHeight;
        ConnectAllNeighbors();
        Debug.Log($"MapGenerator: map expanded to {width}x{height}");
        return true;
    }

    private List<HexTile> GetSpacedGroupSpots(HexTile anchor, List<HexTile> validTiles, int groupSize)
    {
        List<HexTile> spots = new List<HexTile>();
        HashSet<HexTile> blocked = new HashSet<HexTile>(); // taken tiles + their neighbors

        List<HexTile> ring = new List<HexTile> { anchor };
        HashSet<HexTile> visited = new HashSet<HexTile> { anchor };

        while (spots.Count < groupSize && ring.Count > 0)
        {
            List<HexTile> nextRing = new List<HexTile>();
            foreach (HexTile tile in ring)
            {
                if (validTiles.Contains(tile) && tile.CanEnter() && !blocked.Contains(tile))
                {
                    spots.Add(tile);
                    blocked.Add(tile);
                    foreach (HexTile n in tile.neighbors)
                        if (n != null) blocked.Add(n); // reserve the gap around it

                    if (spots.Count >= groupSize) break;
                }

                foreach (HexTile neighbor in tile.neighbors)
                {
                    if (neighbor != null && !visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        nextRing.Add(neighbor);
                    }
                }
            }
            ring = nextRing;
        }

        return spots;
    }

    // ---------------------------------------------------------------------
    // Enemy clusters: groups land near each other, but never share a tile.
    // ---------------------------------------------------------------------
    public List<UnitInstance> SpawnEnemyWave(int totalCount, int waveLevel = 0)
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
            List<HexTile> groupSpots = GetSpacedGroupSpots(anchorTile, validTiles, enemyGroupSize);
            /*foreach (HexTile neighbor in anchorTile.neighbors)
            {
                if (groupSpots.Count >= enemyGroupSize) break;
                if (neighbor != null && neighbor.CanEnter() && !groupSpots.Contains(neighbor))
                    groupSpots.Add(neighbor);
            }*/

            int groupTarget = Mathf.Min(enemyGroupSize, groupSpots.Count, totalCount - spawned.Count);

            for (int i = 0; i < groupTarget; i++)
            {
                HexTile spot = groupSpots[i];
                UnitInstance enemy = SpawnEnemyUnit(spot, isEnemy: true, waveLevel: waveLevel);
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

    private List<UnitInstance> placeUnits()
    {
        List<UnitInstance> spawnedUnits = new List<UnitInstance>();

        if (gameStateManager == null)
        {
            Debug.LogError("MapGenerator: GameStateManager instance is null. Cannot place units.");
            return spawnedUnits;
        }

        gameStateManager.EnsurePlayerHasTeam();
        List<Unit> playerTeam = gameStateManager.ActiveTeam;
        // Start near the middle of the map so the squad reaches the fight quickly.
        Vector2Int center = new Vector2Int(width / 2 - 1, height / 2 - 1);
        Vector2Int[] playerSpawnOffsets =
        {
            new Vector2Int(0, 0),
            new Vector2Int(1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(1, 1),
            new Vector2Int(-1, 0)
        };
        Vector2Int[] playerSpawnPositions = new Vector2Int[playerSpawnOffsets.Length];
        for (int i = 0; i < playerSpawnOffsets.Length; i++)
            playerSpawnPositions[i] = center + playerSpawnOffsets[i];

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
                spawnedUnits.Add(instance);
            }
            else
            {
                Debug.LogWarning($"No valid player spawn tile found near {preferredPosition} for unit {unit.UnitName}");
            }
        }

        for (int i =0; i < spawnedUnits.Count; i++)
        {
            PlaySpawnEffects(spawnedUnits[i].currentTile.transform.position);
            spawnedUnits[i].playSpawnAnimation();
        }
        StartCoroutine(PlayInitialUnitSpawnTour(spawnedUnits[0]));


        return spawnedUnits;
    }

    private IEnumerator PlayInitialUnitSpawnTour( UnitInstance unit)
    {
        yield return PlayUnitSpawnFocusTour(unit);
        initialUnitSpawnTourComplete = true;
    }

    // -----------------------
    // Camera Spawn Focus
    // -----------------------
    private IEnumerator PlayUnitSpawnFocusTour(UnitInstance unit)
    {
        if (CameraController.Instance == null) yield break;
        yield return CameraController.Instance.FocusOnUnit(unit.transform);
    }

    private IEnumerator PlayVillagerSpawnFocusTour(UnitInstance villager)
    {
        // Villagers wait for the initial player spawn tour to finish before starting their own.
        while (!initialUnitSpawnTourComplete)
            yield return null;

        yield return PlayUnitSpawnFocusTour(villager);
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
        int remainingVillagerSlots = villagerSpawnTarget - villagersSpawned;
        int requestedCount = Mathf.Min(count, remainingVillagerSlots);

        if (requestedCount <= 0)
            return spawnedVillagers;

        if (villagerArchetypes == null || villagerArchetypes.Count == 0)
        {
            Debug.LogError("[MapGenerator] No villager archetypes assigned.");
            return spawnedVillagers;
        }

        List<HexTile> validTiles = GetValidSpawnTiles();
        int spawnAmount = Mathf.Min(requestedCount, validTiles.Count);

        for (int i = 0; i < spawnAmount; i++)
        {
            int tileIndex = UnityEngine.Random.Range(0, validTiles.Count);
            HexTile tile = validTiles[tileIndex];
            validTiles.RemoveAt(tileIndex);

            UnitData villagerData = villagerArchetypes[UnityEngine.Random.Range(0, villagerArchetypes.Count)];
            UnitInstance villager = SpawnVillagerUnit(tile, villagerData);
            if (villager != null) spawnedVillagers.Add(villager);
        }

        villagersSpawned += spawnedVillagers.Count;
        if (CombatManager.Instance != null)
            CombatManager.Instance.TrackAll(spawnedVillagers);
        VillagerCountChanged?.Invoke(villagersSpawned, villagerSpawnTarget);


        if (spawnedVillagers.Count > 0){
            for (int i = 0; i < count; i++)
            {
                spawnedVillagers[i].playSpawnAnimation();
            }
            StartCoroutine(PlayVillagerSpawnFocusTour(spawnedVillagers[0]));
        }
        Debug.Log($"[MapGenerator] Spawned {spawnedVillagers.Count} villagers to rescue.");
        return spawnedVillagers;
    }

    public int GetVillagersSpawned()
    {
        return villagersSpawned;
    }

    public int GetVillagerSpawnTarget()
    {
        return villagerSpawnTarget;
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
        {
            int newlyRevealed = RevealAround(unit.currentTile, GetRevealRadius(unit));
            if (unit.Faction == UnitFaction.Player)
                unit.AddExperience(newlyRevealed);
        }
    }

    private int RevealAround(HexTile center, int radius)
    {
        int newlyRevealed = 0;
        foreach (HexTile tile in tileMap.Values)
        {
            if (tile != null &&
                HexCoordinates.GetDistance(center.gridPosition, tile.gridPosition) <= radius)
            {
                if (!tile.isRevealed) newlyRevealed++;
                tile.Reveal();
            }
        }
        return newlyRevealed;
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
    private HashSet<string> usedCodes = new HashSet<string>();

    public string GenerateUnique4DigitString()
    {
        // Limit reached check (10,000 possible 4-digit combinations: 0000-9999)
        if (usedCodes.Count >= 10000)
        {
            Debug.LogWarning("All 10,000 unique 4-digit combinations have been used!");
            return null;
        }

        string newCode;
        do
        {
            // Generates a random integer between 0 and 9999
            int randomNum = UnityEngine.Random.Range(0, 10000);
            
            // Formats the number to always be 4 digits with leading zeros (e.g., "0042")
            newCode = randomNum.ToString("D4");
        } 
        while (!usedCodes.Add(newCode)); // Repeats if the code already exists in the set

        return newCode;
    }

}