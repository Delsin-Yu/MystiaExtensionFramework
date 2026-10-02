using System.Text;
using System.Text.RegularExpressions;

namespace Mystia.CatalogGen;

public sealed record IdRow(string Pack, string Category, int Id, string Name, string Summary);

public sealed record RecipeRow(string Pack, int Id, int FoodId);

public static class CatalogBuilder
{
    private static readonly (string Suffix, string Category)[] Tables =
    [
        ("FoodTagsLang.txt", "FoodTags"),
        ("BeverageTagsLang.txt", "BeverageTags"),
        ("IngredientsLang.txt", "Ingredients"),
        ("FoodsLang.txt", "Foods"),
        ("BeveragesLang.txt", "Beverages"),
        ("CookersLang.txt", "Cookers"),
        ("IzakayaLang.txt", "Izakayas"),
        ("ItemsLang.txt", "Items"),
        ("BadgesLang.txt", "Badges"),
        ("NormGuestLang.txt", "NormalGuests"),
        ("SpecGuestLang.txt", "SpecialGuests"),
    ];

    private static readonly string[] CategoryOrder =
    [
        "FoodTags",
        "BeverageTags",
        "Ingredients",
        "Foods",
        "Beverages",
        "Recipes",
        "Cookers",
        "Izakayas",
        "Items",
        "Badges",
        "NormalGuests",
        "SpecialGuests",
    ];

    private static readonly string[] PackOrder =
    [
        "Core",
        "DLC1",
        "DLC2",
        "DLC3",
        "DLC4",
        "DLC5",
        "DLCMusic",
    ];

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
        "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
        "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
        "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    public static List<IdRow> ReadLanguages(string databaseRoot)
    {
        var chinese = new Dictionary<(string Pack, string Category, int Id), string>();
        foreach (var file in TablesIn(databaseRoot, "Language_Chinese"))
        {
            var pack = PackName(databaseRoot, file);
            var category = Category(Path.GetFileName(file));
            if (category is null)
                continue;
            foreach (var (id, name) in ReadTable(file))
                chinese.TryAdd((pack, category, id), Display(name));
        }

        var rows = new List<IdRow>();
        var seen = new HashSet<(string Pack, string Category, int Id)>();
        foreach (var file in TablesIn(databaseRoot, "Language_English"))
        {
            var pack = PackName(databaseRoot, file);
            var category = Category(Path.GetFileName(file));
            if (category is null)
                continue;
            foreach (var (id, name) in ReadTable(file))
            {
                if (!seen.Add((pack, category, id)))
                    continue;
                var summary = chinese.TryGetValue((pack, category, id), out var text) ? text : Display(name);
                rows.Add(new IdRow(pack, category, id, name, summary));
            }
        }

        return rows;
    }

