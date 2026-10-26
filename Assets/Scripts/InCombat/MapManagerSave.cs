using System.Collections.Generic;
using UnityEngine;

/// <summary>Saving and restoring the map, its buildings and every unit on it (see SaveSystem).</summary>
public partial class MapManager
{
    // ---------------------------------------------------------------------
    // Write
    // ---------------------------------------------------------------------

    public void WriteSave(BattleSaveData data)
    {
        data.Seed = seed;
        data.Width = width;
        data.Height = height;
        data.RoundsSinceTownCenterVillager = roundsSinceTownCenterVillager;
        data.NextTownCenterIndex = nextTownCenterIndex;

        data.Tiles = new List<TileSave>();
        foreach (HexTile tile in tileMap.Values)
        {
            if (tile == null) continue;
            data.Tiles.Add(new TileSave
            {
                X = tile.gridPosition.x,
                Y = tile.gridPosition.y,
                PrefabIndex = tile.prefabIndex,
                Revealed = tile.isRevealed,
                FortificationLevel = tile.FortificationLevel,
                ExtractionSlots = tile.IsExtractionPoint ? tile.ExtractionSlots : 0
            });
        }

        data.Units = new List<UnitStateSave>();
        foreach (UnitInstance unit in FindObjectsByType<UnitInstance>())
        {
            // Carried villagers have no tile; they are written under their carrier.
            if (unit == null || unit.IsDead || unit.IsExtracted || unit.currentTile == null) continue;
            UnitStateSave saved = DescribeUnit(unit);
            if (saved != null) data.Units.Add(saved);
        }
    }

    private UnitStateSave DescribeUnit(UnitInstance unit)
    {
        var saved = new UnitStateSave
        {
            UnitName = unit.unitName,
            X = unit.currentTile.gridPosition.x,
            Y = unit.currentTile.gridPosition.y,
            InStructure = unit.currentTile.garrison == unit,
            MaxHealth = unit.maxHealth,
            CurrentHealth = unit.currentHealth,
            AttackPower = unit.attackPower,
            AttackRange = unit.attackRange,
            DefensePower = unit.defensePower,
            MovementRange = unit.movementRange,
            VisibilityRange = unit.visibilityRange,
            MaxActions = unit.maxActionsPerTurn,
            ActionsRemaining = unit.actionsRemaining,
            ColorScheme = unit.colorScheme
        };

        Unit persistent = unit.PersistentUnit;
        if (persistent != null)
        {
            saved.InstanceID = persistent.InstanceID;
            saved.ArchetypeName = persistent.Archetype != null ? persistent.Archetype.name : null;
            saved.WeaponPrefabName = persistent.WeaponPrefab != null ? persistent.WeaponPrefab.name : null;
            saved.EquipmentPrefabName = persistent.EquipmentPrefab != null ? persistent.EquipmentPrefab.name : null;
        }

        switch (unit.Faction)
        {
            case UnitFaction.Player:
                saved.Kind = SavedUnitKind.Hero;
                foreach (KeyValuePair<int, UnitInstance> carried in unit.CarriedVillagers)
                {
                    UnitInstance villager = carried.Value;
                    UnitData data = unit.rescuedUnitData[carried.Key];
                    bool isFood = villager.TryGetComponent(out WorkerVillager worker) && worker.IsFood;
                    saved.CarriedVillagers.Add(new CarriedSave
                    {
                        Slot = carried.Key,
                        ArchetypeName = data != null ? data.name : null,
                        UnitName = villager.unitName,
                        IsWorker = villager.IsWorker,
                        IsFood = isFood
                    });
                }
                return saved;

            case UnitFaction.Enemy:
                saved.Kind = SavedUnitKind.Enemy;
                return saved;

            case UnitFaction.Villager:
                if (unit.IsWorker && unit.TryGetComponent(out WorkerVillager workerState))
                {
                    saved.Kind = SavedUnitKind.Worker;
                    saved.IsFood = workerState.IsFood;
                    saved.State = workerState.State;
                    saved.Carried = workerState.Carried;
                    saved.HasSite = workerState.Site != null;
                    if (saved.HasSite)
                    {
                        saved.SiteX = workerState.Site.gridPosition.x;
                        saved.SiteY = workerState.Site.gridPosition.y;
                    }
                }
                else
                {
                    saved.Kind = SavedUnitKind.Villager;
                }
                return saved;

            case UnitFaction.Structure:
                foreach (KeyValuePair<BuildingType, List<UnitInstance>> entry in structures)
                {
                    if (!entry.Value.Contains(unit)) continue;
                    saved.Kind = SavedUnitKind.Structure;
                    saved.Building = entry.Key;
                    return saved;
                }
                return null;

            default:
                return null;
        }
    }

    // ---------------------------------------------------------------------
    // Restore (called from Awake instead of generating a new battle)
    // ---------------------------------------------------------------------

