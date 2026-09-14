using MaterialStore;
using System.Collections.Generic;
using UnityEngine;

public enum MatBasic
{
    INVALID = -1,
    INORGANIC = 0,
    AMBER = 1,
    CORAL = 2,
    GREEN_GLASS = 3,
    CLEAR_GLASS = 4,
    CRYSTAL_GLASS = 5,
    ICE = 6,
    COAL = 7,
    POTASH = 8,
    ASH = 9,
    PEARLASH = 10,
    LYE = 11,
    MUD = 12,
    VOMIT = 13,
    SALT = 14,
    FILTH = 15,
    FILTH_FROZEN = 16,
    UNKOWN_FROZEN = 17,
    GRIME = 18,
    ICHOR = 20,
    LEATHER = 37,
    BLOOD_1 = 39,
    BLOOD_2 = 40,
    BLOOD_3 = 41,
    BLOOD_4 = 42,
    BLOOD_5 = 43,
    BLOOD_6 = 44,
    BLOOD_NAMED = 242,
    PLANT = 419,
    WOOD = 420,
    PLANTCLOTH = 421,

    // filthy hacks to get interface stuff
    DESIGNATION = -2,
    CONSTRUCTION = -3,

}

/// <summary>
/// Loaded content shared by presentation and meshing. Only ContentLoader builds and publishes it.
/// Configuration trees and texture layout are fixed after publication; sprite selection, material
/// lookup caches, seasonal DF colors and transparent material variants retain their existing mutability.
/// Unity material/texture operations and sprite access remain on the main thread.
/// </summary>
public sealed class ContentCatalog
{
    public static ContentCatalog Current { get; private set; }
    public CreatureSpriteManager SpriteManager { get; }

    internal ContentCatalog(CreatureSpriteManager sprites) => SpriteManager = sprites;

    internal void Publish() => Current = this;

    internal void Unpublish()
    {
        if (ReferenceEquals(Current, this)) Current = null;
    }

    internal static void ResetForNewScene()
    {
        Current = null;
        transparentMaterialVersions.Clear();
    }

    public static MatBasic lookupMaterialType(string value)
    {
        if (value == null)
            return MatBasic.INVALID;
        switch (value)
        {
            case "Stone":
                return MatBasic.INORGANIC;
            case "Metal":
                return MatBasic.INORGANIC;
            case "Inorganic":
                return MatBasic.INORGANIC;
            case "GreenGlass":
                return MatBasic.GREEN_GLASS;
            case "Wood":
                return MatBasic.WOOD;
            case "Plant":
                return MatBasic.PLANT;
            case "Ice":
                return MatBasic.ICE;
            case "ClearGlass":
                return MatBasic.CLEAR_GLASS;
            case "CrystalGlass":
                return MatBasic.CRYSTAL_GLASS;
            case "PlantCloth":
                return MatBasic.PLANTCLOTH;
            case "Leather":
                return MatBasic.LEATHER;
            case "Vomit":
                return MatBasic.VOMIT;
            case "Designation":
                return MatBasic.DESIGNATION;
            case "Construction":
                return MatBasic.CONSTRUCTION;
            default:
                return MatBasic.INVALID;
        }
    }

    public Texture2DArray PatternTextureArray { get; internal set; }
    public float PatternTextureDepth { get; internal set; }

    public TextureStorage shapeTextureStorage { get; internal set; }
    public TextureStorage specialTextureStorage { get; internal set; }

    /// <summary>
    /// Get the color associated with a material, falling back to DF state color.
    /// </summary>
    /// <param name="mat"></param>
    /// <returns></returns>
    public static Color GetColor(MatPairStruct mat)
    {
        MaterialTextureSet textureSet;
        if (MaterialCollection.Instance.TryGetValue(mat, out textureSet))
        {
            if(!textureSet.useDFColor)
                return textureSet.color;
        }
        if (!MaterialRaws.Instance.ContainsKey(mat))
            return new Color32(128, 128, 128, 128);
        var stateColor = MaterialRaws.Instance[mat].state_color;
        if (stateColor == null)
            return new Color32(128, 128, 128, 128);
        return new Color32((byte)stateColor.red, (byte)stateColor.green, (byte)stateColor.blue, 128);
    }

    public enum GrowthPeriod
    {
        Fresh,
        Current,
        Stale
    }

    public static Color GetGrowthColor(GrowthPeriod period, int growthID, MatPairStruct mat)
    {
        if (period == GrowthPeriod.Fresh)
            return GetColor(mat);
        if (mat.mat_index >= DFConnection.Instance.NetPlantRawList.plant_raws.Count)
            return GetColor(mat);
        if (growthID >= DFConnection.Instance.NetPlantRawList.plant_raws[mat.mat_index].growths.Count)
            return GetColor(mat);

        var growth = DFConnection.Instance.NetPlantRawList.plant_raws[mat.mat_index].growths[growthID];
        int currentTicks = TimeHolder.DisplayedTime.CurrentYearTicks;

        RemoteFortressReader.GrowthPrint freshPrints = null;
        RemoteFortressReader.GrowthPrint currentPrint = null;
        RemoteFortressReader.GrowthPrint stalePrint = null;

        int minTicks = int.MaxValue;
        int maxTicks = int.MinValue;

        foreach (var print in growth.prints)
        {
            if ((print.timing_start == -1 || print.timing_start <= currentTicks) && (print.timing_end == -1 || print.timing_end >= currentTicks))
                currentPrint = print;
            if(print.timing_start == -1 || print.timing_start < minTicks)
            {
                minTicks = print.timing_start;
                freshPrints = print;
            }
            if (print.timing_end == -1 || print.timing_end > maxTicks)
            {
                maxTicks = print.timing_end;
                stalePrint = print;
            }
        }
        if (currentPrint == null)
            currentPrint = stalePrint;
        switch (period)
        {
            case GrowthPeriod.Current:
                return DfColor.MorphColor(GetColor(mat), freshPrints.color, currentPrint.color);
            case GrowthPeriod.Stale:
                return DfColor.MorphColor(GetColor(mat), freshPrints.color, stalePrint.color);
        }
        return GetColor(mat);
    }

