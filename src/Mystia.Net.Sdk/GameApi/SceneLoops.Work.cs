using System.Diagnostics.CodeAnalysis;

using Common.TimelineExtestion;
using Common.UI;
using Common.UI.GlobalMap;
using GameData.Core.Collections;
using GameData.Core.Collections.NightSceneUtility;
using GameData.Profile;
using NightScene.GuestManagementUtility;
using NightScene.PartnerUtility;

using Mystia;
using Mystia.Listeners;

using UnityEngine;

namespace Mystia.Scenes;

[AutoWire]
public interface IWorkSceneGameLoop
{
    void Setup(IWorkSceneServices services);

    void Update(IWorkSceneServices services, float delta);

    void Shutdown(IWorkSceneServices services);
}

public interface IWorkSceneServices
{
    IWorkSceneGuests Guests { get; }

    IWorkSceneEconomyServices Economy { get; }

    IQteServices Qte { get; }

    IWorkSceneCook Cook { get; }

    IWorkSceneStorage Storage { get; }

    IWorkSceneTray Tray { get; }

    IWorkSceneTime Time { get; }

    IWorkSceneIzakaya Izakaya { get; }

    /// <summary>Timed buffs of the running work scene.</summary>
    IWorkSceneBuffs Buffs => throw new NotSupportedException();

    /// <summary>The spell the running work scene is executing.</summary>
    ISpellHost Spells => throw new NotSupportedException();

    /// <summary>
    /// The challenge timeline of the running work scene, whose phases, clock and guest spawns the framework
    /// observes. The member is only valid inside this scene's loop, like every other service here.
    /// </summary>
    IWorkSceneChallengeServices Challenge => throw new NotSupportedException();

    ICommonServices Common { get; }

    IPresentationServices Presentation { get; }
}

/// <summary>
/// The guests of the running work scene: the switches that gate the game's own guest behaviour, the commands
/// that drive one guest group, and the serving members.
/// <para>
/// A group is named by its <see cref="GuestHandle"/> and a dish by its <see cref="DishProxy"/> — never by the
/// game's controller, order or sellable. A query answers through <c>TryGet</c> when it may find nothing, and
/// every handle it hands back is minted for the running scene session, so a stale one is refused by its own
/// <c>TryGet</c>.
/// </para>
/// </summary>
public interface IWorkSceneGuests
{
    void SetSpawnEnabled(bool enabled);

    void SetSeatingEnabled(bool enabled);

    void SetLeaveEnabled(bool enabled);

    void SetOrderingEnabled(bool enabled);

    void SetEvaluationEnabled(bool enabled);

    /// <summary>Spawns one group of normal guests; the return names the new group.</summary>
    GuestHandle SpawnNormal(IReadOnlyList<GuestDescription> guests, int desk = -1);

    /// <summary>Spawns one special guest by id; the return names the new group.</summary>
    GuestHandle SpawnSpecial(int guestId, int desk = -1);

    bool Seat(GuestHandle group, int desk, bool firstSpawn = true, int seat = -1);

    /// <summary>The group sitting at a desk, or false when the desk is empty.</summary>
    /// <param name="desk">The desk to look at.</param>
    /// <param name="guest">The seated group, when the call answers true.</param>
    bool TryGetSeated(int desk, [NotNullWhen(true)] out GuestProxy? guest);

    void Leave(GuestHandle group, GuestLeaveKind kind);

    void SetPatience(GuestHandle group, int value);

    void BeginOrderSession(GuestHandle group);

    void BeginOrderSession(GuestHandle group, OrderHandle order, string message);

    void BeginOrderSession(GuestHandle group, OrderGenerationOutcome result, OrderHandle order, string message);

    void Evaluate(GuestHandle group);

    // Bridge implemented members; the default bodies throw until the bridge wiring lands, so an unwired
    // member fails loudly instead of silently doing nothing.

    /// <summary>Whether a group of this size still fits into the waiting seats.</summary>
    bool CanQueue(GuestHandle group) => throw new NotSupportedException();

    /// <summary>
    /// Walks a group into the queue the way the game's own spawn path does when seating failed: the capacity
    /// check, the walk to a waiting seat, the registration with the guest manager and the patience countdown.
    /// <para>
    /// The verdict stays with the caller: a group that runs out of patience is reported through
    /// <c>IGuestGroupListener.OnGroupQueuePatienceDepleted</c> and then left where it is, instead of being sent
    /// back to where it spawned the way the game's own queue path does it. A caller replaying a verdict another
    /// machine already gave needs exactly that — the replaying machine must not decide on its own — and one that
    /// wants the game's behaviour gives the verdict itself (<see cref="StopPatientCountdown"/> followed by
    /// <see cref="GuestProxy.MoveToSpawn"/>).
    /// </para>
    /// Returns false when the queue cannot take the group, which leaves the caller to send it back to where it
    /// spawned, the fallback the game itself takes.
    /// </summary>
    /// <param name="group">The group to walk into the queue.</param>
    /// <param name="tryToJumpQueue">Whether the group takes the first free waiting seat instead of the last one.</param>
    bool TryQueue(GuestHandle group, bool tryToJumpQueue = false) => throw new NotSupportedException();

