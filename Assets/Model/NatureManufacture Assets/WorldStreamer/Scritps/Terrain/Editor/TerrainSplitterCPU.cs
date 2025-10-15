// /**
//  * Created by Pawel Homenko on  02/2024
//  */

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;
using UnityEngine;
using UnityEditor;

namespace WorldStreamer2
{
    public static class TerrainSplitterCPU
    {
        private static TerrainManagerSettings _terrainManagerSettings;

        public static void SplitTerrain(TerrainManagerSettings managerSettings, bool allTerrains = false)
        {
            _terrainManagerSettings = managerSettings;

            if (!Directory.Exists("Assets/" + _terrainManagerSettings.terrainsDataPath))
            {
                Directory.CreateDirectory("Assets/" + _terrainManagerSettings.terrainsDataPath);
            }

            Undo.SetCurrentGroupName("Split Terrain");

            _terrainManagerSettings.terrainsCount = _terrainManagerSettings.splitSize * _terrainManagerSettings.splitSize;

            Terrain[] terrains = Selection.GetFiltered<Terrain>(SelectionMode.TopLevel);

            if (allTerrains)
                terrains = Terrain.activeTerrains;

            var currentObjectIndex = 0;
            List<TreeInstance> treeInstancesList = new List<TreeInstance>();
            TreeInstance[] treeInstances;
            TreeInstance ti;

            foreach (var terrain in terrains)
            {
                currentObjectIndex++;


                if (terrain == null)
                {
                    continue;
                }

                var progressCaption = "Spliting terrain " + terrain.name + " (" + currentObjectIndex.ToString() +
                                      " of " + terrains.Length.ToString() + ")";

                for (int i = 0; i < _terrainManagerSettings.terrainsCount; i++)
                {
                    int xI = (i % _terrainManagerSettings.splitSize);
                    int yI = (i / _terrainManagerSettings.splitSize);

                    float size = 1 / (float)_terrainManagerSettings.splitSize;

                    string progressText = "Generating terrain split " + i + "/terrainsCount";
                    float progress = i / (float)_terrainManagerSettings.terrainsCount;

                    EditorUtility.DisplayProgressBar(progressCaption, progressText, progress);

                    TerrainData td = new TerrainData();
                    GameObject tgo = Terrain.CreateTerrainGameObject(td);

                    Undo.RegisterCreatedObjectUndo(td, "Create terrain data");
                    Undo.RegisterCreatedObjectUndo(tgo, "Create terrain split");

                    tgo.name = terrain.name + " " + xI + "_" + yI;


                    Terrain newTerrain = tgo.GetComponent<Terrain>();
                    newTerrain.terrainData = td;

                    AssetDatabase.CreateAsset(td,
                        "Assets" + _terrainManagerSettings.terrainsDataPath + newTerrain.name + ".asset");


                    //copy all prototypes
                    newTerrain.terrainData.terrainLayers = terrain.terrainData.terrainLayers;

                    newTerrain.terrainData.detailPrototypes = terrain.terrainData.detailPrototypes;

                    newTerrain.terrainData.treePrototypes = terrain.terrainData.treePrototypes;


                    TerrainSettingsSetter.SetTerrainSettings(newTerrain,_terrainManagerSettings);
                    newTerrain.materialTemplate = terrain.materialTemplate;


                    Vector3 parentPosition = terrain.GetPosition();


                    float spaceShiftX = terrain.terrainData.size.z / _terrainManagerSettings.splitSize;
                    float spaceShiftY = terrain.terrainData.size.x / _terrainManagerSettings.splitSize;

                    float xWShift = (i % _terrainManagerSettings.splitSize) * spaceShiftX;
                    float zWShift = (i / _terrainManagerSettings.splitSize) * spaceShiftY;

                    //Debug.Log(xWShift + " " + zWShift);


                    td.heightmapResolution = terrain.terrainData.heightmapResolution / _terrainManagerSettings.splitSize;

                    td.size = new Vector3(terrain.terrainData.size.x / _terrainManagerSettings.splitSize,
                        terrain.terrainData.size.y,
                        terrain.terrainData.size.z / _terrainManagerSettings.splitSize
                    );


                    float[,] terrainHeight = terrain.terrainData.GetHeights(0, 0,
                        terrain.terrainData.heightmapResolution, terrain.terrainData.heightmapResolution);

                    float[,] partHeight =
                        new float[terrain.terrainData.heightmapResolution / _terrainManagerSettings.splitSize + 1,
                            terrain.terrainData.heightmapResolution / _terrainManagerSettings.splitSize + 1];


                    int heightShift = terrain.terrainData.heightmapResolution / _terrainManagerSettings.splitSize;

                    int startX = 0;
                    int startY = 0;

                    int end = terrain.terrainData.heightmapResolution / _terrainManagerSettings.splitSize + 1;


                    for (int x = startX; x < end; x++)
                    {
                        bool cancel = EditorUtility.DisplayCancelableProgressBar(progressCaption,
                            progressText + " (Split height)",
                            progress + (((float)x / (end - startX)) / 5f) /
                            (float)_terrainManagerSettings.terrainsCount);

                        if (cancel)
                        {
                            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
                            EditorUtility.ClearProgressBar();
                            return;
                        }

                        for (int y = startY; y < end; y++)
                        {
                            float ph = terrainHeight[x + heightShift * xI, y + heightShift * yI];

                            partHeight[x, y] = ph;
                        }
                    }

                    newTerrain.terrainData.SetHeights(0, 0, partHeight);


                    //hole data
                    bool[,] terrainHole = terrain.terrainData.GetHoles(0, 0,
                        terrain.terrainData.holesResolution, terrain.terrainData.holesResolution);

                    bool[,] partHole =
                        new bool[terrain.terrainData.holesResolution / _terrainManagerSettings.splitSize,
                            terrain.terrainData.holesResolution / _terrainManagerSettings.splitSize];

                    int endHole = terrain.terrainData.holesResolution / _terrainManagerSettings.splitSize;
                    int holeShift = terrain.terrainData.heightmapResolution / _terrainManagerSettings.splitSize;

                    for (int x = startX; x < endHole; x++)
                    {
                        bool cancel = EditorUtility.DisplayCancelableProgressBar(progressCaption,
                            progressText + " (Split height)",
                            progress + (1 / 5f + ((float)x / (endHole - startX)) / 5f) /
                            (float)_terrainManagerSettings.terrainsCount);

                        if (cancel)
                        {
                            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
                            EditorUtility.ClearProgressBar();
                            return;
                        }

                        for (int y = startY; y < endHole; y++)
                        {
                            partHole[x, y] = terrainHole[x + holeShift * xI, y + holeShift * yI];
                        }
                    }

                    newTerrain.terrainData.SetHoles(0, 0, partHole);


                    //td.holesResolution = terrain.terrainData.heightmapResolution / _terrainManagerSettings.splitSize;


                    // terrain.terrainData.holesTexture


                    td.alphamapResolution = terrain.terrainData.alphamapResolution / _terrainManagerSettings.splitSize;

                    float[,,] terrainSplat = terrain.terrainData.GetAlphamaps(0, 0,
                        terrain.terrainData.alphamapResolution, terrain.terrainData.alphamapResolution);

                    float[,,] partSplat =
                        new float[terrain.terrainData.alphamapResolution / _terrainManagerSettings.splitSize,
                            terrain.terrainData.alphamapResolution / _terrainManagerSettings.splitSize,
                            terrain.terrainData.alphamapLayers
                        ];

                    int splatShift = terrain.terrainData.alphamapResolution / _terrainManagerSettings.splitSize;

                    int start = 0;
                    end = terrain.terrainData.alphamapResolution / _terrainManagerSettings.splitSize;


                    for (int s = 0; s < terrain.terrainData.alphamapLayers; s++)
                    {
                        bool cancel = EditorUtility.DisplayCancelableProgressBar(progressCaption,
                            progressText + " (Processing splat " + s + ")",
                            progress + (2 / 5f + (s / (float)terrain.terrainData.alphamapLayers) / 5f) /
                            (float)_terrainManagerSettings.terrainsCount);
                        if (cancel)
                        {
                            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
                            EditorUtility.ClearProgressBar();
                            return;
                        }

                        for (int x = start; x < end; x++)
                        {
                            for (int y = start; y < end; y++)
                            {
                                partSplat[x, y, s] = terrainSplat[x + splatShift * xI, y + splatShift * yI, s];
                            }
                        }
                    }

                    newTerrain.terrainData.SetAlphamaps(0, 0, partSplat);

                    td.SetDetailResolution(terrain.terrainData.detailResolution / _terrainManagerSettings.splitSize,
                        terrain.terrainData.detailResolutionPerPatch);
                    int detailShift = terrain.terrainData.detailResolution / _terrainManagerSettings.splitSize;
                    for (int d = 0; d < terrain.terrainData.detailPrototypes.Length; d++)
                    {
                        bool cancel = EditorUtility.DisplayCancelableProgressBar(progressCaption,
                            progressText + " (Processing detail " + d + ")",
                            progress + (3 / 5f + (d / (float)terrain.terrainData.detailPrototypes.Length) / 5f) /
                            (float)_terrainManagerSettings.terrainsCount);
                        if (cancel)
                        {
                            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
                            EditorUtility.ClearProgressBar();
                            return;
                        }

                        int[,] terrainDetail = terrain.terrainData.GetDetailLayer(0, 0,
                            terrain.terrainData.detailResolution, terrain.terrainData.detailResolution, d);

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

                        newTerrain.terrainData.SetDetailLayer(0, 0, d, partDetail);
                    }

                    float sizeCheckX = size * xI;
                    float sizeCheckX1 = size * (xI + 1);

                    float sizeCheckY = size * yI;
                    float sizeCheckY1 = size * (yI + 1);


                    treeInstances = terrain.terrainData.treeInstances;
                    treeInstancesList.Clear();
                    for (int t = 0; t < treeInstances.Length; t++)
                    {
                        if (t % 1000 == 0)
                        {
                            bool cancel = EditorUtility.DisplayCancelableProgressBar(progressCaption,
                                progressText + " (Processing trees " + t + "/" + treeInstances.Length + ")",
                                progress + (4 / 5f + ((float)t / treeInstances.Length) / 5f) /
                                (float)_terrainManagerSettings.terrainsCount);
                            if (cancel)
                            {
                                Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
                                EditorUtility.ClearProgressBar();
                                return;
                            }
                        }

                        ti = treeInstances[t];

                        if (ti.position.x >= sizeCheckY && ti.position.x <= sizeCheckY1 &&
                            ti.position.z >= sizeCheckX && ti.position.z <= sizeCheckX1)
                        {
                            ti.position = new Vector3((ti.position.x * _terrainManagerSettings.splitSize) % 1,
                                ti.position.y, (ti.position.z * _terrainManagerSettings.splitSize) % 1);

                            treeInstancesList.Add(ti);
                        }
                    }

                    //Debug.Log("end");

                    newTerrain.terrainData.SetTreeInstances(treeInstancesList.ToArray(), true);

                    newTerrain.gameObject.AddComponent<TerrainCullingSystem>();


                    tgo.transform.position = parentPosition + new Vector3(tgo.transform.position.x + zWShift,
                        tgo.transform.position.y,
                        tgo.transform.position.z + xWShift);

                    /*
                    tgo.transform.position = new Vector3(tgo.transform.position.x + parentPosition.x,
                                                          tgo.transform.position.y + parentPosition.y,
                                                          tgo.transform.position.z + parentPosition.z
                                                         );
                                                          */

                    terrain.gameObject.SetActive(false);


                    AssetDatabase.SaveAssets();
                    GC.Collect();
                }


                EditorUtility.ClearProgressBar();
            }

            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        }
    }
}