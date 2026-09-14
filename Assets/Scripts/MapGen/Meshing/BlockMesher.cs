using Armok.Lifecycle;
using DFHack;
using MaterialStore;
using RemoteFortressReader;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;

// Two implementations: single and multithreaded.
// All of the actual meshing code is exactly the same;
// the only difference is in how it's called.

abstract class BlockMesher : ISessionWorker {
    protected volatile bool finished;
    protected readonly ContentLoader content;
    protected Exception failure;
    public abstract bool IsStopped { get; }
    public virtual void RequestStop()
    {
        finished = true;
        lock (requestQueue) Monitor.PulseAll(requestQueue);
    }
    public abstract bool WaitForStop(int milliseconds);
    protected void CheckCancellation() { if (finished) throw new OperationCanceledException(); }

    // If n = 0, single threaded; else multithreaded.
    public static BlockMesher GetMesher (int nThreads) {
        if (nThreads < 0) {
            throw new System.InvalidOperationException(nThreads + " threads? What??");
        } else if (nThreads == 0) {
            return new SingleThreadedMesher();
        } else {
            return new MultiThreadedMesher(nThreads);
        }
    }

    public struct Result {
        public DFCoord location;
        public CPUMesh tiles;
        public CPUMesh stencilTiles;
        public CPUMesh transparentTiles;
        public CPUMesh topTiles;
        public CPUMesh topStencilTiles;
        public CPUMesh topTransparentTiles;
        public CPUMesh water;
        public CPUMesh magma;
        public CPUMesh collisionMesh;
        public CPUMesh terrainMesh;
        public CPUMesh topTerrainMesh;
    }

    protected struct Request {
        public bool tiles;
        public bool liquids;
        public MapDataStore data;
    }

    [ThreadStatic]
    static MeshCombineUtility.MeshInstance[] meshBuffer;
    [ThreadStatic]
    static MeshCombineUtility.MeshInstance[] stencilMeshBuffer;
    [ThreadStatic]
    static MeshCombineUtility.MeshInstance[] transparentMeshBuffer;
    [ThreadStatic]
    static MeshCombineUtility.MeshInstance[] collisionMeshBuffer;
    [ThreadStatic]
    static MeshCombineUtility.MeshInstance[] terrainMeshBuffer;
    [ThreadStatic]
    static float[,] heights;

    static protected void InitBuffers()
    {
        meshBuffer =
            new MeshCombineUtility.MeshInstance[MapCoordinates.BlockSize * MapCoordinates.BlockSize * (int)MeshLayer.StaticCutout];
        stencilMeshBuffer =
            new MeshCombineUtility.MeshInstance[MapCoordinates.BlockSize * MapCoordinates.BlockSize * ((int)MeshLayer.StaticTransparent - (int)MeshLayer.StaticCutout)];
        transparentMeshBuffer =
            new MeshCombineUtility.MeshInstance[MapCoordinates.BlockSize * MapCoordinates.BlockSize * ((int)MeshLayer.Collision - (int)MeshLayer.StaticTransparent)];
        collisionMeshBuffer =
            new MeshCombineUtility.MeshInstance[MapCoordinates.BlockSize * MapCoordinates.BlockSize * ((int)MeshLayer.Count - (int)MeshLayer.Collision)];
        terrainMeshBuffer =
            new MeshCombineUtility.MeshInstance[MapCoordinates.BlockSize * MapCoordinates.BlockSize];
        heights = new float[2, 2];
    }

    // Some queues.
    // All of these need to be locked before access.
    // In general, NEVER LOCK MORE THAN ONE at the same time - 
    // That road leads to deadlock.
    protected readonly Util.UniqueQueue<DFCoord, Request> requestQueue; 
    protected readonly Queue<Result> resultQueue;
    protected readonly Stack<MapDataStore> recycledBlocks; // Object pool!

    protected BlockMesher() {
        content = ContentLoader.Instance;
        // Make queues
        requestQueue = new Util.UniqueQueue<DFCoord, Request>();
        recycledBlocks = new Stack<MapDataStore>();
        resultQueue = new Queue<Result>();

        GC.Collect(); //force a garbage collect after initial load.
    }