    /// <summary>
    /// Stops a queued group's patience countdown. The game's own verdict does this before it sends the group
    /// back to where it spawned, so a replayed verdict only has to call this and then move the group.
    /// </summary>
    /// <param name="group">The queued group whose countdown is stopped.</param>
    void StopPatientCountdown(GuestHandle group) => throw new NotSupportedException();

    /// <summary>
    /// Shows the mood bar of a group that is about to order for the first time and keeps it in step with the
    /// group's mood. This is the display half of the game's own first order: a machine that replays the first
    /// order calls it next to <see cref="BeginOrderSession(GuestHandle)"/>, because the replay does not go
    /// through the panel the game shows the bar from.
    /// </summary>
    /// <param name="group">The group whose mood bar is shown.</param>
    void ShowMood(GuestHandle group) => throw new NotSupportedException();

    /// <summary>
    /// Registers a group as one the player may drive out of the izakaya. The game does this when a repeated
    /// order cycle starts, so a machine replaying that cycle has to do it too, or the desk cannot be repelled
    /// there.
    /// </summary>
    /// <param name="group">The group the player may repel from now on.</param>
    void SetRepellable(GuestHandle group) => throw new NotSupportedException();

    /// <summary>
    /// Shows a dish on a desk's table, or clears the slot when <paramref name="dish"/> is null, exactly as the
    /// game's own serving path shows it. The slot of the other kind is left alone.
    /// </summary>
    /// <param name="deskCode">The desk whose table shows the dish.</param>
    /// <param name="dish">The dish to show, or null to clear the slot.</param>
    /// <param name="kind">Which of the table's two slots the dish belongs to.</param>
    void ShowServedDish(int deskCode, DishProxy? dish, DishKind kind) => throw new NotSupportedException();

    /// <summary>
    /// Sends one beverage to a group through the game's own serving path (it goes in the air first, the
    /// landing spot is re-checked and the order is evaluated when it becomes full). Returns the beverage that
    /// was actually registered on the order, or null when the serve was dropped.
    /// </summary>
    DishProxy? ServeBeverage(GuestHandle group, DishProxy beverage) => throw new NotSupportedException();

    /// <summary>Marks a beverage as being thrown to a group, or clears that mark with null.</summary>
    void SetBeverageInAir(GuestHandle group, DishProxy? beverage) => throw new NotSupportedException();

    /// <summary>Tells the partners that an order changed status (the same call the game's own serving makes).</summary>
    void NotifyOrderStatusUpdate(OrderHandle order, PartnerOrderContext context, int index) => throw new NotSupportedException();

    /// <summary>The guest groups currently seated at a desk, in the game's own order.</summary>
    IReadOnlyList<GuestHandle> InDeskGuests => throw new NotSupportedException();

    /// <summary>The order a group is currently considering, or false when it has none.</summary>
    /// <param name="group">The group to look at.</param>
    /// <param name="order">The pending order, when the call answers true.</param>
    bool TryGetPendingOrder(GuestHandle group, [NotNullWhen(true)] out OrderProxy? order) => throw new NotSupportedException();

    /// <summary>Whether both the dish and the beverage of an order have been served.</summary>
    bool IsOrderFullfilled(OrderHandle order) => throw new NotSupportedException();
}

public interface IWorkSceneCook
{
    void SetCallEnabled(bool enabled);

    void Start(int cookerIndex, Sellable result, Recipe recipe);

    void Extract(int cookerIndex);

    void Store(int cookerIndex, Sellable value);

    void StartCountdown(int cookerIndex, float qteScore);
}

public interface IWorkSceneStorage
{
    void Store(Sellable sellable);
}

public interface IWorkSceneTray
{
    void SetCloseEnabled(bool enabled);

    void Receive(Sellable sellable);
}

public interface IWorkSceneTime
{
    void SetMode(GameTimeManager.TimeMode mode);

    // Bridge implemented members; the default bodies throw until the bridge wiring lands.

    /// <summary>Whole length of the night in seconds; writing it replaces the game's night length source.</summary>
    int WholeNightSeconds
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>Gates guest spawning and the night countdown starting (covers the normal, challenge and creator box loops).</summary>
    void SetTimingEnabled(bool enabled) => throw new NotSupportedException();

    /// <summary>Starts one night with the arguments the game would have used.</summary>
    void BeginTiming() => throw new NotSupportedException();
}

public interface IWorkSceneIzakaya
{
    void SetCloseEnabled(bool enabled);

    void Close();

    /// <summary>
    /// Gates the time driven path that closes the izakaya when the countdown reaches zero, for when the
    /// player driven close cannot be gated alone.
    /// </summary>
    void SetTimeCloseEnabled(bool enabled) => throw new NotSupportedException();
}
