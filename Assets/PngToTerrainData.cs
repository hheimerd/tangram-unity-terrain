using UnityEditor;
using UnityEngine;

public class PngToTerrainData : MonoBehaviour
{
    [SerializeField] private Terrain terrain;
    [SerializeField] private Texture2D texture;

    const float UMax = 8900; // everest = 8848
    const float  UMin = 0; // sea level
    
    private float Unpack(Color color)
    {
        return (color.r * 256f + color.g + color.b / 256f) * 255f - 32768f;
    }
    
    public void Apply()
    {
        var heights = new float[texture.height,texture.width];
        var pixels = texture.GetPixels();
        terrain.terrainData.heightmapResolution = texture.width;
       
        
        for (int h = 0; h < texture.height; h++)
        {
            for (var w = 0; w < texture.width; w++)
            {
                var color = pixels[h * texture.width + w];

                float height = Unpack(color);
                // normalize to [0f - 1f]
                float normalized = (height - UMin)/(UMax - UMin);
                
                heights[h,w] = normalized;
            }
        }
                    
        terrain.terrainData.SetHeights(0,0, heights);
    }

}

[CustomEditor(typeof(PngToTerrainData))] 
public class PngToTerrainDataEditor: UnityEditor.Editor
{
    private PngToTerrainData terrain;

    void OnEnable()
    {
        this.terrain = (PngToTerrainData)target;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        
        EditorGUILayout.PropertyField(serializedObject.FindProperty("terrain"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("texture"));

        if (GUILayout.Button("Apply"))
        {
            terrain.Apply();
        }
    }
}