    // Needs to be run frequently.
    // Handles either talking to threads (multi) or doing meshing (single)
    public abstract void Poll();

    // Does what you'd expect.
    public void Terminate()
    {
        RequestStop();
        if (!WaitForStop(500)) Debug.LogWarning("Meshing workers are still stopping.");
    }

    /// <summary>
    /// Gets the status of the current generator
    /// </summary>
    /// <returns>status of the threaded mesher, if applicable.</returns>
    public abstract string Status();

    // Enqueue a block for meshing; inclde 
    public bool Enqueue(DFCoord targetLocation, bool tiles, bool liquids) {
        if (finished) return false;
        if (!MapCoordinates.IsBlockCorner(targetLocation)) {
            throw new UnityException("Can't enqueue non-block-corners");
        }
        if (!tiles && !liquids) {
            throw new UnityException("Why mesh something without tiles or liquids?");
        }
        UnityEngine.Profiling.Profiler.BeginSample("BlockMesher.Enqueue");
        // Using our object pool
        MapDataStore targetDataStore = AllocateBlockStore();

        // Copy data
        if (!MapDataStore.Main.CopySliceTo(targetLocation, MapDataStore.BLOCK_SIZE, targetDataStore))
        {
            UnityEngine.Profiling.Profiler.EndSample();
            return true; //it's empty, but it isn't a failure condition.
        }

        // In case we displace another block update
        Request redundant;
        lock (requestQueue)
        {
            // Will be { tiles: false, liquids: false, data: null } if we don't have anything queued
            redundant = requestQueue[targetLocation];

            //if there's no existing queue member, don't add more than needed.
            if (redundant.data == null)
            {
                if (requestQueue.Count >= 16)
                {
                    UnityEngine.Profiling.Profiler.EndSample();
                    return false;
                }
            }

            Request meshRequest = new Request();
            // If either request wanted liquids, do liquids;
            // If either request wanted tiles, do tiles.
            meshRequest.liquids = liquids || redundant.liquids;
            meshRequest.tiles   = tiles   || redundant.tiles;
            meshRequest.data    = targetDataStore;
            requestQueue.EnqueueAndDisplace(targetLocation, meshRequest);
            Monitor.Pulse(requestQueue);
        }
        if (redundant.data != null) {
            lock (recycledBlocks) {
                // Object pooling!
                recycledBlocks.Push(redundant.data);
            }
        }
        UnityEngine.Profiling.Profiler.EndSample();
        return true;
    }

    public Result? Dequeue()
    {
        if (Monitor.TryEnter(resultQueue, 1000))
            try
            {
                if (resultQueue.Count > 0)
                {
                    return resultQueue.Dequeue();
                }
                else
                {
                    return null;
                }
            }
            finally
            {
                Monitor.Exit(resultQueue);
            }
        else
            return null;
    }

    public bool HasNewMeshes {
        get {
            if (Monitor.TryEnter(resultQueue, 1000))
                try
                {
                    return resultQueue.Count > 0;
                }
                finally
                {
                    Monitor.Exit(resultQueue);
                }
            else
                return false;
        }
    }

    // Object pooling!
    protected MapDataStore AllocateBlockStore() {
        lock (recycledBlocks) {
            if (recycledBlocks.Count == 0) {
                // Nothing to recycle
                return new MapDataStore(new BlockCoord(0,0,0));
            } else {
                // Reuse another data store
                return recycledBlocks.Pop ();
            }
        }
    }

    // ACTUAL MESHING CODE!
    // -------------------------------------------------

    protected Result CreateMeshes(Request request)
    {
        CheckCancellation();
        Result result = new Result();
        result.location = request.data.SliceOrigin;
        if (request.liquids)
        {
            GameMap.BeginSample("Liquid Generation");
            result.water = GenerateLiquidSurface(request.data, MapDataStore.WATER_INDEX);
            result.magma = GenerateLiquidSurface(request.data, MapDataStore.MAGMA_INDEX);
            GameMap.EndSample();
        }
        if (request.tiles)
        {
            GameMap.BeginSample("Tile Generation");
            GenerateTiles(request.data, out result.tiles, out result.stencilTiles, out result.transparentTiles, out result.topTiles, out result.topStencilTiles, out result.topTransparentTiles, out result.collisionMesh, out result.terrainMesh, out result.topTerrainMesh);
            GameMap.EndSample();
        }
        return result;
    }

