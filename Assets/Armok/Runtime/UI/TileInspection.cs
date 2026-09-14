using System.Text;
using Building;
using DFHack;
using RemoteFortressReader;

// Reads the current main-thread map snapshot. Inspection never sends commands to DF.
public static class TileInspection
{
    public static bool TryDescribe(DFCoord position, out string title, out string details)
    {
        title = "Tile";
        details = "Tile data has not loaded yet.";
        var tile = MapDataStore.Main?[position];
        if (tile == null) return false;
        if (tile.Hidden)
        {
            title = "Unexplored";
            details = "This tile has not been revealed.";
            return true;
        }

        var types = DFConnection.Instance?.NetTiletypeList?.tiletype_list;
        if (types != null && tile.tileType >= 0 && tile.tileType < types.Count)
            title = Friendly(types[tile.tileType].shape.ToString());

        var text = new StringBuilder();
        text.Append("Material   ").AppendLine(MaterialName(tile.material));
        if (tile.layer_material != tile.material && tile.layer_material.mat_type >= 0)
            text.Append("Layer   ").AppendLine(MaterialName(tile.layer_material));
        if (tile.waterLevel > 0) text.Append("Water   ").Append(tile.waterLevel).AppendLine(" / 7");
        if (tile.magmaLevel > 0) text.Append("Magma   ").Append(tile.magmaLevel).AppendLine(" / 7");
        if (tile.digDesignation != TileDigDesignation.NO_DIG)
            text.Append("Designation   ").AppendLine(Friendly(tile.digDesignation.ToString()));

        var building = BuildingManager.GetBuildingInfo(position);
        if (building != null)
        {
            BuildingDefinition definition;
            string name = GameMap.buildings != null && GameMap.buildings.TryGetValue(building.building_type, out definition)
                ? (string.IsNullOrEmpty(definition.name) ? Friendly(definition.id) : definition.name)
                : "Building";
            text.Append("Building   ").AppendLine(name);
        }

        var units = CreatureManager.Instance?.Units;
        int count = 0;
        if (units != null)
            foreach (var unit in units)
            {
                if (unit == null || !CreatureManager.IsValidCreature(unit) ||
                    unit.pos_x != position.x || unit.pos_y != position.y || unit.pos_z != position.z) continue;
                count++;
                if (count > 2) continue;
                string race = "Creature";
                if (unit.race != null && CreatureRaws.Instance != null && unit.race.mat_type >= 0 && unit.race.mat_type < CreatureRaws.Instance.Count)
                    race = Friendly(CreatureRaws.Instance[unit.race.mat_type].creature_id);
                text.Append("Unit   ").AppendLine(string.IsNullOrEmpty(unit.name) ? race : unit.name + " · " + race);
            }
        if (count > 2) text.Append("+ ").Append(count - 2).AppendLine(" more units");
        details = text.ToString().TrimEnd();
        return true;
    }

    private static string MaterialName(MatPairStruct material)
    {
        MaterialDefinition definition;
        if (MaterialRaws.Instance == null || !MaterialRaws.Instance.TryGetValue(material, out definition)) return "Unknown";
        return string.IsNullOrEmpty(definition.name) ? Friendly(definition.id) : definition.name;
    }

    private static string Friendly(string value)
    {
        if (string.IsNullOrEmpty(value)) return "Unknown";
        value = value.Replace('_', ' ').Replace(':', ' ').ToLowerInvariant();
        return char.ToUpperInvariant(value[0]) + value.Substring(1);
    }
}
