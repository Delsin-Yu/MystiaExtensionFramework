using Mystia.CatalogGen;
using Mystia.Data;
using Xunit;

namespace Mystia.Tests;

public sealed class CatalogTests
{
    [Fact]
    public void NamesBecomeConstantsAndCollisionsKeepTheLowerId()
    {
        var source = CatalogBuilder.Emit(
            [
                new IdRow("Core", "Foods", 0, "Seafood Miso Soup", "海鲜味噌汤"),
                new IdRow("Core", "Foods", 3, "Pork & Trout Kebab", "猪肉鳟鱼熏"),
                new IdRow("Core", "FoodTags", -20, "Trend - Popular", "流行·喜爱"),
                new IdRow("Core", "FoodTags", 0, "Meat", "肉"),
                new IdRow("Core", "SpecialGuests", 0, "<brief>Wriggle</brief> Nightbug", "莉格露"),
                new IdRow("Core", "Izakayas", -1, "?????", "?????"),
                new IdRow("Core", "Ingredients", 1, "Pork", "猪肉"),
                new IdRow("Core", "Ingredients", 8, "Pork", "猪肉"),
                new IdRow("DLC1", "Foods", 1000, "Deep Fried Shrimp Tempura", "炸虾天妇罗"),
            ],
            [new RecipeRow("Core", 0, 0), new RecipeRow("Core", 9, 3)]);

        Assert.Contains("public static class Catalogs", source);
        Assert.Contains("public static class Core", source);
        Assert.Contains("public static class DLC1", source);
        Assert.Contains("/// <summary>海鲜味噌汤</summary>", source);
        Assert.Contains("public const int SeafoodMisoSoup = 0;", source);
        Assert.Contains("/// <summary>猪肉鳟鱼熏</summary>", source);
        Assert.Contains("public const int PorkAndTroutKebab = 3;", source);
        Assert.Contains("/// <summary>流行·喜爱</summary>", source);
        Assert.Contains("public const int TrendPopular = -20;", source);
        Assert.Contains("/// <summary>肉</summary>", source);
        Assert.Contains("public const int Meat = 0;", source);
        Assert.Contains("/// <summary>莉格露</summary>", source);
        Assert.Contains("public const int Wriggle = 0;", source);
        Assert.Contains("public const int IdMinus1 = -1;", source);
        Assert.Contains("public const int Pork = 1;", source);
        Assert.Contains("public const int Pork_8 = 8;", source);
        Assert.Contains("public const int PorkAndTroutKebab = 9;", source);
        Assert.Contains("/// <summary>炸虾天妇罗</summary>", source);
        Assert.Contains("public const int DeepFriedShrimpTempura = 1000;", source);
    }

    [Fact]
    public void GeneratedCatalogMatchesStockIds()
    {
        Assert.Equal(0, Catalogs.Core.FoodTags.Meat);
        Assert.Equal(1, Catalogs.Core.FoodTags.Aquatic);
        Assert.Equal(2, Catalogs.Core.FoodTags.Vegetarian);
        Assert.Equal(0, Catalogs.Core.Foods.SeafoodMisoSoup);
        Assert.Equal(4, Catalogs.Core.Foods.GrilledLamprey);
        Assert.Equal(0, Catalogs.Core.Recipes.SeafoodMisoSoup);
        Assert.Equal(0, Catalogs.Core.Beverages.GreenTea);
        Assert.Equal(1, Catalogs.Core.Ingredients.Pork);
        Assert.Equal(0, Catalogs.Core.SpecialGuests.Wriggle);
        Assert.Equal(0, Catalogs.Core.Cookers.BoilingPot);
        Assert.Equal(1000, Catalogs.DLC1.Foods.DeepFriedShrimpTempura);

        var source = File.ReadAllText(GeneratedFile());
        Assert.Contains("/// <summary>肉</summary>", source);
        Assert.Contains("/// <summary>海鲜味噌汤</summary>", source);
        Assert.Contains("/// <summary>莉格露</summary>", source);
        Assert.Contains("public static class DLCMusic", source);
    }

    private static string GeneratedFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Mystia.Net.Sdk", "Catalog", "GameIds.g.cs");
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Generated catalog was not found.");
    }
}
