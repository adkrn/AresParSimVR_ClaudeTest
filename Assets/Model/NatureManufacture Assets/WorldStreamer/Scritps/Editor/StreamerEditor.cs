using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;

namespace WorldStreamer2
{
    [CustomEditor(typeof(Streamer))]
    class StreamerEditor : Editor
    {
        //[MenuItem("GameObject/Create Other/Create World Streamer", false, 10)]
        [MenuItem("GameObject/NM/World Streamer", false, 10)]
        static public void CreateWorldStreamer()
        {
            GameObject gameObject = new GameObject("WorldStreamer");
            gameObject.tag = Streamer.STREAMERTAG;
            gameObject.AddComponent<Streamer>();
        }

        public override bool RequiresConstantRepaint()
        {
            return true;
        }


        bool checkedScenes = false;
        bool scenesToAddCheck = false;
        Editor _editor;

        public override void OnInspectorGUI()
        {
            Streamer streamer = (Streamer)target;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Streaming Info", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.indentLevel++;
            EditorGUILayout.BeginHorizontal(GUILayout.MaxWidth(360));

            if (Application.isPlaying == false && Streamer.loadingManager != null)
            {
                Streamer.loadingManager = null;
            }


            if (Streamer.loadingManager != null)
            {
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField("Scenes To Load", GUILayout.MaxWidth(120));
                EditorGUILayout.LabelField(Streamer.loadingManager.ScenesToLoadCount.ToString(), GUILayout.MaxWidth(100));
                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField("Scenes To Unload", GUILayout.MaxWidth(120));
                EditorGUILayout.LabelField(Streamer.loadingManager.ScenesToUnloadCount.ToString(), GUILayout.MaxWidth(100));
                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField("Async Operations");
                EditorGUILayout.LabelField(Streamer.loadingManager.AsyncOperationsCount.ToString(), GUILayout.MaxWidth(100));
                EditorGUILayout.EndVertical();
            }
            else
            {
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField("Scenes To Load", GUILayout.MaxWidth(120));
                EditorGUILayout.LabelField("0", GUILayout.MaxWidth(50));
                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField("Scenes To Unload", GUILayout.MaxWidth(120));
                EditorGUILayout.LabelField("0", GUILayout.MaxWidth(100));
                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField("Asyncs Operation", GUILayout.MaxWidth(120));
                EditorGUILayout.LabelField("0", GUILayout.MaxWidth(100));
                EditorGUILayout.EndVertical();
            }


            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();

            // DrawDefaultInspector();
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.Space();

            EditorGUI.indentLevel++;
            //public float 
            streamer.positionCheckTime = EditorGUILayout.FloatField(new GUIContent("Position Check Time", "Frequancy in seconds in which you want to check if grid element is close /far enough to load/unload."),
                streamer.positionCheckTime);

            //public float 
            streamer.destroyTileDelay = EditorGUILayout.FloatField(new GUIContent("Destroy Tile Delay", "Time in seconds after which grid element will be unloaded."), streamer.destroyTileDelay);

            ////public int 
            //streamer.maxParallelSceneLoading = EditorGUILayout.IntField(new GUIContent("Max Parallel Scene Loading", "Amount of max grid elements that you want to start loading in one frame."), streamer.maxParallelSceneLoading);

            // public int 
            streamer.sceneLoadWaitFrames = EditorGUILayout.IntField(new GUIContent("Scene Load Wait Frames", "Number of empty frames between loading actions."), streamer.sceneLoadWaitFrames);

            EditorGUILayout.Space();

            streamer.looping = EditorGUILayout.Toggle(
                new GUIContent("Looping", "Enable looping system, each layer is streamed independently, so if you want to synchronize them, they should have the same XYZ size. More info at manual."),
                streamer.looping);

            if (streamer.looping)
            {
                EditorGUI.indentLevel++;
                streamer.overideRangeLimit = EditorGUILayout.Toggle(new GUIContent("Overide single instance scene loading", "Override scene split range limit with loading scenes multiple times."),
                    streamer.overideRangeLimit);

                streamer.overideScenesLimits = EditorGUILayout.Toggle(new GUIContent("Sync scene collection limits", "Override scene collection split limit."), streamer.overideScenesLimits);

                streamer.xLimitsOverrideIs = EditorGUILayout.Toggle(new GUIContent("Override X limits", "Override scene collection split limit on x axis."), streamer.xLimitsOverrideIs);
                if (streamer.xLimitsOverrideIs)
                {
                    EditorGUI.indentLevel++;
                    streamer.xLimitsOverride = EditorGUILayout.Vector2IntField(new GUIContent("Override X limits", "Override scene collection split limit on x axis."), streamer.xLimitsOverride);
                    EditorGUI.indentLevel--;
                }

                streamer.yLimitsOverrideIs = EditorGUILayout.Toggle(new GUIContent("Override Y limits", "Override scene collection split limit on y axis."), streamer.yLimitsOverrideIs);
                if (streamer.yLimitsOverrideIs)
                {
                    EditorGUI.indentLevel++;
                    streamer.yLimitsOverride = EditorGUILayout.Vector2IntField(new GUIContent("Override Y limits", "Override scene collection split limit on y axis."), streamer.yLimitsOverride);
                    EditorGUI.indentLevel--;
                }

                streamer.zLimitsOverrideIs = EditorGUILayout.Toggle(new GUIContent("Override Z limits", "Override scene collection split limit on z axis."), streamer.zLimitsOverrideIs);
                if (streamer.zLimitsOverrideIs)
                {
                    EditorGUI.indentLevel++;
                    streamer.zLimitsOverride = EditorGUILayout.Vector2IntField(new GUIContent("Override Z limits", "Override scene collection split limit on z axis."), streamer.zLimitsOverride);
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.Space();


            streamer.terrainNeighbours =
                (TerrainNeighbours)EditorGUILayout.ObjectField(
                    new GUIContent("Terrain Neighbours",
                        "If you want to fix small holes from LODs system at unity terrain borders, drag and drop object here from scene hierarchy that contains our \"Terrain Neighbours\" script."),
                    streamer.terrainNeighbours, typeof(TerrainNeighbours), true);
            EditorGUILayout.Space();

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();

            //EditorGUI.indentLevel--;
            EditorGUILayout.LabelField("Player Settings", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.Space();
            streamer.player = (Transform)EditorGUILayout.ObjectField(new GUIContent("Player", "Drag and drop here, an object that system have to follow during streaming process."), streamer.player, typeof(Transform),
                true);

            streamer.spawnedPlayer = EditorGUILayout.Toggle(new GUIContent("Spawned Player", "Streamer will wait for player spawn and fill it automatically"), streamer.spawnedPlayer);


            if (streamer.player != null && EditorUtility.IsPersistent(streamer.player))
            {
                streamer.player = null;
            }

            if (streamer.spawnedPlayer)
            {
                string newTag = EditorGUILayout.TagField(new GUIContent("Player Tag", "Streamer will search for player by tag."), streamer.playerTag);

                if (streamer.playerTag != newTag)
                {
                    streamer.playerTag = newTag;
                    EditorUtility.SetDirty(streamer);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.EndVertical();

            EditorGUI.indentLevel--;
            EditorGUILayout.Space();

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RegisterCompleteObjectUndo(target, "UI change streamer");
            }

            SerializedProperty list = serializedObject.FindProperty("sceneCollectionManagers");

            //EditorGUILayout.PropertyField(list);
            //EditorGUI.indentLevel++;

            GUIStyle foldoutStyle = new GUIStyle(EditorStyles.foldout);
            foldoutStyle.fontStyle = FontStyle.Bold;
            list.isExpanded = EditorGUILayout.Foldout(list.isExpanded, new GUIContent("Scene Collection Managers"), foldoutStyle);


            if (list.isExpanded)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(list.FindPropertyRelative("Array.size"));
                EditorGUILayout.Space();
                EditorGUI.indentLevel++;
                for (int i = 0; i < list.arraySize; i++)
                {
                    SerializedProperty scriptable = list.GetArrayElementAtIndex(i);

                    if (scriptable != null)
                    {
                        Color old = GUI.color;


                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        EditorGUILayout.BeginHorizontal();
                        if (scriptable.objectReferenceValue != null)
                        {
                            if (i < streamer.sceneCollectionManagers.Count)
                                scriptable.isExpanded = EditorGUILayout.Foldout(scriptable.isExpanded,
                                    scriptable.objectReferenceValue.name + " (" + (streamer.sceneCollectionManagers[i].active ? "active" : "inactive") + ")", true, foldoutStyle);
                            else
                            {
                                scriptable.isExpanded = EditorGUILayout.Foldout(scriptable.isExpanded, scriptable.objectReferenceValue.name, true, EditorStyles.boldLabel);

                                if (streamer.sceneCollectionManagers.Count > i && streamer.sceneCollectionManagers[i] != null && !streamer.sceneCollectionManagers[i].active)
                                    GUI.color = Color.gray;
                            }
                        }

                        EditorGUILayout.PropertyField(scriptable, new GUIContent(""));

                        EditorGUILayout.EndHorizontal();


                        if (scriptable.isExpanded && scriptable.objectReferenceValue != null)
                        {
                            //EditorGUI.indentLevel++;
                            Editor.CreateCachedEditor(scriptable.objectReferenceValue, null, ref _editor);
                            if (_editor != null)
                            {
                                EditorGUI.BeginChangeCheck();
                                //_editor.DrawDefaultInspector();
                                _editor.OnInspectorGUI();

                                if (EditorGUI.EndChangeCheck())
                                {
                                    Undo.RegisterCompleteObjectUndo(streamer.sceneCollectionManagers[i], "UI change streamer collection");
                                    Undo.FlushUndoRecordObjects();

                                    EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                                }
                            }

                            //EditorGUI.indentLevel--;
                            EditorGUILayout.Space();
                        }

                        if (streamer.sceneCollectionManagers != null && i < streamer.sceneCollectionManagers.Count)
                        {
                            SceneCollectionManager currentCollection = streamer.sceneCollectionManagers[i];
                            if (currentCollection != null)
                            {
                                if (currentCollection.deloadingRange.x < currentCollection.loadingRange.x || currentCollection.deloadingRange.y < currentCollection.loadingRange.y ||
                                    currentCollection.deloadingRange.z < currentCollection.loadingRange.z)
                                {
                                    EditorGUILayout.HelpBox("Scene collection " + currentCollection.name + " deloading range must >= loading range", MessageType.Error, true);
                                }

                                if (streamer.looping && !streamer.overideRangeLimit)
                                {
                                    if (Mathf.Abs(currentCollection.xLimitsx - currentCollection.xLimitsy) < currentCollection.deloadingRange.x * 2 ||
                                        Mathf.Abs(currentCollection.yLimitsx - currentCollection.yLimitsy) < currentCollection.deloadingRange.y * 2 ||
                                        Mathf.Abs(currentCollection.zLimitsx - currentCollection.zLimitsy) < currentCollection.deloadingRange.z * 2)
                                    {
                                        EditorGUILayout.HelpBox("Scene collection " + currentCollection.name + " deloading range * 2 must > scene collection limits for looping to work correctly", MessageType.Warning,
                                            true);
                                    }
                                }
                            }
                        }

                        EditorGUILayout.EndVertical();

                        GUI.color = old;
                        EditorGUILayout.Space();
                    }
                }

                EditorGUILayout.Space();
                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }


            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();

            //if (_editor == null)
            //{
            //    _editor =
            //        Editor.CreateEditor(myTarget.sceneCollectionManagers[0]);
            //}
            //_editor.DrawDefaultInspector();


            serializedObject.ApplyModifiedProperties();


            if (streamer.sceneCollectionManagers == null || streamer.sceneCollectionManagers.Count == 0)
            {
                EditorGUILayout.HelpBox("No scene collection manager", MessageType.Info, true);
            }
            else
            {
                List<string> scenesToAdd = new List<string>();
                foreach (var sceneCollectionManager in streamer.sceneCollectionManagers)
                {
                    if (sceneCollectionManager != null)
                    {
                        SceneCollectionManager currentCollection = sceneCollectionManager;

                        if (!Directory.Exists(currentCollection.path))
                        {
                            EditorGUILayout.HelpBox("Scene collection path doesn't exist.", MessageType.Error, true);
                            return;
                        }

                        if (!checkedScenes)
                        {
                            checkedScenes = true;
                            List<EditorBuildSettingsScene> scenesList = new List<EditorBuildSettingsScene>();
                            scenesList.AddRange(EditorBuildSettings.scenes);


                            scenesToAdd.AddRange(currentCollection.names);

                            foreach (var item in scenesList)
                            {
                                if (scenesToAdd.Contains(item.path.Replace(currentCollection.path, "")) && item.enabled)
                                {
                                    scenesToAdd.Remove(item.path.Replace(currentCollection.path, ""));
                                }
                            }

                            if (scenesToAdd.Count > 0)
                            {
                                scenesToAddCheck = true;
                            }
                        }
                    }
                }

                if (scenesToAddCheck)
                    EditorGUILayout.HelpBox("Add scenes from scene collection to build settings.", MessageType.Error, true);

                if (GUILayout.Button("Add scenes to build settings"))
                {
                    AddScenesToBuild(streamer);
                    scenesToAddCheck = false;
                }
            }

            //Check tag exist
            //Open tag manager
            SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty tagsProp = tagManager.FindProperty("tags");


            // First check if it is not already present
            bool found = false;
            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                SerializedProperty t = tagsProp.GetArrayElementAtIndex(i);
                if (t.stringValue.Equals(Streamer.STREAMERTAG))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                EditorGUILayout.HelpBox("No tag " + Streamer.STREAMERTAG + " in unity.", MessageType.Error, true);
                if (GUILayout.Button("Add tag to unity"))
                {
                    found = false;
                    for (int i = 0; i < tagsProp.arraySize; i++)
                    {
                        SerializedProperty t = tagsProp.GetArrayElementAtIndex(i);
                        if (t.stringValue.Equals(Streamer.STREAMERTAG))
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        tagsProp.InsertArrayElementAtIndex(0);
                        SerializedProperty n = tagsProp.GetArrayElementAtIndex(0);
                        n.stringValue = Streamer.STREAMERTAG;
                        tagManager.ApplyModifiedProperties();
                    }

                    streamer.tag = Streamer.STREAMERTAG;
                }
            }

            if (streamer.tag != Streamer.STREAMERTAG)
            {
                EditorGUILayout.HelpBox("Streamer must have " + Streamer.STREAMERTAG + " Tag.", MessageType.Error, true);
                if (GUILayout.Button("Change tag"))
                {
                    streamer.tag = Streamer.STREAMERTAG;
                }
            }
        }

        /// <summary>
        /// Adds the scenes to build.
        /// </summary>
        void AddScenesToBuild(Streamer myTarget)
        {
            List<EditorBuildSettingsScene> scenesList = new List<EditorBuildSettingsScene>();
            scenesList.AddRange(EditorBuildSettings.scenes);
            foreach (var sceneCollectionManager in myTarget.sceneCollectionManagers)
            {
                List<string> scenesToAdd = new List<string>();
                scenesToAdd.AddRange(sceneCollectionManager.names);

                foreach (var item in scenesList)
                {
                    if (scenesToAdd.Contains(item.path.Replace(sceneCollectionManager.path, "")))
                    {
                        scenesToAdd.Remove(item.path.Replace(sceneCollectionManager.path, ""));
                        item.enabled = true;
                    }
                }

                foreach (var item in scenesToAdd)
                {
                    scenesList.Add(new EditorBuildSettingsScene(sceneCollectionManager.path + item, true));
                }
            }

            EditorBuildSettings.scenes = scenesList.ToArray();
        }
    }
}