    static int coord2Index(int x, int y)
    {
        return (x * (MapCoordinates.BlockSize + 1)) + y;
    }
    CPUMesh GenerateLiquidSurface(MapDataStore data, int liquid_select)
    {
        int block_x = data.SliceOrigin.x / MapCoordinates.BlockSize;
        int block_y = data.SliceOrigin.y / MapCoordinates.BlockSize;
        int block_z = data.SliceOrigin.z;

        Vector3[] finalVertices = new Vector3[(MapCoordinates.BlockSize + 1) * (MapCoordinates.BlockSize + 1)];
        Vector3[] finalNormals = new Vector3[(MapCoordinates.BlockSize + 1) * (MapCoordinates.BlockSize + 1)];
        Vector2[] finalUVs = new Vector2[(MapCoordinates.BlockSize + 1) * (MapCoordinates.BlockSize + 1)];
        List<int> finalFaces = new List<int>();

        // Is this necessary?
        heights[0,0] = 0;
        heights[0,1] = 0;
        heights[1,0] = 0;
        heights[1,1] = 0;

        for (int xx = 0; xx <= MapCoordinates.BlockSize; xx++)
            for (int yy = 0; yy <= MapCoordinates.BlockSize; yy++)
            {
                CheckCancellation();
                //first find the heights of all tiles sharing one corner.
                for (int xxx = 0; xxx < 2; xxx++)
                    for (int yyy = 0; yyy < 2; yyy++)
                    {
                CheckCancellation();
                        int x = (block_x * MapCoordinates.BlockSize) + xx + xxx - 1;
                        int y = (block_y * MapCoordinates.BlockSize) + yy + yyy - 1;
                        if (x < 0 || y < 0 || x >= MapDataStore.MapSize.x || y >= MapDataStore.MapSize.y)
                        {
                            heights[xxx, yyy] = -1;
                            continue;
                        }
                        var maybeTile = data[x, y, block_z];
                        if (maybeTile == null)
                        {
                            heights[xxx, yyy] = -1;
                            continue;
                        }
                        var tile = maybeTile;
                        if (tile.isWall)
                        {
                            heights[xxx, yyy] = -1;
                            continue;
                        }
                        heights[xxx, yyy] = data.GetLiquidLevel(new DFCoord(x,y,block_z), liquid_select);
                        heights[xxx, yyy] /= 7.0f;
                        if (tile.isFloor)
                        {
                            heights[xxx, yyy] *= (MapCoordinates.TileHeight - MapCoordinates.FloorHeight);
                            heights[xxx, yyy] += MapCoordinates.FloorHeight;
                        }
                        else
                            heights[xxx, yyy] *= MapCoordinates.TileHeight;

                    }

                //now find their average, discaring invalid ones.
                float height = 0;
                float total = 0;
                foreach (var item in heights)
                {
                    if (item < 0)
                        continue;
                    height += item;
                    total++;
                }
                if (total >= 1)
                    height /= total;
                //find the slopes.
                float sx = ((
                    (heights[0, 0] < 0 ? height : heights[0, 0]) +
                    (heights[0, 1] < 0 ? height : heights[0, 1])) / 2) - ((
                    (heights[1, 0] < 0 ? height : heights[1, 0]) +
                    (heights[1, 1] < 0 ? height : heights[1, 1])) / 2);
                float sy = ((
                    (heights[0, 0] < 0 ? height : heights[0, 0]) +
                    (heights[1, 0] < 0 ? height : heights[1, 0])) / 2) - ((
                    (heights[0, 1] < 0 ? height : heights[0, 1]) +
                    (heights[1, 1] < 0 ? height : heights[1, 1])) / 2);
                finalNormals[coord2Index(xx, yy)] = new Vector3(sx, MapCoordinates.TileWidth * 2, -sy);
                finalNormals[coord2Index(xx, yy)].Normalize();

                finalVertices[coord2Index(xx, yy)] = MapCoordinates.DFtoUnityCoord(xx, yy, -MapCoordinates.MapZOffset);
                finalVertices[coord2Index(xx, yy)].x -= MapCoordinates.TileWidth / 2.0f;
                finalVertices[coord2Index(xx, yy)].z += MapCoordinates.TileWidth / 2.0f;
                finalVertices[coord2Index(xx, yy)].y += height;
                finalUVs[coord2Index(xx, yy)] = new Vector2(xx, yy);
            }
        for (int xx = 0; xx < MapCoordinates.BlockSize; xx++)
            for (int yy = 0; yy < MapCoordinates.BlockSize; yy++)
            {
                CheckCancellation();
                if (data.GetLiquidLevel(
                        new DFCoord((block_x * MapCoordinates.BlockSize) + xx, (block_y * MapCoordinates.BlockSize) + yy, block_z),
                        liquid_select) == 0) {
                        continue;
                }
                finalFaces.Add(coord2Index(xx, yy));
                finalFaces.Add(coord2Index(xx + 1, yy));
                finalFaces.Add(coord2Index(xx + 1, yy + 1));

                finalFaces.Add(coord2Index(xx, yy));
                finalFaces.Add(coord2Index(xx + 1, yy + 1));
                finalFaces.Add(coord2Index(xx, yy + 1));
            }
        if (finalFaces.Count > 0) {
            return new CPUMesh(vertices: finalVertices,
                                normals: finalNormals,
                                tangents: null,
                                uv: finalUVs,
                                uv2: null,
                                uv3: null,
                                colors: null,
                                triangles: finalFaces.ToArray());
        } else {
            return null;
        }
    }

