using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Drives the Plan screen's labor directives, debrief strip and Vanguard
/// mirror (DecisionContent.uxml; course selection is CampaignMapController,
/// squad picking is PlanningPhaseController). Plain component now -
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

    // Combat Vanguard: mirrors the Roster tab's live selection rather than a stepper
    private VisualElement _vanguardRoster;
    private Label _vanguardEmptyHint;

    // Over-budget warning (the forecast numbers themselves live in the sidebar ledger)
    private Label _planWarning;

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

        _vanguardRoster = _root.Q<VisualElement>("vanguard-roster");
        _vanguardEmptyHint = _root.Q<Label>("vanguard-empty-hint");

        _planWarning = _root.Q<Label>("plan-warning");

        _btnExecute = _root.Q<Button>("btn-execute-cycle");
        _btnReset = _root.Q<Button>("btn-reset");
    }

    private void BindButtons()
    {
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
    /// Public, and also called by PhaseShellController whenever this tab
    /// becomes visible, since the Campaign Map tab can change
    /// SelectedCampaignAction (affecting the course label and the forecast's
    /// yield multiplier) while this tab isn't the one on screen.
    ///
    /// The Live Asset Ledger header itself is shared chrome now (visible on
    /// every tab), so PhaseShellController owns writing it - this just asks
    /// it to re-pull the same ComputeForecast() rather than duplicating that
    /// label-writing code here too.
    /// </summary>
    public void Refresh()
    {
        var a = _gsm.CurrentAllocation;
        var forecast = _gsm.ComputeForecast();

        PhaseShellController.Instance?.RefreshHeader();

        RefreshVanguardRoster();
        _countScavenge.text = a.ScavengeUnits.ToString();
        _countHarvest.text = a.HarvestUnits.ToString();
        _countExpansion.text = a.ExpansionUnits.ToString();

        if (_planWarning != null)
        {
            _planWarning.text = forecast.IsOverBudget ? "Not enough Scrap to build the queued ship." : "";
            if (forecast.IsOverBudget) _planWarning.AddToClassList("plan-warning--visible");
            else _planWarning.RemoveFromClassList("plan-warning--visible");
        }

        // Disable "Execute Cycle" when a queued ship can't be paid for, rather
        // than letting ExecuteCycle() silently reject it.
        _btnExecute.SetEnabled(!forecast.IsOverBudget);
    }

    /// <summary>
    /// Combat Vanguard has no stepper of its own - it mirrors whichever units
    /// are currently picked on the Roster tab (GameStateManager.ActiveTeam,
    /// kept live in sync by PlanningPhaseController.ToggleSelection). This just
    /// rebuilds the small icon+name chip row and the "N Selected" count/empty
    /// hint from that list; no game-state math happens here.
    /// </summary>
    private void RefreshVanguardRoster()
    {
        var activeTeam = _gsm.ActiveTeam;

        if (_countVanguard != null) _countVanguard.text = $"{activeTeam.Count} Selected";

        if (_vanguardRoster != null)
        {
            _vanguardRoster.Clear();
            foreach (var unit in activeTeam)
            {
                if (unit != null) _vanguardRoster.Add(BuildVanguardChip(unit));
            }
        }

        if (_vanguardEmptyHint != null)
            _vanguardEmptyHint.style.display = activeTeam.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static VisualElement BuildVanguardChip(Unit unit)
    {
        var chip = new VisualElement();
        chip.AddToClassList("vanguard-chip");

        var icon = new VisualElement();
        icon.AddToClassList("vanguard-chip__icon");
        if (unit.UnitIcon != null) icon.style.backgroundImage = new StyleBackground(unit.UnitIcon);
        chip.Add(icon);

        var name = new Label(unit.UnitName);
        name.AddToClassList("vanguard-chip__name");
        chip.Add(name);

        return chip;
    }

}
