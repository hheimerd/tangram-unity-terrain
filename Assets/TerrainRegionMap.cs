#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mapzen;
using Mapzen.VectorData;
using Mapzen.VectorData.Formats;
using UnityEngine;
using UnityEngine.Networking;
using Utils;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

public class TerrainRegionMap : MonoBehaviour
{
    private const int HEIGHT_MAP_RESOLUTION = 513;
    private const int DETAILS_RESOLUTION = 128;

    // Version information
    // This allows us to check whether an asset was serialized with a different version than this code.
    // If a serialized field of this class is changed or renamed, currentAssetVersion should be incremented.

    private static Dictionary<string, Texture2D> _textureCache = new();
    private static Dictionary<string, MvtTile> _mvtCache = new();

    private const int currentAssetVersion = 1;

    [SerializeField]
    private int serializedAssetVersion = currentAssetVersion;

    [SerializeField]
    private List<GameObject> trees = new();

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

    private Transform _parrent;
    [SerializeField] private TerrainLayer[] terrainLayers;

    public void DownloadTilesAsync()
    {
        TileBounds bounds = new TileBounds(Area);

        _parrent = new GameObject(RegionName).transform;


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
            Matrix4x4 scale = Matrix4x4.Scale(new Vector3(scaleRatio, scaleRatio, scaleRatio));
            Matrix4x4 translate = Matrix4x4.Translate(new Vector3(offsetX * scaleRatio, 0.0f, offsetY * scaleRatio));
            Matrix4x4 transform = translate * scale;

            var terrainData = new TerrainData();
            terrainData.heightmapResolution = HEIGHT_MAP_RESOLUTION; // power of 2 + 1
            terrainData.SetDetailResolution(DETAILS_RESOLUTION, 16);
            terrainData.size = new Vector3(scaleRatio, ColorHeightConverter.UMax * UnitsPerMeter, scaleRatio);

            var terrainGameObject = Terrain.CreateTerrainGameObject(terrainData);
            var terrain = terrainGameObject.GetComponent<Terrain>();

            terrainGameObject.transform.position = new Vector3(offsetX * scaleRatio, 0, offsetY * scaleRatio);
            terrainGameObject.transform.parent = _parrent;

            var wrappedTileAddress = tileAddress.Wrapped();

            terrains[tileAddress.y - yStartIndex, tileAddress.x - xStartIndex] = terrain;

            terrain.terrainData.terrainLayers = terrainLayers;

            coroutines.Add(StartCoroutine(ApplyTerrainHeight(wrappedTileAddress, terrain.terrainData)));
            // coroutines.Add(StartCoroutine(ApplyTerrainTrees(wrappedTileAddress, terrain, transform)));
        }

