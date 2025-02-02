using HarmonyLib;
using Hazel;
using System;
using System.Collections.Generic;
using System.Linq;
using TOHE.Roles.AddOns.Impostor;
using TOHE.Roles.Crewmate;
using TOHE.Roles.Neutral;
using UnityEngine;

namespace TOHE;

[HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.FixedUpdate))]
class ShipFixedUpdatePatch
{
    public static void Postfix(/*ShipStatus __instance*/)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (Main.IsFixedCooldown && Main.RefixCooldownDelay >= 0)
        {
            Main.RefixCooldownDelay -= Time.fixedDeltaTime;
        }
        else if (!float.IsNaN(Main.RefixCooldownDelay))
        {
            Utils.MarkEveryoneDirtySettingsV4();
            Main.RefixCooldownDelay = float.NaN;
            Logger.Info("Refix Cooldown", "CoolDown");
        }
    }
}
[HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.UpdateSystem), typeof(SystemTypes), typeof(PlayerControl), typeof(MessageReader))]
public static class MessageReaderUpdateSystemPatch
{
    public static void Prefix(ShipStatus __instance, [HarmonyArgument(0)] SystemTypes systemType, [HarmonyArgument(1)] PlayerControl player, [HarmonyArgument(2)] MessageReader reader)
    {
        RepairSystemPatch.Prefix(__instance, systemType, player, MessageReader.Get(reader).ReadByte());
    }
    public static void Postfix(ShipStatus __instance, [HarmonyArgument(0)] SystemTypes systemType, [HarmonyArgument(1)] PlayerControl player, [HarmonyArgument(2)] MessageReader reader)
    {
        RepairSystemPatch.Postfix(__instance, systemType, player, MessageReader.Get(reader).ReadByte());
    }
}
[HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.UpdateSystem), typeof(SystemTypes), typeof(PlayerControl), typeof(byte))]
class RepairSystemPatch
{
    public static bool IsComms;
    public static bool Prefix(ShipStatus __instance,
        [HarmonyArgument(0)] SystemTypes systemType,
        [HarmonyArgument(1)] PlayerControl player,
        [HarmonyArgument(2)] byte amount)
    {
        Logger.Msg("SystemType: " + systemType.ToString() + ", PlayerName: " + player.GetNameWithRole().RemoveHtmlTags() + ", amount: " + amount, "RepairSystem");
        if (RepairSender.enabled && AmongUsClient.Instance.NetworkMode != NetworkModes.OnlineGame)
            Logger.SendInGame("SystemType: " + systemType.ToString() + ", PlayerName: " + player.GetNameWithRole().RemoveHtmlTags() + ", amount: " + amount);

        if (!AmongUsClient.Instance.AmHost) return true; //Execute the following only on the host

        IsComms = PlayerControl.LocalPlayer.myTasks.ToArray().Any(x => x.TaskType == TaskTypes.FixComms);

        if ((Options.CurrentGameMode == CustomGameMode.SoloKombat || Options.CurrentGameMode == CustomGameMode.FFA) && systemType == SystemTypes.Sabotage) return false;

        if (Options.DisableSabotage.GetBool() && systemType == SystemTypes.Sabotage) return false;

        //Note: "SystemTypes.Laboratory" сauses bugs in the Host, it is better not to use
        if (player.Is(CustomRoles.Fool) &&
            (systemType is
            SystemTypes.Comms or
            SystemTypes.Electrical))
        { return false; }

        switch (player.GetCustomRole())
        {
            case CustomRoles.SabotageMaster:
                SabotageMaster.RepairSystem(__instance, systemType, amount);
                break;
            case CustomRoles.Alchemist when Alchemist.FixNextSabo:
                Alchemist.RepairSystem(systemType, amount);
                break;
        }

        switch (systemType)
        {
            case SystemTypes.Doors when player.Is(CustomRoles.Unlucky) && player.IsAlive():
                var Ue = IRandom.Instance;
                if (Ue.Next(0, 100) < Options.UnluckySabotageSuicideChance.GetInt())
                {
                    player.Kill(player);
                    Main.PlayerStates[player.PlayerId].deathReason = PlayerState.DeathReason.Suicide;
                    return false;
                }
                break;
            case SystemTypes.Electrical when 0 <= amount && amount <= 4 && Main.NormalOptions.MapId == 4:
                if (Options.DisableAirshipViewingDeckLightsPanel.GetBool() && Vector2.Distance(player.transform.position, new(-12.93f, -11.28f)) <= 2f) return false;
                if (Options.DisableAirshipGapRoomLightsPanel.GetBool() && Vector2.Distance(player.transform.position, new(13.92f, 6.43f)) <= 2f) return false;
                if (Options.DisableAirshipCargoLightsPanel.GetBool() && Vector2.Distance(player.transform.position, new(30.56f, 2.12f)) <= 2f) return false;
                break;
            case SystemTypes.Sabotage when AmongUsClient.Instance.NetworkMode != NetworkModes.FreePlay:
                if (Main.BlockSabo.Any()) return false;
                if (player.Is(CustomRoleTypes.Impostor) && (player.IsAlive() || !Options.DeadImpCantSabotage.GetBool()) && !player.Is(CustomRoles.Minimalism)) return true;
                switch (player.GetCustomRole())
                {
                    case CustomRoles.Glitch:
                        Glitch.Mimic(player);
                        return false;
                    case CustomRoles.Magician:
                        Magician.UseCard(player);
                        return false;
                    case CustomRoles.WeaponMaster:
                        WeaponMaster.SwitchMode();
                        return false;
                    case CustomRoles.Jackal when Jackal.CanUseSabotage.GetBool():
                        return true;
                    case CustomRoles.Sidekick when Jackal.CanUseSabotageSK.GetBool():
                        return true;
                    case CustomRoles.Traitor when Traitor.CanUseSabotage.GetBool():
                        return true;
                    case CustomRoles.Parasite when player.IsAlive():
                        return true;
                    case CustomRoles.Refugee when player.IsAlive():
                        return true;
                    default:
                        return false;
                }
            case SystemTypes.Security when amount == 1:
                var camerasDisabled = (MapNames)Main.NormalOptions.MapId switch
                {
                    MapNames.Skeld => Options.DisableSkeldCamera.GetBool(),
                    MapNames.Polus => Options.DisablePolusCamera.GetBool(),
                    MapNames.Airship => Options.DisableAirshipCamera.GetBool(),
                    _ => false,
                };
                if (camerasDisabled)
                {
                    player.Notify(Translator.GetString("CamerasDisabledNotify"), 15f);
                }
                return !camerasDisabled;
        }
        return true;
    }
    public static void Postfix(ShipStatus __instance,
        [HarmonyArgument(0)] SystemTypes systemType,
        [HarmonyArgument(1)] PlayerControl player,
        [HarmonyArgument(2)] byte amount)
    {
        Camouflage.CheckCamouflage();

        if (systemType == SystemTypes.Electrical && 0 <= amount && amount <= 4)
        {
            var SwitchSystem = ShipStatus.Instance.Systems[SystemTypes.Electrical].Cast<SwitchSystem>();
            if (SwitchSystem != null && SwitchSystem.IsActive)
            {
                switch (player.GetCustomRole())
                {
                    case CustomRoles.SabotageMaster:
                        Logger.Info($"{player.GetNameWithRole().RemoveHtmlTags()} instant-fix-lights", "SwitchSystem");
                        SabotageMaster.SwitchSystemRepair(SwitchSystem, amount);
                        break;
                    case CustomRoles.Alchemist when Alchemist.FixNextSabo:
                        Logger.Info($"{player.GetNameWithRole().RemoveHtmlTags()} instant-fix-lights", "SwitchSystem");
                        SwitchSystem.ActualSwitches = 0;
                        SwitchSystem.ExpectedSwitches = 0;
                        Alchemist.FixNextSabo = false;
                        break;
                }
            }
        }

        if (player.Is(CustomRoles.Damocles) && systemType is SystemTypes.Reactor or SystemTypes.LifeSupp or SystemTypes.Comms or SystemTypes.Laboratory or SystemTypes.HeliSabotage or SystemTypes.Electrical)
        {
            Damocles.OnRepairSabotage();
        }
    }
    public static void CheckAndOpenDoorsRange(ShipStatus __instance, int amount, int min, int max)
    {
        var Ids = new List<int>();
        for (var i = min; i <= max; i++)
        {
            Ids.Add(i);
        }
        CheckAndOpenDoors(__instance, amount, Ids.ToArray());
    }
    private static void CheckAndOpenDoors(ShipStatus __instance, int amount, params int[] DoorIds)
    {
        if (!DoorIds.Contains(amount)) return;
        foreach (int id in DoorIds)
        {
            __instance.RpcUpdateSystem(SystemTypes.Doors, (byte)id);
        }
    }
}
[HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.CloseDoorsOfType))]
class CloseDoorsPatch
{
    public static bool Prefix(/*ShipStatus __instance, */[HarmonyArgument(0)] SystemTypes room)
    {
        bool allow = !Options.DisableSabotage.GetBool() && Options.CurrentGameMode != CustomGameMode.SoloKombat && Options.CurrentGameMode != CustomGameMode.FFA;

        if (Main.BlockSabo.Any()) allow = false;
        if (Options.DisableCloseDoor.GetBool()) allow = false;

        Logger.Info($"({room}) => {(allow ? "Allowed" : "Blocked")}", "DoorClose");
        return allow;
    }
}
[HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Start))]
class StartPatch
{
    public static void Postfix()
    {
        Logger.CurrentMethod();
        Logger.Info("-----------Game start-----------", "Phase");

        Utils.CountAlivePlayers(true);

        if (Options.AllowConsole.GetBool())
        {
            if (!BepInEx.ConsoleManager.ConsoleActive && BepInEx.ConsoleManager.ConsoleEnabled)
                BepInEx.ConsoleManager.CreateConsole();
        }
        else
        {
            if (BepInEx.ConsoleManager.ConsoleActive && !DebugModeManager.AmDebugger)
            {
                BepInEx.ConsoleManager.DetachConsole();
                Logger.SendInGame("Sorry, console use is prohibited in this room, so your console has been turned off");
            }
        }
    }
}
[HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.StartMeeting))]
class StartMeetingPatch
{
    public static void Prefix(/*ShipStatus __instance, PlayerControl reporter,*/ GameData.PlayerInfo target)
    {
        MeetingStates.ReportTarget = target;
        MeetingStates.DeadBodies = UnityEngine.Object.FindObjectsOfType<DeadBody>();
    }
}
[HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Begin))]
class BeginPatch
{
    public static void Postfix()
    {
        Logger.CurrentMethod();

        //Should I initialize the host role here?
    }
}
[HarmonyPatch(typeof(GameManager), nameof(GameManager.CheckTaskCompletion))]
class CheckTaskCompletionPatch
{
    public static bool Prefix(ref bool __result)
    {
        if (Options.DisableTaskWin.GetBool() || Options.NoGameEnd.GetBool() || TaskState.InitialTotalTasks == 0 || Options.CurrentGameMode == CustomGameMode.SoloKombat || Options.CurrentGameMode == CustomGameMode.FFA)
        {
            __result = false;
            return false;
        }
        return true;
    }
}