    public static List<RecipeRow> ReadRecipes(string databaseRoot)
    {
        var recipes = new List<RecipeRow>();
        var seen = new HashSet<(string Pack, int Id)>();
        var pattern = new Regex(@"- id:\s*(-?\d+)\s*\r?\n\s*foodID:\s*(-?\d+)", RegexOptions.IgnoreCase);
        foreach (var file in Directory.EnumerateFiles(databaseRoot, "*RecipeProfile.asset", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var pack = PackName(databaseRoot, file);
            var text = File.ReadAllText(file);
            foreach (Match match in pattern.Matches(text))
            {
                var id = int.Parse(match.Groups[1].Value);
                if (!seen.Add((pack, id)))
                    continue;
                recipes.Add(new RecipeRow(pack, id, int.Parse(match.Groups[2].Value)));
            }
        }

        return recipes;
    }

    public static string Emit(IReadOnlyList<IdRow> rows, IReadOnlyList<RecipeRow> recipes)
    {
        var all = rows.ToList();
        foreach (var recipe in recipes)
        {
            var food = Food(rows, recipe.Pack, recipe.FoodId);
            var name = food?.Name ?? "Recipe " + recipe.Id;
            var summary = food?.Summary ?? name;
            all.Add(new IdRow(recipe.Pack, "Recipes", recipe.Id, name, summary));
        }

        var packs = all.Select(row => row.Pack).Distinct(StringComparer.Ordinal).OrderBy(Rank).ThenBy(name => name, StringComparer.Ordinal).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated>");
        builder.AppendLine("// Generated by Mystia.CatalogGen. Do not edit.");
        builder.AppendLine("namespace Mystia.Data;");
        builder.AppendLine();
        builder.AppendLine("public static class Catalogs");
        builder.AppendLine("{");
        var emitted = false;
        foreach (var pack in packs)
        {
            var categories = all.Where(row => row.Pack == pack).Select(row => row.Category).Distinct().ToArray();
            if (categories.Length == 0)
                continue;
            if (emitted)
                builder.AppendLine();
            emitted = true;
            builder.Append("    public static class ").Append(pack).AppendLine();
            builder.AppendLine("    {");
            var wroteCategory = false;
            foreach (var category in CategoryOrder)
            {
                if (!categories.Contains(category))
                    continue;
                var members = all.Where(row => row.Pack == pack && row.Category == category).OrderBy(row => row.Id).ToArray();
                if (members.Length == 0)
                    continue;
                if (wroteCategory)
                    builder.AppendLine();
                wroteCategory = true;
                AppendClass(builder, category, members, "        ");
            }

            builder.AppendLine("    }");
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    public static string Identifier(string name)
    {
        var brief = Brief(name);
        var source = brief.Count > 0 ? string.Join("", brief) : name;
        source = source.Replace("&", " And ").Replace("+", " And ").Replace("/", " ");
        var tokens = Regex.Matches(source, "[A-Za-z0-9]+");
        var builder = new StringBuilder();
        foreach (Match token in tokens)
        {
            var text = token.Value;
            if (text.Length == 0)
                continue;
            builder.Append(char.ToUpperInvariant(text[0]));
            if (text.Length > 1)
                builder.Append(text[1..]);
        }

        if (builder.Length == 0)
            return "";
        if (char.IsDigit(builder[0]))
            builder.Insert(0, '_');
        var identifier = builder.ToString();
        if (Keywords.Contains(identifier))
            identifier = "@" + identifier;
        return identifier;
    }

    private static IdRow? Food(IReadOnlyList<IdRow> rows, string pack, int foodId)
    {
        var same = rows.FirstOrDefault(row => row.Pack == pack && row.Category == "Foods" && row.Id == foodId);
        if (same is not null)
            return same;
        foreach (var candidate in PackOrder)
        {
            var match = rows.FirstOrDefault(row => row.Pack == candidate && row.Category == "Foods" && row.Id == foodId);
            if (match is not null)
                return match;
        }

        return rows.FirstOrDefault(row => row.Category == "Foods" && row.Id == foodId);
    }

    private static void AppendClass(StringBuilder builder, string category, IdRow[] members, string indent)
    {
        builder.Append(indent).Append("public static class ").Append(category).AppendLine();
        builder.Append(indent).AppendLine("{");
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            var identifier = Identifier(member.Name);
            if (identifier.Length == 0)
                identifier = "Id" + Token(member.Id);
            var candidate = identifier;
            if (!used.Add(candidate))
            {
                candidate = identifier + "_" + Token(member.Id);
                var repeat = 2;
                while (!used.Add(candidate))
                    candidate = identifier + "_" + Token(member.Id) + "_" + repeat++;
            }

            builder.Append(indent).Append("    /// <summary>").Append(Xml(member.Summary)).AppendLine("</summary>");
            builder.Append(indent).Append("    public const int ").Append(candidate).Append(" = ").Append(member.Id).AppendLine(";");
        }

        builder.Append(indent).AppendLine("}");
    }

    private static IEnumerable<string> TablesIn(string databaseRoot, string language)
    {
        return Directory.EnumerateFiles(databaseRoot, "*.txt", SearchOption.AllDirectories)
            .Where(path => path.Contains(language, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static string PackName(string databaseRoot, string file)
    {
        var relative = Path.GetRelativePath(databaseRoot, file);
        var split = relative.IndexOfAny(['\\', '/']);
        return split < 0 ? relative : relative[..split];
    }

    private static int Rank(string pack)
    {
        var index = Array.IndexOf(PackOrder, pack);
        return index < 0 ? PackOrder.Length : index;
    }

    private static string? Category(string fileName)
    {
        foreach (var (suffix, category) in Tables)
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return category;
        }

        return null;
    }

    private static IEnumerable<(int Id, string Name)> ReadTable(string path)
    {
        var header = true;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;
            var cells = line.Split('\t');
            if (cells.Length < 2)
                continue;
            if (header)
            {
                header = false;
                if (cells[0].Equals("id", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            if (!int.TryParse(cells[0].Trim(), out var id))
                continue;
            var name = cells[1].Trim().Trim('"');
            if (name.Length == 0)
                continue;
            yield return (id, name);
        }
    }

    private static List<string> Brief(string name)
    {
        var found = new List<string>();
        foreach (Match match in Regex.Matches(name, "<brief>(.*?)</brief>", RegexOptions.IgnoreCase))
        {
            if (match.Groups[1].Value.Length > 0)
                found.Add(match.Groups[1].Value);
        }

        return found;
    }

    private static string Display(string name) =>
        Regex.Replace(name, "</?brief>", "", RegexOptions.IgnoreCase).Replace("\r", " ").Replace("\n", " ").Trim();

    private static string Token(int id) => id < 0 ? "Minus" + (-id).ToString() : id.ToString();

    private static string Xml(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
