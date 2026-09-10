// TimberbotMapText.cs. Plain-text map renderer for the MCP get_region tool.
//
// Unity-free. Renders a /api/tiles JSON payload (format=json) as a north-up
// character grid with a legend. No ANSI escapes: measured on synthetic
// regions this costs ~0.6-0.85 tokens per cell vs ~57 tokens per cell for
// the raw JSON (docs/audit/00-repo-audit.md §6.3).
//
// Encoding (one character per cell, highest-priority rule wins):
//   '@' building entrance tile          '~' water (badwater '!')
//   letter  occupant (see Symbol())      '+' dead tree / stump
//   digit   terrain height % 10 on an empty tile ('.' when height is 0)
//   '?'     tile missing from the payload
// Elevation of occupied tiles is listed per z-level in the legend.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Timberbot
{
    public static class TimberbotMapText
    {
        private static readonly (string key, char glyph, string label)[] Symbols =
        {
            ("Path", '=', "path"), ("Stairs", '/', "stairs"), ("Platform", '_', "platform"),
            ("DistrictCenter", 'D', "district center"),
            ("Pine", 'T', "tree"), ("Birch", 'T', "tree"), ("Oak", 'T', "tree"), ("Maple", 'T', "tree"),
            ("Chestnut", 'T', "tree"), ("Mangrove", 'T', "tree"),
            ("Bush", 'B', "bush"), ("berry", 'B', "bush"), ("Shrub", 'B', "bush"),
            ("Ruin", 'R', "ruin"), ("Relic", 'R', "ruin"),
            ("Pump", 'P', "water pump"), ("Tank", 'W', "water tank"),
            ("Dam", 'X', "dam/levee/floodgate"), ("Levee", 'X', "dam/levee/floodgate"),
            ("Floodgate", 'X', "dam/levee/floodgate"), ("Sluice", 'X', "dam/levee/floodgate"),
            ("Warehouse", '$', "storage"), ("Pile", '$', "storage"),
            ("Lumberjack", 'L', "lumberjack"), ("Forester", 'f', "forester"), ("Gatherer", 'G', "gatherer"),
            ("Farm", 'F', "farm/food"), ("Food", 'F', "farm/food"), ("Grill", 'F', "farm/food"), ("Bakery", 'F', "farm/food"),
            ("Lodge", 'H', "housing"), ("Rowhouse", 'H', "housing"), ("Barrack", 'H', "housing"),
            ("Mill", 'M', "mill/workshop"), ("Workshop", 'M', "mill/workshop"),
            ("Inventor", 'S', "science"), ("Numbercruncher", 'S', "science"),
            ("PowerWheel", 'E', "power"), ("PowerShaft", 'E', "power"), ("Engine", 'E', "power"), ("Wheel", 'E', "power"),
            ("Builder", 'K', "builders/hauling"), ("Hauling", 'K', "builders/hauling"),
        };

        public static string Render(JObject tiles, int x1, int y1, int x2, int y2)
        {
            if (x2 < x1) { var t = x1; x1 = x2; x2 = t; }
            if (y2 < y1) { var t = y1; y1 = y2; y2 = t; }
            var byPos = new Dictionary<(int, int), JObject>();
            var arr = tiles?["tiles"] as JArray;
            if (arr != null)
                foreach (var tok in arr)
                    if (tok is JObject o) byPos[(o.Value<int>("x"), o.Value<int>("y"))] = o;

            var legend = new SortedDictionary<char, string>();
            var zLevels = new SortedSet<int>();
            var sb = new StringBuilder();
            sb.Append("region x").Append(x1).Append("..").Append(x2).Append(" y").Append(y1).Append("..").Append(y2)
              .Append(" (north = top, ").Append((x2 - x1 + 1) * (y2 - y1 + 1)).Append(" cells)\n");

            for (int y = y2; y >= y1; y--)
            {
                sb.Append(y.ToString(CultureInfo.InvariantCulture).PadLeft(3)).Append(' ');
                for (int x = x1; x <= x2; x++)
                {
                    if (!byPos.TryGetValue((x, y), out var tile)) { sb.Append('?'); legend['?'] = "no data"; continue; }
                    int terrain = tile.Value<int?>("terrain") ?? 0;
                    zLevels.Add(terrain);
                    string occupant = TopOccupant(tile);
                    bool entrance = Truthy(tile["entrance"]);
                    if (entrance && occupant == null) { sb.Append('@'); legend['@'] = "entrance"; continue; }
                    if (occupant != null)
                    {
                        char g; string label;
                        if (Truthy(tile["dead"])) { g = '+'; label = "dead tree (buildable)"; }
                        else
                        {
                            (g, label) = Symbol(occupant);
                            if (g == 'T' && Truthy(tile["seedling"])) { g = 't'; label = "seedling"; }
                        }
                        sb.Append(g); legend[g] = label; continue;
                    }
                    double water = tile.Value<double?>("water") ?? 0;
                    if (water > 0)
                    {
                        double bad = tile.Value<double?>("badwater") ?? 0;
                        if (bad > 0) { sb.Append('!'); legend['!'] = "contaminated water"; }
                        else { sb.Append('~'); legend['~'] = "water"; }
                        continue;
                    }
                    if (terrain <= 0) { sb.Append('.'); legend['.'] = "ground z=0"; }
                    else { sb.Append((char)('0' + terrain % 10)); legend['0'] = "digit = terrain height % 10 (empty tile)"; }
                }
                sb.Append('\n');
            }
            sb.Append("    ");
            for (int x = x1; x <= x2; x++) sb.Append((char)('0' + Math.Abs(x) % 10));
            sb.Append('\n');
            sb.Append("legend:");
            foreach (var kv in legend) sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value).Append(';');
            sb.Append('\n');
            if (zLevels.Count > 0)
            {
                sb.Append("z levels present:");
                foreach (var z in zLevels) sb.Append(' ').Append(z);
                sb.Append(". Use format=json for water depth, moisture and per-occupant z.\n");
            }
            return sb.ToString();
        }

        // Highest-z occupant name from either the json array form or the toon "Name:z+Name:z" string.
        private static string TopOccupant(JObject tile)
        {
            var occ = tile["occupants"];
            if (occ == null || occ.Type == JTokenType.Null) return null;
            if (occ is JArray list)
            {
                string best = null; int bestZ = int.MinValue;
                foreach (var o in list)
                {
                    var z = o.Value<int?>("z") ?? 0; var name = o.Value<string>("name");
                    if (name != null && z >= bestZ) { bestZ = z; best = name; }
                }
                return best;
            }
            if (occ.Type == JTokenType.String)
            {
                var s = occ.Value<string>();
                if (string.IsNullOrEmpty(s)) return null;
                string best = null; int bestZ = int.MinValue;
                foreach (var part in s.Split('+'))
                {
                    var idx = part.LastIndexOf(':');
                    var name = idx > 0 ? part.Substring(0, idx) : part;
                    int z = 0;
                    if (idx > 0) int.TryParse(part.Substring(idx + 1), out z);
                    if (z >= bestZ) { bestZ = z; best = name; }
                }
                return best;
            }
            return null;
        }

        private static bool Truthy(JToken t)
        {
            if (t == null) return false;
            if (t.Type == JTokenType.Boolean) return t.Value<bool>();
            if (t.Type == JTokenType.Integer) return t.Value<int>() != 0;
            return false;
        }

        public static (char glyph, string label) Symbol(string occupant)
        {
            foreach (var (key, glyph, label) in Symbols)
                if (occupant.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return (glyph, label);
            // Fallback: first letter of the canonical name, upper-case, so unknown buildings stay visible.
            var dot = occupant.IndexOf('.');
            var name = dot > 0 ? occupant.Substring(0, dot) : occupant;
            var c = name.Length > 0 ? char.ToUpperInvariant(name[0]) : '#';
            if (!char.IsLetter(c)) c = '#';
            return (c, "building: " + name);
        }
    }
}
