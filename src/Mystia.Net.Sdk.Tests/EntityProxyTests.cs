using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Mystia.Listeners;
using Mystia.Modding.Bridge;
using Mystia.Scenes;
using Xunit;

namespace Mystia.Tests;

/// <summary>
/// The entity handles and their projections: a handle is the stable name of a guest group, an order or a dish
/// for one scene session, and the projection behind it is what a mod reads and drives. These run without the
/// engine by installing a fake port, which is the same seam the bridge installs its own ports through.
/// </summary>
[Collection("Scene loop services")]
public sealed class EntityProxyTests
{
    private sealed class FakeGuest(nint pointer) : IGuestEntity
    {
        public nint Pointer { get; } = pointer;

        public object? Native { get; } = new object();

        public int DeskCode { get; private set; } = 3;

        public GuestKind Kind => GuestKind.Normal;

        public int GuestCount => 2;

        public IReadOnlyList<int> GuestIds { get; } = new[] { 7, 8 };

        public int Mood { get; private set; } = 50;

        public int Fund { get; private set; } = 120;

        public int MaxFundCarry { get; private set; } = 300;

        public int ExtraFundByBuff => 5;

        public float EnduranceLimit => 100f;

        public IReadOnlyList<OrderProxy> Orders => [];

        public GuestLeaveType FinalLeaveType => GuestLeaveType.Fading;

        public bool HasEvaluated { get; private set; }

        public bool HasLeft { get; private set; }

        public bool IsQueued { get; private set; }

        public int PendingOrderCount => 1;

        public bool TryGetPendingOrder([NotNullWhen(true)] out OrderProxy? order)
        {
            order = null;
            return false;
        }

        public void SetMood(int value) => Mood = value;

        public void SetFund(int value) => Fund = value;

        public void SetMaxFundCarry(int value) => MaxFundCarry = value;

        public void SetPatience(int value)
        {
        }

        public void MarkLeft() => HasLeft = true;

        public void MoveToQueue(Action? onArrived)
        {
            IsQueued = true;
            onArrived?.Invoke();
        }

        public void MoveToSpawn() => IsQueued = false;

        public void FlyToSpawn(bool instantly)
        {
        }

        public void RemoveFromQueue() => IsQueued = false;
    }

    private static GuestHandle Track(nint pointer, out FakeGuest native)
    {
        native = new FakeGuest(pointer);
        return GuestDirectory.Track(native);
    }

    /// <summary>
    /// A handle resolves to a projection that projects the engine object, and the handle a projection reports is
    /// the handle it was reached through, so a mod can compare and store handles instead of projections.
    /// </summary>
    [Fact]
    public void A_guest_handle_resolves_through_its_projection()
    {
        GuestDirectory.Install(native => native as FakeGuest);
        try
        {
            var handle = Track(0x1234, out var native);

            Assert.False(handle.IsNone);
            Assert.True(handle.TryGet(out var guest));
            Assert.Equal(handle, guest!.Handle);
            // The projection reads through the port, so it answers the engine object the port wraps and the
            // values the port projects.
            Assert.Same(native.Native, guest.Native);
            Assert.Equal(3, guest.DeskCode);
            Assert.Equal(GuestKind.Normal, guest.Kind);
            Assert.Equal(new List<int> { 7, 8 }, guest.GuestIds);
            Assert.Equal(120, guest.Fund);

            guest.SetFund(200);
            Assert.Equal(200, guest.Fund);
            guest.MarkLeft();
            Assert.True(guest.HasLeft);
        }
        finally
        {
            GuestDirectory.Clear();
            GuestDirectory.Install(null);
        }
    }

    /// <summary>
    /// The same engine object always answers the same handle, which is what makes a handle usable as the key of
    /// the mod's own bookkeeping while the game keeps handing out the controller.
    /// </summary>
    [Fact]
    public void Tracking_the_same_object_twice_answers_the_same_handle()
    {
        GuestDirectory.Install(native => native as FakeGuest);
        try
        {
            var native = new FakeGuest(0x2000);

            var first = GuestDirectory.Track(native);
            var second = GuestDirectory.Track(native);

            Assert.Equal(first, second);
            Assert.Equal(first.GetHashCode(), second.GetHashCode());
            Assert.True(GuestDirectory.ProxyOf(native)!.Handle == first);
        }
        finally
        {
            GuestDirectory.Clear();
            GuestDirectory.Install(null);
        }
    }

    /// <summary>
    /// One scene loop is one session: a handle minted in the night that just ended stops resolving, so a mod that
    /// kept one cannot reach a controller the game already destroyed.
    /// </summary>
    [Fact]
    public void A_handle_stops_resolving_when_the_session_rotates()
    {
        GuestDirectory.Install(native => native as FakeGuest);
        try
        {
            var handle = Track(0x3000, out var native);
            Assert.True(handle.TryGet(out _));

            EntitySession.Rotate();

            Assert.False(handle.TryGet(out var gone));
            Assert.Null(gone);
            // Asking the directory by the engine object again mints a handle of the *new* session, which is why
            // a mod keeps the handle: the object it was minted for is not the object it is asked about anymore.
            Assert.Null(GuestDirectory.NativeOf(handle));
            var again = GuestDirectory.Track(native);
            Assert.NotEqual(handle, again);
            Assert.True(again.TryGet(out _));
        }
        finally
        {
            GuestDirectory.Clear();
            GuestDirectory.Install(null);
        }
    }

