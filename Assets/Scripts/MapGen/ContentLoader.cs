using Armok.Lifecycle;
using hqx;
using MaterialStore;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.UI;

public class ContentLoader : MonoBehaviour
{
    [SerializeField]
    private ProgressBar mainProgressBar;
    [SerializeField]
    private ProgressBar subProgressBar;

    public void Start()
    {
        sessionRegistration = DFConnection.RegisterConnectionCallback(Initialize);
    }

    private ContentCatalog catalog;
    public ContentCatalog Catalog { get; private set; }
    public const long LoadFrameTimeout = 16;

    // Retained for legacy configuration callers; execution is owned by LoadOperation.
    internal static IEnumerator StartStaticCoroutine(IEnumerator enumerator) => enumerator;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void ResetForNewScene()
    {
        _routiner = null;
        ContentCatalog.ResetForNewScene();
    }

    internal static IDisposable RegisterLoadCallback(Func<IEnumerator> coroutine)
        => SessionRuntime.Current.OnContent(coroutine);

    private SessionCoordinator session;
    private LoadOperation loadOperation;
    private Func<IEnumerator>[] loadCallbacks;

    [SerializeField]
    private CreatureSpriteManager _spriteManager = new CreatureSpriteManager();
    private static ContentLoader _routiner;

    public CreatureSpriteManager SpriteManager { get { return _spriteManager; } }

    public void Awake()
    {
        _routiner = this;
        session = SessionRuntime.Current;
        session.Changed += PresentLoadingState;
        SessionRuntime.Stopping += CancelLoading;
        catalog = new ContentCatalog(_spriteManager);
        SessionRuntime.AfterStop(() =>
        {
            catalog.Unpublish();
            Catalog = null;
        });

        try { PrepareTextures(); }
        catch (Exception error) { SessionRuntime.Stop(session, error); }
    }

    private void PrepareTextures()
    {

        catalog.shapeTextureStorage = new TextureStorage();
        catalog.specialTextureStorage = new TextureStorage();

        Debug.Log("Compiling material shape textures.");
        using (new AssetLoadTiming("Read material shape textures"))
        {
            var shapes = Resources.Load<Texture2DArray>("shapeTextures");
            if (shapes == null) throw new InvalidDataException("Generated shape textures are missing. Run Mytools > Validation > Prepare Content.");
            catalog.shapeTextureStorage.AddTextureArray(shapes);
        }

        catalog.DefaultShapeTexIndex = catalog.shapeTextureStorage.AddTexture(CreateFlatTexture(new Color(1f, 0.5f, 1f, 0.5f)));
        catalog.DefaultSpecialTexIndex = catalog.specialTextureStorage.AddTexture(Texture2D.blackTexture);
    }

