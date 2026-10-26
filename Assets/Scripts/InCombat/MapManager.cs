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
public partial class MapManager : MonoBehaviour
{
    public static MapManager Instance { get; private set; }
    private static readonly Vector2Int ExfilTilePosition = new Vector2Int(0, 0);
    private const int StartZoneSize = 3;
    private const int VillagerEdgeMargin = 2;
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
    [Tooltip("Asset names of the enemy archetypes in the order they unlock. The opening wave is the first type only; each reinforcement wave unlocks the next. Archetypes not listed unlock with the last one.")]
    [SerializeField] private List<string> enemyUnlockOrder = new List<string> { "Minion", "Mage", "Rogue", "Warrior" };

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
    [Tooltip("Each level adds this fraction of base defense (rounded, so +1 defense shows up after a few levels).")]
    [SerializeField] private float defenseScalingPerWave = 0.15f;
    [Tooltip("Multiplier on every per-wave scaling rate above; below 1 slows how fast enemies grow.")]
    [SerializeField] private float waveScalingPace = 0.4f;
    [Tooltip("Levels needed per +1 movement range (0 disables).")]
    [SerializeField] private int levelsPerMovementBonus = 6;
    [Tooltip("Levels needed per +1 attack range (0 disables). Capped at +1 so ranged enemies don't outrange everything.")]
    [SerializeField] private int levelsForRangeBonus = 15;
    [Tooltip("Level at which enemies gain an extra action per turn (0 disables).")]
    [SerializeField] private int levelForExtraAction = 15;
    [Tooltip("Extra enemy levels (same scaling as waves) for an enemy spawning right next to the exfil zone. Falls off linearly to 0 at the far corner of the map.")]
    [SerializeField] private float maxExfilProximityLevels = 3f;

    [Header("Villagers / Rescue")]
    [Tooltip("Archetypes used when spawning rescuable villagers. Rescue logic itself lives on UnitInstance.")]
    public List<UnitData> villagerArchetypes;
    private int villagerSpawnTarget;
    private int villagersSpawned;

    [Header("Worker Villagers")]
    [Tooltip("Archetype for the player's worker villagers. Falls back to the first villager archetype if empty.")]
    public UnitData workerArchetype;
    [Tooltip("Farm visual built by food workers. A placeholder cube is used if empty.")]
    public GameObject farmPrefab;
    [Tooltip("Lumber mill visual built by materials workers. A placeholder cube is used if empty.")]
    public GameObject lumberMillPrefab;
    [Tooltip("Minimum movement range for workers (the base villager archetype only moves 1 tile).")]
    [SerializeField] private int workerMovementRange = 3;

    [Header("Building")]
    [Tooltip("Optional wall prefab. If empty, a simple cube is generated at runtime. A UnitInstance is added automatically if the prefab lacks one.")]
    public GameObject wallPrefab;
    [Tooltip("Settlement Materials spent per wall.")]
    public int wallMaterialCost = 5;
    [Tooltip("Settlement Materials spent per Fortify (each click builds the next fortification level on the unit's tile).")]
    public int fortifyMaterialCost = 2;
    [Tooltip("Fortification objects spawned on the tile, indexed by level (1, 2, 3). They stay after the unit leaves. A missing entry falls back to the nearest lower level.")]
    [SerializeField] private GameObject[] fortificationPrefabs = new GameObject[3];
    [Tooltip("Offset from the tile's top surface where the fortification object is placed.")]
    [SerializeField] private Vector3 fortificationOffset = Vector3.zero;
    public int wallDefense = 1;
    [Tooltip("Cost, health and look of Town Center, Church and Barracks (and the wall's size). Walls take cost/prefab/health from the wall fields above.")]
    [SerializeField] private BuildingSpec[] buildingSpecs = BuildingSpec.CreateDefaults();
    [SerializeField] private Color fallbackWallColor = new Color(0.45f, 0.33f, 0.22f);

