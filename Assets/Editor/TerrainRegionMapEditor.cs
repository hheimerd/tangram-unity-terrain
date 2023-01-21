using UnityEngine;
using System.Collections.Generic;
using UnityEditor;

namespace Mapzen.Unity.Editor
{
    [CustomEditor(typeof(TerrainRegionMap))]
    public class TerrainRegionMapEditor : UnityEditor.Editor
    {
        private TerrainRegionMap map;

        void OnEnable()
        {
            this.map = (TerrainRegionMap)target;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("AllowedOrigin"));

            GUILayout.BeginHorizontal();
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ApiKey"));
            if (GUILayout.Button("Get an API key", EditorStyles.miniButtonRight))
            {
                Application.OpenURL("https://developers.nextzen.org/");
            }
            GUILayout.EndHorizontal();
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Area"), true);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("UnitsPerMeter"));

            EditorGUILayout.PropertyField(serializedObject.FindProperty("RegionName"));

            bool valid = map.IsValid();

            if (GUILayout.Button("Download"))
            {
                map.LogWarnings();

                if (valid)
                {
                    map.DownloadTilesAsync();
                }
                else
                {
                    map.LogErrors();
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
