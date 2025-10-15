// /**
//  * Created by Pawel Homenko on  11/2023
//  */

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TerrainTools;

namespace WorldStreamer2
{
    public static class TerrainSplitter
    {
        private const float SplitProgressParts = 5f;
        private static int _managerSettingsSplitSize;
        private static TerrainManagerSettings _terrainManagerSettings;
        private static string _progressCaption;
        private static float _progressTerrains;
        private static string _progressTextTerrainCount;
        private static Material _MainPaintMaterial = TerrainPaintUtility.GetBuiltinPaintMaterial();


        //enum type for painting on terrain
        private enum PaintingType
        {
            Heightmap,
            Hole,
            Texture,
        }


        public static void SplitTerrain(TerrainManagerSettings managerSettings, bool allTerrains = false)
        {
            _terrainManagerSettings = managerSettings;
            CreateDirectoryIfNotExists();
            Undo.SetCurrentGroupName("Split Terrain");
            _managerSettingsSplitSize = _terrainManagerSettings.splitSize;

            _terrainManagerSettings.terrainsCount = _managerSettingsSplitSize * _managerSettingsSplitSize;
            Terrain[] terrains = GetTerrains(allTerrains);
            ProcessTerrains(terrains);
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        }

        private static Terrain[] GetTerrains(bool allTerrains)
        {
            return allTerrains ? Terrain.activeTerrains : Selection.GetFiltered<Terrain>(SelectionMode.TopLevel);
        }

