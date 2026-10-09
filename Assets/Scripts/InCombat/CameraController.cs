using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch; // Alias to prevent namespace conflicts
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

public class CameraController : MonoBehaviour
{
    public static CameraController Instance { get; private set; }

    [Header("Movement")]
    public float moveSpeed = 10f;
    public float dragSpeed = 0.1f;

    [Header("Zoom")]
    public float zoomSpeed = 5f;
    public float minZoom = 5f;
    public float maxZoom = 25f;
    public float pinchZoomSpeed = 0.02f;

    [Header("Bounds (Optional)")]
    public Vector2 minBounds;
    public Vector2 maxBounds;
    public bool useBounds = false;

    [Header("Spawn Focus")]
    [Tooltip("Orthographic size used while focused on a spawning unit.")]
    public float spawnFocusZoom = 6f;
    [Tooltip("Seconds to pan/zoom in on the spawning unit.")]
    public float spawnPanInDuration = 0.5f;
    [Tooltip("Seconds to hold on the spawning unit before returning.")]
    public float spawnHoldDuration = 0.6f;
    [Tooltip("Seconds to pan/zoom back out to the default view.")]
    public float spawnPanOutDuration = 0.5f;

    [Header("Touch Tap")]
    [Tooltip("Max finger travel (pixels) for a touch to still count as a tap.")]
    public float tapMaxMovePixels = 25f;
    [Tooltip("Max seconds between touch down and up for a tap.")]
    public float tapMaxDuration = 0.35f;

