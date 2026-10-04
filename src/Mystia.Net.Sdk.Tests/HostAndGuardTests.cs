using System.Globalization;
using GameData.Core.Collections.NightSceneUtility;
using NightScene.GuestManagementUtility;
using Mystia.Modding.Bridge;
using Mystia.Modding.Host;
using Mystia;
using Mystia.Data;
using Mystia.Listeners;
using Mystia.Scenes;
using Mystia.Syringe;
using Xunit;

namespace Mystia.Tests;

public sealed class HostAndGuardTests
{
    [Fact]
    public void PlayerOrderListsModsThenAppendsTheRestById()
    {
        var mods = new[]
        {
            new ModManifest { Id = "b", LoadAfter = ["a"] },
            new ModManifest { Id = "c" },
            new ModManifest { Id = "a" },
        };

        var sorted = ModOrder.Sort(mods, ["c", "a"]).Select(mod => mod.Id).ToArray();

        Assert.Equal(new[] { "c", "a", "b" }, sorted);
    }

    [Fact]
    public void DuplicateRegistrationKeyFails()
    {
        var registry = new ModRegistry();
        registry.Add("alpha", new object());

        var error = Assert.Throws<InvalidOperationException>(() => registry.Add("alpha", new object()));

        Assert.Contains("alpha", error.Message);
    }

    [Fact]
    public void ClaimedGroupStillNotifiesTheGuestListener()
    {
        var seen = false;
        var registry = new ModRegistry();
        registry.Add<IGuestGroupListener>(new SpawnListener(() => seen = true));
        registry.Add<IGuestDirector>(new ClaimingDirector());
        BridgeInstaller.Bind(registry);
        try
        {
            var driver = GuestPipeline.NotifySpawned(null!);
            Assert.True(seen);
            Assert.NotNull(driver);
        }
        finally
        {
            BridgeInstaller.Bind(null);
        }
    }

    [Fact]
    public void EarlierDirectorOwnsTheGroupAndANullClaimFallsThrough()
    {
        var asked = new List<string>();
        var registry = new ModRegistry();
        registry.Add<IGuestDirector>(new RecordingDirector("first", claim: false, asked));
        registry.Add<IGuestDirector>(new RecordingDirector("second", claim: true, asked));
        registry.Add<IGuestDirector>(new RecordingDirector("third", claim: true, asked));
        BridgeInstaller.Bind(registry);
        try
        {
            var driver = GuestPipeline.NotifySpawned(null!);
            Assert.NotNull(driver);
            Assert.Equal(new[] { "first", "second" }, asked);
        }
        finally
        {
            BridgeInstaller.Bind(null);
        }
    }

    private sealed class SpawnListener(Action seen)
        : IGuestGroupListener
    {
        public void OnGroupSpawned(GuestHandle group, GuestSpawnRequest request) => seen();
    }

    private sealed class ClaimingDirector : IGuestDirector
    {
        public IGuestDriver? Claim(NightScene.GuestManagementUtility.GuestGroupController group) => new IdleDriver();
    }

    private sealed class RecordingDirector(string name, bool claim, List<string> asked)
        : IGuestDirector
    {
        public IGuestDriver? Claim(NightScene.GuestManagementUtility.GuestGroupController group)
        {
            asked.Add(name);
            return claim ? new IdleDriver() : null;
        }
    }

    private sealed class IdleDriver : IGuestDriver
    {
        public void Start(IGuestControls controls)
        {
        }
    }

