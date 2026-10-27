using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Small coroutine-friendly UI Toolkit tweens shared by reward-style screens.</summary>
public static class UIAnim
{
    public static float EaseOutBack(float p)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float x = p - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }

    public static void SetScale(VisualElement el, float s) =>
        el.style.scale = new StyleScale(new Scale(new Vector2(s, s)));

    /// <summary>Bouncy scale+fade in. Uses unscaled time so it runs even if timeScale is touched.</summary>
    public static IEnumerator PunchIn(VisualElement el, float duration = 0.32f)
    {
        el.style.opacity = 0f;
        SetScale(el, 0f);
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float p = Mathf.Clamp01(t / duration);
            el.style.opacity = Mathf.Clamp01(p * 2.2f);
            SetScale(el, EaseOutBack(p));
            yield return null;
        }
        el.style.opacity = 1f;
        SetScale(el, 1f);
    }

    /// <summary>Quick grow-and-settle emphasis on an element already on screen.</summary>
    public static IEnumerator Pop(VisualElement el, float peak = 1.25f, float duration = 0.28f)
    {
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float p = t / duration;
            SetScale(el, 1f + (peak - 1f) * Mathf.Sin(p * Mathf.PI));
            yield return null;
        }
        SetScale(el, 1f);
    }

    public static IEnumerator FadeTo(VisualElement el, float to, float duration)
    {
        float from = el.resolvedStyle.opacity;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            el.style.opacity = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        el.style.opacity = to;
    }

    /// <summary>White screen flash that decays.</summary>
    public static IEnumerator Flash(VisualElement flash, float peak = 0.75f, float duration = 0.4f)
    {
        if (flash == null) yield break;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            flash.style.opacity = peak * (1f - t / duration);
            yield return null;
        }
        flash.style.opacity = 0f;
    }

    /// <summary>Decaying random wobble of a container, via translate.</summary>
    public static IEnumerator Shake(VisualElement el, float magnitude = 14f, float duration = 0.35f)
    {
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float decay = 1f - t / duration;
            el.style.translate = new StyleTranslate(new Translate(
                Random.Range(-magnitude, magnitude) * decay,
                Random.Range(-magnitude, magnitude) * decay));
            yield return null;
        }
        el.style.translate = new StyleTranslate(new Translate(0, 0));
    }

    static readonly Color[] ConfettiColors =
    {
        new Color(1f, 0.82f, 0.25f), new Color(0.0f, 0.95f, 1f), new Color(1f, 0.35f, 0.5f),
        new Color(0.5f, 1f, 0.55f), new Color(0.75f, 0.5f, 1f)
    };

    /// <summary>
    /// Bursts confetti from a point (in layer-local coordinates). UI Toolkit has no
    /// particles, so each piece is a small absolutely-positioned element animated by hand.
    /// </summary>
    public static IEnumerator Confetti(VisualElement layer, Vector2 origin, int count = 60, float duration = 1.6f)
    {
        var pieces = new VisualElement[count];
        var vel = new Vector2[count];
        var spin = new float[count];
        for (int i = 0; i < count; i++)
        {
            var p = new VisualElement { pickingMode = PickingMode.Ignore };
            float w = Random.Range(7f, 14f);
            p.style.position = Position.Absolute;
            p.style.width = w;
            p.style.height = w * Random.Range(0.4f, 1f);
            p.style.backgroundColor = ConfettiColors[Random.Range(0, ConfettiColors.Length)];
            p.style.left = origin.x;
            p.style.top = origin.y;
            layer.Add(p);
            pieces[i] = p;

            float angle = Random.Range(-Mathf.PI * 0.95f, -Mathf.PI * 0.05f); // upward fan
            float speed = Random.Range(380f, 900f);
            vel[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
            spin[i] = Random.Range(-720f, 720f);
        }

        const float gravity = 1500f;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float dt = Time.unscaledDeltaTime;
            float fade = Mathf.Clamp01((duration - t) / (duration * 0.4f));
            for (int i = 0; i < count; i++)
            {
                vel[i].y += gravity * dt;
                var p = pieces[i];
                p.style.left = p.style.left.value.value + vel[i].x * dt;
                p.style.top = p.style.top.value.value + vel[i].y * dt;
                p.style.rotate = new StyleRotate(new Rotate(new Angle(spin[i] * t, AngleUnit.Degree)));
                p.style.opacity = fade;
            }
            yield return null;
        }
        foreach (var p in pieces) p.RemoveFromHierarchy();
    }
}
