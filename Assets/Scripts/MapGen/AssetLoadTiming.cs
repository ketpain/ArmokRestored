using System;
using System.Diagnostics;
using UnityEngine;

/// <summary>Wall-clock loading times, including frames yielded to keep the UI responsive.</summary>
internal sealed class AssetLoadTiming : IDisposable
{
    private readonly string stage;
    private readonly Stopwatch watch = Stopwatch.StartNew();

    public AssetLoadTiming(string stage)
    {
        this.stage = stage;
    }

    public void Dispose()
    {
        watch.Stop();
        UnityEngine.Debug.LogFormat("Asset loading [{0}]: {1:F1} ms", stage, watch.Elapsed.TotalMilliseconds);
    }
}
