using System.Reflection;
using System.Reflection.Emit;
using GameData.Core.Collections.NightSceneUtility;
using HarmonyLib;
using Mystia.Listeners;
using Mystia.Scenes;
using NightScene.GuestManagementUtility;

namespace Mystia.Modding.Bridge;

internal static class GuestPipeline
{
    // The engine group stays this file's own business: the listener is handed its handle, and the framework's
    // own director contract (IGuestDirector, which a mod must not implement) keeps naming the controller.
    internal static IGuestDriver? NotifySpawned(GuestGroupController group, GuestSpawnRequest request = default)
    {
        var handle = EntitySeams.GuestHandleOf(group);
        foreach (var listener in Dispatch.Instances<IGuestGroupListener>())
            listener.OnGroupSpawned(handle, request);
        foreach (var director in Dispatch.Instances<IGuestDirector>())
        {
            var driver = director.Claim(group);
            if (driver is not null)
                return driver;
        }

        return null;
    }

    /// <summary>
    /// Asks every listener about the order the game just generated. The order travels by handle; the seam that
    /// called this resolves the handle the listeners leave behind (see GuestSeams.Ordered), so a listener that
    /// writes another handle back replaces the game's order while one that leaves it alone changes nothing.
    /// </summary>
    internal static void RunOrder(GuestGroupController? group, ref OrderHandle? order, ref string message)
    {
        var handle = EntitySeams.GuestHandleOf(group);
        foreach (var listener in Dispatch.Instances<IGuestGroupListener>())
            listener.OnGroupOrdered(handle, ref order, ref message);
    }

    internal static void RunEvaluation(GuestGroupController group, ref GuestEvaluation result)
    {
        var handle = EntitySeams.GuestHandleOf(group);
        foreach (var listener in Dispatch.Instances<IGuestGroupListener>())
            listener.OnGroupEvaluated(handle, ref result);
    }

    internal static IEnumerable<CodeInstruction> InsertEvaluationOverride(IEnumerable<CodeInstruction> instructions)
    {
        var list = instructions.ToList();
        var adjust = typeof(GuestPipeline).GetMethod(nameof(AdjustEvaluation), BindingFlags.NonPublic | BindingFlags.Static);
        for (var index = 1; index < list.Count; index++)
        {
            if (list[index].opcode != OpCodes.Switch)
                continue;
            var load = list[index - 1];
            var address = LoadAddress(load);
            if (address is null)
                continue;
            list.Insert(index - 1, address);
            list.Insert(index - 1, new CodeInstruction(OpCodes.Ldarg_1));
            list.Insert(index + 1, new CodeInstruction(OpCodes.Call, adjust));
            return list;
        }

        return list;
    }

    // The game's verdict is mirrored into the framework's own enum for the listener and mirrored back, because
    // the listener may replace it. The inserted call keeps the game's own signature so the transpiler stays a
    // two instruction splice.
    private static void AdjustEvaluation(GuestGroupController group, ref GuestGroupController.EvaluationResult result)
    {
        var mirrored = Mirrors.ToSdk(result);
        RunEvaluation(group, ref mirrored);
        result = Mirrors.ToGame(mirrored);
    }

    private static CodeInstruction? LoadAddress(CodeInstruction load)
    {
        if (load.opcode == OpCodes.Ldloc_0)
            return new CodeInstruction(OpCodes.Ldloca_S, (byte)0);
        if (load.opcode == OpCodes.Ldloc_1)
            return new CodeInstruction(OpCodes.Ldloca_S, (byte)1);
        if (load.opcode == OpCodes.Ldloc_2)
            return new CodeInstruction(OpCodes.Ldloca_S, (byte)2);
        if (load.opcode == OpCodes.Ldloc_3)
            return new CodeInstruction(OpCodes.Ldloca_S, (byte)3);
        if (load.opcode == OpCodes.Ldloc_S || load.opcode == OpCodes.Ldloc)
            return new CodeInstruction(load.opcode == OpCodes.Ldloc_S ? OpCodes.Ldloca_S : OpCodes.Ldloca, load.operand);
        return null;
    }
}
