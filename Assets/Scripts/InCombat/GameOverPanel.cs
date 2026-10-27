using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Win / Lose overlay shown when GameStateManager ends the run. Built in code over
/// the combat HUD's UIDocument (like the research HUD), so no UXML is needed.
/// Shows the score breakdown with Play Again and Title buttons.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class GameOverPanel : MonoBehaviour
{
    private static readonly Color Cyan = new Color(0f, 0.95f, 1f);
    private static readonly Color Gold = new Color(1f, 0.8f, 0.2f);
    private static readonly Color Red = new Color(1f, 0.35f, 0.35f);
    private static readonly Color Text = new Color(0.88f, 0.98f, 1f);

    private VisualElement _overlay;
    private VisualElement _card;
    private Label _title, _subtitle, _score;
    private VisualElement _rows;

    private void Start()
    {
        VisualElement root = GetComponent<UIDocument>().rootVisualElement;
        if (root == null) return;

        Build(root);
        GameStateManager.OnGameOver += Show;
        if (GameStateManager.Instance != null && GameStateManager.Instance.GameEnded)
            Show(GameStateManager.Instance.LastResult);
    }

    private void OnDestroy() => GameStateManager.OnGameOver -= Show;

    private void Build(VisualElement root)
    {
        _overlay = new VisualElement { name = "game-over-overlay", pickingMode = PickingMode.Position };
        _overlay.style.position = Position.Absolute;
        _overlay.style.left = _overlay.style.top = _overlay.style.right = _overlay.style.bottom = 0;
        _overlay.style.backgroundColor = new Color(0f, 0.02f, 0.05f, 0.82f);
        _overlay.style.alignItems = Align.Center;
        _overlay.style.justifyContent = Justify.Center;
        _overlay.style.display = DisplayStyle.None;

        _card = new VisualElement();
        _card.style.minWidth = 420;
        _card.style.paddingTop = _card.style.paddingBottom = 28;
        _card.style.paddingLeft = _card.style.paddingRight = 36;
        _card.style.backgroundColor = new Color(0.03f, 0.08f, 0.12f, 0.98f);
        SetBorder(_card, Cyan, 2, 8);
        _card.style.alignItems = Align.Stretch;

        _title = MakeLabel(44, Cyan, true);
        _title.style.unityTextAlign = TextAnchor.MiddleCenter;
        _subtitle = MakeLabel(15, Text, false);
        _subtitle.style.unityTextAlign = TextAnchor.MiddleCenter;
        _subtitle.style.marginBottom = 18;

        _rows = new VisualElement();
        _rows.style.marginBottom = 14;

        _score = MakeLabel(34, Gold, true);
        _score.style.unityTextAlign = TextAnchor.MiddleCenter;
        _score.style.marginBottom = 22;

        var buttons = new VisualElement();
        buttons.style.flexDirection = FlexDirection.Row;
        buttons.style.justifyContent = Justify.Center;
        buttons.Add(MakeButton("PLAY AGAIN", () => GameStateManager.Instance?.RestartGame()));
        buttons.Add(MakeButton("TITLE", () => GameStateManager.Instance?.ReturnToTitle()));

        _card.Add(_title);
        _card.Add(_subtitle);
        _card.Add(_rows);
        _card.Add(_score);
        _card.Add(buttons);
        _overlay.Add(_card);
        root.Add(_overlay);
    }

    private void Show(GameStateManager.GameResult r)
    {
        if (_overlay == null) return;

        // A win goes to the victory scene (dancing heroes + confetti); only the loss uses this card.
        if (r.Won)
        {
            StartCoroutine(LoadVictoryScene());
            return;
        }

        _title.text = r.Won ? "YOU ESCAPED" : "MORALE BROKEN";
        _title.style.color = r.Won ? Cyan : Red;
        _subtitle.text = r.Won
            ? $"{r.VillagersEscaped} villagers made it to safety."
            : "Your people lost the will to go on.";

        _rows.Clear();
        AddRow("Rounds survived", r.Rounds.ToString());
        AddRow("Enemies defeated", r.EnemiesKilled.ToString());
        AddRow("Heroes lost", r.HeroesLost.ToString());
        AddRow("Villagers lost", r.VillagersLost.ToString());
        AddRow("Villagers escaped", r.VillagersEscaped.ToString());

        _score.text = $"SCORE  {r.Score:N0}";
        _overlay.style.display = DisplayStyle.Flex;
    }

    private System.Collections.IEnumerator LoadVictoryScene()
    {
        yield return new WaitForSecondsRealtime(0.8f);
        UnityEngine.SceneManagement.SceneManager.LoadScene(GameStateManager.VictorySceneName);
    }

    private void AddRow(string label, string value)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.paddingTop = row.style.paddingBottom = 3;

        Label left = MakeLabel(16, Text, false);
        Label right = MakeLabel(16, Text, true);
        left.text = label;
        right.text = value;
        row.Add(left);
        row.Add(right);
        _rows.Add(row);
    }

    private static Label MakeLabel(int size, Color color, bool bold)
    {
        var label = new Label();
        label.style.fontSize = size;
        label.style.color = color;
        label.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
        return label;
    }

    private static Button MakeButton(string text, System.Action onClick)
    {
        var button = new Button(onClick) { text = text };
        button.style.fontSize = 16;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.color = Cyan;
        button.style.backgroundColor = new Color(0f, 0.15f, 0.2f);
        button.style.marginLeft = button.style.marginRight = 8;
        button.style.paddingLeft = button.style.paddingRight = 22;
        button.style.paddingTop = button.style.paddingBottom = 10;
        SetBorder(button, Cyan, 1, 4);
        return button;
    }

    private static void SetBorder(VisualElement element, Color color, float width, float radius)
    {
        element.style.borderTopColor = element.style.borderBottomColor = element.style.borderLeftColor = element.style.borderRightColor = color;
        element.style.borderTopWidth = element.style.borderBottomWidth = element.style.borderLeftWidth = element.style.borderRightWidth = width;
        element.style.borderTopLeftRadius = element.style.borderTopRightRadius = element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = radius;
    }
}
