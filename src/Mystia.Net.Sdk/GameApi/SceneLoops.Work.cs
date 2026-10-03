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

public interface IWorkSceneGuests
{
    void SetSpawnEnabled(bool enabled);

    void SetSeatingEnabled(bool enabled);

    void SetLeaveEnabled(bool enabled);

    void SetOrderingEnabled(bool enabled);

    void SetEvaluationEnabled(bool enabled);

    GuestGroupController SpawnNormal(IReadOnlyList<NormalGuest> guests, int desk = -1);

    GuestGroupController SpawnSpecial(int guestId, int desk = -1);

    bool Seat(GuestGroupController group, int desk, bool firstSpawn = true, int seat = -1);

    GuestGroupController At(int desk);

    void Leave(GuestGroupController group, GuestLeaveKind kind);

    void SetPatience(GuestGroupController group, int value);

    void BeginOrderSession(GuestGroupController group);

    void BeginOrderSession(GuestGroupController group, GuestsManager.OrderBase order, string message);

    void BeginOrderSession(GuestGroupController group, GuestsManager.OrderGenerationResult result, GuestsManager.OrderBase order, string message);

    void Evaluate(GuestGroupController group);

    // Bridge implemented members; the default bodies throw until the bridge wiring lands, so an unwired
    // member fails loudly instead of silently doing nothing.

    /// <summary>
    /// Sends one beverage to a group through the game's own serving path (it goes in the air first, the
    /// landing spot is re-checked and the order is evaluated when it becomes full). Returns the beverage that
    /// was actually registered on the order, or null when the serve was dropped.
    /// </summary>
    Sellable? ServeBeverage(GuestGroupController group, Sellable beverage) => throw new NotSupportedException();

    /// <summary>Marks a beverage as being thrown to a group, or clears that mark with null.</summary>
    void SetBeverageInAir(GuestGroupController group, Sellable? beverage) => throw new NotSupportedException();

    /// <summary>Tells the partners that an order changed status (the same call the game's own serving makes).</summary>
    void NotifyOrderStatusUpdate(GuestsManager.OrderBase order, PartnerManager.OrderChangeContext context, int index) => throw new NotSupportedException();

    /// <summary>The guest groups currently seated at a desk.</summary>
    IReadOnlyList<GuestGroupController> InDeskGuests => throw new NotSupportedException();

    /// <summary>The order a group is currently considering; null when it has none.</summary>
    GuestsManager.OrderBase? PendingOrder(GuestGroupController group) => throw new NotSupportedException();

    /// <summary>Whether both the dish and the beverage of an order have been served.</summary>
    bool IsOrderFullfilled(GuestsManager.OrderBase order) => throw new NotSupportedException();
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
