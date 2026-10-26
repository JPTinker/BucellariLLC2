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

    /// <summary>Creates the (empty) tab root. Call once; the shell adds it to the content slot.</summary>
    public VisualElement Build()
    {
        _root = new VisualElement { name = "research-root" };
        _root.style.flexGrow = 1;
        _root.style.paddingLeft = _root.style.paddingRight = 12;
        _root.style.paddingTop = _root.style.paddingBottom = 8;

        var title = new Label("RESEARCH");
        title.AddToClassList("label-caps");
        title.AddToClassList("section-title");
        _root.Add(title);

        _resourcesLabel = new Label();
        _resourcesLabel.AddToClassList("body-text");
        _root.Add(_resourcesLabel);

        _slotsLabel = new Label();
        _slotsLabel.AddToClassList("label-caps");
        _root.Add(_slotsLabel);

        _activeStrip = new VisualElement();
        _activeStrip.style.flexDirection = FlexDirection.Row;
        _activeStrip.style.flexWrap = Wrap.Wrap;
        _root.Add(_activeStrip);

        _message = new Label();
        _message.AddToClassList("body-text");
        _message.AddToClassList("text-muted");
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
            var chip = new Label($"{label} ({research.KillsDone(progress, data)}/{(data != null ? data.KillsToComplete : progress.KillsRemaining)} kills)");
            chip.AddToClassList("label-caps");
            chip.AddToClassList("tag");
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
        panel.AddToClassList("panel");
        panel.style.marginBottom = 8;

        var header = new Label(TrackTitle(track, research));
        header.AddToClassList("label-caps");
        header.AddToClassList("card-title");
        panel.Add(header);

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
        card.AddToClassList("panel");
        card.style.width = 190;
        card.style.marginRight = 6;
        card.style.marginTop = 4;
        card.tooltip = node.Description;
        card.style.opacity = status == ResearchStatus.Locked ? 0.5f : 1f;

        var name = new Label(string.IsNullOrEmpty(node.DisplayName) ? node.name : node.DisplayName);
        name.AddToClassList("label-caps");
        card.Add(name);

        var effect = new Label(DescribeEffect(node));
        effect.AddToClassList("body-text");
        effect.AddToClassList("text-muted");
        effect.style.whiteSpace = WhiteSpace.Normal;
        card.Add(effect);

        var cost = new Label($"{node.FoodCost} Food / {node.MaterialsCost} Materials / {node.KillsToComplete} kills");
        cost.AddToClassList("body-text");
        cost.style.whiteSpace = WhiteSpace.Normal;
        card.Add(cost);

        switch (status)
        {
            case ResearchStatus.Completed:
                card.Add(StatusLabel("RESEARCHED", "text-positive"));
                break;
            case ResearchStatus.Active:
                ResearchProgress progress = research.GetActive(node);
                card.Add(StatusLabel($"IN PROGRESS ({progress.KillsRemaining} kills left)", null));
                break;
            case ResearchStatus.Locked:
                card.Add(StatusLabel($"Requires {node.Prerequisite.DisplayName}", "text-muted"));
                break;
            default:
                bool canStart = research.CanStart(node, out string reason);
                var start = new Button(() => OnStartClicked(node)) { text = "RESEARCH" };
                start.AddToClassList("btn");
                start.SetEnabled(canStart);
                if (!canStart && reason != null) start.tooltip = reason;
                card.Add(start);
                break;
        }
        return card;
    }

    private static Label StatusLabel(string text, string cls)
    {
        var label = new Label(text);
        label.AddToClassList("label-caps");
        if (cls != null) label.AddToClassList(cls);
        return label;
    }

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
