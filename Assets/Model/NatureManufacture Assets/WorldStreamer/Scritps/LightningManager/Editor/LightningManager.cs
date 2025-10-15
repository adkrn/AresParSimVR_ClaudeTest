using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEditor.SceneManagement;
using System;

namespace WorldStreamer2
{
    /// <summary>
    /// Lightning manager for scene collections.
    /// </summary>
    public class LightningManager : Editor
    {
        //Enviroment
        Material skybox;
        Light sun;
        AmbientMode ambientMode;
        float ambientInstensity = 1;

        Color ambientSkyColor = new Color(0.212f, 0.227f, 0.259f);
        Color ambientEquatorColor = Color.grey;
        Color ambientGroundColor = Color.black;

        DefaultReflectionMode reflectionSource;
        int defaultReflectionResolution = 128;

        string[] resolutionValuesText = new string[]
        {
            "128", "256", "512", "1024"
        };

        int[] resolutionValues = new int[]
        {
            128, 256, 512, 1024
        };

        float reflectionInstensity = 1;
        int reflectionBounces;
        Texture reflectionTexture;
        ReflectionCubemapCompression reflectionCubemapCompression = ReflectionCubemapCompression.Auto;


        //Fog
        bool fog;
        Color fogColor = Color.gray;
        FogMode fogMode = FogMode.ExponentialSquared;
        float fogDensity = 0.01f;


        /// <summary>
        /// The collections collapsed.
        /// </summary>
        bool collectionsCollapsed = true;

        /// <summary>
        /// The list size collections.
        /// </summary>
        int listSizeCollections = 0;

        /// <summary>
        /// The current collections.
        /// </summary>
        List<SceneCollectionManager> currentCollections = new List<SceneCollectionManager>();

        //[MenuItem ("World Streamer/Lightning Manager")]
        //static void Init ()
        //{
        //	// Get existing open window or if none, make a new one:
        //	LightningManager window = EditorWindow.GetWindow <LightningManager> ("Lightning Manager");
        //	window.Show ();

        //}

        SceneCollectionManager sceneCollectionManager;


        /// <summary>
        /// Raises the GUI event.
        /// </summary>
        public void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Light probes", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.Space();

            sceneCollectionManager = (SceneCollectionManager) EditorGUILayout.ObjectField("Scene Collection Manager", sceneCollectionManager, typeof(SceneCollectionManager), false);

            EditorGUILayout.Space();
            if (GUILayout.Button("Split light probes group to tiles"))
            {
                SplitLightProbes();
            }

            EditorGUILayout.Space();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();