    private void RestoreFromSave(BattleSaveData data)
    {
        Debug.Log($"MapManager: restoring saved battle (round {data.Round}, {data.Tiles.Count} tiles, {data.Units.Count} units).");
        if (gameStateManager == null)
        {
            Debug.LogError("MapManager: GameStateManager is missing; cannot restore a save.");
            return;
        }

        gameStateManager.ApplySave(data);
        SaveSystem.Loaded = data;

        seed = data.Seed;
        width = data.Width;
        height = data.Height;
        roundsSinceTownCenterVillager = data.RoundsSinceTownCenterVillager;
        nextTownCenterIndex = data.NextTownCenterIndex;

        RestoreTiles(data);

        // Buildings first (they own their tiles), then the rest, heroes last so a hero can stand inside a building.
        foreach (UnitStateSave saved in data.Units)
            if (saved.Kind == SavedUnitKind.Structure) RestoreStructure(saved);
        foreach (UnitStateSave saved in data.Units)
        {
            switch (saved.Kind)
            {
                case SavedUnitKind.Enemy: RestoreEnemy(saved); break;
                case SavedUnitKind.Villager: RestoreVillager(saved); break;
                case SavedUnitKind.Worker: RestoreWorker(saved); break;
            }
        }

        UnitInstance firstHero = null;
        foreach (UnitStateSave saved in data.Units)
        {
            if (saved.Kind != SavedUnitKind.Hero) continue;
            UnitInstance hero = RestoreHero(saved);
            if (firstHero == null) firstHero = hero;
        }

        if (firstHero != null) StartCoroutine(PlayUnitSpawnFocusTour(firstHero));
    }

    private void RestoreTiles(BattleSaveData data)
    {
        ClearMap();
        foreach (TileSave saved in data.Tiles)
        {
            GameObject prefab = saved.PrefabIndex >= 0 && saved.PrefabIndex < tileGameObjectDatabase.Length
                ? tileGameObjectDatabase[saved.PrefabIndex] : null;
            if (prefab == null || !prefab.TryGetComponent(out HexTile prefabTile))
            {
                Debug.LogWarning($"MapManager: saved tile ({saved.X},{saved.Y}) has no usable prefab; skipped.");
                continue;
            }
            SpawnTile(new Vector2Int(saved.X, saved.Y), prefabTile.terrainType, prefab);
        }
        ConnectAllNeighbors();
        MarkExtractionPoints();

        var points = new List<HexTile>();
        foreach (TileSave saved in data.Tiles)
        {
            HexTile tile = GetTile(new Vector2Int(saved.X, saved.Y));
            if (tile == null) continue;

            if (saved.Revealed) tile.Reveal();
            for (int level = 0; level < saved.FortificationLevel; level++) BuildFortification(tile);
            if (saved.ExtractionSlots > 0)
            {
                tile.IsExtractionPoint = true;
                tile.ExtractionSlots = saved.ExtractionSlots;
                AddEvacuationMarker(tile);
                points.Add(tile);
            }
        }
        foreach (HexTile point in points) RefreshExtractableTiles(point);
    }

    private UnitData FindArchetype(string archetypeName)
    {
        if (string.IsNullOrEmpty(archetypeName)) return null;

        UnitData Find(IEnumerable<UnitData> list)
        {
            if (list == null) return null;
            foreach (UnitData candidate in list)
                if (candidate != null && candidate.name == archetypeName) return candidate;
            return null;
        }

        UnitData match = Find(AvailableEnemyArchetypes) ?? Find(villagerArchetypes) ?? Find(gameStateManager.AvailablePlayerArchetypes);
        if (match == null && workerArchetype != null && workerArchetype.name == archetypeName) match = workerArchetype;
        return match;
    }

    private static void ApplySavedStats(UnitInstance unit, UnitStateSave saved)
    {
        unit.maxHealth = saved.MaxHealth;
        unit.currentHealth = Mathf.Min(saved.CurrentHealth, saved.MaxHealth);
        unit.attackPower = saved.AttackPower;
        unit.attackRange = saved.AttackRange;
        unit.defensePower = saved.DefensePower;
        unit.movementRange = saved.MovementRange;
        unit.visibilityRange = saved.VisibilityRange;
        unit.maxActionsPerTurn = saved.MaxActions;
        unit.actionsRemaining = saved.ActionsRemaining;
        unit.NotifyStatsChanged();
    }

    private void RestoreStructure(UnitStateSave saved)
    {
        HexTile tile = GetTile(new Vector2Int(saved.X, saved.Y));
        if (tile == null) return;

        UnitInstance building = CreateStructure(saved.Building, tile);
        if (building == null) return;
        building.unitName = saved.UnitName;
        building.gameObject.name = saved.UnitName;
        building.maxHealth = saved.MaxHealth;
        building.currentHealth = saved.CurrentHealth;
        building.defensePower = saved.DefensePower;
        building.NotifyStatsChanged();
    }

