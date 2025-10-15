// /**
//  * Created by Pawel Homenko on  11/2023
//  */

using UnityEngine;

namespace WorldStreamer2
{
    public static class TerrainManagerUtils
    {
        public static Texture2D HeightMapToNormal(Texture2D sourceImage, float strength = 1f)
        {
            //Mathf.Clamp(strength, 0.0f, 10.0f);
            int w = sourceImage.width;
            int h = sourceImage.height;
            Texture2D normal = new Texture2D(w, h);
            Color[] currentColors = null;
            Color[] lColors = null;
            Color[] rColors = null;
            float sampleL;
            float sampleR;
            float sampleU;
            float sampleD;
            float xVector, yVector;

            for (int x = 0; x < w; x++)
            {
                if (currentColors != null) lColors = currentColors;
                currentColors = rColors ?? sourceImage.GetPixels(x, 0, 1, h);
                lColors ??= currentColors;
                rColors = x < w - 1 ? sourceImage.GetPixels(x + 1, 0, 1, h) : currentColors;

                for (int y = 0; y < currentColors.Length; y++)
                {
                    sampleL = lColors[y].r * strength;
                    sampleR = rColors[y].r * strength;
                    if (y < h - 1) sampleU = currentColors[y + 1].r * strength;
                    else sampleU = currentColors[y].r * strength;
                    if (y > 0) sampleD = currentColors[y - 1].r * strength;
                    else sampleD = currentColors[y].r * strength;

                    xVector = Mathf.Clamp01((((sampleL - sampleR) + 1) * 0.5f));
                    yVector = Mathf.Clamp01((((sampleD - sampleU) + 1) * 0.5f));
                    Color col = new Color(xVector, yVector, 1f, 1f);
                    normal.SetPixel(x, y, col);
                }
            }

            normal.Apply();
            return normal;
        }

        public static Texture2D GetNormalMap(float[,] rawHeights, float str = 2.0f)
        {
            int width = rawHeights.GetLength(0);
            int height = rawHeights.GetLength(0);


            Texture2D normal = new Texture2D(width, height, TextureFormat.ARGB32, true);

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    normal.SetPixel(x, y, new Color(rawHeights[y, x], rawHeights[y, x], rawHeights[y, x]));
                }
            }

            normal.Apply();

            normal = HeightMapToNormal(normal, str / 20f);

            normal = Resize(normal, Mathf.ClosestPowerOfTwo(normal.width), Mathf.ClosestPowerOfTwo(normal.width));

            normal.Apply(true);

            return normal;
        }

        public static void CalculateTerrainMaxMinHeight(Terrain terrain, float maxHeightTerrain, float minHeightTerrain, TerrainManager terrainManager)
        {
            int heightMapResolution = terrain.terrainData.heightmapResolution;
            float[,] rawHeights = terrain.terrainData.GetHeights(0, 0, heightMapResolution, heightMapResolution);
            float height;


            float min = float.MaxValue;
            float max = float.MinValue;


            for (int y = 0; y < heightMapResolution; y++)
            {
                for (int x = 0; x < heightMapResolution; x++)
                {
                    rawHeights[y, x] = Mathf.Clamp01(rawHeights[y, x]);
                    height = rawHeights[y, x];


                    if (height > max)
                        max = height;
                    if (height < min)
                        min = height;
                }
            }

            if (max > terrainManager.MaxHeightTerrain) terrainManager.MaxHeightTerrain = max;
            if (min < terrainManager.MinHeightTerrain) terrainManager.MinHeightTerrain = min;
        }

        public static Texture2D Resize(Texture2D texture2D, int targetX, int targetY)
        {
            RenderTexture rt = new RenderTexture(targetX, targetY, 24);
            RenderTexture.active = rt;
            Graphics.Blit(texture2D, rt);
            Texture2D result = new Texture2D(targetX, targetY);
            result.ReadPixels(new Rect(0, 0, targetX, targetY), 0, 0);
            result.Apply();
            return result;
        }

        public static Color Overlay(Color baseCol, Color blendCol)
        {
            Color outColor = new Color
            {
                a = 1
            };
            if (baseCol is {r: < 0.5f, g: < 0.5f, b: < 0.5f})
            {
                outColor.r = 2 * baseCol.r * blendCol.r;
                outColor.g = 2 * baseCol.g * blendCol.g;
                outColor.b = 2 * baseCol.b * blendCol.b;
            }
            else
            {
                outColor.r = 1 - 2 * (1 - baseCol.r) * (1 - blendCol.r);
                outColor.g = 1 - 2 * (1 - baseCol.g) * (1 - blendCol.g);
                outColor.b = 1 - 2 * (1 - baseCol.b) * (1 - blendCol.b);
            }

            return outColor;
        }

        public static Color LinearLightAddSub(Color baseCol, Color blendCol)
        {
            Color outColor = blendCol;
            outColor.a = 1;

            if (blendCol.r > 0.5f)
            {
                outColor.r = baseCol.r + (blendCol.r * 2 - 1) * 0.5f;
            }
            else
            {
                outColor.r = baseCol.r - (1 - blendCol.r * 2) * 0.5f;
            }

            if (blendCol.g > 0.5f)
            {
                outColor.g = baseCol.g + (blendCol.g * 2 - 1) * 0.5f;
            }
            else
            {
                outColor.g = baseCol.g - (1 - blendCol.g * 2) * 0.5f;
            }

            outColor.b = blendCol.b;
            if (blendCol.b > 0.5f)
            {
                outColor.b = baseCol.b + (blendCol.b * 2 - 1) * 0.5f;
            }
            else
            {
                outColor.b = baseCol.b - (1 - blendCol.b * 2) * 0.5f;
            }

            return outColor;
        }
    }
}