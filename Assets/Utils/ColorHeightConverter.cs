using UnityEngine;

namespace Utils
{
    public static class ColorHeightConverter
    {
        
        /// everest = 8848
        public const float UMax = 8900; 
        
        /// sea level
        public const float UMin = 0;

        private static float Unpack(Color32 color)
        {
            return (color.r * 256f + color.g + color.b / 256f) - 32768f;
        }

        public static float RGBColor32ToHeight(Color32 color)
        {
            float height = Unpack(color);
            // normalize to [0f - 1f]
            return (height - UMin)/(UMax - UMin);
        }
    }
}
