using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Drives TitleScreen.uxml. The title scene deliberately has no
/// GameStateManager in it: both buttons just load DecisionPhase, whose own
/// FullGameManager prefab boots the campaign. Continue sets
/// SaveSystem.LoadOnNextStart first so that GameStateManager restores the
/// save instead of starting fresh.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class TitleScreenController : MonoBehaviour
{
    [SerializeField] private string gameSceneName = "DecisionPhase";

    private Button _btnContinue, _btnNewGame, _btnConfirmNew, _btnCancelNew;
    private Label _saveHint;
    private VisualElement _confirmOverlay;

    private void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        _btnContinue = root.Q<Button>("btn-continue");
        _btnNewGame = root.Q<Button>("btn-new-game");
        _btnConfirmNew = root.Q<Button>("btn-confirm-new");
        _btnCancelNew = root.Q<Button>("btn-cancel-new");
        _saveHint = root.Q<Label>("save-hint");
        _confirmOverlay = root.Q<VisualElement>("confirm-overlay");

        bool hasSave = SaveSystem.HasSave;
        _btnContinue?.SetEnabled(hasSave);
        if (_saveHint != null) _saveHint.style.display = hasSave ? DisplayStyle.None : DisplayStyle.Flex;

        if (_btnContinue != null) _btnContinue.clicked += ContinueGame;
        if (_btnNewGame != null) _btnNewGame.clicked += OnNewGameClicked;
        if (_btnConfirmNew != null) _btnConfirmNew.clicked += StartNewGame;
        if (_btnCancelNew != null) _btnCancelNew.clicked += HideConfirm;
    }

    private void OnDisable()
    {
        if (_btnContinue != null) _btnContinue.clicked -= ContinueGame;
        if (_btnNewGame != null) _btnNewGame.clicked -= OnNewGameClicked;
        if (_btnConfirmNew != null) _btnConfirmNew.clicked -= StartNewGame;
        if (_btnCancelNew != null) _btnCancelNew.clicked -= HideConfirm;
    }

    private void ContinueGame()
    {
        if (!SaveSystem.HasSave) return;
        SaveSystem.LoadOnNextStart = true;
        SceneManager.LoadScene(gameSceneName);
    }

    private void OnNewGameClicked()
    {
        // Only ask for confirmation when there's a save the player might mean to keep.
        if (SaveSystem.HasSave && _confirmOverlay != null) _confirmOverlay.RemoveFromClassList("hidden");
        else StartNewGame();
    }

    private void StartNewGame()
    {
        SaveSystem.LoadOnNextStart = false;
        SceneManager.LoadScene(gameSceneName);
    }

    private void HideConfirm() => _confirmOverlay?.AddToClassList("hidden");
}
