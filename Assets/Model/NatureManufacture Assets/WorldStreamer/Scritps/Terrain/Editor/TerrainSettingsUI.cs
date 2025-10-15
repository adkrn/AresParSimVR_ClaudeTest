// /**
//  * Created by Pawel Homenko on  11/2023
//  */

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldStreamer2
{
    public static class TerrainSettingsUI
    {
        private static readonly GUIContent SBasicTerrain = EditorGUIUtility.TrTextContent("Basic Terrain");

        private static readonly GUIContent SGroupingID =
            EditorGUIUtility.TrTextContent("Grouping ID", "Grouping ID for auto connection");

        private static readonly GUIContent SAllowAutoConnect = EditorGUIUtility.TrTextContent("Auto Connect",
            "Allow the current terrain tile to automatically connect to neighboring tiles sharing the same grouping ID.");

        // private static readonly GUIContent SAttemptReconnect = EditorGUIUtility.TrTextContent("Reconnect", "Will attempt to re-run auto connection");

        private static readonly GUIContent SDrawTerrain =
            EditorGUIUtility.TrTextContent("Draw", "Toggle the rendering of terrain");

        private static readonly GUIContent SDrawInstancedTerrain =
            EditorGUIUtility.TrTextContent("Draw Instanced", "Toggle terrain instancing rendering");

        private static readonly GUIContent SPixelError = EditorGUIUtility.TrTextContent("Pixel Error",
            "The accuracy of the mapping between the terrain maps (heightmap, textures, etc.) and the generated terrain; higher values indicate lower accuracy but lower rendering overhead.");

        private static readonly GUIContent SBaseMapDist = EditorGUIUtility.TrTextContent("Base Map Dist.",
            "The maximum distance at which terrain textures will be displayed at full resolution. Beyond this distance, a lower resolution composite image will be used for efficiency.");

        private static readonly GUIContent SCastShadows =
            EditorGUIUtility.TrTextContent("Cast Shadows", "Does the terrain cast shadows?");

        // private static readonly GUIContent SCreateMaterial = EditorGUIUtility.TrTextContent("Create...", "Create a new Material asset to be used by the terrain by duplicating the current default Terrain material.");

        private static readonly GUIContent SReflectionProbes = EditorGUIUtility.TrTextContent("Reflection Probes",
            "How reflection probes are used on terrain. Only effective when using built-in standard material or a custom material which supports rendering with reflection.");

        private static readonly GUIContent SPreserveTreePrototypeLayers = EditorGUIUtility.TrTextContent(
            "Preserve Tree Prototype Layers",
            "Enable this option if you want your tree instances to take on the layer values of their prototype prefabs, rather than the terrain GameObject's layer.");

        private static readonly GUIContent STreeAndDetails = EditorGUIUtility.TrTextContent("Tree & Detail Objects");

        private static readonly GUIContent SDrawTrees =
            EditorGUIUtility.TrTextContent("Draw", "Should trees, grass and details be drawn?");

        private static readonly GUIContent SDetailObjectDistance = EditorGUIUtility.TrTextContent("Detail Distance",
            "The distance (from camera) beyond which details will be culled.");

        private static readonly GUIContent SDetailObjectDensity = EditorGUIUtility.TrTextContent("Detail Density",
            "The number of detail/grass objects in a given unit of area. The value can be set lower to reduce rendering overhead.");

        private static readonly GUIContent STreeDistance = EditorGUIUtility.TrTextContent("Tree Distance",
            "The distance (from camera) beyond which trees will be culled. For SpeedTree trees this parameter is controlled by the LOD group settings.");

        private static readonly GUIContent STreeBillboardDistance = EditorGUIUtility.TrTextContent("Billboard Start",
            "The distance (from camera) at which 3D tree objects will be replaced by billboard images. For SpeedTree trees this parameter is controlled by the LOD group settings.");

        private static readonly GUIContent STreeCrossFadeLength = EditorGUIUtility.TrTextContent("Fade Length",
            "Distance over which trees will transition between 3D objects and billboards. For SpeedTree trees this parameter is controlled by the LOD group settings.");

        private static readonly GUIContent STreeMaximumFullLODCount = EditorGUIUtility.TrTextContent("Max Mesh Trees",
            "The maximum number of visible trees that will be represented as solid 3D meshes. Beyond this limit, trees will be replaced with billboards. For SpeedTree trees this parameter is controlled by the LOD group settings.");

        private static readonly GUIContent SGrassWindSettings =
            EditorGUIUtility.TrTextContent("Wind Settings for Grass (On Terrain Data)");

        private static readonly GUIContent SWavingGrassStrength =
            EditorGUIUtility.TrTextContent("Speed", "The speed of the wind as it blows grass.");

        private static readonly GUIContent SWavingGrassSpeed = EditorGUIUtility.TrTextContent("Size",
            "The size of the 'ripples' on grassy areas as the wind blows over them.");

        private static readonly GUIContent SWavingGrassAmount =
            EditorGUIUtility.TrTextContent("Bending", "The degree to which grass objects are bent over by the wind.");

        private static readonly GUIContent SWavingGrassTint =
            EditorGUIUtility.TrTextContent("Grass Tint", "Overall color tint applied to grass objects.");

        private static readonly GUIContent SBakeLightProbesForTrees = EditorGUIUtility.TrTextContent(
            "Bake Light Probes For Trees",
            "If the option is enabled, Unity will create internal light probes at the position of each tree (these probes are internal and will not affect other renderers in the scene) and apply them to tree renderers for lighting. Otherwise trees are still affected by LightProbeGroups. The option is only effective for trees that have LightProbe enabled on their prototype prefab.");

        private static readonly GUIContent SDeringLightProbesForTrees = EditorGUIUtility.TrTextContent(
            "Remove Light Probe Ringing",
            "When enabled, removes visible overshooting often observed as ringing on objects affected by intense lighting at the expense of reduced contrast.");


        public static void TerrainSettingsGUI(TerrainManager terrainManager)
        {
            /*EditorApplication.SetSceneRepaintDirty();
                EditorUtility.SetDirty(terrain);
              if (!EditorApplication.isPlaying)
                    SceneManagement.EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
             */

            terrainManager.ManagerSettings.showBasicTerrainSettings =
                EditorGUILayout.BeginFoldoutHeaderGroup(terrainManager.ManagerSettings.showBasicTerrainSettings,
                    SBasicTerrain);

            if (terrainManager.ManagerSettings.showBasicTerrainSettings)
            {
                ++EditorGUI.indentLevel;

                terrainManager.ManagerSettings.groupingID =
                    EditorGUILayout.IntField(SGroupingID, terrainManager.ManagerSettings.groupingID);
                terrainManager.ManagerSettings.allowAutoConnect =
                    EditorGUILayout.Toggle(SAllowAutoConnect, terrainManager.ManagerSettings.allowAutoConnect);


                terrainManager.ManagerSettings.drawHeightmap =
                    EditorGUILayout.Toggle(SDrawTerrain, terrainManager.ManagerSettings.drawHeightmap);
                terrainManager.ManagerSettings.drawInstanced =
                    EditorGUILayout.Toggle(SDrawInstancedTerrain, terrainManager.ManagerSettings.drawInstanced);
                terrainManager.ManagerSettings.heightmapPixelError = EditorGUILayout.Slider(SPixelError, terrainManager.ManagerSettings.heightmapPixelError, 1, 200); // former string formatting: ""
                terrainManager.ManagerSettings.basemapDistance = EditorGUILayout.Slider(SBaseMapDist, terrainManager.ManagerSettings.basemapDistance, 0.0f, 20000.0f);
                terrainManager.ManagerSettings.shadowCastingMode =
                    (ShadowCastingMode) EditorGUILayout.EnumPopup(SCastShadows, terrainManager.ManagerSettings.shadowCastingMode);
                terrainManager.ManagerSettings.reflectionProbeUsage =
                    (ReflectionProbeUsage) EditorGUILayout.EnumPopup(SReflectionProbes, terrainManager.ManagerSettings.reflectionProbeUsage);
                terrainManager.ManagerSettings.materialTemplate = EditorGUILayout.ObjectField("Material", terrainManager.ManagerSettings.materialTemplate, typeof(Material), false) as Material;


                --EditorGUI.indentLevel;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.Space();

            terrainManager.ManagerSettings.showTreeAndDetailSettings =
                EditorGUILayout.BeginFoldoutHeaderGroup(terrainManager.ManagerSettings.showTreeAndDetailSettings,
                    STreeAndDetails);

            if (terrainManager.ManagerSettings.showTreeAndDetailSettings)
            {
                ++EditorGUI.indentLevel;

                terrainManager.ManagerSettings.drawTreesAndFoliage =
                    EditorGUILayout.Toggle(SDrawTrees, terrainManager.ManagerSettings.drawTreesAndFoliage);
                terrainManager.ManagerSettings.bakeLightProbesForTrees = EditorGUILayout.Toggle(SBakeLightProbesForTrees, terrainManager.ManagerSettings.bakeLightProbesForTrees);
                terrainManager.ManagerSettings.deringLightProbesForTrees = EditorGUILayout.Toggle(SDeringLightProbesForTrees, terrainManager.ManagerSettings.deringLightProbesForTrees);

                terrainManager.ManagerSettings.preserveTreePrototypeLayers =
                    EditorGUILayout.Toggle(SPreserveTreePrototypeLayers, terrainManager.ManagerSettings.preserveTreePrototypeLayers);
                terrainManager.ManagerSettings.detailObjectDistance = EditorGUILayout.Slider(SDetailObjectDistance, terrainManager.ManagerSettings.detailObjectDistance, 0, 1000);
                terrainManager.ManagerSettings.detailObjectDensity = EditorGUILayout.Slider(SDetailObjectDensity, terrainManager.ManagerSettings.detailObjectDensity, 0.0f, 1.0f);
                terrainManager.ManagerSettings.treeDistance =
                    EditorGUILayout.Slider(STreeDistance, terrainManager.ManagerSettings.treeDistance, 0, 5000);
                terrainManager.ManagerSettings.treeBillboardDistance = EditorGUILayout.Slider(STreeBillboardDistance, terrainManager.ManagerSettings.treeBillboardDistance, 5, 2000);
                terrainManager.ManagerSettings.treeCrossFadeLength = EditorGUILayout.Slider(STreeCrossFadeLength, terrainManager.ManagerSettings.treeCrossFadeLength, 0, 200);
                terrainManager.ManagerSettings.treeMaximumFullLODCount = EditorGUILayout.IntSlider(STreeMaximumFullLODCount, terrainManager.ManagerSettings.treeMaximumFullLODCount, 0, 10000);


                --EditorGUI.indentLevel;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.Space();

            terrainManager.ManagerSettings.showGrassWindSettings =
                EditorGUILayout.BeginFoldoutHeaderGroup(terrainManager.ManagerSettings.showGrassWindSettings,
                    SGrassWindSettings);

            if (terrainManager.ManagerSettings.showGrassWindSettings)
            {
                ++EditorGUI.indentLevel;

                terrainManager.ManagerSettings.wavingGrassStrength = EditorGUILayout.Slider(SWavingGrassStrength, terrainManager.ManagerSettings.wavingGrassStrength, 0, 1);
                terrainManager.ManagerSettings.wavingGrassSpeed = EditorGUILayout.Slider(SWavingGrassSpeed, terrainManager.ManagerSettings.wavingGrassSpeed, 0, 1);
                terrainManager.ManagerSettings.wavingGrassAmount = EditorGUILayout.Slider(SWavingGrassAmount, terrainManager.ManagerSettings.wavingGrassAmount, 0, 1);
                terrainManager.ManagerSettings.wavingGrassTint =
                    EditorGUILayout.ColorField(SWavingGrassTint, terrainManager.ManagerSettings.wavingGrassTint);

                --EditorGUI.indentLevel;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }
    }
}