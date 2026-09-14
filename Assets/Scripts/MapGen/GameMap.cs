using Building;
using DF.Enums;
using DFHack;
using MapGen;
using RemoteFortressReader;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The class responsible for talking to DF and meshing the data it gets.
// Relevant vocabulary: A "map tile" is an individual square on the map.
// A "map block" is a 16x16x1 area of map tiles stored by DF; think minecraft "chunks".

public class GameMap : MonoBehaviour
{
    public CanvasGroup helpOverlay;

    public Material waterMaterial;
    public Material magmaMaterial;

    public Light magmaGlowPrefab;
    public Text cursorProperties;

    public bool overheadShadows = true;

    public bool firstPerson = false;

    public int cursX = -30000;
    public int cursY = -30000;
    public int cursZ = -30000;

    public WorldMapMaker worldMap;

    public PanelSlider optionsPanel;

    // Parameters managing the currently visible area of the map.
    // Tracking:
    public int PosXBlock
    {
        get
        {
            return PosXTile / MapCoordinates.BlockSize;
        }
    }
    public int PosYBlock
    {
        get
        {
            return PosYTile / MapCoordinates.BlockSize;
        }
    }
    public int PosXTile { get; private set; }
    public int PosYTile { get; private set; }
    public int PosZ { get; private set; }

    BlockMesher mesher;

    public BlockMeshSet[,,] mapMeshes;

    // Dirty flags for those meshes
    bool[,,] blockDirtyBits;
    bool[,,] blockContentBits;
    UpdateSchedule[,,] blockUpdateSchedules;
    bool[,,] liquidBlockDirtyBits;
    // Lights from magma.
    Light[,,] magmaGlow;

    DFCoord mapSize; //This is to keep track of changing size of the map.
    DFCoord mapPosition;

    // Stuff to let the material list & various meshes & whatnot be loaded from xml specs at runtime.
    public static Dictionary<BuildingStruct, BuildingDefinition> buildings;
    public static Dictionary<MatPairStruct, MaterialDefinition> creatures;

    // Does about what you'd think it does.
    void Start()
    {
        enabled = false;

        Debug.Log("Started Armok Vision version " + BuildSettings.Instance.content_version);

        Debug.Log("scmCommitId: " + BuildManifest.Instance.scmCommitId);
        Debug.Log("scmBranch: " + BuildManifest.Instance.scmBranch);
        Debug.Log("buildNumber: " + BuildManifest.Instance.buildNumber);
        Debug.Log("buildStartTime: " + BuildManifest.Instance.buildStartTime);
        Debug.Log("projectId: " + BuildManifest.Instance.projectId);
        Debug.Log("bundleId: " + BuildManifest.Instance.bundleId);
        Debug.Log("unityVersion: " + BuildManifest.Instance.unityVersion);
        Debug.Log("xcodeVersion: " + BuildManifest.Instance.xcodeVersion);
        Debug.Log("cloudBuildTargetName: " + BuildManifest.Instance.cloudBuildTargetName);

        session = SessionRuntime.Current;
        readyRegistration = session.OnReady(OnConnectToDF);
        SessionRuntime.Stopping += StopPresentation;

#if DEVELOPMENT_BUILD
        Debug.Log("Dev build");
#endif
        if (dfScreen != null)
            dfScreen.SetActive(GameSettings.Instance.game.showDFScreen);
    }

    public static GameMap Instance { get; private set; }

    static Thread mainThread;
    // Awake is called when the script instance is being loaded
    public void Awake()
    {
        Instance = this;
        mainThread = Thread.CurrentThread;
    }

    public static bool IsMainThread
    {
        get
        {
            return Thread.CurrentThread == mainThread;
        }
    }

    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD"), System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void BeginSample(string name)
    {
        if (IsMainThread)
            UnityEngine.Profiling.Profiler.BeginSample(name);
    }
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD"), System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void BeginSample(string name, Object targetObject)
    {
        if (IsMainThread)
            UnityEngine.Profiling.Profiler.BeginSample(name, targetObject);
    }

    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD"), System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void EndSample()
    {
        if (IsMainThread)
            UnityEngine.Profiling.Profiler.EndSample();
    }

    private Armok.Lifecycle.SessionCoordinator session;
    private System.IDisposable readyRegistration;
    private void StopPresentation() { enabled = false; HideHelp(); }

