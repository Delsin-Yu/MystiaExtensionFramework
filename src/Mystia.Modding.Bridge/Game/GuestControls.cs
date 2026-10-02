using NightScene.GuestManagementUtility;

namespace Mystia.Modding.Bridge;

internal sealed class GuestControls : Mystia.Listeners.IGuestControls
{
    private readonly GuestGroupController _group;

    internal GuestControls(GuestGroupController group) => _group = group;

    public void Seat(int desk, int mood, Action onSeated) =>
        GuestsManager.instance.SetManualControlledToSeat(_group, mood, desk, onSeated);

    public void OrderFoodAndBeverage(int foodId, int beverageId, Action<GuestGroupController.EvaluationResult> onEvaluated) =>
        GuestsManager.instance.SetNormalManualControlledOrder(_group, foodId, beverageId, onEvaluated);

    public void OrderByTag(int foodTag, int beverageTag, Action<GuestGroupController.EvaluationResult> onEvaluated) =>
        GuestsManager.instance.SetSpecialManualControlledOrder(_group, foodTag, beverageTag, onEvaluated);

    public void SetPatience(int value) =>
        GuestsManager.instance.SetManualControlledPatient(_group, value);

    public void Leave(bool instantly) =>
        GuestsManager.instance.SetManualControlledLeave(_group, instantly);
}
