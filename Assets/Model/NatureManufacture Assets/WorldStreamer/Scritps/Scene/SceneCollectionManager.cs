using UnityEngine;
using System.Collections;
using System;
using System.IO;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WorldStreamer2
{
    /// <summary>
    /// Contains information about scenes in collection
    /// </summary>
    [Serializable]
    public class SceneCollectionManager : ScriptableObject
    {
        /// <summary>
        /// Is scene collection active and loading
        /// </summary>
        public bool active = true;

        /// <summary>
        /// Loading priority
        /// </summary>
        public int priority = 1;

        [Tooltip("Amount of max grid elements that you want to start loading in one frame.")]
        /// <summary>
        /// The max parallel scene loading.
        /// </summary>
        public int maxParallelSceneLoading = 1;

        //[Header("Ranges")]
        [Tooltip("Distance in grid elements that you want hold loaded.")]
        /// <summary>
        /// The loading range of new tiles.
        /// </summary>
        public Vector3Int loadingRange = new Vector3Int(3, 3, 3);

        [Tooltip("Enables ring streaming.")]
        /// <summary>
        /// The use loading minimum range.
        /// </summary>
        public bool useLoadingRangeMin = false;

        [Tooltip("Area that you want to cutout from loading range.")]
        /// <summary>
        /// The loading minimum range.
        /// </summary>
        public Vector3Int loadingRangeMin = new Vector3Int(2, 2, 2);

        [Tooltip("Enables ccene collection elements to unload when scene loaded.")]
        /// <summary>
        /// Use scene collection to unload when loaded
        /// </summary>
        public bool useUnloadRangeConnect = false;

        [Tooltip("Scene collection elements to unload when scene loaded.")]
        /// <summary>
        /// Scene collection to unload when loaded
        /// </summary>
        public SceneCollectionManager unloadRangeConnect; //sync with other layer

        [HideInInspector] [Tooltip("Scene collection elements to unload when scene loaded.")]
        /// <summary>
        /// Scene collection to unload when loaded
        /// </summary>
        public SceneCollectionManager unloadRangeConnectParent;


        [Tooltip("Distance in grid elements after which you want to unload assets.")]
        /// <summary>
        /// The deloading range of tiles.
        /// </summary>
        public Vector3Int deloadingRange = new Vector3Int(3, 3, 3);


        [Header("Settings")]
        /// <summary>
        /// The strem object name prefix.
        /// </summary>
        public string prefixName = "stream";

        /// <summary>
        /// The scene name prefix.
        /// </summary>
        public string prefixScene = "Scene";

        /// <summary>
        /// The path of scenes.
        /// </summary>
        public string path = "Assets/WorldStreamer/SplitScenes/";

        [Space(5)]
        /// <summary>
        /// The names of the scenes in collection.
        /// </summary>
        public string[] names;

        [Space(5)]
        /// <summary>
        /// The is split by x.
        /// </summary>
        public bool xSplitIs = true;

        /// <summary>
        /// The is split by y.
        /// </summary>
        public bool ySplitIs = false;

        /// <summary>
        /// The is split by z.
        /// </summary>
        public bool zSplitIs = true;

        [Space(5)]
        /// <summary>
        /// The size of the tile in x.
        /// </summary>
        public int xSize = 500;

        /// <summary>
        /// The size of the  tile in y.
        /// </summary>
        public int ySize = 500;

        /// <summary>
        /// The size of the  tile in z.
        /// </summary>
        public int zSize = 500;

        [Space(5)]
        /// <summary>
        /// The x axis limits.
        /// </summary>
        public int xLimitsx = int.MaxValue;

        /// <summary>
        /// The x axis limits.
        /// </summary>
        public int xLimitsy = int.MinValue;


        /// <summary>
        /// The y axis limits.
        /// </summary>
        public int yLimitsx = int.MaxValue;

        /// <summary>
        /// The y axis limits.
        /// </summary>
        public int yLimitsy = int.MinValue;

        /// <summary>
        /// The z axis limits.
        /// </summary>
        public int zLimitsx = int.MaxValue;

        /// <summary>
        /// The z axis limits.
        /// </summary>
        public int zLimitsy = int.MinValue;

        //For in scene use
        /// <summary>
        /// The x axis limits.
        /// </summary>
        [HideInInspector] public int xLimitsScenex = int.MaxValue;

        /// <summary>
        /// The x axis limits.
        /// </summary>
        [HideInInspector] public int xLimitsSceney = int.MinValue;


        /// <summary>
        /// The y axis limits.
        /// </summary>
        [HideInInspector] public int yLimitsScenex = int.MaxValue;

        /// <summary>
        /// The y axis limits.
        /// </summary>
        [HideInInspector] public int yLimitsSceney = int.MinValue;

        /// <summary>
        /// The z axis limits.
        /// </summary>
        [HideInInspector] public int zLimitsScenex = int.MaxValue;

        /// <summary>
        /// The z axis limits.
        /// </summary>
        [HideInInspector] public int zLimitsSceney = int.MinValue;

        [Space(5)]
        /// <summary>
        /// The collapsed for scene collection editor.
        /// </summary>
        [HideInInspector]
        public bool
            collapsed = true;

        /// <summary>
        /// The layer number for scene collection editor.
        /// </summary>
        [HideInInspector] public int
            layerNumber = 0;

        [Space(5)]
        /// <summary>
        /// The color.
        /// </summary>
        public Color color = Color.red;

        public bool showDebug = false;


        /// <summary>
        /// The scenes array.
        /// </summary>
        public Dictionary<Vector3Int, SceneSplit> scenesArray;


        [HideInInspector] public int xLoadingLimity;
        [HideInInspector] public int xLoadingLimitx;
        [HideInInspector] public int xLoadingRange;

        [HideInInspector] public int yLoadingLimity;
        [HideInInspector] public int yLoadingLimitx;
        [HideInInspector] public int yLoadingRange;


        [HideInInspector] public int zLoadingLimity;
        [HideInInspector] public int zLoadingLimitx;
        [HideInInspector] public int zLoadingRange;


        /// <summary>
        /// The x position.
        /// </summary> 
        [HideInInspector] public int xPos = 0;

        /// <summary>
        /// The y position.
        /// </summary>
        [HideInInspector] public int yPos = 0;

        /// <summary>
        /// The z position.
        /// </summary>
        [HideInInspector] public int zPos = 0;


        /// <summary>
        /// The currently scene loading.
        /// </summary>
        [HideInInspector] public int currentlySceneLoading = 0;


        /// <summary>
        /// The loaded scenes.
        /// </summary>
        //[HideInInspector]
        public List<SceneSplit> loadedScenes = new List<SceneSplit>();


#if UNITY_EDITOR

        [MenuItem("Assets/Create/Scene Collection Manager")]
        public static void Create()
        {
            Selection.activeObject = Create("Assets/", "SceneCollectionManager.asset");
        }

        public static SceneCollectionManager Create(string path, string assetName)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            SceneCollectionManager asset = ScriptableObject.CreateInstance<SceneCollectionManager>();


            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            if (path[path.Length - 1] != '/' && path[path.Length - 1] != '\\')
                path += "/";


            string assetPathAndName = AssetDatabase.GenerateUniqueAssetPath(path + assetName);


            AssetDatabase.CreateAsset(asset, assetPathAndName);
            AssetDatabase.SaveAssets();

            EditorUtility.FocusProjectWindow();

            return asset;
        }
#endif

        public void GetSceneCollectionWolrdLimits(out Vector2Int xLimits, out Vector2Int yLimits, out Vector2Int zLimits)
        {
            xLimits = new Vector2Int(xLimitsx * xSize, (xLimitsy + 1) * xSize);
            yLimits = new Vector2Int(yLimitsx * ySize, (yLimitsy + 1) * ySize);
            zLimits = new Vector2Int(zLimitsx * zSize, (zLimitsy + 1) * zSize);
        }

        public void ResetPosition()
        {
            xPos = int.MinValue;
            yPos = int.MinValue;
            zPos = int.MinValue;
        }

        public bool CheckPosition(Vector3 pos)
        {
            int xPosCurrent = (xSize != 0) ? (int) (Mathf.FloorToInt(pos.x / xSize)) : 0;
            int yPosCurrent = (ySize != 0) ? (int) (Mathf.FloorToInt(pos.y / ySize)) : 0;
            int zPosCurrent = (zSize != 0) ? (int) (Mathf.FloorToInt(pos.z / zSize)) : 0;


            if (xPosCurrent != xPos || yPosCurrent != yPos || zPosCurrent != zPos)
            {
                xPos = xPosCurrent;
                yPos = yPosCurrent;
                zPos = zPosCurrent;

                return true;
            }

            return false;
        }

        public Vector3Int GetTilePosition(Vector3 pos)
        {
            int xPosCurrent = (xSize != 0) ? (int) (Mathf.FloorToInt(pos.x / xSize)) : 0;
            int yPosCurrent = (ySize != 0) ? (int) (Mathf.FloorToInt(pos.y / ySize)) : 0;
            int zPosCurrent = (zSize != 0) ? (int) (Mathf.FloorToInt(pos.z / zSize)) : 0;

            return new Vector3Int(xPosCurrent, yPosCurrent, zPosCurrent);
        }

        public void CalculateLoadingLimits(bool looping, bool overideRangeLimit)
        {
            CalculateLoadingLimits(looping, overideRangeLimit, false, Vector2Int.zero, Vector2Int.zero, Vector2Int.zero);
        }

        public void CalculateLoadingLimits(bool looping, bool overideRangeLimit, bool overideScenesLimits, Vector2Int xLimits, Vector2Int yLimits, Vector2Int zLimits)
        {
            loadedScenes.Clear();
            currentlySceneLoading = 0;
            //Application.backgroundLoadingPriority = ThreadPriority.High;
            // Debug.Log(Application.backgroundLoadingPriority);
            xLoadingLimity = xLimitsy;
            xLoadingLimitx = xLimitsx;
            xLoadingRange = xLoadingLimity - xLoadingLimitx + 1;

            yLoadingLimity = yLimitsy;
            yLoadingLimitx = yLimitsx;
            yLoadingRange = yLoadingLimity - yLoadingLimitx + 1;


            zLoadingLimity = zLimitsy;
            zLoadingLimitx = zLimitsx;
            zLoadingRange = zLoadingLimity - zLoadingLimitx + 1;


            if (overideScenesLimits)
            {
                if (xSize != 0)
                {
                    xLoadingLimity = xLimits.y / xSize - 1;
                    xLoadingLimitx = xLimits.x / xSize;
                    xLoadingRange = xLoadingLimity - xLoadingLimitx + 1;
                }


                if (ySize != 0)
                {
                    yLoadingLimity = yLimits.y / ySize - 1;
                    yLoadingLimitx = yLimits.x / ySize;
                    yLoadingRange = yLoadingLimity - yLoadingLimitx + 1;
                }


                if (zSize != 0)
                {
                    zLoadingLimity = zLimits.y / zSize - 1;
                    zLoadingLimitx = zLimits.x / zSize;
                    zLoadingRange = zLoadingLimity - zLoadingLimitx + 1;
                }
            }


            //Debug.Log(xLoadingLimitx + " " + xLoadingLimity + " " + xLoadingRange + " " + deloadingRange.x);
            if (looping && overideRangeLimit)
            {
                if ((xLoadingLimitx != 0 || xLoadingLimity != 0) && deloadingRange.x != 0)
                {
                    //int xLimitChange = Mathf.CeilToInt((Mathf.Ceil((deloadingRange.x * 2) / xLoadingRange)) * 0.5f);
                    int xLimitChange = Mathf.CeilToInt((Mathf.Ceil((deloadingRange.x * 2) / (float) xLoadingRange) - 0.5f) * 0.5f);


                    xLoadingLimitx -= xLimitChange * xLoadingRange;
                    xLoadingLimity += xLimitChange * xLoadingRange;

                    xLoadingRange = xLoadingLimity - xLoadingLimitx + 1;
                }


                if ((yLoadingLimitx != 0 || yLoadingLimity != 0) && deloadingRange.y != 0)
                {
                    int yLimitChange = Mathf.CeilToInt((Mathf.Ceil((deloadingRange.y * 2) / (float) yLoadingRange) - 0.5f) * 0.5f);
                    //int yLimitChange = Mathf.CeilToInt((Mathf.Ceil((deloadingRange.y * 2) / yLoadingRange)) * 0.5f);

                    yLoadingLimitx -= yLimitChange * yLoadingRange;
                    yLoadingLimity += yLimitChange * yLoadingRange;
                    yLoadingRange = yLoadingLimity - yLoadingLimitx + 1;
                }

                if ((zLoadingLimitx != 0 || zLoadingLimity != 0) && deloadingRange.z != 0)
                {
                    int zLimitChange = Mathf.CeilToInt((Mathf.Ceil((deloadingRange.z * 2) / (float) zLoadingRange) - 0.5f) * 0.5f);
                    //int zLimitChange = Mathf.CeilToInt((Mathf.Ceil((deloadingRange.z * 2) / zLoadingRange)) * 0.5f);

                    zLoadingLimitx -= zLimitChange * zLoadingRange;
                    zLoadingLimity += zLimitChange * zLoadingRange;
                    zLoadingRange = zLoadingLimity - zLoadingLimitx + 1;
                }
            }


            xLimitsScenex = xLimitsx;
            xLimitsSceney = xLimitsy;
            yLimitsScenex = yLimitsx;
            yLimitsSceney = yLimitsy;
            zLimitsScenex = zLimitsx;
            zLimitsSceney = zLimitsy;

            //Debug.Log(xLoadingLimitx + " " + xLoadingLimity + " " + xLoadingRange);
        }

        public void PrepareScenesArray(bool overideRangeLimit)
        {
            PrepareScenesArray(overideRangeLimit, false, Vector2Int.zero, Vector2Int.zero, Vector2Int.zero);
        }


        public void PrepareScenesArray(bool overideRangeLimit, bool overideScenesLimits, Vector2Int xLimits, Vector2Int yLimits, Vector2Int zLimits)
        {
            scenesArray = new Dictionary<Vector3Int, SceneSplit>(new Vector3IntArrayComparer());


            foreach (var sceneName in names)
            {
                int posX = 0;
                int posY = 0;
                int posZ = 0;

                Streamer.SceneNameToPos(this, sceneName, out posX, out posY, out posZ);

                SceneSplit sceneSplit = new SceneSplit();
                sceneSplit.posX = posX;
                sceneSplit.posY = posY;
                sceneSplit.posZ = posZ;
                sceneSplit.basePosX = posX;
                sceneSplit.basePosY = posY;
                sceneSplit.basePosZ = posZ;
                sceneSplit.sceneName = sceneName.Replace(".unity", "");
                sceneSplit.sceneCollectionManager = this;
                sceneSplit.loadingFinished = false;

                //Debug.Log(new Vector3Int(posX, posY, posZ));
                scenesArray.Add(new Vector3Int(posX, posY, posZ), sceneSplit);
            }


            int tempxLimity = xLimitsy;
            int tempxLimitx = xLimitsx;
            int tempxRange = tempxLimity - tempxLimitx + 1;

            int tempyLimity = yLimitsy;
            int tempyLimitx = yLimitsx;
            int tempyRange = tempyLimity - tempyLimitx + 1;

            int tempzLimity = zLimitsy;
            int tempzLimitx = zLimitsx;
            int tempzRange = tempzLimity - tempzLimitx + 1;

            if (overideScenesLimits)
            {
                if (xSize != 0)
                {
                    tempxLimity = xLimits.y / xSize - 1;
                    tempxLimitx = xLimits.x / xSize;
                    tempxRange = tempxLimity - tempxLimitx + 1;
                }

                if (ySize != 0)
                {
                    tempyLimity = yLimits.y / ySize - 1;
                    tempyLimitx = yLimits.x / ySize;
                    tempyRange = tempyLimity - tempyLimitx + 1;
                }

                if (zSize != 0)
                {
                    tempzLimity = zLimits.y / zSize - 1;
                    tempzLimitx = zLimits.x / zSize;
                    tempzRange = tempzLimity - tempzLimitx + 1;
                }
            }

            //Debug.Log(name + " " + tempxLimitx + " " + tempxLimity + " " + tempzLimitx + " " + tempzLimity + " " + xLimits.x + " " + xLimits.y);
            //Debug.Log(name + " " + xLoadingLimitx + " " + xLoadingLimity);


            if (overideRangeLimit)
            {
                for (int x = xLoadingLimitx; x <= xLoadingLimity; x++)
                {
                    for (int y = yLoadingLimitx; y <= yLoadingLimity; y++)
                    {
                        for (int z = zLoadingLimitx; z <= zLoadingLimity; z++)
                        {
                            Vector3Int sceneID = new Vector3Int(x, y, z);

                            int xFinal = Streamer.mod((x + Mathf.Abs(tempxLimitx)), tempxRange) + tempxLimitx;
                            int yFinal = Streamer.mod((y + Mathf.Abs(tempyLimitx)), tempyRange) + tempyLimitx;
                            int zFinal = Streamer.mod((z + Mathf.Abs(tempzLimitx)), tempzRange) + tempzLimitx;

                            // Debug.Log(sceneID);
                            if (!scenesArray.ContainsKey(sceneID))
                            {
                                //Debug.Log(x + " " + tempxLimitx + " " + tempxRange);

                                sceneID = new Vector3Int(xFinal, yFinal, zFinal);
                                // Debug.Log(sceneID);

                                if (!scenesArray.ContainsKey(sceneID))
                                {
                                    //Debug.Log("no scene");
                                    continue;
                                }

                                SceneSplit split = scenesArray[sceneID];

                                SceneSplit sceneSplit = new SceneSplit();
                                sceneSplit.posX = x;
                                sceneSplit.posY = y;
                                sceneSplit.posZ = z;
                                sceneSplit.basePosX = split.basePosX;
                                sceneSplit.basePosY = split.basePosY;
                                sceneSplit.basePosZ = split.basePosZ;
                                sceneSplit.sceneName = split.sceneName;
                                sceneSplit.sceneCollectionManager = this;
                                sceneSplit.loadingFinished = false;

                                //Debug.Log(split.sceneName);

                                scenesArray.Add(new Vector3Int(x, y, z), sceneSplit);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Converts scene name into vector int position
        /// </summary>
        /// <param name="sceneName">Scene name.</param>
        public static Vector3Int SceneNameToVectorIntPos(SceneCollectionManager sceneCollectionManager, string sceneName)
        {
            int posX = 0;
            int posY = 0;
            int posZ = 0;

            string[] values = sceneName.Replace(sceneCollectionManager.prefixScene, "").Replace(".unity", "").Split(new char[]
            {
                '_'
            }, System.StringSplitOptions.RemoveEmptyEntries);

            foreach (var item in values)
            {
                if (item[0] == 'x')
                {
                    posX = int.Parse(item.Replace("x", ""));
                }

                if (item[0] == 'y')
                {
                    posY = int.Parse(item.Replace("y", ""));
                }

                if (item[0] == 'z')
                {
                    posZ = int.Parse(item.Replace("z", ""));
                }
            }

            return new Vector3Int(posX, posY, posZ);
        }
    }
}