    void OnConnectToDF()
    {
        Debug.Log("Connected");
        enabled = true;
        mesher = BlockMesher.GetMesher(GameSettings.Instance.meshing.meshingThreads);
        SessionRuntime.Own(mesher);
        // Initialize materials, if available
        if (GameSettings.Instance.debug.saveMaterialList)
            MapDataExport.SaveMaterialList(MaterialRaws.Instance, Path.Combine(exportDirectory, "MaterialList.csv"));
        // Initialize items, if available
            if (GameSettings.Instance.debug.saveItemList)
                MapDataExport.SaveMaterialList(ItemRaws.Instance, Path.Combine(exportDirectory, "ItemList.csv"));
        if (DFConnection.Instance.NetBuildingList != null)
        {
            if (buildings == null)
                buildings = new Dictionary<BuildingStruct, BuildingDefinition>();
            buildings.Clear();
            foreach (BuildingDefinition building in DFConnection.Instance.NetBuildingList.building_list)
            {
                buildings[building.building_type] = building;
            }
            if (GameSettings.Instance.debug.saveBuildingList)
                MapDataExport.SaveBuildingList(DFConnection.Instance.NetBuildingList, Path.Combine(exportDirectory, "BuildingList.csv"));
        }
        if (CreatureRaws.Instance != null)
        {
            if (creatures == null)
                creatures = new Dictionary<MatPairStruct, MaterialDefinition>();
            foreach (CreatureRaw creatureRaw in CreatureRaws.Instance)
            {
                foreach (var caste in creatureRaw.caste)
                {
                    MatPairStruct creatureCaste = new MatPairStruct(creatureRaw.index, caste.index);
                    MaterialDefinition creatureDef = new MaterialDefinition();
                    creatureDef.mat_pair = creatureCaste;
                    creatureDef.id = creatureRaw.creature_id + ":" + caste.caste_id;
                    creatureDef.name = caste.caste_name[0];
                    creatureDef.state_color = creatureRaw.color;
                    creatures[creatureCaste] = creatureDef;
                }
            }
            GeneratedCreatureTranslator.AddFakeCreaturesToList(creatures);
            if (GameSettings.Instance.debug.saveCreatureList)
                MapDataExport.SaveMaterialList(creatures, Path.Combine(exportDirectory, "CreatureList.csv"));
        }

        if (GameSettings.Instance.debug.saveTiletypeList)
            MapDataExport.SaveTileTypeList(DFConnection.Instance.NetTiletypeList, Path.Combine(exportDirectory, "TiletypeList.csv"));

        if (GameSettings.Instance.debug.savePlantList)
            MapDataExport.SavePlantList(TokenLists.PlantTokenList.GrowthIDs, Path.Combine(exportDirectory, "PlantList.csv"));

        UpdateView();

        InitializeBlocks();
    }

    public GameObject dfScreen;


    GameObject selected;

    // Run once per frame.
    void Update()
    {
        if (!SessionRuntime.Ready) return;
        if (ArmokInput.Action("ToggleDF").WasPressedThisFrame() && !MapInputUI.HasKeyboardFocus)
        {
            GameSettings.Instance.game.showDFScreen = !GameSettings.Instance.game.showDFScreen;

            dfScreen.SetActive(GameSettings.Instance.game.showDFScreen);
        }

        UpdateView();


        if (!GameSettings.Instance.game.showDFScreen && !MapInputUI.HasKeyboardFocus && DFConnection.Instance.WorldMode != dfproto.GetWorldInfoOut.Mode.MODE_ADVENTURE)
        {
            if (ArmokInput.Action("ScaleUnits").WasPressedThisFrame())
            {
                switch (GameSettings.Instance.units.scaleUnits)
                {
                    case GameSettings.UnitScale.Fixed:
                        GameSettings.Instance.units.scaleUnits = GameSettings.UnitScale.Logarithmic;
                        break;
                    case GameSettings.UnitScale.Logarithmic:
                        GameSettings.Instance.units.scaleUnits = GameSettings.UnitScale.Real;
                        break;
                    case GameSettings.UnitScale.Real:
                    default:
                        GameSettings.Instance.units.scaleUnits = GameSettings.UnitScale.Fixed;
                        break;
                }
            }
            if (ArmokInput.Action("OverheadShadows").WasPressedThisFrame())
            {
                overheadShadows = !overheadShadows;
            }

            if (ArmokInput.Action("TakeScreenshot").WasPressedThisFrame())
                screenshots.Capture(ArmokInput.Action("Mod").IsPressed());
            if (ArmokInput.Action("Refresh").WasPressedThisFrame())
            {
                Refresh();
            }
            if (ArmokInput.Action("Cancel").WasPressedThisFrame())
            {
                if (optionsPanel != null) optionsPanel.Slide();
            }
            if (ArmokInput.Action("ToggleMap").WasPressedThisFrame() && mapWindow != null && mapCamera != null)
            {
                mapWindow.SetActive(!mapWindow.activeSelf);
                mapCamera.SetActive(!mapCamera.activeSelf);
            }
            if(ArmokInput.Action("ToggleUI").WasPressedThisFrame())
            {
                mainUI.gameObject.SetActive(!mainUI.gameObject.activeSelf);
            }
        }

        if (ContentLoader.Instance == null)
            return;

        UpdateRequestRegion();
        UpdateBlocks();
        //DrawBlocks();
        UpdateBlockVisibility();
    }
    private void UpdateBlockVisibility()
    {
        Shader.SetGlobalVector("_ViewMin", MapCoordinates.DFtoUnityBottomCorner(new DFCoord(
            Mathf.Max((PosXBlock - GameSettings.Instance.rendering.drawRangeSide + 1) * MapCoordinates.BlockSize, 0),
            Mathf.Min((PosYBlock + GameSettings.Instance.rendering.drawRangeSide) * MapCoordinates.BlockSize, mapSize.y * 16),
            -9999
        )) + new Vector3(0, 0, MapCoordinates.TileWidth));
        Shader.SetGlobalVector("_ViewMax", MapCoordinates.DFtoUnityBottomCorner(new DFCoord(
            Mathf.Min((PosXBlock + GameSettings.Instance.rendering.drawRangeSide) * MapCoordinates.BlockSize, mapSize.x * 16),
            Mathf.Max((PosYBlock - GameSettings.Instance.rendering.drawRangeSide + 1) * MapCoordinates.BlockSize, 0),
            9999
        )) + new Vector3(0, 0, MapCoordinates.TileWidth));

        for (int z = 0; z < mapMeshes.GetLength(2); z++)
        {
            UpdateBlockVisibility(z);
        }
    }

