using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Shows/hides the Roster and Decision screens so only one is visible at a
/// time. Both screens' nav buttons (nav-roster / nav-decision, wired in
/// PlanningPhaseController and DecisionPhaseController respectively) call
/// into this rather than knowing about each other directly.
///
/// Put this on any always-active GameObject in the scene - e.g. a shared
/// parent of both screen GameObjects - and assign both UIDocuments in the
/// Inspector. Campaign Map / Inventory aren't wired here yet since their
/// nav buttons are still disabled stubs; add a Tab entry + Show* method for
/// each once those screens exist.
///
/// Screens are toggled via rootVisualElement.style.display, NOT
/// GameObject.SetActive(). SetActive(false) stops any Coroutine running on
/// that GameObject rather than pausing it, which would kill an in-progress
/// reveal/level-up/draft fanfare on the Roster screen mid-animation if the
/// player switched tabs during one. Toggling display keeps both
/// MonoBehaviours alive and just removes the hidden one from layout and
/// hit-testing.
/// </summary>
public class PhaseTabSwitcher : MonoBehaviour
{
    public static PhaseTabSwitcher Instance { get; private set; }

    public enum Tab { Roster, Decision }

    [SerializeField] private UIDocument rosterDocument;
    [SerializeField] private UIDocument decisionDocument;

    public Tab CurrentTab { get; private set; } = Tab.Roster;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("PhaseTabSwitcher: multiple instances in scene, keeping the first.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // Roster is the entry point (reveal/level-up/draft overlays live
        // there and should be what the player sees first after combat).
        Show(Tab.Roster);
    }

    public void ShowRoster() => Show(Tab.Roster);
    public void ShowDecision() => Show(Tab.Decision);

    private void Show(Tab tab)
    {
        CurrentTab = tab;

        if (rosterDocument != null)
            rosterDocument.rootVisualElement.style.display =
                tab == Tab.Roster ? DisplayStyle.Flex : DisplayStyle.None;

        if (decisionDocument != null)
            decisionDocument.rootVisualElement.style.display =
                tab == Tab.Decision ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
