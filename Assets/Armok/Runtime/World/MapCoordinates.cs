using DFHack;
using UnityEngine;

/// <summary>DF tile anchors use X/east, Y/south and Z/up; Unity uses Y/up and negative Z/south.</summary>
public static class MapCoordinates
{
    public const float TileHeight = 3.0f;
    public const float FloorHeight = 0.5f;
    public const float TileWidth = 2.0f;
    public const int BlockSize = 16;

    static object mapZOffsetLock = new object();
    static int _mapZoffset = 0;
    public static int MapZOffset
    {
        get
        {
            lock (mapZOffsetLock)
            {
                return _mapZoffset;
            }
        }
        set
        {
            lock (mapZOffsetLock)
            {
                _mapZoffset = value;
            }
        }
    }
    public static Vector3 DFtoUnityCoord(int x, int y, int z)
    {
        Vector3 outCoord = new Vector3(x * TileWidth, (z + MapZOffset) * TileHeight, y * (-TileWidth));
        return outCoord;
    }
    public static Vector3 DFtoUnityCoord(float x, float y, float z)
    {
        Vector3 outCoord = new Vector3(x * TileWidth, (z + MapZOffset) * TileHeight, y * (-TileWidth));
        return outCoord;
    }
    public static Vector3 DFtoUnityDirection(float x, float y, float z)
    {
        return new Vector3(x* TileWidth, z *TileHeight, y * (-TileWidth));
    }
    public static Vector3 DFtoUnityCoord(DFCoord input)
    {
        Vector3 outCoord = new Vector3(input.x * TileWidth, (input.z + MapZOffset) * TileHeight, input.y * (-TileWidth));
        return outCoord;
    }
    public static Vector3 DFtoUnityDirection(DFCoord input)
    {
        return DFtoUnityDirection(input.x, input.y, input.z);
    }
    public static Vector3 DFtoUnityTileCenter(DFCoord input)
    {
        Vector3 result = DFtoUnityCoord(input);
        result.y += TileHeight / 2;
        return result;
    }
    public static Vector3 DFtoUnityBottomCorner(DFCoord input)
    {
        Vector3 result = DFtoUnityCoord(input);
        result.x -= TileWidth / 2;
        result.z -= TileWidth / 2;
        return result;
    }
    public static DFCoord UnityToDFCoord(Vector3 input)
    {
        int x = Mathf.RoundToInt(input.x / TileWidth);
        int y = Mathf.RoundToInt(input.z / -TileWidth);
        int z = Mathf.FloorToInt(input.y / TileHeight);
        return new DFCoord(x, y, z - MapZOffset);
    }
    public static Vector3 UnityToFloatingDFCoord(Vector3 input)
    {
        float x = input.x / TileWidth;
        float y = input.z / -TileWidth;
        float z = input.y / TileHeight;
        return new Vector3(x + 0.5f, y + 0.5f, z - MapZOffset);
    }
    public static bool IsBlockCorner(DFCoord input)
    {
        return input.x % BlockSize == 0 &&
               input.y % BlockSize == 0;
    }
}
