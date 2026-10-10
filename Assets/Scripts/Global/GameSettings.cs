using System;
using UnityEngine;

/// <summary>
/// Player preferences, stored in PlayerPrefs (separate from the campaign save,
/// so they survive New Game). Add new settings here as a property + key; the
/// SettingsPanel UI reads/writes through these.
/// </summary>
public static class GameSettings
{
    private const string ScrollKey = "settings.scrollSpeed";
    private const string VolumeKey = "settings.masterVolume";

    public const float MinScrollSpeed = 0.25f;
    public const float MaxScrollSpeed = 3f;

    private static float? _scrollSpeed;
    private static float? _masterVolume;

    /// <summary>Raised after any setting changes.</summary>
    public static event Action Changed;

    /// <summary>Multiplier on camera pan / scroll-zoom / pinch speed. 1 = default.</summary>
    public static float ScrollSpeed
    {
        get => _scrollSpeed ??= Mathf.Clamp(PlayerPrefs.GetFloat(ScrollKey, 1f), MinScrollSpeed, MaxScrollSpeed);
        set
        {
            _scrollSpeed = Mathf.Clamp(value, MinScrollSpeed, MaxScrollSpeed);
            PlayerPrefs.SetFloat(ScrollKey, _scrollSpeed.Value);
            Changed?.Invoke();
        }
    }

    /// <summary>Master volume 0..1, applied through AudioListener.volume.</summary>
    public static float MasterVolume
    {
        get => _masterVolume ??= Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
        set
        {
            _masterVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(VolumeKey, _masterVolume.Value);
            ApplyAudio();
            Changed?.Invoke();
        }
    }

    public static void Save() => PlayerPrefs.Save();

    public static void ResetToDefaults()
    {
        ScrollSpeed = 1f;
        MasterVolume = 1f;
        Save();
    }

    private static void ApplyAudio() => AudioListener.volume = MasterVolume;

    // Apply the saved volume as soon as the game starts, before any menu is opened.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyOnStartup() => ApplyAudio();
}
