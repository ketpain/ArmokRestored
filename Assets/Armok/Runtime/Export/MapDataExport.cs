using RemoteFortressReader;
using System.Collections.Generic;
using System.IO;

public static class MapDataExport
{
    public static void SaveTileTypeList(TiletypeList tiles, string fileName)
    {
        if (tiles == null)
            return;
        try
        {
            File.Delete(fileName);
        }
        catch (IOException)
        {
            return;
        }
        using (StreamWriter writer = new StreamWriter(fileName))
        {
            foreach (Tiletype item in tiles.tiletype_list)
            {
                writer.WriteLine(
                    item.id + "," + 
                    "\"" + item.name + "\"," +
                    item.shape + ":" +
                    item.special + ":" +
                    item.material + ":" +
                    item.variant + ":" +
                    item.direction
                    );
            }
        }
    }
    public static void SaveBuildingList(BuildingList buildings, string fileName)
    {
        if (buildings == null)
            return;


        try
        {
            File.Delete(fileName);
        }
        catch (IOException)
        {
            return;
        }
        using (StreamWriter writer = new StreamWriter(fileName))
        {
            foreach (var item in buildings.building_list)
            {
                writer.WriteLine("\"" +
                    item.name + "\",\"" +
                    item.id + "\"," +
                    item.building_type.building_type + ":" +
                    item.building_type.building_subtype + ":" +
                    item.building_type.building_custom
                    );
            }
        }
    }
    class ListContainer
    {
        public List<MaterialDefinition> list = new List<MaterialDefinition>();
    }

    public static void SaveMaterialList(IEnumerable<KeyValuePair<MatPairStruct, MaterialDefinition>> list, string filename)
    {
        string jsonFile = Path.ChangeExtension(filename, "json");
        try
        {
            File.Delete(filename);
            File.Delete(jsonFile);
        }
        catch (IOException)
        {
            return;
        }
        ListContainer container = new ListContainer();
        using (StreamWriter writer = new StreamWriter(filename))
        {
            foreach (var item in list)
            {
                writer.WriteLine("\"" + 
                    item.Value.name + "\",\"" +
                    item.Value.id + "\"," +
                    item.Value.mat_pair.mat_type + "," +
                    item.Value.mat_pair.mat_index
                    );
                container.list.Add(item.Value);
            }
        }
        File.WriteAllText(jsonFile, Newtonsoft.Json.JsonConvert.SerializeObject(container, Newtonsoft.Json.Formatting.Indented));
    }

    public static void SavePlantList(Dictionary<string, Dictionary<string, Dictionary<string, BuildingStruct>>> plants, string fileName)
    {
        if (plants == null)
            return;
        try
        {
            File.Delete(fileName);
        }
        catch (IOException)
        {
            return;
        }
        using (StreamWriter writer = new StreamWriter(fileName))
        {
            foreach (var plant in plants)
            {
                foreach (var growth in plant.Value)
                {
                    foreach (var print in growth.Value)
                    {
                        writer.Write("\"");
                        writer.Write(plant.Key);
                        writer.Write("\",\"");
                        writer.Write(growth.Key);
                        writer.Write("\",\"");
                        writer.Write(print.Key);
                        writer.Write("\",\"");
                        writer.Write(print.Value);
                        writer.Write("\"");
                        writer.WriteLine();
                    }
                }
            }
        }
    }
}