        private static void CreateDirectoryIfNotExists()
        {
            string path = "Assets/" + _terrainManagerSettings.terrainsDataPath;
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private static void ProcessTerrains(IReadOnlyCollection<Terrain> terrains)
        {
            if (!_MainPaintMaterial) _MainPaintMaterial = TerrainPaintUtility.GetBuiltinPaintMaterial();

            int currentObjectIndex = 0;
            foreach (Terrain terrain in terrains)
            {
                currentObjectIndex++;
                if (terrain == null) continue;

                Undo.RecordObject(terrain.gameObject, "Split Terrain");

                _progressCaption = $"Splitting terrain {terrain.name} ({currentObjectIndex} of {terrains.Count})";

                TerrainData terrainData = terrain.terrainData;
                Vector3 terrainPosition = terrain.GetPosition();

                GetTerrainRenderTextures(terrain, out RenderTexture heightMap, out RenderTexture holeTexture, out List<RenderTexture> alphamaps);


                List<(Terrain terrain, TerrainData terrainData)> newTerrains = CreateNewTerrains(terrain);

                SetNewTerrainsData(terrainPosition, terrainData, newTerrains, heightMap, holeTexture, alphamaps);

                AssetDatabase.SaveAssets();

                EditorUtility.ClearProgressBar();

                terrain.gameObject.SetActive(false);

                GC.Collect();

                ReleaseRenderTextures(heightMap, holeTexture, alphamaps);
            }
        }

        private static void SetNewTerrainsData(Vector3 terrainPosition, TerrainData terrainData, List<(Terrain terrain, TerrainData terrainData)> newTerrains, RenderTexture heightMap, RenderTexture holeTexture,
            List<RenderTexture> alphamaps)
        {
            for (int i = 0; i < _terrainManagerSettings.terrainsCount; i++)
            {
                int xI = (i % _managerSettingsSplitSize);
                int yI = (i / _managerSettingsSplitSize);
                GetTerrainChunkBrushRect(terrainPosition, terrainData, xI, yI, out BrushTransform brushChunkXForm);

                ProcessTerrainHeightGPU(newTerrains[i].terrain, brushChunkXForm, heightMap);

                ProcessTerrainHoleGPU(newTerrains[i].terrain, brushChunkXForm, holeTexture);

                ProcessTerrainAlphamapsGPU(terrainData, newTerrains[i].terrain, brushChunkXForm, alphamaps);

                ProcessTerrainDetails(terrainData, newTerrains[i].terrainData, xI, yI);

                ProcessTerrainTrees(terrainData, xI, yI, _progressTextTerrainCount, newTerrains[i].terrainData);
                newTerrains[i].terrainData.SyncHeightmap();
            }
        }

        private static List<(Terrain terrain, TerrainData terrainData)> CreateNewTerrains(Terrain terrain)
        {
            TerrainData terrainData = terrain.terrainData;

            List<(Terrain terrain, TerrainData terrainData)> newTerrains = new();
            for (int i = 0; i < _terrainManagerSettings.terrainsCount; i++)
            {
                newTerrains.Add(ProcessEachTerrainSplit(i, terrain, terrainData));
            }

            return newTerrains;
        }

        private static void ReleaseRenderTextures(RenderTexture heightMap, RenderTexture holeTexture, List<RenderTexture> alphamaps)
        {
            RenderTexture.ReleaseTemporary(heightMap);
            RenderTexture.ReleaseTemporary(holeTexture);
            // release all alphamaps textures
            foreach (RenderTexture alphamap in alphamaps)
            {
                RenderTexture.ReleaseTemporary(alphamap);
            }
        }

        private static void GetTerrainRenderTextures(Terrain terrain, out RenderTexture heightMap, out RenderTexture holeTexture, out List<RenderTexture> alphamaps)
        {
            TerrainData terrainData = terrain.terrainData;
            BrushTransform brushXForm = GetTerrainBrushRect(terrain, terrainData);

            heightMap = GetTerrainRenderTexture(terrain, brushXForm, PaintingType.Heightmap);

            holeTexture = GetTerrainRenderTexture(terrain, brushXForm, PaintingType.Hole);

            //get all alphamaps textures into list
            alphamaps = new List<RenderTexture>();
            for (int index = 0; index < terrainData.terrainLayers.Length; index++)
            {
                TerrainLayer terrainLayer = terrainData.terrainLayers[index];
                RenderTexture alphamap = GetTerrainRenderTexture(terrain, brushXForm, PaintingType.Texture, terrainLayer);
                alphamaps.Add(alphamap);
            }
        }

        private static BrushTransform GetTerrainBrushRect(Terrain terrain, TerrainData terrainData)
        {
            //Vector3 terrainPosition = terrain.GetPosition();
            Vector2 terrainSize = new(terrainData.size.x, terrainData.size.z);
            //Vector2 brushPosition = new(terrainPosition.x, terrainPosition.z);

            Rect brushRect = new(Vector2.zero, terrainSize);
            BrushTransform brushXForm = BrushTransform.FromRect(brushRect);
            return brushXForm;
        }


        private static (Terrain terrain, TerrainData terrainData) ProcessEachTerrainSplit(int i, Terrain terrain, TerrainData terrainData)
        {
            int xI = (i % _managerSettingsSplitSize);
            int yI = (i / _managerSettingsSplitSize);

            _progressTextTerrainCount = $"Generating terrain split {i}/terrainsCount";
            _progressTerrains = i / (float)_terrainManagerSettings.terrainsCount;

            EditorUtility.DisplayProgressBar(_progressCaption, _progressTextTerrainCount, _progressTerrains);

            TerrainData newTerrainData = CreateNewTerrain(terrain, terrainData, i, xI, yI, out Terrain newTerrain);


            return (newTerrain, newTerrainData);
        }

        private static TerrainData CreateNewTerrain(Terrain terrain, TerrainData terrainData, int i, int xI, int yI, out Terrain newTerrain)
        {
            TerrainData newTerrainData = GetNewTerrainData(terrainData, $"{terrainData.name} {xI}_{yI}");

            GameObject newTerrainGo = Terrain.CreateTerrainGameObject(newTerrainData);

            UndoRegisterCreatedObjects(newTerrainData, newTerrainGo);

            newTerrainGo.name = $"{terrain.name} {xI}_{yI}";
            newTerrain = AssignTerrainData(newTerrainGo, newTerrainData);

            TerrainSettingsSetter.SetTerrainSettings(newTerrain, _terrainManagerSettings);

            SetNewTerrainPosition(i, terrain, terrainData, newTerrainGo);

            newTerrain.materialTemplate = terrain.materialTemplate;

            CreateAssetForNewTerrain(newTerrain, _terrainManagerSettings, newTerrainData);


            return newTerrainData;
        }


        private static void ProcessTerrainHoleGPU(Terrain newTerrain, BrushTransform brushChunkXForm, RenderTexture renderTexture)
        {
            PaintTerrain(newTerrain, brushChunkXForm, renderTexture, PaintingType.Hole);
        }

        private static void ProcessTerrainHeightGPU(Terrain newTerrain, BrushTransform brushChunkXForm, RenderTexture renderTexture)
        {
            PaintTerrain(newTerrain, brushChunkXForm, renderTexture, PaintingType.Heightmap);
        }


        private static void ProcessTerrainAlphamapsGPU(TerrainData terrainData, Terrain newTerrain, BrushTransform brushChunkXForm, List<RenderTexture> renderTexture)
        {
            int alphamapId = 0;
            for (int index = 0; index < terrainData.terrainLayers.Length; index++)
            {
                TerrainLayer terrainLayer = terrainData.terrainLayers[index];
                PaintTerrain(newTerrain, brushChunkXForm, renderTexture[index], PaintingType.Texture, terrainLayer);

                string info = $"{_progressTextTerrainCount} (Processing splat {alphamapId})";
                float currentProgress = (alphamapId / (float)terrainData.alphamapLayers);
                if (ShowCancelableProgressBar(info, 2, currentProgress)) return;
                alphamapId++;
            }
        }

        private static PaintContext BeginPaint(Terrain terrain, BrushTransform brushXForm, PaintingType paintingType, TerrainLayer terrainLayer = null)
        {
            PaintContext paintContext;
            switch (paintingType)
            {
                case PaintingType.Heightmap:
                    paintContext = TerrainPaintUtility.BeginPaintHeightmap(terrain, brushXForm.GetBrushXYBounds());
                    break;
                case PaintingType.Hole:
                    paintContext = TerrainPaintUtility.BeginPaintHoles(terrain, brushXForm.GetBrushXYBounds());
                    break;
                case PaintingType.Texture:
                default:
                    paintContext = TerrainPaintUtility.BeginPaintTexture(terrain, brushXForm.GetBrushXYBounds(), terrainLayer);
                    break;
            }

            return paintContext;
        }

        private static void EndPainting(PaintingType paintingType, PaintContext paintContext)
        {
            switch (paintingType)
            {
                case PaintingType.Heightmap:
                    TerrainPaintUtility.EndPaintHeightmap(paintContext, "Terrain Paint");
                    break;
                case PaintingType.Hole:
                    TerrainPaintUtility.EndPaintHoles(paintContext, "Terrain Paint");
                    break;
                case PaintingType.Texture:
                default:
                    TerrainPaintUtility.EndPaintTexture(paintContext, "Terrain Paint");
                    break;
            }
        }

        private static void PaintTerrain(Terrain newTerrain, BrushTransform brushChunkXForm, RenderTexture renderTexture, PaintingType paintingType, TerrainLayer terrainLayer = null)
        {
            PaintContext paintContext = BeginPaint(newTerrain, brushChunkXForm, paintingType, terrainLayer);

            TerrainPaintUtility.SetupTerrainToolMaterialProperties(paintContext, brushChunkXForm, _MainPaintMaterial);

            Graphics.Blit(renderTexture, paintContext.destinationRenderTexture);

            EndPainting(paintingType, paintContext);
        }

        private static RenderTexture GetTerrainRenderTexture(Terrain terrain, BrushTransform brushXForm, PaintingType paintingType, TerrainLayer terrainLayer = null)
        {
            PaintContext paintContext = BeginPaint(terrain, brushXForm, paintingType, terrainLayer);

            TerrainPaintUtility.SetupTerrainToolMaterialProperties(paintContext, brushXForm, _MainPaintMaterial);


            RenderTexture temp = RenderTexture.GetTemporary(paintContext.sourceRenderTexture.descriptor);

            Graphics.Blit(paintContext.sourceRenderTexture, temp);
            temp.filterMode = FilterMode.Point;
            TerrainPaintUtility.ReleaseContextResources(paintContext);
            return temp;
        }


        private static void GetTerrainChunkBrushRect(Vector3 terrainPosition, TerrainData terrainData, int xI, int yI, out BrushTransform brushChunkXForm)
        {
            Vector3 terrainSizeChunk = terrainData.size / _managerSettingsSplitSize;

            //Vector2 chunkSize = new(terrainSizeChunk.x, terrainSizeChunk.z);
            Vector2 terrainSize = new(terrainData.size.x, terrainData.size.z);
            
            //Vector2 brushPosition = new Vector2(terrainPosition.x, terrainPosition.z) - new Vector2(yI * terrainSizeChunk.x, xI * terrainSizeChunk.z);
            Vector2 brushPosition =  - new Vector2(yI * terrainSizeChunk.x, xI * terrainSizeChunk.z);

            //Debug.Log($"name: {terrainData.name} terrainSizeChunk: {terrainSizeChunk} terrainSize: {terrainSize} brushPosition: {brushPosition}");

            Rect brushChunkRect = new(brushPosition, terrainSize);
            brushChunkXForm = BrushTransform.FromRect(brushChunkRect);
        }


        private static void ProcessTerrainDetails(TerrainData terrainData, TerrainData newTerrainData, int xI, int yI)
        {
            int startX;
            int startY;
            newTerrainData.SetDetailResolution(terrainData.detailResolution / _managerSettingsSplitSize, terrainData.detailResolutionPerPatch);

            int detailShift = terrainData.detailResolution / _managerSettingsSplitSize;

            for (int d = 0; d < terrainData.detailPrototypes.Length; d++)
            {
                string info = $"{_progressTextTerrainCount} (Processing detail {d})";
                float currentProgress = (d / (float)terrainData.detailPrototypes.Length);
                if (ShowCancelableProgressBar(info, 3, currentProgress)) return;

                int[,] terrainDetail = terrainData.GetDetailLayer(0, 0,
                    terrainData.detailResolution, terrainData.detailResolution, d);

                int[,] partDetail = new int[detailShift, detailShift];


                startX = startY = 0;


                for (int x = startX; x < detailShift; x++)
                {
                    for (int y = startY; y < detailShift; y++)
                    {
                        int detail = terrainDetail[x + detailShift * xI, y + detailShift * yI];
                        partDetail[x, y] = detail;
                    }
                }

                newTerrainData.SetDetailLayer(0, 0, d, partDetail);
            }
        }

        private static TerrainData GetNewTerrainData(TerrainData terrainData, string name)
        {
            var newTerrainData = new TerrainData
            {
                name = name
            };

#if !UNITY_2021
            newTerrainData.SetDetailScatterMode(terrainData.detailScatterMode);
#endif
            newTerrainData.terrainLayers = terrainData.terrainLayers;
            newTerrainData.detailPrototypes = terrainData.detailPrototypes;
            newTerrainData.treePrototypes = terrainData.treePrototypes;

            newTerrainData.heightmapResolution = terrainData.heightmapResolution / _managerSettingsSplitSize;
            //Debug.Log($"heightmapResolution: {terrainData.heightmapResolution} newTerrainData.heightmapResolution: {newTerrainData.heightmapResolution} managerSettingsSplitSize: {_managerSettingsSplitSize}");

            newTerrainData.size = new Vector3(terrainData.size.x / _managerSettingsSplitSize, terrainData.size.y, terrainData.size.z / _managerSettingsSplitSize);
            //Debug.Log($"terrainData.size: {terrainData.size} newTerrainData.size: {newTerrainData.size} managerSettingsSplitSize: {_managerSettingsSplitSize}");

            return newTerrainData;
        }

        private static void SetNewTerrainPosition(int i, Terrain terrain, TerrainData terrainData, GameObject newTerrainGo)
        {
            float spaceShiftX = terrainData.size.z / _managerSettingsSplitSize;
            float spaceShiftY = terrainData.size.x / _managerSettingsSplitSize;

            float xWShift = (i % _managerSettingsSplitSize) * spaceShiftX;
            // ReSharper disable once PossibleLossOfFraction
            float zWShift = (i / _managerSettingsSplitSize) * spaceShiftY;

            Vector3 parentPosition = terrain.GetPosition();
            Vector3 position = newTerrainGo.transform.position;
            position = parentPosition + new Vector3(position.x + zWShift, position.y, position.z + xWShift);
            newTerrainGo.transform.position = position;
        }


        private static void ProcessTerrainTrees(TerrainData terrainData, int xI, int yI, string progressText, TerrainData newTerrainData)
        {
            float size = 1 / (float)_managerSettingsSplitSize;
            float sizeCheckX = size * xI;
            float sizeCheckX1 = size * (xI + 1);

            float sizeCheckY = size * yI;
            float sizeCheckY1 = size * (yI + 1);


            List<TreeInstance> treeInstancesList = new();

            TreeInstance ti;
            TreeInstance[] treeInstances = terrainData.treeInstances;

            for (int t = 0; t < treeInstances.Length; t++)
            {
                if (t % 1000 == 0)
                {
                    string info = $"{progressText} (Processing trees {t}/{treeInstances.Length})";
                    float currentProgress = ((float)t / treeInstances.Length);
                    if (ShowCancelableProgressBar(info, 4, currentProgress)) return;
                }

                ti = treeInstances[t];

                if (!(ti.position.x >= sizeCheckY) || !(ti.position.x <= sizeCheckY1) ||
                    !(ti.position.z >= sizeCheckX) || !(ti.position.z <= sizeCheckX1)) continue;

                ti.position = new Vector3((ti.position.x * _managerSettingsSplitSize) % 1,
                    ti.position.y, (ti.position.z * _managerSettingsSplitSize) % 1);

                treeInstancesList.Add(ti);
            }

            //Debug.Log("end");

            newTerrainData.SetTreeInstances(treeInstancesList.ToArray(), true);
        }

        private static bool ShowCancelableProgressBar(string info, int partNumber, float currentProgress)
        {
            float terrainsCountProgress = _progressTerrains + (partNumber / SplitProgressParts + currentProgress / SplitProgressParts) / _terrainManagerSettings.terrainsCount;

            bool cancel = EditorUtility.DisplayCancelableProgressBar(_progressCaption, info, terrainsCountProgress);

            if (!cancel) return false;

            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
            EditorUtility.ClearProgressBar();
            return true;
        }

        private static void UndoRegisterCreatedObjects(TerrainData td, GameObject tgo)
        {
            Undo.RegisterCreatedObjectUndo(td, "Create terrain data");
            Undo.RegisterCreatedObjectUndo(tgo, "Create terrain split");
        }

        private static Terrain AssignTerrainData(GameObject tgo, TerrainData td)
        {
            Terrain newTerrain = tgo.GetComponent<Terrain>();
            newTerrain.terrainData = td;

            newTerrain.gameObject.AddComponent<TerrainCullingSystem>();

            return newTerrain;
        }

        private static void CreateAssetForNewTerrain(Terrain terrain, TerrainManagerSettings managerSettings, TerrainData terrainData)
        {
            AssetDatabase.CreateAsset(terrainData, "Assets" + managerSettings.terrainsDataPath + terrain.name + ".asset");
        }
    }
}