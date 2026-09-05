using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch; // Alias to prevent namespace conflicts
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

public class CameraController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 10f;
    public float dragSpeed = 0.01f;

    [Header("Zoom")]
    public float zoomSpeed = 5f;
    public float minZoom = 5f;
    public float maxZoom = 25f;

    [Header("Bounds (Optional)")]
    public Vector2 minBounds;
    public Vector2 maxBounds;
    public bool useBounds = false;

    private Camera cam;
    private Vector2 lastTouchPosition;

    void Awake()
    {
        cam = Camera.main;
    }

    void Update()
    {
        HandleKeyboardMovement();
        HandleMouseZoom();
        // Mobile / Touch Controls
        #if UNITY_IOS || UNITY_ANDROID
        HandleTouchControls();
        #endif 
        //ClampPosition();
        HandleMouseClick();
    }

    private void HandleMouseClick()
    {
        Mouse mouse = Mouse.current;
        // Detect left mouse click
        
        if (mouse.leftButton.wasPressedThisFrame)
        {
            Ray ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
            TrySelectTile(ray);
        }
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
        cam.orthographicSize -= scrollNormalized * zoomSpeed;
        cam.orthographicSize = Mathf.Clamp(cam.orthographicSize, minZoom, maxZoom);
    }

    // -----------------------
    // Mobile Touch Controls
    // -----------------------
    void HandleTouchControls()
    {
    // Ensure EnhancedTouch is active before reading touches
    if (!EnhancedTouchSupport.enabled)
    {
        EnhancedTouchSupport.Enable();
    }

    var activeTouches = Touch.activeTouches;

        if (activeTouches.Count == 1)
        {
            Touch touch = activeTouches[0];

            if (touch.phase == TouchPhase.Began)
            {
                lastTouchPosition = touch.screenPosition;
            }
            else if (touch.phase == TouchPhase.Moved)
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
        else if (activeTouches.Count == 2)
        {
            // Pinch zoom
            Touch t0 = activeTouches[0];
            Touch t1 = activeTouches[1];

            // Access previous position via touch.history or (screenPosition - delta)
            Vector2 prev0 = t0.screenPosition - t0.delta;
            Vector2 prev1 = t1.screenPosition - t1.delta;

            float prevDist = Vector2.Distance(prev0, prev1);
            float currDist = Vector2.Distance(t0.screenPosition, t1.screenPosition);

            float delta = currDist - prevDist;

            cam.orthographicSize -= delta * dragSpeed;
            cam.orthographicSize = Mathf.Clamp(cam.orthographicSize, minZoom, maxZoom);
        }
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
}