    bool GenerateTiles(
        MapDataStore data,
        out CPUMesh tiles,
        out CPUMesh stencilTiles,
        out CPUMesh transparentTiles,
        out CPUMesh topTiles,
        out CPUMesh topStencilTiles,
        out CPUMesh topTransparentTiles,
        out CPUMesh collisionTiles,
        out CPUMesh terrainTiles,
        out CPUMesh topTerrainTiles)
    {
        int block_x = data.SliceOrigin.x / MapCoordinates.BlockSize;
        int block_y = data.SliceOrigin.y / MapCoordinates.BlockSize;
        int block_z = data.SliceOrigin.z;
        int bufferIndex = 0;
        int stencilBufferIndex = 0;
        int transparentBufferIndex = 0;
        int collisionIndex = 0;
        int terrainIndex = 0;
        GameMap.BeginSample("Fill Mesh Buffer");
        for (int i = 0; i < (int)MeshLayer.Count; i++)
        {
            for (int xx = (block_x * MapCoordinates.BlockSize); xx < (block_x + 1) * MapCoordinates.BlockSize; xx++)
                for (int yy = (block_y * MapCoordinates.BlockSize); yy < (block_y + 1) * MapCoordinates.BlockSize; yy++)
                {
                CheckCancellation();
                    if (!data.InSliceBounds(new DFCoord(xx, yy, block_z))) throw new UnityException("OOB");
                    if (data[xx, yy, block_z] == null) continue;

                    if (i < (int)MeshLayer.StaticCutout)
                    {
                        FillMeshBuffer(out meshBuffer[bufferIndex],
                            (MeshLayer)i,
                            data[xx, yy, block_z],
                            MapCoordinates.DFtoUnityCoord(xx - (block_x * MapCoordinates.BlockSize),
                            yy - (block_y * MapCoordinates.BlockSize),
                            -MapCoordinates.MapZOffset));
                        bufferIndex++;
                    }
                    else if (i < (int)MeshLayer.StaticTransparent)
                    {
                        FillMeshBuffer(out stencilMeshBuffer[stencilBufferIndex],
                            (MeshLayer)i,
                            data[xx, yy, block_z],
                            MapCoordinates.DFtoUnityCoord(xx - (block_x * MapCoordinates.BlockSize),
                            yy - (block_y * MapCoordinates.BlockSize),
                            -MapCoordinates.MapZOffset));
                        stencilBufferIndex++;
                    }
                    else if (i < (int)MeshLayer.Collision)
                    {
                        FillMeshBuffer(out transparentMeshBuffer[transparentBufferIndex],
                            (MeshLayer)i,
                            data[xx, yy, block_z],
                            MapCoordinates.DFtoUnityCoord(xx - (block_x * MapCoordinates.BlockSize),
                            yy - (block_y * MapCoordinates.BlockSize),
                            -MapCoordinates.MapZOffset));
                        transparentBufferIndex++;
                    }
                    else if (i < (int)MeshLayer.NaturalTerrain)
                    {
                        FillMeshBuffer(out collisionMeshBuffer[collisionIndex],
                            (MeshLayer)i,
                            data[xx, yy, block_z],
                            MapCoordinates.DFtoUnityCoord(xx - (block_x * MapCoordinates.BlockSize),
                            yy - (block_y * MapCoordinates.BlockSize),
                            -MapCoordinates.MapZOffset));
                        collisionIndex++;
                    }
                    else if (i == (int)MeshLayer.NaturalTerrain)
                    {
                        FillMeshBuffer(out terrainMeshBuffer[terrainIndex],
                            (MeshLayer)i,
                            data[xx, yy, block_z],
                            MapCoordinates.DFtoUnityCoord(xx - (block_x * MapCoordinates.BlockSize),
                            yy - (block_y * MapCoordinates.BlockSize),
                            -MapCoordinates.MapZOffset));
                        terrainIndex++;
                    }
                }
        }
        GameMap.EndSample();
        bool dontCare, success;
        GameMap.BeginSample("Generate Voxels");
        VoxelGenerator voxelGen = new VoxelGenerator();
        if (block_z == 0)
            voxelGen.bottomless = true;
         var naturalTerrain = voxelGen.Triangulate(data);
        GameMap.EndSample();
        GameMap.BeginSample("Combine Meshes");
        terrainTiles = MeshCombineUtility.ColorCombine(terrainMeshBuffer, terrainIndex, out dontCare, false, naturalTerrain);
        topTerrainTiles = MeshCombineUtility.ColorCombine(terrainMeshBuffer, terrainIndex, out dontCare, true);
        stencilTiles = MeshCombineUtility.ColorCombine(stencilMeshBuffer, stencilBufferIndex, out dontCare, false);
        topStencilTiles = MeshCombineUtility.ColorCombine(stencilMeshBuffer, stencilBufferIndex, out dontCare, true);
        transparentTiles = MeshCombineUtility.ColorCombine(transparentMeshBuffer, transparentBufferIndex, out dontCare, false);
        topTransparentTiles = MeshCombineUtility.ColorCombine(transparentMeshBuffer, transparentBufferIndex, out dontCare, true);
        topTiles = MeshCombineUtility.ColorCombine(meshBuffer, bufferIndex, out dontCare, true);
        tiles = MeshCombineUtility.ColorCombine(meshBuffer, bufferIndex, out success, false);
        collisionTiles = MeshCombineUtility.ColorCombine(collisionMeshBuffer, collisionIndex, out dontCare, false, naturalTerrain);
        GameMap.EndSample();

        return success;
    }

