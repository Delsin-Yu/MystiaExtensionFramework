using System.Reflection;
using Mystia.Listeners;
using Mystia.Modding.Bridge;
using Mystia.Numerics;
using Mystia.Scenes;
using Xunit;

namespace Mystia.Tests;

/// <summary>
/// The capabilities a mod uses at any time and that the framework added last: the unscaled clock, the hotkey
/// keyboard, the navigation switch and the browser opener. The clock is the one a background thread reads; the
/// keyboard is the one a loop polls.
/// </summary>
// BridgeInstaller.Registry is a process wide static, so this class must not run in parallel with the other
// suites that bind a registry (the xunit default parallelises test classes).
[Collection("Scene loop services")]
public sealed class CommonServicesTests
{
    /// <summary>
    /// The clock answers on a thread that is not the main one: the main thread publishes a value and the other
    /// thread reads exactly it, which is what a timeout or a log timestamp on a worker thread is built on.
    /// </summary>
    [Fact]
    public void The_clock_reads_what_the_main_thread_published()
    {
        HostClock.Publish(12.5f);
        try
        {
            var seen = -1f;
            var reader = new Thread(() => seen = CommonServices.Shared.Clock.Now);
            reader.Start();

            Assert.True(reader.Join(TimeSpan.FromSeconds(10)));
            Assert.Equal(12.5f, seen);
        }
        finally
        {
            HostClock.Publish(0f);
        }
    }

    /// <summary>
    /// A hotkey a player saved is a key name, so every member of <see cref="MystiaKey"/> must map to the
    /// engine key of the same name: renaming one would silently unbind the keys already in a config file.
    /// </summary>
    [Fact]
    public void Every_hotkey_keeps_its_engine_name()
    {
        foreach (var key in Enum.GetValues<MystiaKey>())
        {
            var engine = KeyboardServices.Key(key);
            if (key is MystiaKey.None)
            {
                Assert.Equal(UnityEngine.KeyCode.None, engine);
                continue;
            }

            Assert.Equal(key.ToString(), Enum.GetName(engine));
        }
    }

    /// <summary>
    /// Only a browser URL reaches the operating system; anything else is refused before the engine sees it.
    /// </summary>
    [Fact]
    public void Only_an_http_url_can_be_opened()
    {
        Assert.Throws<ArgumentException>(() => CommonServices.Shared.OpenUrl("not a url"));
        Assert.Throws<ArgumentException>(() => CommonServices.Shared.OpenUrl("file:///C:/temp/report.html"));
        Assert.Throws<ArgumentException>(() => CommonServices.Shared.OpenUrl("javascript:alert(1)"));
    }

    /// <summary>
    /// The day input contract is synchronous: every registered listener is asked, in the order it was
    /// registered, and the character identity it is handed is the one the framework described.
    /// </summary>
    [Fact]
    public void Every_day_input_listener_is_asked()
    {
        var trace = new List<string>();
        var registry = new ModRegistry();
        registry.Add<IDayInputListener>(new DayInputListener(trace, "first"));
        registry.Add<IDayInputListener>(new DayInputListener(trace, "second"));
        BridgeInstaller.Bind(registry);
        try
        {
            DayInputPipeline.CharacterReady(new DayCharacter("Self", true));
            DayInputPipeline.Move(new DayCharacter("Self", true), new Vector2(0.5f, -1f));

            Assert.Equal(
                new[]
                {
                    "first:ready:Self:True",
                    "second:ready:Self:True",
                    "first:move:Self:True:0.5,-1",
                    "second:move:Self:True:0.5,-1",
                },
                trace);
        }
        finally
        {
            BridgeInstaller.Bind(null);
        }
    }

    /// <summary>
    /// The capabilities added to <see cref="ICommonServices"/> name no engine type either, so a mod that only
    /// polls its hotkeys, timestamps its log or opens a browser never references Unity to do it.
    /// </summary>
    [Fact]
    public void The_new_common_capabilities_name_no_engine_type()
    {
        var banned = new[] { "UnityEngine", "Il2Cpp", "Il2CppInterop", "NightScene", "DayScene", "GameData", "Common" };
        MemberInfo[] members =
        [
            typeof(ICommonServices).GetProperty(nameof(ICommonServices.Clock))!,
            typeof(ICommonServices).GetProperty(nameof(ICommonServices.Input))!,
            typeof(ICommonServices).GetProperty(nameof(ICommonServices.UiNavigationEnabled))!,
            typeof(ICommonServices).GetMethod(nameof(ICommonServices.OpenUrl))!,
        ];

        foreach (var member in members)
        {
            foreach (var named in TypesNamedBy(member))
            {
                var space = named.Namespace ?? string.Empty;

                Assert.DoesNotContain(banned, prefix => space.StartsWith(prefix, StringComparison.Ordinal));
                Assert.True(
                    space.StartsWith("Mystia", StringComparison.Ordinal)
                    || space.StartsWith("System", StringComparison.Ordinal),
                    $"{member.Name} names {named.FullName}.");
            }
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
            _ => [],
        };

        return named.SelectMany(Flatten);
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        foreach (var argument in type.GetGenericArguments())
            yield return argument;
    }

    private sealed class DayInputListener(List<string> trace, string name) : IDayInputListener
    {
        public void OnCharacterReady(in DayCharacter unit) =>
            trace.Add($"{name}:ready:{unit.Name}:{unit.IsLocalPlayer}");

        public void OnMoveInput(in DayCharacter unit, Vector2 direction) =>
            trace.Add($"{name}:move:{unit.Name}:{unit.IsLocalPlayer}:{direction.X},{direction.Y}");
    }
}
