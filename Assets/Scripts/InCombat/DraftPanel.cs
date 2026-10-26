using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Barracks draft overlay: three hero archetypes to pick from, or Skip. Built in code over
/// the combat HUD's UIDocument (like <see cref="GameOverPanel"/>).
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class DraftPanel : MonoBehaviour
{
    private static readonly Color Cyan = new Color(0f, 0.95f, 1f);
    private static readonly Color Text = new Color(0.88f, 0.98f, 1f);

    private VisualElement _overlay;
    private VisualElement _cards;

    public bool IsOpen => _overlay != null && _overlay.style.display == DisplayStyle.Flex;

    private void Start()
    {
        VisualElement root = GetComponent<UIDocument>().rootVisualElement;
        if (root != null) Build(root);
    }

    private void Build(VisualElement root)
    {
        _overlay = new VisualElement { name = "draft-overlay", pickingMode = PickingMode.Position };
        _overlay.style.position = Position.Absolute;
        _overlay.style.left = _overlay.style.top = _overlay.style.right = _overlay.style.bottom = 0;
        _overlay.style.backgroundColor = new Color(0f, 0.02f, 0.05f, 0.82f);
        _overlay.style.alignItems = Align.Center;
        _overlay.style.justifyContent = Justify.Center;
        _overlay.style.display = DisplayStyle.None;

        var card = new VisualElement();
        card.style.paddingTop = card.style.paddingBottom = 24;
        card.style.paddingLeft = card.style.paddingRight = 32;
        card.style.backgroundColor = new Color(0.03f, 0.08f, 0.12f, 0.98f);
        SetBorder(card, Cyan, 2, 8);
        card.style.alignItems = Align.Stretch;

        Label title = MakeLabel("BARRACKS DRAFT", 36, Cyan, true);
        title.style.unityTextAlign = TextAnchor.MiddleCenter;
        Label subtitle = MakeLabel("Pick a hero to join the squad", 15, Text, false);
        subtitle.style.unityTextAlign = TextAnchor.MiddleCenter;
        subtitle.style.marginBottom = 16;

        _cards = new VisualElement();
        _cards.style.flexDirection = FlexDirection.Row;
        _cards.style.justifyContent = Justify.Center;
        _cards.style.marginBottom = 16;

        var skip = new Button { text = "SKIP DRAFT" };
        skip.style.alignSelf = Align.Center;
        StyleButton(skip);
        skip.clicked += () => Close(null);

        card.Add(title);
        card.Add(subtitle);
        card.Add(_cards);
        card.Add(skip);
        _overlay.Add(card);
        root.Add(_overlay);
    }

    /// <summary>One draft card: the archetype and the rarity it will join with.</summary>
    public struct Option
    {
        public UnitData Data;
        public UnitRarity Rarity;
    }

    private Action<Option?> _onClosed;

    /// <summary>Shows the options; <paramref name="onClosed"/> gets the pick, or null for skip.</summary>
    public void Show(List<Option> options, Action<Option?> onClosed)
    {
        if (_overlay == null) { onClosed?.Invoke(null); return; }

        _onClosed = onClosed;
        _cards.Clear();
        foreach (Option choice in options)
        {
            Option captured = choice;
            UnitData option = choice.Data;
            Color rarityColor = UnitRarityTable.GetColor(choice.Rarity);
            var button = new Button();
            StyleButton(button);
            button.style.width = 190;
            button.style.whiteSpace = WhiteSpace.Normal;
            string name = string.IsNullOrEmpty(option.UnitName) ? option.name : option.UnitName;
            string range = option.AttackRange > 1 ? $"Ranged {option.AttackRange}" : "Melee";
            button.style.color = rarityColor;
            SetBorder(button, rarityColor, 3, 4);
            button.text = $"{UnitRarityTable.GetDisplayName(choice.Rarity)}\n{name.ToUpperInvariant()}\n{range}\nHP {option.MaxHP}   ATK {option.BaseAttack}\nDEF {option.defensePower}   MOVE {option.MoveSpeed}";
            button.tooltip = option.Description;
            button.clicked += () => Close(captured);
            _cards.Add(button);
        }
        _overlay.style.display = DisplayStyle.Flex;
    }

    private void Close(Option? picked)
    {
        _overlay.style.display = DisplayStyle.None;
        Action<Option?> callback = _onClosed;
        _onClosed = null;
        callback?.Invoke(picked);
    }

    private static Label MakeLabel(string text, int size, Color color, bool bold)
    {
        var label = new Label(text);
        label.style.fontSize = size;
        label.style.color = color;
        label.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
        return label;
    }

    private static void StyleButton(Button button)
    {
        button.style.fontSize = 15;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.color = Cyan;
        button.style.backgroundColor = new Color(0f, 0.15f, 0.2f);
        button.style.marginLeft = button.style.marginRight = 8;
        button.style.paddingLeft = button.style.paddingRight = 18;
        button.style.paddingTop = button.style.paddingBottom = 12;
        SetBorder(button, Cyan, 1, 4);
    }

    private static void SetBorder(VisualElement element, Color color, float width, float radius)
    {
        element.style.borderTopColor = element.style.borderBottomColor = element.style.borderLeftColor = element.style.borderRightColor = color;
        element.style.borderTopWidth = element.style.borderBottomWidth = element.style.borderLeftWidth = element.style.borderRightWidth = width;
        element.style.borderTopLeftRadius = element.style.borderTopRightRadius = element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = radius;
    }
}
