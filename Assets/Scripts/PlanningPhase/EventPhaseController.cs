using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Shows the pending narrative event (GameStateManager.Events) in PhaseShell's
/// event-overlay. Plain component (no UIDocument), initialized by
/// PhaseShellController like the other tab controllers. The overlay waits for
/// any draft / level-up / reveal overlay to finish before appearing.
/// </summary>
public class EventPhaseController : MonoBehaviour
{
    private VisualElement _root;
    private VisualElement _overlay, _image, _choices;
    private Label _title, _body, _result, _effects;
    private Button _continue;
    private IVisualElementScheduledItem _poll;

    public bool IsShowing => _overlay != null && !_overlay.ClassListContains("hidden");

    public void Initialize(VisualElement root)
    {
        _root = root;
        _overlay = root.Q<VisualElement>("event-overlay");
        _image = root.Q<VisualElement>("event-image");
        _choices = root.Q<VisualElement>("event-choices");
        _title = root.Q<Label>("event-title");
        _body = root.Q<Label>("event-body");
        _result = root.Q<Label>("event-result");
        _effects = root.Q<Label>("event-effects");
        _continue = root.Q<Button>("event-continue");

        if (_overlay == null || _choices == null)
        {
            Debug.LogError("EventPhaseController: event-overlay / event-choices missing from PhaseShell.uxml.");
            return;
        }

        _continue.clicked += CloseOverlay;

        // Poll so the event appears after a draft/reveal chain finishes, however it ends.
        _poll = root.schedule.Execute(TryShow).Every(300);
        TryShow();
    }

    private void TryShow()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null || IsShowing || !gsm.Events.HasPendingEvent) return;
        if (gsm.HasPendingDraft || gsm.PendingLevelUps.Count > 0 || gsm.PendingReveal.Count > 0) return;
        if (AnyVisible("draft-overlay") || AnyVisible("reveal-overlay") || AnyVisible("level-up-overlay")) return;

        Show(gsm.Events.PendingEvent);
    }

    private bool AnyVisible(string overlayName)
    {
        var o = _root.Q<VisualElement>(overlayName);
        return o != null && !o.ClassListContains("hidden");
    }

    private void Show(EventData evt)
    {
        var gsm = GameStateManager.Instance;
        _title.text = evt.Title;
        _body.text = evt.Body;
        _image.style.display = evt.Image != null ? DisplayStyle.Flex : DisplayStyle.None;
        _image.style.backgroundImage = evt.Image != null ? new StyleBackground(evt.Image) : StyleKeyword.None;

        _result.AddToClassList("hidden");
        _effects.AddToClassList("hidden");
        _continue.AddToClassList("hidden");
        _body.RemoveFromClassList("hidden");
        _choices.style.display = DisplayStyle.Flex;
        _choices.Clear();

        for (int i = 0; i < evt.Choices.Count; i++)
        {
            int index = i;
            EventChoice choice = evt.Choices[i];
            bool ok = gsm.Events.CanChoose(choice, out string reason);
            var button = new Button(() => OnChoose(index))
            {
                text = ok ? choice.Label : $"{choice.Label}  ({reason})"
            };
            button.AddToClassList("nav-button");
            button.AddToClassList("event-choice");
            button.SetEnabled(ok);
            _choices.Add(button);
        }

        _overlay.RemoveFromClassList("hidden");
    }

    private void OnChoose(int index)
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null || !gsm.Events.ResolveEvent(index, out EventManager.Outcome outcome)) return;

        _choices.Clear();
        _choices.style.display = DisplayStyle.None;

        _result.text = outcome.ResultText;
        _result.EnableInClassList("hidden", string.IsNullOrEmpty(outcome.ResultText));

        var sb = new StringBuilder();
        foreach (string line in outcome.EffectLines) sb.AppendLine(line);
        _effects.text = sb.ToString().TrimEnd();
        _effects.EnableInClassList("hidden", outcome.EffectLines.Count == 0);

        _continue.RemoveFromClassList("hidden");
        PhaseShellController.Instance?.RefreshHeader();
    }

    private void CloseOverlay()
    {
        _overlay.AddToClassList("hidden");
        // Effects can change the roster (recruit/loss); rebuild the roster list and play any recruit reveal.
        PhaseShellController.Instance?.RosterController?.OnRosterChangedExternally();
        PhaseShellController.Instance?.RefreshHeader();
    }
}