    void UpdateBlockVisibility(int z)
    {
        if (z < 0 || z >= mapMeshes.GetLength(2))
            return;
        for (int x = 0; x < mapMeshes.GetLength(0); x++)
            for (int y = 0; y < mapMeshes.GetLength(1); y++)
            {
                if (mapMeshes[x, y, z] == null)
                    continue;
                if(
                    x <= PosXBlock - GameSettings.Instance.rendering.drawRangeSide
                    || x >= PosXBlock + GameSettings.Instance.rendering.drawRangeSide
                    || y <= PosYBlock - GameSettings.Instance.rendering.drawRangeSide
                    || y >= PosYBlock + GameSettings.Instance.rendering.drawRangeSide
                )
                    mapMeshes[x, y, z].UpdateVisibility(BlockMeshSet.Visibility.None);
                else
                    mapMeshes[x, y, z].UpdateVisibility(GetVisibility(z));
            }
    }

    public static bool IsInVisibleArea(DFCoord coord)
    {
        return (coord.z < (Instance.firstPerson ? Instance.PosZ + GameSettings.Instance.rendering.drawRangeUp : Instance.PosZ))
                    && (coord.z >= (Instance.PosZ - GameSettings.Instance.rendering.drawRangeDown))
                    && (coord.x / MapCoordinates.BlockSize > (Instance.PosXBlock - GameSettings.Instance.rendering.drawRangeSide))
                    && (coord.x / MapCoordinates.BlockSize < (Instance.PosXBlock + GameSettings.Instance.rendering.drawRangeSide))
                    && (coord.y / MapCoordinates.BlockSize > (Instance.PosYBlock - GameSettings.Instance.rendering.drawRangeSide))
                    && (coord.y / MapCoordinates.BlockSize < (Instance.PosYBlock + GameSettings.Instance.rendering.drawRangeSide));
    }

    BlockMeshSet.Visibility GetVisibility(int z)
    {
        if (z > PosZ + GameSettings.Instance.rendering.drawRangeUp)
            return BlockMeshSet.Visibility.None;
        else if (z >= PosZ)
        {
            if (firstPerson)
                return BlockMeshSet.Visibility.Walls;
            else if (overheadShadows)
                return BlockMeshSet.Visibility.Shadows;
            else
                return BlockMeshSet.Visibility.None;
        }
        else if (z == PosZ - 1)
            return BlockMeshSet.Visibility.All;
        else if (z >= PosZ - GameSettings.Instance.rendering.drawRangeDown)
        {
            return BlockMeshSet.Visibility.Walls;
        }
        else
            return BlockMeshSet.Visibility.None;
    }

    public float helpFadeLength = 0.5f;
    Coroutine helpFade;
    bool helpEnabled = false;
    private void ToggleHelp()
    {
        StopCoroutine(helpFade);
        if (helpEnabled)
        {
            HideHelp();
        }
        else
        {
            ShowHelp();
        }
    }

    public void HideHelp()
    {
        helpFade = StartCoroutine(DisableHelp());
    }

    public void ShowHelp()
    {
        helpFade = StartCoroutine(EnableHelp());
    }

    IEnumerator DisableHelp()
    {
        helpEnabled = false;
        for (float f = helpOverlay.alpha; f >= 0; f -= (Time.unscaledDeltaTime / helpFadeLength))
        {
            helpOverlay.alpha = f;
            yield return null;
        }
        helpOverlay.gameObject.SetActive(false);
        yield return null;
    }
    IEnumerator EnableHelp()
    {
        helpEnabled = true;
        helpOverlay.gameObject.SetActive(true);
        for (float f = helpOverlay.alpha; f <= 1; f += (Time.unscaledDeltaTime / helpFadeLength))
        {
            helpOverlay.alpha = f;
            yield return null;
        }
        yield return null;
    }




    public void Refresh()
    {
        if (blockDirtyBits != null)
        {
            for (int z = 0; z < blockDirtyBits.GetLength(2); z++)
                for (int x = 0; x < blockDirtyBits.GetLength(0); x++)
                    for (int y = 0; y < blockDirtyBits.GetLength(1); y++)
                    {
                        if (blockContentBits[x, y, z])
                        {
                            SplatManager.Instance.DirtyLayer(x, y, z);
                            SplatManager.Instance.DirtyGrass(x, y, z);
                            SplatManager.Instance.DirtySpatter(x, y, z);
                        }
                        blockDirtyBits[x, y, z] = blockContentBits[x, y, z];
                        liquidBlockDirtyBits[x, y, z] = blockContentBits[x, y, z];
                    }
        }
    }

    public Mesh testMesh;



    void OnDestroy()
    {
        readyRegistration?.Dispose();
        SessionRuntime.Stopping -= StopPresentation;
        SessionRuntime.Stop(session);
        if (Instance == this) Instance = null;
    }

    public void OpenURL(string url)
    {
        Application.OpenURL(url);
    }

    public DFCoord FollowPos { get; private set; }