    [Fact]
    public void GeneratedSampleEntrancesRegisterBothSceneListeners()
    {
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var mods = Path.Combine(Path.GetTempPath(), "mystia-sample-mods-" + Guid.NewGuid().ToString("N"));
        CopySample(repo, "SampleMod.A", Path.Combine(mods, "sample.a"));
        CopySample(repo, "SampleMod.B", Path.Combine(mods, "sample.b"));

        var lines = new List<string>();
        var context = new HostContext(mods, new TextLog(lines.Add));
        var registry = ModLoader.Load(mods, context, ["sample.b", "sample.a"], lines.Add);
        var listeners = registry.GetInstances<ISceneListener>();

        Assert.Equal(2, listeners.Count);
        foreach (var listener in listeners)
            listener.OnSceneStart(SceneId.Day);

        Assert.Contains(lines, line => line.Contains("Sample A saw Day"));
        Assert.Contains(lines, line => line.Contains("Sample B saw Day"));
    }

    private static void CopySample(string repo, string project, string destination)
    {
        var output = Path.Combine(repo, "samples", project, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(destination);
        File.Copy(Path.Combine(output, project + ".dll"), Path.Combine(destination, project + ".dll"));
        File.Copy(Path.Combine(output, "mod.json"), Path.Combine(destination, "mod.json"));
    }

    [Fact]
    public void DayLoopRunsSetupUpdateAndShutdownInModOrder()
    {
        var trace = new List<string>();
        var registry = new ModRegistry();
        registry.Add<IDaySceneGameLoop>(new RecordingDayLoop("b", trace));
        registry.Add<IDaySceneGameLoop>(new RecordingDayLoop("a", trace));
        BridgeInstaller.Bind(registry);
        try
        {
            SceneLoopHost.Enter(SceneId.Day);
            SceneLoopHost.Tick(0.25f);
            SceneLoopHost.Enter(SceneId.Main);

            Assert.Equal(
                new[]
                {
                    "b setup",
                    "a setup",
                    "b update 0.25",
                    "a update 0.25",
                    "b shutdown",
                    "a shutdown",
                },
                trace);
        }
        finally
        {
            SceneLoopHost.Reset();
            BridgeInstaller.Bind(null);
        }
    }

    [Fact]
    public void SceneServicesThrowAfterTheLoopCallReturns()
    {
        IDaySceneServices? captured = null;
        var registry = new ModRegistry();
        registry.Add<IDaySceneGameLoop>(new CapturingDayLoop(services => captured = services));
        BridgeInstaller.Bind(registry);
        try
        {
            SceneLoopHost.Enter(SceneId.Day);
            var services = captured ?? throw new InvalidOperationException("Setup did not receive services.");
            Assert.Throws<InvalidOperationException>(() => services.Schedule.End());
        }
        finally
        {
            SceneLoopHost.Reset();
            BridgeInstaller.Bind(null);
        }
    }

    [Fact]
    public void NormalGuestModifierPassesTheSameListThroughModOrder()
    {
        var replacement = new List<GuestDescription>();
        List<GuestDescription>? seen = null;
        var registry = new ModRegistry();
        registry.Add<IGuestSpawnModifier>(new ReplacingSpawnModifier(replacement));
        registry.Add<IGuestSpawnModifier>(new ReadingSpawnModifier(list => seen = list));
        BridgeInstaller.Bind(registry);
        try
        {
            var guests = new List<GuestDescription>();
            GuestSpawnPipeline.ApplyNormal(ref guests);
            Assert.Same(replacement, guests);
            Assert.Same(replacement, seen);
        }
        finally
        {
            BridgeInstaller.Bind(null);
        }
    }

    private sealed class RecordingDayLoop(string name, List<string> trace)
        : IDaySceneGameLoop
    {
        public void Setup(IDaySceneServices services) => trace.Add($"{name} setup");

        public void Update(IDaySceneServices services, float delta) =>
            trace.Add($"{name} update {delta.ToString(CultureInfo.InvariantCulture)}");

        public void Shutdown(IDaySceneServices services) => trace.Add($"{name} shutdown");
    }

    private sealed class CapturingDayLoop(Action<IDaySceneServices> capture)
        : IDaySceneGameLoop
    {
        public void Setup(IDaySceneServices services) => capture(services);

        public void Update(IDaySceneServices services, float delta)
        {
        }

        public void Shutdown(IDaySceneServices services)
        {
        }
    }

    private sealed class ReplacingSpawnModifier(List<GuestDescription> replacement)
        : IGuestSpawnModifier
    {
        public void OnNormalGuestsGenerating(ref List<GuestDescription> guests) => guests = replacement;
    }

    private sealed class ReadingSpawnModifier(Action<List<GuestDescription>> read)
        : IGuestSpawnModifier
    {
        public void OnNormalGuestsGenerating(ref List<GuestDescription> guests) => read(guests);
    }

    [Fact]
    public void SceneGatesStayOffUntilTheLoopTurnsThemBackOn()
    {
        var registry = new ModRegistry();
        registry.Add<IDaySceneGameLoop>(new GateDayLoop());
        registry.Add<IPrepNightSceneGameLoop>(new PrepGateLoop());
        BridgeInstaller.Bind(registry);
        try
        {
            SceneLoopHost.Enter(SceneId.Day);
            Assert.False(StockGate.DayEnd);
            Assert.False(StockGate.Move);
            Assert.False(StockGate.TransitionDialog);
            SceneLoopHost.Enter(SceneId.PrepNight);
            Assert.False(StockGate.MapConfirm);
        }
        finally
        {
            SceneLoopHost.Reset();
            BridgeInstaller.Bind(null);
        }
    }

    [Fact]
    public void VisualModifierSeesTheIndexItWasGiven()
    {
        int? seen = null;
        var registry = new ModRegistry();
        registry.Add<IGuestSpawnModifier>(new VisualModifier((int id, ref int index) => seen = index));
        BridgeInstaller.Bind(registry);
        try
        {
            var index = 4;
            GuestSpawnPipeline.ApplyVisual(7, ref index);
            Assert.Equal(4, seen);
            Assert.Equal(4, index);
        }
        finally
        {
            BridgeInstaller.Bind(null);
        }
    }

    [Fact]
    public void OrderListenerReceivesTheGenerationMessage()
    {
        string? seen = null;
        var registry = new ModRegistry();
        registry.Add<IGuestGroupListener>(new MessageOrderListener(message => seen = message));
        BridgeInstaller.Bind(registry);
        try
        {
            OrderHandle? order = null;
            var message = "tonight";
            GuestPipeline.RunOrder(null!, ref order, ref message);
            Assert.Equal("tonight", seen);
            Assert.Equal("kept", message);
        }
        finally
        {
            BridgeInstaller.Bind(null);
        }
    }

    private sealed class GateDayLoop : IDaySceneGameLoop
    {
        public void Setup(IDaySceneServices services)
        {
            services.Schedule.SetEndEnabled(false);
            services.Input.SetMoveEnabled(false);
            services.Common.SetNightTransitionEnabled(false);
        }

        public void Update(IDaySceneServices services, float delta)
        {
        }

        public void Shutdown(IDaySceneServices services)
        {
        }
    }

    private sealed class PrepGateLoop : IPrepNightSceneGameLoop
    {
        public void Setup(IPrepNightSceneServices services) => services.Map.SetConfirmEnabled(false);

        public void Update(IPrepNightSceneServices services, float delta)
        {
        }

        public void Shutdown(IPrepNightSceneServices services)
        {
        }
    }

    private sealed class VisualModifier(VisualChange change)
        : IGuestSpawnModifier
    {
        public void OnNormalGuestVisual(int guestId, ref int visualIndex) => change(guestId, ref visualIndex);
    }

    private delegate void VisualChange(int guestId, ref int visualIndex);

    private sealed class MessageOrderListener(Action<string> seen)
        : IGuestGroupListener
    {
        public void OnGroupOrdered(GuestHandle group, ref OrderHandle? order, ref string message)
        {
            seen(message);
            message = "kept";
        }
    }

    [Fact]
    public void DatabaseExtensionsShareProxyListsInPlayerOrder()
    {
        var registry = new ModRegistry();
        registry.Add<IDatabaseExtension>(new AddsRice());
        registry.Add<IDatabaseExtension>(new SeesRice());
        BridgeInstaller.Bind(registry);
        try
        {
            DatabaseInject.ResetForTests();
            DatabaseInject.Collect();
            var ingredient = Assert.Single(DatabaseInject.Ingredients);
            Assert.Equal(7, ingredient.Id);
            Assert.Equal("rice-seen", ingredient.Name);
            Assert.Equal("grain", ingredient.Description);
        }
        finally
        {
            DatabaseInject.ResetForTests();
            BridgeInstaller.Bind(null);
        }
    }

    private sealed class AddsRice : IDatabaseExtension
    {
        public void OnInjectIngredients(List<IngredientData> ingredients) =>
            ingredients.Add(new IngredientData { Id = 7, Name = "rice", Description = "grain", Tags = [2] });
    }

    private sealed class SeesRice : IDatabaseExtension
    {
        public void OnInjectIngredients(List<IngredientData> ingredients)
        {
            for (var index = 0; index < ingredients.Count; index++)
            {
                if (ingredients[index].Name != "rice")
                    continue;
                var seen = ingredients[index];
                seen.Name = "rice-seen";
                ingredients[index] = seen;
            }
        }
    }

    [Fact]
    public void AWrongGameAssemblyHashDoesNotStart()
    {
        var root = Directory.CreateTempSubdirectory("mystia-guard");
        try
        {
            var exe = Path.Combine(root.FullName, "game.exe");
            File.WriteAllBytes(exe, [1]);
            File.WriteAllBytes(Path.Combine(root.FullName, "GameAssembly.dll"), [2, 3, 4]);

            var mismatch = GameAssemblyGuard.Check(exe, "00");
            Assert.False(mismatch.Ok);
            Assert.Contains("was not started", mismatch.Message);

            var installed = Environment.GetEnvironmentVariable("MYSTIA_GAME_EXE");
            if (!string.IsNullOrWhiteSpace(installed))
            {
                var match = GameAssemblyGuard.Check(installed, GameAssemblyGuard.ExpectedReleaseHash);
                Assert.True(match.Ok, match.Message);
            }
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public void TheProxyInstallsBesideTheGameAndLeavesAStrangeVersionDllAlone()
    {
        var root = Directory.CreateTempSubdirectory("mystia-proxy");
        try
        {
            var launcher = Path.Combine(root.FullName, "launcher");
            var game = Path.Combine(root.FullName, "game");
            Directory.CreateDirectory(launcher);
            Directory.CreateDirectory(game);
            var payload = Path.Combine(launcher, "Mystia.Proxy.dll");
            File.WriteAllBytes(payload, [1, 2, 3, 4]);

            var installed = ProxyInstall.Install(game, launcher, payload, force: false);
            Assert.True(installed.Ok, installed.Message);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(game, "version.dll")));
            Assert.Equal(launcher, File.ReadAllText(Path.Combine(game, "Mystia.Proxy.txt")).Trim());

            // Reinstalling over our own copy needs no --force.
            Assert.True(ProxyInstall.Install(game, launcher, payload, force: false).Ok);

            // A version.dll we did not write is left where it is unless --force says otherwise.
            File.WriteAllBytes(Path.Combine(game, "version.dll"), [9, 9]);
            Assert.False(ProxyInstall.Install(game, launcher, payload, force: false).Ok);
            Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(Path.Combine(game, "version.dll")));
            Assert.True(ProxyInstall.Install(game, launcher, payload, force: true).Ok);

            var uninstalled = ProxyInstall.Uninstall(game, launcher, payload);
            Assert.Contains("removed", uninstalled.Message);
            Assert.False(File.Exists(Path.Combine(game, "version.dll")));
            Assert.False(File.Exists(Path.Combine(game, "Mystia.Proxy.txt")));
        }
        finally
        {
            root.Delete(true);
        }
    }
}
