using System.Collections;

using GameData.Core.Collections.NightSceneUtility;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

using Mystia;
using Mystia.Scenes;
using Mystia.Spells;

namespace Mystia.Modding.Bridge;

/// <summary>
/// The spell the running work scene is executing, published to mods through
/// <see cref="IWorkSceneServices.Spells"/>. The bridge updates it around every card execution; between two
/// cards both ids are -1.
/// </summary>
internal sealed class WorkSceneSpellHost : ISpellHost
{
    internal static readonly WorkSceneSpellHost Shared = new();

    public int SpellId { get; private set; } = -1;

    public int GuestId { get; private set; } = -1;

    internal void Begin(int spellId, int guestId)
    {
        SpellId = spellId;
        GuestId = guestId;
    }

    internal void End()
    {
        SpellId = -1;
        GuestId = -1;
    }
}

/// <summary>
/// Lookup of the mod supplied <see cref="ISpell"/> instances by the spell id they are registered for. A card
/// is resolved when it is executed, not when the database is built, so the table is built lazily and rebuilt
/// whenever the registry grew since the last lookup.
/// </summary>
internal static class SpellPipeline
{
    private static readonly Dictionary<int, ISpell> Instances = [];
    private static int _registered = -1;

    internal static ISpell? Find(int spellId)
    {
        var registered = Dispatch.Instances<ISpell>().Count();
        if (registered != _registered)
        {
            Instances.Clear();
            foreach (var spell in Dispatch.Instances<ISpell>())
                Instances[spell.SpellId] = spell;
            _registered = registered;
        }

        return Instances.GetValueOrDefault(spellId);
    }
}

/// <summary>
/// Spell instance written into <c>DataBaseNight.SpecialGuestSpell</c>.
/// <para>
/// The data face (<c>SpellData</c>) carries the declaration of a spell card - its language entries, its
/// declaration portrait and the guest it belongs to. The effect is code, so the instance holds the mod's
/// <see cref="ISpell"/> and runs the routine of the card that was drawn: <see cref="ISpell.Positive"/> for
/// the reward card and <see cref="ISpell.Negative"/> for the punishment card. A spell id without a registered
/// implementation keeps the earlier behaviour - the declaration, portrait and buff paths still run and the
/// effect is empty - so a data only spell is not dropped.
/// </para>
/// </summary>
internal sealed class BridgeSpell : SpellBase
{
    // Managed only: the injected class must not hand this field to il2cpp.
    [HideFromIl2Cpp]
    private string Owner { get; set; } = "";

    // The spell id this asset was declared with, which is also the key of the mod implementation.
    [HideFromIl2Cpp]
    private int Spell { get; set; } = -1;

    public BridgeSpell(nint pointer) : base(pointer)
    {
    }

    public BridgeSpell() : base(ClassInjector.DerivedConstructorPointer<BridgeSpell>()) =>
        ClassInjector.DerivedConstructorBody(this);

    internal static BridgeSpell Create(int spellId, string owner)
    {
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<BridgeSpell>())
            ClassInjector.RegisterTypeInIl2Cpp<BridgeSpell>();
        var spell = ScriptableObject.CreateInstance<BridgeSpell>();
        spell.Owner = owner ?? "";
        spell.Spell = spellId;
        spell.hideFlags = HideFlags.HideAndDontSave;
        return spell;
    }

    public override string OnGettingSpellOwnerIdentifier() => Owner;

    public override Il2CppSystem.Collections.IEnumerator OnPositiveBuffExecute(SpellExecutionContext spellExecutionContext) =>
        Execute(
            spellExecutionContext,
            SpellPipeline.Find(Spell)?.Positive(WorkSceneServices.Shared, CoroutineScheduler.Shared, BridgeLog.Shared));

    public override Il2CppSystem.Collections.IEnumerator OnNegativeBuffExecute(SpellExecutionContext spellExecutionContext) =>
        Execute(
            spellExecutionContext,
            SpellPipeline.Find(Spell)?.Negative(WorkSceneServices.Shared, CoroutineScheduler.Shared, BridgeLog.Shared));

    private Il2CppSystem.Collections.IEnumerator Execute(SpellExecutionContext spellExecutionContext, IEnumerator? routine)
    {
        if (routine is null)
            return EmptyCoroutine.Empty();

        WorkSceneSpellHost.Shared.Begin(Spell, spellExecutionContext.CharacterData?.Id ?? -1);
        var session = new SpellSession(Spell, routine);
        CoroutinePump.Start(CoroutineScheduler.Shared, _ => session);
        return ManagedEnumerator.Wrap(session.Wait());
    }
}

/// <summary>
/// One mod spell routine, driven by <see cref="CoroutinePump"/> while the game's spell queue waits on the
/// <see cref="ManagedEnumerator"/> adapter.
/// <para>
/// Every resume step runs inside <see cref="ServiceScope"/> and the scope is left before the step yields, so
/// a routine never holds the scene services across frames. A nested managed routine is stepped here for the
/// same reason; a native yield value (a game coroutine, <c>WaitForSeconds</c>, an await token, ...) is handed
/// to the pump, which knows how to wait for it.
/// </para>
/// A routine that throws is reported through the bridge log and the session is closed, so a failed card
/// cannot stall the game's spell queue.
/// </summary>
internal sealed class SpellSession : IEnumerator
{
    private readonly int _spellId;
    private readonly Stack<IEnumerator> _frames = new();
    private object? _current;
    private bool _finished;