    /// <summary>
    /// The default handle names nothing and never resolves, whichever directory is asked.
    /// </summary>
    [Fact]
    public void The_none_handle_never_resolves()
    {
        Assert.True(GuestHandle.None.IsNone);
        Assert.False(GuestHandle.None.TryGet(out _));
        Assert.True(OrderHandle.None.IsNone);
        Assert.False(OrderHandle.None.TryGet(out _));
        Assert.True(DishHandle.None.IsNone);
        Assert.False(DishHandle.None.TryGet(out _));
        Assert.Equal(GuestHandle.None, default);
    }

    private sealed class RecordingListener(List<string> trace, GuestEvaluation verdict) : IGuestGroupListener
    {
        public void OnGroupEvaluated(GuestHandle group, ref GuestEvaluation result)
        {
            trace.Add(group.IsNone ? "none" : "guest");
            result = verdict;
        }
    }

    /// <summary>
    /// The interruption contract at the entity seams: every listener is asked in order and the value one leaves
    /// behind is what the next one sees, so a listener that rewrites the verdict cannot hide it from the rest.
    /// </summary>
    [Fact]
    public void Every_guest_listener_is_asked_and_the_verdict_travels_through()
    {
        var trace = new List<string>();
        var registry = new ModRegistry();
        registry.Add<IGuestGroupListener>(new RecordingListener(trace, GuestEvaluation.Good));
        registry.Add<IGuestGroupListener>(new RecordingListener(trace, GuestEvaluation.ExBad));
        BridgeInstaller.Bind(registry);
        try
        {
            var result = GuestEvaluation.Normal;
            GuestPipeline.RunEvaluation(null!, ref result);

            Assert.Equal(new[] { "none", "none" }, trace);
            Assert.Equal(GuestEvaluation.ExBad, result);
        }
        finally
        {
            BridgeInstaller.Bind(null);
        }
    }

    /// <summary>
    /// The point of the entity layer: nothing a mod implements or reads for a guest, an order or a dish names an
    /// engine type, so the game types stay behind the bridge.
    /// </summary>
    [Fact]
    public void The_entity_contracts_name_no_engine_type()
    {
        var banned = new[] { "UnityEngine", "Il2Cpp", "Il2CppInterop", "NightScene", "DayScene", "GameData", "Common" };

        foreach (var type in new[]
                 {
                     typeof(IGuestGroupListener),
                     typeof(IGuestSpawnModifier),
                     typeof(IWorkSceneGuests),
                     typeof(ServePannelView),
                     typeof(GuestSpawnRequest),
                     typeof(GuestHandle),
                     typeof(GuestProxy),
                     typeof(OrderHandle),
                     typeof(OrderProxy),
                     typeof(DishHandle),
                     typeof(DishProxy),
                     typeof(GuestDescription),
                     typeof(IPortraitProvider),
                     typeof(IChatConfirmationListener),
                     typeof(ChatConfirmationView),
                     typeof(IDayInputListener),
                     typeof(DayCharacter),
                     typeof(ICharacterServices),
                     typeof(CharacterCreateSpec),
                     typeof(IClock),
                     typeof(IInputServices),
                     typeof(MystiaKey),
                 })
        {
            foreach (var named in TypesNamedBy(type))
            {
                var space = named.Namespace ?? string.Empty;

                Assert.DoesNotContain(banned, prefix => space.StartsWith(prefix, StringComparison.Ordinal));
                Assert.True(
                    space.StartsWith("Mystia", StringComparison.Ordinal)
                    || space.StartsWith("System", StringComparison.Ordinal),
                    $"{type.Name} names {named.FullName}.");
            }
        }
    }

    private static IEnumerable<Type> TypesNamedBy(Type type)
    {
        var named = type.IsEnum
            ? [Enum.GetUnderlyingType(type)]
            : type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .SelectMany(member => member switch
                {
                    PropertyInfo property => new[] { property.PropertyType }
                        .Concat(property.GetIndexParameters().Select(parameter => parameter.ParameterType)),
                    MethodInfo method => method.GetParameters()
                        .Select(parameter => parameter.ParameterType)
                        .Append(method.ReturnType),
                    FieldInfo field => [field.FieldType],
                    _ => [],
                });

        return named.SelectMany(Flatten);
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        if (type.IsArray && type.GetElementType() is { } element)
            yield return element;

        foreach (var argument in type.GetGenericArguments())
            yield return argument;
    }
}
