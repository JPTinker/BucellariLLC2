using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class TeamManagerUIController : MonoBehaviour
{
    [Header("UI Templates")]
    [SerializeField] private VisualTreeAsset unitCardTemplate;

    private UIDocument uiDocument;
    private ScrollView unitListScrollView;
    private Button marchToWarButton;
    private Label timerValueLabel;

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
    }

    private void Start()
    {
        // Query rootVisualElement in Start() when UIDocument is guaranteed to be initialized
        if (uiDocument == null)
        {
            Debug.LogError("UIDocument component missing!");
            return;
        }

        VisualElement root = uiDocument.rootVisualElement;
        if (root == null)
        {
            Debug.LogError("rootVisualElement is null! Ensure 'Source Asset' (UXML) and 'Panel Settings' are assigned on UIDocument.");
            return;
        }

        // 1. Query elements from UXML
        unitListScrollView = root.Q<ScrollView>(className: "unit-list");
        marchToWarButton = root.Q<Button>("btn-engage");
        timerValueLabel = root.Q<Label>(className: "timer-value");

        // 2. Register button callbacks
        if (marchToWarButton != null)
        {
            marchToWarButton.clicked += OnMarchToWarClicked;
        }

        // 3. Render the roster data
        RefreshRosterUI();
    }

    private void OnDestroy()
    {
        // Unsubscribe in OnDestroy instead of OnDisable when subscribing in Start
        if (marchToWarButton != null)
        {
            marchToWarButton.clicked -= OnMarchToWarClicked;
        }
    }

    public void RefreshRosterUI()
    {
        if (unitListScrollView == null) return;

        unitListScrollView.Clear();

        GameStateManager gsm = GameStateManager.Instance;
        if (gsm == null) return;

        gsm.EnsurePlayerHasTeam();

        foreach (Unit unit in gsm.ActiveTeam)
        {
            VisualElement card = CreateUnitCard(unit);
            unitListScrollView.Add(card);
        }
    }

    private VisualElement CreateUnitCard(Unit unit)
    {
        VisualElement card = new VisualElement();
        card.AddToClassList("unit-card");
        card.AddToClassList("unit-card-ready");

        VisualElement iconFrame = new VisualElement();
        iconFrame.AddToClassList("unit-icon-frame");
        Label iconLabel = new Label("⚔");
        iconLabel.AddToClassList("unit-icon");
        iconFrame.Add(iconLabel);

        VisualElement details = new VisualElement();
        details.AddToClassList("unit-details");

        VisualElement header = new VisualElement();
        header.AddToClassList("unit-header");

        Label nameLabel = new Label(unit.UnitName);
        nameLabel.AddToClassList("unit-name");

        Label badge = new Label("READY");
        badge.AddToClassList("status-badge");
        badge.AddToClassList("status-badge-active");

        header.Add(nameLabel);
        header.Add(badge);

        VisualElement hpRow = CreateStatBar("HP", unit.MaxHP, unit.MaxHP, "hp-fill");
        VisualElement atkRow = CreateStatBar("ATK", unit.BaseAttack, 30, "atk-fill");

        details.Add(header);
        details.Add(hpRow);
        details.Add(atkRow);

        card.Add(iconFrame);
        card.Add(details);

        return card;
    }

    private VisualElement CreateStatBar(string labelText, int currentVal, int maxVal, string fillClass)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("stat-row");

        Label label = new Label(labelText);
        label.AddToClassList("stat-label");

        VisualElement barBg = new VisualElement();
        barBg.AddToClassList("stat-bar-bg");

        VisualElement barFill = new VisualElement();
        barFill.AddToClassList("stat-bar-fill");
        barFill.AddToClassList(fillClass);

        float percentage = Mathf.Clamp01((float)currentVal / maxVal) * 100f;
        barFill.style.width = Length.Percent(percentage);

        barBg.Add(barFill);
        row.Add(label);
        row.Add(barBg);

        return row;
    }

    private void OnMarchToWarClicked()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.LoadCombatMap("CombatMap");
        }
    }
}