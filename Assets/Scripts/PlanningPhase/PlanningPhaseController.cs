using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Drives the pre-combat "Planning Phase" team-selection screen.
/// Attach to the same GameObject as the UIDocument for MapPhase.uxml.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class PlanningPhaseController : MonoBehaviour
{
    [Header("UXML Template")]
    [Tooltip("Assign UnitCard.uxml here. Each roster/draft card is instantiated from this template.")]
    [SerializeField] private VisualTreeAsset unitCardTemplate;

    [Header("Fanfare Timing")]
    [Tooltip("How long the reveal card stays fully visible before auto-advancing (seconds). Set to 0 to require a tap instead.")]
    public float revealHoldSeconds = 1.6f;
    [Tooltip("Duration of the punch-in bounce for the reveal card and draft cards.")]
    public float punchInDuration = 0.32f;
    [Tooltip("How far the punch-in overshoots past full size (1.0 = no overshoot).")]
    public float punchInOvershoot = 1.12f;

    private UIDocument _document;
    private VisualElement _root;

    private ScrollView _unitList;
    private Button _engageButton;
    private Label _selectionCounter;

    private VisualElement _revealOverlay;
    private VisualElement _revealCard;
    private VisualElement _revealFlash;
    private VisualElement _revealIcon;
    private Label _revealName;

    private VisualElement _draftOverlay;
    private VisualElement _draftOptionsContainer;
    private UnitData _draftSelectedUnit;
    private bool _draftSelectionMade;

    private Button _navRoster;
    private Button _navMap;
    private Button _navInventory;

    // Selection state
    private readonly List<Unit> _selectedUnits = new List<Unit>();
    private readonly Dictionary<Unit, VisualElement> _cardsByUnit = new Dictionary<Unit, VisualElement>();

    // Reveal queue state
    private readonly Queue<Unit> _revealQueue = new Queue<Unit>();
    private bool _revealInProgress;
    private bool _revealAdvanceRequested;

    private void OnEnable()
    {
        _document = GetComponent<UIDocument>();
        _root = _document.rootVisualElement;

        _unitList = _root.Q<ScrollView>("unit-list");
        _engageButton = _root.Q<Button>("btn-engage");
        _selectionCounter = _root.Q<Label>("selection-counter");

        _revealOverlay = _root.Q<VisualElement>("reveal-overlay");
        _revealCard = _root.Q<VisualElement>("reveal-card");
        _revealFlash = _root.Q<VisualElement>("reveal-flash");
        _revealIcon = _root.Q<VisualElement>("reveal-icon");
        _revealName = _root.Q<Label>("reveal-name");

        _draftOverlay = _root.Q<VisualElement>("draft-overlay");
        _draftOptionsContainer = _root.Q<VisualElement>("draft-options");

        _navRoster = _root.Q<Button>("nav-roster");
        _navMap = _root.Q<Button>("nav-map");
        _navInventory = _root.Q<Button>("nav-inventory");

        WarnIfMissing(_unitList, "unit-list");
        WarnIfMissing(_engageButton, "btn-engage");
        WarnIfMissing(_selectionCounter, "selection-counter");
        WarnIfMissing(_revealOverlay, "reveal-overlay");
        WarnIfMissing(_revealCard, "reveal-card");
        WarnIfMissing(_revealIcon, "reveal-icon");
        WarnIfMissing(_revealName, "reveal-name");
        WarnIfMissing(_draftOverlay, "draft-overlay");
        WarnIfMissing(_draftOptionsContainer, "draft-options");

        if (unitCardTemplate == null)
        {
            Debug.LogError("PlanningPhaseController: 'Unit Card Template' is not assigned in the Inspector. " +
                            "Drag UnitCard.uxml into that field.");
        }

        // Cards read/wrap in a row rather than stacking vertically, since the
        // ScrollView's internal content-container class isn't stable across
        // Unity versions to target from USS.
        if (_unitList != null)
        {
            _unitList.contentContainer.style.flexDirection = FlexDirection.Row;
            _unitList.contentContainer.style.flexWrap = Wrap.Wrap;
        }

        if (_engageButton != null) _engageButton.clicked += OnEngageClicked;
        _revealOverlay?.RegisterCallback<ClickEvent>(OnRevealTapped);

        // Only Roster is functional right now; Campaign Map and Inventory are
        // visible but disabled until those screens exist.
        _navMap?.SetEnabled(false);
        _navInventory?.SetEnabled(false);

        BuildInitialState();
    }

    private void WarnIfMissing(VisualElement element, string expectedName)
    {
        if (element == null)
        {
            Debug.LogError($"PlanningPhaseController: could not find an element named '{expectedName}' " +
                            "in the UXML. Check MapPhase.uxml still defines it.");
        }
    }

    private void OnDisable()
    {
        if (_engageButton != null) _engageButton.clicked -= OnEngageClicked;
        if (_revealOverlay != null) _revealOverlay.UnregisterCallback<ClickEvent>(OnRevealTapped);
    }

    private void BuildInitialState()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null)
        {
            Debug.LogError("PlanningPhaseController: GameStateManager.Instance is null.");
            return;
        }

        RefreshRosterList();
        RefreshSelectionCounter();

        // A pending draft (choose 1 of N new recruits) always takes priority: the
        // player picks first, then their pick plays through the reveal animation
        // before settling into the roster list.
        if (gsm.PendingDraftOptions.Count > 0 && _draftOverlay != null && _draftOptionsContainer != null)
        {
            StartCoroutine(PlayDraftSequence());
        }
        else
        {
            EnqueueAndPlayReveals();
        }
    }
    public void LevelChangeDraftOffer()
    {
        var gsm = GameStateManager.Instance;
        if (gsm.PendingDraftOptions.Count > 0 && _draftOverlay != null && _draftOptionsContainer != null)
        {
            StartCoroutine(PlayDraftSequence());
        }
        else
        {
            EnqueueAndPlayReveals();
        }        
    }

    private void EnqueueAndPlayReveals()
    {
        var gsm = GameStateManager.Instance;
        if (gsm.PendingReveal.Count == 0) return;
        if (_revealOverlay == null || _revealCard == null) return;

        foreach (var unit in gsm.PendingReveal)
            _revealQueue.Enqueue(unit);

        StartCoroutine(PlayRevealQueue());
    }

    // ---------------------------------------------------------------
    // Roster list
    // ---------------------------------------------------------------

    private void RefreshRosterList()
    {
        if (_unitList == null || unitCardTemplate == null) return;

        _unitList.Clear();
        _cardsByUnit.Clear();

        foreach (var unit in GameStateManager.Instance.FullRoster)
        {
            if (unit == null) continue;
            var card = BuildUnitCard(unit);
            _cardsByUnit[unit] = card;
            _unitList.Add(card);
        }
    }

    private VisualElement BuildUnitCard(Unit unit)
    {
        var card = unitCardTemplate.Instantiate();
        var cardRoot = card.Q<VisualElement>("card-root");

        PopulateCardTemplate(card, unit.UnitIcon, unit.UnitName, unit.MaxHP, unit.BaseAttack);

        var footerTag = card.Q<Label>("card-footer-tag");
        if (footerTag != null) footerTag.text = "[ \u2713 SELECTED ]";

        cardRoot.RegisterCallback<ClickEvent>(_ => ToggleSelection(unit, cardRoot));

        return card;
    }

    /// <summary>
    /// Fills in the shared UnitCard.uxml template's icon/name/stat fields.
    /// Plain numbers, no bars/pips - stats are shown as raw values.
    /// </summary>
    private void PopulateCardTemplate(VisualElement card, Sprite icon, string unitName, int hp, int atk)
    {
        var iconFrame = card.Q<VisualElement>("card-icon-frame");
        if (iconFrame != null && icon != null)
        {
            iconFrame.style.backgroundImage = new StyleBackground(icon);
        }

        var nameLabel = card.Q<Label>("card-name");
        if (nameLabel != null) nameLabel.text = unitName.ToUpperInvariant();

        var hpValue = card.Q<Label>("card-hp-value");
        if (hpValue != null) hpValue.text = hp.ToString();

        var atkValue = card.Q<Label>("card-atk-value");
        if (atkValue != null) atkValue.text = atk.ToString();
    }

    // ---------------------------------------------------------------
    // Selection
    // ---------------------------------------------------------------

    private void ToggleSelection(Unit unit, VisualElement card)
    {
        if (_selectedUnits.Contains(unit))
        {
            _selectedUnits.Remove(unit);
            card.RemoveFromClassList("unit-card-selected");
        }
        else
        {
            if (_selectedUnits.Count >= GameStateManager.MaxTeamSize)
            {
                StartCoroutine(PulseDenied(card));
                return;
            }

            _selectedUnits.Add(unit);
            card.AddToClassList("unit-card-selected");
        }

        RefreshSelectionCounter();
    }

    private IEnumerator PulseDenied(VisualElement card)
    {
        card.AddToClassList("unit-card-denied");
        yield return new WaitForSeconds(0.2f);
        card.RemoveFromClassList("unit-card-denied");
    }

    private void RefreshSelectionCounter()
    {
        if (_selectionCounter != null)
            _selectionCounter.text = $"{_selectedUnits.Count} / {GameStateManager.MaxTeamSize} SELECTED";
        _engageButton?.SetEnabled(_selectedUnits.Count > 0);
    }

    private void OnEngageClicked()
    {
        if (GameStateManager.Instance.SetActiveTeam(_selectedUnits))
        {
            GameStateManager.Instance.LoadCombatMap();
        }
    }

    // ---------------------------------------------------------------
    // Fanfare (punch-in bounce + flash), shared by reveal + draft cards
    // ---------------------------------------------------------------

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float p = x - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }

    /// <summary>
    /// Scales/fades an element in with a bouncy overshoot rather than a linear tween.
    /// The ease-out-back curve itself overshoots past 1.0 mid-animation and settles
    /// at exactly 1.0 by the end - `overshoot` is currently unused but kept as a
    /// tunable hook if you want to scale the bounce amount later.
    /// </summary>
    private IEnumerator PunchIn(VisualElement el, float duration, float overshoot)
    {
        el.style.opacity = 0f;
        el.style.scale = new StyleScale(new Scale(Vector2.zero));

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            float scale = p >= 1f ? 1f : EaseOutBack(p);

            el.style.opacity = Mathf.Clamp01(p * 2.2f);
            el.style.scale = new StyleScale(new Scale(new Vector2(scale, scale)));
            yield return null;
        }

        el.style.opacity = 1f;
        el.style.scale = new StyleScale(new Scale(Vector2.one));
    }

    private IEnumerator PlayFlash(VisualElement flash, float duration = 0.45f)
    {
        if (flash == null) yield break;

        flash.style.opacity = 1f;
        flash.style.scale = new StyleScale(new Scale(new Vector2(0.3f, 0.3f)));

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            float scale = Mathf.Lerp(0.3f, 3.5f, p);
            flash.style.scale = new StyleScale(new Scale(new Vector2(scale, scale)));
            flash.style.opacity = 1f - p;
            yield return null;
        }

        flash.style.opacity = 0f;
    }

    // ---------------------------------------------------------------
    // New-unit reveal animation
    // ---------------------------------------------------------------

    private IEnumerator PlayRevealQueue()
    {
        _revealInProgress = true;
        _revealOverlay.RemoveFromClassList("hidden");

        while (_revealQueue.Count > 0)
        {
            var unit = _revealQueue.Dequeue();
            PopulateRevealCard(unit);

            StartCoroutine(PlayFlash(_revealFlash));
            yield return StartCoroutine(PunchIn(_revealCard, punchInDuration, punchInOvershoot));

            _revealAdvanceRequested = false;
            if (revealHoldSeconds > 0f)
            {
                float t = 0f;
                while (t < revealHoldSeconds && !_revealAdvanceRequested)
                {
                    t += Time.deltaTime;
                    yield return null;
                }
            }
            else
            {
                while (!_revealAdvanceRequested) yield return null;
            }

            _revealCard.style.opacity = 0f;
            yield return new WaitForSeconds(0.1f);
        }

        _revealOverlay.AddToClassList("hidden");
        GameStateManager.Instance.ClearPendingReveal();
        _revealInProgress = false;
    }

    private void PopulateRevealCard(Unit unit)
    {
        _revealName.text = unit.UnitName.ToUpperInvariant();
        if (unit.UnitIcon != null)
        {
            _revealIcon.style.backgroundImage = new StyleBackground(unit.UnitIcon);
        }
    }

    private void OnRevealTapped(ClickEvent evt)
    {
        if (_revealInProgress) _revealAdvanceRequested = true;
    }

    // ---------------------------------------------------------------
    // New-unit draft (choose 1 of N)
    // ---------------------------------------------------------------

    private IEnumerator PlayDraftSequence()
    {
        var options = new List<UnitData>(GameStateManager.Instance.PendingDraftOptions);
        var draftCards = BuildDraftOptions(options);

        _draftOverlay.RemoveFromClassList("hidden");
        _draftSelectionMade = false;
        _draftSelectedUnit = null;

        // Stagger each card's punch-in slightly so they don't all pop at once.
        for (int i = 0; i < draftCards.Count; i++)
        {
            StartCoroutine(PunchIn(draftCards[i], punchInDuration, punchInOvershoot));
            yield return new WaitForSeconds(0.08f);
        }

        while (!_draftSelectionMade) yield return null;

        _draftOverlay.AddToClassList("hidden");
        _draftOptionsContainer.Clear();

        GameStateManager.Instance.ResolveUnitDraft(_draftSelectedUnit);

        RefreshRosterList();
        RefreshSelectionCounter();

        if (GameStateManager.Instance.PendingDraftsToOffer > 0)
        {
            GameStateManager.Instance.OfferNextUnitDraft();
            yield return StartCoroutine(PlayDraftSequence());
        }
        else
        {
            EnqueueAndPlayReveals();
        }
    }

    private List<VisualElement> BuildDraftOptions(List<UnitData> options)
    {
        var built = new List<VisualElement>();
        if (_draftOptionsContainer == null || unitCardTemplate == null) return built;

        _draftOptionsContainer.Clear();

        foreach (var unit in options)
        {
            if (unit == null) continue;
            var card = BuildDraftCard(unit);
            _draftOptionsContainer.Add(card);
            built.Add(card);
        }

        return built;
    }

    private VisualElement BuildDraftCard(UnitData unit)
    {
        var card = unitCardTemplate.Instantiate();
        var cardRoot = card.Q<VisualElement>("card-root");
        cardRoot.AddToClassList("draft-card");

        PopulateCardTemplate(card, unit.UnitIcon, unit.UnitName, unit.MaxHP, unit.BaseAttack);

        var footerTag = card.Q<Label>("card-footer-tag");
        if (footerTag != null)
        {
            footerTag.text = "[ CHOOSE ]";
            footerTag.style.display = DisplayStyle.Flex;
        }

        cardRoot.RegisterCallback<ClickEvent>(_ =>
        {
            _draftSelectedUnit = unit;
            _draftSelectionMade = true;
        });

        return card;
    }
}
