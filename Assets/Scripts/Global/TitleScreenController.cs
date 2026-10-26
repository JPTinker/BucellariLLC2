using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Drives TitleScreen.uxml. The title scene deliberately has no
/// GameStateManager in it: New Game just loads the battle scene, whose own
/// FullGameManager prefab boots a fresh run. Continue (shown only when a battle
/// save exists) flags SaveSystem.LoadOnNextStart so that scene rebuilds the saved battle instead.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class TitleScreenController : MonoBehaviour
{
    private Button _btnNewGame;
    private Button _btnContinue;

    private void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        _btnNewGame = root.Q<Button>("btn-new-game");

        _btnContinue = root.Q<Button>("btn-continue");
        string saveSummary = SaveSystem.Describe();
        if (saveSummary != null)
        {
            Label detail = root.Q<Label>("continue-detail");
            if (detail != null) detail.text = saveSummary;
            if (_btnContinue != null) _btnContinue.clicked += ContinueGame;
            HideElement(root.Q<VisualElement>("save-hint"));
        }
        else
        {
            HideElement(_btnContinue);
        }
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
        if (_btnContinue != null) _btnContinue.clicked -= ContinueGame;
    }

    private static void HideElement(VisualElement element)
    {
        if (element != null) element.style.display = DisplayStyle.None;
    }

    private void StartNewGame()
    {
        SaveSystem.LoadOnNextStart = false;
        SceneManager.LoadScene(GameStateManager.GameSceneName);
    }

    private void ContinueGame()
    {
        SaveSystem.LoadOnNextStart = true;
        SceneManager.LoadScene(GameStateManager.GameSceneName);
    }
}
