using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Shows short speech bubbles above units. Listens to <see cref="UnitSpeech.OnSpeak"/>,
/// asks <see cref="UnitSpeechLibrary"/> what to say, and pins a bubble above the
/// speaker in screen space (same projection as UnitStatusOverlayController).
/// Added automatically next to UnitStatusOverlayController, so it needs no scene setup.
/// Bubbles are built in code with inline styles; move them to UXML/USS when the look is settled.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class UnitSpeechBubbleController : MonoBehaviour
{
    [SerializeField] private bool speechEnabled = true;
    [SerializeField] private Camera worldCamera;

    [Header("Placement")]
    [Tooltip("World height above the unit's pivot. Sits above the status badge (which tracks ~2.2).")]
    [SerializeField] private float bubbleHeightOffset = 1.25f;
    [SerializeField] private float riseDistance = 14.4f;

    [Header("Timing")]
    [SerializeField] private float lifetime = 2.4f;
    [SerializeField] private float fadeTime = 0.4f;
    [Tooltip("Minimum seconds between bubbles from the same unit. Priority triggers (kill, death, level up, flanked) ignore it.")]
    [SerializeField] private float unitCooldown = 4f;
    [Tooltip("Cap on bubbles on screen at once, so a big fight doesn't become a wall of text.")]
    [SerializeField, Min(1)] private int maxBubbles = 4;

    private UIDocument uiDocument;
    private VisualElement layer;
    private readonly List<Bubble> bubbles = new List<Bubble>();
    private readonly Dictionary<UnitInstance, float> lastSpoke = new Dictionary<UnitInstance, float>();

    private sealed class Bubble
    {
        public UnitInstance Unit;
        public Vector3 LastWorldPoint;
        public VisualElement Root;
        public float Age;
    }

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
        if (worldCamera == null) worldCamera = Camera.main;
    }

    private void OnEnable() => UnitSpeech.OnSpeak += HandleSpeak;

    private void OnDisable() => UnitSpeech.OnSpeak -= HandleSpeak;

    private void Start()
    {
        if (uiDocument == null || uiDocument.rootVisualElement == null) return;

        layer = new VisualElement { name = "speech-bubble-layer", pickingMode = PickingMode.Ignore };
        layer.style.position = Position.Absolute;
        layer.style.left = 0;
        layer.style.top = 0;
        layer.style.right = 0;
        layer.style.bottom = 0;
        uiDocument.rootVisualElement.Add(layer);
    }

    private void HandleSpeak(UnitInstance unit, UnitSpeechTrigger trigger, float delay)
    {
        if (!speechEnabled || layer == null || unit == null) return;
        if (delay > 0f) StartCoroutine(SpeakAfter(unit, trigger, delay));
        else TrySpeak(unit, trigger);
    }

    private System.Collections.IEnumerator SpeakAfter(UnitInstance unit, UnitSpeechTrigger trigger, float delay)
    {
        yield return new WaitForSeconds(delay);
        TrySpeak(unit, trigger); // a unit that died in the meantime is handled by TrySpeak/Show
    }

    private void TrySpeak(UnitInstance unit, UnitSpeechTrigger trigger)
    {
        if (unit == null || !unit.IsRevealed) return; // never talk from inside the fog

        bool priority = IsPriority(trigger);
        if (!priority && lastSpoke.TryGetValue(unit, out float last) && Time.time - last < unitCooldown) return;

        string text = UnitSpeechLibrary.Pick(unit, trigger);
        if (string.IsNullOrEmpty(text)) return;

        // One bubble per unit: a newer line replaces the old one.
        for (int i = bubbles.Count - 1; i >= 0; i--)
            if (bubbles[i].Unit == unit) RemoveBubble(i);

        if (bubbles.Count >= maxBubbles)
        {
            if (!priority) return;
            RemoveBubble(0); // drop the oldest to make room
        }

        lastSpoke[unit] = Time.time;
        Show(unit, text);
    }

    private static bool IsPriority(UnitSpeechTrigger trigger) =>
        trigger == UnitSpeechTrigger.Kill || trigger == UnitSpeechTrigger.Death ||
        trigger == UnitSpeechTrigger.LevelUp || trigger == UnitSpeechTrigger.Flanked;

    private void Show(UnitInstance unit, string text)
    {
        VisualElement root = new VisualElement { pickingMode = PickingMode.Ignore };
        root.style.position = Position.Absolute;
        root.style.translate = new Translate(Length.Percent(-50), Length.Percent(-100));
        root.style.maxWidth = 220;
        root.style.paddingLeft = root.style.paddingRight = 10;
        root.style.paddingTop = root.style.paddingBottom = 5;
        root.style.backgroundColor = new Color(1f, 1f, 1f, 0.95f);
        root.style.borderTopLeftRadius = root.style.borderTopRightRadius =
            root.style.borderBottomLeftRadius = root.style.borderBottomRightRadius = 10;
        root.style.borderTopWidth = root.style.borderBottomWidth =
            root.style.borderLeftWidth = root.style.borderRightWidth = 2;
        Color border = unit.Faction == UnitFaction.Enemy ? new Color(0.75f, 0.2f, 0.2f) : new Color(0.2f, 0.4f, 0.75f);
        root.style.borderTopColor = root.style.borderBottomColor =
            root.style.borderLeftColor = root.style.borderRightColor = border;

        Label label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.color = new Color(0.1f, 0.1f, 0.12f);
        label.style.fontSize = 14;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        root.Add(label);

        layer.Add(root);
        bubbles.Add(new Bubble { Unit = unit, LastWorldPoint = BubblePoint(unit.transform.position), Root = root });
    }

    private Vector3 BubblePoint(Vector3 unitPosition) => unitPosition + Vector3.up * bubbleHeightOffset;

    private void Update()
    {
        if (layer == null || bubbles.Count == 0) return;

        if (worldCamera == null)
        {
            worldCamera = Camera.main;
            if (worldCamera == null) return;
        }

        VisualElement root = uiDocument.rootVisualElement;
        if (root.panel == null) return;

        for (int i = bubbles.Count - 1; i >= 0; i--)
        {
            Bubble bubble = bubbles[i];
            bubble.Age += Time.deltaTime;
            if (bubble.Age >= lifetime) { RemoveBubble(i); continue; }

            // A speaker that was destroyed (a dying line) keeps its bubble where it stood.
            if (bubble.Unit != null) bubble.LastWorldPoint = BubblePoint(bubble.Unit.transform.position);

            Vector3 viewport = worldCamera.WorldToViewportPoint(bubble.LastWorldPoint);
            bool onScreen = viewport.z > 0f && viewport.x > 0f && viewport.x < 1f && viewport.y > 0f && viewport.y < 1f;
            bubble.Root.style.display = onScreen ? DisplayStyle.Flex : DisplayStyle.None;
            if (!onScreen) continue;

            Vector2 panelPoint = RuntimePanelUtils.CameraTransformWorldToPanel(root.panel, bubble.LastWorldPoint, worldCamera);
            float t = bubble.Age / lifetime;
            bubble.Root.style.left = panelPoint.x;
            bubble.Root.style.top = panelPoint.y - riseDistance * t;

            float fadeStart = lifetime - fadeTime;
            bubble.Root.style.opacity = bubble.Age > fadeStart ? Mathf.Clamp01((lifetime - bubble.Age) / fadeTime) : 1f;
        }
    }

    private void RemoveBubble(int index)
    {
        bubbles[index].Root.RemoveFromHierarchy();
        bubbles.RemoveAt(index);
    }
}
