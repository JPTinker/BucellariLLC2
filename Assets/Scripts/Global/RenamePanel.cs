using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Code-built "name your hero" overlay (inline styles, like SettingsPanel).
/// Attach once per UIDocument root, then call Show(unit, onRenamed).
/// </summary>
public class RenamePanel
{
    private readonly VisualElement _overlay;
    private readonly TextField _field;
    private Unit _unit;
    private Action _onRenamed;

    public RenamePanel(VisualElement root)
    {
        _overlay = new VisualElement { name = "rename-overlay" };
        var s = _overlay.style;
        s.position = Position.Absolute;
        s.left = 0; s.top = 0; s.right = 0; s.bottom = 0;
        s.backgroundColor = new Color(0.086f, 0.129f, 0.18f, 0.92f);
        s.justifyContent = Justify.Center;
        s.alignItems = Align.Center;
        s.display = DisplayStyle.None;

        var card = new VisualElement();
        var c = card.style;
        c.width = 380; c.maxWidth = Length.Percent(92);
        c.paddingLeft = c.paddingRight = 22; c.paddingTop = c.paddingBottom = 20;
        c.backgroundColor = new Color(0.086f, 0.129f, 0.18f);
        c.borderTopWidth = c.borderBottomWidth = c.borderLeftWidth = c.borderRightWidth = 1;
        c.borderTopColor = c.borderBottomColor = c.borderLeftColor = c.borderRightColor = new Color(0f, 0.86f, 0.906f, 0.4f);
        _overlay.Add(card);

        var title = new Label("NAME YOUR HERO");
        title.style.color = new Color(0f, 0.86f, 0.906f);
        title.style.fontSize = 12;
        title.style.letterSpacing = 3;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.marginBottom = 12;
        card.Add(title);

        _field = new TextField { maxLength = NameGenerator.MaxNameLength };
        _field.style.marginBottom = 10;
        card.Add(_field);

        var random = new Button(() => _field.value = NameGenerator.Random()) { text = "RANDOM NAME" };
        random.style.marginBottom = 14;
        card.Add(random);

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        card.Add(row);

        var cancel = new Button(Hide) { text = "CANCEL" };
        var save = new Button(Confirm) { text = "SAVE" };
        foreach (var b in new[] { cancel, save })
        {
            b.style.flexGrow = 1;
            b.style.marginLeft = b.style.marginRight = 3;
            b.style.paddingTop = b.style.paddingBottom = 8;
        }
        row.Add(cancel);
        row.Add(save);

        // Keep taps on the overlay from reaching the cards behind it.
        _overlay.RegisterCallback<ClickEvent>(e => e.StopPropagation());
        root.Add(_overlay);
    }

    public void Show(Unit unit, Action onRenamed)
    {
        if (unit == null) return;
        _unit = unit;
        _onRenamed = onRenamed;
        _field.SetValueWithoutNotify(unit.UnitName);
        _overlay.style.display = DisplayStyle.Flex;
        _field.Focus();
    }

    private void Hide() => _overlay.style.display = DisplayStyle.None;

    private void Confirm()
    {
        string name = (_field.value ?? "").Trim();
        if (name.Length == 0)
        {
            UnityEngine.Object.FindAnyObjectByType<NotificationManager>()?.ShowNotification("Name can't be empty");
            return;
        }
        if (_unit != null) _unit.UnitName = name;
        Hide();
        _onRenamed?.Invoke();
    }
}
