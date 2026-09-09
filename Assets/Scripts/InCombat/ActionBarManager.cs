using UnityEngine;
using UnityEngine.UIElements;

public class ActionBarManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private UIDocument uiDocument;

    private Button fortifyButton;
    private Button scoutButton;
    private Button spellsButton;
    private Button attackButton;
    private Button moveButton;
    private Button endTurnButton;

    private UnitInstance selectedUnit;

    private void OnEnable()
    {
        VisualElement root = uiDocument.rootVisualElement;

        // Find buttons from the UXML
        fortifyButton = root.Q<Button>("action-fortify");
        scoutButton = root.Q<Button>("action-scout");
        spellsButton = root.Q<Button>("action-spells");
        attackButton = root.Q<Button>("action-attack");
        moveButton = root.Q<Button>("action-move");
        endTurnButton = root.Q<Button>("end-turn");

        RegisterButtonCallbacks();

        // Nothing selected initially
        SetSelectedUnit(null);
    }

    private void OnDisable()
    {
        UnregisterButtonCallbacks();
    }

    private void RegisterButtonCallbacks()
    {
        fortifyButton.clicked += OnFortifyClicked;
        scoutButton.clicked += OnScoutClicked;
        spellsButton.clicked += OnSpellsClicked;
        attackButton.clicked += OnAttackClicked;
        moveButton.clicked += OnMoveClicked;
        endTurnButton.clicked += OnEndTurnClicked;
    }

    private void UnregisterButtonCallbacks()
    {
        fortifyButton.clicked -= OnFortifyClicked;
        scoutButton.clicked -= OnScoutClicked;
        spellsButton.clicked -= OnSpellsClicked;
        attackButton.clicked -= OnAttackClicked;
        spellsButton.clicked -= OnSpellsClicked;
        moveButton.clicked -= OnMoveClicked;
        endTurnButton.clicked -= OnEndTurnClicked;
    }

    public void SetSelectedUnit(UnitInstance unit)
    {
        selectedUnit = unit;

        RefreshButtons();
    }

    private void RefreshButtons()
    {
        bool unitSelected = selectedUnit != null;

        if (!unitSelected)
        {
            SetAllActionButtons(false);
            return;
        }

        fortifyButton.SetEnabled(true);
        scoutButton.SetEnabled(true);
        spellsButton.SetEnabled(false);
        attackButton.SetEnabled(false);
        moveButton.SetEnabled(false);

        // End turn doesn't necessarily depend on the selected unit.
        endTurnButton.SetEnabled(false);
    }

    private void SetAllActionButtons(bool enabled)
    {
        fortifyButton.SetEnabled(enabled);
        scoutButton.SetEnabled(enabled);
        spellsButton.SetEnabled(enabled);
        attackButton.SetEnabled(enabled);
        moveButton.SetEnabled(enabled);
    }

    private void OnFortifyClicked()
    {
        if (selectedUnit == null)
            return;

        //selectedUnit.Fortify();

        RefreshButtons();
    }

    private void OnScoutClicked()
    {
        if (selectedUnit == null)
            return;

        //selectedUnit.Scout();

        RefreshButtons();
    }

    private void OnSpellsClicked()
    {
        if (selectedUnit == null)
            return;

        //selectedUnit.CastSpell();

        RefreshButtons();
    }

    private void OnAttackClicked()
    {
        if (selectedUnit == null)
            return;

        //selectedUnit.Attack();

        RefreshButtons();
    }

    private void OnMoveClicked()
    {
        if (selectedUnit == null)
            return;

        //selectedUnit.Move();

        RefreshButtons();
    }

    private void OnEndTurnClicked()
    {
        // We'll connect this to your turn manager.
        Debug.Log("End Turn clicked.");
    }
}