using System;
using System.IO;
using MaterialStore;
using UnityEditor;
using UnityEngine;

namespace Armok.Editor
{
    /// <summary>Fresh-checkout preparation usable without a running DF process.</summary>
    public static class ProjectValidation
    {
        [MenuItem("Mytools/Validation/Prepare Content")]
        public static void PrepareContent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Prepare content outside Play Mode.");

            MaterialCollector.BuildMaterialCollection();
            AssetDatabase.SaveAssets();
            var shapes = Resources.Load<Texture2DArray>("shapeTextures");
            var patterns = Resources.Load<Texture2DArray>("patternTextures");
            var definitions = Resources.Load<MaterialCollection>("materialDefinitions");
            if (shapes == null || patterns == null || definitions == null ||
                shapes.depth == 0 || patterns.depth == 0 || definitions.textures.Count == 0)
                throw new InvalidOperationException("Required material resources were not generated.");

            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/project-validation.json", JsonUtility.ToJson(new PreparationReport
            {
                unityVersion = Application.unityVersion,
                buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                shapeSlices = shapes.depth,
                patternSlices = patterns.depth,
                materialDefinitions = definitions.textures.Count
            }, true));
            Debug.Log("Project content preparation passed. See Logs/project-validation.json.");
        }

        [Serializable]
        private class PreparationReport
        {
            public string unityVersion;
            public string buildTarget;
            public int shapeSlices;
            public int patternSlices;
            public int materialDefinitions;
        }
    }
}
