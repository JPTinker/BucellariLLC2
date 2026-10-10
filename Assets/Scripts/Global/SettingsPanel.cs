using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Settings overlay built entirely in code (inline styles) so it can be dropped
/// into any UIDocument - Title, Decision shell, Combat HUD - without editing
/// UXML/USS. Usage: <c>SettingsPanel.Attach(root, parentForButton)</c>.
/// To add a setting: add a property to GameSettings, then add a row in Build().
/// </summary>
public static class SettingsPanel
{
    private static readonly Color Cyan = new Color(0f, 0.86f, 0.906f);
    private static readonly Color Panel = new Color(0.086f, 0.129f, 0.18f);
    private static readonly Color Text = new Color(0.878f, 0.984f, 1f);

    /// <summary>Adds the overlay to <paramref name="root"/> and a SETTINGS button to <paramref name="buttonParent"/> (optionally at <paramref name="index"/>).</summary>
    public static Button Attach(VisualElement root, VisualElement buttonParent, int index = -1)
    {
        if (root == null) return null;

        VisualElement overlay = Build(root);
        root.Add(overlay);

        var button = new Button(() => overlay.style.display = DisplayStyle.Flex) { text = "SETTINGS", name = "btn-settings" };
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        if (buttonParent != null)
        {
            if (index >= 0 && index <= buttonParent.childCount) buttonParent.Insert(index, button);
            else buttonParent.Add(button);
        }
        return button;
    }

    private static VisualElement Build(VisualElement root)
    {
        var overlay = new VisualElement { name = "settings-overlay", pickingMode = PickingMode.Position };
        var s = overlay.style;
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
        c.backgroundColor = Panel;
        SetBorder(card, new Color(0f, 0.86f, 0.906f, 0.4f));
        overlay.Add(card);

        var title = new Label("SETTINGS");
        title.style.color = Cyan;
        title.style.fontSize = 12;
        title.style.letterSpacing = 3;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.marginBottom = 14;
        card.Add(title);

        // --- Scroll speed ---
        var scroll = AddSlider(card, "Scroll speed", GameSettings.MinScrollSpeed, GameSettings.MaxScrollSpeed,
            GameSettings.ScrollSpeed, v => GameSettings.ScrollSpeed = v);

        // --- Master volume ---
        var volume = AddSlider(card, "Sound volume", 0f, 1f,
            GameSettings.MasterVolume, v => GameSettings.MasterVolume = v);

        // --- Footer ---
        var footer = new VisualElement();
        footer.style.flexDirection = FlexDirection.Row;
        footer.style.justifyContent = Justify.SpaceBetween;
        footer.style.marginTop = 18;
        card.Add(footer);

        var reset = new Button(() =>
        {
            GameSettings.ResetToDefaults();
            scroll.SetValueWithoutNotify(GameSettings.ScrollSpeed);
            volume.SetValueWithoutNotify(GameSettings.MasterVolume);
        }) { text = "RESET" };
        var close = new Button(() =>
        {
            GameSettings.Save();
            overlay.style.display = DisplayStyle.None;
        }) { text = "CLOSE" };
        foreach (var b in new[] { reset, close })
        {
            b.style.flexGrow = 1;
            b.style.marginLeft = b.style.marginRight = 3;
            b.style.paddingTop = b.style.paddingBottom = 8;
        }
        footer.Add(reset);
        footer.Add(close);

        // Swallow clicks so they don't fall through to the world / UI behind.
        overlay.RegisterCallback<ClickEvent>(e => e.StopPropagation());
        return overlay;
    }

    private static Slider AddSlider(VisualElement parent, string label, float min, float max, float value, System.Action<float> onChange)
    {
        var slider = new Slider(label, min, max) { showInputField = true };
        slider.SetValueWithoutNotify(value);
        slider.style.color = Text;
        slider.style.marginBottom = 10;
        slider.RegisterValueChangedCallback(e => onChange(e.newValue));
        parent.Add(slider);
        return slider;
    }

    private static void SetBorder(VisualElement e, Color color)
    {
        e.style.borderTopWidth = e.style.borderBottomWidth = e.style.borderLeftWidth = e.style.borderRightWidth = 1;
        e.style.borderTopColor = e.style.borderBottomColor = e.style.borderLeftColor = e.style.borderRightColor = color;
    }
}
