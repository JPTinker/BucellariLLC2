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
        if (roundLabel != null) roundLabel.text = round.ToString("00");
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

    /// <summary>Raised when a unit card is selected.</summary>
    public event Action<UnitInstance> OnUnitSelected;

    /// <summary>Raised once, the moment every player unit is either dead or extracted.</summary>
    public event Action OnRoundEnded;

    public enum CombatAction
    {
        Fortify,
        Extract,
        Scout,
        EndTurn
    }

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
    }

    private void OnDisable()
    {
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
        endTurnButton = root.Q<Button>("end-turn");

        fortifyButton?.RegisterCallback<ClickEvent>(_ => RaiseAction(CombatAction.Fortify));
        extractButton?.RegisterCallback<ClickEvent>(_ => RaiseAction(CombatAction.Extract));
        scoutButton?.RegisterCallback<ClickEvent>(_ => RaiseAction(CombatAction.Scout));
        endTurnButton?.RegisterCallback<ClickEvent>(_ => RaiseAction(CombatAction.EndTurn));

        unitList = root.Q<ScrollView>("unit-list");

        // End Turn doesn't depend on a selected unit; the rest start disabled.
        endTurnButton?.SetEnabled(true);
        RefreshActionButtonStates();

        RefreshRoster();
        if (MapManager.Instance != null)
            MapManager.Instance.VillagerCountChanged += HandleVillagerCountChanged;
        RefreshTownsfolkDisplay();
        SetRound(combatManager.currentRound);
        SetGold(GameStateManager.Instance.Resources.Gold);
        RefreshSettlementLedger();
    }

    /// <summary>
    /// Read-only snapshot of the settlement economy (GameStateManager.Settlement)
    /// so the player can see what they're fighting for without leaving combat.
    /// Food/Materials/Morale only change via ExecuteCycle/ApplyPostBattleResults
    /// (neither runs mid-battle), but Units can drop mid-fight via casualties, so
    /// this is re-pulled after every action alongside the roster/townsfolk panels.
    /// </summary>
    private void RefreshSettlementLedger()
    {
        GameStateManager gsm = GameStateManager.Instance;
        if (gsm == null) return;

        GameStateManager.SettlementResources s = gsm.Settlement;

        if (settlementUnitsLabel != null) settlementUnitsLabel.text = $"{gsm.Population}/{s.UnitCapacity}";
        if (settlementVillagersLabel != null) settlementVillagersLabel.text = $"{s.Villagers}/{s.VillagerCapacity}";
        if (settlementFoodLabel != null) settlementFoodLabel.text = s.Food.ToString();
        if (settlementMaterialsLabel != null) settlementMaterialsLabel.text = s.Materials.ToString();
        if (settlementMoraleLabel != null) settlementMoraleLabel.text = $"{s.Morale}%";
    }

    private void HandleVillagerCountChanged(int spawned, int target)
    {
        SetTownsfolkSpawned(spawned, target);
        SetTownsfolkSaved(GameStateManager.Instance.SavedVillagersThisBattle, target);
    }

    public void RefreshTownsfolkDisplay()
    {
        if (MapManager.Instance == null || GameStateManager.Instance == null)
            return;

        int spawned = MapManager.Instance.GetVillagersSpawned();
        int target = MapManager.Instance.GetVillagerSpawnTarget();
        SetTownsfolkSpawned(spawned, target);
        SetTownsfolkSaved(GameStateManager.Instance.SavedVillagersThisBattle, target);
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
            cardView.Root.RegisterCallback<ClickEvent>(_ => SelectUnit(unit));
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

        foreach (KeyValuePair<UnitInstance, UnitCardView> kvp in cardViews)
            kvp.Value.SetSelected(kvp.Key == unit);

        RefreshActionButtonStates();

        OnUnitSelected?.Invoke(unit);
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
            return;
        }

        // Rule 2: If they are not adjacent to (0,0) then extract is disabled
        bool isAdjacentToOrigin = false;
        if (unit.currentTile != null)
        {
            Vector2Int pos = unit.currentTile.gridPosition;
            int dx = Mathf.Abs(pos.x);
            int dy = Mathf.Abs(pos.y);
            isAdjacentToOrigin = (Mathf.Max(dx, dy) == 1);
        }
        extractButton?.SetEnabled(isAdjacentToOrigin);

        // Rule 3: If the unit is already fortified then fortify is disabled
        // (Change 'isFortified' to 'IsFortified' if your property casing differs)
        bool isFortified = unit.IsFortified; 
        fortifyButton?.SetEnabled(!isFortified);

        // Scout can be used if they have actions remaining
        scoutButton?.SetEnabled(true);
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