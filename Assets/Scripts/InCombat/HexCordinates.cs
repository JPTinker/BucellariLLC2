using UnityEngine;

/// <summary>
/// Coordinate math for a pointy-top hex grid using "odd-r" horizontal offset
/// coordinates (matches HexTile.gridPosition directly - x = column, y = row).
/// </summary>
public static class HexCoordinates
{
    private static readonly Vector2Int[] EvenRowNeighbors =
    {
        new Vector2Int(+1, 0), new Vector2Int(0, -1), new Vector2Int(-1, -1),
        new Vector2Int(-1, 0), new Vector2Int(-1, +1), new Vector2Int(0, +1)
    };

    private static readonly Vector2Int[] OddRowNeighbors =
    {
        new Vector2Int(+1, 0), new Vector2Int(+1, -1), new Vector2Int(0, -1),
        new Vector2Int(-1, 0), new Vector2Int(0, +1), new Vector2Int(+1, +1)
    };

    public static Vector2Int[] GetNeighborCoordinates(Vector2Int gridPosition)
    {
        bool isOddRow = (gridPosition.y & 1) != 0;
        Vector2Int[] offsets = isOddRow ? OddRowNeighbors : EvenRowNeighbors;

        var result = new Vector2Int[6];
        for (int i = 0; i < 6; i++)
            result[i] = gridPosition + offsets[i];

        return result;
    }

    /// Converts offset coordinates to cube coordinates, used for distance calculations.
    public static Vector3Int OffsetToCube(Vector2Int gridPosition)
    {
        int x = gridPosition.x - (gridPosition.y - (gridPosition.y & 1)) / 2;
        int z = gridPosition.y;
        int y = -x - z;
        return new Vector3Int(x, y, z);
    }

    public static int GetDistance(Vector2Int a, Vector2Int b)
    {
        Vector3Int cubeA = OffsetToCube(a);
        Vector3Int cubeB = OffsetToCube(b);

        return (Mathf.Abs(cubeA.x - cubeB.x)
                + Mathf.Abs(cubeA.y - cubeB.y)
                + Mathf.Abs(cubeA.z - cubeB.z)) / 2;
    }

    /// World-space position for a tile, given hex "radius" (center to corner).
    public static Vector3 OffsetToWorldPosition(Vector2Int gridPosition, float hexSize)
    {
        float width = Mathf.Sqrt(3f) * hexSize;
        float height = 1.5f * hexSize;

        float x = width * (gridPosition.x + 0.5f * (gridPosition.y & 1));
        float z = height * gridPosition.y;

        return new Vector3(x, 0f, z);
    }
}