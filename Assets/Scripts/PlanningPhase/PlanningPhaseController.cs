using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Drives the pre-combat "Planning Phase" team-selection screen.
///
/// No longer owns a UIDocument itself. PhaseShellController instantiates
/// DecisionContent.uxml into the shared shell's content-slot and calls
/// Initialize() with the shell's rootVisualElement (not just this content's
/// own subtree - the reveal/level-up/draft overlays live at the shell level
/// now, so this still needs to reach them by name). Nav buttons
/// (Roster/Decision/Campaign Map/Store) moved to the shell's shared footer
/// too, so this class no longer queries or wires them.
/// </summary>
public class PlanningPhaseController : MonoBehaviour
{
    [Header("UXML Template")]
    [Tooltip("Assign UnitCard_updated.uxml here. Each roster/draft card is instantiated from this template.")]
    [SerializeField] private VisualTreeAsset unitCardTemplate;

    [Header("Fanfare Timing")]
    [Tooltip("How long the reveal card stays fully visible before auto-advancing (seconds). Set to 0 to require a tap instead.")]
    public float revealHoldSeconds = 1.6f;
    [Tooltip("How long the level-up card stays fully visible before auto-advancing (seconds). Set to 0 to require a tap instead.")]
    public float levelUpHoldSeconds = 1.6f;
    [Tooltip("Duration of the punch-in bounce for the reveal card and draft cards.")]
    public float punchInDuration = 0.32f;
    [Tooltip("How far the punch-in overshoots past full size (1.0 = no overshoot).")]
    public float punchInOvershoot = 1.12f;

    private VisualElement _root;

    private ScrollView _unitList;
    private Button _engageButton;

    private VisualElement _revealOverlay;
    private VisualElement _revealCard;
    private VisualElement _revealFlash;
    private VisualElement _revealIcon;
    private Label _revealName;

    private VisualElement _levelUpOverlay;
    private VisualElement _levelUpCard;
    private VisualElement _levelUpFlash;
    private VisualElement _levelUpIcon;
    private Label _levelUpName;
    private Label _levelUpLevel;
    private Label _levelUpHpValue;
    private Label _levelUpAtkValue;

    private VisualElement _draftOverlay;
    private VisualElement _draftOptionsContainer;
    private UnitData _draftSelectedUnit;
    private UnitRarity _draftSelectedRarity;
    private bool _draftSelectedVillager;
    private bool _draftSelectionMade;

    // Selection state
    private readonly List<Unit> _selectedUnits = new List<Unit>();
    private readonly Dictionary<Unit, VisualElement> _cardsByUnit = new Dictionary<Unit, VisualElement>();

    /// <summary>The units currently picked for the next battle. Read by the Decision tab's Combat Vanguard card.</summary>
    public IReadOnlyList<Unit> SelectedUnits => _selectedUnits;

    // Reveal queue state
    private readonly Queue<Unit> _revealQueue = new Queue<Unit>();
    private bool _revealInProgress;
    private bool _revealAdvanceRequested;

    // Level-up queue state
    private bool _levelUpInProgress;
    private bool _levelUpAdvanceRequested;

