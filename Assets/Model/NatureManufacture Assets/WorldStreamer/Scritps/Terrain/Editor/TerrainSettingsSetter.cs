// /**
//  * Created by Pawel Homenko on  11/2023
//  */

using UnityEditor;
using UnityEngine;

namespace WorldStreamer2
{
    public static class TerrainSettingsSetter
    {
        public static void SetTerrainSettings(TerrainManagerSettings terrainManagerSettings, bool allTerrains = false)
        {
            Terrain[] terrains = Selection.GetFiltered<Terrain>(SelectionMode.TopLevel);

            if (allTerrains)
                terrains = Terrain.activeTerrains;

            foreach (var terrain in terrains)
            {
                Undo.RegisterCompleteObjectUndo(terrain, "Modify Terrain");
                Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Modify Terrain");

                SetTerrainSettings(terrain, terrainManagerSettings);
            }
        }

        public static void SetTerrainSettings(Terrain terrain, TerrainManagerSettings terrainManagerSettings)
        {
            terrain.groupingID = terrainManagerSettings.groupingID;
            terrain.allowAutoConnect = terrainManagerSettings.allowAutoConnect;
            terrain.drawHeightmap = terrainManagerSettings.drawHeightmap;
            terrain.drawInstanced = terrainManagerSettings.drawInstanced;
            terrain.heightmapPixelError = terrainManagerSettings.heightmapPixelError;
            terrain.basemapDistance = terrainManagerSettings.basemapDistance;
            terrain.shadowCastingMode = terrainManagerSettings.shadowCastingMode;
            terrain.reflectionProbeUsage = terrainManagerSettings.reflectionProbeUsage;
            terrain.materialTemplate = terrainManagerSettings.materialTemplate;


            terrain.drawTreesAndFoliage = terrainManagerSettings.drawTreesAndFoliage;
            terrain.bakeLightProbesForTrees = terrainManagerSettings.bakeLightProbesForTrees;
            terrain.deringLightProbesForTrees = terrainManagerSettings.deringLightProbesForTrees;


            terrain.preserveTreePrototypeLayers = terrainManagerSettings.preserveTreePrototypeLayers;
            terrain.detailObjectDistance = terrainManagerSettings.detailObjectDistance;
            terrain.detailObjectDensity = terrainManagerSettings.detailObjectDensity;
            terrain.treeDistance = terrainManagerSettings.treeDistance;
            terrain.treeBillboardDistance = terrainManagerSettings.treeBillboardDistance;
            terrain.treeCrossFadeLength = terrainManagerSettings.treeCrossFadeLength;
            terrain.treeMaximumFullLODCount = terrainManagerSettings.treeMaximumFullLODCount;

            var data = terrain.terrainData;
            data.wavingGrassStrength = terrainManagerSettings.wavingGrassStrength;
            data.wavingGrassSpeed = terrainManagerSettings.wavingGrassSpeed;
            data.wavingGrassAmount = terrainManagerSettings.wavingGrassAmount;
            data.wavingGrassTint = terrainManagerSettings.wavingGrassTint;
        }
    }
}