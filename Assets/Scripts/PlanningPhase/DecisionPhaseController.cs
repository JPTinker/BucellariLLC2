using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Drives the Decision (resource-planning) tab content. Plain component now -
/// no longer owns a UIDocument. PhaseShellController instantiates
/// DecisionContent.uxml into the shared shell's content-slot and calls
/// Initialize() with the shell's rootVisualElement, same as
/// PlanningPhaseController for the Roster tab.
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
public class DecisionPhaseController : MonoBehaviour
{
    private VisualElement _root;
    private GameStateManager _gsm;

    // Debrief
    private Label _debriefMoraleTotal, _debriefKills, _debriefSaved, _debriefLosses;

    // Directive counters
    private Label _countVanguard, _countScavenge, _countHarvest, _countExpansion;

    // Forecast
    private Label _forecastFood, _forecastScrap, _forecastVillagers, _forecastMorale;

    // Buttons
    private Button _btnExecute, _btnReset;

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



    public void Initialize(VisualElement shellRoot)
    {
        _root = shellRoot;
        _gsm = GameStateManager.Instance;

        QueryElements();
        BindButtons();
        ShowDebrief();
        Refresh();
    }

    private void QueryElements()
    {
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
        _forecastVillagers = _root.Q<Label>("forecast-villagers");
        _forecastMorale = _root.Q<Label>("forecast-morale");

        _btnExecute = _root.Q<Button>("btn-execute-cycle");
        _btnReset = _root.Q<Button>("btn-reset");
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
                // A queued ship can't be paid for - ComputeForecast().IsOverBudget
                // already disables this via Refresh(), but guard here too in case
                // the button state is stale.
                return;
            }
            PhaseShellController.Instance?.FlashSyncStatus();
            Refresh();
        };
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
    ///
    /// The Live Asset Ledger header itself is shared chrome now (visible on
    /// every tab), so PhaseShellController owns writing it - this just asks
    /// it to re-pull the same ComputeForecast() rather than duplicating that
    /// label-writing code here too.
    /// </summary>
    private void Refresh()
    {
        var a = _gsm.CurrentAllocation;
        var forecast = _gsm.ComputeForecast();

        PhaseShellController.Instance?.RefreshHeader();

        _countVanguard.text = a.VanguardUnits.ToString();
        _countScavenge.text = a.ScavengeUnits.ToString();
        _countHarvest.text = a.HarvestUnits.ToString();
        _countExpansion.text = a.ExpansionUnits.ToString();

        _forecastFood.text = $"Net {(forecast.FoodDelta >= 0 ? "+" : "")}{forecast.FoodDelta} ({(forecast.FoodDelta >= 0 ? "Surplus" : "Deficit")})";
        SetTone(_forecastFood, forecast.FoodDelta >= 0);

        _forecastScrap.text = $"Net {(forecast.MaterialsDelta >= 0 ? "+" : "")}{forecast.MaterialsDelta}" + (forecast.WillBuildShip ? " (Ship)" : "");
        SetTone(_forecastScrap, forecast.MaterialsDelta >= 0);

        _forecastVillagers.text = $"{forecast.VillagersSent} Sent / {forecast.IdleVillagers} Idle";
        _forecastMorale.text = $"{(forecast.MoraleDelta >= 0 ? "+" : "")}{forecast.MoraleDelta}% Next Cycle";

        // Disable "Execute Cycle" when a queued ship can't be paid for, rather
        // than letting ExecuteCycle() silently reject it.
        _btnExecute.SetEnabled(!forecast.IsOverBudget);
    }

    private static void SetTone(Label label, bool positive)
    {
        label.RemoveFromClassList("text-positive");
        label.RemoveFromClassList("text-negative");
        label.AddToClassList(positive ? "text-positive" : "text-negative");
    }
}