    /// <summary>
    /// Called once by PhaseShellController right after it instantiates
    /// DecisionContent.uxml into the shell's content-slot. Replaces what used
    /// to be OnEnable. `shellRoot` is the shell's WHOLE rootVisualElement,
    /// not just this content's own subtree, because the reveal/level-up/
    /// draft overlays live at the shell level (PhaseShell.uxml) so they can
    /// cover the header and footer too - every element name below is still
    /// unique across the whole tree, so Q<>() from the shell root works the
    /// same as it used to from this content's own root.
    /// </summary>
    public void Initialize(VisualElement shellRoot)
    {
        _root = shellRoot;

        _unitList = _root.Q<ScrollView>("unit-list");
        _engageButton = _root.Q<Button>("btn-engage");

        _revealOverlay = _root.Q<VisualElement>("reveal-overlay");
        _revealCard = _root.Q<VisualElement>("reveal-card");
        _revealFlash = _root.Q<VisualElement>("reveal-flash");
        _revealIcon = _root.Q<VisualElement>("reveal-icon");
        _revealName = _root.Q<Label>("reveal-name");

        _levelUpOverlay = _root.Q<VisualElement>("level-up-overlay");
        _levelUpCard = _root.Q<VisualElement>("level-up-card");
        _levelUpFlash = _root.Q<VisualElement>("level-up-flash");
        _levelUpIcon = _root.Q<VisualElement>("level-up-icon");
        _levelUpName = _root.Q<Label>("level-up-name");
        _levelUpLevel = _root.Q<Label>("level-up-level");
        _levelUpHpValue = _root.Q<Label>("level-up-hp-value");
        _levelUpAtkValue = _root.Q<Label>("level-up-atk-value");

        _draftOverlay = _root.Q<VisualElement>("draft-overlay");
        _draftOptionsContainer = _root.Q<VisualElement>("draft-options");

        WarnIfMissing(_unitList, "unit-list");
        WarnIfMissing(_engageButton, "btn-engage");
        WarnIfMissing(_revealOverlay, "reveal-overlay");
        WarnIfMissing(_revealCard, "reveal-card");
        WarnIfMissing(_revealIcon, "reveal-icon");
        WarnIfMissing(_revealName, "reveal-name");
        WarnIfMissing(_levelUpOverlay, "level-up-overlay");
        WarnIfMissing(_levelUpCard, "level-up-card");
        WarnIfMissing(_levelUpIcon, "level-up-icon");
        WarnIfMissing(_levelUpName, "level-up-name");
        WarnIfMissing(_levelUpLevel, "level-up-level");
        WarnIfMissing(_levelUpHpValue, "level-up-hp-value");
        WarnIfMissing(_levelUpAtkValue, "level-up-atk-value");
        WarnIfMissing(_draftOverlay, "draft-overlay");
        WarnIfMissing(_draftOptionsContainer, "draft-options");

        if (unitCardTemplate == null)
        {
            Debug.LogError("PlanningPhaseController: 'Unit Card Template' is not assigned in the Inspector. " +
                            "Drag UnitCard_updated.uxml into that field.");
        }

        // Cards flow left-to-right and wrap onto a new line once a row runs out
        // of width, rather than stacking in a single column. Set via code, not
        // USS, since the ScrollView's internal content-container class isn't
        // stable across Unity versions.
        if (_unitList != null)
        {
            _unitList.contentContainer.style.flexDirection = FlexDirection.Row;
            _unitList.contentContainer.style.flexWrap = Wrap.Wrap;
        }

        if (_engageButton != null) _engageButton.clicked += OnEngageClicked;
        _revealOverlay?.RegisterCallback<ClickEvent>(OnRevealTapped);
        _levelUpOverlay?.RegisterCallback<ClickEvent>(OnLevelUpTapped);

        BuildInitialState();
    }

    private void WarnIfMissing(VisualElement element, string expectedName)
    {
        if (element == null)
        {
            Debug.LogError($"PlanningPhaseController: could not find an element named '{expectedName}' " +
                            "in the UXML. Check DecisionContent.uxml (or PhaseShell.uxml, for the overlays) still defines it.");
        }
    }

    private void OnDisable()
    {
        if (_engageButton != null) _engageButton.clicked -= OnEngageClicked;
        if (_revealOverlay != null) _revealOverlay.UnregisterCallback<ClickEvent>(OnRevealTapped);
        if (_levelUpOverlay != null) _levelUpOverlay.UnregisterCallback<ClickEvent>(OnLevelUpTapped);
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
        // before settling into the roster list. Level-ups and new-recruit reveals
        // follow, in that order, once any draft is resolved.
        if (gsm.HasPendingDraft && _draftOverlay != null && _draftOptionsContainer != null)
        {
            StartCoroutine(PlayDraftSequence());
        }
        else
        {
            StartCoroutine(PlayLevelUpsThenReveals());
        }
    }
    public void LevelChangeDraftOffer()
    {
        var gsm = GameStateManager.Instance;
        if (gsm.HasPendingDraft &&_draftOverlay != null && _draftOptionsContainer != null)
        {
            StartCoroutine(PlayDraftSequence());
        }
        else
        {
            StartCoroutine(PlayLevelUpsThenReveals());
        }        
    }

