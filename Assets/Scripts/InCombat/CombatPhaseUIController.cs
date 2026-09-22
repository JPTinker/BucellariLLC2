using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class CombatPhaseUIController : MonoBehaviour
{
    [Header("Unit Card Template")]
    [Tooltip("Assign the unit-card.uxml VisualTreeAsset here.")]
    [SerializeField] private VisualTreeAsset unitCardAsset;

    [Header("Extraction")]
    [Tooltip("Grid coordinate a unit must be adjacent to in order to Extract.")]
    [SerializeField] private Vector2Int extractionPoint = Vector2Int.zero;

    private UIDocument uiDocument;
    private ScrollView unitList;

    // Resource bar elements
    private Label townsfolkSavedLabel;
    private Label unitCountLabel;
    private Label goldLabel;
    private Label roundLabel;
    private Label turnLabel;

    // Action bar buttons
    private Button healButton;
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
        Heal,
        Fortify,
        Extract,
        Scout,
        EndTurn
    }

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
    }

    private void Start()
    {
        combatManager = CombatManager.Instance;
        if (uiDocument == null || uiDocument.rootVisualElement == null) return;

        VisualElement root = uiDocument.rootVisualElement;

        // Resource bar
        townsfolkSavedLabel = root.Q<Label>("townsfolk-saved-value");
        unitCountLabel = root.Q<Label>("unit-count-value");
        goldLabel = root.Q<Label>("gold-value");
        roundLabel = root.Q<Label>("round-value");
        turnLabel = root.Q<Label>("turn-label");

        // Action bar
        healButton = root.Q<Button>("action-heal");
        fortifyButton = root.Q<Button>("action-fortify");
        extractButton = root.Q<Button>("action-extract");
        scoutButton = root.Q<Button>("action-scout");
        endTurnButton = root.Q<Button>("end-turn");

        healButton?.RegisterCallback<ClickEvent>(_ => RaiseAction(CombatAction.Heal));
        fortifyButton?.RegisterCallback<ClickEvent>(_ => RaiseAction(CombatAction.Fortify));
        extractButton?.RegisterCallback<ClickEvent>(_ => RaiseAction(CombatAction.Extract));
        scoutButton?.RegisterCallback<ClickEvent>(_ => RaiseAction(CombatAction.Scout));
        endTurnButton?.RegisterCallback<ClickEvent>(_ => RaiseAction(CombatAction.EndTurn));

        unitList = root.Q<ScrollView>("unit-list");

        // End Turn doesn't depend on a selected unit; the rest start disabled.
        endTurnButton?.SetEnabled(true);
        RefreshActionButtonStates();

        RefreshRoster();
        SetTownsfolkSaved(0, combatManager.GetVillagerCount());
        SetRound(combatManager.currentRound);
        SetGold(GameStateManager.Instance.Resources.Gold);
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
            healButton?.SetEnabled(false);
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

        // Rule 3: If the unit is not below its max health then heal is disabled
        bool isBelowMaxHealth = unit.currentHealth < unit.maxHealth;
        healButton?.SetEnabled(isBelowMaxHealth);

        // Rule 4: If the unit is already fortified then fortify is disabled
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
    }
    private sealed class UnitCardView
    {
        public VisualElement Root { get; }

        private readonly UnitInstance unit;
        private readonly Label nameLabel;
        private readonly Label hpValueLabel;
        private readonly Label atkValueLabel;
        private readonly Label badgeLabel;
        private readonly Label footerTagLabel;

        public UnitCardView(UnitInstance unit, VisualTreeAsset cardAsset)
        {
            this.unit = unit;

            if (cardAsset != null)
            {
                Root = cardAsset.Instantiate();
                VisualElement cardRoot = Root.Q<VisualElement>("card-root") ?? Root;
                Root = cardRoot;
            }
            else
            {
                Root = new VisualElement();
                Root.AddToClassList("unit-card");
                Debug.LogWarning("CombatPhaseUIController: unitCardAsset not assigned, falling back to a blank card.");
            }

            nameLabel = Root.Q<Label>("card-name");
            hpValueLabel = Root.Q<Label>("card-hp-value");
            atkValueLabel = Root.Q<Label>("card-atk-value");
            badgeLabel = Root.Q<Label>("card-badge");
            footerTagLabel = Root.Q<Label>("card-footer-tag");

            Refresh();
        }

        public void Refresh()
        {
            if (nameLabel != null) nameLabel.text = unit.unitName.ToUpperInvariant();
            if (hpValueLabel != null) hpValueLabel.text = $"{unit.currentHealth}/{unit.maxHealth}";
            if (atkValueLabel != null) atkValueLabel.text = unit.attackPower.ToString();
            if (footerTagLabel != null) footerTagLabel.text = unit.attackRange > 1 ? "RANGED" : "FRONTLINE";
            if (badgeLabel != null) badgeLabel.text = unit.actionsRemaining <= 0 ? "OUT" : "";

            Root.EnableInClassList("unit-card-exhausted", unit.actionsRemaining <= 0);
        }

        public void SetSelected(bool selected)
        {
            Root.EnableInClassList("unit-card-selected", selected);
        }
    }
}