    /// <summary>
    /// This is the function that actually decides what mesh and texture each tile gets
    /// </summary>
    /// <param name="buffer">Buffer to put the chosen meshes into for combining</param>
    /// <param name="layer">layer currently being worked on</param>
    /// <param name="tile">The tile to get all the needed info from.</param>
    void FillMeshBuffer(out MeshCombineUtility.MeshInstance buffer, MeshLayer layer, MapDataStore.Tile tile, Vector3 pos)
    {
        buffer = new MeshCombineUtility.MeshInstance();
        Vector2 index1 = Vector2.zero;
        Vector2 index2 = Vector2.zero;
        MeshContent meshContent = null;
        buffer.color = Color.grey;
        if (layer == MeshLayer.Collision)
        {
            if (!content.CollisionMeshConfiguration.GetValue(tile, layer, out meshContent))
            {
                buffer.meshData = null;
                return;
            }
            if (meshContent.MeshData.ContainsKey(MeshLayer.Collision))
                buffer.meshData = meshContent.MeshData[MeshLayer.Collision];
            else if (meshContent.MeshData.ContainsKey(MeshLayer.StaticMaterial))
                buffer.meshData = meshContent.MeshData[MeshLayer.StaticMaterial];
            else
            {
                buffer.meshData = null;
                return;
            }
            buffer.transform = Matrix4x4.TRS(pos, meshContent.GetRotation(tile), Vector3.one);
            buffer.hiddenFaces = CalculateHiddenFaces(tile, meshContent.Rotation);
            return;
        }
        if (content.DesignationMeshConfiguration.GetValue(tile, layer, out meshContent))
        {
            if (!meshContent.MeshData.ContainsKey(layer))
            {
                buffer.meshData = null;
                return;
            }
            buffer.meshData = meshContent.MeshData[layer];
            buffer.transform = Matrix4x4.TRS(pos, meshContent.GetRotation(tile), Vector3.one);

            index1.x = ContentLoader.GetPatternIndex(tile.DesignationMat);
            buffer.color = ContentLoader.GetColor(tile.DesignationMat);
            if (meshContent.ShapeTexture != null)
                index1.y = meshContent.ShapeTexture.StorageIndex;
            else
                index1.y = content.DefaultShapeTexIndex;
            if (meshContent.SpecialTexture != null)
                index2.x = meshContent.SpecialTexture.ArrayIndex;
            else
                index2.x = content.DefaultSpecialTexIndex;

            buffer.uv1Transform = Matrix4x4.identity;
            buffer.uv2Force = index1;
            buffer.uv3Force = index2;
            buffer.hiddenFaces = CalculateHiddenFaces(tile, meshContent.Rotation);
            return;
        }

        var matColor = ContentLoader.GetColor(tile.GetMaterial(layer));
        var matPatternIndex = ContentLoader.GetPatternIndex(tile.GetMaterial(layer));

        switch (layer)
        {
            case MeshLayer.GrowthMaterial:
            case MeshLayer.GrowthCutout:
            case MeshLayer.GrowthTransparent:
                matColor = ContentLoader.GetColor(new MatPairStruct(55, 0), tile.GetMaterial(layer));
                break;
            case MeshLayer.GrowthMaterial1:
            case MeshLayer.GrowthCutout1:
            case MeshLayer.GrowthTransparent1:
                matColor = ContentLoader.GetColor(new MatPairStruct(55, 1), tile.GetMaterial(layer));
                break;
            case MeshLayer.GrowthMaterial2:
            case MeshLayer.GrowthCutout2:
            case MeshLayer.GrowthTransparent2:
                matColor = ContentLoader.GetColor(new MatPairStruct(55, 2), tile.GetMaterial(layer));
                break;
            case MeshLayer.GrowthMaterial3:
            case MeshLayer.GrowthCutout3:
            case MeshLayer.GrowthTransparent3:
                matColor = ContentLoader.GetColor(new MatPairStruct(55, 3), tile.GetMaterial(layer));
                break;
            default:
                break;
        }

        switch (layer)
        {
            case MeshLayer.GrowthMaterial:
            case MeshLayer.GrowthMaterial1:
            case MeshLayer.GrowthMaterial2:
            case MeshLayer.GrowthMaterial3:
            case MeshLayer.GrowthCutout:
            case MeshLayer.GrowthCutout1:
            case MeshLayer.GrowthCutout2:
            case MeshLayer.GrowthCutout3:
            case MeshLayer.GrowthTransparent:
            case MeshLayer.GrowthTransparent1:
            case MeshLayer.GrowthTransparent2:
            case MeshLayer.GrowthTransparent3:
                {
                    switch (tile.tiletypeMaterial)
                    {
                        case TiletypeMaterial.PLANT:
                        case TiletypeMaterial.ROOT:
                        case TiletypeMaterial.TREE_MATERIAL:
                        case TiletypeMaterial.MUSHROOM:
                            if (!content.GrowthMeshConfiguration.GetValue(tile, layer, out meshContent))
                            {
                                buffer.meshData = null;
                                return;
                            }
                            break;
                        default:
                            buffer.meshData = null;
                            return;
                    }
                }
                break;
            default:
                {
                    if (layer == MeshLayer.NaturalTerrain)
                    {
                        if (VoxelGenerator.IsNatural(tile) && !VoxelGenerator.HandleShape(tile) && !VoxelGenerator.UseBoth(tile))
                            layer = MeshLayer.StaticMaterial;
                        else
                        {
                            buffer.meshData = null;
                            return;
                        }
                    }
                    else if (VoxelGenerator.IsNatural(tile) && !VoxelGenerator.UseBoth(tile))
                    {
                        buffer.meshData = null;
                        return;
                    }
                    if (!content.TileMeshConfiguration.GetValue(tile, layer, out meshContent))
                    {
                        buffer.meshData = null;
                        return;
                    }
                }
                break;
        }

        //Use the transparent shader instead of the opaque shader if the material is transparent.
        if(matColor.a < 0.5f)
        {
            switch (layer)
            {
                case MeshLayer.StaticMaterial:
                case MeshLayer.BaseMaterial:
                case MeshLayer.LayerMaterial:
                case MeshLayer.VeinMaterial:
                    buffer.meshData = null;
                    return;
                case MeshLayer.StaticTransparent:
                    layer = MeshLayer.StaticMaterial;
                    break;
                case MeshLayer.BaseTransparent:
                    layer = MeshLayer.BaseMaterial;
                    break;
                case MeshLayer.LayerTransparent:
                    layer = MeshLayer.LayerMaterial;
                    break;
                case MeshLayer.VeinTransparent:
                    layer = MeshLayer.VeinMaterial;
                    break;
                default:
                    break;
            }
        }

        if (!meshContent.MeshData.ContainsKey(layer))
        {

            buffer.meshData = null;
            return;
        }
        buffer.meshData = meshContent.MeshData[layer];
        buffer.transform = Matrix4x4.TRS(pos, meshContent.GetRotation(tile), Vector3.one);
        Matrix4x4 shapeTextTransform = content.DefaultShapeTexTransform;
        Matrix4x4 specialTexTransform = Matrix4x4.identity;

        NormalContent tileTexContent;
        if (meshContent.ShapeTexture == null)
        {
            if (content.ShapeTextureConfiguration.GetValue(tile, layer, out tileTexContent))
            {
                shapeTextTransform = tileTexContent.UVTransform;
                index1.y = tileTexContent.StorageIndex;
            }
        }
        else
        {
            shapeTextTransform = meshContent.ShapeTexture.UVTransform;
            index1.y = meshContent.ShapeTexture.StorageIndex;
        }

        index1.x = matPatternIndex;



        if (meshContent.SpecialTexture != null)
        {
            specialTexTransform = meshContent.SpecialTexture.UVTransform;
            index2.x = meshContent.SpecialTexture.ArrayIndex;
        }
        else
        {
            specialTexTransform = content.DefaultSpecialTexTransform;
            index2.x = content.DefaultSpecialTexIndex;
        }

        buffer.color = matColor;

        buffer.uv1Transform = Matrix4x4.identity;
        buffer.uv2Force = index1;
        buffer.uv3Force = index2;

        buffer.hiddenFaces = CalculateHiddenFaces(tile, meshContent.Rotation);
    }