    // Gates the villager spawn-focus tour behind the player unit spawn-focus tour,
    // so villagers only start their camera zoom-in after the players' has finished.
    private bool initialUnitSpawnTourComplete = true;

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
        if (SaveSystem.LoadOnNextStart)
        {
            SaveSystem.LoadOnNextStart = false;
            if (SaveSystem.TryRead(out BattleSaveData save))
            {
                gameStateManager = GameStateManager.Instance;
                RestoreFromSave(save);
                return;
            }
            Debug.LogWarning("MapManager: no readable save found; starting a new battle.");
        }
        if (randomizeSeed)
            seed = UnityEngine.Random.Range(0, 10000);
        Generate();
        gameStateManager = GameStateManager.Instance;
        placeUnits();
        SpawnEnemyWave(enemySpawnCount);
        villagerSpawnTarget = GetPlayerUnitCount() * VillagersPerPlayerUnit;
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

    private const int TownCenterVillagerInterval = 3;
    private const int VillagersPerTownCenter = 5;
    private int roundsSinceTownCenterVillager;
    private int nextTownCenterIndex;

    private void HandleTownCenterVillagers()
    {
        List<UnitInstance> centers = GetStructures(BuildingType.TownCenter);
        if (centers.Count == 0) { roundsSinceTownCenterVillager = 0; return; }
        if (++roundsSinceTownCenterVillager < TownCenterVillagerInterval) return;

        // Every Town Center raises the villager cap by 5. Keep the timer full at the cap so one spawns as soon as a slot frees.
        if (GetTownCenterVillagers().Count >= centers.Count * VillagersPerTownCenter) return;

        roundsSinceTownCenterVillager = 0;
        // Take turns so each Town Center fills its own 5 slots.
        SpawnTownCenterVillager(centers[nextTownCenterIndex++ % centers.Count].currentTile);
    }

    private void HandleRoundAdvanced(int round)
    {
        HandleTownCenterVillagers();
    }

    /// <summary>The player's Town Center villagers still on the field.</summary>
    public List<WorkerVillager> GetTownCenterVillagers()
    {
        var result = new List<WorkerVillager>();
        foreach (UnitInstance unit in FindObjectsByType<UnitInstance>())
            if (unit != null && unit.IsWorker && EnemyBattlefieldState.IsOnField(unit) &&
                unit.TryGetComponent(out WorkerVillager worker))
                result.Add(worker);
        return result;
    }

    /// Archetypes enemies may spawn as at this wave level: one type at wave 0, one more per wave, all by wave 3.
    private List<UnitData> UnlockedEnemyArchetypes(int waveLevel)
    {
        var ordered = new List<UnitData>();
        foreach (string archetypeName in enemyUnlockOrder)
        {
            UnitData match = AvailableEnemyArchetypes.Find(a => a != null && a.name == archetypeName);
            if (match != null && !ordered.Contains(match)) ordered.Add(match);
        }
        foreach (UnitData archetype in AvailableEnemyArchetypes)
            if (archetype != null && !ordered.Contains(archetype)) ordered.Add(archetype);

        if (ordered.Count == 0) return AvailableEnemyArchetypes;

        int unlocked = Mathf.Clamp(1 + waveLevel, 1, ordered.Count);
        return ordered.GetRange(0, unlocked);
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

        UnitData enemyData = data;
        if (enemyData == null)
        {
            List<UnitData> pool = isEnemy ? UnlockedEnemyArchetypes(waveLevel) : AvailableEnemyArchetypes;
            enemyData = pool[UnityEngine.Random.Range(0, pool.Count)];
        }
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
        if (isEnemy)
        {
            instance.Faction = UnitFaction.Enemy;
            ApplyWaveScaling(instance, waveLevel + GetExfilProximityLevels(tile));
        }
        string uniqueId = GenerateUnique4DigitString();
        instance.unitName = $"{enemyData.UnitName}_{uniqueId}";
        instance.gameObject.name = instance.unitName;

        instance.PlaceOnTile(tile);

        // 6. Spawn Visual Juice / Particle Effects
        //PlaySpawnEffects(tile.transform.position);

        return instance;
    }

    /// <summary>Bonus levels for spawning close to the exfil tile: full at distance 0, none at the map's far corner.</summary>
    private int GetExfilProximityLevels(HexTile tile)
    {
        int farthest = HexCoordinates.GetDistance(new Vector2Int(width - 1, height - 1), ExfilTilePosition);
        if (farthest <= 0 || maxExfilProximityLevels <= 0f) return 0;

        int dist = HexCoordinates.GetDistance(tile.gridPosition, ExfilTilePosition);
        float closeness = 1f - Mathf.Clamp01((float)dist / farthest);
        return Mathf.RoundToInt(closeness * maxExfilProximityLevels);
    }

