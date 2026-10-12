using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Drives the Memorial tab - a read-only list of every unit lost for good
/// (GameStateManager.Memorial), newest first. Plain component, no UIDocument
/// of its own: PhaseShellController instantiates MemorialContent.uxml into
/// the content-slot and calls Initialize() with the shell root, then
/// Refresh() each time the tab is shown.
/// </summary>
public class MemorialController : MonoBehaviour
{
    private VisualElement _root;
    private ScrollView _list;
    private Label _empty;

    public void Initialize(VisualElement shellRoot)
    {
        _root = shellRoot;
        _list = _root.Q<ScrollView>("memorial-list");
        _empty = _root.Q<Label>("memorial-empty");

        if (_list == null || _empty == null)
            Debug.LogError("MemorialController: memorial-list / memorial-empty not found. Check MemorialContent.uxml.");

        Refresh();
    }

    public void Refresh()
    {
        if (_list == null) return;
        _list.Clear();

        var gsm = GameStateManager.Instance;
        var memorial = gsm != null ? gsm.Memorial : null;
        bool any = memorial != null && memorial.Count > 0;
        if (_empty != null) _empty.style.display = any ? DisplayStyle.None : DisplayStyle.Flex;
        if (!any) return;

        for (int i = memorial.Count - 1; i >= 0; i--)
            _list.Add(BuildRow(memorial[i]));
    }

    private static VisualElement BuildRow(FallenRecord r)
    {
        Color rarity = UnitRarityTable.GetColor(r.Rarity);

        var row = new VisualElement();
        row.AddToClassList("memorial-row");
        row.style.borderLeftColor = rarity;

        var name = new Label(string.IsNullOrEmpty(r.UnitName) ? "Unknown" : r.UnitName);
        name.AddToClassList("memorial-row-name");
        name.style.color = rarity;
        row.Add(name);

        string cls = string.IsNullOrEmpty(r.ClassName) ? "" : $" · {r.ClassName}";
        var info = new Label($"Lv {r.Level}{cls}");
        info.AddToClassList("memorial-row-class");
        row.Add(info);

        string how = r.Cause == LossCause.Battle
            ? "Fell in battle"
            : string.IsNullOrEmpty(r.Detail) ? "Lost to an event" : $"Lost to an event ({r.Detail})";
        var when = new Label($"{how} · Cycle {r.CycleNumber}");
        when.AddToClassList("memorial-row-when");
        row.Add(when);

        return row;
    }
}