    private Camera cam;
    private Vector2 touchStartPosition;
    private float touchStartTime;
    private bool touchStartedOverUI;
    private bool gestureWasMultiTouch;
    private Vector2 lastTouchPosition;
    private int lastTouchCount = 0;
    private Vector3 defaultPosition;
    private float defaultZoom;
    private bool isFocusing = false;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        cam = Camera.main;
        defaultPosition = transform.position;
        defaultZoom = cam.orthographicSize;
    }

    void OnEnable()
    {
        if (!EnhancedTouchSupport.enabled)
            EnhancedTouchSupport.Enable();
    }

    void OnDisable()
    {
        if (EnhancedTouchSupport.enabled)
            EnhancedTouchSupport.Disable();
    }

    void Update()
    {
        // User controls are suspended while the camera is focusing on a spawning unit.
        if (isFocusing) return;

        HandleKeyboardMovement();
        HandleMouseZoom();
        // Mobile / Touch Controls (also compiled in-editor so it can be tested via the Device Simulator)
        #if UNITY_IOS || UNITY_ANDROID || UNITY_EDITOR
        HandleTouchControls();
        #endif
        //ClampPosition();
        HandleMouseClick();
    }

    private void HandleMouseClick()
    {
        Mouse mouse = Mouse.current;
        // No mouse on touch devices (iPhone) - Mouse.current is null there.
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
            TrySelectTileAt(mouse.position.ReadValue());
    }

    /// <summary>Raycasts into the world from a screen position, unless a UI Toolkit element is under it.</summary>
    private void TrySelectTileAt(Vector2 screenPos)
    {
        if (IsPointerOverUI(screenPos)) return;
        TrySelectTile(cam.ScreenPointToRay(screenPos));
    }

    /// <summary>True if a pickable UI Toolkit element (button, panel, etc.) is under the screen position.</summary>
    private static bool IsPointerOverUI(Vector2 screenPos)
    {
        Vector2 flipped = new(screenPos.x, Screen.height - screenPos.y);
        foreach (UIDocument doc in FindObjectsByType<UIDocument>())
        {
            VisualElement root = doc.rootVisualElement;
            if (root?.panel == null) continue;
            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(root.panel, flipped);
            if (root.panel.Pick(panelPos) != null) return true;
        }
        return false;
    }

    // -----------------------
    // PC Movement (WASD)
    // -----------------------
    void HandleKeyboardMovement()
    {
        // 2. Access the current keyboard instance
        Keyboard keyboard = Keyboard.current;

        // Return early if no keyboard is plugged in/detected
        if (keyboard == null) return;

        // 3. Read input keys using KeyControl properties or buttons
        float h = 0f;
        float v = 0f;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) h -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) h += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) v -= 1f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) v += 1f;

        // 4. Normalize movement vector so diagonal travel isn't faster
        Vector3 dir = new Vector3(h, 0f, v).normalized;

        float zoomFactor = ZoomScaledSpeed();
        transform.Translate(dir * moveSpeed * zoomFactor * Time.deltaTime, Space.World);
    }
    // -----------------------
    // PC Zoom (Mouse Wheel)
    // -----------------------
    void HandleMouseZoom()
    {
        // 1. Check if a mouse is connected
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        // 2. Read the Vector2 scroll value (y represents the vertical scroll wheel)
        float scroll = mouse.scroll.ReadValue().y;
        if (scroll == 0f) return;

        // 3. Normalize the value (the new system returns delta increments like ~120 per notch)
        float scrollNormalized = Mathf.Sign(scroll);

        // 4. Update the orthographic size
        //Debug.Log($"Mouse scroll detected: {scroll}, normalized: {scrollNormalized}");
        cam.orthographicSize -= scrollNormalized * zoomSpeed;
        cam.orthographicSize = Mathf.Clamp(cam.orthographicSize, minZoom, maxZoom);
    }

    // -----------------------
    // Mobile Touch Controls
    // -----------------------
    void HandleTouchControls()
    {
        var activeTouches = Touch.activeTouches;
        int touchCount = activeTouches.Count;

        if (touchCount == 1)
        {
            Touch touch = activeTouches[0];

            if (touch.phase == TouchPhase.Began)
            {
                touchStartPosition = touch.screenPosition;
                touchStartTime = Time.unscaledTime;
                touchStartedOverUI = IsPointerOverUI(touch.screenPosition);
                gestureWasMultiTouch = false;
            }

            // Also resync on the first frame we drop back to one finger (e.g. after a pinch),
            // otherwise the pan would jump using a stale lastTouchPosition from before the pinch.
            if (touch.phase == TouchPhase.Began || lastTouchCount != 1)
            {
                lastTouchPosition = touch.screenPosition;
            }
            else if (touch.phase == TouchPhase.Ended)
            {
                bool isTap = !touchStartedOverUI && !gestureWasMultiTouch
                    && Time.unscaledTime - touchStartTime <= tapMaxDuration
                    && (touch.screenPosition - touchStartPosition).magnitude <= tapMaxMovePixels;
                if (isTap) TrySelectTileAt(touch.screenPosition);
            }
            else if (touch.phase == TouchPhase.Moved && !touchStartedOverUI)
            {
                Vector2 currentPos = touch.screenPosition;
                Vector3 delta = currentPos - lastTouchPosition;

                Vector3 move = new Vector3(
                    -delta.x * dragSpeed,
                    0f,
                    -delta.y * dragSpeed
                );

                float zoomFactor = ZoomScaledSpeed();
                transform.Translate(move * zoomFactor, Space.World);
                lastTouchPosition = currentPos;
            }
        }
        else if (touchCount == 2)
        {
            gestureWasMultiTouch = true;

            // Pinch zoom
            Touch t0 = activeTouches[0];
            Touch t1 = activeTouches[1];

            // Skip the frame either finger first touches down: its delta is zero, which would
            // otherwise be read as the previous pinch distance and cause a zoom jolt.
            if (lastTouchCount == 2 && t0.phase != TouchPhase.Began && t1.phase != TouchPhase.Began)
            {
                Vector2 prev0 = t0.screenPosition - t0.delta;
                Vector2 prev1 = t1.screenPosition - t1.delta;

                float prevDist = Vector2.Distance(prev0, prev1);
                float currDist = Vector2.Distance(t0.screenPosition, t1.screenPosition);

                float delta = currDist - prevDist;

                cam.orthographicSize -= delta * pinchZoomSpeed;
                cam.orthographicSize = Mathf.Clamp(cam.orthographicSize, minZoom, maxZoom);
            }
        }

        lastTouchCount = touchCount;
    }

    // -----------------------
    // Clamp Camera Position
    // -----------------------
    void ClampPosition()
    {
        if (!useBounds) return;

        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x, minBounds.x, maxBounds.x);
        pos.z = Mathf.Clamp(pos.z, minBounds.y, maxBounds.y);
        transform.position = pos;
    }

    float ZoomScaledSpeed()
    {
        return cam.orthographicSize / maxZoom;
    }
    void TrySelectTile(Ray ray)
    {
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            HexTile tile = hit.collider.GetComponent<HexTile>();
            if (tile != null)
                tile.OnTilePressed();
        }
    }

    // -----------------------
    // Spawn Focus: pan/zoom in on a spawning unit, hold, then return to the
    // default view. User controls are disabled for the duration. Callers
    // (e.g. MapManager) should `yield return StartCoroutine(...)` this to
    // sequence focus on multiple units one at a time.
    // -----------------------
    public IEnumerator FocusOnUnit(Transform target)
    {
        if (target == null) yield break;

        // Wait out any focus already in progress so overlapping calls queue instead of clashing.
        while (isFocusing)
            yield return null;

        isFocusing = true;

        Vector3 startPosition = transform.position;
        float startZoom = cam.orthographicSize;
        Vector3 focusPosition = new Vector3(target.position.x, startPosition.y, target.position.z - 5.5f); //added in a camera offset so the object is in frame. 

        yield return PanAndZoom(startPosition, focusPosition, startZoom, spawnFocusZoom, spawnPanInDuration);
        yield return new WaitForSeconds(spawnHoldDuration);
        yield return PanAndZoom(transform.position, defaultPosition, cam.orthographicSize, defaultZoom, spawnPanOutDuration);

        isFocusing = false;
    }
    /// <summary>Pans to a unit and stays there (zoom unchanged). Skipped while a spawn focus is running.</summary>
    public void PanToUnit(Transform target, float duration = 0.4f)
    {
        if (target == null || isFocusing) return;
        if (panRoutine != null) StopCoroutine(panRoutine);
        Vector3 focusPosition = new Vector3(target.position.x, transform.position.y, target.position.z - 5.5f);
        panRoutine = StartCoroutine(PanAndZoom(transform.position, focusPosition, cam.orthographicSize, cam.orthographicSize, duration));
    }

    private Coroutine panRoutine;

    private IEnumerator PanAndZoom(Vector3 fromPosition, Vector3 toPosition, float fromZoom, float toZoom, float duration)
    {
        if (duration <= 0f)
        {
            transform.position = toPosition;
            cam.orthographicSize = toZoom;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            transform.position = Vector3.Lerp(fromPosition, toPosition, t);
            cam.orthographicSize = Mathf.Lerp(fromZoom, toZoom, t);
            yield return null;
        }

        transform.position = toPosition;
        cam.orthographicSize = toZoom;
    }
}
