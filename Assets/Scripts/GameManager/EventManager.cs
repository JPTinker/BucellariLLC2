using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rolls, holds and resolves narrative events. Plain class owned by
/// GameStateManager (which supplies the registry, balance and state it
/// mutates). State is saved so reloading a pending event can't reroll it.
/// </summary>
public class EventManager
{
    public struct Outcome
    {
        public string ResultText;
        public List<string> EffectLines;
    }

    private readonly GameStateManager _gsm;

    private readonly HashSet<string> _firedIds = new HashSet<string>();
    private readonly Dictionary<string, int> _lastFiredCycle = new Dictionary<string, int>();

    private int _lastRollCycle = -1;

    public EventData PendingEvent { get; private set; }
    public bool HasPendingEvent => PendingEvent != null;

    /// <summary>Raised after a choice is applied: (event, choice index).</summary>
    public static event Action<EventData, int> OnEventResolved;

    public EventManager(GameStateManager gsm) { _gsm = gsm; }

    // ------------------------------------------------------------
    // Rolling
    // ------------------------------------------------------------

    /// <summary>Queues a scripted event if one matches, else rolls a random one. No-op if one is already pending.</summary>
    public void TryQueueEvent()
    {
        if (PendingEvent != null || _gsm.AvailableEvents == null) return;

        // One roll per cycle, remembered in the save, so reloading can't reroll.
        if (_lastRollCycle == _gsm.CycleNumber) return;
        _lastRollCycle = _gsm.CycleNumber;

        var scripted = new List<EventData>();
        var random = new List<EventData>();
        foreach (EventData e in _gsm.AvailableEvents)
        {
            if (e == null || e.Choices == null || e.Choices.Count == 0 || !IsEligible(e)) continue;
            if (e.Mode == EventData.TriggerMode.Scripted)
            {
                if (e.ScriptedCyclesRemaining == _gsm.EvacuationCyclesRemaining) scripted.Add(e);
            }
            else if (e.Weight > 0) random.Add(e);
        }

        EventData pick = null;
        if (scripted.Count > 0)
            pick = scripted[0];
        else if (random.Count > 0 && UnityEngine.Random.value < _gsm.Balance.EventChance)
            pick = PickWeighted(random);

        if (pick == null) return;

        PendingEvent = pick;
        _firedIds.Add(pick.EventID);
        _lastFiredCycle[pick.EventID] = _gsm.CycleNumber;
    }

    private bool IsEligible(EventData e)
    {
        int remaining = _gsm.EvacuationCyclesRemaining;
        if (remaining < e.MinCyclesRemaining || remaining > e.MaxCyclesRemaining) return false;

        int stage = _gsm.Resources.CurrentStageIndex;
        if (stage < e.MinStage || (e.MaxStage > 0 && stage > e.MaxStage)) return false;

        if (e.OneShot && _firedIds.Contains(e.EventID)) return false;
        if (e.CooldownCycles > 0 && _lastFiredCycle.TryGetValue(e.EventID, out int last) &&
            _gsm.CycleNumber - last < e.CooldownCycles) return false;
        return true;
    }

    private static EventData PickWeighted(List<EventData> pool)
    {
        int total = 0;
        foreach (EventData e in pool) total += e.Weight;
        int roll = UnityEngine.Random.Range(0, total);
        foreach (EventData e in pool)
        {
            roll -= e.Weight;
            if (roll < 0) return e;
        }
        return pool[pool.Count - 1];
    }

    // ------------------------------------------------------------
    // Resolving
    // ------------------------------------------------------------

    public bool CanChoose(EventChoice c, out string reason)
    {
        reason = null;
        var s = _gsm.Settlement;
        if (c.RequiresFood > 0 && s.Food < c.RequiresFood) reason = $"Needs {c.RequiresFood} Food";
        else if (c.RequiresMaterials > 0 && s.Materials < c.RequiresMaterials) reason = $"Needs {c.RequiresMaterials} Scrap";
        else if (c.RequiresGold > 0 && _gsm.Resources.Gold < c.RequiresGold) reason = $"Needs {c.RequiresGold} Gold";
        return reason == null;
    }

    /// <summary>Applies the chosen option and clears the pending event. Returns false if there is no event or the choice is invalid/unaffordable.</summary>
    public bool ResolveEvent(int choiceIndex, out Outcome outcome)
    {
        outcome = default;
        EventData evt = PendingEvent;
        if (evt == null || choiceIndex < 0 || choiceIndex >= evt.Choices.Count) return false;

        EventChoice choice = evt.Choices[choiceIndex];
        if (!CanChoose(choice, out _)) return false;

        outcome.ResultText = choice.ResultText;
        outcome.EffectLines = new List<string>();
        foreach (EventEffect effect in choice.Effects)
        {
            string line = Apply(effect);
            if (!string.IsNullOrEmpty(line)) outcome.EffectLines.Add(line);
        }

        PendingEvent = null;
        OnEventResolved?.Invoke(evt, choiceIndex);
        return true;
    }

