using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class CombatPhaseUIController : MonoBehaviour
{
    [Header("Unit Card Template")]
    [Tooltip("Assign UnitCard_Battle.uxml here - the compact card sized for the roster sidebar.")]
    [SerializeField] private VisualTreeAsset unitCardAsset;

    [Header("Extraction")]
    [Tooltip("Grid coordinate a unit must be adjacent to in order to Extract.")]
    [SerializeField] private Vector2Int extractionPoint = Vector2Int.zero;

    private UIDocument uiDocument;
    private ScrollView unitList;

    // Resource bar elements
    private Label townsfolkSavedLabel;
    private Label townsfolkSpawnedLabel;
    private Label unitCountLabel;
    private Label goldLabel;
    private Label roundLabel;
    private Label turnLabel;

    // Settlement ledger (read-only snapshot of GameStateManager.Settlement while fighting)
    private Label settlementUnitsLabel;
    private Label settlementVillagersLabel;
    private Label settlementFoodLabel;
    private Label settlementMaterialsLabel;
    private Label settlementMoraleLabel;

    // Action bar buttons
    private Button fortifyButton;
    private Button extractButton;
    private Button scoutButton;
    private Button buildButton;
    private Button nextUnitButton;
    private Button endTurnButton;

    private readonly List<UnitInstance> playerUnits = new List<UnitInstance>();
    private readonly Dictionary<UnitInstance, UnitCardView> cardViews = new Dictionary<UnitInstance, UnitCardView>();

    private UnitInstance selectedUnit;
    private bool roundEndedSignaled;
    private CombatManager combatManager;

    // ---------------------------------------------------------------
    // Public "control surface" - other scripts push data INTO the UI...
    // ---------------------------------------------------------------

    public void SetGold(int gold)
    {
        if (goldLabel != null) goldLabel.text = gold.ToString();
    }

    public void SetRound(int round)
    {
        if (roundLabel != null) roundLabel.text = $"{round:00}";
        // A fresh round means the "all units gone" condition can fire again.
        roundEndedSignaled = false;
    }

    public void SetTownsfolkSaved(int saved, int total)
    {
        if (townsfolkSavedLabel != null) townsfolkSavedLabel.text = $"{saved} / {total}";
    }

    public void SetTurnLabel(string text)
    {
        if (turnLabel != null) turnLabel.text = text;
    }

    // ---------------------------------------------------------------
    // ...and these events fire OUT of the UI so game logic can react.
    // ---------------------------------------------------------------

    /// <summary>Raised when an action button is pressed for the currently selected unit.</summary>
    public event Action<CombatAction, UnitInstance> OnActionRequested;

    /// <summary>Raised when a building is picked in the build menu.</summary>
    public event Action<BuildingType, UnitInstance> OnBuildingChosen;

    /// <summary>Raised when Back is pressed in the build menu.</summary>
    public event Action<UnitInstance> OnBuildCancelled;

    /// <summary>Raised when a unit card is selected.</summary>
    public event Action<UnitInstance> OnUnitSelected;

    /// <summary>Raised once, the moment every player unit is either dead or extracted.</summary>
    public event Action OnRoundEnded;

    public enum CombatAction
    {
        Fortify,
        Extract,
        Scout,
        Build,
        EndTurn,
        EscapeWorkers
    }

    private Button escapeWorkersButton;

    // Build menu: replaces the action buttons in the bottom bar while a hero is choosing what to build.
    private VisualElement actionButtonsRow;
    private VisualElement buildMenuRow;
    private Label actionHeadingLabel;
    private readonly Dictionary<BuildingType, Button> buildMenuButtons = new Dictionary<BuildingType, Button>();
    private static readonly BuildingType[] BuildMenuOrder =
        { BuildingType.TownCenter, BuildingType.Church, BuildingType.Wall, BuildingType.Barracks };

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
    }

    private void OnDisable()
    {
        ResearchManager.OnProgressChanged -= RefreshResearchHud;
        GameStateManager.OnSettlementChanged -= RefreshSettlementLedger;
        if (MapManager.Instance != null)
            MapManager.Instance.VillagerCountChanged -= HandleVillagerCountChanged;
    }

    private void Start()
    {
        combatManager = CombatManager.Instance;
        if (uiDocument == null || uiDocument.rootVisualElement == null) return;

        VisualElement root = uiDocument.rootVisualElement;

        // Resource bar
        townsfolkSavedLabel = root.Q<Label>("townsfolk-saved-value");
        townsfolkSpawnedLabel = root.Q<Label>("townsfolk-spawned-value");
        unitCountLabel = root.Q<Label>("unit-count-value");
        goldLabel = root.Q<Label>("gold-value");
        roundLabel = root.Q<Label>("round-value");
        turnLabel = root.Q<Label>("turn-label");

        // Settlement ledger
        settlementUnitsLabel = root.Q<Label>("settlement-units-value");
        settlementVillagersLabel = root.Q<Label>("settlement-villagers-value");
        settlementFoodLabel = root.Q<Label>("settlement-food-value");
        settlementMaterialsLabel = root.Q<Label>("settlement-materials-value");
        settlementMoraleLabel = root.Q<Label>("settlement-morale-value");

        // Action bar
        fortifyButton = root.Q<Button>("action-fortify");
        extractButton = root.Q<Button>("action-extract");
        scoutButton = root.Q<Button>("action-scout");
        buildButton = root.Q<Button>("action-build");
        endTurnButton = root.Q<Button>("end-turn");
        nextUnitButton = root.Q<Button>("action-next-unit");

        nextUnitButton?.RegisterCallback<ClickEvent>(_ => SelectNextReadyUnit());
        buildButton?.RegisterCallback<ClickEvent>(_ => RaiseAction(CombatAction.Build));
        fortifyButton?.RegisterCallback<ClickEvent>(_ => RaiseAction(CombatAction.Fortify));
        extractButton?.RegisterCallback<ClickEvent>(_ => RaiseAction(CombatAction.Extract));
        scoutButton?.RegisterCallback<ClickEvent>(_ => RaiseAction(CombatAction.Scout));
        endTurnButton?.RegisterCallback<ClickEvent>(_ => RaiseAction(CombatAction.EndTurn));

        // Worker escape button: built in code and placed beside End Turn, so no UXML edit is needed.
        // Only shown when the battle actually has worker villagers.
        if (endTurnButton != null && endTurnButton.parent != null)
        {
            escapeWorkersButton = new Button { name = "action-escape-workers", text = "Workers Escape" };
            foreach (string cls in endTurnButton.GetClasses())
                escapeWorkersButton.AddToClassList(cls);
            escapeWorkersButton.RegisterCallback<ClickEvent>(_ =>
            {
                OnActionRequested?.Invoke(CombatAction.EscapeWorkers, null);
                RefreshWorkerEscapeButton();
            });
            int endIndex = endTurnButton.parent.IndexOf(endTurnButton);
            endTurnButton.parent.Insert(endIndex, escapeWorkersButton);
            RefreshWorkerEscapeButton();
        }

        // Settings (scroll speed / volume) beside End Turn; styled like the other action buttons.
        if (endTurnButton != null && endTurnButton.parent != null)
        {
            var settingsButton = SettingsPanel.Attach(root, endTurnButton.parent, endTurnButton.parent.IndexOf(endTurnButton));
            if (settingsButton != null)
                foreach (string cls in endTurnButton.GetClasses())
                    settingsButton.AddToClassList(cls);
        }

        unitList = root.Q<ScrollView>("unit-list");
        BuildBuildMenu(root);

        // End Turn doesn't depend on a selected unit; the rest start disabled.
        endTurnButton?.SetEnabled(true);
        RefreshActionButtonStates();

        RefreshRoster();
        if (MapManager.Instance != null)
            MapManager.Instance.VillagerCountChanged += HandleVillagerCountChanged;
        RefreshTownsfolkDisplay();
        SetRound(combatManager.currentRound);
        RefreshSettlementLedger();

        BuildResearchHud(root);
        if (GetComponent<GameOverPanel>() == null) gameObject.AddComponent<GameOverPanel>();

        // Repaint the top bar the moment workers deposit food/materials.
        GameStateManager.OnSettlementChanged -= RefreshSettlementLedger;
        GameStateManager.OnSettlementChanged += RefreshSettlementLedger;
    }

    /// <summary>
    /// Read-only snapshot of the settlement economy (GameStateManager.Settlement)
    /// so the player can see what they're fighting for without leaving combat.
    /// Re-pulled after every action and whenever GameStateManager.OnSettlementChanged
    /// fires (deposits, spending, morale, escapes).
    /// </summary>
    public void RefreshSettlementLedger()
    {
        GameStateManager gsm = GameStateManager.Instance;
        if (gsm == null) return;

        GameStateManager.SettlementResources s = gsm.Settlement;
        RefreshWorkerEscapeButton();

        if (settlementUnitsLabel != null) settlementUnitsLabel.text = $"{gsm.FullRoster.Count}/{GameStateManager.MaxHeroes}";
        if (settlementVillagersLabel != null) settlementVillagersLabel.text = $"{gsm.VillagersEscapedThisBattle}/{gsm.Balance.VillagersToWin}";
        if (settlementFoodLabel != null) settlementFoodLabel.text = s.Food.ToString();
        if (settlementMaterialsLabel != null) settlementMaterialsLabel.text = s.Materials.ToString();
        if (settlementMoraleLabel != null) settlementMoraleLabel.text = $"{s.Morale}%";

        // Pulse + "+N" float when a resource goes up (skipped on the first paint).
        if (lastFood >= 0 && s.Food > lastFood)
            PlayGainAnimation("food", settlementFoodLabel, s.Food - lastFood);
        if (lastMaterials >= 0 && s.Materials > lastMaterials)
            PlayGainAnimation("materials", settlementMaterialsLabel, s.Materials - lastMaterials);
        lastFood = s.Food;
        lastMaterials = s.Materials;
    }

    // ---------------------------------------------------------------
    // Research HUD: up to ResearchManager.MaxActiveResearch projects, paid by kills.
    // Built in code and inserted before the round block, so the UXML needs no edit.
    // ---------------------------------------------------------------

    private struct ResearchSlotView
    {
        public VisualElement Root, Fill;
        public Label Name, Count;
    }

    private ResearchSlotView[] researchSlots;

    private void BuildResearchHud(VisualElement root)
    {
        VisualElement bar = root.Q<VisualElement>("resource-bar");
        if (bar == null) return;

        var block = new VisualElement { name = "research-block" };
        block.AddToClassList("hud-block");
        block.AddToClassList("research-block");

        researchSlots = new ResearchSlotView[ResearchManager.MaxActiveResearch];
        for (int i = 0; i < researchSlots.Length; i++)
        {
            var slot = new VisualElement();
            slot.AddToClassList("research-slot");
            var name = new Label();
            name.AddToClassList("research-slot__name");
            var track = new VisualElement();
            track.AddToClassList("research-slot__track");
            var fill = new VisualElement();
            fill.AddToClassList("research-slot__fill");
            track.Add(fill);
            var count = new Label();
            count.AddToClassList("research-slot__count");
            slot.Add(name);
            slot.Add(track);
            slot.Add(count);
            block.Add(slot);
            researchSlots[i] = new ResearchSlotView { Root = slot, Fill = fill, Name = name, Count = count };
        }

        var separator = new VisualElement();
        separator.AddToClassList("hud-separator");
        block.Add(separator);

        VisualElement round = bar.Q<VisualElement>("round-block");
        if (round != null) bar.Insert(bar.IndexOf(round), block);
        else bar.Add(block);

        ResearchManager.OnProgressChanged -= RefreshResearchHud;
        ResearchManager.OnProgressChanged += RefreshResearchHud;
        RefreshResearchHud();
    }

    public void RefreshResearchHud()
    {
        if (researchSlots == null) return;
        ResearchManager research = ResearchManager.Current;

        // Finished mid-battle: toast it here (the Research tab would otherwise announce it later).
        if (research.RecentlyCompleted.Count > 0)
        {
            var names = new List<string>();
            foreach (ResearchData done in research.RecentlyCompleted) names.Add(done.DisplayName);
            research.RecentlyCompleted.Clear();
            FindAnyObjectByType<NotificationManager>()?.ShowNotification($"Research complete: {string.Join(", ", names)}");
        }

        for (int i = 0; i < researchSlots.Length; i++)
        {
            ResearchSlotView view = researchSlots[i];
            ResearchProgress progress = i < research.Active.Count ? research.Active[i] : null;
            ResearchData data = progress != null ? research.FindData(progress.Id) : null;

            view.Root.EnableInClassList("research-slot--empty", data == null);
            if (data == null)
            {
                view.Name.text = "EMPTY";
                view.Count.text = "-";
                view.Fill.style.width = Length.Percent(0);
                continue;
            }

            int done = research.KillsDone(progress, data);
            view.Name.text = string.IsNullOrEmpty(data.DisplayName) ? data.name : data.DisplayName;
            view.Count.text = $"{done}/{data.KillsToComplete}";
            view.Fill.style.width = Length.Percent(100f * done / data.KillsToComplete);
        }
    }

    private int lastFood = -1;
    private int lastMaterials = -1;

    /// <summary>Glow + icon pop + value flash + rising "+N" on the matching top-HUD block.</summary>
    private void PlayGainAnimation(string key, Label valueLabel, int amount)
    {
        if (uiDocument == null || uiDocument.rootVisualElement == null) return;
        VisualElement root = uiDocument.rootVisualElement;
        VisualElement block = root.Q<VisualElement>($"{key}-block");
        VisualElement icon = root.Q<VisualElement>($"{key}-icon");
        VisualElement glow = root.Q<VisualElement>($"{key}-glow");
        if (block == null) return;

        glow?.AddToClassList("hud-glow--active");
        icon?.AddToClassList("hud-icon--gain");
        valueLabel?.AddToClassList("value-gain");

        // Let the transition back out after the pop.
        block.schedule.Execute(() =>
        {
            glow?.RemoveFromClassList("hud-glow--active");
            icon?.RemoveFromClassList("hud-icon--gain");
            valueLabel?.RemoveFromClassList("value-gain");
        }).StartingIn(180);

        var floater = new Label($"+{amount}") { pickingMode = PickingMode.Ignore };
        floater.AddToClassList("gain-float");
        floater.AddToClassList($"gain-float--{key}");
        block.Add(floater);
        // Add the end-state class a tick later so the transition actually runs.
        floater.schedule.Execute(() => floater.AddToClassList("gain-float--rise")).StartingIn(30);
        floater.schedule.Execute(() => floater.RemoveFromHierarchy()).StartingIn(1000);
    }

    private void HandleVillagerCountChanged(int spawned, int target)
    {
        SetTownsfolkSpawned(spawned, target);
        SetTownsfolkSaved(CombatManager.Instance != null ? CombatManager.Instance.GetVillagerCount() : 0, target);
    }

    public void RefreshTownsfolkDisplay()
    {
        if (MapManager.Instance == null || GameStateManager.Instance == null)
            return;

        int spawned = MapManager.Instance.GetVillagersSpawned();
        int target = MapManager.Instance.GetVillagerSpawnTarget();
        SetTownsfolkSpawned(spawned, target);
        SetTownsfolkSaved(CombatManager.Instance != null ? CombatManager.Instance.GetVillagerCount() : 0, target);
    }

    private void SetTownsfolkSpawned(int spawned, int target)
    {
        if (townsfolkSpawnedLabel != null)
            townsfolkSpawnedLabel.text = $"{spawned} / {target}";
    }

    private static bool IsActivePlayerUnit(UnitInstance unit)
    {
        return unit != null && unit.Faction == UnitFaction.Player && !unit.IsDead && !unit.IsExtracted;
    }

    private void RefreshRoster()
    {
        if (unitList == null) return;

        unitList.Clear();
        playerUnits.Clear();
        cardViews.Clear();

        UnitInstance[] units = FindObjectsByType<UnitInstance>();
        foreach (UnitInstance unit in units)
        {
            if (!IsActivePlayerUnit(unit)) continue;

            playerUnits.Add(unit);
            UnitCardView cardView = new UnitCardView(unit, unitCardAsset);
            cardView.Root.RegisterCallback<ClickEvent>(_ =>
            {
                // Clicking the selected card again deselects it.
                if (selectedUnit == unit)
                {
                    if (combatManager != null) combatManager.DeselectUnit();
                    else SelectUnit(null);
                    return;
                }
                SelectUnit(unit);
            });
            cardViews.Add(unit, cardView);
            unitList.Add(cardView.Root);
        }

        if (unitCountLabel != null)
            unitCountLabel.text = $"{playerUnits.Count} UNIT{(playerUnits.Count == 1 ? "" : "S")}";

        // If the previously selected unit died / extracted, clear the selection.
        if (selectedUnit != null && !playerUnits.Contains(selectedUnit))
        {
            selectedUnit = null;
        }

        CheckForRoundEnd();
    }

    private void CheckForRoundEnd()
    {
        if (playerUnits.Count == 0 && !roundEndedSignaled)
        {
            roundEndedSignaled = true;
            OnRoundEnded?.Invoke();
        }
    }

    public void SelectUnit(UnitInstance unit)
    {
        Debug.Log($"CombatPhaseUIController: SelectUnit called with {unit?.unitName ?? "null"}");
        selectedUnit = unit;
        ShowBuildMenu(false);

        foreach (KeyValuePair<UnitInstance, UnitCardView> kvp in cardViews)
            kvp.Value.SetSelected(kvp.Key == unit);

        RefreshActionButtonStates();

        OnUnitSelected?.Invoke(unit);
    }

    /// <summary>
    /// Selects the next player unit (after the current one, wrapping) that still has actions,
    /// highlights its moves via CombatManager, and pans the camera to it.
    /// </summary>
    public void SelectNextReadyUnit()
    {
        int start = selectedUnit != null ? playerUnits.IndexOf(selectedUnit) : -1;
        for (int i = 1; i <= playerUnits.Count; i++)
        {
            UnitInstance candidate = playerUnits[(start + i + playerUnits.Count) % playerUnits.Count];
            if (!IsActivePlayerUnit(candidate) || candidate.actionsRemaining <= 0) continue;

            if (combatManager != null) combatManager.SelectUnit(candidate);
            SelectUnit(candidate);
            if (CameraController.Instance != null) CameraController.Instance.PanToUnit(candidate.transform);
            return;
        }

        if (combatManager != null && combatManager.notificationManager != null)
            combatManager.notificationManager.ShowNotification("No units have actions left.");
    }

    private void RefreshActionButtonStates()
    {
        UnitInstance unit = selectedUnit;
        bool hasSelection = unit != null;
        bool hasActions = hasSelection && unit.actionsRemaining > 0;

        // Rule 1: If they have no actions (or no unit selected), all action buttons are disabled
        if (!hasActions)
        {
            fortifyButton?.SetEnabled(false);
            extractButton?.SetEnabled(false);
            scoutButton?.SetEnabled(false);
            buildButton?.SetEnabled(false);
            return;
        }

        // Rule 2: only villagers are extracted, so Extract (Drop Off) needs a carried villager and a spot next to an extraction point
        bool isAdjacentToOrigin = unit.currentTile != null &&
            (unit.currentTile.IsExtractable ||
             (MapManager.Instance != null && MapManager.Instance.IsAdjacentToBoat(unit.currentTile.gridPosition)));
        bool carryingVillager = unit.rescuedUnitData != null && Array.Exists(unit.rescuedUnitData, v => v != null);
        extractButton?.SetEnabled(isAdjacentToOrigin && carryingVillager);
        if (extractButton != null) extractButton.text = "DROP OFF";

        // Rule 3: Fortify can be stacked up to the max level, then it is disabled
        bool maxFortified = !unit.CanFortifyFurther;

        // Scout can be used if they have actions remaining
        scoutButton?.SetEnabled(true);

        // Build needs a free adjacent tile; each building's cost and prerequisites are checked in the build menu.
        MapManager map = MapManager.Instance;
        GameStateManager gsm = GameStateManager.Instance;
        bool canFortify = !maxFortified && map != null && gsm != null &&
                          gsm.Settlement.Materials >= map.fortifyMaterialCost;
        fortifyButton?.SetEnabled(canFortify);
        bool canBuild = map != null && map.GetBuildableTiles(unit).Count > 0;
        buildButton?.SetEnabled(canBuild);
    }

    // ---------------------------------------------------------------
    // Build menu (bottom bar). Built in code beside the action buttons.
    // ---------------------------------------------------------------

    private void BuildBuildMenu(VisualElement root)
    {
        VisualElement bar = root.Q<VisualElement>("action-bar");
        if (bar == null) return;

        actionButtonsRow = bar.Q<VisualElement>(className: "action-buttons");
        actionHeadingLabel = bar.Q<Label>(className: "panel-title");
        if (actionButtonsRow == null) return;

        buildMenuRow = new VisualElement { name = "build-menu" };
        buildMenuRow.AddToClassList("action-buttons");
        buildMenuRow.style.display = DisplayStyle.None;

        foreach (BuildingType type in BuildMenuOrder)
        {
            BuildingType captured = type;
            var button = new Button { name = $"build-{type}" };
            button.AddToClassList("action-button");
            button.style.height = 60;
            button.style.whiteSpace = WhiteSpace.Normal;
            button.RegisterCallback<ClickEvent>(_ =>
            {
                OnBuildingChosen?.Invoke(captured, selectedUnit);
                MarkChosenBuilding(captured);
                RefreshBuildMenuStates();
            });
            buildMenuButtons[type] = button;
            buildMenuRow.Add(button);
        }

        var back = new Button { name = "build-back", text = "BACK" };
        back.AddToClassList("action-button");
        back.style.height = 60;
        back.RegisterCallback<ClickEvent>(_ => OnBuildCancelled?.Invoke(selectedUnit));
        buildMenuRow.Add(back);

        actionButtonsRow.parent.Insert(actionButtonsRow.parent.IndexOf(actionButtonsRow) + 1, buildMenuRow);
    }

    /// <summary>Swaps the bottom bar between the normal actions and the build menu.</summary>
    public void ShowBuildMenu(bool show)
    {
        if (buildMenuRow == null || actionButtonsRow == null) return;

        buildMenuRow.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        actionButtonsRow.style.display = show ? DisplayStyle.None : DisplayStyle.Flex;
        if (actionHeadingLabel != null)
            actionHeadingLabel.text = show ? "BUILD - PICK A BUILDING, THEN A HIGHLIGHTED TILE" : "AVAILABLE ACTIONS";

        if (show)
        {
            MarkChosenBuilding(null);
            RefreshBuildMenuStates();
        }
    }

    /// <summary>Labels each building with its cost and greys out the ones that can't be built yet, with the reason as a tooltip.</summary>
    private void RefreshBuildMenuStates()
    {
        MapManager map = MapManager.Instance;
        if (map == null) return;

        foreach (KeyValuePair<BuildingType, Button> entry in buildMenuButtons)
        {
            BuildingSpec spec = map.GetBuildingSpec(entry.Key);
            string cost = spec.FoodCost > 0 ? $"{spec.MaterialCost} MAT  {spec.FoodCost} FOOD" : $"{spec.MaterialCost} MAT";
            bool ok = map.CanBuild(entry.Key, out string reason);
            entry.Value.text = $"{spec.DisplayName.ToUpperInvariant()}\n{cost}";
            entry.Value.tooltip = ok ? "" : reason;
            entry.Value.SetEnabled(ok);
        }
    }

    private void MarkChosenBuilding(BuildingType? chosen)
    {
        foreach (KeyValuePair<BuildingType, Button> entry in buildMenuButtons)
        {
            bool isChosen = chosen.HasValue && entry.Key == chosen.Value;
            Color border = isChosen ? new Color(0f, 0.95f, 1f) : new Color(0.29f, 0.32f, 0.36f);
            entry.Value.style.borderTopColor = entry.Value.style.borderBottomColor = border;
            entry.Value.style.borderLeftColor = entry.Value.style.borderRightColor = border;
            float width = isChosen ? 3 : 1;
            entry.Value.style.borderTopWidth = entry.Value.style.borderBottomWidth = width;
            entry.Value.style.borderLeftWidth = entry.Value.style.borderRightWidth = width;
        }
    }

    /// <summary>Shows the Escape button only while worker villagers are on the field and haven't been told to flee yet.</summary>
    public void RefreshWorkerEscapeButton()
    {
        if (escapeWorkersButton == null) return;
        bool show = combatManager != null && combatManager.HasActiveWorkers && !combatManager.WorkersEscaping;
        escapeWorkersButton.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void RaiseAction(CombatAction action)
    {
        if (action != CombatAction.EndTurn && selectedUnit == null) return;
        OnActionRequested?.Invoke(action, selectedUnit);
        RefreshActionButtonStates();
        RefreshRoster();
        RefreshTownsfolkDisplay();
        RefreshSettlementLedger();
    }
    private sealed class UnitCardView
    {
        public VisualElement Root { get; }

        private readonly UnitInstance unit;
        private readonly VisualElement cardRoot;
        private readonly VisualElement portrait;
        private readonly Label nameLabel;
        private readonly Label tagLabel;
        private readonly Label hpValueLabel;
        private readonly VisualElement healthFill;
        private readonly Label actionsValueLabel;
        private readonly Label rangeValueLabel;
        private readonly Label moveValueLabel;
        private readonly Label badgeLabel;

        public UnitCardView(UnitInstance unit, VisualTreeAsset cardAsset)
        {
            this.unit = unit;

            if (cardAsset != null)
            {
                // Keep Root pointing at the TemplateContainer Instantiate() returns rather
                // than drilling into "card-root" - the <Style src> stylesheet declared in
                // the UXML is attached to that outer container, not to card-root itself, so
                // replacing Root with just card-root (as this used to do) would silently
                // strip the card's styling once it's parented into the roster ScrollView.
                Root = cardAsset.Instantiate();
                cardRoot = Root.Q<VisualElement>("card-root") ?? Root;
            }
            else
            {
                Root = new VisualElement();
                Root.AddToClassList("combat-unit-card");
                cardRoot = Root;
                Debug.LogWarning("CombatPhaseUIController: unitCardAsset not assigned, falling back to a blank card.");
            }

            portrait = Root.Q<VisualElement>("card-portrait");
            nameLabel = Root.Q<Label>("card-name");
            tagLabel = Root.Q<Label>("card-tag");
            hpValueLabel = Root.Q<Label>("card-hp-value");
            healthFill = Root.Q<VisualElement>("card-health-fill");
            actionsValueLabel = Root.Q<Label>("card-actions-value");
            rangeValueLabel = Root.Q<Label>("card-range-value");
            moveValueLabel = Root.Q<Label>("card-move-value");
            badgeLabel = Root.Q<Label>("card-badge");

            cardRoot.AddToClassList(UnitRarityTable.GetUssClass(unit.Rarity));

            Refresh();
        }

        public void Refresh()
        {
            if (portrait != null && unit.PersistentUnit?.UnitIcon != null)
                portrait.style.backgroundImage = new StyleBackground(unit.PersistentUnit.UnitIcon);

            if (nameLabel != null) nameLabel.text = unit.unitName.ToUpperInvariant();

            if (tagLabel != null)
            {
                string role = unit.attackRange > 1 ? "RANGED" : "FRONTLINE";
                if (unit.Rarity != UnitRarity.Common)
                    role = $"{UnitRarityTable.GetDisplayName(unit.Rarity)} · {role}";
                tagLabel.text = unit.Wounds > 0 ? $"{role} · {unit.Wounds} WOUND{(unit.Wounds == 1 ? "" : "S")}" : role;
            }

            if (hpValueLabel != null) hpValueLabel.text = $"{unit.currentHealth} / {unit.maxHealth}";
            if (healthFill != null)
            {
                int maxHealth = Mathf.Max(1, unit.maxHealth);
                float pct = 100f * Mathf.Clamp(unit.currentHealth, 0, maxHealth) / maxHealth;
                healthFill.style.width = new Length(pct, LengthUnit.Percent);
            }

            if (actionsValueLabel != null) actionsValueLabel.text = $"{unit.actionsRemaining}/{unit.maxActionsPerTurn}";
            if (rangeValueLabel != null) rangeValueLabel.text = unit.attackRange.ToString();
            if (moveValueLabel != null) moveValueLabel.text = unit.movementRange.ToString();

            if (badgeLabel != null)
            {
                badgeLabel.text = unit.actionsRemaining <= 0 ? "OUT" : "";
                badgeLabel.style.display = unit.actionsRemaining <= 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            cardRoot.EnableInClassList("unit-card-exhausted", unit.actionsRemaining <= 0);
        }

        public void SetSelected(bool selected)
        {
            cardRoot.EnableInClassList("unit-card-selected", selected);
        }
    }
}