    void UpdateView()
    {
        ViewInfo view = DFConnection.Instance.PopViewInfoUpdate();
        if (view == null) return;
        UnityEngine.Profiling.Profiler.BeginSample("UpdateView", this);
        //Debug.Log("Got view");

        if (view.follow_unit_id != -1 && CreatureManager.Instance.Units != null)
        {
            int unitIndex = CreatureManager.Instance.Units.FindIndex(x => x.id == view.follow_unit_id);
            if (unitIndex >= 0)
            {
                var unit = CreatureManager.Instance.Units[unitIndex];
                FollowPos = new DFCoord(unit.pos_x, unit.pos_y, unit.pos_z);
                return;
            }
        }
        FollowPos = new DFCoord(view.view_pos_x + (view.view_size_x / 2), view.view_pos_y + (view.view_size_y / 2), view.view_pos_z);
        UnityEngine.Profiling.Profiler.EndSample();
    }
    // Update the region we're requesting
    void UpdateRequestRegion()
    {
        UnityEngine.Profiling.Profiler.BeginSample("UpdateRequestRegion", this);
        DFConnection.Instance.SetRequestRegion(
            new BlockCoord(
                PosXBlock - GameSettings.Instance.rendering.drawRangeSide,
                PosYBlock - GameSettings.Instance.rendering.drawRangeSide,
                PosZ - GameSettings.Instance.rendering.drawRangeDown
            ),
            new BlockCoord(
                PosXBlock + GameSettings.Instance.rendering.drawRangeSide,
                PosYBlock + GameSettings.Instance.rendering.drawRangeSide,
                PosZ + GameSettings.Instance.rendering.drawRangeUp
            ));
        UnityEngine.Profiling.Profiler.EndSample();
    }

    const double timeout = 15;
    System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();

    void UpdateBlocks()
    {
        if (DFConnection.Instance.EmbarkMapSize != mapSize)
        {
            ClearMap();
            InitializeBlocks();
            mapSize = DFConnection.Instance.EmbarkMapSize;
            DFConnection.Instance.RequestMapReset();
        }
        if (DFConnection.Instance.EmbarkMapPosition != mapPosition)
        {
            ClearMap();
            mapPosition = DFConnection.Instance.EmbarkMapPosition;
            MapCoordinates.MapZOffset = DFConnection.Instance.EmbarkMapPosition.z;
            DFConnection.Instance.RequestMapReset();
        }
        if (MapDataStore.MapSize.x < 48)
            return;

        stopwatch.Restart();
        BuildingManager.Instance.BeginExistenceCheck();
        ItemManager.Instance.BeginExistenceCheck();
        UnityEngine.Profiling.Profiler.BeginSample("UpdateBlocks", this);
        MapBlock block;
        if(DFConnection.Instance.UpdatedAnyBlocks)
        {
            FlowManager.Instance.Clear();
            DFConnection.Instance.UpdatedAnyBlocks = false;
        }
        while((block = DFConnection.Instance.PopMapBlockUpdate()) != null)
        {
            bool setTiles;
            bool setLiquids;
            bool setSpatters;
            UnityEngine.Profiling.Profiler.BeginSample("StoreTiles", this);
            MapDataStore.StoreTiles(block, out setTiles, out setLiquids, out setSpatters);
            UnityEngine.Profiling.Profiler.EndSample();
            if (setTiles)
            {
                addSeasonalUpdates(block, block.map_x, block.map_y, block.map_z);
                SetDirtyBlock(block.map_x, block.map_y, block.map_z);
                SetBlockContent(block.map_x, block.map_y, block.map_z);
            }
            if (setLiquids)
            {
                SetDirtyLiquidBlock(block.map_x, block.map_y, block.map_z);
                SetBlockContent(block.map_x, block.map_y, block.map_z);
            }
            if (setSpatters)
            {
                SetDirtySpatterBlock(block.map_x, block.map_y, block.map_z);
            }
            //foreach (var item in block.items)
            //{
            //    ///Send it to item manager later.
            //}
            UnityEngine.Profiling.Profiler.BeginSample("BuildingManager.LoadBlock", this);
            BuildingManager.Instance.LoadBlock(block);
            UnityEngine.Profiling.Profiler.EndSample();
            UnityEngine.Profiling.Profiler.BeginSample("ItemManager.LoadBlock", this);
            ItemManager.Instance.LoadBlock(block);
            UnityEngine.Profiling.Profiler.EndSample();

            FlowManager.Instance[new DFCoord(block.map_x, block.map_y, block.map_z)] = block.flows;
            //Add tile fires to flows directly.
            List<FlowInfo> tileFlows = null;
            if(block.tiles != null && block.tiles.Count == 256)
            {
                for(int x = 0; x < 16; x++)
                    for(int y = 0; y < 16; y++)
                    {
                        int index = y * 16 + x;
                        var tile = DFConnection.Instance.NetTiletypeList.tiletype_list[block.tiles[index]];
                        if(tile.material == TiletypeMaterial.CAMPFIRE || tile.material == TiletypeMaterial.FIRE)
                        {
                            FlowInfo flow = new FlowInfo();
                            flow.type = FlowType.Fire;
                            if (tile.material == TiletypeMaterial.CAMPFIRE)
                                flow.type = FlowType.CampFire;
                            else
                                flow.type = FlowType.Fire;
                            flow.density = 100;
                            flow.pos = new DFCoord(block.map_x + x, block.map_y + y, block.map_z);
                            if (tileFlows == null)
                                tileFlows = new List<FlowInfo>();
                            tileFlows.Add(flow);
                        }
                    }
                FlowManager.Instance.SetTileFlows(new DFCoord(block.map_x, block.map_y, block.map_z), tileFlows);
            }
        }
        BuildingManager.Instance.EndExistenceCheck();
        ItemManager.Instance.EndExitenceCheck();
        DirtySeasonalBlocks();
        UnityEngine.Profiling.Profiler.BeginSample("EnqueueMeshUpdates", this);
        EnqueueMeshUpdates();
        UnityEngine.Profiling.Profiler.EndSample();

        mesher.Poll();

        FetchNewMeshes();
        UnityEngine.Profiling.Profiler.EndSample();
    }

