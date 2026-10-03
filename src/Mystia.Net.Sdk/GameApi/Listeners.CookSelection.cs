using GameData.Core.Collections;

using Mystia;

namespace Mystia.Listeners;

/// <summary>
/// A cooking submission. <c>FromSelectionPanel</c> tells the selection panel path from the direct one;
/// <c>IngredientIds</c> is the ingredient list the submitting caller selected, or null when it submits none.
/// </summary>
public record struct CookingRequest(Recipe Recipe, int[]? IngredientIds, bool FromSelectionPanel);

[AutoWire]
public interface ICookSelectionListener
{
    void OnPreCookingSubmit(ref CookingRequest request, ref bool cancelInvocation) { }
}
