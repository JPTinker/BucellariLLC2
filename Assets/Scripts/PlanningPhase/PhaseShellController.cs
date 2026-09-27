using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Owns PhaseShell.uxml: the single UIDocument for this scene, the shared
/// stats header, the swappable content-slot, and the shared bottom nav.
///
/// Supersedes PhaseTabSwitcher.cs from the previous iteration - delete that
/// file/component if it's still in the project. The old version toggled
/// between two separate UIDocuments; now there's one UIDocument and this
/// swaps which instantiated content tree is visible inside it.
///
/// Attach this to the same GameObject as the UIDocument referencing
/// PhaseShell.uxml, alongside a PlanningPhaseController and a
/// DecisionPhaseController (plain components now - neither owns a
/// UIDocument anymore). Assign RosterContent.uxml, DecisionContent.uxml and
/// TabPlaceholder.uxml plus both controller references in the Inspector.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class PhaseShellController : MonoBehaviour
{
    public static PhaseShellController Instance { get; private set; }

    public enum Tab { Roster, Decision, Map, Store }

    [Header("Content templates")]
    [SerializeField] private VisualTreeAsset rosterContentTemplate;
    [SerializeField] private VisualTreeAsset decisionContentTemplate;
    [Tooltip("TabPlaceholder.uxml. Reused for both Campaign Map and Store until each has real content.")]
    [SerializeField] private VisualTreeAsset placeholderContentTemplate;

    [Header("Tab controllers")]
    [Tooltip("Plain component now - no longer requires its own UIDocument.")]
    [SerializeField] private PlanningPhaseController rosterController;
    [SerializeField] private DecisionPhaseController decisionController;

    private UIDocument _document;
    private VisualElement _root;
    private VisualElement _contentSlot;

    private VisualElement _rosterRoot;
    private VisualElement _decisionRoot;
    private VisualElement _mapPlaceholderRoot;
    private VisualElement _storePlaceholderRoot;

    // Header (Live Asset Ledger)
    private Label _resUnitsVal, _resUnitsCap, _resUnitsDelta;
    private Label _resFoodVal, _resFoodDelta;
    private Label _resMaterialsVal, _resMaterialsDelta;
    private Label _resVillagersVal, _resVillagersCap, _resVillagersDelta;
    private Label _resMoraleVal, _resMoraleDelta;
    private Label _ledgerSyncStatus;

    // Footer
    private Button _navRoster, _navDecision, _navMap, _navStore;

    public Tab CurrentTab { get; private set; } = Tab.Roster;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("PhaseShellController: multiple instances in scene, keeping the first.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        _document = GetComponent<UIDocument>();
        _root = _document.rootVisualElement;

        QueryHeader();
        QueryFooter();
        _contentSlot = _root.Q<VisualElement>("content-slot");

        BuildContent();
        WireNav();

        RefreshHeader();
        Show(Tab.Roster);
    }

    private void QueryHeader()
    {
        _resUnitsVal = _root.Q<Label>("res-units-val");
        _resUnitsCap = _root.Q<Label>("res-units-cap");
        _resUnitsDelta = _root.Q<Label>("res-units-delta");
        _resFoodVal = _root.Q<Label>("res-food-val");
        _resFoodDelta = _root.Q<Label>("res-food-delta");
        _resMaterialsVal = _root.Q<Label>("res-materials-val");
        _resMaterialsDelta = _root.Q<Label>("res-materials-delta");
        _resVillagersVal = _root.Q<Label>("res-villagers-val");
        _resVillagersCap = _root.Q<Label>("res-villagers-cap");
        _resVillagersDelta = _root.Q<Label>("res-villagers-delta");
        _resMoraleVal = _root.Q<Label>("res-morale-val");
        _resMoraleDelta = _root.Q<Label>("res-morale-delta");
        _ledgerSyncStatus = _root.Q<Label>("ledger-sync-status");
    }

    private void QueryFooter()
    {
        _navRoster = _root.Q<Button>("nav-roster");
        _navDecision = _root.Q<Button>("nav-decision");
        _navMap = _root.Q<Button>("nav-map");
        _navStore = _root.Q<Button>("nav-store");
    }

    /// <summary>
    /// Instantiates each tab's content once into content-slot (rather than
    /// re-instantiating on every tab switch) and hands Roster/Decision their
    /// controller's Initialize() call. Order matters: content must already
    /// be attached under content-slot (a descendant of _root) before
    /// Initialize() runs, since both controllers query by name against the
    /// WHOLE shell root - that's how PlanningPhaseController still reaches
    /// the reveal/level-up/draft overlays, which live in PhaseShell.uxml,
    /// not inside RosterContent.uxml.
    /// </summary>
    private void BuildContent()
    {
        if (rosterContentTemplate == null || decisionContentTemplate == null || placeholderContentTemplate == null)
        {
            Debug.LogError("PhaseShellController: one or more content templates aren't assigned in the Inspector.");
            return;
        }

        _rosterRoot = rosterContentTemplate.Instantiate();
        _decisionRoot = decisionContentTemplate.Instantiate();
        _mapPlaceholderRoot = placeholderContentTemplate.Instantiate();
        _storePlaceholderRoot = placeholderContentTemplate.Instantiate();

        SetPlaceholderText(_mapPlaceholderRoot, "CAMPAIGN MAP", "Not built yet.");
        SetPlaceholderText(_storePlaceholderRoot, "STORE", "Not built yet.");

        _contentSlot.Add(_rosterRoot);
        _contentSlot.Add(_decisionRoot);
        _contentSlot.Add(_mapPlaceholderRoot);
        _contentSlot.Add(_storePlaceholderRoot);

        if (rosterController != null) rosterController.Initialize(_root);
        else Debug.LogError("PhaseShellController: Roster Controller isn't assigned in the Inspector.");

        if (decisionController != null) decisionController.Initialize(_root);
        else Debug.LogError("PhaseShellController: Decision Controller isn't assigned in the Inspector.");
    }

    private static void SetPlaceholderText(VisualElement placeholderRoot, string title, string body)
    {
        var titleLabel = placeholderRoot.Q<Label>("placeholder-title");
        var bodyLabel = placeholderRoot.Q<Label>("placeholder-body");
        if (titleLabel != null) titleLabel.text = title;
        if (bodyLabel != null) bodyLabel.text = body;
    }

    private void WireNav()
    {
        if (_navRoster != null) _navRoster.clicked += () => Show(Tab.Roster);
        if (_navDecision != null) _navDecision.clicked += () => Show(Tab.Decision);
        if (_navMap != null) _navMap.clicked += () => Show(Tab.Map);
        if (_navStore != null) _navStore.clicked += () => Show(Tab.Store);
    }

    public void ShowRoster() => Show(Tab.Roster);
    public void ShowDecision() => Show(Tab.Decision);

    /// <summary>
    /// Toggles which content tree is visible via display style rather than
    /// re-instantiating or using GameObject.SetActive() - the latter would
    /// stop any Coroutine running on the hidden tab's controller (e.g. a
    /// mid-flight reveal/level-up animation on Roster) instead of pausing
    /// it, since SetActive(false) kills coroutines rather than suspending
    /// them.
    /// </summary>
    private void Show(Tab tab)
    {
        CurrentTab = tab;

        SetVisible(_rosterRoot, tab == Tab.Roster);
        SetVisible(_decisionRoot, tab == Tab.Decision);
        SetVisible(_mapPlaceholderRoot, tab == Tab.Map);
        SetVisible(_storePlaceholderRoot, tab == Tab.Store);

        SetActiveNav(_navRoster, tab == Tab.Roster);
        SetActiveNav(_navDecision, tab == Tab.Decision);
        SetActiveNav(_navMap, tab == Tab.Map);
        SetActiveNav(_navStore, tab == Tab.Store);
    }

    private static void SetVisible(VisualElement element, bool visible)
    {
        if (element != null) element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static void SetActiveNav(Button button, bool active)
    {
        if (button == null) return;
        if (active) button.AddToClassList("nav-button-active");
        else button.RemoveFromClassList("nav-button-active");
    }

    /// <summary>
    /// Re-reads GameStateManager (CurrentAllocation + ComputeForecast()) and
    /// writes the Live Asset Ledger header. Called on startup and by
    /// DecisionPhaseController after every allocation change - the header is
    /// visible on every tab, so it needs to stay in sync even while the
    /// player isn't looking at the Decision tab.
    /// </summary>
    public void RefreshHeader()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null) return;

        var s = gsm.Settlement;
        var forecast = gsm.ComputeForecast();

        // Units cell is Population vs Unit Capacity (see tooltip) - idle count
        // (units not yet assigned to Vanguard this cycle) is the annotation.
        _resUnitsVal.text = gsm.Population.ToString();
        _resUnitsCap.text = $"/{s.UnitCapacity}";
        _resUnitsDelta.text = $"{forecast.IdleUnits} IDLE";

        _resFoodVal.text = s.Food.ToString();
        SetDelta(_resFoodDelta, forecast.FoodDelta);

        _resMaterialsVal.text = s.Materials.ToString();
        SetDelta(_resMaterialsDelta, forecast.MaterialsDelta);

        // Villagers cell mirrors Units: current headcount vs capacity, with idle
        // (not yet assigned to a labor directive this cycle) as the annotation.
        _resVillagersVal.text = s.Villagers.ToString();
        _resVillagersCap.text = $"/{s.VillagerCapacity}";
        _resVillagersDelta.text = $"{forecast.IdleVillagers} IDLE";

        _resMoraleVal.text = $"{s.Morale}%";
        SetDelta(_resMoraleDelta, forecast.MoraleDelta, suffix: "%");
    }

    private static void SetDelta(Label label, int value, string suffix = "")
    {
        label.text = $"{(value >= 0 ? "+" : "")}{value}{suffix}";
        label.RemoveFromClassList("text-positive");
        label.RemoveFromClassList("text-negative");
        label.AddToClassList(value >= 0 ? "text-positive" : "text-negative");
    }

    /// <summary>Called by DecisionPhaseController after a successful ExecuteCycle().</summary>
    public void FlashSyncStatus()
    {
        if (_ledgerSyncStatus == null) return;

        _ledgerSyncStatus.text = "[ CYCLE EXECUTED ]";
        _ledgerSyncStatus.RemoveFromClassList("text-primary-fixed-dim");
        _ledgerSyncStatus.AddToClassList("text-primary-container");

        Invoke(nameof(ResetSyncStatus), 2f);
    }

    private void ResetSyncStatus()
    {
        if (_ledgerSyncStatus == null) return;

        _ledgerSyncStatus.text = "[ MATRIX STABLE ]";
        _ledgerSyncStatus.RemoveFromClassList("text-primary-container");
        _ledgerSyncStatus.AddToClassList("text-primary-fixed-dim");
    }
}
