using System.Reflection;
using Mystia.Assets;
using Mystia.Modding.Bridge;
using Mystia.Numerics;
using Mystia.Scenes;
using Xunit;

namespace Mystia.Tests;

/// <summary>
/// The presentation surface: the effect and audio layers and the floating labels. What can be pinned without
/// a running engine is pinned here — the shape of the contract (no engine type is ever named, and the value
/// types are the framework's own), what the entries refuse, and how a handle behaves when nothing was filed
/// under a key. Instantiating the effect, building the overlay canvas and creating the text is the engine's
/// part of the job and can only be exercised in game.
/// </summary>
// SceneLoopHost / BridgeInstaller / ServiceScope are process wide statics, so this class must not run in
// parallel with the other suites that bind a registry (the same collection the scope suite uses).
[Collection("Scene loop services")]
public sealed class PresentationTests
{
    private static readonly string[] BannedPrefixes = ["UnityEngine", "Il2Cpp", "Il2CppInterop"];

    /// <summary>
    /// The point of the seam: a mod may only name framework types, so neither the presentation interface, the
    /// label contract, the handles nor the style may name anything from the engine or the interop, value types
    /// included.
    /// </summary>
    [Theory]
    [InlineData(typeof(IPresentationServices))]
    [InlineData(typeof(IFloatingLabel))]
    [InlineData(typeof(FloatingLabelStyle))]
    [InlineData(typeof(IVfxHandle))]
    [InlineData(typeof(TransformHandle))]
    public void Presentation_members_never_name_the_engine(Type contract)
    {
        var framework = contract.Assembly;

        foreach (var member in contract.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            foreach (var type in TypesNamedBy(member))
            {
                var name = type.Namespace ?? string.Empty;

                Assert.DoesNotContain(BannedPrefixes, prefix => name.StartsWith(prefix, StringComparison.Ordinal));
                Assert.True(
                    type.IsGenericParameter
                    || type.Assembly == framework
                    || name.StartsWith("System", StringComparison.Ordinal),
                    $"{contract.Name}.{member.Name} names {type.FullName}, which does not come from the framework.");
            }
        }
    }

    /// <summary>
    /// The positions the presentation layer speaks are the framework's mirrored value types, not the engine's:
    /// a mod that passes a position never has to name a Unity type to do it.
    /// </summary>
    [Fact]
    public void Presentation_positions_are_framework_value_types()
    {
        var playVfx = typeof(IPresentationServices).GetMethod(nameof(IPresentationServices.PlayVfx))!;
        Assert.Equal(typeof(Vector3), playVfx.GetParameters()[1].ParameterType);
        Assert.Equal(
            typeof(Vector3),
            typeof(IPresentationServices).GetProperty(nameof(IPresentationServices.PlayerPosition))!.PropertyType);
        Assert.Equal(
            typeof(Vector3),
            typeof(IPresentationServices).GetMethod(nameof(IPresentationServices.TablePosition))!.ReturnType);

        // The host a label hangs on is an opaque framework handle, and the label a call hands back too.
        Assert.Equal(typeof(TransformHandle), typeof(IPresentationServices).GetMethod(nameof(IPresentationServices.Bind))!.ReturnType);
        Assert.Equal(
            typeof(IFloatingLabel),
            typeof(IPresentationServices).GetMethod(nameof(IPresentationServices.AttachLabel))!.ReturnType);
    }

    /// <summary>The scene scope keeps the new entries from touching a scene that is not the caller's.</summary>
    [Fact]
    public void Presentation_members_throw_outside_a_scene_loop()
    {
        var presentation = SplashSceneServices.Shared.Presentation;

        Assert.Throws<InvalidOperationException>(() => presentation.PlayScreenOverlay("effect"));
        Assert.Throws<InvalidOperationException>(() => presentation.TryRegisterPrefab("key", new object()));
        Assert.Throws<InvalidOperationException>(() => presentation.Bind(new object()));
        Assert.Throws<InvalidOperationException>(() => presentation.SpawnLabel(null!, "text", FloatingLabelStyle.Default));
        Assert.Throws<InvalidOperationException>(() => presentation.AttachLabel(null!, "text", FloatingLabelStyle.Default));
    }