    private MeshCombineUtility.HiddenFaces CalculateHiddenFaces(MapDataStore.Tile tile, RotationType rotation)
    {
        MeshCombineUtility.HiddenFaces hiddenFaces = MeshCombineUtility.HiddenFaces.None;
        if (rotation == RotationType.None)
        {
            if (tile.North != null && (tile.North.isWall || tile.North.Hidden))
                hiddenFaces |= MeshCombineUtility.HiddenFaces.North;
            if (tile.South != null && (tile.South.isWall || tile.South.Hidden))
                hiddenFaces |= MeshCombineUtility.HiddenFaces.South;
            if (tile.East != null && (tile.East.isWall || tile.East.Hidden))
                hiddenFaces |= MeshCombineUtility.HiddenFaces.East;
            if (tile.West != null && (tile.West.isWall || tile.West.Hidden))
                hiddenFaces |= MeshCombineUtility.HiddenFaces.West;
        }
        if (rotation != RotationType.TreeRound && rotation != RotationType.TreeRoundTall)//these two rotations don't give a flat rotation.
        {
            if (tile.Up != null && (tile.Up.isSolidBase || tile.Up.Hidden))
                hiddenFaces |= MeshCombineUtility.HiddenFaces.Up;
            if (tile.Down != null && (tile.Down.isWall || tile.Down.Hidden))
                hiddenFaces |= MeshCombineUtility.HiddenFaces.Down;
        }
        return hiddenFaces;
    }
}