    /// <summary>
    /// Gets a color for a given item type and material combination, including any seasonal variations.
    /// </summary>
    /// <param name="item">Item type</param>
    /// <param name="mat">Material</param>
    /// <returns></returns>
    public static Color GetColor(MatPairStruct item, MatPairStruct mat)
    {
        switch (item.mat_type)
        {
            case 55://PLANT_GROWTH
                {
                    return GetGrowthColor(GrowthPeriod.Current, item.mat_index, mat);
                }
            default:
                break;
        }
        return GetColor(mat);
    }

    /// <summary>
    /// Get the material color of an item including dye
    /// </summary>
    /// <param name="item">item to get the color from</param>
    /// <returns></returns>
    public static Color GetColor(RemoteFortressReader.Item item)
    {
        var color = GetColor(item.type, item.material);
        var dye = item.dye;
        if (dye == null)
            return color;
        if (!Application.isPlaying)
        {
            if (dye.red + dye.green + dye.blue == 0)
                return color;
        }
        color *= (Color)new Color32((byte)dye.red, (byte)dye.green, (byte)dye.blue, 255);
        return color;
    }

    public static int GetPatternIndex(MatPairStruct mat)
    {
        MaterialTextureSet textureSet;
        if (MaterialCollection.Instance.TryGetValue(mat, out textureSet))
        {
            return textureSet.patternIndex;
        }
        if (Current == null)
            return 0;
        return Current.DefaultMatTexIndex;
    }

    public static int GetShapeIndex(MatPairStruct mat)
    {
        MaterialTextureSet textureSet;
        if (MaterialCollection.Instance.TryGetValue(mat, out textureSet))
        {
            return textureSet.shapeIndex;
        }
        if (Current == null)
            return 0;
        return Current.DefaultShapeTexIndex;
    }

    public int DefaultMatTexIndex { get; internal set; }

    public float DefaultMatTexArrayIndex
    {
        get
        {
            return 0;
        }
    }



    public int DefaultShapeTexIndex { get; internal set; }

    public Matrix4x4 DefaultShapeTexTransform
    {
        get
        {
            return shapeTextureStorage.getUVTransform(DefaultShapeTexIndex);
        }
    }

    public int DefaultSpecialTexIndex { get; internal set; }

    public Matrix4x4 DefaultSpecialTexTransform
    {
        get
        {
            return specialTextureStorage.getUVTransform(DefaultSpecialTexIndex);
        }
    }


    public TileConfiguration<NormalContent> ShapeTextureConfiguration { get; internal set; }
    public TileConfiguration<MeshContent> TileMeshConfiguration { get; internal set; }
    public TileConfiguration<MeshContent> GrowthMeshConfiguration { get; internal set; }
    public TileConfiguration<LayerContent> MaterialLayerConfiguration { get; internal set; }
    //public TileConfiguration<MeshContent> BuildingMeshConfiguration { get; internal set; }
    //public TileConfiguration<NormalContent> BuildingShapeTextureConfiguration { get; internal set; }
    public TileConfiguration<MeshContent> DesignationMeshConfiguration { get; internal set; }
    public TileConfiguration<MeshContent> CollisionMeshConfiguration { get; internal set; }
    //public TileConfiguration<MeshContent> BuildingCollisionMeshConfiguration { get; internal set; }
    public TileConfiguration<MeshContent> ItemMeshConfiguration { get; internal set; }

    static Dictionary<EntityId, Material> transparentMaterialVersions = new Dictionary<EntityId, Material>();

    public static Material getFinalMaterial(Material material, float alpha)
    {
        EntityId entityId = material.GetEntityId();
        if (!transparentMaterialVersions.ContainsKey(entityId) || transparentMaterialVersions[entityId] == null)
        {
            switch(material.shader.name)
            {
                case "Standard":
                    Debug.LogWarning(material.name + " Has a standard shader!");
                    transparentMaterialVersions[entityId] = material;
                    break;
                case "Art/ArtImageVertexOpaque":
                    transparentMaterialVersions[entityId] = material;
                    break;
                case "Art/SingleImage":
                    {
                        var mat = new Material(material);
                        mat.shader = Shader.Find("Art/SingleImage Transparent");
                        mat.name += " Transparent";
                        transparentMaterialVersions[entityId] = mat;
                    }
                    break;
                default:
                    {
                        var mat = new Material(material);
                        mat.shader = Shader.Find("Building/Transparent");
                        mat.name += " Transparent";
                        transparentMaterialVersions[entityId] = mat;
                    }
                    break;
            }
        }

        if (alpha < 0.5f)
            return transparentMaterialVersions[entityId];
        else
            return material;
    }
}