    public static Texture2D CreateFlatTexture(Color color)
    {
        Texture2D tex = new Texture2D(4, 4);
        var pix = tex.GetPixels();
        for (int i = 0; i < pix.Length; i++)
        {
            pix[i] = color;
        }
        tex.SetPixels(pix);
        tex.Apply();
        return tex;
    }
    public static Texture2D LoadTexture(XAttribute textureAtt, XElement elemtype, Color defaultColor, bool linear = false)
    {
        if (textureAtt == null)
            return CreateFlatTexture(defaultColor);

        string texturePath = Path.Combine(Path.GetDirectoryName(new Uri(elemtype.BaseUri).LocalPath), textureAtt.Value);
        texturePath = Path.GetFullPath(texturePath);
        if (!File.Exists(texturePath))
        {
            Debug.LogError("File not found: " + texturePath);
            return CreateFlatTexture(defaultColor);
        }

        byte[] patternData = File.ReadAllBytes(texturePath);
        Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false, linear);
        texture.LoadImage(patternData);
        GameSettings.ClampToMaxSize(texture);
        texture.name = texturePath;
        if (texture.width * 4 <= GameSettings.Instance.rendering.maxTextureSize && texture.height * 4 <= GameSettings.Instance.rendering.maxTextureSize)
        {
            HqxSharp.Scale4(texture);
        }
        else if (texture.width * 3 <= GameSettings.Instance.rendering.maxTextureSize && texture.height * 3 <= GameSettings.Instance.rendering.maxTextureSize)
        {
            HqxSharp.Scale3(texture);
        }
        else if (texture.width * 2 <= GameSettings.Instance.rendering.maxTextureSize && texture.height * 2 <= GameSettings.Instance.rendering.maxTextureSize)
        {
            HqxSharp.Scale2(texture);
        }
        return texture;
    }


    public void Initialize()
    {
        if (loadOperation != null) return;
        loadCallbacks = session.BeginContent();
        loadOperation = new LoadOperation(LoadAssets(), () => session.IsStopping);
        StartCoroutine(RunLoad());
    }

    IEnumerator RunLoad()
    {
        while (loadOperation.MoveNext()) yield return loadOperation.Current;
        if (loadOperation.Failure != null) SessionRuntime.Stop(session, loadOperation.Failure);
        else if (loadOperation.Completed && !session.IsStopping)
        {
            try { session.ContentReady(); }
            catch (Exception error) { SessionRuntime.Stop(session, error); }
        }
    }

    void CancelLoading()
    {
        StopAllCoroutines();
        loadOperation?.Dispose();
        PresentLoadingState();
    }

    // Progress presentation is an adapter to the existing serialized bars.
    // Other UI subscribes to the session, rather than being called by the loader.
    void PresentLoadingState()
    {
        bool loading = session.Stage == SessionStage.LoadingContent;
        if (mainProgressBar != null)
        {
            mainProgressBar.gameObject.SetActive(loading);
            if (loading) mainProgressBar.SetProgress(session.Progress, session.Message);
        }
        if (subProgressBar != null) subProgressBar.gameObject.SetActive(loading);
    }

    IEnumerator LoadAssets()
    {
        System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        mainProgressBar.gameObject.SetActive(true);
        subProgressBar.gameObject.SetActive(true);
        watch.Start();
        using (new AssetLoadTiming("Read material patterns"))
            catalog.PatternTextureArray = Resources.Load<Texture2DArray>("patternTextures");
        if (catalog.PatternTextureArray == null) throw new InvalidDataException("Generated pattern textures are missing. Run Mytools > Validation > Prepare Content.");
        catalog.PatternTextureDepth = catalog.PatternTextureArray.depth;
        Shader.SetGlobalTexture("_MatTexArray", catalog.PatternTextureArray);
        float totalItems = loadCallbacks.Length + 2; //Streaming assets, finalizations, and load callbacks.
        float doneItems = 0;
        session.Report("Loading content", doneItems / totalItems);
        using (new AssetLoadTiming("Parse content definitions"))
            yield return ParseContentIndexFile(Application.streamingAssetsPath + "/index.txt");
        doneItems++;
        session.Report("Loading content", doneItems / totalItems);
        yield return FinalizeTextureAtlases();
        using (new AssetLoadTiming("Match DF materials"))
            MaterialCollection.Init();

        foreach (var callback in loadCallbacks)
        {
            doneItems++;
            session.Report("Loading content", doneItems / totalItems);
            if (callback != null)
                using (new AssetLoadTiming(callback.Method.DeclaringType.Name + "." + callback.Method.Name))
                    yield return callback();
        }
        Catalog = catalog;
        catalog.Publish();
        Debug.Log("Done!");
        watch.Stop();
        Debug.LogFormat("Asset loading [Total]: {0:F1} ms", watch.Elapsed.TotalMilliseconds);
        Debug.Log(string.Format("loaded {0} meshes, and {1} shape textures.", MeshContent.NumCreated, catalog.shapeTextureStorage.AtlasTexture.depth));
        Debug.Log("Loading Complete. Press ESC to change settings or leave feedback. Have a nice day!");
        Debug.Log("Screen is " + Screen.dpi + "dpi.");
        DFConnection.Instance.NeedNewBlocks = true;
        mainProgressBar.gameObject.SetActive(false);
        subProgressBar.gameObject.SetActive(false);

        yield break;
    }

    IEnumerator ParseContentIndexFile(string path)
    {
        session.Report("Loading Index File: " + path, session.Progress);

        string line;
        List<string> fileArray = new List<string>(); //This allows us to parse the file in reverse.
        StreamReader file = new StreamReader(path);
        while ((line = file.ReadLine()) != null)
        {
            line = line.Trim(); //remove trailing spaces
            if (string.IsNullOrEmpty(line))
                continue;
            if (line[0] == '#') //Allow comments
                continue;

            fileArray.Add(line);
        }
        file.Close();
        string filePath;

        for (int i = fileArray.Count - 1; i >= 0; i--)
        {
            try
            {
                filePath = Path.Combine(Path.GetDirectoryName(path), fileArray[i]);
            }
            catch (Exception)
            {
                continue; //Todo: Make an error message here
            }
            subProgressBar.SetProgress(1.0f - (i / (float)fileArray.Count));
            if (Directory.Exists(filePath)) //if it's a directory, just parse the contents
            {
                yield return ParseContentDirectory(filePath);
            }
            else
            {
                switch (Path.GetExtension(filePath))
                {
                    case ".txt":
                        bool rawFile;
                        using (var mightBeRaw = new StreamReader(filePath))
                            rawFile = mightBeRaw.ReadLine() == Path.GetFileNameWithoutExtension(filePath);
                        if (rawFile)
                            yield return ParseContentRawFile(filePath);
                        else
                            yield return ParseContentIndexFile(filePath);
                        break;
                    case ".xml":
                        yield return ParseContentXMLFile(filePath);
                        break;
                    default:
                        break;
                }
            }
        }
        yield return null;
    }

    IEnumerator ParseContentXMLFile(string path)
    {
        subProgressBar.SetProgress("Loading XML File: " + path);
        XElement doc = XElement.Load(path, LoadOptions.SetBaseUri | LoadOptions.SetLineInfo);
        while (doc != null)
        {
            switch (doc.Name.LocalName)
            {
                case "shapeTextures":
                    if (catalog.ShapeTextureConfiguration == null)
                        catalog.ShapeTextureConfiguration = TileConfiguration<NormalContent>.GetFromRootElement(doc, "shapeTexture");
                    yield return catalog.ShapeTextureConfiguration.AddSingleContentConfig(doc, catalog.shapeTextureStorage);
                    break;
                case "tileMeshes":
                    if (catalog.TileMeshConfiguration == null)
                        catalog.TileMeshConfiguration = TileConfiguration<MeshContent>.GetFromRootElement(doc, "tileMesh");
                    yield return catalog.TileMeshConfiguration.AddSingleContentConfig(doc, new MeshContent.TextureStorageContainer(null, catalog.shapeTextureStorage, catalog.specialTextureStorage));
                    break;
                case "materialLayers":
                    if (catalog.MaterialLayerConfiguration == null)
                        catalog.MaterialLayerConfiguration = TileConfiguration<LayerContent>.GetFromRootElement(doc, "materialLayer");
                    yield return catalog.MaterialLayerConfiguration.AddSingleContentConfig(doc);
                    break;
                case "growthMeshes":
                    if (catalog.GrowthMeshConfiguration == null)
                        catalog.GrowthMeshConfiguration = TileConfiguration<MeshContent>.GetFromRootElement(doc, "growthMesh");
                    yield return catalog.GrowthMeshConfiguration.AddSingleContentConfig(doc, new MeshContent.TextureStorageContainer(null, catalog.shapeTextureStorage, catalog.specialTextureStorage));
                    break;
                case "designationMeshes":
                    if (catalog.DesignationMeshConfiguration == null)
                        catalog.DesignationMeshConfiguration = TileConfiguration<MeshContent>.GetFromRootElement(doc, "designationMesh");
                    yield return catalog.DesignationMeshConfiguration.AddSingleContentConfig(doc, new MeshContent.TextureStorageContainer(null, catalog.shapeTextureStorage, catalog.specialTextureStorage));
                    break;
                case "collisionMeshes":
                    if (catalog.CollisionMeshConfiguration == null)
                        catalog.CollisionMeshConfiguration = TileConfiguration<MeshContent>.GetFromRootElement(doc, "collisionMesh");
                    yield return catalog.CollisionMeshConfiguration.AddSingleContentConfig(doc, new MeshContent.TextureStorageContainer(null, catalog.shapeTextureStorage, catalog.specialTextureStorage));
                    break;
                case "itemMeshes":
                    if (catalog.ItemMeshConfiguration == null)
                        catalog.ItemMeshConfiguration = TileConfiguration<MeshContent>.GetFromRootElement(doc, "itemMesh");
                    yield return catalog.ItemMeshConfiguration.AddSingleContentConfig(doc, null);
                    break;
                default:
                    break;
            }
            doc = doc.NextNode as XElement;
        }
        yield return null;
    }

    IEnumerator ParseContentRawFile(string path)
    {
        session.Report("Loading Raw File: " + path, session.Progress);
        var tokenList = RawLoader.SplitRawFileText(File.ReadAllText(path));
        var tokenEnumerator = tokenList.GetEnumerator();
        try
        {
            if (tokenEnumerator.MoveNext()) // Because they always start at -1.
            {
                if (tokenEnumerator.Current.Token == "OBJECT")
                {
                    switch (tokenEnumerator.Current.Parameters[0])
                    {
                        case "GRAPHICS":
                            yield return SpriteManager.ParseGraphics(tokenEnumerator, path);
                            break;
                        default:
                            Debug.Log("Unhandled Token: " + tokenEnumerator.Current.Parameters[0]);
                            break;
                    }
                }
                else
                    Debug.Log("Unexpected Token: " + tokenEnumerator.Current.Token);
            }
        }
        finally
        {
            tokenEnumerator.Dispose();
        }
        yield return null;
    }

    IEnumerator ParseContentDirectory(string path)
    {
        foreach (var file in Directory.GetFiles(path, "*.txt", SearchOption.AllDirectories))
        {
            bool rawFile;
            using (var mightBeRaw = new StreamReader(file))
                rawFile = mightBeRaw.ReadLine() == Path.GetFileNameWithoutExtension(file);
            if (rawFile)
                yield return ParseContentRawFile(file);
        }
        yield return null;
    }

    IEnumerator FinalizeTextureAtlases()
    {
        session.Report("Compacting textures...", session.Progress);
        subProgressBar.SetProgress(0.0f / 3.0f, "Building shape textures...");
        yield return null;
        using (new AssetLoadTiming("Build shape texture array"))
            catalog.shapeTextureStorage.CompileTextures("ShapeTexture", TextureFormat.RGBA32, new Color(1.0f, 0.5f, 0.0f, 0.5f), true);
        subProgressBar.SetProgress(1.0f / 3.0f, "Building special textures...");
        yield return null;
        using (new AssetLoadTiming("Build special texture array"))
            catalog.specialTextureStorage.CompileTextures("SpecialTexture");

        subProgressBar.SetProgress(2.0f / 3.0f, "Updating Material Manager...");
        yield return null;

        Vector4 arrayCount = new Vector4(catalog.PatternTextureDepth, catalog.shapeTextureStorage.Count, catalog.specialTextureStorage.Count, catalog.shapeTextureStorage.Count);
        if (MaterialManager.Instance)
        {
            MaterialManager.Instance.SetTexture("_ShapeMap", catalog.shapeTextureStorage.AtlasTexture);
            MaterialManager.Instance.SetTexture("_BumpMap", catalog.shapeTextureStorage.AtlasTexture);
            MaterialManager.Instance.SetTexture("_SpecialTex", catalog.specialTextureStorage.AtlasTexture);
            MaterialManager.Instance.SetVector("_TexArrayCount", arrayCount);
        }
        session.Report("Finalizing low detail creature sprites", session.Progress);
        SpriteManager.subProgressBar = subProgressBar;
        using (new AssetLoadTiming("Build creature sprites"))
            yield return SpriteManager.FinalizeSprites();
        //get rid of any un-used textures left over.
        using (new AssetLoadTiming("Request content cleanup"))
        {
            Resources.UnloadUnusedAssets();
            GC.Collect();
        }
        yield return null;
    }

    private System.IDisposable sessionRegistration;
    private void OnDestroy()
    {
        sessionRegistration?.Dispose();
        if (session != null) session.Changed -= PresentLoadingState;
        SessionRuntime.Stopping -= CancelLoading;
        SessionRuntime.Stop(session);
        loadOperation?.Dispose();
        if (_routiner == this) _routiner = null;
    }
}
