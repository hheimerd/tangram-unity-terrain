using UnityEngine;

namespace Utils
{
    public class TextureToTerrain
    {
        public static void ApplyHeightTexture(TerrainData terrainData, Texture2D texture)
        {
            var height = new float[terrainData.heightmapResolution, terrainData.heightmapResolution];
            var pixels = texture.GetPixels();

            for (int h = 0; h < texture.height; h++)
            {
                for (var w = 0; w < texture.width; w++)
                {
                    var color = pixels[h * texture.width + w];
                        
                    height[h, w] = ColorHeightConverter.RGBColor32ToHeight(color);

                    // TODO: Fix negative values
                    if (height[h, w] <= 0)
                        height[h, w] = height[h, w - 1];
                }
            }

            // fill the last pixel (513'th)
            for (int i = 0; i < terrainData.heightmapResolution; i++)
            {
                height[i, terrainData.heightmapResolution - 1] = height[i, terrainData.heightmapResolution - 2];
                height[terrainData.heightmapResolution - 1, i] = height[terrainData.heightmapResolution - 2, i];
            }

            terrainData.SetHeights(0, 0, height);
        }
    }
}
