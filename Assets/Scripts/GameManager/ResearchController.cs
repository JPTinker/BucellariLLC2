using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Drives the Research tab (the old Store placeholder slot). Plain component
/// with no UIDocument: the battle HUD calls <see cref="Build"/> to get
/// the tab's root element, then <see cref="Refresh"/> each time the tab opens.
/// The tree is built in code from GameStateManager.AvailableResearch, one row
/// per track with its tiers left to right, so adding a ResearchData asset is
/// all it takes to add a node.
/// </summary>
public class ResearchController : MonoBehaviour
{
    private VisualElement _root;
    private Label _slotsLabel;
    private VisualElement _activeStrip;
    private Label _resourcesLabel;
    private Label _message;
    private ScrollView _tracks;

    // The combat HUD does not load the Decision stylesheets, so the tree styles itself inline.
    private static readonly Color TextColor = new Color(0.88f, 0.98f, 1f);
    private static readonly Color MutedColor = new Color(0.62f, 0.74f, 0.8f);
    private static readonly Color AccentColor = new Color(0f, 0.95f, 1f);
    private static readonly Color PositiveColor = new Color(0.45f, 1f, 0.6f);
    private static readonly Color PanelColor = new Color(0.08f, 0.14f, 0.2f, 1f);

    private static Label MakeLabel(string text, int size, Color color, bool bold = false)
    {
        var label = new Label(text);
        label.style.fontSize = size;
        label.style.color = color;
        label.style.whiteSpace = WhiteSpace.Normal;
        if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
        return label;
    }

    private static void StylePanel(VisualElement panel)
    {
        panel.style.backgroundColor = PanelColor;
        panel.style.paddingTop = panel.style.paddingBottom = 8;
        panel.style.paddingLeft = panel.style.paddingRight = 10;
        panel.style.borderTopLeftRadius = panel.style.borderTopRightRadius =
            panel.style.borderBottomLeftRadius = panel.style.borderBottomRightRadius = 6;
        panel.style.borderTopWidth = panel.style.borderBottomWidth =
            panel.style.borderLeftWidth = panel.style.borderRightWidth = 1;
        panel.style.borderTopColor = panel.style.borderBottomColor =
            panel.style.borderLeftColor = panel.style.borderRightColor = new Color(0f, 0.95f, 1f, 0.35f);
    }

    /// <summary>Creates the (empty) tab root. Call once; the shell adds it to the content slot.</summary>
    public VisualElement Build()
    {
        _root = new VisualElement { name = "research-root" };
        _root.style.flexGrow = 1;
        _root.style.paddingLeft = _root.style.paddingRight = 12;
        _root.style.paddingTop = _root.style.paddingBottom = 8;

        _resourcesLabel = MakeLabel("", 15, TextColor);
        _root.Add(_resourcesLabel);

        _slotsLabel = MakeLabel("", 14, AccentColor, true);
        _root.Add(_slotsLabel);

        _activeStrip = new VisualElement();
        _activeStrip.style.flexDirection = FlexDirection.Row;
        _activeStrip.style.flexWrap = Wrap.Wrap;
        _root.Add(_activeStrip);

        _message = MakeLabel("", 14, MutedColor);
        _root.Add(_message);

        _tracks = new ScrollView(ScrollViewMode.Vertical);
        _tracks.style.flexGrow = 1;
        _root.Add(_tracks);

        return _root;
    }

    public void Refresh()
    {
        if (_root == null) return;

        GameStateManager gsm = GameStateManager.Instance;
        if (gsm == null) return;
        ResearchManager research = gsm.Research;

        // Finished since the last look: tell the player once.
        if (research.RecentlyCompleted.Count > 0)
        {
            var names = new List<string>();
            foreach (ResearchData done in research.RecentlyCompleted) names.Add(done.DisplayName);
            research.RecentlyCompleted.Clear();
            var notifier = FindAnyObjectByType<NotificationManager>();
            if (notifier != null) notifier.ShowNotification($"Research complete: {string.Join(", ", names)}");
        }

        _resourcesLabel.text = $"Food {gsm.Settlement.Food}/{research.MaxFood}   Materials {gsm.Settlement.Materials}/{research.MaxMaterials}";
        _slotsLabel.text = $"SLOTS {research.Active.Count}/{research.SlotCount}";

        _activeStrip.Clear();
        foreach (ResearchProgress progress in research.Active)
        {
            ResearchData data = gsm.AvailableResearch != null ? gsm.AvailableResearch.Find(r => r != null && r.name == progress.Id) : null;
            string label = data != null ? data.DisplayName : progress.Id;
            var chip = MakeLabel($"{label} ({research.KillsDone(progress, data)}/{(data != null ? data.KillsToComplete : progress.KillsRemaining)} kills)", 13, AccentColor, true);
            StylePanel(chip);
            chip.style.marginRight = 6;
            _activeStrip.Add(chip);
        }

        _tracks.Clear();
        if (gsm.AvailableResearch == null || gsm.AvailableResearch.Count == 0)
        {
            _message.text = "No research available yet. Assign ResearchData assets on GameStateManager.";
            return;
        }
        _message.text = "";

        foreach (ResearchTrack track in System.Enum.GetValues(typeof(ResearchTrack)))
        {
            var nodes = gsm.AvailableResearch.FindAll(r => r != null && r.Track == track);
            if (nodes.Count == 0) continue;
            nodes.Sort((a, b) => a.Tier.CompareTo(b.Tier));
            _tracks.Add(BuildTrackRow(gsm, research, track, nodes));
        }
    }

