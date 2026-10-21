# Future Idea: Town Management in the Decision Phase

Status: brainstorm only, nothing here is implemented.

## The problem

Battle is fun. The Decision phase is mostly spreadsheet math (allocation sliders, forecast, rest/travel). The player's choices there don't *feel* like they show up on the hex map, and what happens on the map doesn't visibly change the town.

**Goal:** every building/ship/upgrade the player buys should be something they can *see, touch, and be saved by* in the next battle, and every battle result should visibly mark the town.

**Design rule:** a purchase is only meaningful if the player can answer "what will this do for me on the map?" in one sentence.

## What already exists we can build on

- Settlement resources: Food, Materials, Morale, Villagers, `VillagerCapacity`, `UnitCapacity`, `ExtraBoats` (ships dock beside the starting boat in battle).
- Cycle directives: Vanguard / Scavenge / Harvest / Expansion, plus `ComputeForecast`/`ExecuteCycle`.
- In-battle: Fortify, Build Wall (`MapManager.BuildWall`), workers gathering at farms and lumber mills, rescue + exfil, reinforcements.
- Post-battle: XP, level-ups, fallen records / memorial, morale from kills/rescues/casualties.
- Evacuation clock (`EvacuationCyclesRemaining`) and voyage between stages.

## Core idea: the town is your loadout

Buildings you own in the Decision phase are *brought into* the next battle as concrete things: starting structures, abilities, or map-wide rules. Buying is "choosing your battle kit for the campaign."

---

## Idea list

### A. Buildings that change the battle map

| Building | Decision-phase effect | What the player sees in battle |
|---|---|---|
| **Watchtower** | +vision range for the squad | Units start with a larger revealed radius; Scout is cheaper or free |
| **Barracks** | +`UnitCapacity`, faster rest healing | More units to draft; wounded return at higher HP |
| **Infirmary / Chapel** | Heals between battles, lowers casualty morale hit | Fewer permadeaths feel like losses; "revive once" for a unit |
| **Armory / Forge** | Unlocks weapon/armor tiers for `ItemData` | Units show new gear; +attack/defense stat lines |
| **Granary** | Food cap and spoilage protection | Food deltas get better; fewer starvation events |
| **Workshop / Sawmill** | Materials income, cheaper walls | Wall/Fortify cost drops; workers carry more |
| **Stables / Roadworks** | +movement | Units start with +1 move on first round |
| **Signal Fire / Harbor Beacon** | Villagers spawn closer to exfil | Rescue distances shrink |

### B. Ships (the evacuation theme)

Ships are already "+capacity". Give them *jobs*:

- **Transport:** more villager/unit capacity (current behavior).
- **Warship:** bombards a hex line once per battle from the shoreline.
- **Supply ship:** drops a crate of Materials mid-battle (a reinforcement for the *player*).
- **Hospital ship:** extracted wounded units heal on the voyage.
- **Scout ship:** reveals a map region before the battle starts.

Idea: each docked ship is a visible object on the map edge, and losing one in battle has consequences.

### C. Town as an emotional/narrative layer

- **Named villagers with jobs** (smith, healer, cook). Rescue the *right* villager and a building gets better. Losing one has a face.
- **Memorial grows**: fallen unit records already exist; a built memorial gives a morale buff scaled to dead heroes.
- **Town reacts to the last battle**: after a bad loss, empty beds, quiet streets; after a win, celebration (cheap visual state).
- **Morale thresholds** unlock/lock options: high morale = festival (free action), low morale = desertion.

### D. Decisions with real trade-offs

- **Limited build slots.** The town has N plots. Barracks *or* Granary, forces identity.
- **Upkeep.** Buildings cost Food/Materials per cycle. Big town = hungry town.
- **Defend the town.** Some stages are a siege: the buildings you bought are literally on the battle map and enemies attack them.
- **Risk vs reward directives.** Sending villagers to Scavenge brings Materials but a captured villager means a building runs short-handed.

### E. Meta / progression hooks

- **Tech tree light:** 3 branches (Defense, Logistics, Medicine) with one new building each tier.
- **Prestige "Founding Charter" choices** at campaign start (e.g. *Fishing Village*: cheap ships; *Garrison Town*: cheap units).
- **Blueprints as stage rewards:** AfterAction can award a blueprint for a building you can't normally buy.

---

## Mapping buildings -> existing systems (cheap first steps)

1. **Add a `BuildingData` ScriptableObject** (name, cost, upkeep, effect type, icon, prefab) mirroring `UnitData`.
2. **`SettlementResources.Buildings`** list (+ `SaveData`, bump `SaveSystem.CurrentVersion`).
3. **Store tab** in `PhaseShellController` (already a placeholder tab) lists purchasable buildings/ships.
4. **Effects via modifiers:** add a `SettlementModifiers` struct computed from owned buildings (vision bonus, move bonus, wall cost, heal rate). `MapManager`/`CombatManager`/`UnitInstance` read it at battle start.
5. **Battle visualization:** `MapManager` places a small decorative prop per building near the player's start/exfil so the town is physically present.
6. **AfterAction**: show "Your Watchtower saved 2 units" style callouts so the connection is explicit.

## Suggested MVP (smallest thing that makes it feel connected)

1. Store tab with 4 buildings: **Watchtower** (vision), **Infirmary** (heal), **Workshop** (cheaper walls), **Barracks** (+unit cap).
2. Each building has one numeric modifier read in battle.
3. Prop spawned on map for each owned building.
4. AfterAction line crediting buildings that mattered.

## Open questions

- Do buildings persist forever, or can battles damage/destroy them?
- Is the town one fixed place, or does it move with the evacuation voyage (buildings on ships)?
- Gold vs Materials: which currency buys which category? (Gold = units/ships, Materials = buildings?)
- How many build slots, and does expanding slots tie into the `Expansion` directive?
- Should the player see the town as a 2D screen, or a small playable hex "home" map?