    private string Apply(EventEffect e)
    {
        var s = _gsm.Settlement;
        switch (e.Type)
        {
            case EventEffectType.Food:
                s.Food = Mathf.Max(0, s.Food + e.Amount);
                return Signed(e.Amount, "Food");
            case EventEffectType.Materials:
                s.Materials = Mathf.Max(0, s.Materials + e.Amount);
                return Signed(e.Amount, "Scrap");
            case EventEffectType.Gold:
                _gsm.Resources.Gold = Mathf.Max(0, _gsm.Resources.Gold + e.Amount);
                return Signed(e.Amount, "Gold");
            case EventEffectType.Morale:
                s.Morale = Mathf.Clamp(s.Morale + e.Amount, 0, 100);
                return Signed(e.Amount, "Morale");
            case EventEffectType.Villagers:
                s.Villagers = Mathf.Clamp(s.Villagers + e.Amount, 0, s.VillagerCapacity);
                return Signed(e.Amount, "Villagers");
            case EventEffectType.HealAll:
                foreach (Unit u in _gsm.FullRoster) u.CurrentHP = u.MaxHP;
                return "All units healed";
            case EventEffectType.DamageAllUnits:
                foreach (Unit u in _gsm.FullRoster) u.CurrentHP = Mathf.Max(1, u.CurrentHP - Mathf.Abs(e.Amount));
                return $"All units -{Mathf.Abs(e.Amount)} HP";
            case EventEffectType.GrantUnit:
                return GrantUnit(e.Archetype);
            case EventEffectType.LoseRandomUnit:
                return LoseUnits(Mathf.Max(1, e.Amount));
        }
        return null;
    }

    private string GrantUnit(UnitData archetype)
    {
        if (_gsm.UnitsFull) return "Roster full - no recruit joined";
        if (archetype == null)
        {
            var pool = _gsm.AvailablePlayerArchetypes?.FindAll(a => a != null);
            if (pool == null || pool.Count == 0) return null;
            archetype = pool[UnityEngine.Random.Range(0, pool.Count)];
        }
        _gsm.AddUnitToRoster(archetype, flagAsNew: true);
        return $"+1 recruit ({archetype.UnitName})";
    }

    private string LoseUnits(int count)
    {
        int lost = 0;
        for (int i = 0; i < count && _gsm.FullRoster.Count > 1; i++)
        {
            Unit u = _gsm.FullRoster[UnityEngine.Random.Range(0, _gsm.FullRoster.Count)];
            _gsm.FullRoster.Remove(u);
            _gsm.ActiveTeam.Remove(u);
            _gsm.PendingReveal.Remove(u);
            lost++;
        }
        return lost > 0 ? $"-{lost} unit{(lost > 1 ? "s" : "")} lost" : null;
    }

    private static string Signed(int amount, string label) => $"{(amount >= 0 ? "+" : "")}{amount} {label}";

    // ------------------------------------------------------------
    // Save / load
    // ------------------------------------------------------------

    public void WriteTo(SaveData d)
    {
        d.PendingEventId = PendingEvent != null ? PendingEvent.EventID : null;
        d.LastEventRollCycle = _lastRollCycle;
        d.FiredEventIds = new List<string>(_firedIds);
        d.EventCooldownIds = new List<string>();
        d.EventCooldownValues = new List<int>();
        foreach (var kv in _lastFiredCycle)
        {
            d.EventCooldownIds.Add(kv.Key);
            d.EventCooldownValues.Add(kv.Value);
        }
    }

    public void ReadFrom(SaveData d)
    {
        _firedIds.Clear();
        _lastFiredCycle.Clear();
        PendingEvent = null;
        // Pre-event saves (version 1) have no field -> 0, which equals their cycle 0 and skips one roll; harmless.
        _lastRollCycle = d.LastEventRollCycle;

        if (d.FiredEventIds != null)
            foreach (string id in d.FiredEventIds) if (!string.IsNullOrEmpty(id)) _firedIds.Add(id);

        if (d.EventCooldownIds != null && d.EventCooldownValues != null)
        {
            int n = Mathf.Min(d.EventCooldownIds.Count, d.EventCooldownValues.Count);
            for (int i = 0; i < n; i++) _lastFiredCycle[d.EventCooldownIds[i]] = d.EventCooldownValues[i];
        }

        if (!string.IsNullOrEmpty(d.PendingEventId) && _gsm.AvailableEvents != null)
            PendingEvent = _gsm.AvailableEvents.Find(e => e != null && e.EventID == d.PendingEventId);
    }
}
