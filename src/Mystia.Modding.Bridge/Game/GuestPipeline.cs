using System.Reflection;
using System.Reflection.Emit;
using GameData.Core.Collections.NightSceneUtility;
using HarmonyLib;
using Mystia.Listeners;
using NightScene.GuestManagementUtility;

namespace Mystia.Modding.Bridge;

internal static class GuestPipeline
{
    internal static IGuestDriver? NotifySpawned(GuestGroupController group)
    {
        foreach (var listener in Dispatch.Instances<IGuestGroupListener>())
            listener.OnGroupSpawned(group);
        foreach (var director in Dispatch.Instances<IGuestDirector>())
        {
            var driver = director.Claim(group);
            if (driver is not null)
                return driver;
        }

        return null;
    }

    internal static void RunOrder(GuestGroupController group, ref GuestsManager.OrderBase order, ref string message)
    {
        foreach (var listener in Dispatch.Instances<IGuestGroupListener>())
            listener.OnGroupOrdered(group, ref order, ref message);
    }

    internal static void RunEvaluation(GuestGroupController group, ref GuestGroupController.EvaluationResult result)
    {
        foreach (var listener in Dispatch.Instances<IGuestGroupListener>())
            listener.OnGroupEvaluated(group, ref result);
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

    private static void AdjustEvaluation(GuestGroupController group, ref GuestGroupController.EvaluationResult result) =>
        RunEvaluation(group, ref result);

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
