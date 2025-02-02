﻿using System.Collections.Generic;
using System.Linq;
using TOHE.Roles.Crewmate;
using static TOHE.Options;

namespace TOHE.Roles.Impostor
{
    internal class Nullifier
    {
        private static readonly int Id = 642000;
        public static List<byte> playerIdList = new();

        public static OptionItem NullCD;
        private static OptionItem KCD;
        private static OptionItem Delay;

        public static void SetupCustomOption()
        {
            SetupRoleOptions(Id, TabGroup.ImpostorRoles, CustomRoles.Nullifier);
            NullCD = FloatOptionItem.Create(Id + 10, "NullCD", new(0f, 180f, 2.5f), 30f, TabGroup.ImpostorRoles, false)
                .SetParent(CustomRoleSpawnChances[CustomRoles.Nullifier])
                .SetValueFormat(OptionFormat.Seconds);
            KCD = FloatOptionItem.Create(Id + 11, "KillCooldown", new(0f, 180f, 2.5f), 25f, TabGroup.ImpostorRoles, false)
                .SetParent(CustomRoleSpawnChances[CustomRoles.Nullifier])
                .SetValueFormat(OptionFormat.Seconds);
            Delay = IntegerOptionItem.Create(Id + 12, "NullifierDelay", new(0, 20, 1), 5, TabGroup.ImpostorRoles, false)
                .SetParent(CustomRoleSpawnChances[CustomRoles.Nullifier])
                .SetValueFormat(OptionFormat.Seconds);
        }

        public static void Init()
        {
            playerIdList = new();
        }

        public static void Add(byte playerId)
        {
            playerIdList.Add(playerId);
        }

        public static void SetKillCooldown(byte id) => Main.AllPlayerKillCooldown[id] = KCD.GetFloat();

        public static bool IsEnable => playerIdList.Any();

