using NightScene.EventUtility;

using Mystia;

namespace Mystia.Listeners;

/// <summary>
/// Fund, tip, experience and passion edits. The game edits funds/experience/passion with a value and a
/// <c>MathOperation</c>, and tips with a serve type plus the three buff factors.
/// </summary>
[AutoWire]
public interface IWorkMetricsListener
{
    void OnPreFundEdit(ref float value, ref EventManager.MathOperation operation, ref bool cancelInvocation) { }

    void OnFundEdited(float value) { }

    void OnPreTipEdit(ref int value, ref EventManager.ServeType serveType, ref float comboBuff, ref float moodBuff, ref float extraBuff, ref bool cancelInvocation) { }

    void OnTipEdited(int value, EventManager.ServeType serveType, float comboBuff, float moodBuff, float extraBuff) { }

    void OnPreExperienceEdit(ref float value, ref EventManager.MathOperation operation, ref bool cancelInvocation) { }

    void OnExperienceEdited(float value) { }

    void OnPrePassionEdit(ref float value, ref EventManager.MathOperation operation, ref bool cancelInvocation) { }

    void OnPassionEdited(float value) { }
}
