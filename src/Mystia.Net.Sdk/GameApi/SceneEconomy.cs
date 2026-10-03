using NightScene.EventUtility;

namespace Mystia.Scenes;

/// <summary>
/// Work scene value edits, replayed through the game's own edit calls. Every edit runs the same path the
/// game uses, so <c>IWorkMetricsListener</c> sees it and the edit is not blocked by its own gates.
/// </summary>
public interface IWorkSceneEconomyServices
{
    void EditFund(float value, EventManager.MathOperation operation = EventManager.MathOperation.Add);

    void EditTip(int value, EventManager.ServeType serveType, float comboBuff = 0f, float moodBuff = 0f, float extraBuff = 0f);

    void EditExperience(float value, EventManager.MathOperation operation = EventManager.MathOperation.Add);

    void EditPassion(float value, EventManager.MathOperation operation = EventManager.MathOperation.Add);

    /// <summary>Gates the popularity tag edit path.</summary>
    void SetPopularityTagsEnabled(bool enabled);
}
