using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;

namespace WorldStreamer2
{
    [CustomEditor(typeof(SceneCollectionManager))]
    class SceneCollectionManagerEditor : Editor
    {
        SerializedProperty activeProp;
        SerializedProperty priorityProp;
        SerializedProperty maxParallelSceneLoadingProp;
        SerializedProperty loadingRangegProp;
        SerializedProperty useLoadingRangeMinProp;
        SerializedProperty deloadingRangeProp;

        SerializedProperty loadingRangeMinProp;
        SerializedProperty useUnloadRangeConnectProp;
        SerializedProperty unloadRangeConnectProp;

        void OnEnable()
        {
            // Fetch the objects from the GameObject script to display in the inspector
            activeProp = serializedObject.FindProperty("active");
            priorityProp = serializedObject.FindProperty("priority");
            maxParallelSceneLoadingProp = serializedObject.FindProperty("maxParallelSceneLoading");

            loadingRangegProp = serializedObject.FindProperty("loadingRange");
            useLoadingRangeMinProp = serializedObject.FindProperty("useLoadingRangeMin");
            deloadingRangeProp = serializedObject.FindProperty("deloadingRange");

            loadingRangeMinProp = serializedObject.FindProperty("loadingRangeMin");
            useUnloadRangeConnectProp = serializedObject.FindProperty("useUnloadRangeConnect");
            unloadRangeConnectProp = serializedObject.FindProperty("unloadRangeConnect");
        }


        public override void OnInspectorGUI()
        {
            SceneCollectionManager myTarget = (SceneCollectionManager)target;

            // DrawDefaultInspector();
            EditorGUILayout.Space();
            GUILayout.Label("Main", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(activeProp, new GUIContent("Active"));
            EditorGUILayout.PropertyField(priorityProp, new GUIContent("Priority", "Priority for streaming of scenes."));
            EditorGUILayout.PropertyField(maxParallelSceneLoadingProp, new GUIContent("Max Parallel Scene Loading", "Amount of max grid elements that you want to start loading in one frame."));


            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();

            GUILayout.Label("Ranges", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(loadingRangegProp, new GUIContent("Loading Range", "Distance in grid elements that you want hold loaded."));


            EditorGUILayout.PropertyField(useLoadingRangeMinProp, new GUIContent("Use Loading Range Min", "Area that you want to cutout from loading range."));

            if (useLoadingRangeMinProp.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(loadingRangeMinProp, new GUIContent("Loading Range Min", "Area that you want to cutout from loading range."));

                EditorGUILayout.PropertyField(useUnloadRangeConnectProp, new GUIContent("Sync with other layer", "Enables ccene collection elements to unload when scene loaded."));

                if (useUnloadRangeConnectProp.boolValue)
                {
                    //EditorGUILayout.HelpBox("Sync with other layer may generate small lag when turned on.", MessageType.Warning, true);
                    EditorGUILayout.PropertyField(unloadRangeConnectProp, new GUIContent("Sync Layer", "Area that you want to cutout from loading range."));
                }

                EditorGUI.indentLevel--;
            }


            EditorGUILayout.PropertyField(deloadingRangeProp, new GUIContent("Deloading Range", "Distance in grid elements after which you want to unload assets."));


            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.EndVertical();
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RegisterCompleteObjectUndo(myTarget, "UI change streamer collection");
                Undo.FlushUndoRecordObjects();

                if (!Application.isPlaying)
                    EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }

            bool warningCheckingEmpty = false;
            if ((myTarget.loadingRange.x > 0 || myTarget.deloadingRange.x > 0) && !myTarget.xSplitIs)
                warningCheckingEmpty = true;
            if ((myTarget.loadingRange.y > 0 || myTarget.deloadingRange.y > 0) && !myTarget.ySplitIs)
                warningCheckingEmpty = true;
            if ((myTarget.loadingRange.z > 0 || myTarget.deloadingRange.z > 0) && !myTarget.zSplitIs)
                warningCheckingEmpty = true;

            if (warningCheckingEmpty)
                EditorGUILayout.HelpBox("Loading Range for streaming is setup for axis that has tile size of 0", MessageType.Warning, true);
        }
    }
}