    /// <summary>Flat per-wave bonus applied to this instance only - the shared UnitData asset is never touched.</summary>
    private void ApplyWaveScaling(UnitInstance enemy, int waveLevel)
    {
        if (waveLevel <= 0) return;

        float pace = waveScalingPace;
        enemy.maxHealth = Mathf.RoundToInt(enemy.maxHealth * (1f + waveLevel * hpScalingPerWave * pace));
        enemy.currentHealth = enemy.maxHealth;
        enemy.attackPower = Mathf.RoundToInt(enemy.attackPower * (1f + waveLevel * attackScalingPerWave * pace));
        enemy.defensePower += Mathf.RoundToInt(enemy.defensePower * waveLevel * defenseScalingPerWave * pace);
        if (levelsPerMovementBonus > 0)
            enemy.movementRange += waveLevel / levelsPerMovementBonus;
        if (levelsForRangeBonus > 0 && waveLevel >= levelsForRangeBonus)
            enemy.attackRange += 1;
        if (levelForExtraAction > 0 && waveLevel >= levelForExtraAction)
        {
            enemy.maxActionsPerTurn += 1;
            enemy.actionsRemaining = enemy.maxActionsPerTurn;
        }
        enemy.NotifyStatsChanged();
    }

    // ---------------------------------------------------------------------
    // Building: a hero spends Materials/Food to raise a structure on an adjacent tile.
    // Structures are UnitInstances (Faction.Structure) so enemies attack them
    // through the normal combat pipeline; they block movement while standing.
    // Church and Barracks need a standing Town Center, and the tiles around a
    // Town Center are reserved: nothing can be built on them afterwards.
    // ---------------------------------------------------------------------

    private readonly Dictionary<BuildingType, List<UnitInstance>> structures = new Dictionary<BuildingType, List<UnitInstance>>();

    /// <summary>The cost/look of a structure type. Walls take their cost, prefab and health from the older wall fields and research.</summary>
    public BuildingSpec GetBuildingSpec(BuildingType type)
    {
        BuildingSpec spec = null;
        if (buildingSpecs != null)
            foreach (BuildingSpec candidate in buildingSpecs)
                if (candidate != null && candidate.Type == type) { spec = candidate; break; }
        if (spec == null)
            foreach (BuildingSpec candidate in BuildingSpec.CreateDefaults())
                if (candidate.Type == type) { spec = candidate; break; }

        if (type != BuildingType.Wall) return spec;
        return new BuildingSpec
        {
            Type = spec.Type,
            DisplayName = spec.DisplayName,
            Size = spec.Size,
            Color = fallbackWallColor,
            MaterialCost = wallMaterialCost,
            MaxHealth = Mathf.Max(1, ResearchManager.Current.WallMaxHealth),
            Defense = wallDefense,
            Prefab = wallPrefab
        };
    }

    /// <summary>Standing structures of a type (destroyed ones are dropped).</summary>
    public List<UnitInstance> GetStructures(BuildingType type)
    {
        if (!structures.TryGetValue(type, out List<UnitInstance> list)) return new List<UnitInstance>();
        list.RemoveAll(unit => unit == null || unit.IsDead);
        return new List<UnitInstance>(list);
    }

    public bool HasStandingTownCenter => GetStructures(BuildingType.TownCenter).Count > 0;

    /// <summary>Whether the player may start this building now: prerequisites first, then resources. <paramref name="reason"/> explains a no.</summary>
    public bool CanBuild(BuildingType type, out string reason)
    {
        reason = null;
        if (!MeetsPrerequisites(type, out reason)) return false;

        BuildingSpec spec = GetBuildingSpec(type);
        GameStateManager gsm = GameStateManager.Instance;
        if (gsm == null) { reason = "Unavailable"; return false; }
        if (gsm.Settlement.Materials < spec.MaterialCost) { reason = $"Need {spec.MaterialCost} Materials"; return false; }
        if (gsm.Settlement.Food < spec.FoodCost) { reason = $"Need {spec.FoodCost} Food"; return false; }
        return true;
    }

