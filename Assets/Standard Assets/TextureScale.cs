// Only works on textures that are marked readable and support SetPixels.

using System;
using System.Threading.Tasks;
using UnityEngine;

public static class TextureScale
{
    // Small sprite jobs cost much less than scheduling workers. Large images use
    // a bounded number of ThreadPool tasks, with all pixel state local to the call.
    private const int ParallelPixelThreshold = 256 * 1024;

    public static void Point(Texture2D tex, int newWidth, int newHeight)
    {
        ScaleTexture(tex, newWidth, newHeight, false);
    }

    public static void Bilinear(Texture2D tex, int newWidth, int newHeight)
    {
        ScaleTexture(tex, newWidth, newHeight, true);
    }

    private static void ScaleTexture(Texture2D tex, int newWidth, int newHeight, bool useBilinear)
    {
        if (tex == null) throw new ArgumentNullException(nameof(tex));
        var pixels = ThreadedScale(tex.GetPixels(), tex.width, tex.height, newWidth, newHeight, useBilinear);
        tex.Reinitialize(newWidth, newHeight);
        tex.SetPixels(pixels);
        tex.Apply();
    }

    /// <summary>Resizes CPU pixels. Safe for independent concurrent calls; Unity texture access stays with the caller.</summary>
    public static Color[] ThreadedScale(Color[] source, int oldWidth, int oldHeight, int newWidth, int newHeight, bool useBilinear)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (oldWidth <= 0) throw new ArgumentOutOfRangeException(nameof(oldWidth));
        if (oldHeight <= 0) throw new ArgumentOutOfRangeException(nameof(oldHeight));
        if (newWidth <= 0) throw new ArgumentOutOfRangeException(nameof(newWidth));
        if (newHeight <= 0) throw new ArgumentOutOfRangeException(nameof(newHeight));
        if ((long)oldWidth * oldHeight != source.Length)
            throw new ArgumentException("Source pixel count does not match its dimensions.", nameof(source));

        var output = new Color[checked(newWidth * newHeight)];
        // Preserve the original sample positions, including its same-size
        // bilinear resampling, so existing sprites retain exactly the same pixels.
        float ratioX = useBilinear ? 1.0f / ((float)newWidth / (oldWidth - 1)) : (float)oldWidth / newWidth;
        float ratioY = useBilinear ? 1.0f / ((float)newHeight / (oldHeight - 1)) : (float)oldHeight / newHeight;

        Action<int, int> scaleRows = (start, end) =>
        {
            for (int y = start; y < end; y++)
            {
                int destinationRow = y * newWidth;
                if (useBilinear)
                {
                    int yFloor = (int)Mathf.Floor(y * ratioY);
                    int row1 = yFloor * oldWidth;
                    int row2 = Math.Min(yFloor + 1, oldHeight - 1) * oldWidth;
                    float yLerp = y * ratioY - yFloor;
                    for (int x = 0; x < newWidth; x++)
                    {
                        int xFloor = (int)Mathf.Floor(x * ratioX);
                        int xNext = Math.Min(xFloor + 1, oldWidth - 1);
                        float xLerp = x * ratioX - xFloor;
                        output[destinationRow + x] = ColorLerpUnclamped(
                            ColorLerpUnclamped(source[row1 + xFloor], source[row1 + xNext], xLerp),
                            ColorLerpUnclamped(source[row2 + xFloor], source[row2 + xNext], xLerp), yLerp);
                    }
                }
                else
                {
                    int sourceRow = (int)(ratioY * y) * oldWidth;
                    for (int x = 0; x < newWidth; x++)
                        output[destinationRow + x] = source[(int)(sourceRow + ratioX * x)];
                }
            }
        };

        int workers = Math.Min(8, Math.Min(Environment.ProcessorCount, newHeight / 32));
        if (output.Length < ParallelPixelThreshold || workers < 2)
            scaleRows(0, newHeight);
        else
            Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers },
                worker => scaleRows(worker * newHeight / workers, (worker + 1) * newHeight / workers));
        return output;
    }

    private static Color ColorLerpUnclamped(Color c1, Color c2, float value)
    {
        return new Color(c1.r + (c2.r - c1.r) * value,
                         c1.g + (c2.g - c1.g) * value,
                         c1.b + (c2.b - c1.b) * value,
                         c1.a + (c2.a - c1.a) * value);
    }
}