    /// <summary>
    /// Plays the level-up queue (if any), then the new-unit reveal queue (if any).
    /// This is the tail every entry point (draft-less startup, and the end of a
    /// resolved draft chain) funnels into.
    /// </summary>
    private IEnumerator PlayLevelUpsThenReveals()
    {
        if (GameStateManager.Instance.PendingLevelUps.Count > 0 && _levelUpOverlay != null && _levelUpCard != null)
        {
            yield return StartCoroutine(PlayLevelUpQueue());
        }

        EnqueueAndPlayReveals();
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
        var cardRoot = card.Q<VisualElement>("player-card");

        PopulateCardTemplate(card, unit.UnitIcon, unit.UnitName, unit.Level, unit.CurrentHP, unit.MaxHP,
            unit.BaseAttack, unit.AttackRange, unit.MaxMovementPoints, unit.Faction, unit.Rarity, unit.Wounds);

        SetCardTag(card, "[ \u2713 SELECTED ]");

        cardRoot.RegisterCallback<ClickEvent>(_ => ToggleSelection(unit, cardRoot));

        cardRoot.Add(BuildDismissButton(unit));

        return card;
    }

    /// <summary>
    /// Two-click "dismiss" button: the first click arms it ("CONFIRM?"), the second
    /// within 3 seconds converts the unit into a Villager.
    /// </summary>
    private Button BuildDismissButton(Unit unit)
    {
        const string idleText = "DISMISS → +1 VILLAGER";
        var button = new Button { text = idleText };
        button.AddToClassList("unit-dismiss-button");
        button.style.marginTop = 4;
        button.style.fontSize = 11;

        IVisualElementScheduledItem resetTimer = null;
        button.clicked += () =>
        {
            var gsm = GameStateManager.Instance;
            if (gsm == null) return;

            if (!gsm.CanDismissUnit(unit, out string reason))
            {
                FindAnyObjectByType<NotificationManager>()?.ShowNotification(reason);
                return;
            }

            if (button.text == idleText)
            {
                button.text = "CONFIRM? (cannot undo)";
                resetTimer = button.schedule.Execute(() => button.text = idleText);
                resetTimer.ExecuteLater(3000);
                return;
            }

            resetTimer?.Pause();
            _selectedUnits.Remove(unit);
            gsm.DismissUnitForVillager(unit);
            RefreshRosterList();
            RefreshSelectionCounter();
            FindAnyObjectByType<NotificationManager>()?.ShowNotification($"{unit.UnitName} dismissed - +1 villager");
        };

        // Don't let the click also toggle the card's selection.
        button.RegisterCallback<ClickEvent>(e => e.StopPropagation());
        return button;
    }

