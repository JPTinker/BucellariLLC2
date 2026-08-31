using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MVP win/lose check: level ends when one side has no units left.
/// Register units with Track() as they're spawned (or point it at a UnitSpawner).
/// </summary>
public class CombatManager : MonoBehaviour
{
    public event Action OnLevelWon;
    public event Action OnLevelLost;

    private readonly List<UnitInstance> playerUnits = new List<UnitInstance>();
    private readonly List<UnitInstance> enemyUnits = new List<UnitInstance>();

    private bool levelEnded = false;



    public void TrackAll(IEnumerable<UnitInstance> units)
    {
        foreach (var unit in units)
            Track(unit);
    }

    public void Track(UnitInstance unit)
    {
        if (unit == null) return;

        var list = unit.isEnemy ? enemyUnits : playerUnits;
        list.Add(unit);
        unit.OnDeath += HandleUnitDeath;
    }

    private void HandleUnitDeath(UnitInstance unit)
    {
        unit.OnDeath -= HandleUnitDeath;

        playerUnits.Remove(unit);
        enemyUnits.Remove(unit);

        CheckLevelEnd();
    }

    private void CheckLevelEnd()
    {
        if (levelEnded) return;

        if (enemyUnits.Count == 0)
        {
            levelEnded = true;
            OnLevelWon?.Invoke();
        }
        else if (playerUnits.Count == 0)
        {
            levelEnded = true;
            OnLevelLost?.Invoke();
        }
    }
}