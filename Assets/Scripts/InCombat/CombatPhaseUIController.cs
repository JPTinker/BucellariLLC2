using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class CombatPhaseUIController : MonoBehaviour
{
    private UIDocument uiDocument;
    private ScrollView unitList;
    private readonly List<UnitInstance> playerUnits = new List<UnitInstance>();
    private readonly Dictionary<UnitInstance, UnitCardView> cardViews = new Dictionary<UnitInstance, UnitCardView>();

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
    }

    private void Start()
    {
        if (uiDocument == null) return;

        unitList = uiDocument.rootVisualElement?.Q<ScrollView>("unit-list");
        RefreshRoster();
    }

    private void Update()
    {
        if (unitList == null) return;

        UnitInstance[] currentUnits = FindObjectsByType<UnitInstance>(FindObjectsSortMode.None);
        bool rosterChanged = false;
        HashSet<UnitInstance> currentPlayerUnits = new HashSet<UnitInstance>();

        foreach (UnitInstance unit in currentUnits)
        {
            if (unit != null && !unit.isEnemy && !unit.IsDead)
                currentPlayerUnits.Add(unit);
        }

        if (currentPlayerUnits.Count != playerUnits.Count)
        {
            rosterChanged = true;
        }
        else
        {
            foreach (UnitInstance unit in playerUnits)
            {
                if (!currentPlayerUnits.Contains(unit))
                {
                    rosterChanged = true;
                    break;
                }
            }
        }

        if (rosterChanged) RefreshRoster();

        foreach (UnitCardView cardView in cardViews.Values)
            cardView.Refresh();
    }

    private void RefreshRoster()
    {
        if (unitList == null) return;

        unitList.Clear();
        playerUnits.Clear();
        cardViews.Clear();

        UnitInstance[] units = FindObjectsByType<UnitInstance>(FindObjectsSortMode.None);
        foreach (UnitInstance unit in units)
        {
            if (unit == null || unit.isEnemy || unit.IsDead) continue;

            playerUnits.Add(unit);
            UnitCardView cardView = new UnitCardView(unit);
            cardView.Card.RegisterCallback<ClickEvent>(_ => SelectUnit(unit));
            cardViews.Add(unit, cardView);
            unitList.Add(cardView.Card);
        }
    }

    private void SelectUnit(UnitInstance unit)
    {
        if (CombatManager.Instance != null)
            CombatManager.Instance.SelectUnit(unit);
    }

    private sealed class UnitCardView
    {
        public VisualElement Card { get; }
        private readonly UnitInstance unit;
        private readonly Label nameLabel;
        private readonly Label roleLabel;
        private readonly VisualElement healthFill;
        private readonly Label metaLabel;

        public UnitCardView(UnitInstance unit)
        {
            this.unit = unit;
            Card = new VisualElement();
            Card.AddToClassList("unit-card");

            VisualElement avatar = new VisualElement();
            avatar.AddToClassList("unit-avatar");
            avatar.AddToClassList("avatar-vance");
            Label avatarLetter = new Label(GetInitial(unit.unitName));
            avatarLetter.AddToClassList("avatar-letter");
            avatar.Add(avatarLetter);

            VisualElement copy = new VisualElement();
            copy.AddToClassList("unit-copy");

            nameLabel = new Label();
            nameLabel.AddToClassList("unit-name");
            roleLabel = new Label();
            roleLabel.AddToClassList("unit-role");

            VisualElement healthTrack = new VisualElement();
            healthTrack.AddToClassList("health-track");
            healthFill = new VisualElement();
            healthFill.AddToClassList("health-fill");
            healthTrack.Add(healthFill);

            metaLabel = new Label();
            metaLabel.AddToClassList("unit-meta");

            copy.Add(nameLabel);
            copy.Add(roleLabel);
            copy.Add(healthTrack);
            copy.Add(metaLabel);
            Card.Add(avatar);
            Card.Add(copy);
            Refresh();
        }

        public void Refresh()
        {
            int maxHealth = Mathf.Max(1, unit.maxHealth);
            float healthPercent = Mathf.Clamp01((float)unit.currentHealth / maxHealth) * 100f;
            nameLabel.text = unit.unitName.ToUpperInvariant();
            roleLabel.text = unit.attackRange > 1 ? "RANGED UNIT" : "FRONTLINE UNIT";
            healthFill.style.width = Length.Percent(healthPercent);
            metaLabel.text = $"{unit.currentHealth} / {unit.maxHealth} HP   |   {unit.actionsRemaining} ACTIONS";
            Card.EnableInClassList("unit-card-exhausted", unit.actionsRemaining <= 0);
        }

        private static string GetInitial(string name)
        {
            return string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
        }
    }
}