    /// <summary>Builds a unit from its archetype with the loadout it was saved with, ready to place.</summary>
    private UnitInstance InstantiateSavedUnit(UnitStateSave saved, Vector3 position)
    {
        UnitData archetype = FindArchetype(saved.ArchetypeName);
        if (archetype == null || archetype.ModelPrefab == null)
        {
            Debug.LogWarning($"MapManager: saved unit '{saved.UnitName}' has unknown archetype '{saved.ArchetypeName}'; skipped.");
            return null;
        }

        GameObject obj = Instantiate(archetype.ModelPrefab, position, Quaternion.identity);
        if (!obj.TryGetComponent(out UnitInstance instance))
        {
            Destroy(obj);
            return null;
        }

        var unit = new Unit(archetype)
        {
            UnitName = saved.UnitName,
            ColorScheme = saved.ColorScheme,
            WeaponPrefab = Unit.FindPrefab(archetype.weaponPrefabs, saved.WeaponPrefabName),
            EquipmentPrefab = Unit.FindPrefab(archetype.equipmentPrefabs, saved.EquipmentPrefabName)
        };
        instance.Initialize(unit);
        instance.unitName = saved.UnitName;
        instance.gameObject.name = saved.UnitName;
        return instance;
    }

    private void RestoreEnemy(UnitStateSave saved)
    {
        HexTile tile = GetTile(new Vector2Int(saved.X, saved.Y));
        if (tile == null) return;

        UnitInstance enemy = InstantiateSavedUnit(saved, tile.transform.position);
        if (enemy == null) return;
        enemy.Faction = UnitFaction.Enemy;
        enemy.PlaceOnTile(tile);
        ApplySavedStats(enemy, saved);
        enemy.playSpawnAnimation();
    }

    private void RestoreVillager(UnitStateSave saved)
    {
        HexTile tile = GetTile(new Vector2Int(saved.X, saved.Y));
        if (tile == null) return;

        UnitInstance villager = InstantiateSavedUnit(saved, tile.transform.position);
        if (villager == null) return;
        villager.Faction = UnitFaction.Villager;
        villager.PlaceOnTile(tile);
        ApplySavedStats(villager, saved);
        villager.playSpawnAnimation();
    }

    private void RestoreWorker(UnitStateSave saved)
    {
        HexTile tile = GetTile(new Vector2Int(saved.X, saved.Y));
        if (tile == null) return;

        UnitInstance worker = InstantiateSavedUnit(saved, tile.transform.position);
        if (worker == null) return;
        worker.Faction = UnitFaction.Villager;
        worker.IsWorker = true;

        WorkerVillager state = worker.gameObject.AddComponent<WorkerVillager>();
        state.Setup(saved.IsFood);
        state.State = saved.State;
        state.Carried = saved.Carried;
        worker.PlaceOnTile(tile);
        ApplySavedStats(worker, saved);
        worker.playSpawnAnimation();

        if (!saved.HasSite) return;
        HexTile site = GetTile(new Vector2Int(saved.SiteX, saved.SiteY));
        if (site == null) { state.State = WorkerState.Seeking; return; }

        state.Site = site;
        site.workerSiteClaimed = true;
        // The farm / mill is raised when the worker starts gathering; before that the site is only claimed.
        if (saved.State != WorkerState.Seeking && saved.State != WorkerState.Building)
            state.Structure = BuildWorkerStructure(site, saved.IsFood);
    }

    private UnitInstance RestoreHero(UnitStateSave saved)
    {
        Unit record = gameStateManager.FullRoster.Find(u => u.InstanceID == saved.InstanceID);
        HexTile tile = GetTile(new Vector2Int(saved.X, saved.Y));
        if (record == null || record.Archetype == null || record.Archetype.ModelPrefab == null || tile == null)
        {
            Debug.LogWarning($"MapManager: saved hero '{saved.UnitName}' could not be restored.");
            return null;
        }

        GameObject prefab = record.Archetype.ModelPrefab;
        GameObject obj = Instantiate(prefab, tile.transform.position, prefab.transform.rotation);
        if (!obj.TryGetComponent(out UnitInstance hero))
        {
            Destroy(obj);
            return null;
        }

        hero.Initialize(record);
        hero.PlaceOnTile(tile);
        hero.currentHealth = Mathf.Min(saved.CurrentHealth, hero.maxHealth);
        hero.actionsRemaining = saved.ActionsRemaining;
        hero.playSpawnAnimation();

        foreach (CarriedSave carried in saved.CarriedVillagers)
        {
            UnitData data = FindArchetype(carried.ArchetypeName);
            if (data == null || data.ModelPrefab == null) continue;

            GameObject villagerObject = Instantiate(data.ModelPrefab, tile.transform.position, data.ModelPrefab.transform.rotation);
            if (!villagerObject.TryGetComponent(out UnitInstance villager)) { Destroy(villagerObject); continue; }

            villager.Initialize(data);
            villager.Faction = UnitFaction.Villager;
            villager.unitName = carried.UnitName;
            villager.gameObject.name = carried.UnitName;
            if (carried.IsWorker)
            {
                villager.IsWorker = true;
                villagerObject.AddComponent<WorkerVillager>().Setup(carried.IsFood);
            }
            villager.playSpawnAnimation();
            hero.AttachCarried(villager, data, carried.Slot);
        }

        hero.NotifyStatsChanged();
        return hero;
    }
}
