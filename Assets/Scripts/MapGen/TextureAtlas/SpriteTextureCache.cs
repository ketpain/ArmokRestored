using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using DFHack;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Disposable disk cache of generated creature graphics, never live DF state.</summary>
internal sealed class SpriteTextureCache
{
    private const int Magic = 0x41565343;
    // Bump when sprite resampling, bevel generation, or the stored pixel format changes.
    private const int Version = 1;
    private const long MaxPayloadBytes = 256L * 1024 * 1024;
    private readonly byte[] sourceHash;
    internal string CachePath { get; private set; }

    internal SpriteTextureCache(string sourcePath, string pageName, byte[] sourceBytes, string directory = null)
    {
        using (var hash = SHA256.Create()) sourceHash = hash.ComputeHash(sourceBytes);
        byte[] identity;
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(Path.GetFullPath(sourcePath));
                writer.Write(pageName);
            }
            using (var hash = SHA256.Create()) identity = hash.ComputeHash(stream.ToArray());
        }
        directory = directory ?? Path.Combine(Application.persistentDataPath, "Cache", "CreatureSprites");
        // One slot per source/page; edits replace old content instead of accumulating versions.
        CachePath = Path.Combine(directory, BitConverter.ToString(identity).Replace("-", "") + ".bin");
    }

    internal byte[] Fingerprint(int tileWidth, int tileHeight, int pageHeight, IList<DFCoord2d> coordinates)
    {
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(Version);
                writer.Write(sourceHash);
                writer.Write(Application.unityVersion);
                writer.Write((int)QualitySettings.activeColorSpace);
                writer.Write((int)SystemInfo.graphicsDeviceType);
                writer.Write(tileWidth);
                writer.Write(tileHeight);
                writer.Write(pageHeight);
                writer.Write(coordinates.Count);
                // The selected tiles and their order come from the current DF raws.
                foreach (var coordinate in coordinates)
                {
                    writer.Write(coordinate.x);
                    writer.Write(coordinate.y);
                }
            }
            using (var hash = SHA256.Create()) return hash.ComputeHash(stream.ToArray());
        }
    }

    internal bool TryLoad(byte[] fingerprint, int width, int height, int depth,
        out Texture2DArray colors, out Texture2DArray normals)
    {
        colors = null;
        normals = null;
        Texture2DArray loadedColors = null;
        Texture2DArray loadedNormals = null;
        bool success = false;
        int mipCount = MipCount(width, height);
        long payloadBytes = PayloadBytes(width, height, depth);
        if (payloadBytes > MaxPayloadBytes) return false;
        try
        {
            using (var stream = File.OpenRead(CachePath))
            using (var reader = new BinaryReader(stream))
            using (var hash = SHA256.Create())
            {
                if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version ||
                    !Equal(reader.ReadBytes(32), fingerprint) ||
                    reader.ReadInt32() != width || reader.ReadInt32() != height ||
                    reader.ReadInt32() != depth || reader.ReadInt32() != mipCount ||
                    stream.Length - stream.Position != payloadBytes + 32)
                    return false;

                loadedColors = new Texture2DArray(width, height, depth, TextureFormat.ARGB32, true);
                loadedNormals = new Texture2DArray(width, height, depth, TextureFormat.ARGB32, true, true);
                ReadPixels(reader, hash, loadedColors);
                ReadPixels(reader, hash, loadedNormals);
                hash.TransformFinalBlock(new byte[0], 0, 0);
                if (!Equal(hash.Hash, reader.ReadBytes(32))) return false;
                // Mips are cached too: regenerate neither the colors nor their normal maps.
                loadedColors.Apply(false, false);
                loadedNormals.Apply(false, false);
                colors = loadedColors;
                normals = loadedNormals;
                success = true;
                return true;
            }
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        finally
        {
            if (!success)
            {
                Release(loadedColors);
                Release(loadedNormals);
            }
        }
    }

    internal bool TrySave(byte[] fingerprint, Texture2DArray colors, Texture2DArray normals)
    {
        if (PayloadBytes(colors.width, colors.height, colors.depth) > MaxPayloadBytes) return false;
        string temporaryPath = CachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            using (var hash = SHA256.Create())
            {
                writer.Write(Magic);
                writer.Write(Version);
                writer.Write(fingerprint);
                writer.Write(colors.width);
                writer.Write(colors.height);
                writer.Write(colors.depth);
                writer.Write(colors.mipmapCount);
                WritePixels(writer, hash, colors);
                WritePixels(writer, hash, normals);
                hash.TransformFinalBlock(new byte[0], 0, 0);
                writer.Write(hash.Hash);
            }
            if (File.Exists(CachePath)) File.Replace(temporaryPath, CachePath, null);
            else File.Move(temporaryPath, CachePath);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void ReadPixels(BinaryReader reader, HashAlgorithm hash, Texture2DArray texture)
    {
        for (int slice = 0; slice < texture.depth; slice++)
            for (int mip = 0; mip < texture.mipmapCount; mip++)
            {
                int count = Math.Max(1, texture.width >> mip) * Math.Max(1, texture.height >> mip);
                var bytes = reader.ReadBytes(count * 4);
                if (bytes.Length != count * 4) throw new EndOfStreamException();
                hash.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
                var pixels = new Color32[count];
                for (int i = 0; i < count; i++)
                    pixels[i] = new Color32(bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2], bytes[i * 4 + 3]);
                texture.SetPixels32(pixels, slice, mip);
            }
    }

    private static void WritePixels(BinaryWriter writer, HashAlgorithm hash, Texture2DArray texture)
    {
        for (int slice = 0; slice < texture.depth; slice++)
            for (int mip = 0; mip < texture.mipmapCount; mip++)
            {
                var pixels = texture.GetPixels32(slice, mip);
                var bytes = new byte[pixels.Length * 4];
                for (int i = 0; i < pixels.Length; i++)
                {
                    bytes[i * 4] = pixels[i].r;
                    bytes[i * 4 + 1] = pixels[i].g;
                    bytes[i * 4 + 2] = pixels[i].b;
                    bytes[i * 4 + 3] = pixels[i].a;
                }
                hash.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
                writer.Write(bytes);
            }
    }

    private static long PayloadBytes(int width, int height, int depth)
    {
        long pixels = 0;
        for (int mip = 0; mip < MipCount(width, height); mip++)
            pixels += (long)Math.Max(1, width >> mip) * Math.Max(1, height >> mip);
        return pixels * depth * 4 * 2;
    }

    private static int MipCount(int width, int height)
    {
        int count = 1;
        for (int size = Math.Max(width, height); size > 1; size >>= 1) count++;
        return count;
    }

    private static bool Equal(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static void Release(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Object.Destroy(value);
        else Object.DestroyImmediate(value);
    }
}
