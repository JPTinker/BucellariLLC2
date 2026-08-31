using UnityEngine;
using TMPro;
using System.Collections;

public class NotificationManager: MonoBehaviour
{
    [Header("References")]
    public TextMeshProUGUI notificationText;
    public CanvasGroup canvasGroup;

    [Header("Animation Settings")]
    public float fadeInDuration = 0.5f;
    public float displayDuration = 2f;
    public float fadeOutDuration = 0.5f;
    public float scaleAmount = 1.2f; // Scale effect for emphasis

    [Header("Movement (Optional)")]
    public bool slideIn = true;
    public float slideDistance = 100f;

    private RectTransform rectTransform;
    private Vector3 originalScale;
    private Vector2 originalPosition;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        originalScale = rectTransform.localScale;
        originalPosition = rectTransform.anchoredPosition;

        // Start hidden
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }
        notificationText.gameObject.SetActive(false);
    }

    public void ShowNotification(string message)
    {
        StopAllCoroutines();
        StartCoroutine(ShowNotificationCoroutine(message));
    }

    IEnumerator ShowNotificationCoroutine(string message)
    {
        // Setup
        notificationText.gameObject.SetActive(true);
        notificationText.text = message;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }

        // Reset position and scale
        rectTransform.localScale = originalScale * scaleAmount;

        if (slideIn)
        {
            rectTransform.anchoredPosition = originalPosition + Vector2.up * slideDistance;
        }

        // Fade in + slide in + scale down
        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeInDuration;

            if (canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.Lerp(0f, 1f, t);
            }

            rectTransform.localScale = Vector3.Lerp(
                originalScale * scaleAmount,
                originalScale,
                t
            );

            if (slideIn)
            {
                rectTransform.anchoredPosition = Vector2.Lerp(
                    originalPosition + Vector2.up * slideDistance,
                    originalPosition,
                    t
                );
            }

            yield return null;
        }

        // Ensure final values
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
        }
        rectTransform.localScale = originalScale;
        rectTransform.anchoredPosition = originalPosition;

        // Display
        yield return new WaitForSeconds(displayDuration);

        // Fade out
        elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeOutDuration;

            if (canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.Lerp(1f, 0f, t);
            }

            yield return null;
        }

        // Cleanup
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }
        notificationText.gameObject.SetActive(false);
    }
}