    /// <summary>
    /// A key nothing was filed under is a report and an inert handle, never a throw: the caller of a well
    /// formed call always owns a handle, it can be compared with the handle of another miss, and stopping it
    /// twice is harmless.
    /// </summary>
    [Fact]
    public void A_miss_is_reported_and_plays_nothing()
    {
        InScene(presentation =>
        {
            // An empty asset path is the one input that is refused outright.
            Assert.Null(presentation.PlayVfx(string.Empty, default));
            Assert.Null(presentation.PlayVfx("   ", default));
            Assert.Null(presentation.PlayScreenOverlay(string.Empty));

            // An unfiled key plays nothing either: the miss is reported once and answers null, so a caller
            // cannot stop an effect it never started.
            Assert.Null(presentation.PlayVfx("mystia.tests/never-filed", default));
            Assert.Null(presentation.PlayVfx("mystia.tests/never-filed", default));
            Assert.Null(presentation.PlayScreenOverlay("mystia.tests/never-filed"));

            // Audio has no handle to hand back: a miss is simply ignored.
            presentation.PlayAudio(string.Empty);
            presentation.PlayAudio("mystia.tests/never-filed");
        });
    }

    /// <summary>
    /// A label that cannot be built is refused before any engine object is created: no host, no text, and a
    /// style the engine would only mangle (a zero or non finite size, a non finite colour or offset).
    /// </summary>
    [Fact]
    public void Invalid_labels_are_refused()
    {
        InScene(presentation =>
        {
            Assert.Null(presentation.SpawnLabel(null!, "text", FloatingLabelStyle.Default));
            Assert.Null(presentation.AttachLabel(null!, "text", FloatingLabelStyle.Default));

            // A host handle whose text is empty: refused before the transform is ever touched.
            var host = new UnityTransformHandle(null!);
            Assert.Null(presentation.SpawnLabel(host, string.Empty, FloatingLabelStyle.Default));
            Assert.Null(presentation.SpawnLabel(host, "   ", FloatingLabelStyle.Default));
            Assert.Null(presentation.AttachLabel(host, string.Empty, FloatingLabelStyle.Default));

            Assert.Null(presentation.SpawnLabel(host, "text", new FloatingLabelStyle(Vector3.Zero, Color.White, 0f)));
            Assert.Null(presentation.SpawnLabel(host, "text", new FloatingLabelStyle(Vector3.Zero, Color.White, float.NaN)));
            Assert.Null(presentation.SpawnLabel(host, "text", new FloatingLabelStyle(Vector3.Zero, new Color(float.NaN, 0f, 0f), 5f)));
            Assert.Null(presentation.SpawnLabel(host, "text", new FloatingLabelStyle(new Vector3(0f, float.PositiveInfinity, 0f), Color.White, 5f)));
            Assert.Null(presentation.AttachLabel(host, "text", new FloatingLabelStyle(Vector3.Zero, Color.White, -1f)));
            Assert.Null(presentation.SpawnLabel(host, "text", FloatingLabelStyle.Default, float.NaN));
            Assert.Null(presentation.SpawnLabel(host, "text", FloatingLabelStyle.Default, -1f));
        });
    }

    /// <summary>A template is only ever cloned out of an engine object the mod actually holds.</summary>
    [Fact]
    public void Invalid_prefabs_are_refused()
    {
        InScene(presentation =>
        {
            Assert.False(presentation.TryRegisterPrefab(string.Empty, new object()));
            Assert.False(presentation.TryRegisterPrefab("   ", new object()));
            Assert.False(presentation.TryRegisterPrefab("mystia.tests/template", new object()));
            Assert.False(presentation.TryRegisterPrefab("mystia.tests/template", "not a game object"));
        });
    }

    /// <summary>Only an engine object can be wrapped into a host handle.</summary>
    [Fact]
    public void Bind_only_wraps_engine_objects()
    {
        InScene(presentation =>
        {
            Assert.Null(presentation.Bind(null!));
            Assert.Null(presentation.Bind(new object()));
            Assert.Null(presentation.Bind("not an engine object"));
        });
    }

    // One scene loop per check: the entries are only valid inside the Setup of the loop they were handed to.
    private static void InScene(Action<IPresentationServices> body)
    {
        var registry = new ModRegistry();
        registry.Add<IDaySceneGameLoop>(new CapturingDayLoop(body));
        BridgeInstaller.Bind(registry);
        try
        {
            SceneLoopHost.Enter(SceneId.Day);
        }
        finally
        {
            SceneLoopHost.Reset();
            BridgeInstaller.Bind(null);
        }
    }

    private sealed class CapturingDayLoop(Action<IPresentationServices> body) : IDaySceneGameLoop
    {
        public void Setup(IDaySceneServices services) => body(services.Presentation);

        public void Update(IDaySceneServices services, float delta)
        {
        }

        public void Shutdown(IDaySceneServices services)
        {
        }
    }

    private static IEnumerable<Type> TypesNamedBy(MemberInfo member)
    {
        var named = member switch
        {
            PropertyInfo property => new[] { property.PropertyType }
                .Concat(property.GetIndexParameters().Select(parameter => parameter.ParameterType)),
            MethodInfo method => method.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType),
            FieldInfo field => [field.FieldType],
            EventInfo @event when @event.EventHandlerType is { } handler => [handler],
            _ => [],
        };

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
