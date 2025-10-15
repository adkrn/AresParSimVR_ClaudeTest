using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldStreamer2
{
    public class TerrainManagerSettings : MonoBehaviour
    {
        public int splitSize = 2;

        public enum SplitterType
        {
            CPU,
            GPU
        }

        public SplitterType splitterType = SplitterType.CPU;

        public string terrainsDataPath = "/NatureManufacture Assets/WorldStreamer/TerrainsData/";
        public bool addTerrainCulling = true;
        public float trainglesTreesMax = 65000;


        public int basemapResolution = 512;
        public bool useTerrainNormal = true;
        public int terrainNormalDetails = 4;
        public float terrainNormalStrength = 1;
        public bool useTextureNormal = true;

        public bool useSmoothness = true;
        public int terrainsCount = 16;
        public int terrainLod = 3;

        public bool useBaseMap = true;

        public bool addVerticesDown = true;
        public float verticesDownDistance = 5;

        public float yOffset = -0.4f;

        public string terrainPrefixName = "LOD1_";
        public Color ambientLightColor = new Color(1.1f, 1.1f, 1.1f);
        public string terrainPath = "/NatureManufacture Assets/WorldStreamer/TerrainMeshes/";
        public List<TerrainTrees> terrainTrees = new List<TerrainTrees>();

        public ColorSpace colorSpaceLast = ColorSpace.Uninitialized;

        //TerrainSettings 
        public bool showBasicTerrainSettings = false;
        public int groupingID = 0;
        public bool allowAutoConnect = true;
        public bool drawHeightmap = true;
        public bool drawInstanced = true;
        public float heightmapPixelError = 5;
        public float basemapDistance = 1000;
        public ShadowCastingMode shadowCastingMode = ShadowCastingMode.TwoSided;
        public ReflectionProbeUsage reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        public Material materialTemplate = null;

        public bool showTreeAndDetailSettings = false;
        public bool drawTreesAndFoliage = true;
        public bool bakeLightProbesForTrees = true;
        public bool deringLightProbesForTrees = true;

        public bool preserveTreePrototypeLayers = false;
        public float detailObjectDistance = 150;
        public float detailObjectDensity = 1;
        public float treeDistance = 2000;
        public float treeBillboardDistance = 50;
        public float treeCrossFadeLength = 5;
        public int treeMaximumFullLODCount = 50;

        public bool showGrassWindSettings = false;
        public float wavingGrassStrength = 0.5f;
        public float wavingGrassSpeed = 0.5f;
        public float wavingGrassAmount = 0.167f;
        public Color wavingGrassTint = new Color(1, 1, 1, 0);
        public bool useMaskSmoothnessURP = true;
    }

    [Serializable]
    public class TerrainTrees
    {
        public GameObject tree = null;
        public bool active = true;
    }
}