    /// <summary>
    /// Fills in the shared UnitCard_updated.uxml template's portrait/identity/stat
    /// fields. The template has no dedicated footer-tag element (that concept comes
    /// from the old UnitCard.uxml), so selection/draft state is shown via the
    /// actions-panel row instead - see SetCardTag.
    /// </summary>
    private void PopulateCardTemplate(VisualElement card, Sprite icon, string unitName, int level, int currentHp,
        int maxHp, int atk, int attackRange, int moveRange, UnitFaction faction, UnitRarity rarity, int wounds = 0)
    {
        // Rarity drives the frame/label accent via a "rarity-<tier>" modifier class.
        var cardRoot = card.Q<VisualElement>("player-card");
        cardRoot?.AddToClassList(UnitRarityTable.GetUssClass(rarity));

        var rarityLabel = card.Q<Label>("rarity-label");
        if (rarityLabel != null) rarityLabel.text = UnitRarityTable.GetDisplayName(rarity);

        var portrait = card.Q<VisualElement>("character-portrait");
        if (portrait != null && icon != null)
        {
            portrait.style.backgroundImage = new StyleBackground(icon);
        }

        var levelLabel = card.Q<Label>("level-label");
        if (levelLabel != null) levelLabel.text = $"LVL {level}";

        var nameLabel = card.Q<Label>("unit-name");
        if (nameLabel != null) nameLabel.text = unitName.ToUpperInvariant();

        var designationLabel = card.Q<Label>("unit-designation");
        if (designationLabel != null) designationLabel.text = attackRange > 1 ? "RANGED ATTACKER" : "FRONTLINE FIGHTER";

        // No per-class icon glyphs yet - hide the emblem rather than show a wrong one.
        var classEmblem = card.Q<VisualElement>("unit-class-emblem");
        if (classEmblem != null) classEmblem.style.display = DisplayStyle.None;

        var moraleLabel = card.Q<Label>("morale-label");
        var moraleDot = card.Q<VisualElement>("morale-dot");
        bool ignoresWounds = rarity == UnitRarity.Mythical;
        if (moraleLabel != null)
        {
            if (ignoresWounds) moraleLabel.text = "UNSCARRABLE";
            else moraleLabel.text = wounds > 0 ? $"{wounds} WOUND{(wounds == 1 ? "" : "S")}" : "UNWOUNDED";
        }
        if (moraleDot != null)
        {
            if (ignoresWounds) moraleDot.style.backgroundColor = UnitRarityTable.GetColor(rarity);
            else moraleDot.style.backgroundColor = wounds > 0 ? new Color(0.87f, 0.35f, 0.31f) : new Color(0.44f, 0.89f, 0.71f);
        }

        var factionLabel = card.Q<Label>("faction-label");
        if (factionLabel != null) factionLabel.text = faction.ToString().ToUpperInvariant();

        int clampedMax = Mathf.Max(1, maxHp);
        int clampedCurrent = Mathf.Clamp(currentHp, 0, clampedMax);
        int pct = Mathf.RoundToInt(100f * clampedCurrent / clampedMax);

        var currentHpLabel = card.Q<Label>("current-hp");
        if (currentHpLabel != null) currentHpLabel.text = clampedCurrent.ToString();

        var maxHpLabel = card.Q<Label>("max-hp");
        if (maxHpLabel != null) maxHpLabel.text = clampedMax.ToString();

        var hpPercentLabel = card.Q<Label>("hp-percentage");
        if (hpPercentLabel != null) hpPercentLabel.text = $"({pct}%)";

        var healthFill = card.Q<VisualElement>("health-fill");
        if (healthFill != null) healthFill.style.width = new Length(pct, LengthUnit.Percent);

        var attackRangeValue = card.Q<Label>("attack-range-value");
        if (attackRangeValue != null) attackRangeValue.text = attackRange.ToString();

        var movementRangeValue = card.Q<Label>("movement-range-value");
        if (movementRangeValue != null) movementRangeValue.text = moveRange.ToString();

        // The template has no raw attack-power field; fold it into the designation
        // row so the number the old card always showed (ATK) is still visible.
        if (designationLabel != null) designationLabel.text += $"  \u00b7  ATK {atk}";

        // Pre-combat cards have no action economy yet - the actions-panel is
        // repurposed as a status/tag strip instead (see SetCardTag).
        var actionPips = card.Q<VisualElement>("action-pips");
        if (actionPips != null) actionPips.style.display = DisplayStyle.None;
    }

    /// <summary>
    /// Shows short status text (selection state, draft prompt, etc.) in the
    /// actions-panel strip at the bottom of the card - the closest equivalent
    /// the new template has to the old card-footer-tag label.
    /// </summary>
    private void SetCardTag(VisualElement card, string text)
    {
        var tagLabel = card.Q<Label>("actions-label");
        if (tagLabel != null) tagLabel.text = text;
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

        // Mirror the live selection into ActiveTeam (not just on Engage) so the
        // Decision tab's Combat Vanguard card - and the shared header's idle-unit
        // count, which now derives from ActiveTeam rather than a manual stepper -
        // both reflect who's picked without requiring a separate commit step.
        GameStateManager.Instance.ActiveTeam = new List<Unit>(_selectedUnits);
        PhaseShellController.Instance?.RefreshHeader();
    }

    private IEnumerator PulseDenied(VisualElement card)
    {
        card.AddToClassList("unit-card-denied");
        yield return new WaitForSeconds(0.2f);
        card.RemoveFromClassList("unit-card-denied");
    }

    // The squad count now lives in the sidebar readiness checklist, which
    // PhaseShellController.RefreshHeader() rewrites from SelectedUnits.
    private void RefreshSelectionCounter()
    {
        PhaseShellController.Instance?.RefreshHeader();
    }