    void SetMaterialBounds(Vector4 bounds)
    {
        MaterialManager.Instance.SetVector("_WorldBounds", bounds);
    }

    void InitializeBlocks()
    {
        int blockSizeX = DFConnection.Instance.EmbarkMapSize.x;
        int blockSizeY = DFConnection.Instance.EmbarkMapSize.y;
        int blockSizeZ = DFConnection.Instance.EmbarkMapSize.z;
        MapCoordinates.MapZOffset = DFConnection.Instance.EmbarkMapPosition.z;

        mapMeshes = new BlockMeshSet[blockSizeX * 16 / MapCoordinates.BlockSize, blockSizeY * 16 / MapCoordinates.BlockSize, blockSizeZ];

        blockDirtyBits = new bool[blockSizeX * 16 / MapCoordinates.BlockSize, blockSizeY * 16 / MapCoordinates.BlockSize, blockSizeZ];
        blockContentBits = new bool[blockSizeX * 16 / MapCoordinates.BlockSize, blockSizeY * 16 / MapCoordinates.BlockSize, blockSizeZ];
        blockUpdateSchedules = new UpdateSchedule[blockSizeX * 16 / MapCoordinates.BlockSize, blockSizeY * 16 / MapCoordinates.BlockSize, blockSizeZ];
        liquidBlockDirtyBits = new bool[blockSizeX * 16 / MapCoordinates.BlockSize, blockSizeY * 16 / MapCoordinates.BlockSize, blockSizeZ];
        magmaGlow = new Light[blockSizeX * 16, blockSizeY * 16, blockSizeZ];
        SplatManager.Instance.Init(blockSizeX * 16 / MapCoordinates.BlockSize, blockSizeY * 16 / MapCoordinates.BlockSize, blockSizeZ);

        Vector3 min = MapCoordinates.DFtoUnityCoord(0, 0, 0) - new Vector3(MapCoordinates.TileWidth / 2, 0, -MapCoordinates.TileWidth / 2);
        Vector3 max = MapCoordinates.DFtoUnityCoord((blockSizeX * 16) - 1, (blockSizeY * 16) - 1, 0) + new Vector3(MapCoordinates.TileWidth / 2, 0, -MapCoordinates.TileWidth / 2);

        SetMaterialBounds(new Vector4(min.x, min.z, max.x, max.z));
    }

    void addSeasonalUpdates(MapBlock block, int mapBlockX, int mapBlockY, int mapBlockZ)
    {
        if (DFConnection.Instance.NetPlantRawList == null)
            return;
        mapBlockX /= MapCoordinates.BlockSize;
        mapBlockY /= MapCoordinates.BlockSize;
        if (blockUpdateSchedules[mapBlockX, mapBlockY, mapBlockZ] != null)
            blockUpdateSchedules[mapBlockX, mapBlockY, mapBlockZ].Clear();
        foreach (var material in block.materials)
        {
            if (material.mat_type != 419
                    || material.mat_index < 0
                    || DFConnection.Instance.NetPlantRawList.plant_raws.Count <= material.mat_index
                    || DFConnection.Instance.NetPlantRawList.plant_raws[material.mat_index].growths.Count == 0)
                continue;
            PlantRaw plantRaw = DFConnection.Instance.NetPlantRawList.plant_raws[material.mat_index];
            if (blockUpdateSchedules[mapBlockX, mapBlockY, mapBlockZ] == null)
                blockUpdateSchedules[mapBlockX, mapBlockY, mapBlockZ] = new UpdateSchedule();
            var schedule = blockUpdateSchedules[mapBlockX, mapBlockY, mapBlockZ];
            foreach (TreeGrowth growth in plantRaw.growths)
            {
                schedule.Add(growth.timing_start);
                schedule.Add(growth.timing_end);
                foreach (GrowthPrint print in growth.prints)
                {
                    schedule.Add(print.timing_start);
                    schedule.Add(print.timing_end);
                }
            }
        }
    }

    void SetDirtyBlock(int mapBlockX, int mapBlockY, int mapBlockZ)
    {
        mapBlockX /= MapCoordinates.BlockSize;
        mapBlockY /= MapCoordinates.BlockSize;
        blockDirtyBits[mapBlockX, mapBlockY, mapBlockZ] = true;
        SplatManager.Instance.DirtyGrass(mapBlockX, mapBlockY, mapBlockZ);
        SplatManager.Instance.DirtyLayer(mapBlockX, mapBlockY, mapBlockZ);
        if (mapBlockZ < SplatManager.Instance.SizeZ - 1)
        {
            SplatManager.Instance.DirtyGrass(mapBlockX, mapBlockY, mapBlockZ + 1);
            SplatManager.Instance.DirtyLayer(mapBlockX, mapBlockY, mapBlockZ + 1);
        }

    }
    void SetBlockContent(int mapBlockX, int mapBlockY, int mapBlockZ)
    {
        mapBlockX /= MapCoordinates.BlockSize;
        mapBlockY /= MapCoordinates.BlockSize;
        blockContentBits[mapBlockX, mapBlockY, mapBlockZ] = true;
    }
    void SetDirtyLiquidBlock(int mapBlockX, int mapBlockY, int mapBlockZ)
    {
        mapBlockX /= MapCoordinates.BlockSize;
        mapBlockY /= MapCoordinates.BlockSize;
        liquidBlockDirtyBits[mapBlockX, mapBlockY, mapBlockZ] = true;
    }
    private void SetDirtySpatterBlock(int map_x, int map_y, int map_z)
    {
        map_x /= MapCoordinates.BlockSize;
        map_y /= MapCoordinates.BlockSize;
        SplatManager.Instance.DirtySpatter(map_x, map_y, map_z);

    }




