using HarmonyLib;
using Hazel;
using System.Linq;

namespace TOHE;

//参考
//https://github.com/Koke1024/Town-Of-Moss/blob/main/TownOfMoss/Patches/MeltDownBoost.cs

[HarmonyPatch(typeof(ReactorSystemType), nameof(ReactorSystemType.Deteriorate))]
public static class ReactorSystemTypePatch
{
    private static bool SetDurationForReactorSabotage = true;
    public static void Prefix(ReactorSystemType __instance)
    {
        if (!Options.SabotageTimeControl.GetBool()) return;
        if ((MapNames)Main.NormalOptions.MapId is MapNames.Airship) return;

        // When the sabotage ends
        if (!__instance.IsActive || !SetDurationForReactorSabotage)
        {
            if (!SetDurationForReactorSabotage && !__instance.IsActive)
            {
                SetDurationForReactorSabotage = true;
            }
            return;
        }

        Logger.Info($" {ShipStatus.Instance.Type}", "ReactorSystemTypePatch - ShipStatus.Instance.Type");
        Logger.Info($" {SetDurationForReactorSabotage}", "ReactorSystemTypePatch - SetDurationCriticalSabotage");
        SetDurationForReactorSabotage = false;

        switch (ShipStatus.Instance.Type)
        {
            case ShipStatus.MapType.Ship: //The Skeld
                __instance.Countdown = Options.SkeldReactorTimeLimit.GetFloat();
                return;
            case ShipStatus.MapType.Hq: //Mira HQ
                __instance.Countdown = Options.MiraReactorTimeLimit.GetFloat();
                return;
            case ShipStatus.MapType.Pb: //Polus
                __instance.Countdown = Options.PolusReactorTimeLimit.GetFloat();
                return;
            case ShipStatus.MapType.Fungle: //The Fungle
                __instance.Countdown = Options.FungleReactorTimeLimit.GetFloat();
                return;
            default:
                return;
        }
    }
}
[HarmonyPatch(typeof(HeliSabotageSystem), nameof(HeliSabotageSystem.Deteriorate))]
public static class HeliSabotageSystemPatch
{
    private static bool SetDurationForReactorSabotage = true;
    public static void Prefix(HeliSabotageSystem __instance)
    {
        if (!Options.SabotageTimeControl.GetBool()) return;
        if ((MapNames)Main.NormalOptions.MapId is not MapNames.Airship) return;

        // When the sabotage ends
        if (!__instance.IsActive || ShipStatus.Instance == null || !SetDurationForReactorSabotage)
        {
            if (!SetDurationForReactorSabotage && !__instance.IsActive)
            {
                SetDurationForReactorSabotage = true;
            }
            return;
        }

        Logger.Info($" {ShipStatus.Instance.Type}", "HeliSabotageSystemPatch - ShipStatus.Instance.Type");
        Logger.Info($" {SetDurationForReactorSabotage}", "HeliSabotageSystemPatch - SetDurationCriticalSabotage");
        SetDurationForReactorSabotage = false;

        __instance.Countdown = Options.AirshipReactorTimeLimit.GetFloat();
    }
}
[HarmonyPatch(typeof(LifeSuppSystemType), nameof(LifeSuppSystemType.Deteriorate))]
public static class LifeSuppSystemTypePatch
{
    private static bool SetDurationForO2Sabotage = true;
    public static void Prefix(LifeSuppSystemType __instance)
    {
        if (!Options.SabotageTimeControl.GetBool()) return;
        if ((MapNames)Main.NormalOptions.MapId is MapNames.Polus or MapNames.Airship or MapNames.Fungle) return;

        // When the sabotage ends
        if (!__instance.IsActive || !SetDurationForO2Sabotage)
        {
            if (!SetDurationForO2Sabotage && !__instance.IsActive)
            {
                SetDurationForO2Sabotage = true;
            }
            return;
        }

        Logger.Info($" {ShipStatus.Instance.Type}", "LifeSuppSystemType - ShipStatus.Instance.Type");
        Logger.Info($" {SetDurationForO2Sabotage}", "LifeSuppSystemType - SetDurationCriticalSabotage");
        SetDurationForO2Sabotage = false;

        switch (ShipStatus.Instance.Type)
        {
            case ShipStatus.MapType.Ship: // The Skeld
                __instance.Countdown = Options.SkeldO2TimeLimit.GetFloat();
                return;
            case ShipStatus.MapType.Hq: // Mira HQ
                __instance.Countdown = Options.MiraO2TimeLimit.GetFloat();
                return;
            default:
                return;
        }
    }
}
[HarmonyPatch(typeof(MushroomMixupSabotageSystem), nameof(MushroomMixupSabotageSystem.Deteriorate))]
public static class MushroomMixupSabotageSystemPatch
{
    private static bool SetDurationMushroomMixupSabotage = true;
    public static void Prefix(MushroomMixupSabotageSystem __instance, ref bool __state)
    {
        __state = __instance.IsActive;

        if (Options.UsePets.GetBool())
        {
            __instance.petEmptyChance = 0;
        }

        if (!Options.SabotageTimeControl.GetBool()) return;
        if ((MapNames)Main.NormalOptions.MapId is not MapNames.Fungle) return;

        // When the sabotage ends
        if (!__instance.IsActive || !SetDurationMushroomMixupSabotage)
        {
            if (!SetDurationMushroomMixupSabotage && !__instance.IsActive)
            {
                SetDurationMushroomMixupSabotage = true;
            }
            return;
        }

        Logger.Info($" {ShipStatus.Instance.Type}", "MushroomMixupSabotageSystem - ShipStatus.Instance.Type");
        Logger.Info($" {SetDurationMushroomMixupSabotage}", "MushroomMixupSabotageSystem - SetDurationCriticalSabotage");
        SetDurationMushroomMixupSabotage = false;

        __instance.currentSecondsUntilHeal = Options.FungleMushroomMixupDuration.GetFloat();
    }
    public static void Postfix(MushroomMixupSabotageSystem __instance, bool __state)
    {
        // When Mushroom Mixup Sabotage ends
        if (__instance.IsActive != __state && GameStates.IsInTask)
        {
            _ = new LateTask(() =>
            {
                // After MushroomMixup sabotage, shapeshift cooldown sets to 0
                foreach (var pc in Main.AllAlivePlayerControls.ToArray())
                {
                    // Reset Ability Cooldown To Default For Alive Players
                    pc.RpcResetAbilityCooldown();
                }
            }, 1.2f, "Reset Ability Cooldown Arter Mushroom Mixup");

            foreach (var pc in Main.AllAlivePlayerControls.Where(pc => !pc.Is(CustomRoleTypes.Impostor) && Main.ResetCamPlayerList.Contains(pc.PlayerId)).ToArray())
            {
                Utils.NotifyRoles(SpecifySeer: pc, ForceLoop: true, MushroomMixup: true);
            }
        }
    }
}
[HarmonyPatch(typeof(ElectricTask), nameof(ElectricTask.Initialize))]
public static class ElectricTaskInitializePatch
{
    public static void Postfix()
    {
        Utils.MarkEveryoneDirtySettingsV2();

        if (!GameStates.IsMeeting)
        {
            foreach (PlayerControl pc in Main.AllAlivePlayerControls)
            {
                if (CustomRolesHelper.NeedUpdateOnLights(pc.GetCustomRole()))
                {
                    Utils.NotifyRoles(SpecifySeer: pc, ForceLoop: true);
                }
            }
        }

        Logger.Info("Lights sabotage called", "ElectricTask");
    }
}
[HarmonyPatch(typeof(ElectricTask), nameof(ElectricTask.Complete))]
public static class ElectricTaskCompletePatch
{
    public static void Postfix()
    {
        Utils.MarkEveryoneDirtySettingsV2();

        if (!GameStates.IsMeeting)
        {
            foreach (PlayerControl pc in Main.AllAlivePlayerControls)
            {
                if (CustomRolesHelper.NeedUpdateOnLights(pc.GetCustomRole()))
                {
                    Utils.NotifyRoles(SpecifySeer: pc, ForceLoop: true);
                }
            }
        }

        Logger.Info("Lights sabotage fixed", "ElectricTask");
    }
}
// https://github.com/tukasa0001/TownOfHost/blob/357f7b5523e4bdd0bb58cda1e0ff6cceaa84813d/Patches/SabotageSystemPatch.cs
// Method called when sabotage occurs
[HarmonyPatch(typeof(SabotageSystemType), nameof(SabotageSystemType.UpdateSystem))]
public static class SabotageSystemTypeRepairDamagePatch
{
    private static bool isCooldownModificationEnabled;
    private static float modifiedCooldownSec;