// For meshing on the Unity thread.
// Mostly for debugging purposes.
sealed class SingleThreadedMesher : BlockMesher {
    public SingleThreadedMesher()
    {
        InitBuffers();
    }

    public override void Poll()
    {
        if (finished || content == null) return;
        StatsReadout.QueueLength = requestQueue.Count;
        if (requestQueue.Count == 0) return;
        Request req = requestQueue.Dequeue();
        GameMap.BeginSample("Generate Meshes");
        resultQueue.Enqueue(CreateMeshes(req));
        recycledBlocks.Push(req.data);
        GameMap.EndSample();
    }

    public override string Status()
    {
        return "Single threaded.";
    }

    public override bool IsStopped => true;
    public override bool WaitForStop(int milliseconds) => true;
}

// Now we're cooking with charcoal.
sealed class MultiThreadedMesher : BlockMesher {
    private readonly Thread[] threads;
    private long averageTicks;

    public MultiThreadedMesher(int nThreads = 1)
    {
        threads = new Thread[nThreads];
        for (int i = 0; i < nThreads; i++)
        {
            threads[i] = new Thread(MeshForever) { IsBackground = true, Name = "Armok meshing " + i };
            threads[i].Start();
        }
    }

    public override bool IsStopped => Array.TrueForAll(threads, thread => !thread.IsAlive);

