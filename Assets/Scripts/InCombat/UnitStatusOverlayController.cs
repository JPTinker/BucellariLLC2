using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Spawns one floating status badge (UnitStatusOverlay.uxml) per active unit
/// and keeps each pinned above that unit in screen space. Stat content
/// (HP/actions/buffs) refreshes reactively off UnitInstance.OnStatsChanged;
/// only screen position is recalculated every frame, since that's the only
/// thing that changes continuously as units and the camera move.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class UnitStatusOverlayController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Assign UnitStatusOverlay.uxml here.")]
    [SerializeField] private VisualTreeAsset overlayAsset;
    [Tooltip("Camera used to project unit world positions into screen space. Defaults to Camera.main.")]
    [SerializeField] private Camera worldCamera;

    [Header("Placement")]
    [Tooltip("World-space height above the unit's pivot the badge tracks (roughly head height).")]
    [SerializeField] private float badgeHeightOffset = 2.2f;
    [Tooltip("Viewport margin outside [0,1] before a badge is hidden for being off-screen.")]
    [SerializeField] private float offscreenMargin = 0.05f;

    private UIDocument uiDocument;
    private VisualElement overlayLayer;

    private readonly Dictionary<UnitInstance, OverlayView> overlays = new Dictionary<UnitInstance, OverlayView>();
    private readonly List<UnitInstance> pendingRemoval = new List<UnitInstance>();

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
        if (worldCamera == null) worldCamera = Camera.main;
    }

    private void Start()
    {
        if (uiDocument == null || uiDocument.rootVisualElement == null) return;

        // A full-screen, click-through layer added on top of the existing
        // combat UI. Badges are positioned within it using panel-space
        // coordinates from RuntimePanelUtils, so it must stay unscaled/unpadded.
        overlayLayer = new VisualElement { name = "battlefield-overlay-layer" };
        overlayLayer.pickingMode = PickingMode.Ignore;
        overlayLayer.style.position = Position.Absolute;
        overlayLayer.style.left = 0;
        overlayLayer.style.top = 0;
        overlayLayer.style.right = 0;
        overlayLayer.style.bottom = 0;
        uiDocument.rootVisualElement.Add(overlayLayer);
    }

    private void Update()
    {
        if (overlayLayer == null) return;

        if (worldCamera == null)
        {
            worldCamera = Camera.main;
            if (worldCamera == null) return;
        }

        SyncOverlaysWithScene();
        RepositionAndRefreshOverlays();
    }

    private void SyncOverlaysWithScene()
    {
        UnitInstance[] currentUnits = FindObjectsByType<UnitInstance>();

        foreach (UnitInstance unit in currentUnits)
        {
            if (unit == null || unit.IsDead || unit.IsExtracted || !unit.IsRevealed) continue;
            if (overlays.ContainsKey(unit)) continue;

            OverlayView view = new OverlayView(unit, overlayAsset);
            overlayLayer.Add(view.Root);
            overlays.Add(unit, view);

            unit.OnStatsChanged += HandleUnitStatsChanged;
            unit.OnDeath += HandleUnitDeath;
        }

        pendingRemoval.Clear();
        foreach (KeyValuePair<UnitInstance, OverlayView> kvp in overlays)
        {
            UnitInstance unit = kvp.Key;
            if (unit == null || unit.IsDead || unit.IsExtracted || !unit.IsRevealed)
                pendingRemoval.Add(unit);
        }

        foreach (UnitInstance unit in pendingRemoval)
            RemoveOverlay(unit);
    }

    private void RemoveOverlay(UnitInstance unit)
    {
        if (!overlays.TryGetValue(unit, out OverlayView view)) return;

        if (unit != null)
        {
            unit.OnStatsChanged -= HandleUnitStatsChanged;
            unit.OnDeath -= HandleUnitDeath;
        }

        view.Root.RemoveFromHierarchy();
        overlays.Remove(unit);
    }

    private void HandleUnitStatsChanged(UnitInstance unit)
    {
        if (overlays.TryGetValue(unit, out OverlayView view))
            view.Refresh();
    }

    private void HandleUnitDeath(UnitInstance unit)
    {
        RemoveOverlay(unit);
    }

    private void RepositionAndRefreshOverlays()
    {
        VisualElement root = uiDocument.rootVisualElement;
        if (root.panel == null) return;

        foreach (KeyValuePair<UnitInstance, OverlayView> kvp in overlays)
        {
            UnitInstance unit = kvp.Key;
            OverlayView view = kvp.Value;
            if (unit == null) continue;

            Vector3 worldPoint = unit.transform.position + Vector3.up * badgeHeightOffset;
            Vector3 viewportPoint = worldCamera.WorldToViewportPoint(worldPoint);

            bool onScreen = viewportPoint.z > 0f &&
                            viewportPoint.x > -offscreenMargin && viewportPoint.x < 1f + offscreenMargin &&
                            viewportPoint.y > -offscreenMargin && viewportPoint.y < 1f + offscreenMargin;

            view.Root.style.display = onScreen ? DisplayStyle.Flex : DisplayStyle.None;
            if (!onScreen) continue;

            Vector2 panelPoint = RuntimePanelUtils.CameraTransformWorldToPanel(root.panel, worldPoint, worldCamera);
            view.Root.style.left = panelPoint.x;
            view.Root.style.top = panelPoint.y;
        }
    }

    private void OnDestroy()
    {
        foreach (KeyValuePair<UnitInstance, OverlayView> kvp in overlays)
        {
            if (kvp.Key == null) continue;
            kvp.Key.OnStatsChanged -= HandleUnitStatsChanged;
            kvp.Key.OnDeath -= HandleUnitDeath;
        }
    }

    private sealed class OverlayView
    {
        public VisualElement Root { get; }

        private readonly UnitInstance unit;
        private readonly Label nameLabel;
        private readonly VisualElement healthFill;
        private readonly VisualElement actionsRow;
        private readonly VisualElement fortifiedIcon;
        private readonly List<VisualElement> actionPips = new List<VisualElement>();

        public OverlayView(UnitInstance unit, VisualTreeAsset overlayAsset)
        {
            this.unit = unit;

            if (overlayAsset != null)
            {
                // Keep the TemplateContainer Instantiate() returns as Root -
                // that's the object the UXML's <Style src> stylesheet is
                // actually attached to. Unwrapping down to "status-root"
                // would detach it from the stylesheet's ancestor chain and
                // the badge would render with no styling applied.
                Root = overlayAsset.Instantiate();
            }
            else
            {
                Root = new VisualElement();
                Debug.LogWarning("UnitStatusOverlayController: overlayAsset not assigned, badges will render blank.");
            }

            // Anchor point is the badge's bottom-center, so it sits just above
            // the projected world point rather than centered on it.
            Root.style.position = Position.Absolute;
            Root.style.translate = new Translate(Length.Percent(-50), Length.Percent(-100));
            Root.pickingMode = PickingMode.Ignore;

            nameLabel = Root.Q<Label>("status-name-label");
            healthFill = Root.Q<VisualElement>("status-health-fill");
            actionsRow = Root.Q<VisualElement>("status-actions-row");
            fortifiedIcon = Root.Q<VisualElement>("status-buff-fortified");

            // Seed the pip pool from whatever the template shipped with.
            actionsRow?.Query<VisualElement>(className: "status-action-pip").ForEach(actionPips.Add);

            Refresh();
        }

        public void Refresh()
        {
            if (unit == null) return;

            if (nameLabel != null)
                nameLabel.text = unit.unitName.ToUpperInvariant();

            RefreshHealth();
            RefreshActionPips();
            RefreshBuffs();

            Root.EnableInClassList("unit-status-exhausted", unit.actionsRemaining <= 0);
        }

        private void RefreshHealth()
        {
            if (healthFill == null) return;

            int maxHealth = Mathf.Max(1, unit.maxHealth);
            float percent = Mathf.Clamp01((float)unit.currentHealth / maxHealth) * 100f;

            healthFill.style.width = Length.Percent(percent);
            healthFill.EnableInClassList("status-health-fill-mid", percent <= 50f && percent > 20f);
            healthFill.EnableInClassList("status-health-fill-low", percent <= 20f);
        }

        private void RefreshActionPips()
        {
            if (actionsRow == null) return;

            int max = Mathf.Max(0, unit.maxActionsPerTurn);

            while (actionPips.Count < max)
            {
                VisualElement pip = new VisualElement();
                pip.AddToClassList("status-action-pip");
                actionsRow.Add(pip);
                actionPips.Add(pip);
            }
            while (actionPips.Count > max)
            {
                VisualElement pip = actionPips[actionPips.Count - 1];
                actionPips.RemoveAt(actionPips.Count - 1);
                pip.RemoveFromHierarchy();
            }

            for (int i = 0; i < actionPips.Count; i++)
                actionPips[i].EnableInClassList("status-action-pip-spent", i >= unit.actionsRemaining);
        }

        private void RefreshBuffs()
        {
            fortifiedIcon?.EnableInClassList("status-buff-icon-active", unit.IsFortified);
        }
    }
}