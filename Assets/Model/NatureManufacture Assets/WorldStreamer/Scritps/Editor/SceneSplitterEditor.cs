using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace WorldStreamer2
{
    /// <summary>
    /// Scene splitter editor.
    /// </summary>
    public class SceneSplitterEditor : EditorWindow
    {
        public int toolbarInt = 0;
        public string[] toolbarStrings = new string[] { "Terrain Manager", "Streaming layers", "Lightning Manager", "Build Settings" };

        /// <summary>
        /// The splits of tiles.
        /// </summary>
        private List<Dictionary<string, GameObject>> splits = new List<Dictionary<string, GameObject>>();

        /// <summary>
        /// The scene splitter settings.
        /// </summary>
        private SceneSplitterSettings sceneSplitterSettings;

        /// <summary>
        /// Warning info
        /// </summary>
        private string warning = "";

        /// <summary>
        /// The collections collapsed.
        /// </summary>
        private bool collectionsCollapsed = true;

        /// <summary>
        /// The colliders collapsed.
        /// </summary>
        private bool collidersCollapsed = true;

        /// <summary>
        /// The list size collections.
        /// </summary>
        private int listSizeColliders = 0;

        /// <summary>
        /// The current collections.
        /// </summary>
        private List<ColliderScene> currentColliders = new List<ColliderScene>();


        private string colliderScenesPath = "ColliderScenes";


        /// <summary>
        /// The layers collapsed.
        /// </summary>
        private bool layersCollapsed = true;

        /// <summary>
        /// The layers to remove.
        /// </summary>
        private List<SceneCollectionManager> layersToRemove = new List<SceneCollectionManager>();

        /// <summary>
        /// The current scene.
        /// </summary>
        private static string currentScene;

        /// <summary>
        /// The generation cancel.
        /// </summary>
        private bool cancel = false;

        /// <summary>
        /// The scroll position.
        /// </summary>
        private Vector2 scrollPos;

        /// <summary>
        /// Terrain Manager Window
        /// </summary>
        internal TerrainManager terrainManager;

        internal LightningManager lightningManager;

        /// <summary>
        ///  Add menu named "Scene splitter" to the Window menu
        /// </summary>
        [MenuItem("Tools/Nature Manufacture/World Streamer/Scene Manager")]
        private static void Init()
        {
            // Get existing open window or if none, make a new one:
            SceneSplitterEditor window = EditorWindow.GetWindow<SceneSplitterEditor>("Scene Manager");
            window.Show();

            currentScene = EditorSceneManager.GetActiveScene().path;
            window.SceneChanged();

            window.terrainManager = ScriptableObject.CreateInstance<TerrainManager>();
            window.lightningManager = ScriptableObject.CreateInstance<LightningManager>();
        }


        /// <summary>
        /// Raises the GUI event.
        /// </summary>
        private void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            if (currentScene != EditorSceneManager.GetActiveScene().path)
            {
                SceneChanged();
            }

            if (sceneSplitterSettings == null)
                CreateSettings();


            GUILayout.Space(10);

            toolbarInt = GUILayout.Toolbar(toolbarInt, toolbarStrings);

            switch (toolbarInt)
            {
                case 1:

                    //GUILayout.Label ("Base Settings - " + currentScene, EditorStyles.boldLabel);

                    GUILayout.Space(10);
                    EditorGUI.BeginChangeCheck();
                    sceneSplitterSettings.scenesPath = EditorGUILayout.TextField("Scene create folder", sceneSplitterSettings.scenesPath);
                    if (EditorGUI.EndChangeCheck())
                    {
                        EditorUtility.SetDirty(sceneSplitterSettings);
                        AssetDatabase.SaveAssets();
                    }


                    SceneSettings();

                    break;
                case 3:
                    BuildSettings();
                    break;
                case 0:
                    if (!terrainManager)
                        terrainManager = ScriptableObject.CreateInstance<TerrainManager>();
                    terrainManager.OnGUI();
                    break;
                case 2:
                    if (!lightningManager)
                        lightningManager = ScriptableObject.CreateInstance<LightningManager>();
                    lightningManager.OnGUI();
                    break;
                default:
                    break;
            }


            GUILayout.Space(10);

            EditorGUILayout.EndScrollView();
            GUILayout.Space(10);
        }

        #region UIMethods

        /// <summary>
        /// UI for scene settings
        /// </summary>
        private void SceneSettings()
        {
            EditorGUILayout.Space();


            GUILayout.Label("Scene layers", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Show Gizmos all layers")))
            {
                for (int layerNum = 0; layerNum < sceneSplitterSettings.sceneCollectionManagers.Count; layerNum++)
                {
                    sceneSplitterSettings.sceneCollectionManagers[layerNum].showDebug = true;
                }
            }

            if (GUILayout.Button(new GUIContent("Hide Gizmos all layers")))
            {
                for (int layerNum = 0; layerNum < sceneSplitterSettings.sceneCollectionManagers.Count; layerNum++)
                {
                    sceneSplitterSettings.sceneCollectionManagers[layerNum].showDebug = false;
                }
            }

            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button(new GUIContent("Add layer", "Creates layer for objects group. Each layer generates separated virtual grid")))
            {
                CreateLayer();
            }

            if (GUILayout.Button(new GUIContent("Add empty layer", "Adds empty space for layer.")))
            {
                AddEmptyLayer();
            }

            layersCollapsed = EditorGUILayout.Foldout(layersCollapsed, "Layers: " + sceneSplitterSettings.sceneCollectionManagers.Count);
            if (layersCollapsed)
            {
                EditorGUI.indentLevel++;
                SceneCollectionManager layer = null;
                for (int layerNum = 0; layerNum < sceneSplitterSettings.sceneCollectionManagers.Count; layerNum++)
                {
                    //if (layer == null)
                    //    continue;

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.Space();

                    layer = sceneSplitterSettings.sceneCollectionManagers[layerNum];
                    if (layer != null)
                    {
                        EditorGUI.BeginChangeCheck();

                        EditorGUILayout.BeginHorizontal();
                        layer.collapsed = EditorGUILayout.Foldout(layer.collapsed, "Layer: " + layer.prefixScene);
                        if (GUILayout.Button(
                                new GUIContent("S",
                                    "Create virtual grid from this layer only. System will check objects prefix, pivot position and it will move objects into this layer and correct grid element. This will happen only if objects have prefix that match layer game object prefix."),
                                GUILayout.Width(25)))
                        {
                            SplitScene(layer);
                        }

                        if (GUILayout.Button(new GUIContent("G", "Generate scenes from virtual grid for this layer only."), GUILayout.Width(25)))
                        {
                            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

                            GenerateSceneCollidersForSplitScene();
                            GenerateScenesMulti(layer, 0, 1);

                            EditorUtility.ClearProgressBar();
                        }

                        if (GUILayout.Button(new GUIContent("C", "Remove virtual grid which is undo for \"S\" for this layer only."), GUILayout.Width(25)))
                        {
                            ClearSplitScene(layer);
                        }

                        if (GUILayout.Button(new GUIContent("X", "Remove layer."), GUILayout.Width(25)))
                        {
                            DeleteLayer(layer);
                            continue;
                        }

                        layer.color = EditorGUILayout.ColorField(new GUIContent("", "Virtual grid gizmo color."), layer.color, GUILayout.Width(60));

                        EditorGUILayout.EndHorizontal();


                        EditorGUI.indentLevel++;
                        // EditorGUI.BeginDisabledGroup(true);
                        sceneSplitterSettings.sceneCollectionManagers[layerNum] = (SceneCollectionManager)EditorGUILayout.ObjectField(layer, typeof(SceneCollectionManager), false);
                        //  EditorGUI.EndDisabledGroup();

                        if (layer.collapsed)
                        {
                            layer.xSplitIs = EditorGUILayout.Toggle(new GUIContent("Split x", "Use X axis in grid."), layer.xSplitIs);
                            if (layer.xSplitIs)
                            {
                                layer.xSize = EditorGUILayout.IntField(new GUIContent("Size X", "Grid element size in X axis."), layer.xSize);
                            }
                            else
                                layer.xSize = 0;

                            layer.ySplitIs = EditorGUILayout.Toggle(new GUIContent("Split y", "Use Y axis in grid."), layer.ySplitIs);
                            if (layer.ySplitIs)
                            {
                                layer.ySize = EditorGUILayout.IntField(new GUIContent("Size Y", "Grid element size in Y axis."), layer.ySize);
                            }
                            else
                                layer.ySize = 0;

                            layer.zSplitIs = EditorGUILayout.Toggle(new GUIContent("Split z", "Use Z axis in grid."), layer.zSplitIs);
                            if (layer.zSplitIs)
                            {
                                layer.zSize = EditorGUILayout.IntField(new GUIContent("Size Z", "Grid element size in Z axis."), layer.zSize);
                            }
                            else
                                layer.zSize = 0;

                            layer.prefixName = EditorGUILayout.DelayedTextField(
                                new GUIContent("GameObject Prefix",
                                    "First word in your object name, which means membership to this layer. Example:  StreamedT_Terrain where \"StreamedT \" is prefix, Terrain is object name. System will search for objects that have \"StreamedT\" at the beginning of the name. If you left this value as empty, system will  put all objects in the scene into this layer."),
                                layer.prefixName);

                            string newName = EditorGUILayout.DelayedTextField(new GUIContent("Scene Prefix", "Your future scenes and scene collection prefab names."), layer.prefixScene);
                            if (layer.prefixScene != newName && !string.IsNullOrEmpty(newName))
                            {
                                string path = AssetDatabase.GetAssetPath(layer);
                                string newPath = Path.GetDirectoryName(path);

                                string assetPathAndName = AssetDatabase.GenerateUniqueAssetPath(newPath + "/" + newName + ".asset");

                                AssetDatabase.RenameAsset(path, Path.GetFileName(assetPathAndName));


                                AssetDatabase.SaveAssets();
                                AssetDatabase.Refresh();

                                layer.prefixScene = newName;
                            }

                            layer.showDebug = EditorGUILayout.Toggle("Show debug grid", layer.showDebug);


                            layer.layerNumber = layerNum;

                            GUILayout.Space(10);
                        }

                        if (EditorGUI.EndChangeCheck())
                        {
                            if (sceneSplitterSettings != null)
                                EditorUtility.SetDirty(sceneSplitterSettings);
                            EditorUtility.SetDirty(layer);
                        }
                    }
                    else
                    {
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.Foldout(true, "Layer: Empty");
                        if (GUILayout.Button(new GUIContent("X", "Remove layer."), GUILayout.Width(25)))
                        {
                            DeleteLayer(layer);
                            continue;
                        }

                        EditorGUILayout.EndHorizontal();
                        sceneSplitterSettings.sceneCollectionManagers[layerNum] =
                            (SceneCollectionManager)EditorGUILayout.ObjectField(sceneSplitterSettings.sceneCollectionManagers[layerNum], typeof(SceneCollectionManager), false);


                        if (EditorGUI.EndChangeCheck())
                        {
                            //EditorUtility.SetDirty(sceneSplitterSettings.sceneCollectionManagers[layerNum]);
                            EditorUtility.SetDirty(sceneSplitterSettings);
                            AssetDatabase.SaveAssets();
                        }
                    }

                    EditorGUI.indentLevel--;

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space();
                }

                EditorGUI.indentLevel--;
            }

            foreach (var item in layersToRemove)
            {
                sceneSplitterSettings.sceneCollectionManagers.Remove(item);
                //AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(item));
                // AssetDatabase.SaveAssets();
            }

            layersToRemove.Clear();
            if (sceneSplitterSettings == null)
                CreateSettings();

            EditorGUILayout.Space();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();

            GUILayout.Label("Splitting tiles", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.Space();

            if (GUILayout.Button(new GUIContent("Split All Scenes Into Virtual Grid",
                    "Create virtual grids from all layers. System will check objects prefix, pivot position and it will move objects into correct layer and grid element.")))
            {
                foreach (var layer in sceneSplitterSettings.sceneCollectionManagers)
                {
                    SplitScene(layer);
                }

                currentColliders.Clear();
#if UNITY_2022_1_OR_NEWER && !Unity_2021_3_1 && !Unity_2021_3_2 && !Unity_2021_3_3 && !Unity_2021_3_4 && !Unity_2021_3_5 && !Unity_2021_3_6 && !Unity_2021_3_7 && !Unity_2021_3_8 && !Unity_2021_3_9 && !Unity_2021_3_10 && !Unity_2021_3_11 && !Unity_2021_3_12 && !Unity_2021_3_13 && !Unity_2021_3_14 && !Unity_2021_3_15 && !Unity_2021_3_16 && !Unity_2021_3_17
                currentColliders.AddRange(FindObjectsByType(typeof(ColliderScene), FindObjectsSortMode.None) as ColliderScene[] ?? Array.Empty<ColliderScene>());
#else
                currentColliders.AddRange(FindObjectsOfType(typeof(ColliderScene)) as ColliderScene[] ?? new ColliderScene[0]);
#endif
                listSizeColliders = currentColliders.Count;
            }

            if (GUILayout.Button("Clear Scene Split"))
            {
                foreach (var layer in sceneSplitterSettings.sceneCollectionManagers)
                {
                    ClearSplitScene(layer);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();

            GUILayout.Label("Scene Generation", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.Space();

            if (GUILayout.Button(
                    new GUIContent("Generate All Scenes From Virtual Grid", "System will generate scenes and \"Collider_Stream\" prefabs for all objects at your scene that contain \"ColliderScene\" script.")))
            {
                double time = EditorApplication.timeSinceStartup;
                EditorUtility.DisplayProgressBar("Creating Scenes", "Preparing scene", 0);
                int currentLayerID = 0;
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

                for (int i = 0; i < sceneSplitterSettings.sceneCollectionManagers.Count; i++)
                {
                    SceneCollectionManager layer = sceneSplitterSettings.sceneCollectionManagers[i];

                    if (cancel)
                        break;
                    if (EditorUtility.DisplayCancelableProgressBar("Preparing Scenes " + (currentLayerID + 1) + '/' + sceneSplitterSettings.sceneCollectionManagers.Count, "Preparing scene " + layer.prefixScene,
                            (currentLayerID / (float)sceneSplitterSettings.sceneCollectionManagers.Count)))
                    {
                        cancel = true;
                        break;
                    }

                    GenerateSceneCollidersForSplitScene();
                    GenerateScenesMulti(layer, currentLayerID, sceneSplitterSettings.sceneCollectionManagers.Count);

                    currentLayerID++;
                }

                if (cancel)
                {
                    cancel = false;
                }

                EditorUtility.ClearProgressBar();
                currentColliders.Clear();
#if UNITY_2022_1_OR_NEWER && !Unity_2021_3_1 && !Unity_2021_3_2 && !Unity_2021_3_3 && !Unity_2021_3_4 && !Unity_2021_3_5 && !Unity_2021_3_6 && !Unity_2021_3_7 && !Unity_2021_3_8 && !Unity_2021_3_9 && !Unity_2021_3_10 && !Unity_2021_3_11 && !Unity_2021_3_12 && !Unity_2021_3_13 && !Unity_2021_3_14 && !Unity_2021_3_15 && !Unity_2021_3_16 && !Unity_2021_3_17
                currentColliders.AddRange(FindObjectsByType(typeof(ColliderScene), FindObjectsSortMode.None) as ColliderScene[] ?? Array.Empty<ColliderScene>());
#else
                currentColliders.AddRange(FindObjectsOfType(typeof(ColliderScene)) as ColliderScene[] ?? new ColliderScene[0]);
#endif
                listSizeColliders = currentColliders.Count;

                Debug.Log("Scenes generated in: " + (EditorApplication.timeSinceStartup - time));
            }

            GUILayout.Space(10);

            if (GUILayout.Button(new GUIContent("Generates Scenes from colliders only", "System will generate scenes only from all objects that contains \"Collider_Stream\" script.")))
            {
                GenerateSceneColliders();
                currentColliders.Clear();
#if UNITY_2022_1_OR_NEWER && !Unity_2021_3_1 && !Unity_2021_3_2 && !Unity_2021_3_3 && !Unity_2021_3_4 && !Unity_2021_3_5 && !Unity_2021_3_6 && !Unity_2021_3_7 && !Unity_2021_3_8 && !Unity_2021_3_9 && !Unity_2021_3_10 && !Unity_2021_3_11 && !Unity_2021_3_12 && !Unity_2021_3_13 && !Unity_2021_3_14 && !Unity_2021_3_15 && !Unity_2021_3_16 && !Unity_2021_3_17
                currentColliders.AddRange(FindObjectsByType(typeof(ColliderScene), FindObjectsSortMode.None) as ColliderScene[] ?? Array.Empty<ColliderScene>());
#else
                currentColliders.AddRange(FindObjectsOfType(typeof(ColliderScene)) as ColliderScene[] ?? new ColliderScene[0]);
#endif
                listSizeColliders = currentColliders.Count;
            }

            if (!string.IsNullOrEmpty(warning))
            {
                GUILayout.Space(20);
                var TextStyle = new GUIStyle();
                TextStyle.normal.textColor = Color.red;
                TextStyle.alignment = TextAnchor.MiddleCenter;
                TextStyle.fontStyle = FontStyle.Bold;
                GUILayout.Label(warning, TextStyle);
            }

            EditorGUILayout.Space();
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// UI for build settings
        /// </summary>
        private void BuildSettings()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.Space();


            collectionsCollapsed = EditorGUILayout.Foldout(collectionsCollapsed,
                new GUIContent("Scene collections: ", "Drag and drop here all scene collections which scenes you want to add/remove to/from build settings."));
            if (collectionsCollapsed)
            {
                EditorGUI.indentLevel++;

                for (int i = 0; i < sceneSplitterSettings.sceneCollectionManagers.Count; i++)
                {
                    EditorGUILayout.LabelField(sceneSplitterSettings.sceneCollectionManagers[i].name);
                }

                EditorGUI.indentLevel--;
            }

            GUILayout.Space(10);

            collidersCollapsed = EditorGUILayout.Foldout(collidersCollapsed,
                new GUIContent("Collider Streamers: ", "Drag and drop here all collider streamer prefabs with scenes that you want to add/remove to/from build settings."));
            if (collidersCollapsed)
            {
                EditorGUI.indentLevel++;
                listSizeColliders = EditorGUILayout.IntField("size", listSizeColliders);
                if (listSizeColliders != currentColliders.Count)
                {
                    while (listSizeColliders > currentColliders.Count)
                    {
                        currentColliders.Add(null);
                    }

                    while (listSizeColliders < currentColliders.Count)
                    {
                        currentColliders.RemoveAt(currentColliders.Count - 1);
                    }
                }

                for (int i = 0; i < currentColliders.Count; i++)
                {
                    currentColliders[i] = (ColliderScene)EditorGUILayout.ObjectField(currentColliders[i], typeof(ColliderScene), true);
                }

                EditorGUI.indentLevel--;
            }

            GUILayout.Space(10);

            if (GUILayout.Button(new GUIContent("Add scenes to build settings", "System will add all scenes from chosen scene collections in to build settings.")))
            {
                AddScenesToBuild();


                string scenesPath = this.sceneSplitterSettings.GetScenesPath();

                if (!Directory.Exists(scenesPath))
                {
                    Directory.CreateDirectory(scenesPath);
                    if (!Directory.Exists(scenesPath))
                    {
                        warning = "Scene colliders path doesn't exist - " + scenesPath;

                        return;
                    }
                }

                if (!Directory.Exists(scenesPath + colliderScenesPath + "/"))
                {
                    Directory.CreateDirectory(scenesPath + colliderScenesPath + "/");
                    if (!Directory.Exists(scenesPath + colliderScenesPath + "/"))
                    {
                        warning = "Scene colliders path doesn't exist - " + scenesPath + colliderScenesPath + "/";

                        return;
                    }
                }


                foreach (var item in currentColliders)
                {
                    AddScenesToBuildString(scenesPath + colliderScenesPath + "/" + item.name + ".unity");
                }
            }

            if (GUILayout.Button(new GUIContent("Remove scenes from build settings", "System removes all scenes from build settings from chosen scene collections.")))
            {
                RemoveScenesFromBuild();
                string scenesPath = this.sceneSplitterSettings.GetScenesPath();
                foreach (var item in currentColliders)
                {
                    RemoveScenesString(scenesPath + colliderScenesPath + "/" + item.name + ".unity");
                }
            }

            if (GUILayout.Button(new GUIContent("Remove all scenes from build settings", "System removes all scenes from build settings from chosen scene collections.")))
            {
                EditorBuildSettings.scenes = new EditorBuildSettingsScene[0];
            }

            EditorGUILayout.Space();
            EditorGUILayout.EndVertical();
        }

        #endregion

        /// <summary>
        /// Generates the scene colliders.
        /// </summary>
        private void GenerateSceneColliders()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("Scene needs to be saved before generating colliders.");
                return;
            }

            string scenesPath = this.sceneSplitterSettings.GetScenesPath();
            if (!Directory.Exists(scenesPath))
            {
                Directory.CreateDirectory(scenesPath);
            }

            scenesPath += colliderScenesPath + "/";
            if (!Directory.Exists(scenesPath))
            {
                Directory.CreateDirectory(scenesPath);
            }


#if UNITY_2022_1_OR_NEWER && !Unity_2021_3_1 && !Unity_2021_3_2 && !Unity_2021_3_3 && !Unity_2021_3_4 && !Unity_2021_3_5 && !Unity_2021_3_6 && !Unity_2021_3_7 && !Unity_2021_3_8 && !Unity_2021_3_9 && !Unity_2021_3_10 && !Unity_2021_3_11 && !Unity_2021_3_12 && !Unity_2021_3_13 && !Unity_2021_3_14 && !Unity_2021_3_15 && !Unity_2021_3_16 && !Unity_2021_3_17
            ColliderScene[] colliderScenes = GameObject.FindObjectsByType<ColliderScene>(FindObjectsSortMode.None);
#else
            ColliderScene[] colliderScenes = GameObject.FindObjectsOfType<ColliderScene>();
#endif
            foreach (var colliderScene in colliderScenes)
            {
                Collider collider = colliderScene.gameObject.GetComponent<Collider>();
                GameObject go = new GameObject(colliderScene.name + "_stream");
                UnityEditorInternal.ComponentUtility.CopyComponent(collider);
                UnityEditorInternal.ComponentUtility.PasteComponentAsNew(go);
                UnityEditorInternal.ComponentUtility.CopyComponent(colliderScene.transform);
                UnityEditorInternal.ComponentUtility.PasteComponentValues(go.transform);
                string sceneName = scenesPath + colliderScene.name + ".unity";
                ColliderStreamer colliderStreamer = go.AddComponent<ColliderStreamer>();
                colliderStreamer.sceneName = colliderScene.name;
                colliderStreamer.scenePath = sceneName;
                colliderScene.sceneName = colliderScene.name;

                if (!Directory.Exists(scenesPath + "Prefabs/"))
                {
                    Directory.CreateDirectory(scenesPath + "Prefabs/");
                }

                Object prefab = PrefabUtility.SaveAsPrefabAsset(go, scenesPath + "Prefabs/" + go.name + ".prefab");

                //Object prefab = PrefabUtility.CreateEmptyPrefab (scenesPath + "Prefabs/" + go.name + ".prefab");
                //PrefabUtility.ReplacePrefab (go, prefab, ReplacePrefabOptions.Default);
                //PrefabUtility.DisconnectPrefabInstance (go);

                //GameObject.DestroyImmediate(go);

                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                DestroyImmediate(collider);
                colliderScene.transform.SetParent(null);
                SceneManager.MoveGameObjectToScene(colliderScene.gameObject, scene);
                EditorSceneManager.SaveScene(scene, sceneName);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Resources.UnloadUnusedAssets();
            EditorSceneManager.OpenScene(currentScene, OpenSceneMode.Single);
        }

        /// <summary>
        /// Generates the scene colliders for split scene.
        /// </summary>
        private void GenerateSceneCollidersForSplitScene()
        {
            string scenesPath = this.sceneSplitterSettings.GetScenesPath();
            if (!Directory.Exists(scenesPath))
            {
                warning = "Scene create folder doesn't exist.";
                return;
            }

            scenesPath += colliderScenesPath + "/";
            if (!Directory.Exists(scenesPath))
            {
                Directory.CreateDirectory(scenesPath);
            }

#if UNITY_2022_1_OR_NEWER && !Unity_2021_3_1 && !Unity_2021_3_2 && !Unity_2021_3_3 && !Unity_2021_3_4 && !Unity_2021_3_5 && !Unity_2021_3_6 && !Unity_2021_3_7 && !Unity_2021_3_8 && !Unity_2021_3_9 && !Unity_2021_3_10 && !Unity_2021_3_11 && !Unity_2021_3_12 && !Unity_2021_3_13 && !Unity_2021_3_14 && !Unity_2021_3_15 && !Unity_2021_3_16 && !Unity_2021_3_17
            ColliderScene[] colliderScenes = GameObject.FindObjectsByType<ColliderScene>(FindObjectsSortMode.None);
#else
            ColliderScene[] colliderScenes = GameObject.FindObjectsOfType<ColliderScene>();
#endif
            foreach (var colliderScene in colliderScenes)
            {
                Collider collider = colliderScene.gameObject.GetComponent<Collider>();
                GameObject go = new GameObject(colliderScene.name + "_stream");
                UnityEditorInternal.ComponentUtility.CopyComponent(collider);
                UnityEditorInternal.ComponentUtility.PasteComponentAsNew(go);
                UnityEditorInternal.ComponentUtility.CopyComponent(colliderScene.transform);
                UnityEditorInternal.ComponentUtility.PasteComponentValues(go.transform);
                string sceneName = scenesPath + colliderScene.name + ".unity";
                ColliderStreamer colliderStreamer = go.AddComponent<ColliderStreamer>();
                colliderStreamer.sceneName = colliderScene.name;
                colliderStreamer.scenePath = sceneName;
                colliderScene.sceneName = colliderScene.name;

                if (!Directory.Exists(scenesPath + "Prefabs/"))
                {
                    Directory.CreateDirectory(scenesPath + "Prefabs/");
                    return;
                }

                Object prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, scenesPath + "Prefabs/" + go.name + ".prefab", InteractionMode.AutomatedAction);

                // Object prefab = PrefabUtility.CreateEmptyPrefab (scenesPath + "Prefabs/" + go.name + ".prefab");
                //PrefabUtility.ReplacePrefab (go, prefab, ReplacePrefabOptions.Default);

                //PrefabUtility.DisconnectPrefabInstance (go);
                //GameObject.DestroyImmediate (go);
                //Resources.UnloadAsset (prefab);

                go.transform.parent = colliderScene.transform.parent;
                go.transform.position = colliderScene.transform.position;

                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                DestroyImmediate(collider);

                colliderScene.gameObject.transform.parent = null;

                SceneManager.MoveGameObjectToScene(colliderScene.gameObject, scene);
                EditorSceneManager.SaveScene(scene, sceneName);
                EditorSceneManager.CloseScene(scene, true);
            }
            //AssetDatabase.SaveAssets ();
            //AssetDatabase.Refresh ();
        }

        /// <summary>
        /// Scenes the changed.
        /// </summary>
        private void SceneChanged()
        {
            currentScene = EditorSceneManager.GetActiveScene().path;

#if UNITY_2022_1_OR_NEWER && !Unity_2021_3_1 && !Unity_2021_3_2 && !Unity_2021_3_3 && !Unity_2021_3_4 && !Unity_2021_3_5 && !Unity_2021_3_6 && !Unity_2021_3_7 && !Unity_2021_3_8 && !Unity_2021_3_9 && !Unity_2021_3_10 && !Unity_2021_3_11 && !Unity_2021_3_12 && !Unity_2021_3_13 && !Unity_2021_3_14 && !Unity_2021_3_15 && !Unity_2021_3_16 && !Unity_2021_3_17
            sceneSplitterSettings = FindAnyObjectByType(typeof(SceneSplitterSettings)) as SceneSplitterSettings;
#else
            sceneSplitterSettings = FindObjectOfType(typeof(SceneSplitterSettings)) as SceneSplitterSettings;
#endif

            if (sceneSplitterSettings == null)
                CreateSettings();


            if (sceneSplitterSettings.sceneCollectionManagers.Count > 0)
            {
                splits = new List<Dictionary<string, GameObject>>();
                foreach (var item in sceneSplitterSettings.sceneCollectionManagers)
                {
                    splits.Add(new Dictionary<string, GameObject>());
                }
            }

            currentColliders.Clear();

#if UNITY_2022_1_OR_NEWER && !Unity_2021_3_1 && !Unity_2021_3_2 && !Unity_2021_3_3 && !Unity_2021_3_4 && !Unity_2021_3_5 && !Unity_2021_3_6 && !Unity_2021_3_7 && !Unity_2021_3_8 && !Unity_2021_3_9 && !Unity_2021_3_10 && !Unity_2021_3_11 && !Unity_2021_3_12 && !Unity_2021_3_13 && !Unity_2021_3_14 && !Unity_2021_3_15 && !Unity_2021_3_16 && !Unity_2021_3_17
            currentColliders.AddRange(FindObjectsByType(typeof(ColliderScene), FindObjectsSortMode.None) as ColliderScene[] ?? Array.Empty<ColliderScene>());
#else
            currentColliders.AddRange(FindObjectsOfType(typeof(ColliderScene)) as ColliderScene[] ?? new ColliderScene[0]);
#endif

            listSizeColliders = currentColliders.Count;
        }

        /// <summary>
        /// Creates the settings gameObject.
        /// </summary>
        private void CreateSettings()
        {
#if UNITY_2022_1_OR_NEWER && !Unity_2021_3_1 && !Unity_2021_3_2 && !Unity_2021_3_3 && !Unity_2021_3_4 && !Unity_2021_3_5 && !Unity_2021_3_6 && !Unity_2021_3_7 && !Unity_2021_3_8 && !Unity_2021_3_9 && !Unity_2021_3_10 && !Unity_2021_3_11 && !Unity_2021_3_12 && !Unity_2021_3_13 && !Unity_2021_3_14 && !Unity_2021_3_15 && !Unity_2021_3_16 && !Unity_2021_3_17
            sceneSplitterSettings = FindAnyObjectByType(typeof(SceneSplitterSettings)) as SceneSplitterSettings;
#else
            sceneSplitterSettings = FindObjectOfType(typeof(SceneSplitterSettings)) as SceneSplitterSettings;
#endif

            if (sceneSplitterSettings == null)
            {
                GameObject gameObject = new GameObject("_SceneSplitterSettingsNew");
                sceneSplitterSettings = gameObject.AddComponent<SceneSplitterSettings>();
                sceneSplitterSettings.scenesPath += EditorSceneManager.GetActiveScene().name;
                gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInBuild;
                gameObject.tag = "EditorOnly";
                Debug.Log("Creating scene settings for world streamer");

                Undo.RegisterCreatedObjectUndo(gameObject, "Create object");
            }
        }

        /// <summary>
        /// Deletes the layer.
        /// </summary>
        /// <param name="layer">Layer.</param>
        private void DeleteLayer(SceneCollectionManager layer)
        {
            layersToRemove.Add(layer);
            splits.RemoveAt(0);
        }

        /// <summary>
        /// Creates the layer.
        /// </summary>
        private void CreateLayer()
        {
            SceneCollectionManager newSceneCollection = SceneCollectionManager.Create(sceneSplitterSettings.GetScenesPath(), "SceneCollectionManager.asset");
            if (newSceneCollection != null)
            {
                newSceneCollection.color = new Color(Random.value, Random.value, Random.value, 255);
                newSceneCollection.prefixScene += " prefix_" + sceneSplitterSettings.sceneCollectionManagers.Count;
                sceneSplitterSettings.sceneCollectionManagers.Add(newSceneCollection);
                splits.Add(new Dictionary<string, GameObject>());
            }


            EditorUtility.SetDirty(sceneSplitterSettings);
        }

        /// <summary>
        /// Creates the layer.
        /// </summary>
        private void AddEmptyLayer()
        {
            sceneSplitterSettings.sceneCollectionManagers.Add(null);
            splits.Add(new Dictionary<string, GameObject>());


            EditorUtility.SetDirty(sceneSplitterSettings);
        }

        /// <summary>
        /// Adds the scenes to build.
        /// </summary>
        private void AddScenesToBuild()
        {
            warning = "";
            List<EditorBuildSettingsScene> scenesList = new List<EditorBuildSettingsScene>();
            scenesList.AddRange(EditorBuildSettings.scenes);
            foreach (var currentCollection in sceneSplitterSettings.sceneCollectionManagers)
            {
                if (!Directory.Exists(currentCollection.path))
                {
                    warning = "Scene collection path doesn't exist - " + currentCollection.name;

                    return;
                }


                List<string> scenesToAdd = new List<string>();
                scenesToAdd.AddRange(currentCollection.names);

                foreach (var item in scenesList)
                {
                    if (scenesToAdd.Contains(item.path.Replace(currentCollection.path, "")))
                    {
                        scenesToAdd.Remove(item.path.Replace(currentCollection.path, ""));
                    }
                }

                foreach (var item in scenesToAdd)
                {
                    scenesList.Add(new EditorBuildSettingsScene(currentCollection.path + item, true));
                }
            }

            EditorBuildSettings.scenes = scenesList.ToArray();
        }

        /// <summary>
        /// Adds the scenes to build.
        /// </summary>
        /// <param name="scenePath">Scene path.</param>
        private void AddScenesToBuildString(string scenePath)
        {
            List<EditorBuildSettingsScene> scenesList = new List<EditorBuildSettingsScene>();
            scenesList.AddRange(EditorBuildSettings.scenes);

            List<string> scenesToAdd = new List<string>();
            scenesToAdd.Add(scenePath);

            foreach (var item in scenesList)
            {
                if (scenesToAdd.Contains(item.path))
                {
                    scenesToAdd.Remove(item.path);
                }
            }

            foreach (var item in scenesToAdd)
            {
                scenesList.Add(new EditorBuildSettingsScene(item, true));
            }

            EditorBuildSettings.scenes = scenesList.ToArray();
        }

        private void RemoveScenesString(string scenePath)
        {
            warning = "";
            List<EditorBuildSettingsScene> scenesList = new List<EditorBuildSettingsScene>();
            scenesList.AddRange(EditorBuildSettings.scenes);


            List<string> scenesToAdd = new List<string>();
            scenesToAdd.Add(scenePath);

            List<EditorBuildSettingsScene> newScenesList = new List<EditorBuildSettingsScene>();
            foreach (var item in scenesList)
            {
                if (scenesToAdd.Contains(item.path))
                {
                    newScenesList.Add(item);
                }
            }

            foreach (var removeScene in newScenesList)
            {
                scenesList.Remove(removeScene);
            }


            EditorBuildSettings.scenes = scenesList.ToArray();
        }

        /// <summary>
        /// Removes the scenes from build.
        /// </summary>
        private void RemoveScenesFromBuild()
        {
            warning = "";
            List<EditorBuildSettingsScene> scenesList = new List<EditorBuildSettingsScene>();
            scenesList.AddRange(EditorBuildSettings.scenes);


            foreach (var currentCollection in sceneSplitterSettings.sceneCollectionManagers)
            {
                List<string> scenesToAdd = new List<string>();
                scenesToAdd.AddRange(currentCollection.names);

                List<EditorBuildSettingsScene> newScenesList = new List<EditorBuildSettingsScene>();
                foreach (var item in scenesList)
                {
                    if (scenesToAdd.Contains(item.path.Replace(currentCollection.path, "")))
                    {
                        newScenesList.Add(item);
                    }
                }

                foreach (var removeScene in newScenesList)
                {
                    scenesList.Remove(removeScene);
                }
            }

            EditorBuildSettings.scenes = scenesList.ToArray();
        }

        /// <summary>
        /// Generates the random scene objects.
        /// </summary>
        private void GenerateRandomSceneObjects(SceneCollectionManager layer)
        {
            warning = "";
            for (int i = 0; i < 100; i++)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.position = Random.insideUnitSphere * 100;
                cube.name = layer.prefixName + "_" + i;
            }
        }

        /// <summary>
        /// Gets the ID of tile by position.
        /// </summary>
        /// <returns>The ID of tile.</returns>
        /// <param name="position">Position of tile.</param>
        /// <param name="position">Position of tile.</param>
        /// <param name="layer">Layer.</param>
        private string GetID(Vector3 position, SceneCollectionManager layer)
        {
            int xId = (int)(Mathf.FloorToInt(position.x / layer.xSize));

            if (Mathf.Abs((position.x / layer.xSize) - Mathf.RoundToInt(position.x / layer.xSize)) < 0.001f)
            {
                xId = (int)Mathf.RoundToInt(position.x / layer.xSize);
            }


            int yId = (int)(Mathf.FloorToInt(position.y / layer.ySize));

            if (Mathf.Abs((position.y / layer.ySize) - Mathf.RoundToInt(position.y / layer.ySize)) < 0.001f)
            {
                yId = (int)Mathf.RoundToInt(position.y / layer.ySize);
            }

            int zId = (int)(Mathf.FloorToInt(position.z / layer.zSize));

            if (Mathf.Abs((position.z / layer.zSize) - Mathf.RoundToInt(position.z / layer.zSize)) < 0.001f)
            {
                zId = (int)Mathf.RoundToInt(position.z / layer.zSize);
            }


            return (layer.xSplitIs ? "_x" + xId : "") +
                   (layer.ySplitIs ? "_y" + yId : "")
                   + (layer.zSplitIs ? "_z" + zId : "");
        }

        /// <summary>
        /// Gets the split position divided by size.
        /// </summary>
        /// <returns>The split position I.</returns>
        /// <param name="position">Position.</param>
        /// <param name="layer">Layer.</param>
        private Vector3Int GetSplitPositionID(Vector3 position, SceneCollectionManager layer)
        {
            int x = (int)(Mathf.FloorToInt(position.x / layer.xSize));

            if (Mathf.Abs((position.x / layer.xSize) - Mathf.RoundToInt(position.x / layer.xSize)) < 0.001f)
            {
                x = (int)Mathf.RoundToInt(position.x / layer.xSize);
            }


            int y = (int)(Mathf.FloorToInt(position.y / layer.ySize));

            if (Mathf.Abs((position.y / layer.ySize) - Mathf.RoundToInt(position.y / layer.ySize)) < 0.001f)
            {
                y = (int)Mathf.RoundToInt(position.y / layer.ySize);
            }

            int z = (int)(Mathf.FloorToInt(position.z / layer.zSize));

            if (Mathf.Abs((position.z / layer.zSize) - Mathf.RoundToInt(position.z / layer.zSize)) < 0.001f)
            {
                z = (int)Mathf.RoundToInt(position.z / layer.zSize);
            }


            return new Vector3Int(x, y, z);
        }

        /// <summary>
        /// Gets the split position.
        /// </summary>
        /// <returns>The split position.</returns>
        /// <param name="position">Position of tile.</param>
        /// <param name="layer">Layer.</param>
        private Vector3 GetSplitPosition(Vector3 position, SceneCollectionManager layer)
        {
            int x = (int)(Mathf.FloorToInt(position.x / layer.xSize));

            if (Mathf.Abs((position.x / layer.xSize) - Mathf.RoundToInt(position.x / layer.xSize)) < 0.001f)
            {
                x = (int)Mathf.RoundToInt(position.x / layer.xSize);
            }


            int y = (int)(Mathf.FloorToInt(position.y / layer.ySize));

            if (Mathf.Abs((position.y / layer.ySize) - Mathf.RoundToInt(position.y / layer.ySize)) < 0.001f)
            {
                y = (int)Mathf.RoundToInt(position.y / layer.ySize);
            }

            int z = (int)(Mathf.FloorToInt(position.z / layer.zSize));

            if (Mathf.Abs((position.z / layer.zSize) - Mathf.RoundToInt(position.z / layer.zSize)) < 0.001f)
            {
                z = (int)Mathf.RoundToInt(position.z / layer.zSize);
            }


            return new Vector3(x * layer.xSize, y * layer.ySize, z * layer.zSize);
        }

        /// <summary>
        /// Splits the scene into tiles.
        /// </summary>	
        private void SplitScene(SceneCollectionManager layer)
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            warning = "";
            splits[layer.layerNumber] = new Dictionary<string, GameObject>();


            layer.xLimitsx = int.MaxValue;
            layer.xLimitsy = int.MinValue;
            layer.yLimitsx = int.MaxValue;
            layer.yLimitsy = int.MinValue;
            layer.zLimitsx = int.MaxValue;
            layer.zLimitsy = int.MinValue;


#if UNITY_2022_1_OR_NEWER && !Unity_2021_3_1 && !Unity_2021_3_2 && !Unity_2021_3_3 && !Unity_2021_3_4 && !Unity_2021_3_5 && !Unity_2021_3_6 && !Unity_2021_3_7 && !Unity_2021_3_8 && !Unity_2021_3_9 && !Unity_2021_3_10 && !Unity_2021_3_11 && !Unity_2021_3_12 && !Unity_2021_3_13 && !Unity_2021_3_14 && !Unity_2021_3_15 && !Unity_2021_3_16 && !Unity_2021_3_17
            GameObject[] allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
#else
            GameObject[] allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
#endif

            FindSceneGO(layer.prefixScene, allObjects, splits[layer.layerNumber]);

            ClearSceneGO(layer);

            bool isInCatalog = false;
            foreach (var item in allObjects)
            {
                if (item == null)
                    continue;

                if (item.GetComponent<SceneSplitManager>() != null || item.GetComponent<SceneSplitterSettings>() != null
                                                                   || item.GetComponent<ObjectsParent>() != null)
                    continue;

                isInCatalog = false;

                if (item.transform.parent != null && item.transform.parent.GetComponent<ObjectsParent>() != null
                                                  && item.transform.parent.GetComponent<ObjectsParent>().gameObjectPrefix == layer.prefixName)
                    isInCatalog = true;


                if (!isInCatalog && !item.name.StartsWith(layer.prefixName, System.StringComparison.Ordinal))
                    continue;


                if (item.transform.parent != null && item.transform.parent.GetComponent<ObjectsParent>() == null)
                    continue;


                string itemId = GetID(item.transform.position, layer);

                GameObject split = null;
                if (!splits[layer.layerNumber].TryGetValue(itemId, out split))
                {
                    split = new GameObject(layer.prefixScene + itemId);
                    SceneSplitManager sceneSplitManager = split.AddComponent<SceneSplitManager>();
                    sceneSplitManager.sceneName = split.name;

                    sceneSplitManager.size = new Vector3(layer.xSize != 0 ? layer.xSize : 100, layer.ySize != 0 ? layer.ySize : 100, layer.zSize != 0 ? layer.zSize : 100);
                    sceneSplitManager.position = GetSplitPosition(item.transform.position, layer);
                    sceneSplitManager.color = layer.color;

                    splits[layer.layerNumber].Add(itemId, split);

                    Vector3Int splitPosId = GetSplitPositionID(item.transform.position, layer);

                    if (layer.xSplitIs)
                    {
                        if (splitPosId.x < layer.xLimitsx)
                        {
                            layer.xLimitsx = (int)splitPosId.x;
                        }

                        if (splitPosId.x > layer.xLimitsy)
                        {
                            layer.xLimitsy = (int)splitPosId.x;
                        }
                    }
                    else
                    {
                        layer.xLimitsx = 0;
                        layer.xLimitsy = 0;
                    }

                    if (layer.ySplitIs)
                    {
                        if (splitPosId.y < layer.yLimitsx)
                        {
                            layer.yLimitsx = (int)splitPosId.y;
                        }

                        if (splitPosId.y > layer.yLimitsy)
                        {
                            layer.yLimitsy = (int)splitPosId.y;
                        }
                    }
                    else
                    {
                        layer.yLimitsx = 0;
                        layer.yLimitsy = 0;
                    }

                    if (layer.zSplitIs)
                    {
                        if (splitPosId.z < layer.zLimitsx)
                        {
                            layer.zLimitsx = (int)splitPosId.z;
                        }

                        if (splitPosId.z > layer.zLimitsy)
                        {
                            layer.zLimitsy = (int)splitPosId.z;
                        }
                    }
                    else
                    {
                        layer.zLimitsx = 0;
                        layer.zLimitsy = 0;
                    }
                }

                if (!isInCatalog)
                {
                    item.transform.SetParent(split.transform);
                }
                else
                {
                    ObjectsParent objectsParent = item.transform.parent.GetComponent<ObjectsParent>();

                    ObjectsInSceneParent objectsInSceneParent = split.GetComponentInChildren<ObjectsInSceneParent>();
                    if (objectsInSceneParent == null)
                    {
                        GameObject go = new GameObject(objectsParent.gameObjectPrefix + " Objects");
                        objectsInSceneParent = go.AddComponent<ObjectsInSceneParent>();
                        objectsInSceneParent.gameObjectPrefix = objectsParent.gameObjectPrefix;


                        objectsInSceneParent.transform.SetParent(split.transform);
                    }

                    item.transform.SetParent(objectsInSceneParent.transform);
                }
            }

            if (splits.Count == 0)
            {
                warning = "No objects to split. Check GameObject or Scene Prefix.";
            }
        }

        /// <summary>
        /// Finds the scene stream splits.
        /// </summary>
        /// <param name="allObjects">All objects in scene.</param>
        private void FindSceneGO(string prefixScene, GameObject[] allObjects, Dictionary<string, GameObject> splits)
        {
            foreach (var item in allObjects)
            {
                if (item == null)
                    continue;

                if (item.GetComponent<SceneSplitManager>() == null)
                {
                    continue;
                }

                if (item.transform.parent != null || !item.name.StartsWith(prefixScene, System.StringComparison.Ordinal))
                {
                    continue;
                }


                GameObject go;
                string sceneID = "";

                sceneID = item.name.Replace(prefixScene, "");
                if (!splits.TryGetValue(sceneID, out go))
                    splits.Add(sceneID, item);
            }
        }

        /// <summary>
        /// Clears the split scene.
        /// </summary>
        private void ClearSplitScene(SceneCollectionManager layer)
        {
            warning = "";
            if (splits == null)
                splits = new List<Dictionary<string, GameObject>>();

            while (splits.Count <= layer.layerNumber)
            {
                splits.Add(new Dictionary<string, GameObject>());
            }

            splits[layer.layerNumber] = new Dictionary<string, GameObject>();

#if UNITY_2022_1_OR_NEWER && !Unity_2021_3_1 && !Unity_2021_3_2 && !Unity_2021_3_3 && !Unity_2021_3_4 && !Unity_2021_3_5 && !Unity_2021_3_6 && !Unity_2021_3_7 && !Unity_2021_3_8 && !Unity_2021_3_9 && !Unity_2021_3_10 && !Unity_2021_3_11 && !Unity_2021_3_12 && !Unity_2021_3_13 && !Unity_2021_3_14 && !Unity_2021_3_15 && !Unity_2021_3_16 && !Unity_2021_3_17
            GameObject[] allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
#else
            GameObject[] allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
#endif

            FindSceneGO(layer.prefixScene, allObjects, splits[layer.layerNumber]);
            ClearSceneGO(layer);
        }

        /// <summary>
        /// Clears the scene Game objects.
        /// </summary>
        private void ClearSceneGO(SceneCollectionManager layer)
        {
#if UNITY_2022_1_OR_NEWER && !Unity_2021_3_1 && !Unity_2021_3_2 && !Unity_2021_3_3 && !Unity_2021_3_4 && !Unity_2021_3_5 && !Unity_2021_3_6 && !Unity_2021_3_7 && !Unity_2021_3_8 && !Unity_2021_3_9 && !Unity_2021_3_10 && !Unity_2021_3_11 && !Unity_2021_3_12 && !Unity_2021_3_13 && !Unity_2021_3_14 && !Unity_2021_3_15 && !Unity_2021_3_16 && !Unity_2021_3_17
            ObjectsParent[] objectsParents = FindObjectsByType<ObjectsParent>(FindObjectsSortMode.None);
#else
            ObjectsParent[] objectsParents = FindObjectsOfType<ObjectsParent>();
#endif


            ObjectsParent objectsParent = null;

            foreach (var parent in objectsParents)
            {
                if (parent.gameObjectPrefix == layer.prefixName)
                {
                    objectsParent = parent;
                    break;
                }
            }

#if UNITY_2022_1_OR_NEWER && !Unity_2021_3_1 && !Unity_2021_3_2 && !Unity_2021_3_3 && !Unity_2021_3_4 && !Unity_2021_3_5 && !Unity_2021_3_6 && !Unity_2021_3_7 && !Unity_2021_3_8 && !Unity_2021_3_9 && !Unity_2021_3_10 && !Unity_2021_3_11 && !Unity_2021_3_12 && !Unity_2021_3_13 && !Unity_2021_3_14 && !Unity_2021_3_15 && !Unity_2021_3_16 && !Unity_2021_3_17
            ObjectsInSceneParent[] objectsInSceneParents = FindObjectsByType<ObjectsInSceneParent>(FindObjectsSortMode.None);
#else
            ObjectsInSceneParent[] objectsInSceneParents = FindObjectsOfType<ObjectsInSceneParent>();
#endif
            //ObjectsInSceneParent objectsInSceneParent = null;

            foreach (var parent in objectsInSceneParents)
            {
                if (parent.gameObjectPrefix == layer.prefixName)
                {
                    //Debug.Log(parent);
                    // objectsInSceneParent = parent;

                    foreach (Transform child in parent.transform)
                    {
                        child.SetParent(objectsParent.transform, true);
                    }

                    int check = 0;
                    while (parent.transform.childCount > 0 && check < 100)
                    {
                        foreach (Transform child in parent.transform)
                        {
                            child.SetParent(objectsParent.transform, true);
                        }

                        check++;
                    }
                }
            }


            List<string> toRemove = new List<string>();

            foreach (var item in splits[layer.layerNumber])
            {
                if (item.Value.GetComponent<SceneSplitManager>())
                {
                    Transform splitTrans = item.Value.transform;
                    foreach (Transform splitChild in splitTrans)
                    {
                        if (objectsParent == null)
                        {
                            splitChild.parent = null;
                        }

                        if (splitChild.name.StartsWith(layer.prefixName, System.StringComparison.Ordinal))
                        {
                            splitChild.parent = null;
                        }
                    }

                    int check = 0;
                    while (splitTrans.childCount > 0 && check < 100)
                    {
                        foreach (Transform splitChild in splitTrans)
                        {
                            if (objectsParent == null)
                            {
                                splitChild.parent = null;
                            }

                            if (splitChild.name.StartsWith(layer.prefixName, System.StringComparison.Ordinal))
                            {
                                splitChild.parent = null;
                            }
                        }

                        check++;
                    }

                    //Debug.Log(check);
                    GameObject.DestroyImmediate(splitTrans.gameObject);
                    toRemove.Add(item.Key);
                }
            }

            foreach (var item in toRemove)
            {
                splits[layer.layerNumber].Remove(item);
            }
        }


        /// <summary>
        /// Generates scenes from splits with multi scene.
        /// </summary>
        private void GenerateScenesMulti(SceneCollectionManager layer, int currentLayerID, int layersCount)
        {
            if (cancel)
                return;
            warning = "";


            string scenesPath = this.sceneSplitterSettings.GetScenesPath();
            if (!Directory.Exists(scenesPath))
            {
                Directory.CreateDirectory(scenesPath);
            }

            if (scenesPath[scenesPath.Length - 1] != '/' && scenesPath[scenesPath.Length - 1] != '\\')
                scenesPath += "/";

            scenesPath += layer.prefixScene + "/";
            if (!Directory.Exists(scenesPath))
            {
                Directory.CreateDirectory(scenesPath);
                AssetDatabase.Refresh();
            }

            List<string> sceneNames = new List<string>();

            //EditorApplication.SaveScene ();

            Dictionary<string, GameObject> mainSplits = new Dictionary<string, GameObject>();

#if UNITY_2022_1_OR_NEWER && !Unity_2021_3_1 && !Unity_2021_3_2 && !Unity_2021_3_3 && !Unity_2021_3_4 && !Unity_2021_3_5 && !Unity_2021_3_6 && !Unity_2021_3_7 && !Unity_2021_3_8 && !Unity_2021_3_9 && !Unity_2021_3_10 && !Unity_2021_3_11 && !Unity_2021_3_12 && !Unity_2021_3_13 && !Unity_2021_3_14 && !Unity_2021_3_15 && !Unity_2021_3_16 && !Unity_2021_3_17
            GameObject[] allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
#else
            GameObject[] allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
#endif

            FindSceneGO(layer.prefixScene, allObjects, mainSplits);

            string currentScenePath = EditorSceneManager.GetActiveScene().path;
            //Debug.Log (currentScene);
            Dictionary<string, string> scenes = new Dictionary<string, string>();


            // List<string> splitsNames = new List<string>();
            //foreach (var split in mainSplits)
            //{
            //    splitsNames.Add(split.Value.name);

            //}

            if (splits.Count == 0)
            {
                warning = "No objects to build scenes.";
                return;
            }

            int i = 0;
            foreach (var split in mainSplits)
            {
                if (cancel)
                    return;
                sceneNames.Add(split.Value.name + ".unity");
                string sceneName = scenesPath + split.Value.name + ".unity";


                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

                SceneManager.MoveGameObjectToScene(split.Value, scene);
                EditorSceneManager.SaveScene(scene, sceneName);

                scenes.Add(split.Value.name, sceneName);

                if (EditorUtility.DisplayCancelableProgressBar("Creating Scenes " + (currentLayerID + 1) + '/' + layersCount + " (" + layer.prefixScene + ")",
                        "Creating scene " + Path.GetFileNameWithoutExtension(EditorSceneManager.GetActiveScene().name) + " " + i + " from " + mainSplits.Count,
                        (currentLayerID + (i / (float)mainSplits.Count)) / (float)layersCount))
                {
                    cancel = true;
                    EditorUtility.ClearProgressBar();
                    return;
                }

                i++;
            }


            EditorSceneManager.OpenScene(currentScenePath, OpenSceneMode.Single);

            layer.path = scenesPath;
            layer.names = sceneNames.ToArray();

            EditorUtility.SetDirty(layer);
            AssetDatabase.SaveAssets();
            Resources.UnloadUnusedAssets();
        }


        #region SceneGUI

        private void OnFocus()
        {
            SceneView.duringSceneGui -= this.OnSceneGUI;
            SceneView.duringSceneGui += this.OnSceneGUI;
        }

        private void OnDestroy()
        {
            SceneView.duringSceneGui -= this.OnSceneGUI;
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            Vector3 position = Vector3.zero;
            Vector3 size = Vector3.zero;
            Vector3Int number = Vector3Int.zero;

            if (sceneSplitterSettings != null)

                foreach (var sceneCollectionManager in sceneSplitterSettings.sceneCollectionManagers)
                {
                    if (sceneCollectionManager && sceneCollectionManager.showDebug)
                    {
                        Handles.color = sceneCollectionManager.color;
                        size.x = sceneCollectionManager.xSize == 0 ? 5 : sceneCollectionManager.xSize;
                        size.y = sceneCollectionManager.ySize == 0 ? 5 : sceneCollectionManager.ySize;
                        size.z = sceneCollectionManager.zSize == 0 ? 5 : sceneCollectionManager.zSize;


                        number.x = sceneCollectionManager.xSize == 0 ? 1 : 5;
                        number.y = sceneCollectionManager.ySize == 0 ? 0 : 5;
                        number.z = sceneCollectionManager.zSize == 0 ? 0 : 5;

                        for (int x = -number.x; x <= number.x; x++)
                        {
                            for (int y = -number.y; y <= number.y; y++)
                            {
                                for (int z = -number.z; z <= number.z; z++)
                                {
                                    position.x = x * size.x;
                                    position.y = y * size.y;
                                    position.z = z * size.z;
                                    Handles.DrawWireCube(position + size * 0.5f, size);
                                }
                            }
                        }
                    }
                }
        }

        #endregion
    }
}