    void DirtySeasonalBlocks()
    {
        int xmin = Mathf.Clamp(PosXBlock - GameSettings.Instance.rendering.drawRangeSide, 0, mapMeshes.GetLength(0));
        int xmax = Mathf.Clamp(PosXBlock + GameSettings.Instance.rendering.drawRangeSide, 0, mapMeshes.GetLength(0));
        int ymin = Mathf.Clamp(PosYBlock - GameSettings.Instance.rendering.drawRangeSide, 0, mapMeshes.GetLength(1));
        int ymax = Mathf.Clamp(PosYBlock + GameSettings.Instance.rendering.drawRangeSide, 0, mapMeshes.GetLength(1));
        int zmin = Mathf.Clamp(PosZ - GameSettings.Instance.rendering.drawRangeDown, 0, mapMeshes.GetLength(2));
        int zmax = Mathf.Clamp(PosZ + GameSettings.Instance.rendering.drawRangeUp, 0, mapMeshes.GetLength(2));
        for (int zz = zmin; zz < zmax; zz++)
            for (int yy = ymin; yy < ymax; yy++)
                for (int xx = xmin; xx < xmax; xx++)
                {
                    if (blockUpdateSchedules[xx, yy, zz] != null && blockUpdateSchedules[xx, yy, zz].CheckUpdate(TimeHolder.DisplayedTime.CurrentYearTicks))
                        blockDirtyBits[xx, yy, zz] = true;
                }

    }


    // Have the mesher mesh all dirty tiles in the region
    void EnqueueMeshUpdates()
    {
        if (stopwatch.ElapsedMilliseconds > timeout)
            return;
        int queueCount = 0;
        int xmin = Mathf.Clamp(PosXBlock - GameSettings.Instance.rendering.drawRangeSide + 1, 0, mapMeshes.GetLength(0));
        int xmax = Mathf.Clamp(PosXBlock + GameSettings.Instance.rendering.drawRangeSide, 0, mapMeshes.GetLength(0));
        int ymin = Mathf.Clamp(PosYBlock - GameSettings.Instance.rendering.drawRangeSide + 1, 0, mapMeshes.GetLength(1));
        int ymax = Mathf.Clamp(PosYBlock + GameSettings.Instance.rendering.drawRangeSide, 0, mapMeshes.GetLength(1));
        int zmin = Mathf.Clamp(PosZ - GameSettings.Instance.rendering.drawRangeDown, 0, mapMeshes.GetLength(2));
        int zmax = Mathf.Clamp(PosZ + GameSettings.Instance.rendering.drawRangeUp, 0, mapMeshes.GetLength(2));
        if (PosXBlock >= 0 && PosXBlock < mapMeshes.GetLength(0) && PosYBlock >= 0 && PosYBlock < mapMeshes.GetLength(1))
            for (int zz = PosZ - 1; zz >= zmin; zz--)
            {
                if (zz >= mapMeshes.GetLength(2))
                    continue;
                if (!blockDirtyBits[PosXBlock, PosYBlock, zz] && !liquidBlockDirtyBits[PosXBlock, PosYBlock, zz])
                {
                    continue;
                }
                //If we were not able to add it to the queue, don't try any more till next fame.
                if (!mesher.Enqueue(new DFCoord(PosXBlock * MapCoordinates.BlockSize, PosYBlock * MapCoordinates.BlockSize, zz), blockDirtyBits[PosXBlock, PosYBlock, zz], liquidBlockDirtyBits[PosXBlock, PosYBlock, zz]))
                    return;
                blockDirtyBits[PosXBlock, PosYBlock, zz] = false;
                liquidBlockDirtyBits[PosXBlock, PosYBlock, zz] = false;
                queueCount++;
                if (queueCount > GameSettings.Instance.meshing.queueLimit)
                    return;
                if (stopwatch.ElapsedMilliseconds > timeout)
                    return;
            }
        for (int zz = PosZ - 1; zz >= zmin; zz--)
        {
            if (zz >= mapMeshes.GetLength(2))
                continue;

            for (int yy = ymin; yy < ymax; yy++)
                for (int xx = xmin; xx < xmax; xx++)
                {
                    if (!blockDirtyBits[xx, yy, zz] && !liquidBlockDirtyBits[xx, yy, zz])
                    {
                        continue;
                    }

                    //If we were not able to add it to the queue, don't try any more till next fame.
                    if (!mesher.Enqueue(new DFCoord(xx * MapCoordinates.BlockSize, yy * MapCoordinates.BlockSize, zz), blockDirtyBits[xx, yy, zz], liquidBlockDirtyBits[xx, yy, zz]))
                        return;
                    blockDirtyBits[xx, yy, zz] = false;
                    liquidBlockDirtyBits[xx, yy, zz] = false;
                    queueCount++;
                    if (queueCount > GameSettings.Instance.meshing.queueLimit)
                        return;
                    if (stopwatch.ElapsedMilliseconds > timeout)
                        return;
                }
        }
        for (int zz = PosZ; zz < zmax; zz++)
        {
            if (zz < 0)
                continue;
            for (int yy = ymin; yy < ymax; yy++)
                for (int xx = xmin; xx < xmax; xx++)
                {
                    if (!blockDirtyBits[xx, yy, zz] && !liquidBlockDirtyBits[xx, yy, zz])
                    {
                        continue;
                    }

                    //If we were not able to add it to the queue, don't try any more till next fame.
                    if (!mesher.Enqueue(new DFCoord(xx * MapCoordinates.BlockSize, yy * MapCoordinates.BlockSize, zz), blockDirtyBits[xx, yy, zz], liquidBlockDirtyBits[xx, yy, zz]))
                        return;
                    blockDirtyBits[xx, yy, zz] = false;
                    liquidBlockDirtyBits[xx, yy, zz] = false;
                    queueCount++;
                    if (queueCount > GameSettings.Instance.meshing.queueLimit)
                        return;
                    if (stopwatch.ElapsedMilliseconds > timeout)
                        return;
                }
        }
    }

