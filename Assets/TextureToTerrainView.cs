using System;
using UnityEditor;
using UnityEngine;
using Utils;

public class TextureToTerrainView : MonoBehaviour
{
    [SerializeField] private Terrain terrain;
    [SerializeField] private Texture2D texture;
    
    public void Apply()
    {
        TextureToTerrain.ApplyHeightTexture(terrain.terrainData, texture);
    }
}

[CustomEditor(typeof(TextureToTerrainView))]
public class PngToTerrainViewEditor : UnityEditor.Editor
{
    private TextureToTerrainView terrain;

    void OnEnable()
    {
        this.terrain = (TextureToTerrainView) target;
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

        serializedObject.ApplyModifiedProperties();
    }
}
