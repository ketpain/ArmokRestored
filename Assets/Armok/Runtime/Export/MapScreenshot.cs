using System;
using System.IO;
using UnityEngine;

public sealed class MapScreenshot
{
    private int screenshotCount;

    public void Capture(bool minimapOnly)
    {
        var minimap = Minimap.Instance;
        if (minimapOnly && (minimap == null || minimap.texture == null))
        {
            Debug.LogWarning("No minimap is available to capture. Use F9 for a view screenshot.");
            return;
        }

        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Application.productName, "Screenshots");
            string filename = NextFilename(directory);
            Debug.Log("Saving screenshot to: " + filename);
            if (minimapOnly)
                SaveRenderTexture(minimap.texture, filename);
            else
                ScreenCapture.CaptureScreenshot(filename);
        }
        catch (IOException error)
        {
            Debug.LogError("Could not save screenshot: " + error.Message);
        }
        catch (UnauthorizedAccessException error)
        {
            Debug.LogError("Could not save screenshot: " + error.Message);
        }
    }

    internal string NextFilename(string directory)
    {
        Directory.CreateDirectory(directory);
        string filename;
        do
        {
            screenshotCount++;
            filename = Path.Combine(directory, "screenshot" + screenshotCount + ".png");
        } while (File.Exists(filename));
        return filename;
    }

    internal static void SaveRenderTexture(RenderTexture source, string filename)
    {
        var previous = RenderTexture.active;
        Texture2D photo = null;
        try
        {
            RenderTexture.active = source;
            photo = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
            photo.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            File.WriteAllBytes(filename, photo.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            if (photo != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(photo);
                else UnityEngine.Object.DestroyImmediate(photo);
            }
        }
    }
}
