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
/// PhaseShell.uxml, alongside a PlanningPhaseController, a
/// DecisionPhaseController and a CampaignMapController (plain components - none
/// of them own a UIDocument). Assign DecisionContent.uxml (the single Plan
/// screen) and TabPlaceholder.uxml (still used for the not-yet-built Store
/// tab) plus all three controller references in the Inspector.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class PhaseShellController : MonoBehaviour
{
    public static PhaseShellController Instance { get; private set; }

    public enum Tab { Plan, Store }

    /// <summary>Lets other tabs (e.g. Decision's Combat Vanguard card) read the Roster tab's live selection.</summary>
    public PlanningPhaseController RosterController => rosterController;

    [Header("Content templates")]
    [Tooltip("DecisionContent.uxml - the single Plan screen (course, labor, squad).")]
    [SerializeField] private VisualTreeAsset decisionContentTemplate;
    [Tooltip("TabPlaceholder.uxml. Reused for Store until it has real content.")]
    [SerializeField] private VisualTreeAsset placeholderContentTemplate;

    [Header("Tab controllers")]
    [Tooltip("Plain component now - no longer requires its own UIDocument.")]
    [SerializeField] private PlanningPhaseController rosterController;
    [SerializeField] private DecisionPhaseController decisionController;
    [SerializeField] private CampaignMapController campaignMapController;
    [Tooltip("Optional: shows narrative events. Auto-added if left empty.")]
    [SerializeField] private EventPhaseController eventController;

    private UIDocument _document;
    private VisualElement _root;
    private VisualElement _contentSlot;

    private VisualElement _planRoot;
    private VisualElement _storePlaceholderRoot;

    // Header (Live Asset Ledger)
    private Label _resUnitsVal, _resUnitsCap, _resUnitsDelta;
    private Label _resFoodVal, _resFoodDelta;
    private Label _resMaterialsVal, _resMaterialsDelta;
    private Label _resVillagersVal, _resVillagersCap, _resVillagersDelta;
    private Label _resMoraleVal, _resMoraleDelta;
    private Label _ledgerSyncStatus;
    private Button _saveButton;

    // Readiness checklist
    private Label _checkCourse, _checkSquad, _checkVillagers, _checkBudget;

    // Footer
    private Button _navPlan, _navStore;
    private Button _engageButton;

    public Tab CurrentTab { get; private set; } = Tab.Plan;

    public bool CanEngage
    {
        get
        {
            var gsm = GameStateManager.Instance;
            return gsm != null &&
                !gsm.Events.HasPendingEvent &&
                gsm.HasSelectedCampaignAction &&
                rosterController != null &&
                (gsm.SelectedCampaignAction == GameStateManager.CampaignAction.Rest ||
                 rosterController.SelectedUnits.Count > 0);
        }
    }

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

        // Settings button sits beside Save in the header; its overlay covers the whole shell.
        if (_saveButton != null && _saveButton.parent != null)
        {
            var settingsButton = SettingsPanel.Attach(_root.Q<VisualElement>("phase-root") ?? _root,
                _saveButton.parent, _saveButton.parent.IndexOf(_saveButton));
            settingsButton?.AddToClassList("nav-button");
        }

        BuildContent();
        WireNav();

        RefreshHeader();
        Show(Tab.Plan);

        // Keep chrome out from under the notch / home indicator on real devices.
        _root.RegisterCallback<GeometryChangedEvent>(_ => ApplySafeArea());
        ApplySafeArea();
    }

    /// <summary>Pads the shell's phase-root by Screen.safeArea, converted from screen pixels to panel units.</summary>
    private void ApplySafeArea()
    {
        var phaseRoot = _root.Q<VisualElement>("phase-root");
        if (phaseRoot == null || Screen.width <= 0) return;

        // Panel units per screen pixel. Skip until the root has been laid out.
        float ratio = _root.layout.width / Screen.width;
        if (float.IsNaN(ratio) || ratio <= 0f) return;

        Rect safe = Screen.safeArea;
        // Clamp so a bad safe-area value can never collapse the layout.
        float maxInset = _root.layout.width * 0.15f;
        phaseRoot.style.paddingLeft = Mathf.Clamp(safe.xMin * ratio, 0, maxInset);
        phaseRoot.style.paddingRight = Mathf.Clamp((Screen.width - safe.xMax) * ratio, 0, maxInset);
        phaseRoot.style.paddingBottom = Mathf.Clamp(safe.yMin * ratio, 0, maxInset);
        phaseRoot.style.paddingTop = Mathf.Clamp((Screen.height - safe.yMax) * ratio, 0, maxInset);
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
        _saveButton = _root.Q<Button>("btn-save-game");

        _checkCourse = _root.Q<Label>("check-course");
        _checkSquad = _root.Q<Label>("check-squad");
        _checkVillagers = _root.Q<Label>("check-villagers");
        _checkBudget = _root.Q<Label>("check-budget");
    }

    private void QueryFooter()
    {
        _navPlan = _root.Q<Button>("nav-plan");
        _navStore = _root.Q<Button>("nav-store");
        _engageButton = _root.Q<Button>("btn-engage");
        _engageButton?.SetEnabled(false);
    }

    /// <summary>
    /// Instantiates each tab's content once into content-slot (rather than
    /// re-instantiating on every tab switch) and hands Roster/Decision their
    /// controller's Initialize() call. Order matters: content must already
    /// be attached under content-slot (a descendant of _root) before
    /// Initialize() runs, since both controllers query by name against the
    /// WHOLE shell root - that's how PlanningPhaseController still reaches
    /// the reveal/level-up/draft overlays, which live in PhaseShell.uxml,
    /// not inside DecisionContent.uxml.
    /// </summary>
    private void BuildContent()
    {
        if (decisionContentTemplate == null || placeholderContentTemplate == null)
        {
            Debug.LogError("PhaseShellController: one or more content templates aren't assigned in the Inspector.");
            return;
        }

        _planRoot = decisionContentTemplate.Instantiate();
        _storePlaceholderRoot = placeholderContentTemplate.Instantiate();

        SetPlaceholderText(_storePlaceholderRoot, "STORE", "Not built yet.");

        _contentSlot.Add(_planRoot);
        _contentSlot.Add(_storePlaceholderRoot);

        // Each Initialize() call is wrapped so one tab throwing (e.g. a
        // missing/renamed UXML element) can't abort the rest of BuildContent()/
        // Start() - without this, an exception here would skip WireNav() and
        // Show(Tab.Plan) entirely, leaving every tab stacked and visible at
        // once with no nav button responding to clicks.
        if (rosterController != null)
        {
            try { rosterController.Initialize(_root); }
            catch (System.Exception e) { Debug.LogError($"PhaseShellController: Roster Controller threw during Initialize() - {e}"); }
        }
        else Debug.LogError("PhaseShellController: Roster Controller isn't assigned in the Inspector.");

        if (decisionController != null)
        {
            try { decisionController.Initialize(_root); }
            catch (System.Exception e) { Debug.LogError($"PhaseShellController: Decision Controller threw during Initialize() - {e}"); }
        }
        else Debug.LogError("PhaseShellController: Decision Controller isn't assigned in the Inspector.");

        if (campaignMapController != null)
        {
            try { campaignMapController.Initialize(_root); }
            catch (System.Exception e) { Debug.LogError($"PhaseShellController: Campaign Map Controller threw during Initialize() - {e}"); }
        }
        else Debug.LogError("PhaseShellController: Campaign Map Controller isn't assigned in the Inspector.");

        // Last, so the event overlay's "wait for draft/reveal" check sees the roster tab's state.
        if (eventController == null) eventController = GetComponent<EventPhaseController>() ?? gameObject.AddComponent<EventPhaseController>();
        try { eventController.Initialize(_root); }
        catch (System.Exception e) { Debug.LogError($"PhaseShellController: Event Controller threw during Initialize() - {e}"); }
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
        if (_navPlan != null) _navPlan.clicked += () => Show(Tab.Plan);
        if (_navStore != null) _navStore.clicked += () => Show(Tab.Store);
        if (_saveButton != null) _saveButton.clicked += SaveGame;
    }

    private void SaveGame()
    {
        var gsm = GameStateManager.Instance;
        bool saved = gsm != null && gsm.SaveGame();
        FlashSyncStatus(saved ? "[ GAME SAVED ]" : "[ SAVE FAILED ]");
    }

    public void ShowPlan() => Show(Tab.Plan);

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

        SetVisible(_planRoot, tab == Tab.Plan);
        SetVisible(_storePlaceholderRoot, tab == Tab.Store);

        SetActiveNav(_navPlan, tab == Tab.Plan);
        SetActiveNav(_navStore, tab == Tab.Store);

        if (tab == Tab.Plan) decisionController?.Refresh();
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
        if (gsm == null)
        {
            _engageButton?.SetEnabled(false);
            return;
        }

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
        // (not sent as battle workers this cycle) as the annotation.
        _resVillagersVal.text = s.Villagers.ToString();
        _resVillagersCap.text = $"/{s.VillagerCapacity}";
        _resVillagersDelta.text = $"{forecast.IdleVillagers} IDLE";

        _resMoraleVal.text = $"{s.Morale}%";
        SetDelta(_resMoraleDelta, forecast.MoraleDelta, suffix: "%");

        // Always visible; the checklist explains what's still missing.
        RefreshChecklist(gsm, forecast);
        _engageButton?.SetEnabled(CanEngage);
    }

    /// <summary>Writes the "Ready to march" rows; March to War enables only when every row passes.</summary>
    private void RefreshChecklist(GameStateManager gsm, GameStateManager.CycleForecast forecast)
    {
        int squad = rosterController != null ? rosterController.SelectedUnits.Count : 0;

        SetCheck(_checkCourse, gsm.HasSelectedCampaignAction, "Course chosen", "Choose a course");
        SetCheck(_checkSquad, squad > 0, $"Squad selected ({squad}/{GameStateManager.MaxTeamSize})", $"Select your squad (0/{GameStateManager.MaxTeamSize})");
        SetCheck(_checkVillagers, gsm.VillagersSent > 0, $"{gsm.VillagersSent} workers sent ({gsm.IdleVillagers} stay home)", $"No workers sent ({gsm.IdleVillagers} stay home)");

        if (_checkBudget != null)
        {
            if (forecast.IsOverBudget) _checkBudget.AddToClassList("readiness-row--active");
            else _checkBudget.RemoveFromClassList("readiness-row--active");
            _checkBudget.text = "\u2717 Cannot afford queued ship";
        }
    }

    private static void SetCheck(Label label, bool done, string doneText, string todoText)
    {
        if (label == null) return;
        label.text = done ? "\u2713 " + doneText : "\u2717 " + todoText;
        if (done) label.AddToClassList("readiness-row--done");
        else label.RemoveFromClassList("readiness-row--done");
    }

    private static void SetDelta(Label label, int value, string suffix = "")
    {
        label.text = $"{(value >= 0 ? "+" : "")}{value}{suffix}";
        label.RemoveFromClassList("text-positive");
        label.RemoveFromClassList("text-negative");
        label.AddToClassList(value >= 0 ? "text-positive" : "text-negative");
    }

    /// <summary>Called by DecisionPhaseController after a successful ExecuteCycle() or Save Game.</summary>
    public void FlashSyncStatus(string message = "[ CYCLE EXECUTED ]")
    {
        if (_ledgerSyncStatus == null) return;

        CancelInvoke(nameof(ResetSyncStatus));
        _ledgerSyncStatus.text = message;
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
