using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Xml;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MeshSerializer))]
public class MeshSerializerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if(GUILayout.Button("Serialize XML"))
        {
            MeshSerializer tar = (MeshSerializer)target;

            if (tar != null && tar.outputMesh != null)
            {
                var outputMesh = tar.outputMesh;

                var path = EditorUtility.SaveFilePanel("Save mesh as XML", "", "out.xml", "xml");
                if (!string.IsNullOrEmpty(path))
                {
                    SaveXml(path, outputMesh);
                }
            }
        }
        if (GUILayout.Button("Serialize JSON"))
        {
            MeshSerializer tar = (MeshSerializer)target;

            if (tar != null && tar.outputMesh != null)
            {
                var outputMesh = tar.outputMesh;

                var path = EditorUtility.SaveFilePanel("Save mesh as JSON", "", "out.json", "json");
                if (!string.IsNullOrEmpty(path))
                {
                    SaveJson(path, outputMesh);
                }
            }
        }
    }

    // CPUMesh has readonly fields and no parameterless constructor. Data contracts
    // honor its [Serializable] fields; JsonUtility/XmlSerializer would omit or reject them.
    internal static void SaveXml(string path, MeshContentSerialized mesh)
    {
        var serializer = new DataContractSerializer(typeof(MeshContentSerialized));
        using (var writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true }))
        {
            serializer.WriteObject(writer, mesh);
        }
    }

    internal static void SaveJson(string path, MeshContentSerialized mesh)
    {
        var serializer = new DataContractJsonSerializer(typeof(MeshContentSerialized));
        using (var stream = File.Create(path))
        {
            serializer.WriteObject(stream, mesh);
        }
    }
}
