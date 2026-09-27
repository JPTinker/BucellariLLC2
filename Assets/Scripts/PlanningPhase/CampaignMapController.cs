using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Drives the Campaign Map tab content. Plain component - no UIDocument of its
/// own. PhaseShellController instantiates CampaignMapContent.uxml into the
/// shared shell's content-slot and calls Initialize() with the shell's
/// rootVisualElement, same as PlanningPhaseController/DecisionPhaseController.
///
/// Lets the player pick one of three courses for the upcoming cycle - Stay
/// Put, Travel, Rest - stored on GameStateManager.SelectedCampaignAction and
/// consumed by ComputeForecast()/ExecuteCycle() on the Decision tab. This
/// screen only reads/writes that selection; the actual yield-doubling/
/// healing/evacuation-progress math lives in GameStateManager.
/// </summary>
public class CampaignMapController : MonoBehaviour
{
    [Tooltip("The long ships background image shown behind the campaign map hero panel.")]
    [SerializeField] private Sprite heroBackground;

    private VisualElement _root;
    private GameStateManager _gsm;

    private VisualElement _heroImage;
    private Label _evacuationStatus;

    private Button _optionStayPut, _optionTravel, _optionRest;

    public void Initialize(VisualElement shellRoot)
    {
        _root = shellRoot;
        _gsm = GameStateManager.Instance;

        QueryElements();
        ApplyHeroBackground();
        BindOptions();
        Refresh();
    }

    private void QueryElements()
    {
        _heroImage = _root.Q<VisualElement>("campaign-map-hero-image");
        _evacuationStatus = _root.Q<Label>("evacuation-status");

        _optionStayPut = _root.Q<Button>("option-stay-put");
        _optionTravel = _root.Q<Button>("option-travel");
        _optionRest = _root.Q<Button>("option-rest");

        WarnIfMissing(_heroImage, "campaign-map-hero-image");
        WarnIfMissing(_evacuationStatus, "evacuation-status");
        WarnIfMissing(_optionStayPut, "option-stay-put");
        WarnIfMissing(_optionTravel, "option-travel");
        WarnIfMissing(_optionRest, "option-rest");
    }

    private void WarnIfMissing(VisualElement element, string expectedName)
    {
        if (element == null)
        {
            Debug.LogError($"CampaignMapController: could not find an element named '{expectedName}' " +
                            "in the UXML. Check CampaignMapContent.uxml still defines it.");
        }
    }

    private void ApplyHeroBackground()
    {
        if (_heroImage != null && heroBackground != null)
            _heroImage.style.backgroundImage = new StyleBackground(heroBackground);
    }

    private void BindOptions()
    {
        RegisterOption(_optionStayPut, GameStateManager.CampaignAction.StayPut);
        RegisterOption(_optionTravel, GameStateManager.CampaignAction.Travel);
        RegisterOption(_optionRest, GameStateManager.CampaignAction.Rest);
    }

    private void RegisterOption(Button option, GameStateManager.CampaignAction action)
    {
        if (option == null) return;
        option.clicked += () =>
        {
            _gsm.SelectCampaignAction(action);
            Refresh();
        };
    }

    private void Refresh()
    {
        if (_gsm == null) return;

        bool hasSelection = _gsm.HasSelectedCampaignAction;
        SetSelected(_optionStayPut, hasSelection && _gsm.SelectedCampaignAction == GameStateManager.CampaignAction.StayPut);
        SetSelected(_optionTravel, hasSelection && _gsm.SelectedCampaignAction == GameStateManager.CampaignAction.Travel);
        SetSelected(_optionRest, hasSelection && _gsm.SelectedCampaignAction == GameStateManager.CampaignAction.Rest);

        if (_evacuationStatus != null)
        {
            _evacuationStatus.text = _gsm.EvacuationCyclesRemaining > 0
                ? $"{_gsm.EvacuationCyclesRemaining} Cycles Until Evacuation Zone"
                : "Evacuation Zone Reached";
        }

        // The chosen course changes what the Decision tab's forecast (and
        // therefore the shared header) would yield this cycle - refresh it
        // immediately rather than waiting for the player to switch tabs.
        PhaseShellController.Instance?.RefreshHeader();
    }

    private static void SetSelected(Button option, bool selected)
    {
        if (option == null) return;
        if (selected) option.AddToClassList("campaign-option--selected");
        else option.RemoveFromClassList("campaign-option--selected");
    }
}