    private VisualElement BuildTrackRow(GameStateManager gsm, ResearchManager research, ResearchTrack track, List<ResearchData> nodes)
    {
        var panel = new VisualElement();
        StylePanel(panel);
        panel.style.marginBottom = 8;

        panel.Add(MakeLabel(TrackTitle(track, research), 16, AccentColor, true));

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.flexWrap = Wrap.Wrap;
        foreach (ResearchData node in nodes) row.Add(BuildNode(gsm, research, node));
        panel.Add(row);
        return panel;
    }

    private VisualElement BuildNode(GameStateManager gsm, ResearchManager research, ResearchData node)
    {
        ResearchStatus status = research.GetStatus(node);

        var card = new VisualElement();
        StylePanel(card);
        card.style.width = 190;
        card.style.marginRight = 6;
        card.style.marginTop = 4;
        card.tooltip = node.Description;
        card.style.opacity = status == ResearchStatus.Locked ? 0.5f : 1f;

        card.Add(MakeLabel(string.IsNullOrEmpty(node.DisplayName) ? node.name : node.DisplayName, 14, TextColor, true));
        card.Add(MakeLabel(DescribeEffect(node), 13, MutedColor));
        card.Add(MakeLabel($"{node.FoodCost} Food / {node.MaterialsCost} Materials / {node.KillsToComplete} kills", 13, TextColor));

        switch (status)
        {
            case ResearchStatus.Completed:
                card.Add(StatusLabel("RESEARCHED", PositiveColor));
                break;
            case ResearchStatus.Active:
                ResearchProgress progress = research.GetActive(node);
                card.Add(StatusLabel($"IN PROGRESS ({progress.KillsRemaining} kills left)", AccentColor));
                break;
            case ResearchStatus.Locked:
                card.Add(StatusLabel($"Requires {node.Prerequisite.DisplayName}", MutedColor));
                break;
            default:
                bool canStart = research.CanStart(node, out string reason);
                var start = new Button(() => OnStartClicked(node)) { text = "RESEARCH" };
                start.style.marginTop = 4;
                start.SetEnabled(canStart);
                if (!canStart && reason != null) start.tooltip = reason;
                card.Add(start);
                break;
        }
        return card;
    }

    private static Label StatusLabel(string text, Color color) => MakeLabel(text, 13, color, true);

    private void OnStartClicked(ResearchData node)
    {
        GameStateManager gsm = GameStateManager.Instance;
        if (gsm == null) return;

        if (!gsm.Research.TryStart(node))
        {
            gsm.Research.CanStart(node, out string reason);
            var notifier = FindAnyObjectByType<NotificationManager>();
            if (notifier != null && reason != null) notifier.ShowNotification(reason);
            return;
        }

        Refresh();
    }

    private static string TrackTitle(ResearchTrack track, ResearchManager r)
    {
        switch (track)
        {
            case ResearchTrack.Recruiting: return $"RECRUITING  (stray villager every {r.VillagerSpawnInterval} rounds)";
            case ResearchTrack.Gathering: return $"GATHERING  (worker yield x{r.GatherMultiplier:0.##})";
            case ResearchTrack.Training: return $"TRAINING  (new heroes start at level {r.StartingHeroLevel})";
            case ResearchTrack.Masonry: return $"MASONRY  (wall health {r.WallMaxHealth})";
            case ResearchTrack.Engineering: return $"ENGINEERING  (fortify up to level {r.MaxFortificationLevel})";
            case ResearchTrack.Storehouses: return $"STOREHOUSES  (cap {r.MaxFood} Food / {r.MaxMaterials} Materials)";
            case ResearchTrack.Church: return $"CHURCH  (heroes resting in a Church heal {r.ChurchHealAmount} HP per round)";
            case ResearchTrack.LevelCap: return $"LEVEL CAP  (units level up to {r.MaxUnitLevel})";
            //case ResearchTrack.BattleLength: return $"MORALE  (battles last {r.MaxBattleRounds} rounds)";
            default: return track.ToString().ToUpperInvariant();
        }
    }

    public static string DescribeEffect(ResearchData node)
    {
        switch (node.Track)
        {
            case ResearchTrack.Recruiting: return $"Stray villagers arrive {Mathf.RoundToInt(node.Value)} round(s) sooner";
            case ResearchTrack.Gathering: return $"+{node.Value * 100f:0.#}% worker haul and carry";
            case ResearchTrack.Training: return $"+{Mathf.RoundToInt(node.Value)} starting level for new heroes";
            case ResearchTrack.Masonry: return $"+{Mathf.RoundToInt(node.Value)} wall health";
            case ResearchTrack.Engineering: return $"+{Mathf.RoundToInt(node.Value)} fortification level";
            case ResearchTrack.Storehouses: return $"+{Mathf.RoundToInt(node.Value)} Food and Materials storage per Town Center";
            case ResearchTrack.Church: return $"+{Mathf.RoundToInt(node.Value)} HP healed per round in a Church";
            case ResearchTrack.LevelCap: return $"+{Mathf.RoundToInt(node.Value)} maximum unit level";
            case ResearchTrack.BattleLength: return $"+{Mathf.RoundToInt(node.Value)} battle rounds";
            default: return "";
        }
    }
}