    private BlockMeshSet blockPrefab;

    // Get new meshes from the mesher
    void FetchNewMeshes()
    {

        UnityEngine.Profiling.Profiler.BeginSample("FetchNewMeshes", this);
        while (mesher.HasNewMeshes)
        {
            if (blockPrefab == null)
                blockPrefab = Resources.Load<BlockMeshSet>("MapBlock");
            var newMeshes = mesher.Dequeue().Value;
            int block_x = newMeshes.location.x / MapCoordinates.BlockSize;
            int block_y = newMeshes.location.y / MapCoordinates.BlockSize;
            int block_z = newMeshes.location.z;
            if (mapMeshes[block_x, block_y, block_z] == null)
            {
                mapMeshes[block_x, block_y, block_z] = Instantiate(blockPrefab, MapCoordinates.DFtoUnityCoord(newMeshes.location), Quaternion.identity, transform);
                mapMeshes[block_x, block_y, block_z].name = string.Format("Block_{0}_{1}_{2}", block_x, block_y, block_z);
                mapMeshes[block_x, block_y, block_z].Init();

                SplatManager.Instance.ApplyTerrain(mapMeshes[block_x, block_y, block_z], block_z);
                SplatManager.Instance.ApplyGrass(mapMeshes[block_x, block_y, block_z], block_z);
                SplatManager.Instance.ApplySpatter(mapMeshes[block_x, block_y, block_z], block_z);
            }

            var meshSet = mapMeshes[block_x, block_y, block_z];

            meshSet.LoadMeshes(newMeshes, string.Format("{0}_{1}_{2}", block_x, block_y, block_z));
            meshSet.UpdateVisibility(GetVisibility(block_z));

            //if (newMeshes.collisionMesh != null)
            //{

            //    Mesh collisionMesh = new Mesh();
            //    collisionMesh.name = string.Format("block_collision_{0}_{1}_{2}", block_x, block_y, block_z);
            //    newMeshes.collisionMesh.CopyToMesh(collisionMesh);
            //    meshSet.collisionBlocks.sharedMesh = null;
            //    meshSet.collisionBlocks.sharedMesh = collisionMesh;
            //}
        }
        UnityEngine.Profiling.Profiler.EndSample();
    }

    void ClearMap()
    {
        foreach (var item in mapMeshes)
        {
            if (item != null)
                item.Clear();
        }
        foreach (var item in magmaGlow)
        {
            Destroy(item);
        }
        CreatureManager.Instance.Clear();
        if (MapDataStore.Main != null)
            MapDataStore.Main.Reset();
    }

