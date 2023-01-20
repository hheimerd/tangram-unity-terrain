using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Mapzen.VectorData;
using Mapzen.Unity;
using Mapzen.VectorData.Formats;
using UnityEngine.Networking;

namespace Mapzen
{
    public class TerrainRegionMap : MonoBehaviour
    {
        // Version information
        // This allows us to check whether an asset was serialized with a different version than this code.
        // If a serialized field of this class is changed or renamed, currentAssetVersion should be incremented.

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

        public SceneGroupType GroupOptions;

        public GameObjectOptions GameObjectOptions;

        public MapStyle Style;

        // Private fields

        private IO tileIO = new IO();

        private List<TileTask> tasks = new List<TileTask>();

        private int nTasksForArea = 0;

        private int generation = 0;

        private AsyncWorker worker = new AsyncWorker(2);

        private GameObject regionMap;

        private TileCache tileCache = new TileCache(50);

        public void DownloadTilesAsync()
        {
            TileBounds bounds = new TileBounds(Area);

            // Abort currently running tasks and increase generation
            worker.ClearTasks();
            tasks.Clear();
            nTasksForArea = 0;
            generation++;

            var parent = new GameObject(this.RegionName);

            foreach (var tileAddress in bounds.TileAddressRange)
            {
                nTasksForArea++;
            }

            foreach (var tileAddress in bounds.TileAddressRange)
            {
                float offsetX = (tileAddress.x - bounds.min.x);
                float offsetY = (-tileAddress.y + bounds.min.y);

                float scaleRatio = (float) tileAddress.GetSizeMercatorMeters() * UnitsPerMeter;
                Matrix4x4 scale = Matrix4x4.Scale(new Vector3(scaleRatio, scaleRatio, scaleRatio));
                Matrix4x4 translate =
                    Matrix4x4.Translate(new Vector3(offsetX * scaleRatio, 0.0f, offsetY * scaleRatio));
                Matrix4x4 transform = translate * scale;

                var terrainData = new TerrainData();
                terrainData.heightmapResolution = 512;
                terrainData.size = new Vector3(scaleRatio, 400, scaleRatio);

                var terrainGameObject = Terrain.CreateTerrainGameObject(terrainData);
                terrainGameObject.transform.position = new Vector3(offsetX * scaleRatio, 0, offsetY * scaleRatio);
                terrainGameObject.transform.parent = parent.transform;
                
                // var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
                // plane.transform.position = new Vector3(offsetX * scaleRatio, 0, offsetY * scaleRatio);
                // plane.transform.localScale *= scaleRatio / 10;
                // plane.transform.parent = parent.transform;
                
                // var planeScale = plane.transform.localScale;
                // planeScale.z *= -1;
                // plane.transform.localScale = planeScale;

                var wrappedTileAddress = tileAddress.Wrapped();

                var uri = new Uri(string.Format(
                    "https://tile.nextzen.org/tilezen/terrain/v1/512/terrarium/{0}/{1}/{2}.png?api_key={3}",
                    wrappedTileAddress.z,
                    wrappedTileAddress.x,
                    wrappedTileAddress.y,
                    ApiKey));

                StartCoroutine(MakeTextureRequest(uri, texture =>
                {
                    // var renderer = plane.GetComponent<Renderer>();

                    var height = new float[texture.height,texture.width];
                    var pixels = texture.GetPixels();
                    
                    for (int h = 0; h < texture.height; h++)
                    {
                        for (var w = 0; w < texture.width; w++)
                        {
                            var color = pixels[h * texture.width + w];
                            height[h,w] = (color.r * 0.3f + color.g * 0.59f + color.b * .11f) * UnitsPerMeter;
                        }
                    }
                    
                    terrainData.SetHeights(0,0, height);
                    
                    // var material = new Material(renderer.material.shader);
                    // material.mainTexture = texture;
                    // renderer.material = material;
                }));


                // IEnumerable<FeatureCollection> featureCollections = tileCache.Get(tileAddress);
                //
                // if (featureCollections != null)
                // {
                //     var task = new TileTask(Style, tileAddress, transform, generation);
                //
                //     worker.RunAsync(() =>
                //     {
                //         if (generation == task.Generation)
                //         {
                //             task.Start(featureCollections);
                //             tasks.Add(task);
                //         }
                //     });
                // }
                // else
                // {
                //     // Use a local generation variable to be used in IORequestCallback coroutine
                //     int requestGeneration = generation;
                //
                //     var wrappedTileAddress = tileAddress.Wrapped();
                //
                //     var uri = new Uri(string.Format("https://tile.nextzen.org/tilezen/vector/v1/all/{0}/{1}/{2}.mvt?api_key={3}",
                //         wrappedTileAddress.z,
                //         wrappedTileAddress.x,
                //         wrappedTileAddress.y,
                //         ApiKey));
                //
                //     IO.IORequestCallback onTileFetched = (url, response) =>
                //     {
                //         if (requestGeneration != generation)
                //         {
                //             // Another request has been made before the coroutine was triggered
                //             return;
                //         }
                //
                //         if (response.hasError())
                //         {
                //             Debug.Log(url);
                //             Debug.Log("TileIO Error: " + response.error);
                //             return;
                //         }
                //
                //         if (response.data.Length == 0)
                //         {
                //             Debug.Log("Empty Response");
                //             return;
                //         }
                //
                //         var task = new TileTask(Style, tileAddress, transform, generation);
                //
                //         worker.RunAsync(() =>
                //         {
                //             // Skip any tasks that have been generated for a different generation
                //             if (generation == task.Generation)
                //             {
                //                 // var tileData = new GeoJsonTile(address, response);
                //                 var mvtTile = new MvtTile(tileAddress, response.data);
                //                 
                //                 // Save the tile feature collections in the cache for later use
                //                 tileCache.Add(tileAddress, mvtTile.FeatureCollections);
                //
                //                 // task.Start(mvtTile.FeatureCollections);
                //
                //                 // tasks.Add(task);
                //             }
                //         });
                //     };
                //
                //     // Starts the HTTP request
                //     StartCoroutine(tileIO.FetchNetworkData(uri, onTileFetched));
                // }
            }
        }