    private bool MeetsPrerequisites(BuildingType type, out string reason)
    {
        reason = null;
        if (GetBuildingSpec(type).RequiresTownCenter && !HasStandingTownCenter)
        {
            reason = "Requires a standing Town Center";
            return false;
        }
        return true;
    }

    /// <summary>Empty, revealed tiles next to the builder where a structure can go.</summary>
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
               !tile.IsReserved &&
               tile.terrainType != TerrainType.Water &&
               tile.terrainType != TerrainType.Exfil &&
               !tile.IsExtractionPoint;
    }

    /// <summary>Spawns a structure on the tile. Does not charge resources - the caller does. Returns null if it can't be placed.</summary>
    public UnitInstance BuildStructure(BuildingType type, HexTile tile)
    {
        if (!IsBuildable(tile) || !MeetsPrerequisites(type, out _)) return null;

        UnitInstance building = CreateStructure(type, tile);
        PlaySpawnEffects(tile.transform.position);
        if (type == BuildingType.TownCenter)
        {
            SpawnTownCenterVillager(tile); // the first villager arrives with the building
        }
        return building;
    }

    /// <summary>Raises the structure with no cost, prerequisite or villager side effects (also used to rebuild a saved game).</summary>
    private UnitInstance CreateStructure(BuildingType type, HexTile tile)
    {
        BuildingSpec spec = GetBuildingSpec(type);
        Vector3 position = tile.transform.position + Vector3.up * tile.heightOffset;
        GameObject structureObject;
        float visualLift = 0f;

        if (spec.Prefab != null)
        {
            structureObject = Instantiate(spec.Prefab, position, new Quaternion(0f, 120f, 0f, 1f));
        }
        else
        {
            structureObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Vector3 size = spec.Size * hexSize;
            structureObject.transform.localScale = size;
            visualLift = size.y * 0.5f;

            // Clicks should fall through to the tile underneath.
            Collider col = structureObject.GetComponent<Collider>();
            if (col != null) Destroy(col);

            Renderer structureRenderer = structureObject.GetComponent<Renderer>();
            if (structureRenderer != null) structureRenderer.material.color = spec.Color;
        }

        if (!structureObject.TryGetComponent(out UnitInstance building))
            building = structureObject.AddComponent<UnitInstance>();

        building.Faction = UnitFaction.Structure;
        building.IsWall = type == BuildingType.Wall;
        building.IsEnterable = type != BuildingType.Wall;
        building.unitName = type == BuildingType.Wall ? $"Wall_{GenerateUnique4DigitString()}" : spec.DisplayName;
        structureObject.name = building.unitName;
        building.maxHealth = Mathf.Max(1, spec.MaxHealth);
        building.currentHealth = building.maxHealth;
        building.defensePower = spec.Defense;
        building.attackPower = 0;
        building.attackRange = 0;
        building.movementRange = 0;
        building.visibilityRange = 0;
        building.maxActionsPerTurn = 0;
        building.actionsRemaining = 0;

        building.PlaceOnTile(tile);
        if (visualLift > 0f)
            building.transform.position += Vector3.up * visualLift;

        if (!structures.TryGetValue(type, out List<UnitInstance> list))
            structures[type] = list = new List<UnitInstance>();
        list.Add(building);

        // Nothing may ever be built around a Town Center.
        if (type == BuildingType.TownCenter)
            foreach (HexTile neighbor in tile.neighbors)
                if (neighbor != null) neighbor.IsReserved = true;

        building.NotifyStatsChanged();
        return building;
    }

    /// <summary>A new Town Center starts with one villager on the Food role; WorkerVillager.SetRole swaps it.</summary>
    private void SpawnTownCenterVillager(HexTile townCenter)
    {
        UnitData data = workerArchetype != null ? workerArchetype
            : (villagerArchetypes != null && villagerArchetypes.Count > 0 ? villagerArchetypes[0] : null);
        if (data == null)
        {
            Debug.LogError("[MapGenerator] No worker (or villager) archetype assigned; Town Center villager not spawned.");
            return;
        }

        foreach (HexTile neighbor in townCenter.neighbors)
        {
            if (neighbor == null || !neighbor.CanEnter() || neighbor.terrainType == TerrainType.Water ||
                neighbor.IsExtractionPoint) continue;

            UnitInstance villager = SpawnWorkerUnit(neighbor, data, isFood: true);
            if (villager == null) return;
            if (CombatManager.Instance != null) CombatManager.Instance.TrackAll(new List<UnitInstance> { villager });
            villager.playSpawnAnimation();
            return;
        }
        Debug.LogWarning("[MapGenerator] No free tile next to the Town Center for its villager.");
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
        TerrainType terrain = PickTerrain(noiseValue);

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

                GameObject spawnedUnit = Instantiate(unitPrefab, tile.transform.position, unitPrefab.transform.rotation);
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

        for (int i = 0; i < spawnedUnits.Count; i++)
        {
            PlaySpawnEffects(spawnedUnits[i].currentTile.transform.position);
            spawnedUnits[i].playSpawnAnimation();
        }
        StartCoroutine(PlayInitialUnitSpawnTour(spawnedUnits[0]));


        return spawnedUnits;
    }

    /// <summary>Spawns a freshly drafted hero on a free tile next to the Barracks (or the nearest free tile). Null if there is no room.</summary>
    public UnitInstance SpawnHero(Unit unit)
    {
        List<UnitInstance> barracks = GetStructures(BuildingType.Barracks);
        if (unit == null || unit.Archetype == null || unit.Archetype.ModelPrefab == null) return null;

        HexTile tile = null;
        if (barracks.Count > 0 && barracks[0].currentTile != null)
        {
            foreach (HexTile neighbor in barracks[0].currentTile.neighbors)
            {
                if (neighbor != null && neighbor.CanEnter() && neighbor.terrainType != TerrainType.Water &&
                    neighbor.terrainType != TerrainType.Exfil && !neighbor.IsExtractionPoint)
                {
                    tile = neighbor;
                    break;
                }
            }
            if (tile == null) tile = GetPlayerSpawnTile(barracks[0].currentTile.gridPosition);
        }
        if (tile == null) return null;

        GameObject prefab = unit.Archetype.ModelPrefab;
        GameObject spawned = Instantiate(prefab, tile.transform.position, prefab.transform.rotation);
        if (!spawned.TryGetComponent(out UnitInstance instance))
        {
            Destroy(spawned);
            return null;
        }
        instance.Initialize(unit);
        instance.PlaceOnTile(tile);
        PlaySpawnEffects(tile.transform.position);
        instance.playSpawnAnimation();
        return instance;
    }

    private IEnumerator PlayInitialUnitSpawnTour(UnitInstance unit)
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
    // Evacuation points: one opens on a water tile every few map expansions, takes two villagers, then closes.
    private const int EvacuationPointSlots = 2;
    private readonly Dictionary<HexTile, GameObject> evacuationMarkers = new Dictionary<HexTile, GameObject>();

    /// <summary>Opens an evacuation point on a random water tile that touches land. Returns its tile, or null if none fits.</summary>
    public HexTile CreateEvacuationPoint()
    {
        var revealed = new List<HexTile>();
        var hidden = new List<HexTile>();
        foreach (HexTile tile in tileMap.Values)
        {
            if (tile == null || tile.terrainType != TerrainType.Water || tile.IsExtractionPoint) continue;
            bool touchesLand = false;
            foreach (HexTile neighbor in tile.neighbors)
                if (neighbor != null && neighbor.terrainType != TerrainType.Water) { touchesLand = true; break; }
            if (!touchesLand) continue;
            (tile.isRevealed ? revealed : hidden).Add(tile);
        }

        List<HexTile> pool = revealed.Count > 0 ? revealed : hidden;
        if (pool.Count == 0) return null;

        HexTile point = pool[UnityEngine.Random.Range(0, pool.Count)];
        point.IsExtractionPoint = true;
        point.ExtractionSlots = EvacuationPointSlots;
        RefreshExtractableTiles(point);
        RevealAround(point, 1);
        AddEvacuationMarker(point);
        return point;
    }

    private void AddEvacuationMarker(HexTile point)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "EvacuationPoint";
        marker.transform.SetParent(point.transform, false);
        marker.transform.position = point.transform.position + Vector3.up * (point.heightOffset + hexSize * 0.4f);
        marker.transform.localScale = new Vector3(hexSize * 0.5f, hexSize * 0.4f, hexSize * 0.5f);
        if (marker.TryGetComponent(out Collider col)) Destroy(col);
        Color gold = new Color(1f, 0.82f, 0.1f);
        if (marker.TryGetComponent(out Renderer rend)) rend.material.color = gold;

        // Flat gold disc over the whole tile so the point reads as highlighted.
        GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        glow.name = "EvacuationGlow";
        glow.transform.SetParent(marker.transform, true);
        glow.transform.position = point.transform.position + Vector3.up * (point.heightOffset + 0.02f);
        glow.transform.localScale = new Vector3(hexSize * 1.6f, 0.01f, hexSize * 1.6f);
        if (glow.TryGetComponent(out Collider glowCol)) Destroy(glowCol);
        if (glow.TryGetComponent(out Renderer glowRend)) glowRend.material.color = gold;
        evacuationMarkers[point] = marker;
    }

    /// <summary>Villagers a hero standing on this tile can still hand over (sum of open adjacent points).</summary>
    public int GetEvacuationSlots(HexTile standingOn)
    {
        int slots = 0;
        if (standingOn == null) return 0;
        foreach (HexTile neighbor in standingOn.neighbors)
            if (neighbor != null && neighbor.IsExtractionPoint) slots += neighbor.ExtractionSlots;
        return slots;
    }

    /// <summary>Spends <paramref name="count"/> slots from the open points next to this tile, closing any that run out.</summary>
    public void UseEvacuationSlots(HexTile standingOn, int count)
    {
        if (standingOn == null) return;
        foreach (HexTile neighbor in standingOn.neighbors)
        {
            if (count <= 0) break;
            if (neighbor == null || !neighbor.IsExtractionPoint) continue;

            int used = Mathf.Min(count, neighbor.ExtractionSlots);
            neighbor.ExtractionSlots -= used;
            count -= used;
            if (neighbor.ExtractionSlots > 0) continue;

            neighbor.IsExtractionPoint = false;
            if (evacuationMarkers.TryGetValue(neighbor, out GameObject marker))
            {
                if (marker != null) Destroy(marker);
                evacuationMarkers.Remove(neighbor);
            }
            RefreshExtractableTiles(neighbor);
        }
    }

    // A land tile is extractable while any adjacent evacuation point is open.
    private static void RefreshExtractableTiles(HexTile point)
    {
        foreach (HexTile tile in point.neighbors)
        {
            if (tile == null || tile.IsExtractionPoint) continue;
            bool open = false;
            foreach (HexTile other in tile.neighbors)
                if (other != null && other.IsExtractionPoint) { open = true; break; }
            tile.IsExtractable = open;
        }
    }

    private void MarkExtractionPoints()
    {
        foreach (HexTile tile in tileMap.Values)
        {
            if (tile == null) continue;
            tile.IsExtractionPoint = false;
            tile.IsExtractable = false;
        }
    }

    public bool IsAdjacentToBoat(Vector2Int position) => false;

    /// Straight hex distance from the unit to the nearest tile it could extract from (-1 if there is none).
    /// Used to auto-resolve a forced retreat without animating the run.
    public int DistanceToExtraction(UnitInstance unit)
    {
        if (unit == null || unit.currentTile == null) return -1;

        int best = -1;
        foreach (HexTile tile in tileMap.Values)
        {
            if (tile == null || !tile.IsExtractable) continue;
            int distance = HexCoordinates.GetDistance(unit.currentTile.gridPosition, tile.gridPosition);
            if (best < 0 || distance < best) best = distance;
        }
        return best;
    }

    /// The reachable walkable tile beside any boat with the shortest route from the unit, or null.
    /// Villagers and workers can drop off at any boat, so a boxed-in boat is never a dead end.
    public HexTile FindBestBoatApproach(UnitInstance unit)
    {
        if (unit == null || unit.currentTile == null) return null;

        HexTile best = null;
        int bestLength = int.MaxValue;
        foreach (HexTile boat in tileMap.Values)
        {
            if (boat == null || !boat.IsExtractionPoint) continue;
            foreach (HexTile neighbor in boat.neighbors)
            {
                if (neighbor == null || !neighbor.isWalkable) continue;

                List<HexTile> path = HexPathfinder.FindPath(unit.currentTile, neighbor, -1, unit);
                if (path == null || path.Count >= bestLength) continue;
                bestLength = path.Count;
                best = neighbor;
            }
        }
        return best;
    }

    /// <summary>The standing Town Center's tile (where workers deposit), or null if none.</summary>
    public HexTile GetTownCenterTile()
    {
        List<UnitInstance> centers = GetStructures(BuildingType.TownCenter);
        return centers.Count > 0 ? centers[0].currentTile : null;
    }

    public bool IsAdjacentToTownCenter(HexTile tile)
    {
        HexTile center = GetTownCenterTile();
        return tile != null && center != null && HexCoordinates.GetDistance(tile.gridPosition, center.gridPosition) == 1;
    }

    /// <summary>The reachable walkable tile beside the Town Center with the shortest route from the unit, or null.</summary>
    public HexTile FindTownCenterApproach(UnitInstance unit)
    {
        HexTile center = GetTownCenterTile();
        if (unit == null || unit.currentTile == null || center == null) return null;

        HexTile best = null;
        int bestLength = int.MaxValue;
        foreach (HexTile neighbor in center.neighbors)
        {
            if (neighbor == null || !neighbor.isWalkable) continue;
            // The unit's own tile counts even though it is unwalkable while occupied.
            List<HexTile> path = HexPathfinder.FindPath(unit.currentTile, neighbor, -1, unit);
            if (path == null || path.Count >= bestLength) continue;
            bestLength = path.Count;
            best = neighbor;
        }
        return best;
    }

    /// <summary>Highest fortification level the player may build; raised by the Engineering research track.</summary>
    public static int MaxFortificationLevel = 3;//=> ResearchManager.Current.MaxFortificationLevel;

    /// <summary>Builds the next fortification level on a tile (permanent for the battle). Returns false at max level.</summary>
    public bool BuildFortification(HexTile tile)
    {
        if (tile == null || tile.IsReserved || tile.FortificationLevel >= MaxFortificationLevel) return false;

        int level = tile.FortificationLevel + 1;
        GameObject prefab = null;
        for (int l = level; l >= 1 && prefab == null; l--)
            if (fortificationPrefabs != null && l - 1 < fortificationPrefabs.Length)
                prefab = fortificationPrefabs[l - 1];
        tile.SetFortification(level, prefab, fortificationOffset);
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


        if (spawnedVillagers.Count > 0)
        {
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

        GameObject spawnedObj = Instantiate(data.ModelPrefab, tile.transform.position, data.ModelPrefab.transform.rotation);
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

    // ---------------------------------------------------------------------
    // Worker villagers: the player's autonomous farmers / lumberjacks. They
    // are NOT rescue targets (UnitInstance.IsWorker) and are driven entirely
    // by WorkerVillagerAIController.
    // ---------------------------------------------------------------------
    public List<UnitInstance> SpawnWorkers(int foodWorkers, int materialWorkers)
    {
        List<UnitInstance> workers = new List<UnitInstance>();
        if (foodWorkers + materialWorkers <= 0) return workers;

        UnitData data = workerArchetype != null ? workerArchetype
            : (villagerArchetypes != null && villagerArchetypes.Count > 0 ? villagerArchetypes[0] : null);
        if (data == null)
        {
            Debug.LogError("[MapGenerator] No worker (or villager) archetype assigned; workers not spawned.");
            return workers;
        }

        HexTile exfil = null;
        foreach (HexTile t in tileMap.Values)
            if (t != null && t.IsExtractionPoint) { exfil = t; break; }
        if (exfil == null) return workers;

        // Free tiles closest to the boat first: its neighbors, then ring by ring outward.
        List<HexTile> spawnTiles = new List<HexTile>();
        foreach (HexTile t in tileMap.Values)
        {
            if (t == null || !t.CanEnter() || t.IsExtractionPoint) continue;
            if (t.terrainType == TerrainType.Water) continue;
            spawnTiles.Add(t);
        }
        spawnTiles.Sort((a, b) =>
            HexCoordinates.GetDistance(a.gridPosition, exfil.gridPosition)
                .CompareTo(HexCoordinates.GetDistance(b.gridPosition, exfil.gridPosition)));

        int total = foodWorkers + materialWorkers;
        for (int i = 0; i < total && i < spawnTiles.Count; i++)
        {
            UnitInstance worker = SpawnWorkerUnit(spawnTiles[i], data, isFood: i < foodWorkers);
            if (worker != null) workers.Add(worker);
        }

        if (CombatManager.Instance != null) CombatManager.Instance.TrackAll(workers);
        Debug.Log($"[MapGenerator] Spawned {workers.Count} worker villagers ({foodWorkers} food, {materialWorkers} materials).");
        for (int i = 0; i < workers.Count; i++)
        {
            workers[i].playSpawnAnimation();
        }
        return workers;
    }

    private UnitInstance SpawnWorkerUnit(HexTile tile, UnitData data, bool isFood)
    {
        if (data.ModelPrefab == null) return null;

        GameObject obj = Instantiate(data.ModelPrefab, tile.transform.position, Quaternion.identity);
        if (!obj.TryGetComponent<UnitInstance>(out var instance))
        {
            Debug.LogError($"[MapGenerator] Worker prefab '{data.ModelPrefab.name}' is missing a UnitInstance component!");
            Destroy(obj);
            return null;
        }

        instance.Initialize(data);
        instance.Faction = UnitFaction.Villager;
        instance.IsWorker = true;
        instance.unitName = $"{(isFood ? "Farmer" : "Lumberjack")}_{GenerateUnique4DigitString()}";
        instance.gameObject.name = instance.unitName;
        // Workers use one action to move and one to work each turn; HP / yield buffs come from WorkerVillager.
        instance.maxActionsPerTurn = 1;
        instance.actionsRemaining = 1;
        instance.movementRange = Mathf.Max(instance.movementRange, workerMovementRange);

        WorkerVillager worker = obj.AddComponent<WorkerVillager>();
        worker.Setup(isFood);
        instance.PlaceOnTile(tile);
        return instance;
    }

    /// <summary>Raises the farm / lumber mill visual on a tile a worker has claimed. Returns the spawned object.</summary>
    public GameObject BuildWorkerStructure(HexTile tile, bool isFarm)
    {
        if (tile == null) return null;

        GameObject prefab = isFarm ? farmPrefab : lumberMillPrefab;
        GameObject structure;
        if (prefab != null)
        {
            structure = Instantiate(prefab, tile.transform.position, Quaternion.identity, tile.transform);
        }
        else
        {
            // Placeholder so the mechanic works before prefabs are assigned in the Inspector.
            structure = GameObject.CreatePrimitive(PrimitiveType.Cube);
            structure.transform.SetParent(tile.transform, false);
            structure.transform.position = tile.transform.position + Vector3.up * 0.35f;
            structure.transform.localScale = new Vector3(0.7f, 0.5f, 0.7f);
            if (structure.TryGetComponent<Collider>(out var col)) Destroy(col);
            if (structure.TryGetComponent<Renderer>(out var rend))
                rend.material.color = isFarm ? new Color(0.85f, 0.75f, 0.25f) : new Color(0.4f, 0.28f, 0.15f);
        }
        structure.name = isFarm ? "Farm" : "LumberMill";
        return structure;
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

    private void SpawnTile(Vector2Int gridPosition, TerrainType terrain, GameObject forcedPrefab = null)
    {
        GameObject prefab = forcedPrefab != null ? forcedPrefab : GetPrefabForTerrain(terrain);
        if (prefab == null)
        {
            Debug.LogWarning($"MapGenerator: no prefab configured for {terrain} at {gridPosition}, skipping tile.");
            return;
        }

        Vector3 worldPos = HexCoordinates.OffsetToWorldPosition(gridPosition, hexSize);
        GameObject tileObj = Instantiate(prefab, worldPos, Quaternion.identity, transform);

        HexTile tile = tileObj.GetComponent<HexTile>();
        tile.gridPosition = gridPosition;
        tile.prefabIndex = System.Array.IndexOf(tileGameObjectDatabase, prefab);
        string prefabName = prefab.name.ToLowerInvariant();
        tile.isForest = prefabName.Contains("forest");
        tile.isHill = prefabName.Contains("hill");
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