    private readonly MapScreenshot screenshots = new MapScreenshot();
    private static string exportDirectory => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), Application.productName);


    public void UpdateCenter(Vector3 pos)
    {
        DFCoord dfPos = MapCoordinates.UnityToDFCoord(pos);
        PosXTile = dfPos.x;
        PosYTile = dfPos.y;
        PosZ = dfPos.z + 1;
    }


    public ParticleSystem itemParticleSystem;
    public GameObject mapWindow;
    public GameObject mapCamera;
    public Canvas mainUI;
    //ParticleSystem.Particle[] itemParticles;
    //Dictionary<int, ParticleSystem> customItemParticleSystems = new Dictionary<int, ParticleSystem>();
    //Dictionary<int, ParticleSystem.Particle[]> customItemParticles = new Dictionary<int, ParticleSystem.Particle[]>();
    //Dictionary<int, int> customItemParticleCount = new Dictionary<int, int>();
    //Dictionary<int, bool> noCustomParticleColor = new Dictionary<int, bool>();
    //OpenSimplexNoise noise = new OpenSimplexNoise();
    //void DrawItems()
    //{
    //    return;
    //    if (ContentLoader.Instance == null)
    //        return;
    //    UnityEngine.Profiling.Profiler.BeginSample("DrawItems", this);
    //    if (itemParticles == null)
    //    {
    //        itemParticles = new ParticleSystem.Particle[itemParticleSystem.main.maxParticles];
    //    }
    //    MapDataStore.Tile tempTile = new MapDataStore.Tile(null, new DFCoord(0, 0, 0));
    //    int i = 0;
    //    foreach (var count in customItemParticleSystems)
    //    {
    //        customItemParticleCount[count.Key] = 0;
    //    }
    //    foreach (var item in itemPositions)
    //    {
    //        var pos = item.Key;
    //        if (!(pos.z < PosZ && pos.z >= (PosZ - GameSettings.Instance.rendering.drawRangeDown)))
    //            continue;

    //        for (int index = 0; index < item.Value.Count && index < 100; index++)
    //        {
    //            var currentItem = item.Value[index];
    //            tempTile.material = currentItem.material;
    //            tempTile.construction_item = currentItem.type;
    //            ColorContent colorContent;
    //            MeshContent meshContent;

    //            var part = new ParticleSystem.Particle();
    //            part.startSize = 1;
    //            part.position = MapCoordinates.DFtoUnityCoord(currentItem.pos) + new Vector3(0, MapCoordinates.FloorHeight + 0.1f, 0) + Stacker.SpiralHemisphere(index);
    //            if (ContentLoader.Instance.ColorConfiguration.GetValue(tempTile, MeshLayer.StaticMaterial, out colorContent))
    //                part.startColor = colorContent.color;
    //            else if (materials.ContainsKey(currentItem.material) && materials[currentItem.material].state_color != null)
    //            {
    //                var stateColor = materials[currentItem.material].state_color;
    //                part.startColor = new Color32((byte)stateColor.red, (byte)stateColor.green, (byte)stateColor.blue, 255);
    //            }
    //            else
    //                part.startColor = Color.gray;

    //            if (currentItem.dye != null)
    //            {
    //                part.startColor *= (Color)(new Color32((byte)currentItem.dye.red, (byte)currentItem.dye.green, (byte)currentItem.dye.blue, 255));
    //            }

    //            if (ContentLoader.Instance.ItemMeshConfiguration != null && ContentLoader.Instance.ItemMeshConfiguration.GetValue(tempTile, MeshLayer.StaticMaterial, out meshContent))
    //            {
    //                ParticleSystem partSys;
    //                if (!customItemParticleSystems.ContainsKey(meshContent.UniqueIndex))
    //                {
    //                    partSys = Instantiate(itemParticleSystem);
    //                    partSys.transform.parent = transform;
    //                    var renderer = partSys.GetComponent<ParticleSystemRenderer>();
    //                    Mesh mesh = new Mesh();
    //                    if (meshContent.MeshData.ContainsKey(MeshLayer.StaticCutout))
    //                    {
    //                        meshContent.MeshData[MeshLayer.StaticCutout].CopyToMesh(mesh);
    //                        noCustomParticleColor[meshContent.UniqueIndex] = false;
    //                    }
    //                    else if (meshContent.MeshData.ContainsKey(MeshLayer.NoMaterialCutout))
    //                    {
    //                        meshContent.MeshData[MeshLayer.NoMaterialCutout].CopyToMesh(mesh);
    //                        noCustomParticleColor[meshContent.UniqueIndex] = true;
    //                    }
    //                    else
    //                    {
    //                        bool copied = false;
    //                        foreach (var backup in meshContent.MeshData)
    //                        {
    //                            backup.Value.CopyToMesh(mesh);
    //                            noCustomParticleColor[meshContent.UniqueIndex] = false;
    //                            copied = true;
    //                            break;
    //                        }
    //                        if (!copied)
    //                            continue;
    //                    }
    //                    renderer.mesh = mesh;
    //                    if (meshContent.MaterialTexture != null)
    //                        renderer.material.SetTexture("_MainTex", meshContent.MaterialTexture.Texture);
    //                    else
    //                    {
    //                        TextureContent texCon;
    //                        if (ContentLoader.Instance.MaterialTextureConfiguration.GetValue(tempTile, MeshLayer.StaticMaterial, out texCon))
    //                            renderer.material.SetTexture("_MainTex", texCon.Texture);
    //                    }
    //                    if (meshContent.ShapeTexture != null)
    //                        renderer.material.SetTexture("_BumpMap", meshContent.ShapeTexture.Texture);
    //                    else
    //                    {
    //                        NormalContent normalCon;
    //                        if (ContentLoader.Instance.ShapeTextureConfiguration.GetValue(tempTile, MeshLayer.StaticMaterial, out normalCon))
    //                            renderer.material.SetTexture("_BumpMap", normalCon.Texture);
    //                    }
    //                    if (meshContent.SpecialTexture != null)
    //                        renderer.material.SetTexture("_SpecialTex", meshContent.SpecialTexture.Texture);
    //                    customItemParticleSystems[meshContent.UniqueIndex] = partSys;
    //                    customItemParticles[meshContent.UniqueIndex] = new ParticleSystem.Particle[partSys.main.maxParticles];
    //                    customItemParticleCount[meshContent.UniqueIndex] = 0;
    //                }
    //                if (meshContent.Rotation == RotationType.Random)
    //                    part.rotation = (float)noise.eval(pos.x, pos.y, pos.z) * 360;
    //                part.rotation += index * 254.558f;
    //                if (noCustomParticleColor[meshContent.UniqueIndex])
    //                    part.startColor = Color.gray;
    //                customItemParticles[meshContent.UniqueIndex][customItemParticleCount[meshContent.UniqueIndex]] = part;
    //                customItemParticleCount[meshContent.UniqueIndex]++;
    //            }
    //            else
    //            {
    //                itemParticles[i] = part;
    //                i++;
    //            }
    //        }
    //    }
    //    itemParticleSystem.SetParticles(itemParticles, i);
    //    foreach (var sys in customItemParticleSystems)
    //    {
    //        sys.Value.SetParticles(customItemParticles[sys.Key], customItemParticleCount[sys.Key]);
    //    }
    //    UnityEngine.Profiling.Profiler.EndSample();
    //}

}