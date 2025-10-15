using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace WorldStreamer2
{
    public class TerrainMapsExporter
    {
        private readonly TerrainManager _terrainManager;
        private Scene _previewScene;
        private Scene _lastScene;

        public TerrainMapsExporter(TerrainManager terrainManager)
        {
            _terrainManager = terrainManager;
        }

        public Texture ExportBaseMap(Terrain terrainBase, string terrainName, out Texture mask)
        {
            mask = null;
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


            Terrain terrain = Object.Instantiate(terrainBase);
            terrain.drawTreesAndFoliage = false;
            GameObject go = terrain.gameObject;
            EditorSceneManager.MoveGameObjectToScene(go, _previewScene);
            go.transform.position = Vector3.zero;


#if !NM_URP && !NM_HDRP
            terrain.materialTemplate = new Material(Shader.Find("NatureManufacture Shaders/Terrain/StandardAlbedo"));
            //Debug.Log(_terrainManager.ManagerSettings.ambientLightColor);
            terrain.materialTemplate.SetColor("_AmbientColor", _terrainManager.ManagerSettings.ambientLightColor);
#endif

            GameObject cameraGo = new GameObject("PreviewCamera");
            EditorSceneManager.MoveGameObjectToScene(cameraGo, _previewScene);


            Camera cam = cameraGo.AddComponent<Camera>();


            cam.rect = new Rect(0, 0, 1, 1);
            cam.orthographic = true;
            cam.depthTextureMode = DepthTextureMode.Depth;


            cam.rect = new Rect(0, 0, 1, 1);

            Bounds currentBounds = terrain.terrainData.bounds;

            cam.transform.eulerAngles = new Vector3(0, 0, 0);


            cam.transform.position = currentBounds.center + Vector3.up * currentBounds.max.y + new Vector3(0, 1, 0);


            Selection.activeGameObject = cam.gameObject;


            cam.transform.eulerAngles = new Vector3(90, 0, 0);

            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = cam.transform.position.y + 1000.0f;


            float aspectSize = terrain.terrainData.size.x / terrain.terrainData.size.z;
            cam.aspect = aspectSize;
            if (aspectSize < 1)
                aspectSize = 1;
            cam.orthographicSize = Mathf.Max((currentBounds.max.x - currentBounds.min.x) / 2.0f,
                (currentBounds.max.z - currentBounds.min.z) / 2.0f) / aspectSize;

            cam.scene = _previewScene;

            Debug.Log(aspectSize);

            RenderTexture rt = new RenderTexture(_terrainManager.ManagerSettings.basemapResolution, _terrainManager.ManagerSettings.basemapResolution, 32);
            cam.targetTexture = rt;
            Texture2D screenShot = new Texture2D(_terrainManager.ManagerSettings.basemapResolution, _terrainManager.ManagerSettings.basemapResolution, TextureFormat.ARGB32, false);

            cam.Render();

            RenderTexture.active = rt;
            screenShot.ReadPixels(
                new Rect(0, 0, _terrainManager.ManagerSettings.basemapResolution, _terrainManager.ManagerSettings.basemapResolution), 0,
                0);


            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(rt);


            Object.DestroyImmediate(cameraGo);

            // EditorSceneManager.MoveGameObjectToScene(cameraGo, EditorSceneManager.GetActiveScene());
            Object.DestroyImmediate(go);

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

            terrainBase.basemapDistance = baseMapDistance;

#if NM_URP
            screenShot = ExportTextureSmoothness(terrainBase, terrainName, 4);

            if (_terrainManager.ManagerSettings.useMaskSmoothnessURP)
            {
                Texture2D smoothnessTexture = ExportTextureSmoothness(terrainBase, terrainName, 1);
                Texture2D aoTexture = ExportTextureSmoothness(terrainBase, terrainName, 2);
                Texture2D metalicTexture = ExportTextureSmoothness(terrainBase, terrainName, 3);


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


                TextureImporter importerMask = (TextureImporter)AssetImporter.GetAtPath("Assets" + nameMask);
                importerMask.wrapMode = TextureWrapMode.Clamp;
                importerMask.streamingMipmaps = true;
                importerMask.sRGBTexture = false;
                importerMask.anisoLevel = 8;
                importerMask.SaveAndReimport();
                mask = AssetDatabase.LoadAssetAtPath<Texture>("Assets" + nameMask);
            }
            else
                mask = null;
#endif

            if (_terrainManager.ManagerSettings.useSmoothness)
            {
                Texture2D smoothnessTexture = ExportTextureSmoothness(terrainBase, terrainName);

                for (int x = 0; x < screenShot.width; x++)
                {
                    for (int y = 0; y < screenShot.height; y++)
                    {
                        Color smoothnesColor = smoothnessTexture.GetPixel(x, y);
                        Color basemapColor = screenShot.GetPixel(x, y);
                        basemapColor.a = smoothnesColor.r;

                        // screenShot.SetPixel(x, y, smoothnesColor);
                        screenShot.SetPixel(x, y, basemapColor);
                    }
                }
            }


            byte[] bytesAtlas = screenShot.EncodeToPNG();

            string name = _terrainManager.ManagerSettings.terrainPath + "/T_" + terrainName + "_BC.png";
            System.IO.File.WriteAllBytes(Application.dataPath + name, bytesAtlas);
            AssetDatabase.Refresh();

            //Debug.Log($"smoothness map name {name}");
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath("Assets" + name);
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.streamingMipmaps = true;
            importer.anisoLevel = 8;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture>("Assets" + name);
        }

        public Texture2D ExportTextureNormalmap(Terrain terrainBase, string terrainName)
        {
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


            Terrain terrain = Object.Instantiate(terrainBase);
            terrain.drawTreesAndFoliage = false;
            bool drawInstanced = terrain.drawInstanced;
            terrain.drawInstanced = false;

#if NM_URP
            terrain.materialTemplate = new Material(Shader.Find("NatureManufacture Shaders/Terrain Lit Normal"));

            terrain.materialTemplate.SetFloat("_NMNormal", 0);
#else
            terrain.materialTemplate = new Material(Shader.Find("NatureManufacture Shaders/Terrain/Standard"));
#endif

            // Debug.Log(terrain.drawInstanced);


            GameObject go = terrain.gameObject;
            EditorSceneManager.MoveGameObjectToScene(go, _previewScene);
            go.transform.position = Vector3.zero;

            GameObject cameraGo = new GameObject("PreviewCamera");
            EditorSceneManager.MoveGameObjectToScene(cameraGo, _previewScene);


            Camera cam = cameraGo.AddComponent<Camera>();


            cam.rect = new Rect(0, 0, 1, 1);
            cam.orthographic = true;
            cam.depthTextureMode = DepthTextureMode.Depth;


            cam.rect = new Rect(0, 0, 1, 1);

            Bounds currentBounds = terrain.terrainData.bounds;

            cam.transform.eulerAngles = new Vector3(0, 0, 0);


            cam.transform.position = currentBounds.center - cam.transform.forward * currentBounds.max.y * 1.1f;

            GameObject centerObject = new GameObject("Center");
            centerObject.transform.position = currentBounds.center;
            cam.transform.parent = centerObject.transform;
            centerObject.transform.eulerAngles = new Vector3(90, 0, 0);

            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = cam.transform.position.y + 1000.0f;


            float aspectSize = terrain.terrainData.size.x / terrain.terrainData.size.z;
            cam.aspect = aspectSize;
            if (aspectSize < 1)
                aspectSize = 1;
            cam.orthographicSize = Mathf.Max((currentBounds.max.x - currentBounds.min.x) / 2.0f,
                (currentBounds.max.z - currentBounds.min.z) / 2.0f) / aspectSize;

            cam.scene = _previewScene;

            RenderTexture rt = new RenderTexture(_terrainManager.ManagerSettings.basemapResolution, _terrainManager.ManagerSettings.basemapResolution, 32);
            cam.targetTexture = rt;
            Texture2D screenShot = new Texture2D(_terrainManager.ManagerSettings.basemapResolution, _terrainManager.ManagerSettings.basemapResolution, TextureFormat.ARGB32, false);

            cam.Render();

            RenderTexture.active = rt;
            screenShot.ReadPixels(
                new Rect(0, 0, _terrainManager.ManagerSettings.basemapResolution, _terrainManager.ManagerSettings.basemapResolution), 0,
                0);

            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(rt);


            ////
            //byte[] bytesAtlas = screenShot.EncodeToPNG();

            //string name = terrainManagerSettings.terrainPath + "/T_" + terrainName + "_BCRob.png";
            //System.IO.File.WriteAllBytes(Application.dataPath + name, bytesAtlas);
            ////

            cam.transform.parent = null;
            Object.DestroyImmediate(centerObject);
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

            return screenShot;
        }

        private Texture2D ExportTextureSmoothness(Terrain terrainBase, string terrainName, int type = 1)
        {
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


            Terrain terrain = Object.Instantiate(terrainBase);
            terrain.drawTreesAndFoliage = false;
            bool drawInstanced = terrain.drawInstanced;
            terrain.drawInstanced = false;


#if NM_URP
            terrain.materialTemplate = new Material(Shader.Find("NatureManufacture Shaders/Terrain Lit Normal"));

            terrain.materialTemplate.SetFloat("_NMNormal", type);
#else
            terrain.materialTemplate =
                new Material(Shader.Find("NatureManufacture Shaders/Terrain/StandardSmoothness"));
#endif


            GameObject go = terrain.gameObject;
            EditorSceneManager.MoveGameObjectToScene(go, _previewScene);
            go.transform.position = Vector3.zero;

            GameObject cameraGo = new GameObject("PreviewCamera");
            EditorSceneManager.MoveGameObjectToScene(cameraGo, _previewScene);


            Camera cam = cameraGo.AddComponent<Camera>();


            cam.rect = new Rect(0, 0, 1, 1);
            cam.orthographic = true;
            cam.depthTextureMode = DepthTextureMode.Depth;


            cam.rect = new Rect(0, 0, 1, 1);

            Bounds currentBounds = terrain.terrainData.bounds;

            cam.transform.eulerAngles = new Vector3(0, 0, 0);


            cam.transform.position = currentBounds.center - cam.transform.forward * currentBounds.max.y * 1.1f;

            GameObject centerObject = new GameObject("Center");
            centerObject.transform.position = currentBounds.center;
            cam.transform.parent = centerObject.transform;
            centerObject.transform.eulerAngles = new Vector3(90, 0, 0);

            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = cam.transform.position.y + 1000.0f;

            float aspectSize = terrain.terrainData.size.x / terrain.terrainData.size.z;
            cam.aspect = aspectSize;
            if (aspectSize < 1)
                aspectSize = 1;
            cam.orthographicSize = Mathf.Max((currentBounds.max.x - currentBounds.min.x) / 2.0f,
                (currentBounds.max.z - currentBounds.min.z) / 2.0f) / aspectSize;

            cam.scene = _previewScene;

            RenderTexture rt = new RenderTexture(_terrainManager.ManagerSettings.basemapResolution, _terrainManager.ManagerSettings.basemapResolution, 32);
            cam.targetTexture = rt;
            Texture2D screenShot = new Texture2D(_terrainManager.ManagerSettings.basemapResolution, _terrainManager.ManagerSettings.basemapResolution, TextureFormat.ARGB32, false);

            cam.Render();

            RenderTexture.active = rt;
            screenShot.ReadPixels(
                new Rect(0, 0, _terrainManager.ManagerSettings.basemapResolution, _terrainManager.ManagerSettings.basemapResolution), 0,
                0);

            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(rt);


            //byte[] bytesAtlas = screenShot.EncodeToPNG();

            //string name = terrainManagerSettings.terrainPath + "/T_" + terrainName + "_BC.png";
            //System.IO.File.WriteAllBytes(Application.dataPath + name, bytesAtlas);

            cam.transform.parent = null;
            Object.DestroyImmediate(centerObject);
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

            return screenShot;
        }
    }
}