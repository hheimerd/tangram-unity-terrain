using System;
using System.Collections;
using System.Collections.Generic;
using Mapzen;
using UnityEngine;
using UnityEngine.Networking;
using Utils;

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

        foreach (var tileAddress in bounds.TileAddressRange)
        {
            float offsetX = (tileAddress.x - bounds.min.x);
            float offsetY = (-tileAddress.y + bounds.min.y);

            float scaleRatio = (float) tileAddress.GetSizeMercatorMeters() * UnitsPerMeter;

            var terrainData = new TerrainData();
            terrainData.heightmapResolution = 513; // power of 2 + 1
            terrainData.size = new Vector3(scaleRatio, ColorHeightConverter.UMax * UnitsPerMeter, scaleRatio);

            var terrainGameObject = Terrain.CreateTerrainGameObject(terrainData);
            terrainGameObject.transform.position = new Vector3(offsetX * scaleRatio, 0, offsetY * scaleRatio);
            terrainGameObject.transform.parent = parent.transform;

            var wrappedTileAddress = tileAddress.Wrapped();

            var uri = new Uri(string.Format(
                "https://tile.nextzen.org/tilezen/terrain/v1/512/terrarium/{0}/{1}/{2}.png?api_key={3}",
                wrappedTileAddress.z,
                wrappedTileAddress.x,
                wrappedTileAddress.y,
                ApiKey));
            
            StartCoroutine(MakeTextureRequest(uri, texture =>
            {
                TextureToTerrain.ApplyHeightTexture(terrainData, texture);
            }));
        }
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
