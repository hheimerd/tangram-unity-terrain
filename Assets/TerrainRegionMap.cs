#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mapzen;
using UnityEngine;
using UnityEngine.Networking;
using Utils;
using Object = UnityEngine.Object;

public class TerrainRegionMap : MonoBehaviour
{
    // Version information
    // This allows us to check whether an asset was serialized with a different version than this code.
    // If a serialized field of this class is changed or renamed, currentAssetVersion should be incremented.

    private static Dictionary<string, Texture2D> _cache = new();

    private const int currentAssetVersion = 1;
    [SerializeField] private int serializedAssetVersion = currentAssetVersion;

    // Public fields
    // These are serialized, so renaming them will break asset compatibility.

    public string ApiKey = "";

    public string AllowedOrigin = "";

    public TileArea Area = new TileArea(
        new LngLat(-74.014892578125, 40.70562793820589),
        new LngLat(-74.00390625, 40.713955826286046),
        16);

    public float UnitsPerMeter = 1.0f;

    public string RegionName = "";


    // Private fields

    private GameObject regionMap;


    public void DownloadTilesAsync()
    {
        TileBounds bounds = new TileBounds(Area);

        var parent = new GameObject(RegionName);


        var ranges = bounds.TileAddressRange.ToArray();
        var xSize = ranges[^1].x - ranges[0].x + 1;
        var ySize = ranges[^1].y - ranges[0].y + 1;

        var terrains = new Terrain[ySize, xSize];

        var xStartIndex = ranges[0].x;
        var yStartIndex = ranges[0].y;

        List<Coroutine> coroutines = new();

        foreach (var tileAddress in bounds.TileAddressRange)
        {
            float offsetX = (tileAddress.x - bounds.min.x);
            float offsetY = (-tileAddress.y + bounds.min.y);

            float scaleRatio = (float) tileAddress.GetSizeMercatorMeters() * UnitsPerMeter;

            var terrainData = new TerrainData();
            terrainData.heightmapResolution = 513; // power of 2 + 1
            terrainData.size = new Vector3(scaleRatio, ColorHeightConverter.UMax * UnitsPerMeter, scaleRatio);

            var terrainGameObject = Terrain.CreateTerrainGameObject(terrainData);
            var terrain = terrainGameObject.GetComponent<Terrain>();

            terrainGameObject.transform.position = new Vector3(offsetX * scaleRatio, 0, offsetY * scaleRatio);
            terrainGameObject.transform.parent = parent.transform;

            var wrappedTileAddress = tileAddress.Wrapped();

            var uri = new Uri(string.Format(
                "https://tile.nextzen.org/tilezen/terrain/v1/512/terrarium/{0}/{1}/{2}.png?api_key={3}",
                wrappedTileAddress.z,
                wrappedTileAddress.x,
                wrappedTileAddress.y,
                ApiKey));

            terrains[tileAddress.y - yStartIndex, tileAddress.x - xStartIndex] = terrain;

            coroutines.Add(StartCoroutine(MakeTextureRequest(uri,
                texture => { TextureToTerrain.ApplyHeightTexture(terrainData, texture); })));
        }

        SetTerrainSiblings(terrains);
        StartCoroutine(ConcatTerrainsAfterLoad(coroutines, terrains));
    }

    IEnumerator ConcatTerrainsAfterLoad(IEnumerable<Coroutine> coroutines, Terrain[,] terrains)
    {
        foreach (var coroutine in coroutines)
        {
            yield return coroutine;
        }

        yield return new WaitForSeconds(1);
        ConcatTerrains(terrains);
    }

    private void ConcatTerrains(Terrain[,] terrains)
    {
        terrains.ForEach((terrain, position) =>
        {
            var resolution = terrain.terrainData.heightmapResolution;

            var currentData = terrain.terrainData.GetHeights(0, 0, resolution, resolution);
            var top = terrains.GetElementAt(position.y - 1, position.x)?.terrainData
                ?.GetHeights(0, 0, resolution, resolution);
            var bottom = terrains.GetElementAt(position.y + 1, position.x)?.terrainData
                ?.GetHeights(0, 0, resolution, resolution);
            var left = terrains.GetElementAt(position.y, position.x - 1)?.terrainData
                ?.GetHeights(0, 0, resolution, resolution);
            var right = terrains.GetElementAt(position.y, position.x + 1)?.terrainData
                ?.GetHeights(0, 0, resolution, resolution);

            for (int i = 0; i < resolution; i++)
            {
                if (top != null)
                    currentData[0, i] = top[resolution - 1, i];
                if (left != null)
                    currentData[i, 0] = left[i, resolution - 1];
                if (right != null)
                    currentData[i, resolution - 1] = right[i, 0];
                if (bottom != null)
                    currentData[resolution - 1, i] = bottom[0, i];
            }
            
            terrain.terrainData.SetHeights(0, 0, currentData);
        });
    }

    private void SetTerrainSiblings(Terrain[,] terrains)
    {
        terrains.ForEach((terrain, position) =>
        {
            var top = terrains.GetElementAt(position.y - 1, position.x);
            var bottom = terrains.GetElementAt(position.y + 1, position.x);
            var left = terrains.GetElementAt(position.y, position.x - 1);
            var right = terrains.GetElementAt(position.y, position.x + 1);

            terrain?.SetNeighbors(left, top, right, bottom);
        });
    }

    IEnumerator MakeTextureRequest(Uri uri, Action<Texture2D> callback)
    {
        if (_cache.ContainsKey(uri.ToString()))
        {
            callback(_cache[uri.ToString()]);
            yield break;
        }

        UnityWebRequest request = UnityWebRequestTexture.GetTexture(uri);
        request.SetRequestHeader("Origin", AllowedOrigin);
        yield return request.SendWebRequest();
        if (request.isNetworkError || request.isHttpError)
        {
            Debug.Log(uri);
            Debug.Log(request.error);
        }
        else
        {
            var texture = ((DownloadHandlerTexture) request.downloadHandler).texture;

            _cache.Add(uri.ToString(), texture);
            callback(texture);
        }
    }

    public bool IsValid()
    {
        bool hasApiKey = ApiKey.Length > 0;
        return RegionName.Length > 0 && hasApiKey;
    }

    public void LogWarnings()
    {
        if (ApiKey.Length == 0)
        {
            Debug.LogWarning("Make sure to set an API key in the RegionMap");
        }
    }

    public void LogErrors()
    {
        if (RegionName.Length == 0)
        {
            Debug.LogError("Make sure to give a region name");
        }
    }

    public void OnValidate()
    {
        if (serializedAssetVersion != currentAssetVersion)
        {
            Debug.LogWarningFormat("The RegionMap \"{0}\" was created with a different version of this tool. " +
                                   "Some properties may be missing or have unexpected values.", this.name);
            serializedAssetVersion = currentAssetVersion;
        }
    }
}

public static class Array2DExtensions
{
    public static T? GetElementAt<T>(this T[,] target, int index1, int index2) where T : Object
    {
        var yLength = target.GetLength(0);
        var xLength = target.GetLength(1);

        if (index1 >= yLength || index1 < 0 || index2 >= xLength || index2 < 0)
            return null;

        return target[index1, index2];
    }

    public static void ForEach<T>(this T[,] target, Action<T, (int y, int x)> action) where T : Object
    {
        var yLength = target.GetLength(0);
        var xLength = target.GetLength(1);

        for (int y = 0; y < yLength; y++)
        {
            for (int x = 0; x < xLength; x++)
            {
                action(target[y, x], (y, x));
            }
        }
    }
}
