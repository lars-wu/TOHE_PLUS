using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using InnerNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TOHE.Modules;
using TOHE.Roles.AddOns.Crewmate;
using TOHE.Roles.AddOns.Impostor;
using TOHE.Roles.Crewmate;
using TOHE.Roles.Impostor;
using TOHE.Roles.Neutral;
using UnityEngine;
using static TOHE.Translator;
using static TOHE.Utils;

namespace TOHE;

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckProtect))]
class CheckProtectPatch
{
    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        if (!AmongUsClient.Instance.AmHost) return false;
        Logger.Info("CheckProtect発生: " + __instance.GetNameWithRole().RemoveHtmlTags() + "=>" + target.GetNameWithRole().RemoveHtmlTags(), "CheckProtect");

        if (__instance.Is(CustomRoles.EvilSpirit))
        {
            if (target.Is(CustomRoles.Spiritcaller))
            {
                Spiritcaller.ProtectSpiritcaller();
            }
            else
            {
                Spiritcaller.HauntPlayer(target);
            }

            __instance.RpcResetAbilityCooldown();
            return true;
        }

        if (__instance.Is(CustomRoles.Sheriff))
        {
            if (__instance.Data.IsDead)
            {
                Logger.Info("Blocked", "CheckProtect");
                return false;
            }
        }
        return true;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CmdCheckMurder))] // Modded
class CmdCheckMurderPatch
{
    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        Logger.Info($"{__instance.GetNameWithRole()} => {target.GetNameWithRole()}", "CmdCheckMurder");

        if (!AmongUsClient.Instance.AmHost) return true;
        return CheckMurderPatch.Prefix(__instance, target);
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckMurder))] // Vanilla
class CheckMurderPatch
{
    public static Dictionary<byte, float> TimeSinceLastKill = new();
    public static void Update()
    {
        for (byte i = 0; i < 15; i++)
        {
            if (TimeSinceLastKill.ContainsKey(i))
            {
                TimeSinceLastKill[i] += Time.deltaTime;
                if (15f < TimeSinceLastKill[i]) TimeSinceLastKill.Remove(i);
            }
        }
    }
    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        if (!AmongUsClient.Instance.AmHost) return false;

        var killer = __instance; // alternative variable

        Logger.Info($"{killer.GetNameWithRole().RemoveHtmlTags()} => {target.GetNameWithRole().RemoveHtmlTags()}", "CheckMurder");

        //死人はキルできない
        if (killer.Data.IsDead)
        {
            Logger.Info($"Killer {killer.GetNameWithRole().RemoveHtmlTags()} is dead, kill canceled", "CheckMurder");
            return false;
        }

        //不正キル防止処理
        if (target.Data == null
            || target.inVent
            || target.inMovingPlat
            || target.MyPhysics.Animations.IsPlayingEnterVentAnimation()
            || target.MyPhysics.Animations.IsPlayingAnyLadderAnimation()
        )
        {
            Logger.Info("The target is in a state where they cannot be killed, kill canceled.", "CheckMurder");
            return false;
        }
        if (target.Data.IsDead) //同じtargetへの同時キルをブロック
        {
            Logger.Info("Target is already dead, kill canceled", "CheckMurder");
            return false;
        }
        if (MeetingHud.Instance != null) //会議中でないかの判定
        {
            Logger.Info("Kill during meeting, canceled", "CheckMurder");
            return false;
        }

        var divice = Options.CurrentGameMode == CustomGameMode.SoloKombat || Options.CurrentGameMode == CustomGameMode.FFA ? 3000f : 2000f;
        float minTime = Mathf.Max(0.02f, AmongUsClient.Instance.Ping / divice * 6f); //※AmongUsClient.Instance.Pingの値はミリ秒(ms)なので÷1000
        //TimeSinceLastKillに値が保存されていない || 保存されている時間がminTime以上 => キルを許可
        //↓許可されない場合
        if (TimeSinceLastKill.TryGetValue(killer.PlayerId, out var time) && time < minTime)
        {
            Logger.Info("Last kill was too shortly before", "CheckMurder");
            return false;
        }
        TimeSinceLastKill[killer.PlayerId] = 0f;
        if (target.Is(CustomRoles.Diseased))
        {
            if (Main.KilledDiseased.ContainsKey(killer.PlayerId))
            {
                // Key already exists, update the value
                Main.KilledDiseased[killer.PlayerId] += 1;
            }
            else
            {
                // Key doesn't exist, add the key-value pair
                Main.KilledDiseased.Add(killer.PlayerId, 1);
            }
        }
        if (target.Is(CustomRoles.Antidote))
        {
            if (Main.KilledAntidote.ContainsKey(killer.PlayerId))
            {
                // Key already exists, update the value
                Main.KilledAntidote[killer.PlayerId] += 1;// Main.AllPlayerKillCooldown.TryGetValue(killer.PlayerId, out float kcd) ? (kcd - Options.AntidoteCDOpt.GetFloat() > 0 ? kcd - Options.AntidoteCDOpt.GetFloat() : 0f) : 0f;
            }
            else
            {
                // Key doesn't exist, add the key-value pair
                Main.KilledAntidote.Add(killer.PlayerId, 1);// Main.AllPlayerKillCooldown.TryGetValue(killer.PlayerId, out float kcd) ? (kcd - Options.AntidoteCDOpt.GetFloat() > 0 ? kcd - Options.AntidoteCDOpt.GetFloat() : 0f) : 0f);
            }

        }

        killer.ResetKillCooldown();

        //キル可能判定
        if (killer.PlayerId != target.PlayerId && !killer.CanUseKillButton())
        {
            Logger.Info(killer.GetNameWithRole().RemoveHtmlTags() + "cannot use their kill button, the kill was blocked", "CheckMurder");
            return false;
        }

        if (Options.CurrentGameMode == CustomGameMode.SoloKombat)
        {
            SoloKombatManager.OnPlayerAttack(killer, target);
            return false;
        }

        if (Options.CurrentGameMode == CustomGameMode.FFA)
        {
            FFAManager.OnPlayerAttack(killer, target);
            return true;
        }

        if (Mastermind.ManipulatedPlayers.ContainsKey(killer.PlayerId))
        {
            return Mastermind.ForceKillForManipulatedPlayer(killer, target);
        }

        if (target.Is(CustomRoles.Spy)) Spy.OnKillAttempt(killer, target);

        //実際のキラーとkillerが違う場合の入れ替え処理
        if (Sniper.IsEnable) Sniper.TryGetSniper(target.PlayerId, ref killer);
        if (killer != __instance) Logger.Info($"Real Killer={killer.GetNameWithRole().RemoveHtmlTags()}", "CheckMurder");

        //鹈鹕肚子里的人无法击杀
        if (Pelican.IsEaten(target.PlayerId))
            return false;

        //阻止对活死人的操作

        // 赝品检查
        //if (Counterfeiter.OnClientMurder(killer)) return false;
        if (Glitch.hackedIdList.ContainsKey(killer.PlayerId))
        {
            killer.Notify(string.Format(GetString("HackedByGlitch"), "Kill"));
            return false;
        }

        // Check for protection
        switch (target.GetCustomRole())
        {
            case CustomRoles.WeaponMaster when WeaponMaster.OnAttack(killer, target):
            case CustomRoles.Gambler when Gambler.isShielded.ContainsKey(target.PlayerId):
            case CustomRoles.Alchemist when Alchemist.IsProtected:
                killer.SetKillCooldown(time: 5f);
                return false;
            case CustomRoles.Vengeance when !Vengeance.OnKillAttempt(killer, target):
                return false;
            case CustomRoles.Ricochet when !Ricochet.OnKillAttempt(killer, target):
                return false;
            case CustomRoles.Addict when Addict.IsImmortal(target):
                return false;
        }

        if (Pursuer.IsEnable && Pursuer.OnClientMurder(killer)) return false;
        if (Aid.ShieldedPlayers.ContainsKey(target.PlayerId)) return false;

