using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Drives TitleScreen.uxml. The title scene deliberately has no
/// GameStateManager in it: New Game just loads the battle scene, whose own
/// FullGameManager prefab boots a fresh run. There is no save slot, so the
/// Continue button and the new-game confirmation are hidden.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class TitleScreenController : MonoBehaviour
{
    private Button _btnNewGame;

    private void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        _btnNewGame = root.Q<Button>("btn-new-game");

        HideElement(root.Q<VisualElement>("btn-continue"));
        HideElement(root.Q<VisualElement>("save-hint"));
        HideElement(root.Q<VisualElement>("confirm-overlay"));

        // Settings: button goes under New Game; overlay lives on the root so it covers the screen.
        if (root.Q<Button>("btn-settings") == null)
        {
            var settingsButton = SettingsPanel.Attach(root, _btnNewGame?.parent, _btnNewGame != null ? _btnNewGame.parent.IndexOf(_btnNewGame) + 1 : -1);
            if (settingsButton != null) settingsButton.AddToClassList("btn");
        }

        if (_btnNewGame != null) _btnNewGame.clicked += StartNewGame;
    }

    private void OnDisable()
    {
        if (_btnNewGame != null) _btnNewGame.clicked -= StartNewGame;
    }

    private static void HideElement(VisualElement element)
    {
        if (element != null) element.style.display = DisplayStyle.None;
    }

    private void StartNewGame() => SceneManager.LoadScene(GameStateManager.GameSceneName);
}