    public override bool WaitForStop(int milliseconds)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        bool stopped = true;
        foreach (var thread in threads)
            stopped &= thread.Join(Math.Max(0, milliseconds - (int)watch.ElapsedMilliseconds));
        return stopped;
    }

    public override void Poll()
    {
        var error = Volatile.Read(ref failure);
        if (error != null) { SessionRuntime.Stop(SessionRuntime.Current, error); return; }
        lock (requestQueue) StatsReadout.QueueLength = requestQueue.Count;
        StatsReadout.BlockProcessTime = TimeSpan.FromSeconds(
            Interlocked.Read(ref averageTicks) / (double)System.Diagnostics.Stopwatch.Frequency);
    }

    private void MeshForever()
    {
        try
        {
            InitBuffers();
            while (!finished)
            {
                Request work;
                lock (requestQueue)
                {
                    while (!finished && requestQueue.Count == 0) Monitor.Wait(requestQueue);
                    if (finished) return;
                    work = requestQueue.Dequeue();
                }
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Result result = CreateMeshes(work);
                CheckCancellation();
                Interlocked.Exchange(ref averageTicks, watch.ElapsedTicks);
                lock (resultQueue) { if (!finished) resultQueue.Enqueue(result); }
                lock (recycledBlocks) recycledBlocks.Push(work.data);
            }
        }
        catch (OperationCanceledException) when (finished) { }
        catch (Exception error)
        {
            Volatile.Write(ref failure, error);
            RequestStop();
        }
    }

    public override string Status()
    {
        var result = new StringBuilder();
        foreach (var thread in threads) result.Append(thread.Name).Append(": ").AppendLine(thread.ThreadState.ToString());
        return result.ToString();
    }
}
