using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Drives the Decision (resource-planning) tab UI Toolkit document. Attach to
/// the same GameObject as a UIDocument component referencing DecisionTab.uxml.
///
/// Named DecisionPhaseController - NOT PlanningPhaseController - because that
/// name is already taken by the Roster team-selection screen's controller.
/// Two MonoBehaviours can't share a class name in the same assembly, and
/// GameStateManager.OnSceneLoaded already does
/// FindAnyObjectByType&lt;PlanningPhaseController&gt;() expecting the Roster one,
/// so don't rename this back.
///
/// This replaces every function that lived in the &lt;script&gt; tag of the HTML
/// mock (recalc, adjustUnits, resetAllocations, executeCycle). All the actual
/// math still lives in GameStateManager (ComputeForecast / TryAdjustAllocation /
/// ExecuteCycle) - this class only reads that state and writes it into labels.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class DecisionPhaseController : MonoBehaviour
{
    private VisualElement _root;
    private GameStateManager _gsm;

    // Ledger
    private Label _resUnitsVal, _resUnitsCap, _resUnitsDelta;
    private Label _resFoodVal, _resFoodDelta;
    private Label _resMaterialsVal, _resMaterialsDelta;
    private Label _resLaborVal, _resLaborDelta;
    private Label _resMoraleVal, _resMoraleDelta;
    private Label _ledgerSyncStatus;

    // Debrief
    private Label _debriefMoraleTotal, _debriefKills, _debriefSaved, _debriefLosses;

    // Directive counters
    private Label _countVanguard, _countScavenge, _countHarvest, _countExpansion;

    // Forecast
    private Label _forecastFood, _forecastScrap, _forecastLabor, _forecastMorale;
    private VisualElement _overcrowdBanner;
    private Label _overcrowdDetail;

    // Buttons
    private Button _btnExecute, _btnReset;

    // Shared bottom nav (same names/classes as MapPhase.uxml)
    private Button _navRoster, _navDecision, _navMap, _navInventory;

    [Header("Last Cycle Debrief (read-only snapshot for UI)")]

    // Snapshot of the *This Battle counters, taken by ApplyPostBattleResults()

    // right before it clears them. The Decision screen reads these to show

    // "what just happened" - the *ThisBattle fields themselves are already

    // zero by the time any UI script's Start()/OnEnable() runs, since

    // ApplyPostBattleResults() fires from OnSceneLoaded ahead of the UI.

    public int LastCycleKills { get; private set; }

    public int LastCycleVillagersSaved { get; private set; }

    public int LastCycleCasualties { get; private set; }

    public int LastCycleMoraleDelta { get; private set; }



    private void OnEnable()
    {
        _gsm = GameStateManager.Instance;
        _root = GetComponent<UIDocument>().rootVisualElement;

        QueryElements();
        BindButtons();
        ShowDebrief();
        Refresh();
    }

    private void QueryElements()
    {
        _resUnitsVal = _root.Q<Label>("res-units-val");
        _resUnitsCap = _root.Q<Label>("res-units-cap");
        _resUnitsDelta = _root.Q<Label>("res-units-delta");
        _resFoodVal = _root.Q<Label>("res-food-val");
        _resFoodDelta = _root.Q<Label>("res-food-delta");
        _resMaterialsVal = _root.Q<Label>("res-materials-val");
        _resMaterialsDelta = _root.Q<Label>("res-materials-delta");
        _resLaborVal = _root.Q<Label>("res-labor-val");
        _resLaborDelta = _root.Q<Label>("res-labor-delta");
        _resMoraleVal = _root.Q<Label>("res-morale-val");
        _resMoraleDelta = _root.Q<Label>("res-morale-delta");
        _ledgerSyncStatus = _root.Q<Label>("ledger-sync-status");

        _debriefMoraleTotal = _root.Q<Label>("debrief-morale-total");
        _debriefKills = _root.Q<Label>("debrief-kills");
        _debriefSaved = _root.Q<Label>("debrief-saved");
        _debriefLosses = _root.Q<Label>("debrief-losses");

        _countVanguard = _root.Q<Label>("count-vanguard");
        _countScavenge = _root.Q<Label>("count-scavenge");
        _countHarvest = _root.Q<Label>("count-harvest");
        _countExpansion = _root.Q<Label>("count-expansion");

        _forecastFood = _root.Q<Label>("forecast-food");
        _forecastScrap = _root.Q<Label>("forecast-scrap");
        _forecastLabor = _root.Q<Label>("forecast-labor");
        _forecastMorale = _root.Q<Label>("forecast-morale");
        _overcrowdBanner = _root.Q<VisualElement>("overcrowd-banner");
        _overcrowdDetail = _root.Q<Label>("overcrowd-detail");

        _btnExecute = _root.Q<Button>("btn-execute-cycle");
        _btnReset = _root.Q<Button>("btn-reset");

        _navRoster = _root.Q<Button>("nav-roster");
        _navDecision = _root.Q<Button>("nav-decision");
        _navMap = _root.Q<Button>("nav-map");
        _navInventory = _root.Q<Button>("nav-inventory");
    }

    private void BindButtons()
    {
        RegisterStepper("btn-vanguard-dec", GameStateManager.Directive.Vanguard, -1);
        RegisterStepper("btn-vanguard-inc", GameStateManager.Directive.Vanguard, 1);
        RegisterStepper("btn-scavenge-dec", GameStateManager.Directive.Scavenge, -1);
        RegisterStepper("btn-scavenge-inc", GameStateManager.Directive.Scavenge, 1);
        RegisterStepper("btn-harvest-dec", GameStateManager.Directive.Harvest, -1);
        RegisterStepper("btn-harvest-inc", GameStateManager.Directive.Harvest, 1);
        RegisterStepper("btn-expansion-dec", GameStateManager.Directive.Expansion, -1);
        RegisterStepper("btn-expansion-inc", GameStateManager.Directive.Expansion, 1);

        _btnReset.clicked += () =>
        {
            _gsm.ResetAllocation();
            Refresh();
        };

        _btnExecute.clicked += () =>
        {
            bool applied = _gsm.ExecuteCycle();
            if (!applied)
            {
                // Over Labor budget - ComputeForecast().IsOverLaborBudget already
                // disables this via Refresh(), but guard here too in case the
                // button state is stale.
                return;
            }
            FlashSyncStatus();
            Refresh();
        };

        // Roster and Decision are both functional; Campaign Map and Inventory
        // are visible but disabled until those screens exist - same convention
        // as the Roster screen's own nav.
        if (_navRoster != null) _navRoster.clicked += OnNavRosterClicked;
        _navMap?.SetEnabled(false);
        _navInventory?.SetEnabled(false);
    }

    /// <summary>
    /// Hands off to the Roster tab. PhaseTabSwitcher just toggles which
    /// screen's UIDocument is visible - it doesn't reset either screen's
    /// state, so the current allocation is still here when the player
    /// switches back.
    /// </summary>
    private void OnNavRosterClicked()
    {
        PhaseTabSwitcher.Instance?.ShowRoster();
    }

    private void OnDisable()
    {
        if (_navRoster != null) _navRoster.clicked -= OnNavRosterClicked;
    }

    private void RegisterStepper(string buttonName, GameStateManager.Directive directive, int delta)
    {
        _root.Q<Button>(buttonName).clicked += () =>
        {
            if (_gsm.TryAdjustAllocation(directive, delta))
                Refresh();
        };
    }

    /// <summary>
    /// Pulls the last-cycle snapshot (kills / saves / casualties / morale
    /// delta) that GameStateManager captured in ApplyPostBattleResults()
    /// before it cleared the live counters, and renders the debrief card.
    /// Call once per scene load - this doesn't change with stepper taps.
    /// </summary>
    private void ShowDebrief()
    {
        _debriefKills.text = $"{_gsm.LastCycleKills} Kills";
        _debriefSaved.text = $"{_gsm.LastCycleVillagersSaved} Saved";
        _debriefLosses.text = $"{_gsm.LastCycleCasualties} Lost";

        int total = _gsm.LastCycleMoraleDelta;
        _debriefMoraleTotal.text = $"{(total >= 0 ? "+" : "")}{total}% MORALE";
        _debriefMoraleTotal.RemoveFromClassList("text-positive");
        _debriefMoraleTotal.RemoveFromClassList("text-negative");
        _debriefMoraleTotal.AddToClassList(total >= 0 ? "text-positive" : "text-negative");
    }

    /// <summary>
    /// Re-reads GameStateManager (allocation counts + ComputeForecast()) and
    /// writes every label/class on screen. Call after any allocation change.
    /// </summary>
    private void Refresh()
    {
        var a = _gsm.CurrentAllocation;
        var s = _gsm.Settlement;
        var forecast = _gsm.ComputeForecast();

        _countVanguard.text = a.VanguardUnits.ToString();
        _countScavenge.text = a.ScavengeUnits.ToString();
        _countHarvest.text = a.HarvestUnits.ToString();
        _countExpansion.text = a.ExpansionUnits.ToString();

        _resUnitsVal.text = forecast.AssignedUnits.ToString();
        _resUnitsCap.text = $"/{s.HousingCapacity}";
        _resUnitsDelta.text = $"{forecast.IdleUnits} IDLE";

        SetDelta(_resFoodDelta, forecast.FoodDelta);
        SetDelta(_resMaterialsDelta, forecast.MaterialsDelta);

        _resLaborVal.text = forecast.LaborUsed.ToString();
        _resLaborDelta.text = $"{Mathf.Max(0, forecast.LaborRemaining)} REM";

        SetDelta(_resMoraleDelta, forecast.MoraleDelta, suffix: "%");

        _forecastFood.text = $"Net {(forecast.FoodDelta >= 0 ? "+" : "")}{forecast.FoodDelta} ({(forecast.FoodDelta >= 0 ? "Surplus" : "Deficit")})";
        SetTone(_forecastFood, forecast.FoodDelta >= 0);

        _forecastScrap.text = $"Net {(forecast.MaterialsDelta >= 0 ? "+" : "")}{forecast.MaterialsDelta}" + (a.ExpansionUnits > 0 ? " (Hab Pod)" : "");
        SetTone(_forecastScrap, forecast.MaterialsDelta >= 0);

        _forecastLabor.text = $"{forecast.LaborUsed} / {s.LaborCapacity} AP Used";
        _forecastMorale.text = $"{(forecast.MoraleDelta >= 0 ? "+" : "")}{forecast.MoraleDelta}% Next Cycle";

        _overcrowdBanner.style.display = forecast.WillOvercrowd ? DisplayStyle.Flex : DisplayStyle.None;
        if (forecast.WillOvercrowd)
        {
            int over = _gsm.Population - s.HousingCapacity;
            _overcrowdDetail.text = $"{over} unit{(over == 1 ? "" : "s")} over Housing Capacity - build a Hab-Pod or morale will keep bleeding.";
        }

        // Disable "Execute Cycle" while over Labor budget rather than letting
        // ExecuteCycle() silently reject it.
        _btnExecute.SetEnabled(!forecast.IsOverLaborBudget);
    }

    private static void SetDelta(Label label, int value, string suffix = "")
    {
        label.text = $"{(value >= 0 ? "+" : "")}{value}{suffix}";
        label.RemoveFromClassList("text-positive");
        label.RemoveFromClassList("text-negative");
        label.AddToClassList(value >= 0 ? "text-positive" : "text-negative");
    }

    private static void SetTone(Label label, bool positive)
    {
        label.RemoveFromClassList("text-positive");
        label.RemoveFromClassList("text-negative");
        label.AddToClassList(positive ? "text-positive" : "text-negative");
    }

    private void FlashSyncStatus()
    {
        _ledgerSyncStatus.text = "[ CYCLE EXECUTED ]";
        _ledgerSyncStatus.RemoveFromClassList("text-primary-fixed-dim");
        _ledgerSyncStatus.AddToClassList("text-primary-container");

        // Simple delayed revert - swap for a coroutine/DOTween if you want
        // this eased instead of a hard cut.
        Invoke(nameof(ResetSyncStatus), 2f);
    }

    private void ResetSyncStatus()
    {
        _ledgerSyncStatus.text = "[ MATRIX STABLE ]";
        _ledgerSyncStatus.RemoveFromClassList("text-primary-container");
        _ledgerSyncStatus.AddToClassList("text-primary-fixed-dim");
    }
}
