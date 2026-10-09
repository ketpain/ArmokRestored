using System;
using System.Collections;
using Armok.Lifecycle;
using MaterialStore;
using UnityEditor;
using UnityEngine;

public static class ContentPreviewPreparation
{
    [MenuItem("Mytools/Prepare Content Previews")]
    public static void Prepare()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Prepare previews outside Play Mode; the live session owns runtime loading.");

        MaterialCollector.EnsureMaterialCollection();
        Prepare(ItemManager.Instance.LoadItems());
        Prepare(DecorationManager.Instance.LoadShapes());
        Prepare(ImageManager.Instance.LoadImages());
        Debug.Log("Content previews prepared from the current DF definitions. Use DF Raw Reader to fetch new definitions.");
    }

    private static void Prepare(IEnumerator loader)
    {
        using (var operation = new LoadOperation(loader, () => false))
        {
            while (operation.MoveNext()) { }
            if (operation.Failure != null) throw operation.Failure;
        }
    }
}