            EditorGUILayout.LabelField("Environment", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            skybox = (Material) EditorGUILayout.ObjectField("Skybox Material", skybox, typeof(Material), false);
            sun = (Light) EditorGUILayout.ObjectField("Sun Source", sun, typeof(Light), true);


            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Environment lightning");

            EditorGUI.indentLevel++;
            ambientMode = (AmbientMode) EditorGUILayout.EnumPopup("Source", ambientMode);

            if (skybox != null && ambientMode == AmbientMode.Skybox)
            {
                ambientInstensity = EditorGUILayout.Slider("Intensity Multiplier", ambientInstensity, 0, 8);
            }

            if (ambientMode == AmbientMode.Flat)
            {
                ambientEquatorColor = EditorGUILayout.ColorField("Ambient Color", ambientEquatorColor);
            }

            if (ambientMode == AmbientMode.Trilight)
            {
                ambientSkyColor = EditorGUILayout.ColorField("Sky Color", ambientSkyColor);
                ambientEquatorColor = EditorGUILayout.ColorField("Equator Color", ambientEquatorColor);
                ambientGroundColor = EditorGUILayout.ColorField("Ground Color", ambientGroundColor);
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Environment Reflections");
            EditorGUI.indentLevel++;
            reflectionSource = (DefaultReflectionMode) EditorGUILayout.EnumPopup("Source", reflectionSource);

            if (reflectionSource == DefaultReflectionMode.Skybox)
                defaultReflectionResolution = EditorGUILayout.IntPopup("Resolution", defaultReflectionResolution, resolutionValuesText, resolutionValues);
            else
            {
                reflectionTexture = (Cubemap) EditorGUILayout.ObjectField("Cubemap", reflectionTexture, typeof(Texture), true);
            }


            reflectionCubemapCompression = (ReflectionCubemapCompression) EditorGUILayout.EnumPopup("Compression", reflectionCubemapCompression);

            reflectionInstensity = EditorGUILayout.Slider("Instensity Multiplier", reflectionInstensity, 0, 1);
            reflectionBounces = EditorGUILayout.IntSlider("Bounces", reflectionBounces, 1, 5);

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();


            //Fog parameters
            EditorGUILayout.Space();


            EditorGUILayout.LabelField("Other Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            fog = EditorGUILayout.Toggle("Fog", fog);


            EditorGUI.indentLevel++;
            EditorGUI.BeginDisabledGroup(!fog);
            fogColor = EditorGUILayout.ColorField("Fog color", fogColor);
            fogMode = (FogMode) EditorGUILayout.EnumPopup("Fog Mode", fogMode);

            fogDensity = EditorGUILayout.FloatField("Density", fogDensity);
            EditorGUI.EndDisabledGroup();
            EditorGUI.indentLevel--;


            EditorGUILayout.EndVertical();

            //Setup
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Setting scenes", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.Space();
            if (GUILayout.Button("Setup light settings in current scene"))
            {
                SetupLight();
            }

            collectionsCollapsed = EditorGUILayout.Foldout(collectionsCollapsed, new GUIContent("Scene collections: ", "Drag and drop here all scene collections which scenes you want to add/remove to/from build settings."));
            if (collectionsCollapsed)
            {
                EditorGUI.indentLevel++;
                listSizeCollections = EditorGUILayout.IntField("size", listSizeCollections);
                if (listSizeCollections != currentCollections.Count)
                {
                    while (listSizeCollections > currentCollections.Count)
                    {
                        currentCollections.Add(null);
                    }

                    while (listSizeCollections < currentCollections.Count)
                    {
                        currentCollections.RemoveAt(currentCollections.Count - 1);
                    }
                }

                for (int i = 0; i < currentCollections.Count; i++)
                {
                    currentCollections[i] = (SceneCollectionManager) EditorGUILayout.ObjectField(currentCollections[i], typeof(SceneCollectionManager), true);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();


            if (GUILayout.Button("Setup light settings in scene collections"))
            {
                int sceneId = 1;
                int sceneCount = 0;
                foreach (var sceneCollection in currentCollections)
                {
                    sceneCount += sceneCollection.names.Length;
                }

                foreach (var sceneCollection in currentCollections)
                {
                    foreach (var item in sceneCollection.names)
                    {
                        EditorUtility.DisplayProgressBar("Setting Lightning Settings", "Scene " + sceneId + "/" + sceneCount, sceneId / (float) sceneCount);
                        EditorSceneManager.OpenScene(sceneCollection.path + item);
                        SetupLight();
                        EditorSceneManager.SaveOpenScenes();
                        sceneId++;
                    }
                }

                EditorUtility.ClearProgressBar();
            }

            EditorGUILayout.Space();
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Split light probes into separate tile objects
        /// </summary>
        private void SplitLightProbes()
        {
#if UNITY_2022_1_OR_NEWER && !Unity_2021_3_1 && !Unity_2021_3_2 && !Unity_2021_3_3 && !Unity_2021_3_4 && !Unity_2021_3_5 && !Unity_2021_3_6 && !Unity_2021_3_7 && !Unity_2021_3_8 && !Unity_2021_3_9 && !Unity_2021_3_10 && !Unity_2021_3_11 && !Unity_2021_3_12 && !Unity_2021_3_13 && !Unity_2021_3_14 && !Unity_2021_3_15 && !Unity_2021_3_16 && !Unity_2021_3_17
            LightProbeGroup[] lightProbeGroups = FindObjectsByType<LightProbeGroup>(FindObjectsSortMode.None);
#else
            LightProbeGroup[] lightProbeGroups = FindObjectsOfType<LightProbeGroup>();

#endif

            Dictionary<Vector3Int, List<Vector3>> positionsTiles = new Dictionary<Vector3Int, List<Vector3>>(new Vector3IntArrayComparer());


            int id = 0;
            foreach (var probeGroup in lightProbeGroups)
            {
                EditorUtility.DisplayProgressBar("Spliting light probe groups", "Getting positions", id++ / (float) (lightProbeGroups.Length + 1));

                foreach (var position in probeGroup.probePositions)
                {
                    Vector3 worldPos = probeGroup.transform.TransformPoint(position);


                    Vector3Int posID = GetSplitPositionID(worldPos, sceneCollectionManager);
                    Debug.Log(worldPos + " " + posID);
                    if (positionsTiles.ContainsKey(posID))
                    {
                        positionsTiles[posID].Add(worldPos);
                    }
                    else
                    {
                        positionsTiles.Add(posID, new List<Vector3>() {worldPos});
                    }
                }

                probeGroup.gameObject.SetActive(false);
            }


            EditorUtility.DisplayProgressBar("Spliting light probe groups", "Getting positions", id++ / (float) (lightProbeGroups.Length + 1));

            List<Vector3> positions;

            foreach (var tileKey in positionsTiles.Keys)
            {
                GameObject probeGroupGO = new GameObject(sceneCollectionManager.prefixName + "_LP_" + tileKey.x + "_" + tileKey.y + "_" + tileKey.z);
                LightProbeGroup probeGroup = probeGroupGO.AddComponent<LightProbeGroup>();

                probeGroupGO.transform.position = new Vector3(tileKey.x * sceneCollectionManager.xSize, tileKey.y * sceneCollectionManager.ySize, tileKey.z * sceneCollectionManager.zSize);

                positions = positionsTiles[tileKey];

                for (int i = 0; i < positions.Count; i++)
                {
                    positions[i] = probeGroupGO.transform.InverseTransformPoint(positions[i]);
                }

                probeGroup.probePositions = positions.ToArray();
            }

            EditorUtility.ClearProgressBar();
        }


        /// <summary>
        /// Setups the light parameters.
        /// </summary>
        void SetupLight()
        {
            RenderSettings.skybox = skybox;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientIntensity = ambientInstensity;
            RenderSettings.ambientSkyColor = ambientSkyColor;
            RenderSettings.ambientEquatorColor = ambientEquatorColor;
            RenderSettings.ambientGroundColor = ambientGroundColor;

            RenderSettings.defaultReflectionMode = reflectionSource;
            RenderSettings.defaultReflectionResolution = defaultReflectionResolution;
            RenderSettings.reflectionIntensity = reflectionInstensity;
            RenderSettings.reflectionBounces = reflectionBounces;
#if !UNITY_2021
            RenderSettings.customReflectionTexture = reflectionTexture;
#endif
            LightmapEditorSettings.reflectionCubemapCompression = reflectionCubemapCompression;

            //Fog
            RenderSettings.fog = fog;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogMode = fogMode;
            RenderSettings.fogDensity = fogDensity;
            EditorSceneManager.MarkAllScenesDirty();
        }


        /// <summary>
        /// Gets the split position divided by size.
        /// </summary>
        /// <returns>The split position I.</returns>
        /// <param name="position">Position.</param>
        /// <param name="layer">Layer.</param>
        Vector3Int GetSplitPositionID(Vector3 position, SceneCollectionManager layer)
        {
            layer.xSize = layer.xSize != 0 ? layer.xSize : 100;
            layer.ySize = layer.ySize != 0 ? layer.ySize : 100;
            layer.zSize = layer.zSize != 0 ? layer.zSize : 100;

            int x = (int) (Mathf.FloorToInt(position.x / layer.xSize));

            if (Mathf.Abs((position.x / layer.xSize) - Mathf.RoundToInt(position.x / layer.xSize)) < 0.001f)
            {
                x = (int) Mathf.RoundToInt(position.x / layer.xSize);
            }


            int y = (int) (Mathf.FloorToInt(position.y / layer.ySize));

            if (Mathf.Abs((position.y / layer.ySize) - Mathf.RoundToInt(position.y / layer.ySize)) < 0.001f)
            {
                y = (int) Mathf.RoundToInt(position.y / layer.ySize);
            }

            int z = (int) (Mathf.FloorToInt(position.z / layer.zSize));

            if (Mathf.Abs((position.z / layer.zSize) - Mathf.RoundToInt(position.z / layer.zSize)) < 0.001f)
            {
                z = (int) Mathf.RoundToInt(position.z / layer.zSize);
            }


            return new Vector3Int(x, y, z);
        }
    }
}