        SetTerrainSiblings(terrains);
        StartCoroutine(ConcatTerrainsAfterLoad(coroutines, terrains));
    }

    private IEnumerator ApplyTerrainTrees(TileAddress tileAddress, Terrain terrain, Matrix4x4 transform)
    {
        var uri = new Uri(string.Format("https://tile.nextzen.org/tilezen/vector/v1/all/{0}/{1}/{2}.mvt?api_key={3}",
            tileAddress.z,
            tileAddress.x,
            tileAddress.y,
            ApiKey));

        terrain.terrainData.treePrototypes = trees.Select(prefab => new TreePrototype()
        {
            prefab = prefab
        }).ToArray();
        terrain.terrainData.RefreshPrototypes();

        yield return FetchMvt(uri, tileAddress, tile =>
        {
            TreeInstance treeInstance = new TreeInstance();

            var polygons = (
                from featureCollection in tile.FeatureCollections
                from feature in featureCollection.Features
                where feature.Type == GeometryType.Polygon
                from polygon in feature.CopyGeometry().Polygons
                from points in polygon
                select points.Select(point => new Vector2(point.X, point.Y)).ToArray()
            ).ToArray();

            Vector2 point = new();

            for (int y = 0; y < DETAILS_RESOLUTION; y++)
            {
                var count = 0;

                for (int x = 0; x < DETAILS_RESOLUTION; x++)
                {
                    point.x = x / (float) DETAILS_RESOLUTION;
                    point.y = y / (float) DETAILS_RESOLUTION;

                    if (IsPointOnLine(point, polygons))
                        count++;

                    if (count % 2 == 1)
                    {
                        treeInstance.position = new Vector3(point.x, 0, point.y);
                        treeInstance.prototypeIndex = Random.Range(0, terrain.terrainData.treePrototypes.Length);
                        treeInstance.widthScale = 0.5f;
                        treeInstance.heightScale = 0.5f;
                        terrain.AddTreeInstance(treeInstance);
                    }
                }
            }


            // foreach (var polygon in polygons)
            // {
            //     foreach (var point in polygon)
            //     {
            //         treeInstance.position = new Vector3(point.X, 0, point.Y);
            //         treeInstance.prototypeIndex = Random.Range(0, terrain.terrainData.treePrototypes.Length);
            //         treeInstance.widthScale = 0.5f;
            //         treeInstance.heightScale = 0.5f;
            //         terrain.AddTreeInstance(treeInstance);
            //     }
            // }
        });
    }

    private void ApplyTerrainTextures(TerrainData terrainData)
    {
        // Splatmap data is stored internally as a 3d array of floats, so declare a new empty array ready for your custom splatmap data:
        float[,,] splatmapData =
            new float[terrainData.alphamapWidth, terrainData.alphamapHeight, terrainData.alphamapLayers];

        for (int y = 0; y < terrainData.alphamapHeight; y++)
        {
            for (int x = 0; x < terrainData.alphamapWidth; x++)
            {
                // Normalise x/y coordinates to range 0-1 
                float y_01 = (float) y / (float) terrainData.alphamapHeight;
                float x_01 = (float) x / (float) terrainData.alphamapWidth;

                // Sample the height at this location (note GetHeight expects int coordinates corresponding to locations in the heightmap array)
                float height = terrainData.GetHeight(Mathf.RoundToInt(y_01 * terrainData.heightmapResolution),
                    Mathf.RoundToInt(x_01 * terrainData.heightmapResolution));

                // Calculate the normal of the terrain (note this is in normalised coordinates relative to the overall terrain dimensions)
                Vector3 normal = terrainData.GetInterpolatedNormal(y_01, x_01);

                // Calculate the steepness of the terrain
                float steepness = terrainData.GetSteepness(y_01, x_01);

                // Setup an array to record the mix of texture weights at this point
                float[] splatWeights = new float[terrainData.alphamapLayers];

                // CHANGE THE RULES BELOW TO SET THE WEIGHTS OF EACH TEXTURE ON WHATEVER RULES YOU WANT

                // Texture[0] has constant influence
                splatWeights[0] = 0.5f;

                // Texture[1] is stronger at lower altitudes
                splatWeights[1] = Mathf.Clamp01((terrainData.heightmapResolution - height));

                // Texture[2] stronger on flatter terrain
                // Note "steepness" is unbounded, so we "normalise" it by dividing by the extent of heightmap height and scale factor
                // Subtract result from 1.0 to give greater weighting to flat surfaces
                splatWeights[2] =
                    1.0f - Mathf.Clamp01(steepness * steepness / (terrainData.heightmapResolution / 5.0f));

                // Texture[3] increases with height but only on surfaces facing positive Z axis 
                splatWeights[3] = height * Mathf.Clamp01(normal.z);

                // Sum of all textures weights must add to 1, so calculate normalization factor from sum of weights
                float z = splatWeights.Sum();

                // Loop through each terrain texture
                for (int i = 0; i < terrainData.alphamapLayers; i++)
                {
                    // Normalize so that sum of all texture weights = 1
                    splatWeights[i] /= z;

                    // Assign this point to the splatmap array
                    splatmapData[x, y, i] = splatWeights[i];
                }
            }
        }

        // Finally assign the new splatmap to the terrainData:
        terrainData.SetAlphamaps(0, 0, splatmapData);
    }

    private bool IsPointOnLine(Vector2 point, IEnumerable<Vector2[]> polygons)
    {
        foreach (var polygon in polygons)
        {
            for (int i = 0; i < polygon.Length; i++)
            {
                var currentIndex = i;
                var previousIndex = i == 0 ? polygon.Length - 1 : i - 1;

                var distancePA = Vector2.Distance(point, polygon[currentIndex]);
                var distancePB = Vector2.Distance(point, polygon[previousIndex]);
                var distanceAB = Vector2.Distance(polygon[currentIndex], polygon[previousIndex]);

                if (Math.Abs(distancePA + distanceAB - distanceAB) < 0.001)
                    return true;
            }
        }

        return false;
    }


    private IEnumerator ApplyTerrainHeight(TileAddress tileAddress, TerrainData terrainData)
    {
        var uri = new Uri(string.Format(
            "https://tile.nextzen.org/tilezen/terrain/v1/512/terrarium/{0}/{1}/{2}.png?api_key={3}",
            tileAddress.z,
            tileAddress.x,
            tileAddress.y,
            ApiKey));

        yield return StartCoroutine(FetchTexture(uri,
            texture =>
            {
                TextureToTerrain.ApplyHeightTexture(terrainData, texture);
                ApplyTerrainTextures(terrainData);
            }));
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

    IEnumerator FetchMvt(Uri uri, TileAddress tileAddress, Action<MvtTile> callback)
    {
        if (_mvtCache.ContainsKey(uri.ToString()))
        {
            callback(_mvtCache[uri.ToString()]);
            yield break;
        }

        UnityWebRequest request = UnityWebRequestTexture.GetTexture(uri);
        request.SetRequestHeader("Origin", AllowedOrigin);
        yield return request.SendWebRequest();
        if (request.result is UnityWebRequest.Result.ConnectionError or UnityWebRequest.Result.ProtocolError)
        {
            Debug.Log(uri);
            Debug.Log(request.error);
        }
        else if (request.downloadHandler.data.Length == 0)
        {
            Debug.Log("Empty Response");
        }
        else
        {
            var data = request.downloadHandler.data;
            var mvt = new MvtTile(tileAddress, data);

            _mvtCache.Add(uri.ToString(), mvt);
            callback(mvt);
        }
    }

    IEnumerator FetchTexture(Uri uri, Action<Texture2D> callback)
    {
        if (_textureCache.ContainsKey(uri.ToString()))
        {
            callback(_textureCache[uri.ToString()]);
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

            _textureCache.Add(uri.ToString(), texture);
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