        //判定凶手技能
        if (killer.PlayerId != target.PlayerId)
        {
            //非自杀场景下才会触发
            switch (killer.GetCustomRole())
            {
                //==========内鬼阵营==========//
                case CustomRoles.BountyHunter: //必须在击杀发生前处理
                    BountyHunter.OnCheckMurder(killer, target);
                    break;
                case CustomRoles.Reckless:
                    Reckless.OnCheckMurder(killer);
                    break;
                case CustomRoles.Magician:
                    Magician.OnCheckMurder(killer);
                    break;
                case CustomRoles.WeaponMaster:
                    if (!WeaponMaster.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.Postman:
                    Postman.OnCheckMurder(killer, target);
                    break;
                case CustomRoles.Vengeance:
                    if (!Vengeance.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.Stealth:
                    Stealth.OnCheckMurder(killer, target);
                    break;
                case CustomRoles.Inhibitor:
                    if (Main.AllPlayerKillCooldown[killer.PlayerId] != Options.InhibitorCD.GetFloat())
                    {
                        Main.AllPlayerKillCooldown[killer.PlayerId] = Options.InhibitorCD.GetFloat();
                        killer.SyncSettings();
                    }
                    break;
                case CustomRoles.Nullifier:
                    if (Nullifier.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.Chronomancer:
                    Chronomancer.OnCheckMurder(killer, target);
                    break;
                case CustomRoles.Penguin:
                    if (!Penguin.OnCheckMurderAsKiller(killer, target)) return false;
                    break;
                case CustomRoles.Sapper:
                    return false;
                case CustomRoles.Saboteur:
                    if (Main.AllPlayerKillCooldown[killer.PlayerId] != Options.SaboteurCD.GetFloat())
                    {
                        Main.AllPlayerKillCooldown[killer.PlayerId] = Options.SaboteurCD.GetFloat();
                        killer.SyncSettings();
                    }
                    break;
                case CustomRoles.Gambler:
                    if (!Gambler.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.Mastermind:
                    if (!Mastermind.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.Hitman:
                    if (!Hitman.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.Aid:
                    if (!Aid.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.HeadHunter:
                    HeadHunter.OnCheckMurder(killer, target);
                    break;
                case CustomRoles.SerialKiller:
                    SerialKiller.OnCheckMurder(killer);
                    break;
                case CustomRoles.Glitch:
                    if (!Glitch.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.Eraser:
                    if (!Eraser.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.Bandit:
                    if (!Bandit.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.Vampire:
                    if (!Vampire.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.Pyromaniac:
                    if (!Pyromaniac.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.Agitater:
                    if (!Agitater.OnCheckMurder(killer, target))
                        return false;
                    break;
                case CustomRoles.Eclipse:
                    Eclipse.OnCheckMurder(killer);
                    break;
                case CustomRoles.Jailor:
                    if (!Jailor.OnCheckMurder(killer, target))
                        return false;
                    break;
                case CustomRoles.Poisoner:
                    if (!Poisoner.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.Warlock:
                    if (!Main.isCurseAndKill.ContainsKey(killer.PlayerId)) Main.isCurseAndKill[killer.PlayerId] = false;
                    if (!Main.CheckShapeshift[killer.PlayerId] && !Main.isCurseAndKill[killer.PlayerId])
                    { //Warlockが変身時以外にキルしたら、呪われる処理
                        if (target.Is(CustomRoles.Needy) || target.Is(CustomRoles.Lazy)) return false;
                        Main.isCursed = true;
                        killer.SetKillCooldown();
                        RPC.PlaySoundRPC(killer.PlayerId, Sounds.KillSound);
                        killer.RPCPlayCustomSound("Line");
                        Main.CursedPlayers[killer.PlayerId] = target;
                        Main.WarlockTimer.Add(killer.PlayerId, 0f);
                        Main.isCurseAndKill[killer.PlayerId] = true;
                        //RPC.RpcSyncCurseAndKill();
                        return false;
                    }
                    if (Main.CheckShapeshift[killer.PlayerId])
                    {//呪われてる人がいないくて変身してるときに通常キルになる
                        killer.RpcCheckAndMurder(target);
                        return false;
                    }
                    if (Main.isCurseAndKill[killer.PlayerId]) killer.RpcGuardAndKill(target);
                    return false;
                case CustomRoles.Witness:
                    killer.SetKillCooldown();
                    if (Main.AllKillers.ContainsKey(target.PlayerId))
                        killer.Notify(GetString("WitnessFoundKiller"));
                    else killer.Notify(GetString("WitnessFoundInnocent"));
                    return false;
                case CustomRoles.Assassin:
                    if (!Assassin.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.Undertaker:
                    if (!Undertaker.OnCheckMurder(killer, target)) return false;
                    break;
                //case CustomRoles.Famine:
                //    Baker.FamineKilledTasks(target.PlayerId);
                //    break;

                case CustomRoles.Witch:
                    if (!Witch.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.HexMaster:
                    if (!HexMaster.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.PlagueDoctor:
                    if (!PlagueDoctor.OnPDinfect(killer, target)) return false;
                    break;
                case CustomRoles.Puppeteer:
                    if (target.Is(CustomRoles.Needy) || target.Is(CustomRoles.Lazy) || Medic.ProtectList.Contains(target.PlayerId)) return false;
                    if (killer.CheckDoubleTrigger(target, () =>
                    {
                        Main.PuppeteerList[target.PlayerId] = killer.PlayerId;
                        Main.PuppeteerDelayList[target.PlayerId] = GetTimeStamp();
                        killer.SetKillCooldown(time: Options.PuppeteerCD.GetFloat());
                        killer.RPCPlayCustomSound("Line");
                        NotifyRoles(SpecifySeer: killer);
                    })) return false;
                    break;
                //case CustomRoles.NWitch:
                //    //  if (target.Is(CustomRoles.Needy) || target.Is(CustomRoles.Lazy)) return false;
                //    Main.TaglockedList[target.PlayerId] = killer.PlayerId;
                //    killer.SetKillCooldown();
                //    Utils.NotifyRoles(SpecifySeer: killer);
                //    return false;
                case CustomRoles.Capitalism:
                    return killer.CheckDoubleTrigger(target, () =>
                    {
                        if (!Main.CapitalismAddTask.ContainsKey(target.PlayerId))
                            Main.CapitalismAddTask.Add(target.PlayerId, 0);
                        Main.CapitalismAddTask[target.PlayerId]++;
                        if (!Main.CapitalismAssignTask.ContainsKey(target.PlayerId))
                            Main.CapitalismAssignTask.Add(target.PlayerId, 0);
                        Main.CapitalismAssignTask[target.PlayerId]++;
                        Logger.Info($"资本主义 {killer.GetRealName()} 又开始祸害人了：{target.GetRealName()}", "Capitalism Add Task");
                        //killer.RpcGuardAndKill(killer);
                        killer.SetKillCooldown(Options.CapitalismSkillCooldown.GetFloat());
                    });
                /*     case CustomRoles.Bomber:
                         return false; */
                case CustomRoles.Gangster:
                    if (Gangster.OnCheckMurder(killer, target))
                        return false;
                    break;
                case CustomRoles.BallLightning:
                    if (BallLightning.CheckBallLightningMurder(killer, target))
                        return false;
                    break;
                case CustomRoles.Greedier:
                    Greedier.OnCheckMurder(killer);
                    break;
                case CustomRoles.Imitator:
                    Imitator.OnCheckMurder(killer);
                    break;
                case CustomRoles.QuickShooter:
                    QuickShooter.QuickShooterKill(killer);
                    break;
                case CustomRoles.Sans:
                    Sans.OnCheckMurder(killer);
                    break;
                case CustomRoles.Juggernaut:
                    Juggernaut.OnCheckMurder(killer);
                    break;
                //case CustomRoles.Reverie:
                //    Reverie.OnCheckMurder(killer);
                //    break;
                case CustomRoles.Hangman:
                    if (!Hangman.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.Swooper:
                    if (!Swooper.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.Wraith:
                    if (!Wraith.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.Werewolf:
                    Werewolf.OnCheckMurder(killer, target);
                    break;
                case CustomRoles.Lurker:
                    Lurker.OnCheckMurder(killer);
                    break;
                case CustomRoles.Crusader:
                    Crusader.OnCheckMurder(killer, target);
                    return false;

                //==========中立阵营==========//
                case CustomRoles.PlagueBearer:
                    if (!PlagueBearer.OnCheckMurder(killer, target))
                        return false;
                    break;
                //case CustomRoles.Pirate:
                //    if (!Pirate.OnCheckMurder(killer, target))
                //        return false;
                //    break;

                case CustomRoles.Arsonist:
                    killer.SetKillCooldown(Options.ArsonistDouseTime.GetFloat());
                    if (!Main.isDoused[(killer.PlayerId, target.PlayerId)] && !Main.ArsonistTimer.ContainsKey(killer.PlayerId))
                    {
                        Main.ArsonistTimer.Add(killer.PlayerId, (target, 0f));
                        NotifyRoles(SpecifySeer: __instance);
                        RPC.SetCurrentDousingTarget(killer.PlayerId, target.PlayerId);
                    }
                    return false;
                case CustomRoles.Revolutionist:
                    killer.SetKillCooldown(Options.RevolutionistDrawTime.GetFloat());
                    if (!Main.isDraw[(killer.PlayerId, target.PlayerId)] && !Main.RevolutionistTimer.ContainsKey(killer.PlayerId))
                    {
                        Main.RevolutionistTimer.TryAdd(killer.PlayerId, (target, 0f));
                        NotifyRoles(SpecifySeer: __instance);
                        RPC.SetCurrentDrawTarget(killer.PlayerId, target.PlayerId);
                    }
                    return false;
                case CustomRoles.Farseer:
                    killer.SetKillCooldown(Farseer.FarseerRevealTime.GetFloat());
                    if (!Main.isRevealed[(killer.PlayerId, target.PlayerId)] && !Main.FarseerTimer.ContainsKey(killer.PlayerId))
                    {
                        Main.FarseerTimer.TryAdd(killer.PlayerId, (target, 0f));
                        NotifyRoles(SpecifySeer: __instance);
                        RPC.SetCurrentRevealTarget(killer.PlayerId, target.PlayerId);
                    }
                    return false;
                case CustomRoles.Innocent:
                    target.Kill(killer);
                    return false;
                case CustomRoles.Pelican:
                    if (Pelican.CanEat(killer, target.PlayerId))
                    {
                        Pelican.EatPlayer(killer, target);
                        //killer.RpcGuardAndKill(killer);
                        killer.SetKillCooldown();
                        killer.RPCPlayCustomSound("Eat");
                        target.RPCPlayCustomSound("Eat");
                    }
                    return false;
                case CustomRoles.FFF:
                    if (!FFF.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.Gamer:
                    Gamer.CheckGamerMurder(killer, target);
                    return false;
                case CustomRoles.DarkHide:
                    DarkHide.OnCheckMurder(killer, target);
                    break;
                case CustomRoles.Provocateur:
                    Main.PlayerStates[target.PlayerId].deathReason = PlayerState.DeathReason.PissedOff;
                    killer.Kill(target);
                    //killer.Kill(killer);
                    killer.SetRealKiller(target);
                    Main.Provoked.TryAdd(killer.PlayerId, target.PlayerId);
                    return false;
                case CustomRoles.Totocalcio:
                    Totocalcio.OnCheckMurder(killer, target);
                    return false;
                case CustomRoles.Romantic:
                    if (!Romantic.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.VengefulRomantic:
                    if (!VengefulRomantic.OnCheckMurder(killer, target)) return false;
                    break;
                case CustomRoles.Succubus:
                    Succubus.OnCheckMurder(killer, target);
                    return false;
                case CustomRoles.CursedSoul:
                    CursedSoul.OnCheckMurder(killer, target);
                    return false;
                case CustomRoles.Admirer:
                    Admirer.OnCheckMurder(killer, target);
                    return false;
                case CustomRoles.Amnesiac:
                    Amnesiac.OnCheckMurder(killer, target);
                    return false;
                case CustomRoles.Infectious:
                    Infectious.OnCheckMurder(killer, target);
                    return false;
                case CustomRoles.Monarch:
                    Monarch.OnCheckMurder(killer, target);
                    return false;
                case CustomRoles.Deputy:
                    Deputy.OnCheckMurder(killer, target);
                    return false;
                case CustomRoles.Jackal:
                    if (Jackal.OnCheckMurder(killer, target))
                        return false;
                    break;

                //==========船员职业==========//
                case CustomRoles.Sheriff:
                    if (!Sheriff.OnCheckMurder(killer, target))
                        return false;
                    break;
                case CustomRoles.CopyCat:
                    if (!CopyCat.OnCheckMurder(killer, target))
                        return false;
                    break;

                case CustomRoles.SwordsMan:
                    if (!SwordsMan.OnCheckMurder(killer))
                        return false;
                    break;
                case CustomRoles.Medic:
                    Medic.OnCheckMurderFormedicaler(killer, target);
                    return false;
                //case CustomRoles.Counterfeiter:
                //    if (Counterfeiter.CanBeClient(target) && Counterfeiter.CanSeel(killer.PlayerId))
                //        Counterfeiter.SeelToClient(killer, target);
                //    return false;
                case CustomRoles.Pursuer:
                    if (Pursuer.CanBeClient(target) && Pursuer.CanSeel(killer.PlayerId))
                        Pursuer.SeelToClient(killer, target);
                    return false;
            }
        }

        // 击杀前检查
        if (!killer.RpcCheckAndMurder(target, true))
            return false;
        if (Merchant.OnClientMurder(killer, target)) return false;


        if (killer.Is(CustomRoles.Virus)) Virus.OnCheckMurder(killer, target);
        else if (killer.Is(CustomRoles.Spiritcaller)) Spiritcaller.OnCheckMurder(target);

        // Consigliere
        if (killer.Is(CustomRoles.EvilDiviner))
        {

            if (!EvilDiviner.OnCheckMurder(killer, target))
                return false;
        }

        if (killer.Is(CustomRoles.Unlucky))
        {
            var Ue = IRandom.Instance;
            if (Ue.Next(0, 100) < Options.UnluckyKillSuicideChance.GetInt())
            {
                killer.Kill(killer);
                Main.PlayerStates[killer.PlayerId].deathReason = PlayerState.DeathReason.Suicide;
                return false;
            }
        }

        if (killer.Is(CustomRoles.Swift) && !target.Is(CustomRoles.Pestilence))
        {
            target.Kill(target);
            //killer.RpcGuardAndKill(killer);
            //    target.RpcGuardAndKill(target);
            target.SetRealKiller(killer);
            killer.SetKillCooldown();
            RPC.PlaySoundRPC(killer.PlayerId, Sounds.KillSound);
            return false;
        }
        if (killer.Is(CustomRoles.Mare))
        {
            killer.ResetKillCooldown();
            return true;
        }
        /*     if (killer.Is(CustomRoles.Minimalism))
             {
                 return true;
             } */

        if (killer.Is(CustomRoles.Ritualist))
        {

            if (!Ritualist.OnCheckMurder(killer, target))
                return false;
        }

        // 清道夫清理尸体
        if (killer.Is(CustomRoles.Scavenger))
        {
            if (!target.Is(CustomRoles.Pestilence))
            {
                TP(target.NetTransform, Pelican.GetBlackRoomPS());
                target.SetRealKiller(killer);
                Main.PlayerStates[target.PlayerId].SetDead();
                target.Kill(target);
                killer.SetKillCooldown();
                RPC.PlaySoundRPC(killer.PlayerId, Sounds.KillSound);
                NameNotifyManager.Notify(target, ColorString(GetRoleColor(CustomRoles.Scavenger), GetString("KilledByScavenger")));
                return false;
            }
            if (target.Is(CustomRoles.Pestilence))
            {
                target.Kill(target);
                target.SetRealKiller(killer);
                return false;
            }

        }
        // 肢解者肢解受害者
        if (killer.Is(CustomRoles.OverKiller) && killer.PlayerId != target.PlayerId)
        {
            Main.PlayerStates[target.PlayerId].deathReason = PlayerState.DeathReason.Dismembered;
            _ = new LateTask(() =>
            {
                if (!Main.OverDeadPlayerList.Contains(target.PlayerId)) Main.OverDeadPlayerList.Add(target.PlayerId);
                var ops = target.GetTruePosition();
                var rd = IRandom.Instance;
                for (int i = 0; i < 20; i++)
                {
                    Vector2 location = new(ops.x + ((float)(rd.Next(0, 201) - 100) / 100), ops.y + ((float)(rd.Next(0, 201) - 100) / 100));
                    location += new Vector2(0, 0.3636f);

                    MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(target.NetTransform.NetId, (byte)RpcCalls.SnapTo, SendOption.None, -1);
                    NetHelpers.WriteVector2(location, writer);
                    writer.Write(target.NetTransform.lastSequenceId);
                    AmongUsClient.Instance.FinishRpcImmediately(writer);

                    target.NetTransform.SnapTo(location);
                    killer.MurderPlayer(target, ExtendedPlayerControl.ResultFlags);

                    if (target.Is(CustomRoles.Avanger))
                    {
                        var pcList = Main.AllAlivePlayerControls.Where(x => x.PlayerId != target.PlayerId || Pelican.IsEaten(x.PlayerId) || Medic.ProtectList.Contains(x.PlayerId) || target.Is(CustomRoles.Pestilence)).ToArray();
                        var rp = pcList[IRandom.Instance.Next(0, pcList.Length)];
                        Main.PlayerStates[rp.PlayerId].deathReason = PlayerState.DeathReason.Revenge;
                        rp.SetRealKiller(target);
                        rp.Kill(rp);
                    }

                    MessageWriter messageWriter = AmongUsClient.Instance.StartRpcImmediately(killer.NetId, (byte)RpcCalls.MurderPlayer, SendOption.None, -1);
                    messageWriter.WriteNetObject(target);
                    messageWriter.Write((byte)ExtendedPlayerControl.ResultFlags);
                    AmongUsClient.Instance.FinishRpcImmediately(messageWriter);
                }
                TP(killer.NetTransform, ops);
            }, 0.05f, "OverKiller Murder");
        }

        //==Kill processing==
        __instance.Kill(target);
        //============

        return false;
    }

    public static bool RpcCheckAndMurder(PlayerControl killer, PlayerControl target, bool check = false)
    {
        if (!AmongUsClient.Instance.AmHost) return false;
        if (target == null) target = killer;

        //Jackal can kill Sidekick
        if (killer.Is(CustomRoles.Jackal) && target.Is(CustomRoles.Sidekick) && !Options.JackalCanKillSidekick.GetBool())
            return false;
        //Sidekick can kill Jackal
        if (killer.Is(CustomRoles.Sidekick) && target.Is(CustomRoles.Jackal) && !Options.SidekickCanKillJackal.GetBool())
            return false;
        if (killer.Is(CustomRoles.Jackal) && target.Is(CustomRoles.Recruit) && !Options.JackalCanKillSidekick.GetBool())
            return false;
        //Sidekick can kill Jackal
        if (killer.Is(CustomRoles.Recruit) && target.Is(CustomRoles.Jackal) && !Options.SidekickCanKillJackal.GetBool())
            return false;
        //禁止内鬼刀叛徒
        if (killer.Is(CustomRoleTypes.Impostor) && target.Is(CustomRoles.Madmate) && !Options.ImpCanKillMadmate.GetBool())
            return false;

        //if ((Romantic.BetPlayer.TryGetValue(target.PlayerId, out var RomanticPartner) && target.PlayerId == RomanticPartner && Romantic.isPartnerProtected))
        //    return false;

        // Romantic partner is protected
        if (Romantic.BetPlayer.ContainsValue(target.PlayerId) && Romantic.isPartnerProtected) return false;

        if (Options.OppoImmuneToAttacksWhenTasksDone.GetBool())
        {
            if (target.Is(CustomRoles.Opportunist) && target.AllTasksCompleted())
                return false;
        }

        if (Medic.OnCheckMurder(killer, target))
            return false;



        // Traitor can't kill Impostors but Impostors can kill it
        if (killer.Is(CustomRoles.Traitor) && target.Is(CustomRoleTypes.Impostor))
            return false;


        //禁止叛徒刀内鬼
        if (killer.Is(CustomRoles.Madmate) && target.Is(CustomRoleTypes.Impostor) && !Options.MadmateCanKillImp.GetBool())
            return false;
        //Bitten players cannot kill Vampire
        if (killer.Is(CustomRoles.Infected) && target.Is(CustomRoles.Infectious))
            return false;
        //Vampire cannot kill bitten players
        if (killer.Is(CustomRoles.Infectious) && target.Is(CustomRoles.Infected))
            return false;
        //Bitten players cannot kill each other
        if (killer.Is(CustomRoles.Infected) && target.Is(CustomRoles.Infected) && !Infectious.TargetKnowOtherTarget.GetBool())
            return false;
        //Sidekick can kill Sidekick
        if (killer.Is(CustomRoles.Sidekick) && target.Is(CustomRoles.Sidekick) && !Options.SidekickCanKillSidekick.GetBool())
            return false;
        //Recruit can kill Recruit
        if (killer.Is(CustomRoles.Recruit) && target.Is(CustomRoles.Recruit) && !Options.SidekickCanKillSidekick.GetBool())
            return false;
        //Sidekick can kill Sidekick
        if (killer.Is(CustomRoles.Recruit) && target.Is(CustomRoles.Sidekick) && !Options.SidekickCanKillSidekick.GetBool())
            return false;
        //Recruit can kill Recruit
        if (killer.Is(CustomRoles.Sidekick) && target.Is(CustomRoles.Recruit) && !Options.SidekickCanKillSidekick.GetBool())
            return false;

        if (PlagueBearer.OnCheckMurderPestilence(killer, target))
            return false;

        if (Jackal.ResetKillCooldownWhenSbGetKilled.GetBool() && !killer.Is(CustomRoles.Sidekick) && !target.Is(CustomRoles.Sidekick) && !killer.Is(CustomRoles.Jackal) && !target.Is(CustomRoles.Jackal) && !GameStates.IsMeeting)
            Jackal.AfterPlayerDiedTask(killer);


        if (target.Is(CustomRoles.BoobyTrap) && Options.TrapOnlyWorksOnTheBodyBoobyTrap.GetBool() && !GameStates.IsMeeting)
        {
            Main.BoobyTrapBody.Add(target.PlayerId);
            Main.BoobyTrapKiller.Add(target.PlayerId);
        }

        if (target.Is(CustomRoles.Lucky))
        {
            var rd = IRandom.Instance;
            if (rd.Next(0, 100) < Options.LuckyProbability.GetInt())
            {
                killer.SetKillCooldown();
                return false;
            }
        }
        //  if (target.Is(CustomRoles.Diseased))
        //  {

        ////      killer.RpcGuardAndKill(killer);
        //   //   killer.SetKillCooldownV3(Main.AllPlayerKillCooldown[killer.PlayerId] *= Options.DiseasedMultiplier.GetFloat());
        //   //   killer.ResetKillCooldown();
        //  //    killer.SyncSettings();
        //  }
        if (Main.ForCrusade.Contains(target.PlayerId))
        {
            foreach (PlayerControl player in Main.AllPlayerControls)
            {
                if (player.Is(CustomRoles.Crusader) && player.IsAlive() && !killer.Is(CustomRoles.Pestilence) && !killer.Is(CustomRoles.Minimalism))
                {
                    player.Kill(killer);
                    Main.ForCrusade.Remove(target.PlayerId);
                    killer.RpcGuardAndKill(target);
                    return false;
                }
                if (player.Is(CustomRoles.Crusader) && player.IsAlive() && killer.Is(CustomRoles.Pestilence))
                {
                    killer.Kill(player);
                    Main.ForCrusade.Remove(target.PlayerId);
                    target.RpcGuardAndKill(killer);
                    Main.PlayerStates[player.PlayerId].deathReason = PlayerState.DeathReason.PissedOff;
                    return false;
                }
            }
        }

        switch (target.GetCustomRole())
        {
            case CustomRoles.Medic:
                Medic.IsDead(target); break;
            case CustomRoles.Guardian when target.AllTasksCompleted():
                return false;
            case CustomRoles.Monarch when CustomRoles.Knighted.RoleExist():
                return false;
            case CustomRoles.Luckey:
                var rd = IRandom.Instance;
                if (rd.Next(0, 100) < Options.LuckeyProbability.GetInt())
                {
                    killer.SetKillCooldown(15f);
                    return false;
                }
                break;
            case CustomRoles.CursedWolf:
                if (Main.CursedWolfSpellCount[target.PlayerId] <= 0) break;
                if (killer.Is(CustomRoles.Pestilence)) break;
                if (killer == target) break;
                killer.RpcGuardAndKill(target);
                Main.CursedWolfSpellCount[target.PlayerId] -= 1;
                RPC.SendRPCCursedWolfSpellCount(target.PlayerId);
                if (Options.killAttacker.GetBool())
                {
                    Logger.Info($"{target.GetNameWithRole().RemoveHtmlTags()} : {Main.CursedWolfSpellCount[target.PlayerId]} curses remain", "CursedWolf");
                    Main.PlayerStates[killer.PlayerId].deathReason = PlayerState.DeathReason.Curse;
                    killer.SetRealKiller(target);
                    target.Kill(killer);
                }
                var kcd = target.killTimer + Main.AllPlayerKillCooldown[target.PlayerId];
                target.SetKillCooldown(time: kcd);
                return false;
            case CustomRoles.Jinx:
                if (Main.JinxSpellCount[target.PlayerId] <= 0) break;
                if (killer.Is(CustomRoles.Pestilence)) break;
                if (killer == target) break;
                killer.RpcGuardAndKill(target);
                Main.JinxSpellCount[target.PlayerId] -= 1;
                RPC.SendRPCJinxSpellCount(target.PlayerId);
                if (Jinx.killAttacker.GetBool())
                {
                    Logger.Info($"{target.GetNameWithRole().RemoveHtmlTags()} : {Main.JinxSpellCount[target.PlayerId]} jixes remain", "Jinx");
                    Main.PlayerStates[killer.PlayerId].deathReason = PlayerState.DeathReason.Jinx;
                    killer.SetRealKiller(target);
                    target.Kill(killer);
                }
                var kcd2 = target.killTimer + Main.AllPlayerKillCooldown[target.PlayerId];
                target.SetKillCooldown(time: kcd2);
                return false;
            case CustomRoles.Veteran:
                if (Main.VeteranInProtect.ContainsKey(target.PlayerId)
                    && killer.PlayerId != target.PlayerId
                    && Main.VeteranInProtect[target.PlayerId] + Options.VeteranSkillDuration.GetInt() >= GetTimeStamp())
                {
                    if (!killer.Is(CustomRoles.Pestilence))
                    {
                        killer.SetRealKiller(target);
                        target.Kill(killer);
                        Logger.Info($"{target.GetRealName()} reverse killed：{killer.GetRealName()}", "Veteran Kill");
                        return false;
                    }
                    if (killer.Is(CustomRoles.Pestilence))
                    {
                        target.SetRealKiller(killer);
                        killer.Kill(target);
                        Logger.Info($"{target.GetRealName()} reverse reverse killed：{target.GetRealName()}", "Pestilence Reflect");
                        return false;
                    }
                }
                break;
            case CustomRoles.TimeMaster:
                if (Main.TimeMasterInProtect.ContainsKey(target.PlayerId)
                    && killer.PlayerId != target.PlayerId
                    && Main.TimeMasterInProtect[target.PlayerId] + Options.TimeMasterSkillDuration.GetInt() >= GetTimeStamp(DateTime.UtcNow))
                {
                    foreach (PlayerControl player in Main.AllPlayerControls)
                    {
                        if (!killer.Is(CustomRoles.Pestilence) && Main.TimeMasterBackTrack.ContainsKey(player.PlayerId))
                        {
                            var position = Main.TimeMasterBackTrack[player.PlayerId];
                            TP(player.NetTransform, position);
                        }
                    }
                    killer.SetKillCooldown(target: target, forceAnime: true);
                    return false;
                }

                break;
            case CustomRoles.SuperStar:
                if (Main.AllAlivePlayerControls.Any(x =>
                    x.PlayerId != killer.PlayerId &&
                    x.PlayerId != target.PlayerId &&
                    Vector2.Distance(x.GetTruePosition(), target.GetTruePosition()) < 2f)) return false;
                break;
            case CustomRoles.Gamer:
                if (!Gamer.CheckMurder(killer, target))
                    return false;
                break;
            case CustomRoles.BloodKnight:
                if (BloodKnight.InProtect(target.PlayerId))
                {
                    killer.RpcGuardAndKill(target);
                    target.Notify(GetString("BKOffsetKill"));
                    return false;
                }
                break;
            case CustomRoles.Wildling:
                if (Wildling.InProtect(target.PlayerId))
                {
                    killer.RpcGuardAndKill(target);
                    target.Notify(GetString("BKOffsetKill"));
                    return false;
                }
                break;
            case CustomRoles.Spiritcaller:
                if (Spiritcaller.InProtect(target))
                {
                    killer.RpcGuardAndKill(target);
                    return false;
                }
                break;
        }

        if (killer.PlayerId != target.PlayerId)
        {
            foreach (var pc in Main.AllAlivePlayerControls.Where(x => x.PlayerId != target.PlayerId).ToArray())
            {
                var pos = target.transform.position;
                var dis = Vector2.Distance(pos, pc.transform.position);
                if (dis > Options.BodyguardProtectRadius.GetFloat()) continue;
                if (pc.Is(CustomRoles.Bodyguard))
                {
                    if (pc.Is(CustomRoles.Madmate) && killer.GetCustomRole().IsImpostorTeam())
                        Logger.Info($"{pc.GetRealName()} is a madmate, so they chose to ignore the murder scene", "Bodyguard");
                    else
                    {
                        Main.PlayerStates[pc.PlayerId].deathReason = PlayerState.DeathReason.Sacrifice;
                        if (Options.BodyguardKillsKiller.GetBool()) pc.Kill(killer);
                        else killer.SetKillCooldown();
                        pc.SetRealKiller(killer);
                        pc.Kill(pc);
                        Logger.Info($"{pc.GetRealName()} stood up and died for {killer.GetRealName()}", "Bodyguard");
                        return false;
                    }
                }
            }
        }

        if (Main.ShieldPlayer != byte.MaxValue && Main.ShieldPlayer == target.PlayerId && IsAllAlive)
        {
            Main.ShieldPlayer = byte.MaxValue;
            killer.SetKillCooldown(15f);
            killer.Notify(GetString("TriedToKillLastGameFirstKill"), 10f);
            return false;
        }

        if (Options.MadmateSpawnMode.GetInt() == 1 && Main.MadmateNum < CustomRoles.Madmate.GetCount() && Utils.CanBeMadmate(target))
        {
            Main.MadmateNum++;
            target.RpcSetCustomRole(CustomRoles.Madmate);
            ExtendedPlayerControl.RpcSetCustomRole(target.PlayerId, CustomRoles.Madmate);
            target.Notify(ColorString(GetRoleColor(CustomRoles.Madmate), GetString("BecomeMadmateCuzMadmateMode")));
            killer.SetKillCooldown();
            killer.RpcGuardAndKill(target);
            target.RpcGuardAndKill(killer);
            target.RpcGuardAndKill(target);
            Logger.Info("Add-on assigned:" + target?.Data?.PlayerName + " = " + target.GetCustomRole().ToString() + " + " + CustomRoles.Madmate.ToString(), "Assign " + CustomRoles.Madmate.ToString());
            return false;
        }

        if (!check) killer.Kill(target);
        return true;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.MurderPlayer))]
class MurderPlayerPatch
{
    public static void Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        Logger.Info($"{__instance.GetNameWithRole().RemoveHtmlTags()} => {target.GetNameWithRole().RemoveHtmlTags()}{(target.IsProtected() ? "(Protected)" : string.Empty)}", "MurderPlayer");

        if (RandomSpawn.CustomNetworkTransformPatch.NumOfTP.TryGetValue(__instance.PlayerId, out var num) && num > 2) RandomSpawn.CustomNetworkTransformPatch.NumOfTP[__instance.PlayerId] = 3;
        if (!target.IsProtected())
            Camouflage.RpcSetSkin(target, ForceRevert: true);
    }
    public static void Postfix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        if (target.AmOwner) RemoveDisableDevicesPatch.UpdateDisableDevices();
        if (!target.Data.IsDead || !AmongUsClient.Instance.AmHost) return;

        if (Main.OverDeadPlayerList.Contains(target.PlayerId)) return;

        PlayerControl killer = __instance; // Alternative variable
        if (target.PlayerId == Main.GodfatherTarget) killer.RpcSetCustomRole(CustomRoles.Refugee);

        PlagueDoctor.OnAnyMurder();

        // Replacement process when the actual killer and killer are different
        if (Sniper.IsEnable)
        {
            if (Sniper.TryGetSniper(target.PlayerId, ref killer))
            {
                Main.PlayerStates[target.PlayerId].deathReason = PlayerState.DeathReason.Sniped;
            }
        }

        if (killer.Is(CustomRoles.Sniper))
            if (!Options.UsePets.GetBool()) killer.RpcResetAbilityCooldown();
            else Main.SniperCD.TryAdd(killer.PlayerId, GetTimeStamp());

        if (killer != __instance)
        {
            Logger.Info($"Real Killer = {killer.GetNameWithRole().RemoveHtmlTags()}", "MurderPlayer");

        }
        if (Main.PlayerStates[target.PlayerId].deathReason == PlayerState.DeathReason.etc)
        {
            //If the cause of death is not specified, it is determined as a normal kill.
            Main.PlayerStates[target.PlayerId].deathReason = PlayerState.DeathReason.Kill;
        }

        //Let’s see if Youtuber was stabbed first
        if (Main.FirstDied == byte.MaxValue && target.Is(CustomRoles.Youtuber))
        {
            CustomSoundsManager.RPCPlayCustomSoundAll("Congrats");
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Youtuber);
            CustomWinnerHolder.WinnerIds.Add(target.PlayerId);
        }

        //Record the first blow
        if (Main.FirstDied == byte.MaxValue)
            Main.FirstDied = target.PlayerId;

        if (Postman.Target == target.PlayerId)
        {
            Postman.OnTargetDeath();
        }

        if (target.Is(CustomRoles.Trapper) && killer != target)
            killer.TrapperKilled(target);

        //if ((Romantic.BetPlayer.TryGetValue(target.PlayerId, out var RomanticPartner) && target.PlayerId == RomanticPartner) && target.PlayerId != killer.PlayerId)
        //    VengefulRomantic.PartnerKiller.Add(killer.PlayerId, 1);

        Main.AllKillers.Remove(killer.PlayerId);
        Main.AllKillers.Add(killer.PlayerId, GetTimeStamp());

        switch (target.GetCustomRole())
        {
            case CustomRoles.BallLightning:
                if (killer != target)
                    BallLightning.MurderPlayer(killer, target);
                break;
        }
        switch (killer.GetCustomRole())
        {
            case CustomRoles.BoobyTrap:
                if (!Options.TrapOnlyWorksOnTheBodyBoobyTrap.GetBool() && killer != target)
                {
                    if (!Main.BoobyTrapBody.Contains(target.PlayerId)) Main.BoobyTrapBody.Add(target.PlayerId);
                    if (!Main.KillerOfBoobyTrapBody.ContainsKey(target.PlayerId)) Main.KillerOfBoobyTrapBody.Add(target.PlayerId, killer.PlayerId);
                    Main.PlayerStates[killer.PlayerId].deathReason = PlayerState.DeathReason.Misfire;
                    killer.Kill(killer);
                }
                break;
            case CustomRoles.SwordsMan:
                if (killer != target)
                    SwordsMan.OnMurder(killer);
                break;
            case CustomRoles.BloodKnight:
                BloodKnight.OnMurderPlayer(killer, target);
                break;
            case CustomRoles.Wildling:
                Wildling.OnMurderPlayer(killer, target);
                break;
            case CustomRoles.Underdog:
                int playerCount = Main.AllAlivePlayerControls.Length;
                if (playerCount < Options.UnderdogMaximumPlayersNeededToKill.GetInt())
                    Main.AllPlayerKillCooldown[killer.PlayerId] = Options.UnderdogKillCooldown.GetFloat();
                else Main.AllPlayerKillCooldown[killer.PlayerId] = Options.UnderdogKillCooldownWithMorePlayersAlive.GetFloat();
                break;
            case CustomRoles.Hacker:
                Hacker.HackLimit[killer.PlayerId] += Hacker.HackerAbilityUseGainWithEachKill.GetFloat();
                break;
            case CustomRoles.Camouflager:
                Camouflager.CamoLimit[killer.PlayerId] += Camouflager.CamoAbilityUseGainWithEachKill.GetFloat();
                break;
            case CustomRoles.Councillor:
                Councillor.MurderLimit[killer.PlayerId] += Councillor.CouncillorAbilityUseGainWithEachKill.GetFloat();
                break;
            case CustomRoles.Dazzler:
                Dazzler.DazzleLimit[killer.PlayerId] += Dazzler.DazzlerAbilityUseGainWithEachKill.GetFloat();
                break;
            case CustomRoles.Disperser:
                Disperser.DisperserLimit[killer.PlayerId] += Disperser.DisperserAbilityUseGainWithEachKill.GetFloat();
                break;
            case CustomRoles.EvilDiviner:
                EvilDiviner.DivinationCount[killer.PlayerId] += EvilDiviner.EDAbilityUseGainWithEachKill.GetFloat();
                break;
            case CustomRoles.Swooper:
                Swooper.SwoopLimit[killer.PlayerId] += Swooper.SwooperAbilityUseGainWithEachKill.GetFloat();
                break;
            case CustomRoles.Hangman:
                Hangman.HangLimit[killer.PlayerId] += Hangman.HangmanAbilityUseGainWithEachKill.GetFloat();
                break;
            case CustomRoles.Twister:
                Twister.TwistLimit[killer.PlayerId] += Twister.TwisterAbilityUseGainWithEachKill.GetFloat();
                break;
        }

        if (killer.Is(CustomRoles.Damocles)) Damocles.OnMurder();
        else if (killer.GetCustomRole().IsImpostorTeamV3()) Damocles.OnOtherImpostorMurder();
        if (target.GetCustomRole().IsImpostorTeamV3()) Damocles.OnImpostorDeath();

        if (killer.Is(CustomRoles.TicketsStealer) && killer.PlayerId != target.PlayerId)
            killer.Notify(string.Format(GetString("TicketsStealerGetTicket"), ((Main.AllPlayerControls.Count(x => x.GetRealKiller()?.PlayerId == killer.PlayerId) + 1) * Options.TicketsPerKill.GetFloat()).ToString("0.0#####")));

        if (killer.Is(CustomRoles.Pickpocket) && killer.PlayerId != target.PlayerId)
            killer.Notify(string.Format(GetString("PickpocketGetVote"), ((Main.AllPlayerControls.Count(x => x.GetRealKiller()?.PlayerId == killer.PlayerId) + 1) * Pickpocket.VotesPerKill.GetFloat()).ToString("0.0#####")));

        if (target.Is(CustomRoles.Avanger))
        {
            var pcList = Main.AllAlivePlayerControls.Where(x => x.PlayerId != target.PlayerId).ToArray();
            var rp = pcList[IRandom.Instance.Next(0, pcList.Length)];
            if (!rp.Is(CustomRoles.Pestilence))
            {
                Main.PlayerStates[rp.PlayerId].deathReason = PlayerState.DeathReason.Revenge;
                rp.SetRealKiller(target);
                rp.Kill(rp);
            }
        }

        if (target.Is(CustomRoles.Bait))
        {
            if (killer.PlayerId != target.PlayerId || (target.GetRealKiller()?.GetCustomRole() is CustomRoles.Swooper or CustomRoles.Wraith) || !killer.Is(CustomRoles.Oblivious) || (killer.Is(CustomRoles.Oblivious) && !Options.ObliviousBaitImmune.GetBool()))
            {
                killer.RPCPlayCustomSound("Congrats");
                target.RPCPlayCustomSound("Congrats");
                float delay;
                if (Options.BaitDelayMax.GetFloat() < Options.BaitDelayMin.GetFloat()) delay = 0f;
                else delay = IRandom.Instance.Next((int)Options.BaitDelayMin.GetFloat(), (int)Options.BaitDelayMax.GetFloat() + 1);
                delay = Math.Max(delay, 0.15f);
                if (delay > 0.15f && Options.BaitDelayNotify.GetBool()) killer.Notify(ColorString(GetRoleColor(CustomRoles.Bait), string.Format(GetString("KillBaitNotify"), (int)delay)), delay);
                Logger.Info($"{killer.GetNameWithRole().RemoveHtmlTags()} 击杀诱饵 => {target.GetNameWithRole().RemoveHtmlTags()}", "MurderPlayer");
                _ = new LateTask(() => { if (GameStates.IsInTask) killer.CmdReportDeadBody(target.Data); }, delay, "Bait Self Report");
            }
        }

        if (Mediumshiper.IsEnable) foreach (var pc in Main.AllAlivePlayerControls.Where(x => x.Is(CustomRoles.Mediumshiper)).ToArray())
                pc.Notify(ColorString(GetRoleColor(CustomRoles.Mediumshiper), GetString("MediumshiperKnowPlayerDead")));

        if (Executioner.Target.ContainsValue(target.PlayerId))
            Executioner.ChangeRoleByTarget(target);
        if (Lawyer.Target.ContainsValue(target.PlayerId))
            Lawyer.ChangeRoleByTarget(target);
        if (Hacker.IsEnable) Hacker.AddDeadBody(target);
        if (Mortician.IsEnable) Mortician.OnPlayerDead(target);
        if (Bloodhound.IsEnable) Bloodhound.OnPlayerDead(target);
        if (Tracefinder.IsEnable) Tracefinder.OnPlayerDead(target);
        if (Vulture.IsEnable) Vulture.OnPlayerDead(target);

        AfterPlayerDeathTasks(target);

        Main.PlayerStates[target.PlayerId].SetDead();
        target.SetRealKiller(killer, true); //既に追加されてたらスキップ
        CountAlivePlayers(true);

        Camouflager.IsDead(target);
        TargetDies(__instance, target);

        if (Options.LowLoadMode.GetBool())
        {
            __instance.MarkDirtySettings();
            target.MarkDirtySettings();
            NotifyRoles(SpecifySeer: killer);
            NotifyRoles(SpecifySeer: target);
        }
        else
        {
            SyncAllSettings();
            NotifyRoles();
        }
    }
}

// Triggered when the shapeshifter selects a target
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckShapeshift))]
class CheckShapeshiftPatch
{
    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target, [HarmonyArgument(1)] bool shouldAnimate)
    {
        return ShapeshiftPatch.ProcessShapeshift(__instance, target); // return false to cancel the shapeshift
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CmdCheckShapeshift))]
class CmdCheckShapeshiftPatch
{
    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target, [HarmonyArgument(1)] bool shouldAnimate)
    {
        return CheckShapeshiftPatch.Prefix(__instance, target, shouldAnimate);
    }
}

// Triggered when the egg animation starts playing
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.Shapeshift))]
class ShapeshiftPatch
{ 
    public static List<byte> IgnoreNextSS = new();
    public static void Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        
    }

    public static bool ProcessShapeshift(PlayerControl shapeshifter, PlayerControl target)
    {
        if (!Main.ProcessShapeshifts) return true;
        if (IgnoreNextSS.Contains(shapeshifter.PlayerId))
        {
            IgnoreNextSS.Remove(shapeshifter.PlayerId);
            return true;
        }

        Logger.Info($"{shapeshifter?.GetNameWithRole()} => {target?.GetNameWithRole()}", "Shapeshift");

        var shapeshifting = shapeshifter.PlayerId != target.PlayerId;

        if (Main.CheckShapeshift.TryGetValue(shapeshifter.PlayerId, out var last) && last == shapeshifting)
        {
            // Dunno how you would get here but ok
            return true;
        }

        Main.CheckShapeshift[shapeshifter.PlayerId] = shapeshifting;
        Main.ShapeshiftTarget[shapeshifter.PlayerId] = target.PlayerId;

        if (Sniper.IsEnable) Sniper.OnShapeshift(shapeshifter, shapeshifting);

        if (!AmongUsClient.Instance.AmHost) return true;
        if (!shapeshifting) Camouflage.RpcSetSkin(shapeshifter);

        bool isSSneeded = true;

        if (!Pelican.IsEaten(shapeshifter.PlayerId) && !GameStates.IsVoting)
        {
            switch (shapeshifter.GetCustomRole())
            {
                case CustomRoles.EvilTracker:
                    EvilTracker.OnShapeshift(shapeshifter, target, shapeshifting);
                    isSSneeded = false;
                    break;
                case CustomRoles.RiftMaker:
                    RiftMaker.OnShapeshift(shapeshifter, shapeshifting);
                    isSSneeded = false;
                    break;
                case CustomRoles.Hitman:
                    Hitman.OnShapeshift(shapeshifter, target, shapeshifting);
                    isSSneeded = false;
                    break;
                case CustomRoles.FireWorks:
                    FireWorks.ShapeShiftState(shapeshifter, shapeshifting);
                    isSSneeded = false;
                    break;
                case CustomRoles.Penguin:
                    return false;
                case CustomRoles.Sapper:
                    Sapper.OnShapeshift(shapeshifter);
                    isSSneeded = false;
                    break;
                case CustomRoles.Warlock:
                    if (Main.CursedPlayers[shapeshifter.PlayerId] != null)//呪われた人がいるか確認
                    {
                        if (shapeshifting && !Main.CursedPlayers[shapeshifter.PlayerId].Data.IsDead)//変身解除の時に反応しない
                        {
                            var cp = Main.CursedPlayers[shapeshifter.PlayerId];
                            Vector2 cppos = cp.transform.position;//呪われた人の位置
                            Dictionary<PlayerControl, float> cpdistance = new();
                            float dis;
                            foreach (PlayerControl p in Main.AllAlivePlayerControls)
                            {
                                if (p.PlayerId == cp.PlayerId
                                    || (!Options.WarlockCanKillSelf.GetBool() && p.PlayerId == shapeshifter.PlayerId)
                                    || (!Options.WarlockCanKillAllies.GetBool() && p.GetCustomRole().IsImpostor())
                                    || p.Is(CustomRoles.Pestilence)
                                    || Pelican.IsEaten(p.PlayerId)
                                    || Medic.ProtectList.Contains(p.PlayerId))
                                    continue;

                                dis = Vector2.Distance(cppos, p.transform.position);
                                cpdistance.Add(p, dis);
                                Logger.Info($"{p?.Data?.PlayerName}の位置{dis}", "Warlock");
                            }
                            if (cpdistance.Any())
                            {
                                var min = cpdistance.OrderBy(c => c.Value).FirstOrDefault();
                                PlayerControl targetw = min.Key;
                                if (cp.RpcCheckAndMurder(targetw, true))
                                {
                                    targetw.SetRealKiller(shapeshifter);
                                    Logger.Info($"{targetw.GetNameWithRole().RemoveHtmlTags()}was killed", "Warlock");
                                    cp.Kill(targetw);
                                    shapeshifter.SetKillCooldown();
                                    shapeshifter.Notify(GetString("WarlockControlKill"));
                                }
                                //_ = new LateTask(() => { shapeshifter.CmdCheckRevertShapeshift(false); }, 1.5f, "Warlock RpcRevertShapeshift");
                            }
                            else
                            {
                                shapeshifter.Notify(GetString("WarlockNoTarget"));
                            }
                            Main.isCurseAndKill[shapeshifter.PlayerId] = false;
                        }
                        Main.CursedPlayers[shapeshifter.PlayerId] = null;
                    }
                    isSSneeded = false;
                    break;
                case CustomRoles.Escapee:
                    if (shapeshifting)
                    {
                        if (Main.EscapeeLocation.ContainsKey(shapeshifter.PlayerId))
                        {
                            var position = Main.EscapeeLocation[shapeshifter.PlayerId];
                            Main.EscapeeLocation.Remove(shapeshifter.PlayerId);
                            Logger.Msg($"{shapeshifter.GetNameWithRole().RemoveHtmlTags()}:{position}", "EscapeeTeleport");
                            TP(shapeshifter.NetTransform, position);
                            shapeshifter.RPCPlayCustomSound("Teleport");
                        }
                        else
                        {
                            Main.EscapeeLocation.Add(shapeshifter.PlayerId, shapeshifter.GetTruePosition());
                        }
                    }
                    isSSneeded = false;
                    break;
                case CustomRoles.Miner:
                    if (Main.LastEnteredVent.ContainsKey(shapeshifter.PlayerId))
                    {
                        int ventId = Main.LastEnteredVent[shapeshifter.PlayerId].Id;
                        var vent = Main.LastEnteredVent[shapeshifter.PlayerId];
                        var position = Main.LastEnteredVentLocation[shapeshifter.PlayerId];
                        Logger.Msg($"{shapeshifter.GetNameWithRole().RemoveHtmlTags()}:{position}", "MinerTeleport");
                        TP(shapeshifter.NetTransform, new Vector2(position.x, position.y));
                    }
                    isSSneeded = false;
                    break;
                case CustomRoles.Bomber:
                    if (shapeshifting)
                    {
                        Logger.Info("Bomber explosion", "Boom");
                        CustomSoundsManager.RPCPlayCustomSoundAll("Boom");
                        foreach (PlayerControl tg in Main.AllPlayerControls)
                        {
                            if (!tg.IsModClient())
                                tg.KillFlash();
                            var pos = shapeshifter.transform.position;
                            var dis = Vector2.Distance(pos, tg.transform.position);
                            if (!tg.IsAlive() || Pelican.IsEaten(tg.PlayerId) || Medic.ProtectList.Contains(tg.PlayerId) || (tg.Is(CustomRoleTypes.Impostor) && Options.ImpostorsSurviveBombs.GetBool()) || tg.inVent || tg.Is(CustomRoles.Pestilence))
                                continue;
                            if (dis > Options.BomberRadius.GetFloat())
                                continue;
                            if (tg.PlayerId == shapeshifter.PlayerId)
                                continue;
                            Main.PlayerStates[tg.PlayerId].deathReason = PlayerState.DeathReason.Bombed;
                            tg.SetRealKiller(shapeshifter);
                            tg.Kill(tg);
                            Medic.IsDead(tg);
                        }
                        _ = new LateTask(() =>
                        {
                            var totalAlive = Main.AllAlivePlayerControls.Length;
                            if (Options.BomberDiesInExplosion.GetBool() && totalAlive > 1 && !GameStates.IsEnded)
                            {
                                Main.PlayerStates[shapeshifter.PlayerId].deathReason = PlayerState.DeathReason.Bombed;
                                shapeshifter.Kill(shapeshifter);
                            }
                            //else
                            //{
                            //    shapeshifter.CmdCheckRevertShapeshift(false);
                            //}
                            NotifyRoles();
                        }, 1.5f, "Bomber Suiscide");
                    }
                    isSSneeded = false;
                    //if (Options.BomberDiesInExplosion.GetBool()) isSSneeded = false;
                    break;
                case CustomRoles.Nuker:
                    if (shapeshifting)
                    {
                        Logger.Info("Nuker explosion", "Boom");
                        CustomSoundsManager.RPCPlayCustomSoundAll("Boom");
                        foreach (PlayerControl tg in Main.AllPlayerControls)
                        {
                            if (!tg.IsModClient())
                                tg.KillFlash();
                            var pos = shapeshifter.transform.position;
                            var dis = Vector2.Distance(pos, tg.transform.position);
                            if (!tg.IsAlive() || Pelican.IsEaten(tg.PlayerId) || Medic.ProtectList.Contains(tg.PlayerId) || tg.inVent || tg.Is(CustomRoles.Pestilence))
                                continue;
                            if (dis > Options.NukeRadius.GetFloat())
                                continue;
                            if (tg.PlayerId == shapeshifter.PlayerId)
                                continue;
                            Main.PlayerStates[tg.PlayerId].deathReason = PlayerState.DeathReason.Bombed;
                            tg.SetRealKiller(shapeshifter);
                            tg.Kill(tg);
                            Medic.IsDead(tg);
                        }
                        _ = new LateTask(() =>
                        {
                            var totalAlive = Main.AllAlivePlayerControls.Length;
                            if (totalAlive > 1 && !GameStates.IsEnded)
                            {
                                Main.PlayerStates[shapeshifter.PlayerId].deathReason = PlayerState.DeathReason.Bombed;
                                shapeshifter.Kill(shapeshifter);
                            }
                            //else if (!shapeshifter.IsModClient())
                            //{
                            //    shapeshifter.CmdCheckRevertShapeshift(false);
                            //}
                            NotifyRoles();
                        }, 1.5f, "Nuke");
                    }
                    isSSneeded = false;
                    break;
                case CustomRoles.Assassin:
                    Assassin.OnShapeshift(shapeshifter, shapeshifting);
                    isSSneeded = false;
                    break;
                case CustomRoles.Undertaker:
                    Undertaker.OnShapeshift(shapeshifter, shapeshifting);
                    isSSneeded = false;
                    break;
                case CustomRoles.ImperiusCurse:
                    if (shapeshifting)
                    {
                        _ = new LateTask(() =>
                        {
                            if (!(!GameStates.IsInTask || !shapeshifter.IsAlive() || !target.IsAlive() || shapeshifter.inVent || target.inVent))
                            {
                                var originPs = target.GetTruePosition();
                                TP(target.NetTransform, shapeshifter.GetTruePosition());
                                TP(shapeshifter.NetTransform, originPs);
                            }
                        }, 1.5f, "ImperiusCurse TP");
                    }
                    break;
                case CustomRoles.QuickShooter:
                    QuickShooter.OnShapeshift(shapeshifter, shapeshifting);
                    isSSneeded = false;
                    break;
                case CustomRoles.Camouflager:
                    if (shapeshifting)
                        Camouflager.OnShapeshift(shapeshifter, shapeshifting);
                    if (!shapeshifting)
                        Camouflager.OnReportDeadBody();
                    break;
                case CustomRoles.Hangman:
                    if (Hangman.HangLimit[shapeshifter.PlayerId] < 1 && shapeshifting) shapeshifter.SetKillCooldown(Hangman.ShapeshiftDuration.GetFloat() + 1f);
                    break;
                case CustomRoles.Hacker:
                    Hacker.OnShapeshift(shapeshifter, shapeshifting, target);
                    isSSneeded = false;
                    break;
                case CustomRoles.Disperser:
                    if (shapeshifting)
                        Disperser.DispersePlayers(shapeshifter);
                    isSSneeded = false;
                    break;
                case CustomRoles.Dazzler:
                    if (shapeshifting)
                        Dazzler.OnShapeshift(shapeshifter, target);
                    isSSneeded = false;
                    break;
                case CustomRoles.Deathpact:
                    if (shapeshifting)
                        Deathpact.OnShapeshift(shapeshifter, target);
                    isSSneeded = false;
                    break;
                case CustomRoles.Devourer:
                    if (shapeshifting)
                        Devourer.OnShapeshift(shapeshifter, target);
                    isSSneeded = false;
                    break;
                case CustomRoles.Twister:
                    Twister.TwistPlayers(shapeshifter, shapeshifting);
                    isSSneeded = false;
                    break;
            }
        }

        // Forced rewriting in case the name cannot be corrected due to the timing of canceling the transformation being off.
        if (!shapeshifting && !shapeshifter.Is(CustomRoles.Glitch))
        {
            _ = new LateTask(() =>
            {
                NotifyRoles(NoCache: true);
            },
            1.2f, "ShapeShiftNotify");
        }

        if (!shapeshifting || !isSSneeded)
        {
            _ = new LateTask(shapeshifter.RpcResetAbilityCooldown, 0.01f, "Reset SS CD");
        }

        if (!isSSneeded)
        {
            Main.CheckShapeshift[shapeshifter.PlayerId] = false;
            IgnoreNextSS.Add(shapeshifter.PlayerId);
            shapeshifter.RpcShapeshift(shapeshifter, false);
        }

        return isSSneeded || !Options.DisableShapeshiftAnimations.GetBool() || !shapeshifting;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.ReportDeadBody))]
class ReportDeadBodyPatch
{
    public static Dictionary<byte, bool> CanReport;
    public static Dictionary<byte, List<GameData.PlayerInfo>> WaitReport = new();
    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] GameData.PlayerInfo target)
    {
        if (GameStates.IsMeeting) return false;
        if (Options.DisableMeeting.GetBool()) return false;
        if (Options.CurrentGameMode == CustomGameMode.SoloKombat || Options.CurrentGameMode == CustomGameMode.FFA) return false;
        if (!CanReport[__instance.PlayerId])
        {
            WaitReport[__instance.PlayerId].Add(target);
            Logger.Warn($"{__instance.GetNameWithRole().RemoveHtmlTags()}: Reporting is currently prohibited, so we will wait until it becomes possible.", "ReportDeadBody");
            return false;
        }

        Logger.Info($"{__instance.GetNameWithRole().RemoveHtmlTags()} => {target?.Object?.GetNameWithRole() ?? "null"}", "ReportDeadBody");

        foreach (var kvp in Main.PlayerStates)
        {
            kvp.Value.LastRoom = GetPlayerById(kvp.Key).GetPlainShipRoom();
        }

        if (!AmongUsClient.Instance.AmHost) return true;

        try
        {
            // If the caller is dead, this process will cancel the meeting, so stop here.
            if (__instance.Data.IsDead) return false;

            //=============================================
            // Next, check whether this meeting is allowed
            //=============================================

            var killer = target?.Object?.GetRealKiller();
            var killerRole = killer?.GetCustomRole();

            if (((IsActive(SystemTypes.Comms) && Options.CommsCamouflage.GetBool() && (Main.NormalOptions.MapId != 5 || !Options.CommsCamouflageDisableOnFungle.GetBool())) || Camouflager.IsActive) && Options.DisableReportWhenCC.GetBool()) return false;

            if (target == null)
            {
                if (__instance.Is(CustomRoles.Jester) && !Options.JesterCanUseButton.GetBool()) return false;
                if (__instance.Is(CustomRoles.NiceSwapper) && !NiceSwapper.CanStartMeeting.GetBool()) return false;
            }
            if (target != null)
            {
                if (Bloodhound.UnreportablePlayers.Contains(target.PlayerId)
                    || Vulture.UnreportablePlayers.Contains(target.PlayerId)) return false;

                switch (__instance.GetCustomRole())
                {
                    case CustomRoles.Bloodhound:
                        if (killer != null) Bloodhound.OnReportDeadBody(__instance, target, killer);
                        else __instance.Notify(GetString("BloodhoundNoTrack"));
                        return false;
                    case CustomRoles.Vulture:
                        long now = GetTimeStamp();
                        if ((Vulture.AbilityLeftInRound[__instance.PlayerId] > 0) && (now - Vulture.LastReport[__instance.PlayerId] > (long)Vulture.VultureReportCD.GetFloat()))
                        {
                            Vulture.LastReport[__instance.PlayerId] = now;
                            Vulture.OnReportDeadBody(__instance, target);
                            __instance.Notify(GetString("VultureReportBody"));
                            if (Vulture.AbilityLeftInRound[__instance.PlayerId] > 0)
                            {
                                _ = new LateTask(() =>
                                {
                                    if (GameStates.IsInTask)
                                    {
                                        __instance.Notify(GetString("VultureCooldownUp"));
                                    }
                                    return;
                                }, Vulture.VultureReportCD.GetFloat(), "Vulture CD");
                            }
                            Logger.Info($"{__instance.GetRealName()} ate {target.PlayerName} corpse", "Vulture");
                            return false;
                        }
                        break;
                    case CustomRoles.Cleaner when __instance.killTimer > 0:
                        Main.CleanerBodies.Remove(target.PlayerId);
                        Main.CleanerBodies.Add(target.PlayerId);
                        __instance.Notify(GetString("CleanerCleanBody"));
                        __instance.SetKillCooldown(Options.KillCooldownAfterCleaning.GetFloat());
                        Logger.Info($"{__instance.GetRealName()} cleans up the corpse of {target.PlayerName}", "Cleaner");
                        return false;
                    case CustomRoles.Medusa when __instance.killTimer > 0:
                        Main.MedusaBodies.Remove(target.PlayerId);
                        Main.MedusaBodies.Add(target.PlayerId);
                        __instance.RpcGuardAndKill(__instance);
                        __instance.Notify(GetString("MedusaStoneBody"));
                        __instance.SetKillCooldown(Medusa.KillCooldownAfterStoneGazing.GetFloat());
                        Logger.Info($"{__instance.GetRealName()} stoned {target.PlayerName}'s body", "Medusa");
                        return false;
                }

                if (Main.PlayerStates[target.PlayerId].deathReason == PlayerState.DeathReason.Gambled
                    || killerRole == CustomRoles.Scavenger
                    || Main.CleanerBodies.Contains(target.PlayerId)
                    || Main.MedusaBodies.Contains(target.PlayerId)) return false;

                var tpc = GetPlayerById(target.PlayerId);

                if (__instance.Is(CustomRoles.Oblivious))
                {
                    if (!tpc.Is(CustomRoles.Bait) || (tpc.Is(CustomRoles.Bait) && Options.ObliviousBaitImmune.GetBool())) /* && (target?.Object != null)*/
                    {
                        return false;
                    }
                }

                if (__instance.Is(CustomRoles.Unlucky) && (target?.Object == null || !target.Object.Is(CustomRoles.Bait)))
                {
                    var Ue = IRandom.Instance;
                    if (Ue.Next(0, 100) < Options.UnluckyReportSuicideChance.GetInt())
                    {
                        __instance.Kill(__instance);
                        Main.PlayerStates[__instance.PlayerId].deathReason = PlayerState.DeathReason.Suicide;
                        return false;
                    }
                }

                if (Main.BoobyTrapBody.Contains(target.PlayerId) && __instance.IsAlive())
                {
                    if (!Options.TrapOnlyWorksOnTheBodyBoobyTrap.GetBool())
                    {
                        var killerID = Main.KillerOfBoobyTrapBody[target.PlayerId];
                        Main.PlayerStates[__instance.PlayerId].deathReason = PlayerState.DeathReason.Bombed;
                        __instance.SetRealKiller(GetPlayerById(killerID));

                        __instance.Kill(__instance);
                        RPC.PlaySoundRPC(killerID, Sounds.KillSound);

                        if (!Main.BoobyTrapBody.Contains(__instance.PlayerId)) Main.BoobyTrapBody.Add(__instance.PlayerId);
                        if (!Main.KillerOfBoobyTrapBody.ContainsKey(__instance.PlayerId)) Main.KillerOfBoobyTrapBody.Add(__instance.PlayerId, killerID);
                        return false;
                    }
                    else
                    {
                        var killerID2 = target.PlayerId;
                        Main.PlayerStates[__instance.PlayerId].deathReason = PlayerState.DeathReason.Bombed;
                        __instance.SetRealKiller(GetPlayerById(killerID2));

                        __instance.Kill(__instance);
                        RPC.PlaySoundRPC(killerID2, Sounds.KillSound);
                        return false;
                    }
                }

                if (tpc.Is(CustomRoles.Unreportable)) return false;
            }

            if (Options.SyncButtonMode.GetBool() && target == null)
            {
                Logger.Info("最大:" + Options.SyncedButtonCount.GetInt() + ", 現在:" + Options.UsedButtonCount, "ReportDeadBody");
                if (Options.SyncedButtonCount.GetFloat() <= Options.UsedButtonCount)
                {
                    Logger.Info("使用可能ボタン回数が最大数を超えているため、ボタンはキャンセルされました。", "ReportDeadBody");
                    return false;
                }
                else Options.UsedButtonCount++;
                if (Options.SyncedButtonCount.GetFloat() == Options.UsedButtonCount)
                {
                    Logger.Info("使用可能ボタン回数が最大数に達しました。", "ReportDeadBody");
                }
            }

            AfterReportTasks(__instance, target);

        }
        catch (Exception e)
        {
            Logger.Exception(e, "ReportDeadBodyPatch");
            Logger.SendInGame("Error: " + e.ToString());
        }

        return true;
    }
    public static void AfterReportTasks(PlayerControl player, GameData.PlayerInfo target)
    {
        //====================================================================================
        //    Hereinafter, it is assumed that it is confirmed that the button is pressed.
        //====================================================================================

        if (target == null) //ボタン
        {
            if (player.Is(CustomRoles.Mayor))
            {
                Main.MayorUsedButtonCount[player.PlayerId] += 1;
            }
        }
        else
        {
            var tpc = GetPlayerById(target.PlayerId);
            if (tpc != null && !tpc.IsAlive())
            {
                // 侦探报告
                if (player.Is(CustomRoles.Detective) && player.PlayerId != target.PlayerId)
                {
                    string msg;
                    msg = string.Format(GetString("DetectiveNoticeVictim"), tpc.GetRealName(), tpc.GetDisplayRoleName());
                    if (Options.DetectiveCanknowKiller.GetBool())
                    {
                        var realKiller = tpc.GetRealKiller();
                        if (realKiller == null) msg += "；" + GetString("DetectiveNoticeKillerNotFound");
                        else msg += "；" + string.Format(GetString("DetectiveNoticeKiller"), realKiller.GetDisplayRoleName());
                    }
                    Main.DetectiveNotify.Add(player.PlayerId, msg);
                }
            }

            if (Main.InfectedBodies.Contains(target.PlayerId)) Virus.OnKilledBodyReport(player);
        }

        Main.LastVotedPlayerInfo = null;
        ShapeshiftPatch.IgnoreNextSS.Clear();
        Main.AllKillers.Clear();
        Main.ArsonistTimer.Clear();
        if (Farseer.isEnable) Main.FarseerTimer.Clear();
        Main.PuppeteerList.Clear();
        Main.PuppeteerDelayList.Clear();
        Main.TaglockedList.Clear();
        Main.GuesserGuessed.Clear();
        Main.VeteranInProtect.Clear();
        Main.GrenadierBlinding.Clear();
        Main.Lighter.Clear();
        Main.BlockSabo.Clear();
        Main.BlockedVents.Clear();
        Main.MadGrenadierBlinding.Clear();
        if (Divinator.IsEnable) Divinator.didVote.Clear();
        if (Oracle.IsEnable) Oracle.didVote.Clear();
        if (Bloodhound.IsEnable) Bloodhound.Clear();
        if (Vulture.IsEnable) Vulture.Clear();

        Camouflager.OnReportDeadBody();
        if (Bandit.IsEnable) Bandit.OnReportDeadBody();
        if (Enigma.IsEnable) Enigma.OnReportDeadBody(player, target);
        if (Psychic.IsEnable) Psychic.OnReportDeadBody();
        if (BountyHunter.IsEnable) BountyHunter.OnReportDeadBody();
        if (HeadHunter.IsEnable) HeadHunter.OnReportDeadBody();
        if (SerialKiller.IsEnable()) SerialKiller.OnReportDeadBody();
        if (Sniper.IsEnable) Sniper.OnReportDeadBody();
        if (Vampire.IsEnable) Vampire.OnStartMeeting();
        if (Poisoner.IsEnable) Poisoner.OnStartMeeting();
        if (Pelican.IsEnable) Pelican.OnReportDeadBody();
        if (Agitater.IsEnable) Agitater.OnReportDeadBody();
        //if (Counterfeiter.IsEnable) Counterfeiter.OnReportDeadBody();
        if (Tether.IsEnable) Tether.OnReportDeadBody();
        if (QuickShooter.IsEnable) QuickShooter.OnReportDeadBody();
        if (Eraser.IsEnable) Eraser.OnReportDeadBody();
        if (NiceEraser.IsEnable) NiceEraser.OnReportDeadBody();
        if (Hacker.IsEnable) Hacker.OnReportDeadBody();
        if (Judge.IsEnable) Judge.OnReportDeadBody();
        //    Councillor.OnReportDeadBody();
        if (Greedier.IsEnable()) Greedier.OnReportDeadBody();
        if (Imitator.IsEnable()) Imitator.OnReportDeadBody();
        //if (Tracker.IsEnable) Tracker.OnReportDeadBody();
        if (Addict.IsEnable) Addict.OnReportDeadBody();
        if (Deathpact.IsEnable) Deathpact.OnReportDeadBody();
        if (ParityCop.IsEnable) ParityCop.OnReportDeadBody();
        if (Doomsayer.IsEnable) Doomsayer.OnReportDeadBody();
        if (BallLightning.IsEnable) BallLightning.OnReportDeadBody();
        if (Romantic.IsEnable) Romantic.OnReportDeadBody();
        if (Jailor.IsEnable) Jailor.OnReportDeadBody();
        if (Ricochet.IsEnable) Ricochet.OnReportDeadBody();
        if (Mastermind.IsEnable) Mastermind.OnReportDeadBody();
        if (RiftMaker.IsEnable) RiftMaker.OnReportDeadBody();
        if (Hitman.IsEnable) Hitman.OnReportDeadBody();
        if (Gambler.IsEnable) Gambler.OnReportDeadBody();
        if (Tracker.IsEnable) Tracker.OnReportDeadBody();
        if (PlagueDoctor.IsEnable) PlagueDoctor.OnReportDeadBody();
        if (Penguin.IsEnable) Penguin.OnReportDeadBody();
        if (Sapper.IsEnable) Sapper.OnReportDeadBody();
        if (Chronomancer.IsEnable) Chronomancer.OnReportDeadBody();
        if (Magician.IsEnable) Magician.OnReportDeadBody();
        if (Stealth.IsEnable) Stealth.OnStartMeeting();
        if (Reckless.IsEnable) Reckless.OnReportDeadBody();

        if (Mortician.IsEnable) Mortician.OnReportDeadBody(player, target);
        if (Tracefinder.IsEnable) Tracefinder.OnReportDeadBody(/*player, target*/);
        if (Mediumshiper.IsEnable) Mediumshiper.OnReportDeadBody(target);
        if (Spiritualist.IsEnable) Spiritualist.OnReportDeadBody(target);

        if (player.Is(CustomRoles.Damocles))
        {
            Damocles.OnReport();
        }

        if (Options.InhibitorCDAfterMeetings.GetFloat() != Options.InhibitorCD.GetFloat() || Options.SaboteurCD.GetFloat() != Options.SaboteurCDAfterMeetings.GetFloat())
        {
            foreach (PlayerControl x in Main.AllAlivePlayerControls)
            {
                if (x.Is(CustomRoles.Inhibitor))
                {
                    Main.AllPlayerKillCooldown[x.PlayerId] = Options.InhibitorCDAfterMeetings.GetFloat();
                    continue;
                }
                if (x.Is(CustomRoles.Saboteur))
                {
                    Main.AllPlayerKillCooldown[x.PlayerId] = Options.SaboteurCDAfterMeetings.GetFloat();
                    continue;
                }
            }
        }

        foreach (var x in Main.RevolutionistStart)
        {
            var tar = GetPlayerById(x.Key);
            if (tar == null) continue;
            tar.Data.IsDead = true;
            Main.PlayerStates[tar.PlayerId].deathReason = PlayerState.DeathReason.Sacrifice;
            tar.RpcExileV2();
            Main.PlayerStates[tar.PlayerId].SetDead();
            Logger.Info($"{tar.GetRealName()} 因会议革命失败", "Revolutionist");
        }
        Main.RevolutionistTimer.Clear();
        Main.RevolutionistStart.Clear();
        Main.RevolutionistLastTime.Clear();

        Main.AllPlayerControls
            .Where(pc => Main.CheckShapeshift.ContainsKey(pc.PlayerId))
            .Do(pc => Camouflage.RpcSetSkin(pc, RevertToDefault: true));

        MeetingTimeManager.OnReportDeadBody();

        NotifyRoles(isForMeeting: true, NoCache: true, CamouflageIsForMeeting: true, GuesserIsForMeeting: true);

        _ = new LateTask(SyncAllSettings, 3f);
    }
    public static async void ChangeLocalNameAndRevert(string name, int time)
    {
        //It can't be helped because async Task gives a warning.
        var revertName = PlayerControl.LocalPlayer.name;
        PlayerControl.LocalPlayer.RpcSetNameEx(name);
        await Task.Delay(time);
        PlayerControl.LocalPlayer.RpcSetNameEx(revertName);
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
class FixedUpdatePatch
{
    private static readonly StringBuilder Mark = new(20);
    private static readonly StringBuilder Suffix = new(120);
    private static int LevelKickBufferTime = 10;
    private static readonly Dictionary<byte, int> BufferTime = new();
    private static readonly Dictionary<byte, int> DeadBufferTime = new();

    public static async void Postfix(PlayerControl __instance)
    {
        if (__instance == null) return;
        if (!GameStates.IsModHost) return;

        if (GameStates.IsInTask && ReportDeadBodyPatch.CanReport[__instance.PlayerId] && ReportDeadBodyPatch.WaitReport[__instance.PlayerId].Any())
        {
            if (Glitch.hackedIdList.ContainsKey(__instance.PlayerId))
            {
                __instance.Notify(string.Format(GetString("HackedByGlitch"), "Report"));
                Logger.Info("Dead Body Report Blocked (player is hacked by Glitch)", "FixedUpdate.ReportDeadBody");
                ReportDeadBodyPatch.WaitReport[__instance.PlayerId].Clear();
            }
            else
            {
                var info = ReportDeadBodyPatch.WaitReport[__instance.PlayerId][0];
                ReportDeadBodyPatch.WaitReport[__instance.PlayerId].Clear();
                Logger.Info($"{__instance.GetNameWithRole().RemoveHtmlTags()}:通報可能になったため通報処理を行います", "ReportDeadbody");
                __instance.ReportDeadBody(info);
            }
        }

        if (Options.DontUpdateDeadPlayers.GetBool() && !__instance.IsAlive())
        {
            DeadBufferTime.TryAdd(__instance.PlayerId, IRandom.Instance.Next(50, 70));
            DeadBufferTime[__instance.PlayerId]--;
            if (DeadBufferTime[__instance.PlayerId] > 0) return;
            else DeadBufferTime[__instance.PlayerId] = IRandom.Instance.Next(50, 70);
        }

        if (Options.LowLoadMode.GetBool())
        {
            BufferTime.TryAdd(__instance.PlayerId, 10);
            BufferTime[__instance.PlayerId]--;
            if (BufferTime[__instance.PlayerId] % 2 == 1 && Options.DeepLowLoad.GetBool()) return;
        }

        //if (Options.DeepLowLoad.GetBool()) await Task.Run(() => { DoPostfix(__instance); });
        /*else */
        try
        {
            await DoPostfix(__instance);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error for {__instance.GetNameWithRole().RemoveHtmlTags()}:  {ex}", "FixedUpdatePatch");
        }
    }

    public static Task DoPostfix(PlayerControl __instance)
    {
        var player = __instance;

        bool lowLoad = false;
        if (Options.LowLoadMode.GetBool())
        {
            if (BufferTime[player.PlayerId] > 0) lowLoad = true;
            else BufferTime[player.PlayerId] = 10;
        }

        if (Sniper.IsEnable) Sniper.OnFixedUpdate(player);
        if (!lowLoad)
        {
            Zoom.OnFixedUpdate();
            NameNotifyManager.OnFixedUpdate(player);
            TargetArrow.OnFixedUpdate(player);
            LocateArrow.OnFixedUpdate(player);
        }

        if (!lowLoad)
        {
            if (RPCHandlerPatch.ReportDeadBodyRPCs.Remove(player.PlayerId))
                Logger.Info($"Cleared ReportDeadBodyRPC Count for {player.GetRealName().RemoveHtmlTags()}", "FixedUpdatePatch");
        }

        if (AmongUsClient.Instance.AmHost)
        {//実行クライアントがホストの場合のみ実行
            if (GameStates.IsLobby && ((ModUpdater.hasUpdate && ModUpdater.forceUpdate) || ModUpdater.isBroken || !Main.AllowPublicRoom) && AmongUsClient.Instance.IsGamePublic)
                AmongUsClient.Instance.ChangeGamePublic(false);

            //踢出低等级的人
            if (!lowLoad && GameStates.IsLobby && !player.AmOwner && Options.KickLowLevelPlayer.GetInt() != 0 && (
                (player.Data.PlayerLevel != 0 && player.Data.PlayerLevel < Options.KickLowLevelPlayer.GetInt()) ||
                player.Data.FriendCode == string.Empty
                ))
            {
                LevelKickBufferTime--;
                if (LevelKickBufferTime <= 0)
                {
                    LevelKickBufferTime = 20;
                    AmongUsClient.Instance.KickPlayer(player.GetClientId(), false);
                    string msg = string.Format(GetString("KickBecauseLowLevel"), player.GetRealName().RemoveHtmlTags());
                    Logger.SendInGame(msg);
                    Logger.Info(msg, "LowLevel Kick");
                }
            }

            if (DoubleTrigger.FirstTriggerTimer.Any()) DoubleTrigger.OnFixedUpdate(player);
            switch (player.GetCustomRole())
            {
                case CustomRoles.Vampire:
                    Vampire.OnFixedUpdate(player);
                    break;
                case CustomRoles.Poisoner:
                    Poisoner.OnFixedUpdate(player);
                    break;
                case CustomRoles.BountyHunter when !lowLoad:
                    BountyHunter.FixedUpdate(player);
                    break;
                case CustomRoles.Glitch when !lowLoad:
                    Glitch.UpdateHackCooldown(player);
                    break;
                case CustomRoles.Aid when !lowLoad:
                    Aid.OnFixedUpdate(player);
                    break;
                case CustomRoles.Spy when !lowLoad:
                    Spy.OnFixedUpdate(player);
                    break;
                case CustomRoles.RiftMaker when !lowLoad:
                    RiftMaker.OnFixedUpdate(player);
                    break;
                case CustomRoles.Mastermind when !lowLoad:
                    Mastermind.OnFixedUpdate();
                    break;
                case CustomRoles.Magician when !lowLoad:
                    Magician.OnFixedUpdate(player);
                    break;
                case CustomRoles.SerialKiller:
                    SerialKiller.FixedUpdate(player);
                    break;
                case CustomRoles.PlagueDoctor:
                    PlagueDoctor.OnFixedUpdate(player);
                    break;
                case CustomRoles.Penguin:
                    Penguin.OnFixedUpdate(player);
                    break;
                case CustomRoles.Chronomancer when !lowLoad:
                    Chronomancer.OnFixedUpdate(player);
                    break;
                case CustomRoles.Stealth when !lowLoad:
                    Stealth.OnFixedUpdate(player);
                    break;
                case CustomRoles.Gambler when !lowLoad:
                    Gambler.OnFixedUpdate(player);
                    break;
            }
            if (GameStates.IsInTask && player.Is(CustomRoles.PlagueBearer) && PlagueBearer.IsPlaguedAll(player))
            {
                player.RpcSetCustomRole(CustomRoles.Pestilence);
                player.Notify(GetString("PlagueBearerToPestilence"));
                player.RpcGuardAndKill(player);
                if (!PlagueBearer.PestilenceList.Contains(player.PlayerId))
                    PlagueBearer.PestilenceList.Add(player.PlayerId);
                PlagueBearer.SetKillCooldownPestilence(player.PlayerId);
                PlagueBearer.playerIdList.Remove(player.PlayerId);
            }

            if (!lowLoad && player.Is(CustomRoles.Damocles))
            {
                Damocles.Update(player);
            }

            if (!lowLoad && Options.UsePets.GetBool())
            {
                switch (player.GetCustomRole())
                {
                    case CustomRoles.Doormaster:
                        if (Main.DoormasterCD.TryGetValue(player.PlayerId, out var dm) && dm + Doormaster.VentCooldown.GetInt() < GetTimeStamp())
                        {
                            Main.DoormasterCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.DoormasterCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.Sapper:
                        if (Main.SapperCD.TryGetValue(player.PlayerId, out var spp) && spp + Sapper.ShapeshiftCooldown.GetInt() < GetTimeStamp())
                        {
                            Main.SapperCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.SapperCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.Tether:
                        if (Main.TetherCD.TryGetValue(player.PlayerId, out var th) && th + Tether.VentCooldown.GetInt() < GetTimeStamp())
                        {
                            Main.TetherCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.TetherCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.CameraMan:
                        if (Main.CameraManCD.TryGetValue(player.PlayerId, out var cm) && cm + CameraMan.VentCooldown.GetInt() < GetTimeStamp())
                        {
                            Main.CameraManCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.CameraManCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.Mayor:
                        if (Main.MayorCD.TryGetValue(player.PlayerId, out var my) && my + Options.DefaultKillCooldown < GetTimeStamp())
                        {
                            Main.MayorCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.MayorCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.Paranoia:
                        if (Main.ParanoiaCD.TryGetValue(player.PlayerId, out var pn) && pn + Options.ParanoiaVentCooldown.GetInt() < GetTimeStamp())
                        {
                            Main.ParanoiaCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.ParanoiaCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.NiceHacker:
                        if (Main.HackerCD.TryGetValue(player.PlayerId, out var nh) && nh + NiceHacker.AbilityCD.GetInt() < GetTimeStamp())
                        {
                            Main.HackerCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.HackerCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.Grenadier:
                        if (Main.GrenadierCD.TryGetValue(player.PlayerId, out var gd) && gd + Options.GrenadierSkillCooldown.GetInt() + Options.GrenadierSkillDuration.GetInt() < GetTimeStamp())
                        {
                            Main.GrenadierCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.GrenadierCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.Lighter:
                        if (Main.LighterCD.TryGetValue(player.PlayerId, out var lt) && lt + Options.LighterSkillCooldown.GetInt() + Options.LighterSkillDuration.GetInt() < GetTimeStamp())
                        {
                            Main.LighterCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.LighterCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.SecurityGuard:
                        if (Main.SecurityGuardCD.TryGetValue(player.PlayerId, out var sg) && sg + Options.SecurityGuardSkillCooldown.GetInt() + Options.SecurityGuardSkillDuration.GetInt() < GetTimeStamp())
                        {
                            Main.SecurityGuardCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.SecurityGuardCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.DovesOfNeace:
                        if (Main.DovesOfNeaceCD.TryGetValue(player.PlayerId, out var don) && don + Options.DovesOfNeaceCooldown.GetInt() < GetTimeStamp())
                        {
                            Main.DovesOfNeaceCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.DovesOfNeaceCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.Alchemist:
                        if (Main.AlchemistCD.TryGetValue(player.PlayerId, out var ac) && ac + Alchemist.VentCooldown.GetInt() < GetTimeStamp())
                        {
                            Main.AlchemistCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.AlchemistCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.TimeMaster:
                        if (Main.TimeMasterCD.TryGetValue(player.PlayerId, out var tm) && tm + Options.TimeMasterSkillCooldown.GetInt() + Options.TimeMasterSkillDuration.GetInt() < GetTimeStamp())
                        {
                            Main.TimeMasterCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.TimeMasterCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.Veteran:
                        if (Main.VeteranCD.TryGetValue(player.PlayerId, out var vr) && vr + Options.VeteranSkillCooldown.GetInt() + Options.VeteranSkillDuration.GetInt() < GetTimeStamp())
                        {
                            Main.VeteranCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.VeteranCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.Sniper:
                        if (Main.SniperCD.TryGetValue(player.PlayerId, out var sp) && sp + Options.DefaultShapeshiftCooldown.GetInt() < GetTimeStamp())
                        {
                            Main.SniperCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.SniperCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.Assassin:
                        if (Main.AssassinCD.TryGetValue(player.PlayerId, out var ass) && ass + Assassin.AssassinateCooldown.GetInt() < GetTimeStamp())
                        {
                            Main.AssassinCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.AssassinCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.Undertaker:
                        if (Main.UndertakerCD.TryGetValue(player.PlayerId, out var ut) && ut + Undertaker.AssassinateCooldown.GetInt() < GetTimeStamp())
                        {
                            Main.UndertakerCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.UndertakerCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.Bomber:
                        if (Main.BomberCD.TryGetValue(player.PlayerId, out var bb) && bb + Options.BombCooldown.GetInt() < GetTimeStamp())
                        {
                            Main.BomberCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.BomberCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.Nuker:
                        if (Main.NukerCD.TryGetValue(player.PlayerId, out var nk) && nk + Options.NukeCooldown.GetInt() < GetTimeStamp())
                        {
                            Main.NukerCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.NukerCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.Escapee:
                        if (Main.EscapeeCD.TryGetValue(player.PlayerId, out var ec) && ec + Options.EscapeeSSCD.GetInt() < GetTimeStamp())
                        {
                            Main.EscapeeCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.EscapeeCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.Miner:
                        if (Main.MinerCD.TryGetValue(player.PlayerId, out var mn) && mn + Options.MinerSSCD.GetInt() < GetTimeStamp())
                        {
                            Main.MinerCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.MinerCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.Disperser:
                        if (Main.DisperserCD.TryGetValue(player.PlayerId, out var dp) && dp + Disperser.DisperserShapeshiftCooldown.GetInt() < GetTimeStamp())
                        {
                            Main.DisperserCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.DisperserCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.Twister:
                        if (Main.TwisterCD.TryGetValue(player.PlayerId, out var ts) && ts + Twister.ShapeshiftCooldown.GetInt() < GetTimeStamp())
                        {
                            Main.TwisterCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.TwisterCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                    case CustomRoles.QuickShooter:
                        if (Main.QuickShooterCD.TryGetValue(player.PlayerId, out var qs) && qs + QuickShooter.ShapeshiftCooldown.GetInt() < GetTimeStamp())
                        {
                            Main.QuickShooterCD.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                        }
                        if (Main.QuickShooterCD.ContainsKey(player.PlayerId)) NotifyRoles(SpecifySeer: player);
                        break;
                }
            }


            if (GameStates.IsInTask && Agitater.IsEnable && Agitater.AgitaterHasBombed && Agitater.CurrentBombedPlayer == player.PlayerId)
            {
                if (!player.IsAlive())
                {
                    Agitater.ResetBomb();
                }
                else
                {
                    Vector2 puppeteerPos = player.transform.position;
                    Dictionary<byte, float> targetDistance = new();
                    float dis;
                    foreach (var target in PlayerControl.AllPlayerControls)
                    {
                        if (!target.IsAlive()) continue;
                        if (target.PlayerId != player.PlayerId && target.PlayerId != Agitater.LastBombedPlayer && !target.Data.IsDead)
                        {
                            dis = Vector2.Distance(puppeteerPos, target.transform.position);
                            targetDistance.Add(target.PlayerId, dis);
                        }
                    }
                    if (targetDistance.Any())
                    {
                        var min = targetDistance.OrderBy(c => c.Value).FirstOrDefault();
                        PlayerControl target = GetPlayerById(min.Key);
                        var KillRange = GameOptionsData.KillDistances[Mathf.Clamp(GameOptionsManager.Instance.currentNormalGameOptions.KillDistance, 0, 2)];
                        if (min.Value <= KillRange && player.CanMove && target.CanMove)
                            Agitater.PassBomb(player, target);
                    }
                }
            }


            #region 女巫处理
            if (GameStates.IsInTask && Main.WarlockTimer.ContainsKey(player.PlayerId))//処理を1秒遅らせる
            {
                if (player.IsAlive())
                {
                    if (Main.WarlockTimer[player.PlayerId] >= 1f)
                    {
                        player.RpcResetAbilityCooldown();
                        Main.isCursed = false;//変身クールを１秒に変更
                        player.SyncSettings();
                        Main.WarlockTimer.Remove(player.PlayerId);
                    }
                    else Main.WarlockTimer[player.PlayerId] = Main.WarlockTimer[player.PlayerId] + Time.fixedDeltaTime;//時間をカウント
                }
                else
                {
                    Main.WarlockTimer.Remove(player.PlayerId);
                }
            }
            //ターゲットのリセット
            #endregion

            #region 纵火犯浇油处理
            if (GameStates.IsInTask && Main.ArsonistTimer.ContainsKey(player.PlayerId))//アーソニストが誰かを塗っているとき
            {
                if (!player.IsAlive() || Pelican.IsEaten(player.PlayerId))
                {
                    Main.ArsonistTimer.Remove(player.PlayerId);
                    NotifyRoles(SpecifySeer: __instance);
                    RPC.ResetCurrentDousingTarget(player.PlayerId);
                }
                else
                {
                    var ar_target = Main.ArsonistTimer[player.PlayerId].Item1;//塗られる人
                    var ar_time = Main.ArsonistTimer[player.PlayerId].Item2;//塗った時間
                    if (!ar_target.IsAlive())
                    {
                        Main.ArsonistTimer.Remove(player.PlayerId);
                    }
                    else if (ar_time >= Options.ArsonistDouseTime.GetFloat())//時間以上一緒にいて塗れた時
                    {
                        player.SetKillCooldown();
                        Main.ArsonistTimer.Remove(player.PlayerId);//塗が完了したのでDictionaryから削除
                        Main.isDoused[(player.PlayerId, ar_target.PlayerId)] = true;//塗り完了
                        player.RpcSetDousedPlayer(ar_target, true);
                        NotifyRoles(SpecifySeer: player);//名前変更
                        RPC.ResetCurrentDousingTarget(player.PlayerId);
                    }
                    else
                    {

                        float range = NormalGameOptionsV07.KillDistances[Mathf.Clamp(player.Is(CustomRoles.Reach) ? 2 : Main.NormalOptions.KillDistance, 0, 2)] + 0.5f;
                        float dis = Vector2.Distance(player.transform.position, ar_target.transform.position);//距離を出す
                        if (dis <= range)//一定の距離にターゲットがいるならば時間をカウント
                        {
                            Main.ArsonistTimer[player.PlayerId] = (ar_target, ar_time + Time.fixedDeltaTime);
                        }
                        else//それ以外は削除
                        {
                            Main.ArsonistTimer.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: player);
                            RPC.ResetCurrentDousingTarget(player.PlayerId);

                            Logger.Info($"Canceled: {player.GetNameWithRole().RemoveHtmlTags()}", "Arsonist");
                        }
                    }
                }
            }
            #endregion

            #region 革命家拉人处理
            if (GameStates.IsInTask && Main.RevolutionistTimer.ContainsKey(player.PlayerId))//当革命家拉拢一个玩家时
            {
                if (!player.IsAlive() || Pelican.IsEaten(player.PlayerId))
                {
                    Main.RevolutionistTimer.Remove(player.PlayerId);
                    NotifyRoles(SpecifySeer: player);
                    RPC.ResetCurrentDrawTarget(player.PlayerId);
                }
                else
                {
                    var rv_target = Main.RevolutionistTimer[player.PlayerId].Item1;//拉拢的人
                    var rv_time = Main.RevolutionistTimer[player.PlayerId].Item2;//拉拢时间
                    if (!rv_target.IsAlive())
                    {
                        Main.RevolutionistTimer.Remove(player.PlayerId);
                    }
                    else if (rv_time >= Options.RevolutionistDrawTime.GetFloat())//在一起时间超过多久
                    {
                        player.SetKillCooldown();
                        Main.RevolutionistTimer.Remove(player.PlayerId);//拉拢完成从字典中删除
                        Main.isDraw[(player.PlayerId, rv_target.PlayerId)] = true;//完成拉拢
                        player.RpcSetDrawPlayer(rv_target, true);
                        NotifyRoles(SpecifySeer: player);
                        RPC.ResetCurrentDrawTarget(player.PlayerId);
                        if (IRandom.Instance.Next(1, 100) <= Options.RevolutionistKillProbability.GetInt())
                        {
                            rv_target.SetRealKiller(player);
                            Main.PlayerStates[rv_target.PlayerId].deathReason = PlayerState.DeathReason.Sacrifice;
                            player.Kill(rv_target);
                            Main.PlayerStates[rv_target.PlayerId].SetDead();
                            Logger.Info($"Revolutionist: {player.GetNameWithRole().RemoveHtmlTags()} killed {rv_target.GetNameWithRole().RemoveHtmlTags()}", "Revolutionist");
                        }
                    }
                    else
                    {
                        float range = NormalGameOptionsV07.KillDistances[Mathf.Clamp(player.Is(CustomRoles.Reach) ? 2 : Main.NormalOptions.KillDistance, 0, 2)] + 0.5f;
                        float dis = Vector2.Distance(player.transform.position, rv_target.transform.position);//超出距离
                        if (dis <= range)//在一定距离内则计算时间
                        {
                            Main.RevolutionistTimer[player.PlayerId] = (rv_target, rv_time + Time.fixedDeltaTime);
                        }
                        else//否则删除
                        {
                            Main.RevolutionistTimer.Remove(player.PlayerId);
                            NotifyRoles(SpecifySeer: __instance);
                            RPC.ResetCurrentDrawTarget(player.PlayerId);

                            Logger.Info($"Canceled: {__instance.GetNameWithRole().RemoveHtmlTags()}", "Revolutionist");
                        }
                    }
                }
            }
            if (GameStates.IsInTask && player.IsDrawDone() && player.IsAlive())
            {
                if (Main.RevolutionistStart.ContainsKey(player.PlayerId)) //如果存在字典
                {
                    if (Main.RevolutionistLastTime.ContainsKey(player.PlayerId))
                    {
                        long nowtime = GetTimeStamp();
                        if (Main.RevolutionistLastTime[player.PlayerId] != nowtime) Main.RevolutionistLastTime[player.PlayerId] = nowtime;
                        int time = (int)(Main.RevolutionistLastTime[player.PlayerId] - Main.RevolutionistStart[player.PlayerId]);
                        int countdown = Options.RevolutionistVentCountDown.GetInt() - time;
                        Main.RevolutionistCountdown.Clear();
                        if (countdown <= 0)//倒计时结束
                        {
                            GetDrawPlayerCount(player.PlayerId, out var y);
                            foreach (var pc in y.Where(x => x != null && x.IsAlive()))
                            {
                                pc.Data.IsDead = true;
                                Main.PlayerStates[pc.PlayerId].deathReason = PlayerState.DeathReason.Sacrifice;
                                pc.Kill(pc);
                                Main.PlayerStates[pc.PlayerId].SetDead();
                                NotifyRoles(SpecifySeer: pc);
                            }
                            player.Data.IsDead = true;
                            Main.PlayerStates[player.PlayerId].deathReason = PlayerState.DeathReason.Sacrifice;
                            player.Kill(player);
                            Main.PlayerStates[player.PlayerId].SetDead();
                        }
                        else
                        {
                            Main.RevolutionistCountdown.Add(player.PlayerId, countdown);
                        }
                    }
                    else
                    {
                        Main.RevolutionistLastTime.TryAdd(player.PlayerId, Main.RevolutionistStart[player.PlayerId]);
                    }
                }
                else //如果不存在字典
                {
                    Main.RevolutionistStart.TryAdd(player.PlayerId, GetTimeStamp());
                }
            }
            #endregion

            if (Farseer.isEnable) Farseer.OnPostFix(player);
            if (Addict.IsEnable) Addict.FixedUpdate(player);
            if (Deathpact.IsEnable) Deathpact.OnFixedUpdate(player);

            if (!lowLoad)
            {
                switch (player.GetCustomRole())
                {
                    case CustomRoles.Veteran when GameStates.IsInTask:
                        if (Main.VeteranInProtect.TryGetValue(player.PlayerId, out var vtime) && vtime + Options.VeteranSkillDuration.GetInt() < GetTimeStamp())
                        {
                            Main.VeteranInProtect.Remove(player.PlayerId);
                            player.RpcResetAbilityCooldown();
                            player.Notify(string.Format(GetString("VeteranOffGuard"), (int)Main.VeteranNumOfUsed[player.PlayerId]));
                        }

                        break;

                    case CustomRoles.Express when GameStates.IsInTask:
                        if (Main.ExpressSpeedUp.TryGetValue(player.PlayerId, out var etime) && etime + Options.ExpressSpeedDur.GetInt() < GetTimeStamp())
                        {
                            Main.ExpressSpeedUp.Remove(player.PlayerId);
                            Main.AllPlayerSpeed[player.PlayerId] = Main.ExpressSpeedNormal;
                            player.SyncSettings();
                        }

                        break;

                    case CustomRoles.Grenadier when GameStates.IsInTask:
                        if (Main.GrenadierBlinding.TryGetValue(player.PlayerId, out var gtime) && gtime + Options.GrenadierSkillDuration.GetInt() < GetTimeStamp())
                        {
                            Main.GrenadierBlinding.Remove(player.PlayerId);
                            player.RpcResetAbilityCooldown();
                            player.Notify(string.Format(GetString("GrenadierSkillStop"), (int)Main.GrenadierNumOfUsed[player.PlayerId]));
                            MarkEveryoneDirtySettingsV3();
                        }
                        if (Main.MadGrenadierBlinding.TryGetValue(player.PlayerId, out var mgtime) && mgtime + Options.GrenadierSkillDuration.GetInt() < GetTimeStamp())
                        {
                            Main.MadGrenadierBlinding.Remove(player.PlayerId);
                            player.RpcResetAbilityCooldown();
                            player.Notify(string.Format(GetString("GrenadierSkillStop"), (int)Main.GrenadierNumOfUsed[player.PlayerId]));
                            MarkEveryoneDirtySettingsV3();
                        }

                        break;

                    case CustomRoles.Lighter when GameStates.IsInTask:
                        if (Main.Lighter.TryGetValue(player.PlayerId, out var ltime) && ltime + Options.LighterSkillDuration.GetInt() < GetTimeStamp())
                        {
                            Main.Lighter.Remove(player.PlayerId);
                            player.RpcResetAbilityCooldown();
                            player.Notify(GetString("LighterSkillStop"));
                            player.MarkDirtySettings();
                        }

                        break;

                    case CustomRoles.SecurityGuard when GameStates.IsInTask:
                        if (Main.BlockSabo.TryGetValue(player.PlayerId, out var stime) && stime + Options.SecurityGuardSkillDuration.GetInt() < GetTimeStamp())
                        {
                            Main.BlockSabo.Remove(player.PlayerId);
                            player.RpcResetAbilityCooldown();
                            player.Notify(GetString("SecurityGuardSkillStop"));
                        }

                        break;

                    case CustomRoles.TimeMaster when GameStates.IsInTask:

                        if (Main.TimeMasterInProtect.TryGetValue(player.PlayerId, out var ttime) && ttime + Options.TimeMasterSkillDuration.GetInt() < GetTimeStamp())
                        {
                            Main.TimeMasterInProtect.Remove(player.PlayerId);
                            player.RpcResetAbilityCooldown();
                            player.Notify(GetString("TimeMasterSkillStop"), (int)Main.TimeMasterNumOfUsed[player.PlayerId]);
                        }

                        break;

                    case CustomRoles.Mario when Main.MarioVentCount[player.PlayerId] > Options.MarioVentNumWin.GetInt() && GameStates.IsInTask:
                        Main.MarioVentCount[player.PlayerId] = Options.MarioVentNumWin.GetInt();
                        CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Mario); //马里奥这个多动症赢了
                        CustomWinnerHolder.WinnerIds.Add(player.PlayerId);
                        break;

                    case CustomRoles.Vulture when Vulture.BodyReportCount[player.PlayerId] >= Vulture.NumberOfReportsToWin.GetInt() && GameStates.IsInTask:
                        Vulture.BodyReportCount[player.PlayerId] = Vulture.NumberOfReportsToWin.GetInt();
                        CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Vulture);
                        CustomWinnerHolder.WinnerIds.Add(player.PlayerId);
                        break;

                    case CustomRoles.Pelican:
                        Pelican.OnFixedUpdate();
                        break;

                    case CustomRoles.BallLightning:
                        BallLightning.OnFixedUpdate();
                        break;

                    case CustomRoles.Sapper:
                        Sapper.OnFixedUpdate(player);
                        break;

                    case CustomRoles.Ignitor:
                        Ignitor.OnFixedUpdate(player);
                        break;

                    case CustomRoles.Swooper:
                        Swooper.OnFixedUpdate(player);
                        break;

                    case CustomRoles.Wraith:
                        Wraith.OnFixedUpdate(player);
                        break;

                    case CustomRoles.Chameleon:
                        Chameleon.OnFixedUpdate(player);
                        break;

                    case CustomRoles.Werewolf:
                        Werewolf.OnFixedUpdate(player);
                        break;

                    case CustomRoles.Alchemist:
                        Alchemist.OnFixedUpdate(player);
                        break;

                    case CustomRoles.BloodKnight:
                        BloodKnight.OnFixedUpdate(player);
                        break;

                    case CustomRoles.Spiritcaller:
                        Spiritcaller.OnFixedUpdate(player);
                        break;
                }

                if (Main.AllKillers.TryGetValue(player.PlayerId, out var ktime) && ktime + Options.WitnessTime.GetInt() < GetTimeStamp()) Main.AllKillers.Remove(player.PlayerId);
                if (GameStates.IsInTask && player.IsAlive() && Options.LadderDeath.GetBool()) FallFromLadder.FixedUpdate(player);
                if (GameStates.IsInGame) LoversSuicide();

                #region 傀儡师处理
                if (GameStates.IsInTask && Main.PuppeteerList.ContainsKey(player.PlayerId))
                {
                    if (!player.IsAlive() || Pelican.IsEaten(player.PlayerId))
                    {
                        Main.PuppeteerList.Remove(player.PlayerId);
                        Main.PuppeteerDelayList.Remove(player.PlayerId);
                    }
                    else if (Main.PuppeteerDelayList[player.PlayerId] + Options.PuppeteerDelay.GetInt() < GetTimeStamp())
                    {
                        Vector2 puppeteerPos = player.transform.position;//PuppeteerListのKeyの位置
                        Dictionary<byte, float> targetDistance = new();
                        float dis;
                        foreach (PlayerControl target in Main.AllAlivePlayerControls)
                        {
                            if (target.PlayerId != player.PlayerId && !target.Is(CustomRoleTypes.Impostor) && !target.Is(CustomRoles.Pestilence))
                            {
                                dis = Vector2.Distance(puppeteerPos, target.transform.position);
                                targetDistance.Add(target.PlayerId, dis);
                            }
                        }
                        if (targetDistance.Any())
                        {
                            var min = targetDistance.OrderBy(c => c.Value).FirstOrDefault();//一番値が小さい
                            PlayerControl target = GetPlayerById(min.Key);
                            var KillRange = NormalGameOptionsV07.KillDistances[Mathf.Clamp(Main.NormalOptions.KillDistance, 0, 2)];
                            if (min.Value <= KillRange && player.CanMove && target.CanMove)
                            {
                                if (player.RpcCheckAndMurder(target, true))
                                {
                                    var puppeteerId = Main.PuppeteerList[player.PlayerId];
                                    RPC.PlaySoundRPC(puppeteerId, Sounds.KillSound);
                                    target.SetRealKiller(GetPlayerById(puppeteerId));
                                    player.Kill(target);
                                    //Utils.MarkEveryoneDirtySettings();
                                    player.MarkDirtySettings();
                                    target.MarkDirtySettings();
                                    Main.PuppeteerList.Remove(player.PlayerId);
                                    Main.PuppeteerDelayList.Remove(player.PlayerId);
                                    //Utils.NotifyRoles();
                                    NotifyRoles(SpecifySeer: player);
                                    NotifyRoles(SpecifySeer: target);
                                }
                            }
                        }
                    }
                }
                if (GameStates.IsInTask && Main.TaglockedList.ContainsKey(player.PlayerId))
                {
                    if (!player.IsAlive() || Pelican.IsEaten(player.PlayerId))
                    {
                        Main.TaglockedList.Remove(player.PlayerId);
                    }
                    else
                    {
                        Vector2 puppeteerPos = player.transform.position;//PuppeteerListのKeyの位置
                        Dictionary<byte, float> targetDistance = new();
                        float dis;
                        foreach (PlayerControl target in Main.AllAlivePlayerControls)
                        {
                            if (target.PlayerId != player.PlayerId && !target.Is(CustomRoles.Pestilence))
                            {
                                dis = Vector2.Distance(puppeteerPos, target.transform.position);
                                targetDistance.Add(target.PlayerId, dis);
                            }
                        }
                        if (targetDistance.Any())
                        {
                            var min = targetDistance.OrderBy(c => c.Value).FirstOrDefault();//一番値が小さい
                            PlayerControl target = GetPlayerById(min.Key);
                            var KillRange = NormalGameOptionsV07.KillDistances[Mathf.Clamp(Main.NormalOptions.KillDistance, 0, 2)];
                            if (min.Value <= KillRange && player.CanMove && target.CanMove)
                            {
                                if (player.RpcCheckAndMurder(target, true))
                                {
                                    var puppeteerId = Main.TaglockedList[player.PlayerId];
                                    RPC.PlaySoundRPC(puppeteerId, Sounds.KillSound);
                                    target.SetRealKiller(GetPlayerById(puppeteerId));
                                    player.Kill(target);
                                    //Utils.MarkEveryoneDirtySettings();
                                    player.MarkDirtySettings();
                                    target.MarkDirtySettings();
                                    Main.TaglockedList.Remove(player.PlayerId);
                                    //Utils.NotifyRoles();
                                    NotifyRoles(SpecifySeer: player);
                                    NotifyRoles(SpecifySeer: target);
                                }
                            }
                        }
                    }
                }
                #endregion

                if (GameStates.IsInTask && player == PlayerControl.LocalPlayer)
                {
                    if (Options.DisableDevices.GetBool()) DisableDevice.FixedUpdate();
                    if (player.Is(CustomRoles.AntiAdminer)) AntiAdminer.FixedUpdate();
                    if (player.Is(CustomRoles.Monitor)) Monitor.FixedUpdate();
                }

                if (GameStates.IsInGame && Main.RefixCooldownDelay <= 0)
                    foreach (PlayerControl pc in Main.AllPlayerControls)
                    {
                        if (pc.Is(CustomRoles.Vampire) || pc.Is(CustomRoles.Warlock) || pc.Is(CustomRoles.Assassin) || pc.Is(CustomRoles.Undertaker) || pc.Is(CustomRoles.Poisoner))
                            Main.AllPlayerKillCooldown[pc.PlayerId] = Options.DefaultKillCooldown * 2;
                    }

                if (!Main.DoBlockNameChange && AmongUsClient.Instance.AmHost)
                    ApplySuffix(__instance);
            }
        }

        //LocalPlayer専用
        if (__instance.AmOwner)
        {
            //キルターゲットの上書き処理
            if (GameStates.IsInTask && !__instance.Is(CustomRoleTypes.Impostor) && __instance.CanUseKillButton() && !__instance.Data.IsDead)
            {
                var players = __instance.GetPlayersInAbilityRangeSorted(false);
                PlayerControl closest = !players.Any() ? null : players[0];
                HudManager.Instance.KillButton.SetTarget(closest);
            }
        }

        //役職テキストの表示
        var RoleTextTransform = __instance.cosmetics.nameText.transform.Find("RoleText");
        var RoleText = RoleTextTransform.GetComponent<TMPro.TextMeshPro>();

        if (RoleText != null && __instance != null && !lowLoad)
        {
            if (GameStates.IsLobby)
            {
                if (Main.playerVersion.TryGetValue(__instance.PlayerId, out var ver))
                {
                    if (Main.ForkId != ver.forkId) // フォークIDが違う場合
                        __instance.cosmetics.nameText.text = $"<color=#ff0000><size=1.2>{ver.forkId}</size>\n{__instance?.name}</color>";
                    else if (Main.version.CompareTo(ver.version) == 0)
                        __instance.cosmetics.nameText.text = ver.tag == $"{ThisAssembly.Git.Commit}({ThisAssembly.Git.Branch})" ? $"<color=#87cefa>{__instance.name}</color>" : $"<color=#ffff00><size=1.2>{ver.tag}</size>\n{__instance?.name}</color>";
                    else __instance.cosmetics.nameText.text = $"<color=#ff0000><size=1.2>v{ver.version}</size>\n{__instance?.name}</color>";
                }
                else __instance.cosmetics.nameText.text = __instance?.Data?.PlayerName;
            }
            if (GameStates.IsInGame)
            {
                var RoleTextData = GetRoleText(PlayerControl.LocalPlayer.PlayerId, __instance.PlayerId);

                //if (Options.CurrentGameMode == CustomGameMode.HideAndSeek)
                //{
                //    var hasRole = main.AllPlayerCustomRoles.TryGetValue(__instance.PlayerId, out var role);
                //    if (hasRole) RoleTextData = Utils.GetRoleTextHideAndSeek(__instance.Data.Role.Role, role);
                //}
                RoleText.text = RoleTextData.Item1;
                RoleText.color = RoleTextData.Item2;

                if (Options.CurrentGameMode == CustomGameMode.FFA || Options.CurrentGameMode == CustomGameMode.SoloKombat) RoleText.text = string.Empty;

                RoleText.enabled = IsRoleTextEnabled(__instance);

                if (!PlayerControl.LocalPlayer.Data.IsDead && PlayerControl.LocalPlayer.IsRevealedPlayer(__instance) && __instance.Is(CustomRoles.Trickster))
                {
                    RoleText.text = Farseer.RandomRole[PlayerControl.LocalPlayer.PlayerId];
                    RoleText.text += Farseer.GetTaskState();
                }

                if (!AmongUsClient.Instance.IsGameStarted && AmongUsClient.Instance.NetworkMode != NetworkModes.FreePlay)
                {
                    RoleText.enabled = false; //ゲームが始まっておらずフリープレイでなければロールを非表示
                    if (!__instance.AmOwner) __instance.cosmetics.nameText.text = __instance?.Data?.PlayerName;
                }
                if (Main.VisibleTasksCount) //他プレイヤーでVisibleTasksCountは有効なら
                    RoleText.text += GetProgressText(__instance); //ロールの横にタスクなど進行状況表示


                //変数定義
                var seer = PlayerControl.LocalPlayer;
                var target = __instance;

                string RealName;
                //    string SeerRealName;
                Mark.Clear();
                Suffix.Clear();

                //名前変更
                RealName = target.GetRealName();
                //   SeerRealName = seer.GetRealName();

                //名前色変更処理
                //自分自身の名前の色を変更
                if (target.AmOwner && GameStates.IsInTask)
                { //targetが自分自身
                    if (target.Is(CustomRoles.Arsonist) && target.IsDouseDone())
                        RealName = ColorString(GetRoleColor(CustomRoles.Arsonist), GetString("EnterVentToWin"));
                    else if (target.Is(CustomRoles.Revolutionist) && target.IsDrawDone())
                        RealName = ColorString(GetRoleColor(CustomRoles.Revolutionist), string.Format(GetString("EnterVentWinCountDown"), Main.RevolutionistCountdown.TryGetValue(seer.PlayerId, out var x) ? x : 10));
                    if (Pelican.IsEaten(seer.PlayerId))
                        RealName = ColorString(GetRoleColor(CustomRoles.Pelican), GetString("EatenByPelican"));

                    if (Options.CurrentGameMode == CustomGameMode.SoloKombat)
                        SoloKombatManager.GetNameNotify(target, ref RealName);
                    else if (Options.CurrentGameMode == CustomGameMode.FFA)
                        FFAManager.GetNameNotify(target, ref RealName);
                    if (Deathpact.IsInActiveDeathpact(seer))
                        RealName = Deathpact.GetDeathpactString(seer);
                    if (NameNotifyManager.GetNameNotify(target, out var name))
                        RealName = name;
                }

                //NameColorManager準拠の処理
                RealName = RealName.ApplyNameColorData(seer, target, false);

                switch (target.GetCustomRole()) //seerがインポスター
                {
                    case CustomRoles.Snitch when seer.GetCustomRole().IsImpostor() && target.Is(CustomRoles.Madmate) && target.GetPlayerTaskState().IsTaskFinished:
                        Mark.Append(ColorString(GetRoleColor(CustomRoles.Impostor), "★")); //targetにマーク付与
                        break;
                    case CustomRoles.Marshall when seer.GetCustomRole().IsCrewmate() && target.GetPlayerTaskState().IsTaskFinished:
                        Mark.Append(ColorString(GetRoleColor(CustomRoles.Marshall), "★")); //targetにマーク付与
                        break;
                    case CustomRoles.SuperStar when Options.EveryOneKnowSuperStar.GetBool():
                        Mark.Append(ColorString(GetRoleColor(CustomRoles.SuperStar), "★"));
                        break;
                }

                if (seer.GetCustomRole().IsCrewmate() && seer.Is(CustomRoles.Madmate) && Marshall.MadmateCanFindMarshall) //seerがインポスター
                {
                    if (target.Is(CustomRoles.Marshall) && target.GetPlayerTaskState().IsTaskFinished) //targetがタスクを終わらせたマッドスニッチ
                        Mark.Append(ColorString(GetRoleColor(CustomRoles.Marshall), "★")); //targetにマーク付与
                }

                switch (seer.GetCustomRole())
                {
                    case CustomRoles.Lookout:
                        if (seer.IsAlive() && target.IsAlive())
                        {
                            Mark.Append(ColorString(GetRoleColor(CustomRoles.Lookout), " " + target.PlayerId.ToString()) + " ");
                        }
                        break;
                    case CustomRoles.PlagueBearer when PlagueBearer.IsPlagued(seer.PlayerId, target.PlayerId):
                        Mark.Append($"<color={GetRoleColorCode(CustomRoles.PlagueBearer)}>●</color>");
                        //   PlagueBearer.SendRPC(seer, target);
                        break;
                    case CustomRoles.Arsonist:
                        if (seer.IsDousedPlayer(target))
                        {
                            Mark.Append($"<color={GetRoleColorCode(CustomRoles.Arsonist)}>▲</color>");
                        }
                        else if (
                            Main.currentDousingTarget != byte.MaxValue &&
                            Main.currentDousingTarget == target.PlayerId
                        )
                        {
                            Mark.Append($"<color={GetRoleColorCode(CustomRoles.Arsonist)}>△</color>");
                        }
                        break;
                    case CustomRoles.Revolutionist:
                        if (seer.IsDrawPlayer(target))
                        {
                            Mark.Append($"<color={GetRoleColorCode(CustomRoles.Revolutionist)}>●</color>");
                        }
                        else if (
                            Main.currentDrawTarget != byte.MaxValue &&
                            Main.currentDrawTarget == target.PlayerId
                        )
                        {
                            Mark.Append($"<color={GetRoleColorCode(CustomRoles.Revolutionist)}>○</color>");
                        }
                        break;
                    case CustomRoles.Farseer:
                        if (
                                Main.currentDrawTarget != byte.MaxValue &&
                                Main.currentDrawTarget == target.PlayerId
                            )
                        {
                            Mark.Append($"<color={GetRoleColorCode(CustomRoles.Farseer)}>○</color>");
                        }
                        break;
                    case CustomRoles.Puppeteer:
                        if (Main.PuppeteerList.ContainsValue(seer.PlayerId) && Main.PuppeteerList.ContainsKey(target.PlayerId))
                        {
                            Mark.Append($"<color={GetRoleColorCode(CustomRoles.Impostor)}>◆</color>");
                        }
                        break;
                    case CustomRoles.EvilTracker:
                        Mark.Append(EvilTracker.GetTargetMark(seer, target));
                        break;
                    case CustomRoles.Tracker:
                        Mark.Append(Tracker.GetTargetMark(seer, target));
                        break;
                    case CustomRoles.AntiAdminer when GameStates.IsInTask:
                        AntiAdminer.FixedUpdate();
                        if (target.AmOwner)
                        {
                            if (AntiAdminer.IsAdminWatch) Suffix.Append(GetString("AntiAdminerAD"));
                            if (AntiAdminer.IsVitalWatch) Suffix.Append(GetString("AntiAdminerVI"));
                            if (AntiAdminer.IsDoorLogWatch) Suffix.Append(GetString("AntiAdminerDL"));
                            if (AntiAdminer.IsCameraWatch) Suffix.Append(GetString("AntiAdminerCA"));
                        }
                        break;
                    case CustomRoles.Monitor when GameStates.IsInTask:
                        Monitor.FixedUpdate();
                        if (target.AmOwner)
                        {
                            if (Monitor.IsAdminWatch) Suffix.Append(ColorString(GetRoleColor(CustomRoles.Monitor), GetString("AdminWarning")));
                            if (Monitor.IsVitalWatch) Suffix.Append(ColorString(GetRoleColor(CustomRoles.Monitor), GetString("VitalsWarning")));
                            if (Monitor.IsDoorLogWatch) Suffix.Append(ColorString(GetRoleColor(CustomRoles.Monitor), GetString("DoorlogWarning")));
                            if (Monitor.IsCameraWatch) Suffix.Append(ColorString(GetRoleColor(CustomRoles.Monitor), GetString("CameraWarning")));
                        }
                        break;
                }

                if (Executioner.IsEnable()) Mark.Append(Executioner.TargetMark(seer, target));

                //    Mark.Append(Lawyer.TargetMark(seer, target));

                if (Gamer.IsEnable)
                    Mark.Append(Gamer.TargetMark(seer, target));

                if (Medic.IsEnable)
                {
                    if (seer.PlayerId == target.PlayerId && (Medic.InProtect(seer.PlayerId) || Medic.TempMarkProtected == seer.PlayerId) && (Medic.WhoCanSeeProtect.GetInt() == 0 || Medic.WhoCanSeeProtect.GetInt() == 2))
                        Mark.Append($"<color={GetRoleColorCode(CustomRoles.Medic)}> ●</color>");

                    if (seer.Is(CustomRoles.Medic) && (Medic.InProtect(target.PlayerId) || Medic.TempMarkProtected == target.PlayerId) && (Medic.WhoCanSeeProtect.GetInt() == 0 || Medic.WhoCanSeeProtect.GetInt() == 1))
                        Mark.Append($"<color={GetRoleColorCode(CustomRoles.Medic)}> ●</color>");

                    if (seer.Data.IsDead && Medic.InProtect(target.PlayerId) && !seer.Is(CustomRoles.Medic))
                        Mark.Append($"<color={GetRoleColorCode(CustomRoles.Medic)}> ●</color>");
                }

                if (Totocalcio.IsEnable) Mark.Append(Totocalcio.TargetMark(seer, target));
                if (Romantic.IsEnable || VengefulRomantic.IsEnable || RuthlessRomantic.IsEnable) Mark.Append(Romantic.TargetMark(seer, target));
                if (Lawyer.IsEnable()) Mark.Append(Lawyer.LawyerMark(seer, target));
                if (PlagueDoctor.IsEnable) Mark.Append(PlagueDoctor.GetMarkOthers(seer, target));

                if (Sniper.IsEnable && target.AmOwner)
                {
                    //銃声が聞こえるかチェック
                    Mark.Append(Sniper.GetShotNotify(target.PlayerId));

                }

                if (BallLightning.IsGhost(target) && BallLightning.IsEnable)
                    Mark.Append(ColorString(GetRoleColor(CustomRoles.BallLightning), "■"));

                //タスクが終わりそうなSnitchがいるとき、インポスター/キル可能なニュートラルに警告が表示される
                if (Snitch.IsEnable)
                    Mark.Append(Snitch.GetWarningArrow(seer, target));

                //インポスター/キル可能なニュートラルがタスクが終わりそうなSnitchを確認できる
                if (Snitch.IsEnable) Mark.Append(Snitch.GetWarningMark(seer, target));
                if (Marshall.IsEnable) Mark.Append(Marshall.GetWarningMark(seer, target));

                if (Main.LoversPlayers.Any())
                {
                    //ハートマークを付ける(会議中MOD視点)
                    if (__instance.Is(CustomRoles.Lovers) && PlayerControl.LocalPlayer.Is(CustomRoles.Lovers))
                    {
                        Mark.Append($"<color={GetRoleColorCode(CustomRoles.Lovers)}>♥</color>");
                    }
                    else if (__instance.Is(CustomRoles.Lovers) && PlayerControl.LocalPlayer.Data.IsDead)
                    {
                        Mark.Append($"<color={GetRoleColorCode(CustomRoles.Lovers)}>♥</color>");
                    }
                }
                //else if (__instance.Is(CustomRoles.Ntr) || PlayerControl.LocalPlayer.Is(CustomRoles.Ntr))
                //{
                //    Mark.Append($"<color={Utils.GetRoleColorCode(CustomRoles.Lovers)}>♥</color>");
                //}
                //else if (__instance == PlayerControl.LocalPlayer && CustomRolesHelper.RoleExist(CustomRoles.Ntr))
                //{
                //    Mark.Append($"<color={Utils.GetRoleColorCode(CustomRoles.Lovers)}>♥</color>");
                //}


                //矢印オプションありならタスクが終わったスニッチはインポスター/キル可能なニュートラルの方角がわかる
                if (Snitch.IsEnable) Suffix.Append(Snitch.GetSnitchArrow(seer, target));

                if (BountyHunter.IsEnable) Suffix.Append(BountyHunter.GetTargetArrow(seer, target));

                if (Mortician.IsEnable) Suffix.Append(Mortician.GetTargetArrow(seer, target));

                if (EvilTracker.IsEnable) Suffix.Append(EvilTracker.GetTargetArrow(seer, target));

                if (Bloodhound.IsEnable) Suffix.Append(Bloodhound.GetTargetArrow(seer, target));

                if (PlagueDoctor.IsEnable) Suffix.Append(PlagueDoctor.GetLowerTextOthers(seer, target));

                if (Stealth.IsEnable) Suffix.Append(Stealth.GetSuffix(seer, target));

                if (Tracker.IsEnable) Suffix.Append(Tracker.GetTrackerArrow(seer, target));

                if (Deathpact.IsEnable)
                {
                    Suffix.Append(Deathpact.GetDeathpactPlayerArrow(seer, target));
                    Suffix.Append(Deathpact.GetDeathpactMark(seer, target));
                }

                if (Spiritualist.IsEnable) Suffix.Append(Spiritualist.GetSpiritualistArrow(seer, target));

                if (Options.CurrentGameMode == CustomGameMode.FFA) Suffix.Append(FFAManager.GetPlayerArrow(seer, target));

                if (Vulture.ArrowsPointingToDeadBody.GetBool() && Vulture.IsEnable)
                    Suffix.Append(Vulture.GetTargetArrow(seer, target));

                if (Tracefinder.IsEnable) Suffix.Append(Tracefinder.GetTargetArrow(seer, target));

                if (Options.CurrentGameMode == CustomGameMode.SoloKombat)
                    Suffix.Append(SoloKombatManager.GetDisplayHealth(target));

                /*if(main.AmDebugger.Value && main.BlockKilling.TryGetValue(target.PlayerId, out var isBlocked)) {
                    Mark = isBlocked ? "(true)" : "(false)";
                }*/

                //Devourer
                bool targetDevoured = Devourer.HideNameOfConsumedPlayer.GetBool() && Devourer.PlayerSkinsCosumed.Any(a => a.Value.Contains(target.PlayerId));
                if (targetDevoured)
                    RealName = GetString("DevouredName");

                // Camouflage
                if ((IsActive(SystemTypes.Comms) && Options.CommsCamouflage.GetBool() && (Main.NormalOptions.MapId != 5 || !Options.CommsCamouflageDisableOnFungle.GetBool())) || Camouflager.IsActive)
                    RealName = $"<size=0>{RealName}</size> ";

                string DeathReason = seer.Data.IsDead && seer.KnowDeathReason(target) ? $"({ColorString(GetRoleColor(CustomRoles.Doctor), GetVitalText(target.PlayerId))})" : string.Empty;
                //Mark・Suffixの適用

                var currentText = target.cosmetics.nameText.text;
                var changeTo = $"{RealName}<size=1.7>{DeathReason}</size>{Mark}";
                var needUpdate = false;
                if (currentText != changeTo) needUpdate = true;

                if (needUpdate)
                {
                    target.cosmetics.nameText.text = changeTo;

                    if (Suffix.ToString() != string.Empty)
                    {
                        //名前が2行になると役職テキストを上にずらす必要がある
                        RoleText.transform.SetLocalY(0.35f);
                        target.cosmetics.nameText.text += "\r\n" + Suffix.ToString();

                    }
                    else
                    {
                        //役職テキストの座標を初期値に戻す
                        RoleText.transform.SetLocalY(0.2f);
                    }
                }
            }
            else
            {
                //役職テキストの座標を初期値に戻す
                if (!lowLoad) RoleText.transform.SetLocalY(0.2f);
            }
        }
        return Task.CompletedTask;
    }
    //FIXME: 役職クラス化のタイミングで、このメソッドは移動予定
    public static void LoversSuicide(byte deathId = 0x7f, bool isExiled = false)
    {
        if (!Main.LoversPlayers.Any()) return;
        if (Options.LoverSuicide.GetBool() && Main.isLoversDead == false)
        {
            foreach (PlayerControl loversPlayer in Main.LoversPlayers.ToArray())
            {
                if (!loversPlayer.Data.IsDead && loversPlayer.PlayerId != deathId)
                    continue;
                Main.isLoversDead = true;
                for (int i1 = 0; i1 < Main.LoversPlayers.Count; i1++)
                {
                    PlayerControl partnerPlayer = Main.LoversPlayers[i1];
                    if (loversPlayer.PlayerId == partnerPlayer.PlayerId)
                        continue;
                    if (partnerPlayer.PlayerId != deathId && !partnerPlayer.Data.IsDead)
                    {
                        if (partnerPlayer.Is(CustomRoles.Lovers))
                        {
                            Main.PlayerStates[partnerPlayer.PlayerId].deathReason = PlayerState.DeathReason.FollowingSuicide;
                            if (isExiled)
                                CheckForEndVotingPatch.TryAddAfterMeetingDeathPlayers(PlayerState.DeathReason.FollowingSuicide, partnerPlayer.PlayerId);
                            else
                                partnerPlayer.Kill(partnerPlayer);
                        }
                    }
                }
            }
        }
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.Start))]
class PlayerStartPatch
{
    public static void Postfix(PlayerControl __instance)
    {
        var roleText = UnityEngine.Object.Instantiate(__instance.cosmetics.nameText);
        roleText.transform.SetParent(__instance.cosmetics.nameText.transform);
        roleText.transform.localPosition = new Vector3(0f, 0.2f, 0f);
        roleText.fontSize -= 1.2f;
        roleText.text = "RoleText";
        roleText.gameObject.name = "RoleText";
        roleText.enabled = false;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.SetColor))]
class SetColorPatch
{
    public static bool IsAntiGlitchDisabled;
    public static bool Prefix(PlayerControl __instance, int bodyColor)
    {
        //色変更バグ対策
        if (!AmongUsClient.Instance.AmHost || __instance.CurrentOutfit.ColorId == bodyColor || IsAntiGlitchDisabled) return true;
        return true;
    }
}

[HarmonyPatch(typeof(Vent), nameof(Vent.EnterVent))]
class EnterVentPatch
{
    public static void Postfix(Vent __instance, [HarmonyArgument(0)] PlayerControl pc)
    {
        if (Witch.IsEnable) Witch.OnEnterVent(pc);
        if (HexMaster.IsEnable) HexMaster.OnEnterVent(pc);

        if (pc.Is(CustomRoles.Mayor))
        {
            if (Main.MayorUsedButtonCount.TryGetValue(pc.PlayerId, out var count) && count < Options.MayorNumOfUseButton.GetInt())
            {
                pc?.MyPhysics?.RpcBootFromVent(__instance.Id);
                pc?.ReportDeadBody(null);
            }
        }

        if (pc.Is(CustomRoles.Paranoia))
        {
            if (Main.ParaUsedButtonCount.TryGetValue(pc.PlayerId, out var count) && count < Options.ParanoiaNumOfUseButton.GetInt())
            {
                Main.ParaUsedButtonCount[pc.PlayerId] += 1;
                if (AmongUsClient.Instance.AmHost)
                {
                    _ = new LateTask(() =>
                    {
                        SendMessage(GetString("SkillUsedLeft") + (Options.ParanoiaNumOfUseButton.GetInt() - Main.ParaUsedButtonCount[pc.PlayerId]).ToString(), pc.PlayerId);
                    }, 4.0f, "Skill Remain Message");
                }
                pc?.MyPhysics?.RpcBootFromVent(__instance.Id);
                pc?.NoCheckStartMeeting(pc?.Data);
            }
        }

        if (pc.Is(CustomRoles.Mario))
        {
            Main.MarioVentCount.TryAdd(pc.PlayerId, 0);
            Main.MarioVentCount[pc.PlayerId]++;
            NotifyRoles(SpecifySeer: pc);
            if (pc.AmOwner)
            {
                //     if (Main.MarioVentCount[pc.PlayerId] % 5 == 0) CustomSoundsManager.Play("MarioCoin");
                //     else CustomSoundsManager.Play("MarioJump");
            }
            if (AmongUsClient.Instance.AmHost && Main.MarioVentCount[pc.PlayerId] >= Options.MarioVentNumWin.GetInt())
            {
                CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Mario); //马里奥这个多动症赢了
                CustomWinnerHolder.WinnerIds.Add(pc.PlayerId);
            }
        }

        if (!AmongUsClient.Instance.AmHost) return;

        Main.LastEnteredVent.Remove(pc.PlayerId);
        Main.LastEnteredVent.Add(pc.PlayerId, __instance);
        Main.LastEnteredVentLocation.Remove(pc.PlayerId);
        Main.LastEnteredVentLocation.Add(pc.PlayerId, pc.GetTruePosition());

        if (pc.Is(CustomRoles.Unlucky))
        {
            var Ue = IRandom.Instance;
            if (Ue.Next(0, 100) < Options.UnluckyVentSuicideChance.GetInt())
            {
                pc.Kill(pc);
                Main.PlayerStates[pc.PlayerId].deathReason = PlayerState.DeathReason.Suicide;
            }
        }

        if (pc.Is(CustomRoles.Damocles))
        {
            Damocles.OnEnterVent(__instance.Id);
        }

        switch (pc.GetCustomRole())
        {
            case CustomRoles.Swooper:
                Swooper.OnEnterVent(pc, __instance);
                break;
            case CustomRoles.Wraith:
                Wraith.OnEnterVent(pc, __instance);
                break;
            case CustomRoles.Addict:
                Addict.OnEnterVent(pc, __instance);
                break;
            case CustomRoles.Alchemist:
                Alchemist.OnEnterVent(pc, __instance.Id);
                break;
            case CustomRoles.Chameleon:
                Chameleon.OnEnterVent(pc, __instance);
                break;
            case CustomRoles.Tether:
                Tether.OnEnterVent(pc, __instance.Id);
                break;
            case CustomRoles.Werewolf:
                Werewolf.OnEnterVent(pc);
                break;
            case CustomRoles.Lurker:
                Lurker.OnEnterVent(pc);
                break;
            case CustomRoles.Doormaster:
                Doormaster.OnEnterVent(pc);
                break;
            case CustomRoles.Ventguard:
                if (Main.VentguardNumberOfAbilityUses >= 1)
                {
                    Main.VentguardNumberOfAbilityUses -= 1;
                    if (!Main.BlockedVents.Contains(__instance.Id)) Main.BlockedVents.Add(__instance.Id);
                    pc.Notify(GetString("VentBlockSuccess"));
                }
                else
                {
                    pc.Notify(GetString("OutOfAbilityUsesDoMoreTasks"));
                }
                break;
            case CustomRoles.Veteran:
                if (Main.VeteranNumOfUsed[pc.PlayerId] >= 1)
                {
                    Main.VeteranInProtect.Remove(pc.PlayerId);
                    Main.VeteranInProtect.Add(pc.PlayerId, GetTimeStamp(DateTime.Now));
                    Main.VeteranNumOfUsed[pc.PlayerId] -= 1;
                    //pc.RpcGuardAndKill(pc);
                    pc.RPCPlayCustomSound("Gunload");
                    pc.Notify(GetString("VeteranOnGuard"), Options.VeteranSkillDuration.GetFloat());
                    Main.VeteranCD.TryAdd(pc.PlayerId, GetTimeStamp());
                    pc.MarkDirtySettings();
                }
                else
                {
                    pc.Notify(GetString("OutOfAbilityUsesDoMoreTasks"));
                }
                break;
            case CustomRoles.Grenadier:
                if (Main.GrenadierNumOfUsed[pc.PlayerId] >= 1)
                {
                    if (pc.Is(CustomRoles.Madmate))
                    {
                        Main.MadGrenadierBlinding.Remove(pc.PlayerId);
                        Main.MadGrenadierBlinding.Add(pc.PlayerId, GetTimeStamp());
                        Main.AllPlayerControls.Where(x => x.IsModClient()).Where(x => !x.GetCustomRole().IsImpostorTeam() && !x.Is(CustomRoles.Madmate)).Do(x => x.RPCPlayCustomSound("FlashBang"));
                    }
                    else
                    {
                        Main.GrenadierBlinding.Remove(pc.PlayerId);
                        Main.GrenadierBlinding.Add(pc.PlayerId, GetTimeStamp());
                        Main.AllPlayerControls.Where(x => x.IsModClient()).Where(x => x.GetCustomRole().IsImpostor() || x.GetCustomRole().IsNeutral() && Options.GrenadierCanAffectNeutral.GetBool()).Do(x => x.RPCPlayCustomSound("FlashBang"));
                    }
                    //pc.RpcGuardAndKill(pc);
                    pc.RPCPlayCustomSound("FlashBang");
                    pc.Notify(GetString("GrenadierSkillInUse"), Options.GrenadierSkillDuration.GetFloat());
                    Main.GrenadierCD.TryAdd(pc.PlayerId, GetTimeStamp());
                    Main.GrenadierNumOfUsed[pc.PlayerId] -= 1;
                    MarkEveryoneDirtySettingsV3();
                }
                else
                {
                    pc.Notify(GetString("OutOfAbilityUsesDoMoreTasks"));
                }
                break;
            case CustomRoles.Lighter:
                if (Main.LighterNumOfUsed[pc.PlayerId] >= 1)
                {
                    Main.Lighter.Remove(pc.PlayerId);
                    Main.Lighter.Add(pc.PlayerId, GetTimeStamp());
                    pc.Notify(GetString("LighterSkillInUse"), Options.LighterSkillDuration.GetFloat());
                    Main.LighterCD.TryAdd(pc.PlayerId, GetTimeStamp());
                    Main.LighterNumOfUsed[pc.PlayerId] -= 1;
                    pc.MarkDirtySettings();
                }
                else
                {
                    pc.Notify(GetString("OutOfAbilityUsesDoMoreTasks"));
                }
                break;
            case CustomRoles.SecurityGuard:
                if (Main.SecurityGuardNumOfUsed[pc.PlayerId] >= 1)
                {
                    Main.BlockSabo.Remove(pc.PlayerId);
                    Main.BlockSabo.Add(pc.PlayerId, GetTimeStamp());
                    pc.Notify(GetString("SecurityGuardSkillInUse"), Options.SecurityGuardSkillDuration.GetFloat());
                    Main.SecurityGuardCD.TryAdd(pc.PlayerId, GetTimeStamp());
                    Main.SecurityGuardNumOfUsed[pc.PlayerId] -= 1;
                }
                else
                {
                    pc.Notify(GetString("OutOfAbilityUsesDoMoreTasks"));
                }
                break;
            case CustomRoles.DovesOfNeace:
                if (Main.DovesOfNeaceNumOfUsed[pc.PlayerId] < 1)
                {
                    //pc?.MyPhysics?.RpcBootFromVent(__instance.Id);
                    pc.Notify(GetString("OutOfAbilityUsesDoMoreTasks"));
                }
                else
                {
                    Main.DovesOfNeaceNumOfUsed[pc.PlayerId] -= 1;
                    //pc.RpcGuardAndKill(pc);
                    Main.AllAlivePlayerControls.Where(x =>
                    pc.Is(CustomRoles.Madmate) ?
                    x.CanUseKillButton() && x.GetCustomRole().IsCrewmate() :
                    x.CanUseKillButton()
                    ).Do(x =>
                    {
                        x.RPCPlayCustomSound("Dove");
                        x.ResetKillCooldown();
                        x.SetKillCooldown();
                        if (x.Is(CustomRoles.SerialKiller))
                        { SerialKiller.OnReportDeadBody(); }
                        x.Notify(ColorString(GetRoleColor(CustomRoles.DovesOfNeace), GetString("DovesOfNeaceSkillNotify")));
                    });
                    pc.RPCPlayCustomSound("Dove");
                    pc.Notify(string.Format(GetString("DovesOfNeaceOnGuard"), Main.DovesOfNeaceNumOfUsed[pc.PlayerId]));
                    Main.DovesOfNeaceCD.TryAdd(pc.PlayerId, GetTimeStamp());
                }
                break;
            case CustomRoles.TimeMaster:
                {
                    if (Main.TimeMasterNumOfUsed[pc.PlayerId] >= 1)
                    {
                        Main.TimeMasterNumOfUsed[pc.PlayerId] -= 1;
                        Main.TimeMasterInProtect.Remove(pc.PlayerId);
                        Main.TimeMasterNumOfUsed[pc.PlayerId] -= 1;
                        Main.TimeMasterInProtect.Add(pc.PlayerId, GetTimeStamp());
                        //if (!pc.IsModClient()) pc.RpcGuardAndKill(pc);
                        pc.Notify(GetString("TimeMasterOnGuard"), Options.TimeMasterSkillDuration.GetFloat());
                        Main.TimeMasterCD.TryAdd(pc.PlayerId, GetTimeStamp());
                        foreach (PlayerControl player in Main.AllPlayerControls)
                        {
                            if (Main.TimeMasterBackTrack.ContainsKey(player.PlayerId))
                            {
                                var position = Main.TimeMasterBackTrack[player.PlayerId];
                                TP(player.NetTransform, position);
                                if (pc != player)
                                    player?.MyPhysics?.RpcBootFromVent(player.PlayerId);
                                Main.TimeMasterBackTrack.Remove(player.PlayerId);
                            }
                            else
                            {
                                Main.TimeMasterBackTrack.Add(player.PlayerId, player.GetTruePosition());
                            }
                        }
                    }
                    else
                    {
                        pc.Notify(GetString("OutOfAbilityUsesDoMoreTasks"));
                    }
                    break;
                }
        }
    }
}
[HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.CoEnterVent))]
class CoEnterVentPatch
{
    public static bool Prefix(PlayerPhysics __instance, [HarmonyArgument(0)] int id)
    {
        if (!AmongUsClient.Instance.AmHost) return true;

        if (Options.CurrentGameMode == CustomGameMode.FFA && FFAManager.FFA_DisableVentingWhenTwoPlayersAlive.GetBool() && Main.AllAlivePlayerControls.Length <= 2)
        {
            var pc = __instance.myPlayer;
            if (pc.killTimer <= 0)
            {
                _ = new LateTask(() =>
                {
                    pc?.Notify(GetString("FFA-NoVentingBecauseTwoPlayers"), 7f);
                    pc?.MyPhysics?.RpcBootFromVent(id);
                }, 0.5f);
                return true;
            }
        }
        if (Options.CurrentGameMode == CustomGameMode.FFA && FFAManager.FFA_DisableVentingWhenKCDIsUp.GetBool() && __instance.myPlayer.killTimer <= 0)
        {
            _ = new LateTask(() =>
            {
                __instance.myPlayer?.Notify(GetString("FFA-NoVentingBecauseKCDIsUP"), 7f);
                __instance.myPlayer?.MyPhysics?.RpcBootFromVent(id);
            }, 0.5f);
            return true;
        }

        if (Glitch.hackedIdList.ContainsKey(__instance.myPlayer.PlayerId))
        {
            _ = new LateTask(() =>
            {
                __instance.myPlayer?.Notify(string.Format(GetString("HackedByGlitch"), "Vent"));
                __instance.myPlayer?.MyPhysics?.RpcBootFromVent(id);
            }, 0.5f);
            return true;
        }

        if (Main.BlockedVents.Contains(id))
        {
            var pc = __instance.myPlayer;
            if (Options.VentguardBlockDoesNotAffectCrew.GetBool() && pc.GetCustomRole().IsCrewmate()) { }
            else
            {
                _ = new LateTask(() =>
                {
                    pc?.Notify(GetString("EnteredBlockedVent"));
                    pc?.MyPhysics?.RpcBootFromVent(id);
                }, 0.5f);
                foreach (var ventguard in Main.AllAlivePlayerControls.Where(x => x.Is(CustomRoles.Ventguard)).ToArray())
                {
                    ventguard.Notify(GetString("VentguardNotify"));
                }
                return true;
            }
        }

        if (AmongUsClient.Instance.IsGameStarted)
        {
            if (__instance.myPlayer.IsDouseDone())
            {
                CustomSoundsManager.RPCPlayCustomSoundAll("Boom");
                foreach (PlayerControl pc in Main.AllAlivePlayerControls)
                {
                    if (pc != __instance.myPlayer)
                    {
                        pc.SetRealKiller(__instance.myPlayer);
                        Main.PlayerStates[pc.PlayerId].deathReason = PlayerState.DeathReason.Torched;
                        pc.Kill(pc);
                        Main.PlayerStates[pc.PlayerId].SetDead();
                    }
                }
                foreach (PlayerControl pc in Main.AllPlayerControls)
                {
                    pc.KillFlash();
                }

                CustomWinnerHolder.ShiftWinnerAndSetWinner(CustomWinner.Arsonist); //焼殺で勝利した人も勝利させる
                CustomWinnerHolder.WinnerIds.Add(__instance.myPlayer.PlayerId);
                return true;
            }
            else if (Options.ArsonistCanIgniteAnytime.GetBool())
            {
                var douseCount = GetDousedPlayerCount(__instance.myPlayer.PlayerId).Item1;
                if (douseCount >= Options.ArsonistMinPlayersToIgnite.GetInt()) // Don't check for max, since the player would not be able to ignite at all if they somehow get more players doused than the max
                {
                    if (douseCount > Options.ArsonistMaxPlayersToIgnite.GetInt()) Logger.Warn("Arsonist Ignited with more players doused than the maximum amount in the settings", "Arsonist Ignite");
                    foreach (PlayerControl pc in Main.AllAlivePlayerControls)
                    {
                        if (!__instance.myPlayer.IsDousedPlayer(pc))
                            continue;
                        pc.KillFlash();
                        pc.SetRealKiller(__instance.myPlayer);
                        Main.PlayerStates[pc.PlayerId].deathReason = PlayerState.DeathReason.Torched;
                        pc.Kill(pc);
                        Main.PlayerStates[pc.PlayerId].SetDead();
                    }
                    var apc = Main.AllAlivePlayerControls.Length;
                    if (apc == 1)
                    {
                        CustomWinnerHolder.ShiftWinnerAndSetWinner(CustomWinner.Arsonist);
                        CustomWinnerHolder.WinnerIds.Add(__instance.myPlayer.PlayerId);
                    }
                    if (apc == 2)
                    {
                        foreach (var x in Main.AllAlivePlayerControls.Where(p => p.PlayerId != __instance.myPlayer.PlayerId).ToArray())
                        {
                            if (!CustomRolesHelper.IsImpostor(x.GetCustomRole()) && !CustomRolesHelper.IsNeutralKilling(x.GetCustomRole()))
                            {
                                CustomWinnerHolder.ShiftWinnerAndSetWinner(CustomWinner.Arsonist);
                                CustomWinnerHolder.WinnerIds.Add(__instance.myPlayer.PlayerId);
                            }
                        }
                    }
                    return true;
                }
            }
        }

        if (AmongUsClient.Instance.IsGameStarted && __instance.myPlayer.IsDrawDone())//完成拉拢任务的玩家跳管后
        {
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Revolutionist);//革命者胜利
            GetDrawPlayerCount(__instance.myPlayer.PlayerId, out var x);
            CustomWinnerHolder.WinnerIds.Add(__instance.myPlayer.PlayerId);
            foreach (PlayerControl apc in x.ToArray())
            {
                CustomWinnerHolder.WinnerIds.Add(apc.PlayerId);
            }

            return true;
        }

        //处理弹出管道的阻塞
        if ((__instance.myPlayer.Data.Role.Role != RoleTypes.Engineer && //不是工程师
        !__instance.myPlayer.CanUseImpostorVentButton()) || //不能使用内鬼的跳管按钮
        (__instance.myPlayer.Is(CustomRoles.Mayor) && Main.MayorUsedButtonCount.TryGetValue(__instance.myPlayer.PlayerId, out var count) && count >= Options.MayorNumOfUseButton.GetInt()) ||
        (__instance.myPlayer.Is(CustomRoles.Paranoia) && Main.ParaUsedButtonCount.TryGetValue(__instance.myPlayer.PlayerId, out var count2) && count2 >= Options.ParanoiaNumOfUseButton.GetInt())
        //(__instance.myPlayer.Is(CustomRoles.Veteran) && Main.VeteranNumOfUsed.TryGetValue(__instance.myPlayer.PlayerId, out var count3) && count3 < 1) ||
        //(__instance.myPlayer.Is(CustomRoles.Grenadier) && Main.GrenadierNumOfUsed.TryGetValue(__instance.myPlayer.PlayerId, out var count4) && count4 < 1) ||
        //(__instance.myPlayer.Is(CustomRoles.DovesOfNeace) && Main.DovesOfNeaceNumOfUsed.TryGetValue(__instance.myPlayer.PlayerId, out var count5) && count5 < 1)
        )
        {
            //MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(__instance.NetId, (byte)RpcCalls.BootFromVent, SendOption.Reliable, -1);
            //writer.WritePacked(127);
            //AmongUsClient.Instance.FinishRpcImmediately(writer);
            _ = new LateTask(() =>
            {
                __instance.RpcBootFromVent(id);
                //int clientId = __instance.myPlayer.GetClientId();
                //MessageWriter writer2 = AmongUsClient.Instance.StartRpcImmediately(__instance.NetId, (byte)RpcCalls.BootFromVent, SendOption.Reliable, clientId);
                //writer2.Write(id);
                //AmongUsClient.Instance.FinishRpcImmediately(writer2);
            }, 0.5f, "Fix DesyncImpostor Stuck");
            return false;
        }

        switch (__instance.myPlayer.GetCustomRole())
        {
            case CustomRoles.Swooper:
                Swooper.OnCoEnterVent(__instance, id);
                break;
            case CustomRoles.Wraith:
                Wraith.OnCoEnterVent(__instance, id);
                break;
            case CustomRoles.Chameleon:
                Chameleon.OnCoEnterVent(__instance, id);
                break;
            case CustomRoles.Alchemist when Alchemist.PotionID == 6:
                Alchemist.OnCoEnterVent(__instance, id);
                break;
            case CustomRoles.RiftMaker:
                RiftMaker.OnEnterVent(__instance.myPlayer, id);
                break;
            case CustomRoles.WeaponMaster:
                WeaponMaster.OnEnterVent(__instance.myPlayer, id);
                break;
        }

        //if (__instance.myPlayer.Is(CustomRoles.DovesOfNeace)) __instance.myPlayer.Notify(GetString("DovesOfNeaceMaxUsage"));
        //if (__instance.myPlayer.Is(CustomRoles.Veteran)) __instance.myPlayer.Notify(GetString("OutOfAbilityUsesDoMoreTasks"));
        //if (__instance.myPlayer.Is(CustomRoles.Grenadier)) __instance.myPlayer.Notify(GetString("OutOfAbilityUsesDoMoreTasks"));

        return true;
    }
}

//[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.SetName))]
//class SetNamePatch
//{
//    public static void Postfix(/*PlayerControl __instance, [HarmonyArgument(0)] string name*/)
//    {
//    }
//}
[HarmonyPatch(typeof(GameData), nameof(GameData.CompleteTask))]
class GameDataCompleteTaskPatch
{
    public static void Postfix(PlayerControl pc/*, uint taskId*/)
    {
        Logger.Info($"TaskComplete: {pc.GetNameWithRole().RemoveHtmlTags()}", "CompleteTask");
        Main.PlayerStates[pc.PlayerId].UpdateTask(pc);
        NotifyRoles(SpecifySeer: pc);
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CompleteTask))]
class PlayerControlCompleteTaskPatch
{
    public static bool Prefix(PlayerControl __instance/*, uint idx*/)
    {
        var player = __instance;

        if (Workhorse.OnCompleteTask(player)) //タスク勝利をキャンセル
            return false;

        //来自资本主义的任务
        if (Main.CapitalismAddTask.ContainsKey(player.PlayerId))
        {
            var taskState = player.GetPlayerTaskState();
            taskState.AllTasksCount += Main.CapitalismAddTask[player.PlayerId];
            Main.CapitalismAddTask.Remove(player.PlayerId);
            taskState.CompletedTasksCount++;
            GameData.Instance.RpcSetTasks(player.PlayerId, Array.Empty<byte>()); //タスクを再配布
            player.SyncSettings();
            NotifyRoles(SpecifySeer: player);
            return false;
        }

        return true;
    }
    public static void Postfix(PlayerControl __instance)
    {
        var pc = __instance;
        Snitch.OnCompleteTask(pc);

        var isTaskFinish = pc.GetPlayerTaskState().IsTaskFinished;
        if (isTaskFinish && pc.Is(CustomRoles.Snitch) && pc.Is(CustomRoles.Madmate))
        {
            foreach (var impostor in Main.AllAlivePlayerControls.Where(pc => pc.Is(CustomRoleTypes.Impostor)).ToArray())
                NameColorManager.Add(impostor.PlayerId, pc.PlayerId, "#ff1919");
            NotifyRoles(SpecifySeer: pc);
        }
        if (isTaskFinish &&
            pc.GetCustomRole() is CustomRoles.Doctor or CustomRoles.Sunnyboy or CustomRoles.SpeedBooster)
        {
            //ライターもしくはスピードブースターもしくはドクターがいる試合のみタスク終了時にCustomSyncAllSettingsを実行する
            MarkEveryoneDirtySettings();
        }
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckSporeTrigger))]
public static class PlayerControlCheckSporeTriggerPatch
{
    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] Mushroom mushroom)
    {
        Logger.Info($"{__instance.GetNameWithRole()}, mushroom: {mushroom.name} / {mushroom.Id}, at {mushroom.origPosition}", "Spore Trigger");
        return !AmongUsClient.Instance.AmHost || !Options.DisableSporeTriggerOnFungle.GetBool();
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckUseZipline))]
public static class PlayerControlCheckUseZiplinePatch
{
    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target, [HarmonyArgument(1)] ZiplineBehaviour ziplineBehaviour, [HarmonyArgument(2)] bool fromTop)
    {
        ziplineBehaviour.downTravelTime = Options.ZiplineTravelTimeFromTop.GetFloat();
        ziplineBehaviour.upTravelTime = Options.ZiplineTravelTimeFromBottom.GetFloat();

        Logger.Info($"{__instance.GetNameWithRole()}, target: {target.GetNameWithRole()}, {(fromTop ? $"from Top, travel time: {ziplineBehaviour.downTravelTime}s" : $"from Bottom, travel time: {ziplineBehaviour.upTravelTime}s")}", "Zipline Use");

        if (AmongUsClient.Instance.AmHost)
        {
            if (Options.DisableZiplineFromTop.GetBool() && fromTop) return false;
            if (Options.DisableZiplineFromUnder.GetBool() && !fromTop) return false;

            if (__instance.GetCustomRole().IsImpostor() && Options.DisableZiplineForImps.GetBool()) return false;
            if (__instance.GetCustomRole().IsNeutral() && Options.DisableZiplineForNeutrals.GetBool()) return false;
            if (__instance.GetCustomRole().IsCrewmate() && Options.DisableZiplineForCrew.GetBool()) return false;
        }

        return true;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.ProtectPlayer))]
class PlayerControlProtectPlayerPatch
{
    public static void Postfix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        Logger.Info($"{__instance.GetNameWithRole().RemoveHtmlTags()} => {target.GetNameWithRole().RemoveHtmlTags()}", "ProtectPlayer");
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RemoveProtection))]
class PlayerControlRemoveProtectionPatch
{
    public static void Postfix(PlayerControl __instance)
    {
        Logger.Info($"{__instance.GetNameWithRole().RemoveHtmlTags()}", "RemoveProtection");
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSetRole))]
class PlayerControlSetRolePatch
{
    public static bool Prefix(PlayerControl __instance, ref RoleTypes roleType)
    {
        var target = __instance;
        var targetName = __instance.GetNameWithRole();
        Logger.Info($"{targetName} => {roleType}", "PlayerControl.RpcSetRole");
        if (!ShipStatus.Instance.enabled) return true;
        if (roleType is RoleTypes.CrewmateGhost or RoleTypes.ImpostorGhost)
        {
            var targetIsKiller = target.Is(CustomRoleTypes.Impostor) || Main.ResetCamPlayerList.Contains(target.PlayerId);
            var ghostRoles = new Dictionary<PlayerControl, RoleTypes>();
            foreach (PlayerControl seer in Main.AllPlayerControls)
            {
                var self = seer.PlayerId == target.PlayerId;
                var seerIsKiller = seer.Is(CustomRoleTypes.Impostor) || Main.ResetCamPlayerList.Contains(seer.PlayerId);
                if (target.Is(CustomRoles.EvilSpirit))
                {
                    ghostRoles[seer] = RoleTypes.GuardianAngel;
                }
                else if ((self && targetIsKiller) || (!seerIsKiller && target.Is(CustomRoleTypes.Impostor)))
                {
                    ghostRoles[seer] = RoleTypes.ImpostorGhost;
                }
                else
                {
                    ghostRoles[seer] = RoleTypes.CrewmateGhost;
                }
            }
            if (target.Is(CustomRoles.EvilSpirit))
            {
                roleType = RoleTypes.GuardianAngel;
            }
            else if (ghostRoles.All(kvp => kvp.Value == RoleTypes.CrewmateGhost))
            {
                roleType = RoleTypes.CrewmateGhost;
            }
            else if (ghostRoles.All(kvp => kvp.Value == RoleTypes.ImpostorGhost))
            {
                roleType = RoleTypes.ImpostorGhost;
            }
            else
            {
                foreach ((var seer, var role) in ghostRoles)
                {
                    Logger.Info($"Desync {targetName} => {role} for {seer.GetNameWithRole().RemoveHtmlTags()}", "PlayerControl.RpcSetRole");
                    target.RpcSetRoleDesync(role, seer.GetClientId());
                }
                return false;
            }
        }
        return true;
    }
}
