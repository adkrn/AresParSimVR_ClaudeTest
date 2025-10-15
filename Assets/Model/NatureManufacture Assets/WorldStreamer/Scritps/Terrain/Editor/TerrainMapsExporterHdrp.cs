using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
#if NM_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WorldStreamer2
{
    public class TerrainMapsExporterHdrp
    {
        private static readonly int SMetallicMap = Shader.PropertyToID("_metallicMap");
        private static readonly int SEnableHeightBlend = Shader.PropertyToID("_EnableHeightBlend");
        private static readonly int SHeightTransition = Shader.PropertyToID("_HeightTransition");

        private enum TextureType
        {
            BaseMap = -1,
            NormalMap = 0,
            MetallicMap = 1,
            AOMap = 2,
            SmoothnessMap = 3
        }

        private readonly TerrainManager _terrainManager;


        private Scene _previewScene;
        private Scene _lastScene;

        public TerrainMapsExporterHdrp(TerrainManager terrainManager)
        {
            _terrainManager = terrainManager;
        }

        public Texture ExportBaseMap(Terrain terrainBase, string terrainName)
        {
            Texture2D map = GenerateMap(terrainBase, TextureType.BaseMap);

            byte[] bytesAtlas = map.EncodeToPNG();

            string name = _terrainManager.ManagerSettings.terrainPath + "/T_" + terrainName + "_BC.png";
            System.IO.File.WriteAllBytes(Application.dataPath + name, bytesAtlas);
            AssetDatabase.Refresh();

            //Debug.Log("Assets" + name);
            var importer = (TextureImporter)AssetImporter.GetAtPath("Assets" + name);
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.streamingMipmaps = true;
            importer.anisoLevel = 8;
            importer.SaveAndReimport();

            GC.Collect();


            return AssetDatabase.LoadAssetAtPath<Texture>("Assets" + name);
        }

        private Texture2D GenerateMap(Terrain terrainBase, TextureType textureType)
        {
            float baseMapDistance = terrainBase.basemapDistance;

            terrainBase.basemapDistance = _terrainManager.ManagerSettings.useBaseMap ? 20000 : 0;


            _previewScene = EditorSceneManager.NewPreviewScene();

            Material sky = RenderSettings.skybox;
            float ambient = RenderSettings.ambientIntensity;
            AmbientMode ambientMode = RenderSettings.ambientMode;
            Color ambientColor = RenderSettings.ambientLight;

            if (SceneView.lastActiveSceneView.camera != null)
            {
                _lastScene = SceneView.lastActiveSceneView.camera.scene;
                SceneView.lastActiveSceneView.camera.scene = _previewScene;

                RenderSettings.skybox = null;
                RenderSettings.ambientIntensity = 0;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = _terrainManager.ManagerSettings.ambientLightColor;
            }

            var light = new GameObject("lightNM");
            SceneManager.MoveGameObjectToScene(light, _previewScene);
            light.transform.eulerAngles = new Vector3(90, 0, 0);

            SetHdLightHdrp(light);

            Terrain terrain = Object.Instantiate(terrainBase);
            terrain.drawTreesAndFoliage = false;
            bool drawInstanced = terrain.drawInstanced;
            terrain.drawInstanced = false;


            bool blend = terrain.materialTemplate.HasProperty(SEnableHeightBlend) &&
                         terrain.materialTemplate.GetFloat(SEnableHeightBlend) > 0;
            float heightTransition = 0;
            if (blend)
            {
                heightTransition = terrain.materialTemplate.GetFloat(SHeightTransition);
            }

            terrain.materialTemplate = new Material(Shader.Find("NatureManufacture Shaders/TerrainLitNM Normal"));

            terrain.materialTemplate.SetFloat(SMetallicMap, (int)textureType);

#if NM_HDRP
            if (blend)
            {
                CoreUtils.SetKeyword(terrain.materialTemplate, "_TERRAIN_BLEND_HEIGHT", true);
                terrain.materialTemplate.SetFloat(SHeightTransition, heightTransition);
            }
#endif


            GameObject go = terrain.gameObject;
            SceneManager.MoveGameObjectToScene(go, _previewScene);
            go.transform.position = Vector3.zero;

            var cameraGo = new GameObject("PreviewCamera");
            SceneManager.MoveGameObjectToScene(cameraGo, _previewScene);


            var cam = cameraGo.AddComponent<Camera>();

            AddCameraDataHdrp(cameraGo);


            cam.rect = new Rect(0, 0, 1, 1);
            cam.orthographic = true;
            cam.depthTextureMode = DepthTextureMode.Depth;


            cam.rect = new Rect(0, 0, 1, 1);

            Bounds currentBounds = terrain.terrainData.bounds;

            cam.transform.eulerAngles = new Vector3(0, 0, 0);


            cam.transform.position = currentBounds.center - cam.transform.forward * (currentBounds.max.y + 300);

            var centerObject = new GameObject("Center");

            CreateVolumeHdrp(centerObject);


            SceneManager.MoveGameObjectToScene(centerObject, _previewScene);

            centerObject.transform.position = currentBounds.center;
            cam.transform.parent = centerObject.transform;
            centerObject.transform.eulerAngles = new Vector3(90, 0, 0);

            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = cam.transform.position.y + 300.0f;

            float aspectSize = terrain.terrainData.size.x / terrain.terrainData.size.z;
            cam.aspect = aspectSize;
            if (aspectSize < 1)
                aspectSize = 1;
            cam.orthographicSize = Mathf.Max((currentBounds.max.x - currentBounds.min.x) / 2.0f,
                (currentBounds.max.z - currentBounds.min.z) / 2.0f) / aspectSize;

            cam.scene = _previewScene;

            var rt = new RenderTexture(_terrainManager.ManagerSettings.basemapResolution, _terrainManager.ManagerSettings.basemapResolution, 32);
            cam.targetTexture = rt;
            var map = new Texture2D(_terrainManager.ManagerSettings.basemapResolution, _terrainManager.ManagerSettings.basemapResolution, TextureFormat.ARGB32, false);

            cam.Render();

            RenderTexture.active = rt;
            map.ReadPixels(
                new Rect(0, 0, _terrainManager.ManagerSettings.basemapResolution, _terrainManager.ManagerSettings.basemapResolution), 0,
                0);

            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(rt);


            cam.transform.parent = null;
            Object.DestroyImmediate(centerObject);
            Object.DestroyImmediate(terrain);
            Object.DestroyImmediate(cameraGo);

            EditorUtility.SetDirty(_terrainManager);
            AssetDatabase.Refresh();

            if (_previewScene != null)
            {
                EditorSceneManager.ClosePreviewScene(_previewScene);
                SceneView.lastActiveSceneView.camera.scene = _lastScene;
                RenderSettings.skybox = sky;

                RenderSettings.ambientIntensity = ambient;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambientColor;
            }

            terrainBase.drawInstanced = drawInstanced;
            terrainBase.basemapDistance = baseMapDistance;
            return map;
        }

        public Texture ExportMask(Terrain terrainBase, string terrainName)
        {
            if (_terrainManager.ManagerSettings.useSmoothness)
            {
                Texture2D metalicTexture = GenerateMap(terrainBase, TextureType.MetallicMap);
                Texture2D aoTexture = GenerateMap(terrainBase, TextureType.AOMap);
                Texture2D smoothnessTexture = GenerateMap(terrainBase, TextureType.SmoothnessMap);


                for (int x = 0; x < metalicTexture.width; x++)
                {
                    for (int y = 0; y < metalicTexture.height; y++)
                    {
                        Color metalicColor = metalicTexture.GetPixel(x, y);
                        Color aoColor = aoTexture.GetPixel(x, y);
                        Color smoothnesColor = smoothnessTexture.GetPixel(x, y);

                        metalicColor.g = aoColor.r;
                        metalicColor.b = 0;
                        metalicColor.a = smoothnesColor.r;

                        metalicTexture.SetPixel(x, y, metalicColor);
                    }
                }

                byte[] byteMask = metalicTexture.EncodeToPNG();

                string nameMask = _terrainManager.ManagerSettings.terrainPath + "/T_" + terrainName + "_M.png";
                System.IO.File.WriteAllBytes(Application.dataPath + nameMask, byteMask);
                AssetDatabase.Refresh();


                var importerMask = (TextureImporter)AssetImporter.GetAtPath("Assets" + nameMask);
                importerMask.wrapMode = TextureWrapMode.Clamp;
                importerMask.streamingMipmaps = true;
                importerMask.sRGBTexture = false;
                importerMask.anisoLevel = 8;
                importerMask.SaveAndReimport();

                return AssetDatabase.LoadAssetAtPath<Texture>("Assets" + nameMask);
            }
            else
                return null;
        }

        private static void CreateVolumeHdrp(GameObject centerObject)
        {
#if NM_HDRP
            var volumeObject = new GameObject("Volume")
            {
                transform =
                {
                    parent = centerObject.transform,
                    localPosition = Vector3.zero
                }
            };
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = profile;
            volume.priority = 100;
            volume.blendDistance = 0;
            volume.weight = 1;
            var visualEnvironment = profile.Add<VisualEnvironment>();
            visualEnvironment.skyType.overrideState = true;
            visualEnvironment.skyType.value = 0;
#endif
        }

        private static void SetHdLightHdrp(GameObject light)
        {
#if NM_HDRP
#if UNITY_6000
            var hdLight = light.AddHDLight(LightType.Directional);
#else
            var hdLight = light.AddComponent<HDAdditionalLightData>();
#endif
            //hdLight.intensity = 6.283186f;
            //hdLight.GetComponent<Light>().intensity = 130000;
            hdLight.GetComponent<Light>().intensity = 3.3f; //3.14f;

            hdLight.EnableColorTemperature(false);

#endif
        }

        private static void AddCameraDataHdrp(GameObject cameraGo)
        {
#if NM_HDRP
            var cameraData = cameraGo.AddComponent<HDAdditionalCameraData>();
            cameraData.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;

            FrameSettings frameSettings = cameraData.renderingPathCustomFrameSettings;
            cameraData.customRenderingSettings = true;

            FrameSettingsOverrideMask frameSettingsOverrideMask =
                cameraData.renderingPathCustomFrameSettingsOverrideMask;


            frameSettingsOverrideMask.mask[(uint)FrameSettingsField.Postprocess] = true;
            frameSettings.SetEnabled(FrameSettingsField.Postprocess, false);

            frameSettingsOverrideMask.mask[(uint)FrameSettingsField.ExposureControl] = true;
            frameSettings.SetEnabled(FrameSettingsField.ExposureControl, false);

            frameSettingsOverrideMask.mask[(uint)FrameSettingsField.DirectSpecularLighting] = true;
            frameSettings.SetEnabled(FrameSettingsField.DirectSpecularLighting, false);

            frameSettingsOverrideMask.mask[(uint)FrameSettingsField.SkyReflection] = true;
            frameSettings.SetEnabled(FrameSettingsField.SkyReflection, false);

            cameraData.renderingPathCustomFrameSettings = frameSettings;
            cameraData.renderingPathCustomFrameSettingsOverrideMask = frameSettingsOverrideMask;
#endif
        }

        public Texture2D ExportTextureNormalmap(Terrain terrainBase)
        {
            return GenerateMap(terrainBase, TextureType.NormalMap);
        }
    }
}