        public static bool OnCheckMurder(PlayerControl killer, PlayerControl target)
        {
            if (killer == null) return false;
            if (target == null) return false;
            if (!killer.Is(CustomRoles.Nullifier)) return false;

            return killer.CheckDoubleTrigger(target, () =>
            {
                killer.SetKillCooldown(time: NullCD.GetFloat());
                killer.Notify(Translator.GetString("NullifierUseRemoved"));
                _ = new LateTask(() =>
                {
                    switch (target.GetCustomRole())
                    {
                        case CustomRoles.Admirer:
                            Admirer.AdmireLimit--;
                            Admirer.SendRPC();
                            break;
                        case CustomRoles.Aid:
                            Aid.UseLimit[target.PlayerId]--;
                            break;
                        case CustomRoles.Bloodhound:
                            Bloodhound.UseLimit[target.PlayerId]--;
                            Bloodhound.SendRPCPlus(target.PlayerId);
                            break;
                        case CustomRoles.CameraMan:
                            CameraMan.UseLimit[target.PlayerId]--;
                            CameraMan.SendRPC(target.PlayerId);
                            break;
                        case CustomRoles.Chameleon:
                            Chameleon.UseLimit[target.PlayerId]--;
                            Chameleon.SendRPCPlus(target.PlayerId);
                            break;
                        case CustomRoles.Cleanser:
                            Cleanser.CleanserUses[target.PlayerId]--;
                            Cleanser.SendRPC(target.PlayerId);
                            break;
                        case CustomRoles.Crusader:
                            Crusader.CrusaderLimit[target.PlayerId]--;
                            break;
                        case CustomRoles.Deputy:
                            Deputy.HandcuffLimit--;
                            Deputy.SendRPC();
                            break;
                        case CustomRoles.Divinator:
                            Divinator.CheckLimit[target.PlayerId]--;
                            Divinator.SendRPC(target.PlayerId);
                            break;
                        case CustomRoles.Doormaster:
                            Doormaster.UseLimit[target.PlayerId]--;
                            Doormaster.SendRPC(target.PlayerId);
                            break;
                        case CustomRoles.Mediumshiper:
                            Mediumshiper.ContactLimit[target.PlayerId]--;
                            Mediumshiper.SendRPC(target.PlayerId);
                            break;
                        case CustomRoles.Monarch:
                            Monarch.KnightLimit--;
                            Monarch.SendRPC();
                            break;
                        case CustomRoles.NiceEraser:
                            NiceEraser.EraseLimit[target.PlayerId]--;
                            NiceEraser.SendRPC(target.PlayerId);
                            break;
                        case CustomRoles.NiceHacker:
                            if (target.IsModClient())
                            {
                                NiceHacker.UseLimitSeconds[target.PlayerId] -= NiceHacker.ModdedClientAbilityUseSecondsMultiplier.GetInt();
                                NiceHacker.SendRPC(target.PlayerId, NiceHacker.UseLimitSeconds[target.PlayerId]);
                            }
                            else
                            {
                                NiceHacker.UseLimit[target.PlayerId]--;
                            }
                            break;
                        case CustomRoles.NiceSwapper:
                            NiceSwapper.NiceSwappermax[target.PlayerId]--;
                            break;
                        case CustomRoles.Oracle:
                            Oracle.CheckLimit[target.PlayerId]--;
                            Oracle.SendRPC(target.PlayerId);
                            break;
                        case CustomRoles.ParityCop:
                            ParityCop.MaxCheckLimit[target.PlayerId]--;
                            break;
                        case CustomRoles.Ricochet:
                            Ricochet.UseLimit[target.PlayerId]--;
                            Ricochet.SendRPC(target.PlayerId);
                            break;
                        case CustomRoles.SabotageMaster:
                            SabotageMaster.UsedSkillCount++;
                            SabotageMaster.SendRPC(SabotageMaster.UsedSkillCount);
                            break;
                        case CustomRoles.Sheriff:
                            Sheriff.ShotLimit[target.PlayerId]--;
                            Sheriff.SendRPC(target.PlayerId);
                            break;
                        case CustomRoles.Spy:
                            Spy.UseLimit[target.PlayerId]--;
                            Spy.SendAbilityRPC(target.PlayerId);
                            break;
                        case CustomRoles.SwordsMan:
                            SwordsMan.killed.Add(target.PlayerId);
                            SwordsMan.SendRPC(target.PlayerId);
                            break;
                        case CustomRoles.Tether:
                            Tether.UseLimit[target.PlayerId]--;
                            Tether.SendRPC(target.PlayerId);
                            break;
                        case CustomRoles.Tracker:
                            Tracker.TrackLimit[target.PlayerId]--;
                            Tracker.SendRPC(trackerId: target.PlayerId);
                            break;
                        case CustomRoles.Veteran:
                            Main.VeteranNumOfUsed[target.PlayerId]--;
                            break;
                        case CustomRoles.Grenadier:
                            Main.GrenadierNumOfUsed[target.PlayerId]--;
                            break;
                        case CustomRoles.Lighter:
                            Main.LighterNumOfUsed[target.PlayerId]--;
                            break;
                        case CustomRoles.DovesOfNeace:
                            Main.DovesOfNeaceNumOfUsed[target.PlayerId]--;
                            break;
                        case CustomRoles.TimeMaster:
                            Main.TimeMasterNumOfUsed[target.PlayerId]--;
                            break;
                        case CustomRoles.SecurityGuard:
                            Main.SecurityGuardNumOfUsed[target.PlayerId]--;
                            break;
                        case CustomRoles.Ventguard:
                            Main.VentguardNumberOfAbilityUses--;
                            break;
                        default:
                            break;
                    }
                    if (GameStates.IsInTask) Utils.NotifyRoles(SpecifySeer: target);
                }, Delay.GetInt(), "Nullifier Remove Ability Use");
            });
        }
    }
}