    internal SpellSession(int spellId, IEnumerator routine)
    {
        _spellId = spellId;
        _frames.Push(routine);
    }

    /// <summary>The routine the game's spell queue iterates until the pumped routine is done.</summary>
    internal IEnumerator Wait()
    {
        while (!_finished)
            yield return null;
    }

    public object? Current => _current;

    public bool MoveNext()
    {
        ServiceScope.Enter();
        try
        {
            while (_frames.Count > 0)
            {
                var frame = _frames.Peek();
                if (!frame.MoveNext())
                {
                    _frames.Pop();
                    continue;
                }

                if (frame.Current is IEnumerator nested)
                {
                    _frames.Push(nested);
                    continue;
                }

                _current = frame.Current;
                return true;
            }

            return Done();
        }
        catch (Exception error)
        {
            // A failed card is reported and closed here: the game's own spell queue catches and logs per card
            // as well, and the queue behind this session has to be released either way. The routine is not
            // resumed, so the failure is never retried silently.
            BridgeLog.Shared.Error($"spell {_spellId} routine stopped: {error}");
            return Done();
        }
        finally
        {
            ServiceScope.Exit();
        }
    }

    public void Reset()
    {
    }

    private bool Done()
    {
        _finished = true;
        WorkSceneSpellHost.Shared.End();
        return false;
    }
}

/// <summary>
/// Adapter that hands a managed routine to the game as an <c>Il2CppSystem.Collections.IEnumerator</c>
/// (equivalent to the removed BepInEx <c>IEnumerator.WrapToIl2Cpp()</c>).
/// <para>
/// The game's spell queue iterates the value a card returns with <c>yield return</c>, so it has to be an
/// IL2CPP type implementing <c>Il2CppSystem.Collections.IEnumerator</c>: the interface is registered while
/// the type is injected, otherwise IL2CPP does not see it as an enumerator and the cast fails.
/// </para>
/// </summary>
public sealed class ManagedEnumerator : Il2CppSystem.Object
{
    // Set by the managed constructor; the IL2CPP side constructor only wraps a native pointer.
    private readonly IEnumerator _routine = null!;

    static ManagedEnumerator() =>
        ClassInjector.RegisterTypeInIl2Cpp<ManagedEnumerator>(new RegisterTypeOptions
        {
            Interfaces = new[] { typeof(Il2CppSystem.Collections.IEnumerator) },
        });

    public ManagedEnumerator(nint pointer) : base(pointer)
    {
    }

    public ManagedEnumerator(IEnumerator routine)
        : base(ClassInjector.DerivedConstructorPointer<ManagedEnumerator>())
    {
        _routine = routine ?? throw new ArgumentNullException(nameof(routine));
        ClassInjector.DerivedConstructorBody(this);
    }

    /// <summary>Wraps a managed routine; an IL2CPP enumerator is handed back unchanged.</summary>
    public static Il2CppSystem.Collections.IEnumerator Wrap(IEnumerator routine) =>
        routine is Il2CppSystem.Collections.IEnumerator il2Cpp
            ? il2Cpp
            : new ManagedEnumerator(routine).Cast<Il2CppSystem.Collections.IEnumerator>();

    public Il2CppSystem.Object Current
    {
        get
        {
            var current = _routine.Current;
            return current switch
            {
                null => null!,
                Il2CppSystem.Object il2CppObject => il2CppObject,
                IEnumerator nested => new ManagedEnumerator(nested),
                _ => Unexpected(current),
            };
        }
    }

    public bool MoveNext() => _routine.MoveNext();

    public void Reset() => _routine.Reset();

    private static Il2CppSystem.Object Unexpected(object value)
    {
        BridgeLog.Shared.Warning($"managed routine yielded {value.GetType().FullName}, treated as the next frame");
        return null!;
    }
}

/// <summary>
/// Log handed to mod spell routines.
/// <para>
/// A mod gets its own <see cref="ILog"/> from <c>IModContext</c>, but a spell asset carries no back reference
/// to the mod that registered the implementation, so the routines log into the framework sink under the
/// bridge identity instead of pretending to be the mod.
/// </para>
/// </summary>
internal sealed class BridgeLog : ILog
{
    internal static readonly BridgeLog Shared = new();

    private readonly string _tag;

    private BridgeLog(string tag = "") => _tag = tag;

    public string Id => "Mystia.Modding.Bridge";

    public string Version => "";

    public void Info(string message) => Log(LogLevel.Info, message);

    public void Warning(string message) => Log(LogLevel.Warning, message);

    public void Error(string message) => Log(LogLevel.Error, message);

    public void Debug(string message) => Log(LogLevel.Debug, message);

    public void Message(string message) => Log(LogLevel.Message, message);

    public void Fatal(string message) => Log(LogLevel.Fatal, message);

    public void Log(LogLevel level, string message) =>
        GameBridgeHook.Trace(string.IsNullOrEmpty(_tag) ? $"{level} {message}" : $"{level} [{_tag}] {message}");

    public ILog Tag(string tag) => new BridgeLog(tag);
}
