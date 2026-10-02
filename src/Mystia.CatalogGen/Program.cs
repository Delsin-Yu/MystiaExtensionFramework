namespace Mystia.CatalogGen;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.Error.WriteLine("Usage: Mystia.CatalogGen <game-project-dir> [output-file]");
            Console.Error.WriteLine("The game project directory must contain Assets\\_SortedAssets\\DataBase.");
            return 1;
        }

        var repo = FindRepoRoot();
        var database = Path.Combine(args[0], "Assets", "_SortedAssets", "DataBase");
        var output = args.ElementAtOrDefault(1) ?? Path.Combine(repo, "src", "Mystia.Net.Sdk", "Catalog", "GameIds.g.cs");
        if (!Directory.Exists(database))
        {
            Console.Error.WriteLine("Game database was not found: " + database);
            return 1;
        }

        var rows = CatalogBuilder.ReadLanguages(database);
        var recipes = CatalogBuilder.ReadRecipes(database);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, CatalogBuilder.Emit(rows, recipes));
        Console.WriteLine($"Wrote {rows.Count} records and {recipes.Count} recipes to {output}");
        return 0;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
                return directory.FullName;
            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