    public static void Initialize()
    {
        isCooldownModificationEnabled = Options.SabotageCooldownControl.GetBool();
        modifiedCooldownSec = Options.SabotageCooldown.GetFloat();
    }

    public static void Postfix(SabotageSystemType __instance)
    {
        if (!isCooldownModificationEnabled || !AmongUsClient.Instance.AmHost)
        {
            return;
        }
        __instance.Timer = modifiedCooldownSec;
        __instance.IsDirty = true;
    }
}
[HarmonyPatch(typeof(SecurityCameraSystemType), nameof(SecurityCameraSystemType.UpdateSystem))]
public static class SecurityCameraPatch
{
    public static bool Prefix([HarmonyArgument(1)] MessageReader msgReader)
    {
        byte amount;
        {
            var newReader = MessageReader.Get(msgReader);
            amount = newReader.ReadByte();
            newReader.Recycle();
        }

        if (amount == SecurityCameraSystemType.IncrementOp)
        {
            return !((MapNames)Main.NormalOptions.MapId switch
            {
                MapNames.Skeld => Options.DisableSkeldCamera.GetBool(),
                MapNames.Polus => Options.DisablePolusCamera.GetBool(),
                MapNames.Airship => Options.DisableAirshipCamera.GetBool(),
                _ => false,
            });
        }
        return true;
    }
}