    private void OnEngageClicked()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null || PhaseShellController.Instance == null ||
            !PhaseShellController.Instance.CanEngage)
            return;

        bool resting = gsm.SelectedCampaignAction == GameStateManager.CampaignAction.Rest;

        // Rest needs no squad; anything else must have a valid team to deploy.
        if (!resting && !gsm.SetActiveTeam(_selectedUnits))
            return;

        // ExecuteCycle applies the chosen course: Rest heals and stays here,
        // everything else loads the battle scene.
        if (!gsm.ExecuteCycle() || !resting)
            return;

        RefreshRosterList();
        PhaseShellController.Instance?.RefreshHeader();
        FindAnyObjectByType<NotificationManager>()?.ShowNotification("Squad rested - all units restored");
    }

    // ---------------------------------------------------------------
    // Fanfare (punch-in bounce + flash), shared by reveal + level-up + draft cards
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
        _revealName.style.color = UnitRarityTable.GetColor(unit.Rarity);
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
    // Level-up animation
    // ---------------------------------------------------------------

    private IEnumerator PlayLevelUpQueue()
    {
        _levelUpInProgress = true;
        _levelUpOverlay.RemoveFromClassList("hidden");

        while (GameStateManager.Instance.PendingLevelUps.Count > 0)
        {
            var info = GameStateManager.Instance.PendingLevelUps.Dequeue();
            PopulateLevelUpCard(info);

            StartCoroutine(PlayFlash(_levelUpFlash));
            yield return StartCoroutine(PunchIn(_levelUpCard, punchInDuration, punchInOvershoot));

            _levelUpAdvanceRequested = false;
            if (levelUpHoldSeconds > 0f)
            {
                float t = 0f;
                while (t < levelUpHoldSeconds && !_levelUpAdvanceRequested)
                {
                    t += Time.deltaTime;
                    yield return null;
                }
            }
            else
            {
                while (!_levelUpAdvanceRequested) yield return null;
            }

            _levelUpCard.style.opacity = 0f;
            yield return new WaitForSeconds(0.1f);
        }

        _levelUpOverlay.AddToClassList("hidden");
        GameStateManager.Instance.ClearPendingLevelUps();
        _levelUpInProgress = false;

        // A level-up can raise a unit's stats after the roster list was already
        // built (e.g. HP/ATK values on that unit's card), so refresh it once the
        // whole queue has played out rather than per-card.
        RefreshRosterList();
    }

    private void PopulateLevelUpCard(GameStateManager.LevelUpInfo info)
    {
        if (info.Unit == null) return;

        if (_levelUpName != null)
        {
            _levelUpName.text = info.Unit.UnitName.ToUpperInvariant();
            _levelUpName.style.color = UnitRarityTable.GetColor(info.Unit.Rarity);
        }
        if (info.Unit.UnitIcon != null && _levelUpIcon != null)
        {
            _levelUpIcon.style.backgroundImage = new StyleBackground(info.Unit.UnitIcon);
        }

        if (_levelUpLevel != null) _levelUpLevel.text = $"LV. {info.OldLevel} \u2192 LV. {info.NewLevel}";
        if (_levelUpHpValue != null) _levelUpHpValue.text = $"HP  {info.OldMaxHP} \u2192 {info.NewMaxHP}";
        if (_levelUpAtkValue != null) _levelUpAtkValue.text = $"ATK  {info.OldAttack} \u2192 {info.NewAttack}";
    }

    private void OnLevelUpTapped(ClickEvent evt)
    {
        if (_levelUpInProgress) _levelUpAdvanceRequested = true;
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
        _draftSelectedVillager = false;

        // Stagger each card's punch-in slightly so they don't all pop at once.
        for (int i = 0; i < draftCards.Count; i++)
        {
            StartCoroutine(PunchIn(draftCards[i], punchInDuration, punchInOvershoot));
            yield return new WaitForSeconds(0.08f);
        }

        while (!_draftSelectionMade) yield return null;

        _draftOverlay.AddToClassList("hidden");
        _draftOptionsContainer.Clear();

        var gsm = GameStateManager.Instance;
        if (_draftSelectedVillager)
            gsm.ResolveVillagerDraft();
        else
            gsm.ResolveUnitDraft(_draftSelectedUnit, _draftSelectedRarity);

        RefreshRosterList();
        RefreshSelectionCounter();
        // Population (Units) just changed - the shared header shows it on every
        // tab, so it needs to be told even though we're on the Roster tab.
        PhaseShellController.Instance?.RefreshHeader();

        if (GameStateManager.Instance.PendingDraftsToOffer > 0)
            GameStateManager.Instance.OfferNextUnitDraft();

        if (GameStateManager.Instance.HasPendingDraft)
        {
            yield return StartCoroutine(PlayDraftSequence());
        }
        else
        {
            yield return StartCoroutine(PlayLevelUpsThenReveals());
        }
    }

    private List<VisualElement> BuildDraftOptions(List<UnitData> options)
    {
        var built = new List<VisualElement>();
        if (_draftOptionsContainer == null || unitCardTemplate == null) return built;

        _draftOptionsContainer.Clear();

        for (int i = 0; i < options.Count; i++)
        {
            var unit = options[i];
            if (unit == null) continue;
            var card = BuildDraftCard(unit, GameStateManager.Instance.GetDraftRarity(i));
            _draftOptionsContainer.Add(card);
            built.Add(card);
        }

        if (GameStateManager.Instance.CanDraftSavedVillager)
        {
            var villagerCard = BuildVillagerDraftCard();
            _draftOptionsContainer.Add(villagerCard);
            built.Add(villagerCard);
        }

        return built;
    }

    private VisualElement BuildVillagerDraftCard()
    {
        var card = unitCardTemplate.Instantiate();
        var cardRoot = card.Q<VisualElement>("player-card");
        cardRoot.AddToClassList("draft-card");

        var nameLabel = card.Q<Label>("unit-name");
        if (nameLabel != null) nameLabel.text = "VILLAGER";

        var designationLabel = card.Q<Label>("unit-designation");
        if (designationLabel != null) designationLabel.text = "SETTLER";

        var levelLabel = card.Q<Label>("level-label");
        if (levelLabel != null) levelLabel.style.display = DisplayStyle.None;

        var rarityLabel = card.Q<Label>("rarity-label");
        if (rarityLabel != null) rarityLabel.style.display = DisplayStyle.None;

        // A villager has no combat stats at all - hide every panel built for them.
        var healthPanel = card.Q<VisualElement>("health-panel");
        if (healthPanel != null) healthPanel.style.display = DisplayStyle.None;

        var combatStats = card.Q<VisualElement>("combat-stats");
        if (combatStats != null) combatStats.style.display = DisplayStyle.None;

        var classEmblem = card.Q<VisualElement>("unit-class-emblem");
        if (classEmblem != null) classEmblem.style.display = DisplayStyle.None;

        var statusRow = card.Q<VisualElement>("portrait-status-row");
        if (statusRow != null) statusRow.style.display = DisplayStyle.None;

        var actionPips = card.Q<VisualElement>("action-pips");
        if (actionPips != null) actionPips.style.display = DisplayStyle.None;

        SetCardTag(card, "[ +1 VILLAGER ]");

        cardRoot.RegisterCallback<ClickEvent>(_ =>
        {
            _draftSelectedUnit = null;
            _draftSelectedVillager = true;
            _draftSelectionMade = true;
        });

        return card;
    }

    private VisualElement BuildDraftCard(UnitData unit, UnitRarity rarity)
    {
        var card = unitCardTemplate.Instantiate();
        var cardRoot = card.Q<VisualElement>("player-card");
        cardRoot.AddToClassList("draft-card");

        // A fresh recruit hasn't fought yet - full health, level 1, no wounds.
        PopulateCardTemplate(card, unit.UnitIcon, unit.UnitName, 1, unit.MaxHP, unit.MaxHP,
            unit.BaseAttack, unit.AttackRange, unit.MaxMovementPoints, unit.Faction, rarity);

        SetCardTag(card, "[ CHOOSE ]");

        cardRoot.RegisterCallback<ClickEvent>(_ =>
        {
            _draftSelectedUnit = unit;
            _draftSelectedRarity = rarity;
            _draftSelectionMade = true;
        });

        return card;
    }
}
