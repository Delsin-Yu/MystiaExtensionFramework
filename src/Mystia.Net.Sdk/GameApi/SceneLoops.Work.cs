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

    /// <summary>
    /// The dish boundary of the running work scene (see <see cref="IWorkSceneDishes"/>): the one place where a
    /// dish a mod holds as the game's own sellable becomes the framework's own dish.
    /// </summary>
    IWorkSceneDishes Dishes => throw new NotSupportedException();

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

    /// <summary>
    /// Spawns one group of normal guests with the spawn request another machine rolled with — its spawn
    /// position, its leave type, its desk and whether the group fades in. This is the overload a machine
    /// replaying a spawn uses: the request is the one the rolling machine reported, not one of this machine's
    /// own.
    /// </summary>
    /// <param name="guests">The guests of the group, in the group's own order.</param>
    /// <param name="request">The spawn request to replay.</param>
    GuestHandle SpawnNormal(IReadOnlyList<GuestDescription> guests, GuestSpawnRequest request) =>
        throw new NotSupportedException();

    /// <summary>Spawns one special guest by id; the return names the new group.</summary>
    GuestHandle SpawnSpecial(int guestId, int desk = -1);

    /// <summary>Spawns one special guest by id with the spawn request another machine rolled with.</summary>
    /// <param name="guestId">The id of the special guest.</param>
    /// <param name="request">The spawn request to replay.</param>
    GuestHandle SpawnSpecial(int guestId, GuestSpawnRequest request) => throw new NotSupportedException();

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

    /// <summary>
    /// Builds the order a replayed order session is to place, without placing it. A machine that places an
    /// order another machine rolled builds its own order from what that machine rolled — the request ids, the
    /// desk and the two flags — because the order object itself belongs to the machine that places it, and
    /// only that machine can hand it to <see cref="BeginOrderSession(GuestHandle, OrderGenerationOutcome, OrderHandle, string)"/>.
    /// The handle answers none when the group names no guest to build the order for.
    /// </summary>
    /// <param name="group">The group that is to place the order.</param>
    /// <param name="kind">Whether the order is a normal guest's or a special guest's.</param>
    /// <param name="foodRequest">The food the order asks for, as a recipe id.</param>
    /// <param name="beverageRequest">The beverage the order asks for, as a beverage id.</param>
    /// <param name="deskCode">The desk the order belongs to.</param>
    /// <param name="hidden">Whether the game keeps the order out of its order list UI.</param>
    /// <param name="free">Whether the order costs the guest nothing.</param>
    OrderHandle CreateOrder(
        GuestHandle group,
        OrderKind kind,
        int foodRequest,
        int beverageRequest,
        int deskCode,
        bool hidden,
        bool free) => throw new NotSupportedException();

    /// <summary>
    /// Places a manual order — the story driven path, whose order the mod builds itself with
    /// <see cref="CreateOrder"/> — and reports the verdict the game reaches for it. The order is marked as a
    /// manual one, which is what makes the game settle it the way it settles a story order.
    /// </summary>
    /// <param name="group">The group the order belongs to.</param>
    /// <param name="order">The order to place.</param>
    /// <param name="onEvaluated">Run with the verdict the game reaches for the order.</param>
    void BeginManualOrder(GuestHandle group, OrderHandle order, Action<GuestEvaluation> onEvaluated) => throw new NotSupportedException();

    /// <summary>Evaluates a manual order and reports the game's verdict to the caller.</summary>
    /// <param name="group">The group whose manual order is evaluated.</param>
    /// <param name="onEvaluated">Run with the verdict the game reaches for the order.</param>
    void EvaluateManual(GuestHandle group, Action<GuestEvaluation> onEvaluated) => throw new NotSupportedException();

    /// <summary>Marks a group as already evaluated, which the manual path does before it evaluates an order.</summary>
    /// <param name="group">The group to mark.</param>
    void SetEvaluated(GuestHandle group) => throw new NotSupportedException();

    /// <summary>Drops the order registration of a group, which the manual path cleans before it evaluates.</summary>
    /// <param name="group">The group whose order registration is dropped.</param>
    void CleanOrderInfo(GuestHandle group) => throw new NotSupportedException();

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

/// <summary>
/// The dish boundary of the running work scene.
/// <para>
/// A dish reaches a mod as the game's own sellable on the paths that still speak it — the cook, tray and
/// storage members, and the dish a serve listener is handed — while everything a mod reads or writes for a
/// guest or an order speaks the framework's <see cref="DishProxy"/>. This is where the first becomes the
/// second: without it a mod holding a dish the game handed it could not put that dish on an order slot or a
/// panel.
/// </para>
/// <para>
/// It names the game's sellable on purpose and it is the only contract in the entity layer that does. The
/// members behind it are the ones still to be migrated; each of them that starts handing a
/// <see cref="DishProxy"/> removes one reason for this interface to exist.
/// </para>
/// </summary>
public interface IWorkSceneDishes
{
    /// <summary>
    /// The projection of a dish the caller holds, or null when the object is not a dish of the running session.
    /// </summary>
    /// <param name="dish">The dish the game's own members handed out.</param>
    DishProxy? DishOf(Sellable dish) => throw new NotSupportedException();
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

    /// <summary>
    /// Stores a dish a mod holds as the framework's own projection — the dish it took off an order slot to roll
    /// a serve back. The member that speaks the game's sellable stays for the dishes the cook, tray and storage
    /// paths still hand out.
    /// </summary>
    /// <param name="dish">The dish to put into the storage box.</param>
    void Store(DishProxy dish) => throw new NotSupportedException();
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
