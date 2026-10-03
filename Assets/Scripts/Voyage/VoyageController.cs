using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Shown after the player commits a Travel cycle (GameStateManager.ExecuteCycle).
/// A boat sails along a route from where it was to where it is now; reaching the
/// end (EvacuationCyclesRemaining == 0) plays the "YOU WIN" screen, otherwise we
/// continue into the battle. Pure UI Toolkit - the scene needs only this
/// component and a UIDocument (see Tools > Create Voyage Scene).
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class VoyageController : MonoBehaviour
{
    [Header("Flow")]
    [SerializeField] private string titleSceneName = "TitleScreen";
    [SerializeField] private string fallbackNextSceneName = "DecisionPhase";

    [Header("Boat")]
    [Tooltip("Optional. Replaces the placeholder boat drawn from boxes.")]
    [SerializeField] private Sprite boatSprite;
    [SerializeField] private float sailSeconds = 2.8f;
    [SerializeField] private float bobHeight = 6f;
    [SerializeField] private float holdAfterSail = 0.8f;

    // Route runs between these percentages of the chart's width (matches Voyage.uss).
    private const float RouteStartPct = 8f;
    private const float RouteEndPct = 92f;

    private VisualElement _root, _route, _boat, _flash, _victory;
    private Label _status, _hint;
    private Button _victoryButton;
    private readonly List<VisualElement> _dots = new List<VisualElement>();
    private bool _skip;

    private void Awake()
    {
        // The scene is UI-only; without a camera Unity shows "No Cameras Rendering".
        if (Camera.allCamerasCount == 0)
        {
            var cam = new GameObject("Voyage Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;
        }
    }

    private void OnEnable()
    {
        _root = GetComponent<UIDocument>().rootVisualElement;
        _route = _root.Q("vy-route");
        _boat = _root.Q("vy-boat");
        _flash = _root.Q("vy-flash");
        _victory = _root.Q("vy-victory");
        _status = _root.Q<Label>("vy-status");
        _hint = _root.Q<Label>("vy-hint");
        _victoryButton = _root.Q<Button>("vy-victory-button");

        _root.RegisterCallback<PointerDownEvent>(OnPointerDown);
        if (_victoryButton != null) _victoryButton.clicked += OnVictoryClicked;
    }

    private void OnDisable()
    {
        _root?.UnregisterCallback<PointerDownEvent>(OnPointerDown);
        if (_victoryButton != null) _victoryButton.clicked -= OnVictoryClicked;
    }

    private void OnPointerDown(PointerDownEvent evt) => _skip = true;

    private void Start()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null || !gsm.HasPendingVoyage)
        {
            // Opened directly / nothing to show: don't strand the player here.
            SceneManager.LoadScene(fallbackNextSceneName);
            return;
        }

        if (boatSprite != null && _boat != null)
        {
            _boat.AddToClassList("vy-boat--sprite");
            _boat.style.backgroundImage = new StyleBackground(boatSprite);
        }

        StartCoroutine(Run(gsm));
    }

    private IEnumerator Run(GameStateManager gsm)
    {
        int total = GameStateManager.EvacuationCyclesTotal;
        float from = Progress(total - gsm.VoyageFromRemaining, total);
        float to = Progress(total - gsm.VoyageToRemaining, total);
        bool arrived = gsm.VoyageToRemaining <= 0;

        _status.text = arrived
            ? "LAND AHEAD"
            : $"{gsm.VoyageToRemaining} CYCLE{(gsm.VoyageToRemaining == 1 ? "" : "S")} UNTIL EVACUATION";

        BuildRoute(total, from);
        PlaceBoat(from, 0f);
        yield return new WaitForSecondsRealtime(0.5f);

        // Sail from the previous position to the new one.
        _skip = false;
        for (float t = 0f; t < sailSeconds && !_skip; t += Time.unscaledDeltaTime)
        {
            float p = Mathf.SmoothStep(0f, 1f, t / sailSeconds);
            float f = Mathf.Lerp(from, to, p);
            PlaceBoat(f, Mathf.Sin(t * 5f) * bobHeight);
            MarkPassed(f);
            yield return null;
        }
        PlaceBoat(to, 0f);
        MarkPassed(to);
        _hint?.AddToClassList("hidden");
        yield return new WaitForSecondsRealtime(holdAfterSail);

        gsm.ClearPendingVoyage();

        if (arrived)
        {
            yield return PlayVictory();
            yield break; // the button takes it from here
        }

        // Voyage done: on to the battle this cycle would have started anyway.
        if (gsm.ActiveTeam.Count > 0) gsm.LoadCombatMap(gsm.BattleSceneAfterVoyage);
        else SceneManager.LoadScene(fallbackNextSceneName);
    }

    private static float Progress(int cyclesDone, int total) =>
        Mathf.Clamp01(cyclesDone / (float)Mathf.Max(1, total));

    private void BuildRoute(int total, float initialProgress)
    {
        _route.Clear();
        _dots.Clear();
        for (int i = 0; i <= total; i++)
        {
            var dot = new VisualElement();
            dot.AddToClassList("vy-route-dot");
            dot.style.left = new Length(Mathf.Lerp(0f, 100f, i / (float)total), LengthUnit.Percent);
            _route.Add(dot);
            _dots.Add(dot);
        }
        MarkPassed(initialProgress);
    }

    private void MarkPassed(float progress)
    {
        int total = _dots.Count - 1;
        for (int i = 0; i < _dots.Count; i++)
            _dots[i].EnableInClassList("vy-route-dot--passed", i / (float)total <= progress + 0.001f);
    }

    private void PlaceBoat(float progress, float bob)
    {
        _boat.style.left = new Length(Mathf.Lerp(RouteStartPct, RouteEndPct, progress), LengthUnit.Percent);
        _boat.style.translate = new Translate(0f, bob);
    }

    private IEnumerator PlayVictory()
    {
        yield return UIAnim.Flash(_flash, 1f, 0.5f);

        // The run is over: drop the save and the live campaign so New Game starts clean.
        SaveSystem.Delete();

        _victory.RemoveFromClassList("hidden");
        yield return UIAnim.PunchIn(_victory, 0.6f);
    }

    private void OnVictoryClicked()
    {
        SaveSystem.LoadOnNextStart = false;
        if (GameStateManager.Instance != null) Destroy(GameStateManager.Instance.gameObject);
        SceneManager.LoadScene(titleSceneName);
    }
}