        IEnumerator MakeTextureRequest(Uri uri, Action<Texture2D> callback)
        {
                UnityWebRequest request = UnityWebRequestTexture.GetTexture(uri);
                request.SetRequestHeader("Origin", AllowedOrigin);
                yield return request.SendWebRequest();
                if (request.isNetworkError || request.isHttpError)
                {
                    Debug.Log(uri);
                    Debug.Log(request.error);
                }
                else
                    callback(((DownloadHandlerTexture) request.downloadHandler).texture);
        }  

        public bool HasPendingTasks()
        {
            return nTasksForArea > 0;
        }

        public bool FinishedRunningTasks()
        {
            // Number of tasks ready for the current generation
            int nTasksReady = 0;

            foreach (var task in tasks)
            {
                if (task.Generation == generation)
                {
                    nTasksReady++;
                }
            }

            return nTasksReady == nTasksForArea;
        }

        public void GenerateSceneGraph()
        {
            if (regionMap != null)
            {
                DestroyImmediate(regionMap);
            }

            // Merge all feature meshes
            List<FeatureMesh> features = new List<FeatureMesh>();
            foreach (var task in tasks)
            {
                if (task.Generation == generation)
                {
                    features.AddRange(task.Data);
                }
            }

            tasks.Clear();
            nTasksForArea = 0;

            regionMap = new GameObject(RegionName);
            var sceneGraph = new SceneGraph(regionMap, GroupOptions, GameObjectOptions, features);

            sceneGraph.Generate();
        }

        public bool IsValid()
        {
            bool hasStyle = Style != null;
            bool hasApiKey = ApiKey.Length > 0;
            return RegionName.Length > 0 && hasStyle && hasApiKey;
        }

        public void LogWarnings()
        {
            if (ApiKey.Length == 0)
            {
                Debug.LogWarning("Make sure to set an API key in the RegionMap");
            }

            if (Style != null && Style.Layers.Count == 0)
            {
                Debug.LogWarning("The current MapStyle has no layers, no output will be produced");
            }
        }

        public void LogErrors()
        {
            if (RegionName.Length == 0)
            {
                Debug.LogError("Make sure to give a region name");
            }

            if (Style == null)
            {
                Debug.LogError("Make sure to set a MapStyle");
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
}
