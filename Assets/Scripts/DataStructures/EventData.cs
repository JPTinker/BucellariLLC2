using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A narrative choice event shown on the Decision screen. Random events are
/// rolled by <see cref="EventManager"/> each time DecisionPhase loads;
/// Scripted events fire deterministically at an exact evacuation countdown.
/// Create assets via Assets > Create > Game > Event Data and register them in
/// GameStateManager.AvailableEvents (the asset name is the save-file ID).
/// </summary>
[CreateAssetMenu(fileName = "NewEventData", menuName = "Game/Event Data")]
public class EventData : ScriptableObject
{
    public enum TriggerMode { Random, Scripted }

    [Header("Presentation")]
    public string Title;
    [TextArea(3, 8)]
    public string Body;
    public Sprite Image;

    [Header("Choices")]
    public List<EventChoice> Choices = new List<EventChoice>();

    [Header("Trigger")]
    public TriggerMode Mode = TriggerMode.Random;
    [Tooltip("Relative odds among eligible Random events.")]
    [Min(0)] public int Weight = 10;
    [Tooltip("Scripted only: fires when EvacuationCyclesRemaining equals this value.")]
    public int ScriptedCyclesRemaining = 5;

    [Header("Eligibility")]
    [Tooltip("Inclusive range of EvacuationCyclesRemaining in which this event may fire.")]
    public int MinCyclesRemaining = 0;
    public int MaxCyclesRemaining = GameStateManager.EvacuationCyclesTotal;
    public int MinStage = 0;
    [Tooltip("0 = no upper limit.")]
    public int MaxStage = 0;
    [Tooltip("Never fires again once it has fired in this campaign.")]
    public bool OneShot;
    [Tooltip("Minimum cycles between two firings of this event (0 = none).")]
    [Min(0)] public int CooldownCycles;

    /// <summary>Save-file key.</summary>
    public string EventID => name;
}

/// <summary>One button on an event popup.</summary>
[Serializable]
public class EventChoice
{
    public string Label;
    [TextArea(2, 5)]
    [Tooltip("Flavour text shown after this choice is picked.")]
    public string ResultText;
    public List<EventEffect> Effects = new List<EventEffect>();

    [Header("Requirements (0 = none)")]
    public int RequiresFood;
    public int RequiresMaterials;
    public int RequiresGold;
}

public enum EventEffectType
{
    Food,
    Materials,
    Gold,
    Morale,
    Villagers,
    /// <summary>Heals every roster unit to full.</summary>
    HealAll,
    /// <summary>Removes Amount HP from every roster unit (never kills; leaves 1 HP).</summary>
    DamageAllUnits,
    /// <summary>Adds a recruit. Uses Archetype, or a random archetype when empty. Skipped if the roster is full.</summary>
    GrantUnit,
    /// <summary>Permanently removes Amount random roster units (always keeps at least one).</summary>
    LoseRandomUnit
}

[Serializable]
public class EventEffect
{
    public EventEffectType Type;
    [Tooltip("Signed for resources/morale/villagers; HP for DamageAllUnits; count for LoseRandomUnit.")]
    public int Amount;
    [Tooltip("GrantUnit only. Leave empty for a random archetype.")]
    public UnitData Archetype;
}
