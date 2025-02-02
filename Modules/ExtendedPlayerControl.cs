using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using InnerNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TOHE.Modules;
using TOHE.Roles.AddOns.Impostor;
using TOHE.Roles.Crewmate;
using TOHE.Roles.Impostor;
using TOHE.Roles.Neutral;
using UnityEngine;
using static TOHE.Translator;

namespace TOHE;

static class ExtendedPlayerControl
{
    public static void RpcSetCustomRole(this PlayerControl player, CustomRoles role)
    {
        if (role < CustomRoles.NotAssigned)
        {
            Main.PlayerStates[player.PlayerId].SetMainRole(role);
        }
        else if (role >= CustomRoles.NotAssigned)   //500:NoSubRole 501~:SubRole
        {
            if (!Cleanser.CleansedCanGetAddon.GetBool() && player.Is(CustomRoles.Cleansed)) return;
            Main.PlayerStates[player.PlayerId].SetSubRole(role);
        }
        if (AmongUsClient.Instance.AmHost)
        {
            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SetCustomRole, SendOption.Reliable, -1);
            writer.Write(player.PlayerId);
            writer.WritePacked((int)role);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
    }
    public static void RpcSetCustomRole(byte PlayerId, CustomRoles role)
    {
        if (AmongUsClient.Instance.AmHost)
        {
            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SetCustomRole, SendOption.Reliable, -1);
            writer.Write(PlayerId);
            writer.WritePacked((int)role);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
    }

    public static void RpcExile(this PlayerControl player)
    {
        RPC.ExileAsync(player);
    }
    public static ClientData GetClient(this PlayerControl player)
    {
        try
        {
            var client = AmongUsClient.Instance.allClients.ToArray().FirstOrDefault(cd => cd.Character.PlayerId == player.PlayerId);
            return client;
        }
        catch
        {
            return null;
        }
    }
    public static int GetClientId(this PlayerControl player)
    {
        if (player == null) return -1;
        var client = player.GetClient();
        return client == null ? -1 : client.Id;
    }
    public static CustomRoles GetCustomRole(this GameData.PlayerInfo player)
    {
        return player == null || player.Object == null ? CustomRoles.Crewmate : player.Object.GetCustomRole();
    }
    /// <summary>
    /// *Sub-roles cannot be obtained.
    /// </summary>
    public static CustomRoles GetCustomRole(this PlayerControl player)
    {
        if (player == null)
        {
            var callerMethod = new System.Diagnostics.StackFrame(1, false).GetMethod();
            string callerMethodName = callerMethod.Name;
            Logger.Warn(callerMethod.DeclaringType.FullName + "." + callerMethodName + "tried to get a CustomRole, but the target was null.", "GetCustomRole");
            return CustomRoles.Crewmate;
        }

        return Main.PlayerStates.TryGetValue(player.PlayerId, out var State) ? State.MainRole : CustomRoles.Crewmate;
    }

    public static List<CustomRoles> GetCustomSubRoles(this PlayerControl player)
    {
        if (player == null)
        {
            Logger.Warn("CustomSubRoleを取得しようとしましたが、対象がnullでした。", "getCustomSubRole");
            return new() { CustomRoles.NotAssigned };
        }
        return Main.PlayerStates[player.PlayerId].SubRoles;
    }
    public static CountTypes GetCountTypes(this PlayerControl player)
    {
        if (player == null)
        {
            var caller = new System.Diagnostics.StackFrame(1, false);
            var callerMethod = caller.GetMethod();
            string callerMethodName = callerMethod.Name;
            string callerClassName = callerMethod.DeclaringType.FullName;
            Logger.Warn(callerClassName + "." + callerMethodName + "がCountTypesを取得しようとしましたが、対象がnullでした。", "GetCountTypes");
            return CountTypes.None;
        }

        return Main.PlayerStates.TryGetValue(player.PlayerId, out var State) ? State.countTypes : CountTypes.None;
    }
    public static void RpcSetNameEx(this PlayerControl player, string name)
    {
        foreach (PlayerControl seer in Main.AllPlayerControls)
        {
            Main.LastNotifyNames[(player.PlayerId, seer.PlayerId)] = name;
        }
        HudManagerPatch.LastSetNameDesyncCount++;

        Logger.Info($"Set:{player?.Data?.PlayerName}:{name} for All", "RpcSetNameEx");
        player.RpcSetName(name);
    }

    public static void RpcSetNamePrivate(this PlayerControl player, string name, bool DontShowOnModdedClient = false, PlayerControl seer = null, bool force = false)
    {
        //player: 名前の変更対象
        //seer: 上の変更を確認することができるプレイヤー
        if (player == null || name == null || !AmongUsClient.Instance.AmHost) return;
        if (seer == null) seer = player;
        if (!force && Main.LastNotifyNames[(player.PlayerId, seer.PlayerId)] == name)
        {
            //Logger.info($"Cancel:{player.name}:{name} for {seer.name}", "RpcSetNamePrivate");
            return;
        }
        Main.LastNotifyNames[(player.PlayerId, seer.PlayerId)] = name;
        HudManagerPatch.LastSetNameDesyncCount++;
        Logger.Info($"Set:{player?.Data?.PlayerName}:{name} for {seer.GetNameWithRole().RemoveHtmlTags()}", "RpcSetNamePrivate");

        var clientId = seer.GetClientId();
        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(player.NetId, (byte)RpcCalls.SetName, SendOption.Reliable, clientId);
        writer.Write(name);
        writer.Write(DontShowOnModdedClient);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }
    public static void RpcSetRoleDesync(this PlayerControl player, RoleTypes role, int clientId)
    {
        //player: 名前の変更対象

        if (player == null) return;
        if (AmongUsClient.Instance.ClientId == clientId)
        {
            player.SetRole(role);
            return;
        }
        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(player.NetId, (byte)RpcCalls.SetRole, SendOption.Reliable, clientId);
        writer.Write((ushort)role);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }

    public static void RpcGuardAndKill(this PlayerControl killer, PlayerControl target = null, int colorId = 0, bool forObserver = false)
    {
        if (target == null) target = killer;
        if (!forObserver && !MeetingStates.FirstMeeting) Main.AllPlayerControls.Where(x => x.Is(CustomRoles.Observer) && killer.PlayerId != x.PlayerId).Do(x => x.RpcGuardAndKill(target, colorId, true));
        // Host
        if (killer.AmOwner)
        {
            killer.ProtectPlayer(target, colorId);
            killer.MurderPlayer(target, ResultFlags);
        }
        // Other Clients
        if (killer.PlayerId != 0)
        {
            var sender = CustomRpcSender.Create("GuardAndKill Sender", SendOption.None);
            sender.StartMessage(killer.GetClientId());
            sender.StartRpc(killer.NetId, (byte)RpcCalls.ProtectPlayer)
                .WriteNetObject(target)
                .Write(colorId)
                .EndRpc();
            sender.StartRpc(killer.NetId, (byte)RpcCalls.MurderPlayer)
                .WriteNetObject(target)
                .Write((byte)ResultFlags)
                .EndRpc();
            sender.EndMessage();
            sender.SendMessage();
        }
    }
    public static void SetKillCooldownV2(this PlayerControl player, float time = -1f)
    {
        if (player == null) return;
        if (!player.CanUseKillButton()) return;
        if (time >= 0f) Main.AllPlayerKillCooldown[player.PlayerId] = time * 2;
        else Main.AllPlayerKillCooldown[player.PlayerId] *= 2;
        player.SyncSettings();
        player.RpcGuardAndKill();
        player.ResetKillCooldown();
    }
    public static void SetKillCooldown(this PlayerControl player, float time = -1f, PlayerControl target = null, bool forceAnime = false)
    {
        if (player == null) return;
        if (!player.CanUseKillButton()) return;
        if (target == null) target = player;
        if (time >= 0f) Main.AllPlayerKillCooldown[player.PlayerId] = time * 2;
        else Main.AllPlayerKillCooldown[player.PlayerId] *= 2;
        if (player.Is(CustomRoles.Glitch))
        {
            Glitch.LastKill = Utils.GetTimeStamp() + ((int)(time / 2) - Glitch.KillCooldown.GetInt());
            Glitch.KCDTimer = (int)(time / 2);
        }
        else if (forceAnime || !player.IsModClient() || !Options.DisableShieldAnimations.GetBool())
        {
            player.SyncSettings();
            player.RpcGuardAndKill(target, 11);
        }
        else
        {
            time = Main.AllPlayerKillCooldown[player.PlayerId] / 2;
            if (player.AmOwner) PlayerControl.LocalPlayer.SetKillTimer(time);
            else
            {
                MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SetKillTimer, SendOption.Reliable, player.GetClientId());
                writer.Write(time);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
            }
            Main.AllPlayerControls.Where(x => x.Is(CustomRoles.Observer) && target.PlayerId != x.PlayerId).Do(x => x.RpcGuardAndKill(target, 11, true));
        }
        if (player.GetCustomRole() is not CustomRoles.Inhibitor and not CustomRoles.Saboteur) player.ResetKillCooldown();
    }
    public static void SetKillCooldownV3(this PlayerControl player, float time = -1f, PlayerControl target = null, bool forceAnime = false)
    {
        if (player == null) return;
        if (!player.CanUseKillButton()) return;
        if (target == null) target = player;
        if (time >= 0f) Main.AllPlayerKillCooldown[player.PlayerId] = time * 2;
        else Main.AllPlayerKillCooldown[player.PlayerId] *= 2;
        if (forceAnime || !player.IsModClient() || !Options.DisableShieldAnimations.GetBool())
        {
            player.SyncSettings();
            player.RpcGuardAndKill(target, 11);
        }
        else
        {
            time = Main.AllPlayerKillCooldown[player.PlayerId] / 2;
            if (player.AmOwner) PlayerControl.LocalPlayer.SetKillTimer(time);
            else
            {
                MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SetKillTimer, SendOption.Reliable, player.GetClientId());
                writer.Write(time);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
            }
            Main.AllPlayerControls.Where(x => x.Is(CustomRoles.Observer) && target.PlayerId != x.PlayerId).Do(x => x.RpcGuardAndKill(target, 11, true));
        }
        player.ResetKillCooldown();
    }
    public static void RpcSpecificMurderPlayer(this PlayerControl killer, PlayerControl target = null)
    {
        if (target == null) target = killer;
        if (killer.AmOwner)
        {
            killer.MurderPlayer(target, ResultFlags);
        }
        else
        {
            MessageWriter messageWriter = AmongUsClient.Instance.StartRpcImmediately(killer.NetId, (byte)RpcCalls.MurderPlayer, SendOption.Reliable, killer.GetClientId());
            messageWriter.WriteNetObject(target);
            messageWriter.Write((byte)ResultFlags);
            AmongUsClient.Instance.FinishRpcImmediately(messageWriter);
        }
    }
    [Obsolete]
    public static void RpcSpecificProtectPlayer(this PlayerControl killer, PlayerControl target = null, int colorId = 0)
    {
        if (AmongUsClient.Instance.AmClient)
        {
            killer.ProtectPlayer(target, colorId);
        }
        MessageWriter messageWriter = AmongUsClient.Instance.StartRpcImmediately(killer.NetId, (byte)RpcCalls.ProtectPlayer, SendOption.Reliable, killer.GetClientId());
        messageWriter.WriteNetObject(target);
        messageWriter.Write(colorId);
        AmongUsClient.Instance.FinishRpcImmediately(messageWriter);
    }
    public static void RpcResetAbilityCooldown(this PlayerControl target)
    {
        if (!AmongUsClient.Instance.AmHost) return; //ホスト以外が実行しても何も起こさない
        Logger.Info($"Reset Ability Cooldown for {target.name} (ID: {target.PlayerId})", "RpcResetAbilityCooldown");
        if (target.Is(CustomRoles.Glitch))
        {
            Glitch.LastHack = Utils.GetTimeStamp();
            Glitch.LastMimic = Utils.GetTimeStamp();
            Glitch.MimicCDTimer = 10;
            Glitch.HackCDTimer = 10;
        }
        else if (PlayerControl.LocalPlayer == target)
        {
            //targetがホストだった場合
            PlayerControl.LocalPlayer.Data.Role.SetCooldown();
        }
        else
        {
            //targetがホスト以外だった場合
            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(target.NetId, (byte)RpcCalls.ProtectPlayer, SendOption.None, target.GetClientId());
            writer.WriteNetObject(target);
            writer.Write(0);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
        /*
            プレイヤーがバリアを張ったとき、そのプレイヤーの役職に関わらずアビリティーのクールダウンがリセットされます。
            ログの追加により無にバリアを張ることができなくなったため、代わりに自身に0秒バリアを張るように変更しました。
            この変更により、役職としての守護天使が無効化されます。
            ホストのクールダウンは直接リセットします。
        */
    }
    public static void RpcDesyncRepairSystem(this PlayerControl target, SystemTypes systemType, int amount)
    {
        var messageWriter = AmongUsClient.Instance.StartRpcImmediately(ShipStatus.Instance.NetId, (byte)RpcCalls.UpdateSystem, SendOption.Reliable, target.GetClientId());
        messageWriter.Write((byte)systemType);
        messageWriter.WriteNetObject(target);
        messageWriter.Write((byte)amount);
        AmongUsClient.Instance.FinishRpcImmediately(messageWriter);
    }

    /*public static void RpcBeKilled(this PlayerControl player, PlayerControl KilledBy = null) {
        if(!AmongUsClient.Instance.AmHost) return;
        byte KilledById;
        if(KilledBy == null)
            KilledById = byte.MaxValue;
        else
            KilledById = KilledBy.PlayerId;

        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(player.NetId, (byte)CustomRPC.BeKilled, Hazel.SendOption.Reliable, -1);
        writer.Write(player.PlayerId);
        writer.Write(KilledById);
        AmongUsClient.Instance.FinishRpcImmediately(writer);

        RPC.BeKilled(player.PlayerId, KilledById);
    }*/
    public static void MarkDirtySettings(this PlayerControl player)
    {
        PlayerGameOptionsSender.SetDirty(player.PlayerId);
    }
    public static void SyncSettings(this PlayerControl player)
    {
        PlayerGameOptionsSender.SetDirty(player.PlayerId);
        GameOptionsSender.SendAllGameOptions();
    }
    public static TaskState GetPlayerTaskState(this PlayerControl player)
    {
        return Main.PlayerStates[player.PlayerId].GetTaskState();
    }

    /*public static GameOptionsData DeepCopy(this GameOptionsData opt)
    {
        var optByte = opt.ToBytes(5);
        return GameOptionsData.FromBytes(optByte);
    }*/

    public static string GetDisplayRoleName(this PlayerControl player, bool pure = false)
    {
        return Utils.GetDisplayRoleName(player.PlayerId, pure);
    }
    public static string GetSubRoleName(this PlayerControl player, bool forUser = false)
    {
        var SubRoles = Main.PlayerStates[player.PlayerId].SubRoles.ToArray();
        if (!SubRoles.Any()) return string.Empty;
        var sb = new StringBuilder();
        foreach (CustomRoles role in SubRoles)
        {
            if (role == CustomRoles.NotAssigned) continue;
            sb.Append($"{Utils.ColorString(Color.white, "\n<size=1>")}{Utils.GetRoleName(role, forUser)}");
        }

        return sb.ToString();
    }
    public static string GetAllRoleName(this PlayerControl player, bool forUser = true)
    {
        if (!player) return null;
        var text = Utils.GetRoleName(player.GetCustomRole(), forUser);
        text += player.GetSubRoleName(forUser);
        return text;
    }
    public static string GetNameWithRole(this PlayerControl player, bool forUser = false)
    {
        return $"{player?.Data?.PlayerName}" + (GameStates.IsInGame && Options.CurrentGameMode != CustomGameMode.FFA ? $" ({player?.GetAllRoleName(forUser).RemoveHtmlTags().Replace('\n', ' ')})" : string.Empty);
    }
    public static string GetRoleColorCode(this PlayerControl player)
    {
        return Utils.GetRoleColorCode(player.GetCustomRole());
    }
    public static Color GetRoleColor(this PlayerControl player)
    {
        return Utils.GetRoleColor(player.GetCustomRole());
    }
    public static void ResetPlayerCam(this PlayerControl pc, float delay = 0f)
    {
        if (pc == null || !AmongUsClient.Instance.AmHost || pc.AmOwner) return;

        var systemtypes = (MapNames)Main.NormalOptions.MapId switch
        {
            MapNames.Polus => SystemTypes.Laboratory,
            MapNames.Airship => SystemTypes.HeliSabotage,
            _ => SystemTypes.Reactor,
        };

        _ = new LateTask(() =>
        {
            pc.RpcDesyncRepairSystem(systemtypes, 128);
        }, 0f + delay, "Reactor Desync");

        _ = new LateTask(() =>
        {
            pc.RpcSpecificMurderPlayer();
        }, 0.2f + delay, "Murder To Reset Cam");

        _ = new LateTask(() =>
        {
            pc.RpcDesyncRepairSystem(systemtypes, 16);
            if (Main.NormalOptions.MapId == 4) // Airship only
                pc.RpcDesyncRepairSystem(systemtypes, 17);
        }, 0.4f + delay, "Fix Desync Reactor");
    }
    public static void ReactorFlash(this PlayerControl pc, float delay = 0f)
    {
        if (pc == null) return;

        Logger.Info($"Reactor Flash for {pc}", "ReactorFlash");

        var systemtypes = (MapNames)Main.NormalOptions.MapId switch
        {
            MapNames.Polus => SystemTypes.Laboratory,
            MapNames.Airship => SystemTypes.HeliSabotage,
            _ => SystemTypes.Reactor,
        };

        float FlashDuration = Options.KillFlashDuration.GetFloat();

        pc.RpcDesyncRepairSystem(systemtypes, 128);

        _ = new LateTask(() =>
        {
            pc.RpcDesyncRepairSystem(systemtypes, 16);

            if (Main.NormalOptions.MapId == 4) // on Airship
                pc.RpcDesyncRepairSystem(systemtypes, 17);
        }, FlashDuration + delay, "Fix Desync Reactor");
    }

    public static string GetRealName(this PlayerControl player, bool isMeeting = false)
    {
        return isMeeting ? player?.Data?.PlayerName : player?.name;
    }
    public static bool HasKillButton(this PlayerControl pc)
    {
        if (!pc.IsAlive() || pc.Data.Role.Role == RoleTypes.GuardianAngel || Pelican.IsEaten(pc.PlayerId)) return false;

        return pc.GetCustomRole() switch
        {
            CustomRoles.FireWorks => true,
            CustomRoles.Mafia => true,
            CustomRoles.Underdog => true,
            CustomRoles.Mastermind => true,
            CustomRoles.Gambler => true,
            CustomRoles.RiftMaker => true,
            CustomRoles.Hitman => true,
            CustomRoles.Inhibitor => true,
            CustomRoles.Chronomancer => true,
            CustomRoles.Nullifier => true,
            CustomRoles.Stealth => true,
            CustomRoles.Penguin => true,
            CustomRoles.Sapper => true,
            CustomRoles.Saboteur => true,
            CustomRoles.Sniper => true,
            CustomRoles.Sheriff => true,
            CustomRoles.Crusader => true,
            CustomRoles.CopyCat => true,
            CustomRoles.Jailor => true,
            CustomRoles.Pelican => true,
            CustomRoles.Arsonist => true,
            CustomRoles.Revolutionist => true,
            CustomRoles.SwordsMan => true,
            CustomRoles.Jackal => true,
            CustomRoles.Sidekick => true,
            CustomRoles.HexMaster => true,
            CustomRoles.Bandit => true,
            CustomRoles.Agitater => true,
            CustomRoles.Poisoner => true,
            CustomRoles.Juggernaut => true,
            CustomRoles.Ritualist => true,
            CustomRoles.Pyromaniac => true,
            CustomRoles.Eclipse => true,
            CustomRoles.NSerialKiller => true,
            CustomRoles.PlagueDoctor => true,
            CustomRoles.Magician => true,
            CustomRoles.WeaponMaster => true,
            CustomRoles.Postman => true,
            CustomRoles.Reckless => true,
            CustomRoles.Vengeance => true,
            CustomRoles.HeadHunter => true,
            CustomRoles.Imitator => true,
            CustomRoles.Werewolf => true,
            CustomRoles.Medusa => true,
            CustomRoles.Traitor => true,
            CustomRoles.Glitch => true,
            CustomRoles.Pickpocket => true,
            CustomRoles.Maverick => true,
            CustomRoles.Jinx => true,
            CustomRoles.Parasite => true,
            CustomRoles.Refugee => true,
            CustomRoles.Wraith => true,
            CustomRoles.Bomber => true,
            CustomRoles.Nuker => false,
            CustomRoles.Innocent => true,
            CustomRoles.Aid => true,
            CustomRoles.Witness => true,
            CustomRoles.Pursuer => true,
            CustomRoles.Morphling => true,
            CustomRoles.FFF => true,
            CustomRoles.Medic => true,
            CustomRoles.Gamer => true,
            CustomRoles.DarkHide => true,
            CustomRoles.Provocateur => true,
            CustomRoles.Assassin => true,
            CustomRoles.Undertaker => true,
            CustomRoles.BloodKnight => true,
            CustomRoles.Crewpostor => false,
            CustomRoles.Totocalcio => true,
            CustomRoles.Romantic => true,
            CustomRoles.RuthlessRomantic => true,
            CustomRoles.VengefulRomantic => true,
            CustomRoles.Succubus => true,
            CustomRoles.CursedSoul => true,
            CustomRoles.Admirer => true,
            CustomRoles.Amnesiac => true,
            CustomRoles.Infectious => true,
            CustomRoles.Monarch => true,
            CustomRoles.Deputy => true,
            CustomRoles.Virus => true,
            CustomRoles.Farseer => true,
            CustomRoles.Spiritcaller => true,
            CustomRoles.PlagueBearer => true,
            CustomRoles.Pestilence => true,

            _ => pc.Is(CustomRoleTypes.Impostor),
        };
    }
    public static bool CanUseKillButton(this PlayerControl pc)
    {
        //int playerCount = Main.AllAlivePlayerControls.Count();
        if (!pc.IsAlive() || pc.Data.Role.Role == RoleTypes.GuardianAngel || Pelican.IsEaten(pc.PlayerId)) return false;

        if (Mastermind.ManipulatedPlayers.ContainsKey(pc.PlayerId)) return true;

        return pc.GetCustomRole() switch
        {
            //SoloKombat
            CustomRoles.KB_Normal => pc.SoloAlive(),
            //FFA
            CustomRoles.Killer => pc.IsAlive(),
            //Standard
            CustomRoles.FireWorks => FireWorks.CanUseKillButton(pc),
            CustomRoles.Mafia => Utils.CanMafiaKill(),
            //       CustomRoles.Mare => pc.IsAlive(),
            //CustomRoles.Underdog => playerCount <= Options.UnderdogMaximumPlayersNeededToKill.GetInt(),
            CustomRoles.Underdog => pc.IsAlive(),
            CustomRoles.Mastermind => pc.IsAlive(),
            CustomRoles.Gambler => pc.IsAlive(),
            CustomRoles.RiftMaker => pc.IsAlive(),
            CustomRoles.Hitman => pc.IsAlive(),
            CustomRoles.Sapper => false,
            CustomRoles.Penguin => pc.IsAlive(),
            CustomRoles.Stealth => pc.IsAlive(),
            CustomRoles.Nullifier => pc.IsAlive(),
            CustomRoles.Chronomancer => pc.IsAlive(),
            CustomRoles.Inhibitor => !Utils.IsActive(SystemTypes.Electrical) && !Utils.IsActive(SystemTypes.Laboratory) && !Utils.IsActive(SystemTypes.Comms) && !Utils.IsActive(SystemTypes.LifeSupp) && !Utils.IsActive(SystemTypes.Reactor),
            CustomRoles.Saboteur => Utils.IsActive(SystemTypes.Electrical) || Utils.IsActive(SystemTypes.Laboratory) || Utils.IsActive(SystemTypes.Comms) || Utils.IsActive(SystemTypes.LifeSupp) || Utils.IsActive(SystemTypes.Reactor),
            CustomRoles.Sniper => Sniper.CanUseKillButton(pc),
            CustomRoles.Sheriff => Sheriff.CanUseKillButton(pc.PlayerId),
            CustomRoles.Crusader => Crusader.CanUseKillButton(pc.PlayerId),
            CustomRoles.CopyCat => pc.IsAlive(),
            CustomRoles.Jailor => pc.IsAlive(),
            CustomRoles.Pelican => pc.IsAlive(),
            CustomRoles.Arsonist => Options.ArsonistCanIgniteAnytime.GetBool() ? Utils.GetDousedPlayerCount(pc.PlayerId).Item1 < Options.ArsonistMaxPlayersToIgnite.GetInt() : !pc.IsDouseDone(),
            CustomRoles.Revolutionist => !pc.IsDrawDone(),
            CustomRoles.SwordsMan => pc.IsAlive() && !SwordsMan.IsKilled(pc.PlayerId),
            CustomRoles.Jackal => pc.IsAlive(),
            CustomRoles.Sidekick => pc.IsAlive(),
            CustomRoles.HexMaster => pc.IsAlive(),
            CustomRoles.Bandit => pc.IsAlive(),
            CustomRoles.Agitater => pc.IsAlive(),
            CustomRoles.Poisoner => pc.IsAlive(),
            CustomRoles.Juggernaut => pc.IsAlive(),
            //CustomRoles.Reverie => pc.IsAlive(),
            CustomRoles.Ritualist => pc.IsAlive(),
            CustomRoles.Pyromaniac => pc.IsAlive(),
            CustomRoles.Eclipse => pc.IsAlive(),
            CustomRoles.NSerialKiller => pc.IsAlive(),
            CustomRoles.PlagueDoctor => pc.IsAlive() && PlagueDoctor.CanUseKillButton(),
            CustomRoles.Postman => pc.IsAlive() && !Postman.IsFinished,
            CustomRoles.Magician => pc.IsAlive(),
            CustomRoles.WeaponMaster => pc.IsAlive() && WeaponMaster.CanKill(pc),
            CustomRoles.Reckless => pc.IsAlive(),
            CustomRoles.Vengeance => pc.IsAlive(),
            CustomRoles.HeadHunter => pc.IsAlive(),
            CustomRoles.Imitator => pc.IsAlive(),
            CustomRoles.Werewolf => Werewolf.IsRampaging(pc.PlayerId) && pc.IsAlive(),
            CustomRoles.Medusa => pc.IsAlive(),
            CustomRoles.Traitor => pc.IsAlive(),
            CustomRoles.Glitch => pc.IsAlive(),
            CustomRoles.Pickpocket => pc.IsAlive(),
            CustomRoles.Maverick => pc.IsAlive(),
            CustomRoles.Jinx => pc.IsAlive(),
            CustomRoles.Parasite => pc.IsAlive(),
            CustomRoles.Refugee => pc.IsAlive(),
            //CustomRoles.NWitch => pc.IsAlive(),
            CustomRoles.Wraith => pc.IsAlive(),
            CustomRoles.Bomber => Options.BomberCanKill.GetBool() && pc.IsAlive(),
            CustomRoles.Nuker => false,
            CustomRoles.Innocent => pc.IsAlive(),
            //CustomRoles.Counterfeiter => Counterfeiter.CanUseKillButton(pc.PlayerId),
            CustomRoles.Aid => pc.IsAlive() && Aid.UseLimit[pc.PlayerId] >= 1,
            CustomRoles.Witness => pc.IsAlive(),
            CustomRoles.Pursuer => Pursuer.CanUseKillButton(pc.PlayerId),
            CustomRoles.Morphling => Morphling.CanUseKillButton(pc.PlayerId),
            CustomRoles.FFF => pc.IsAlive(),
            CustomRoles.Medic => Medic.CanUseKillButton(pc.PlayerId),
            CustomRoles.Gamer => pc.IsAlive(),
            CustomRoles.DarkHide => DarkHide.CanUseKillButton(pc),
            CustomRoles.Provocateur => pc.IsAlive() && !Main.Provoked.ContainsKey(pc.PlayerId),
            CustomRoles.Assassin => Assassin.CanUseKillButton(pc),
            CustomRoles.Undertaker => Undertaker.CanUseKillButton(pc),
            CustomRoles.BloodKnight => pc.IsAlive(),
            CustomRoles.Crewpostor => false,
            CustomRoles.Totocalcio => Totocalcio.CanUseKillButton(pc),
            CustomRoles.Romantic => pc.IsAlive(),
            CustomRoles.RuthlessRomantic => pc.IsAlive(),
            CustomRoles.VengefulRomantic => VengefulRomantic.CanUseKillButton(pc),
            CustomRoles.Succubus => Succubus.CanUseKillButton(pc),
            CustomRoles.CursedSoul => CursedSoul.CanUseKillButton(pc),
            CustomRoles.Admirer => Admirer.CanUseKillButton(pc),
            CustomRoles.Amnesiac => Amnesiac.CanUseKillButton(pc),
            //CustomRoles.Warlock => !Main.isCurseAndKill.TryGetValue(pc.PlayerId, out bool wcs) || !wcs,
            CustomRoles.Infectious => Infectious.CanUseKillButton(pc),
            CustomRoles.Monarch => Monarch.CanUseKillButton(pc),
            CustomRoles.Deputy => Deputy.CanUseKillButton(pc),
            CustomRoles.Virus => pc.IsAlive(),
            CustomRoles.Farseer => pc.IsAlive(),
            CustomRoles.Spiritcaller => pc.IsAlive(),
            CustomRoles.PlagueBearer => pc.IsAlive(),
            CustomRoles.Pestilence => pc.IsAlive(),
            //CustomRoles.Pirate => pc.IsAlive(),

            _ => pc.Is(CustomRoleTypes.Impostor),
        };
    }
    public static bool CanUseImpostorVentButton(this PlayerControl pc)
    {
        if (!pc.IsAlive() || pc.Data.Role.Role == RoleTypes.GuardianAngel) return false;
        if (CopyCat.playerIdList.Contains(pc.PlayerId)) return true;

        return pc.GetCustomRole() switch
        {
            CustomRoles.Minimalism or
            CustomRoles.Sheriff or
            CustomRoles.Deputy or
            CustomRoles.Innocent or
            //    CustomRoles.SwordsMan or
            CustomRoles.FFF or
            CustomRoles.Medic or
            //      CustomRoles.NWitch or
            CustomRoles.Monarch or
            CustomRoles.Provocateur or
            CustomRoles.Totocalcio or
            CustomRoles.Romantic or
            CustomRoles.Succubus or
            CustomRoles.CursedSoul or
            CustomRoles.PlagueBearer or
            CustomRoles.Admirer or
            CustomRoles.Amnesiac or
            CustomRoles.PlagueDoctor or
            CustomRoles.Crusader
            => false,

            CustomRoles.Jackal => Jackal.CanVent.GetBool(),
            CustomRoles.VengefulRomantic => Romantic.VengefulCanVent.GetBool(),
            CustomRoles.RuthlessRomantic => Romantic.RuthlessCanVent.GetBool(),
            CustomRoles.Sidekick => Jackal.CanVentSK.GetBool(),
            CustomRoles.Glitch => Glitch.CanVent.GetBool(),
            CustomRoles.Poisoner => Poisoner.CanVent.GetBool(),
            CustomRoles.NSerialKiller => NSerialKiller.CanVent.GetBool(),
            CustomRoles.Magician => Magician.CanVent.GetBool(),
            CustomRoles.Reckless => Reckless.CanVent.GetBool(),
            CustomRoles.WeaponMaster => WeaponMaster.CanVent.GetBool(),
            CustomRoles.Postman => Postman.CanVent.GetBool(),
            CustomRoles.Pyromaniac => Pyromaniac.CanVent.GetBool(),
            CustomRoles.Eclipse => Eclipse.CanVent.GetBool(),
            CustomRoles.Vengeance => Vengeance.CanVent.GetBool(),
            CustomRoles.HeadHunter => HeadHunter.CanVent.GetBool(),
            CustomRoles.Imitator => Imitator.CanVent.GetBool(),
            CustomRoles.DarkHide => DarkHide.CanVent.GetBool(),
            CustomRoles.Werewolf => Werewolf.CanRampage(pc.PlayerId) || pc.inVent || Werewolf.IsRampaging(pc.PlayerId),
            CustomRoles.Pestilence => PlagueBearer.PestilenceCanVent.GetBool(),
            CustomRoles.Medusa => Medusa.CanVent.GetBool(),
            CustomRoles.Traitor => Traitor.CanVent.GetBool(),
            //CustomRoles.NWitch => NWitch.CanVent.GetBool(),
            CustomRoles.Maverick => Maverick.CanVent.GetBool(),
            CustomRoles.Jinx => Jinx.CanVent.GetBool(),
            CustomRoles.Pelican => Pelican.CanVent.GetBool(),
            CustomRoles.Gamer => Gamer.CanVent.GetBool(),
            CustomRoles.BloodKnight => BloodKnight.CanVent.GetBool(),
            CustomRoles.Juggernaut => Juggernaut.CanVent.GetBool(),
            CustomRoles.Infectious => Infectious.CanVent.GetBool(),
            CustomRoles.Ritualist => Ritualist.CanVent.GetBool(),
            CustomRoles.Virus => Virus.CanVent.GetBool(),
            CustomRoles.SwordsMan => SwordsMan.CanVent.GetBool(),
            CustomRoles.Pickpocket => Pickpocket.CanVent.GetBool(),
            CustomRoles.Bandit => Bandit.CanVent.GetBool(),
            CustomRoles.HexMaster => true,
            CustomRoles.Wraith => true,
            //   CustomRoles.Chameleon => true,
            CustomRoles.Parasite => true,
            CustomRoles.Refugee => true,
            CustomRoles.Wildling => Wildling.CanVent.GetBool(),
            CustomRoles.Spiritcaller => Spiritcaller.CanVent.GetBool(),

            CustomRoles.Arsonist => pc.IsDouseDone() || (Options.ArsonistCanIgniteAnytime.GetBool() && (Utils.GetDousedPlayerCount(pc.PlayerId).Item1 >= Options.ArsonistMinPlayersToIgnite.GetInt() || pc.inVent)),
            CustomRoles.Revolutionist => pc.IsDrawDone(),

            //SoloKombat
            CustomRoles.KB_Normal => true,
            //FFA
            CustomRoles.Killer => true,

            _ => pc.Is(CustomRoleTypes.Impostor),
        };
    }
    public static bool CanUseSabotage(this PlayerControl pc) // NOTE: THIS IS FOR THE HUD FOR MODDED CLIENTS, THIS DOES NOT DETERMINE WHETHER A ROLE CAN SABOTAGE
    {
        if (!pc.IsAlive() || pc.Data.Role.Role == RoleTypes.GuardianAngel) return false;

        return pc.GetCustomRole() switch
        {
            CustomRoles.Sheriff or
            CustomRoles.Crusader or
            CustomRoles.CopyCat or
            CustomRoles.CursedSoul or
            CustomRoles.Admirer or
            CustomRoles.Amnesiac or
            CustomRoles.Monarch or
            CustomRoles.Deputy or
            CustomRoles.Arsonist or
            CustomRoles.Medusa or
            CustomRoles.SwordsMan or
            //CustomRoles.Reverie or
            CustomRoles.Innocent or
            CustomRoles.Pelican or
            //CustomRoles.Counterfeiter or
            CustomRoles.Aid or
            CustomRoles.Pursuer or
            CustomRoles.Revolutionist or
            CustomRoles.FFF or
            CustomRoles.Medic or
            CustomRoles.Gamer or
            CustomRoles.HexMaster or
            CustomRoles.Wraith or
            CustomRoles.Juggernaut or
            CustomRoles.Jinx or
            CustomRoles.DarkHide or
            CustomRoles.Provocateur or
            CustomRoles.BloodKnight or
            CustomRoles.Poisoner or
            CustomRoles.Pyromaniac or
            CustomRoles.Eclipse or
            CustomRoles.NSerialKiller or
            CustomRoles.PlagueDoctor or
            CustomRoles.Reckless or
            CustomRoles.Postman or
            CustomRoles.Vengeance or
            CustomRoles.HeadHunter or
            CustomRoles.Imitator or
            CustomRoles.Maverick or
            //CustomRoles.NWitch or
            CustomRoles.Ritualist or
            CustomRoles.Totocalcio or
            CustomRoles.Succubus or
            CustomRoles.Infectious or
            CustomRoles.Virus or
            CustomRoles.Farseer or
            CustomRoles.Pickpocket or
            CustomRoles.PlagueBearer or
            CustomRoles.Pestilence or
            CustomRoles.Romantic or
            CustomRoles.VengefulRomantic or
            CustomRoles.RuthlessRomantic or
            CustomRoles.Werewolf or
            CustomRoles.Spiritcaller
            => false,

            CustomRoles.Jackal => Jackal.CanUseSabotage.GetBool(),
            CustomRoles.Sidekick => Jackal.CanUseSabotageSK.GetBool(),
            CustomRoles.Traitor => Traitor.CanUseSabotage.GetBool(),
            CustomRoles.Parasite => true,
            CustomRoles.Glitch => true,
            CustomRoles.Refugee => true,
            CustomRoles.Magician => true,
            CustomRoles.WeaponMaster => true,


            _ => pc.Is(CustomRoleTypes.Impostor),
        };
    }
    public static bool IsDousedPlayer(this PlayerControl arsonist, PlayerControl target)
    {
        if (arsonist == null || target == null || Main.isDoused == null) return false;
        Main.isDoused.TryGetValue((arsonist.PlayerId, target.PlayerId), out bool isDoused);
        return isDoused;
    }
    public static bool IsDrawPlayer(this PlayerControl arsonist, PlayerControl target)
    {
        if (arsonist == null && target == null && Main.isDraw == null) return false;
        Main.isDraw.TryGetValue((arsonist.PlayerId, target.PlayerId), out bool isDraw);
        return isDraw;
    }
    public static bool IsRevealedPlayer(this PlayerControl player, PlayerControl target)
    {
        if (player == null || target == null || Main.isRevealed == null) return false;
        Main.isRevealed.TryGetValue((player.PlayerId, target.PlayerId), out bool isDoused);
        return isDoused;
    }
    public static void RpcSetDousedPlayer(this PlayerControl player, PlayerControl target, bool isDoused)
    {
        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SetDousedPlayer, SendOption.Reliable, -1);//RPCによる同期
        writer.Write(player.PlayerId);
        writer.Write(target.PlayerId);
        writer.Write(isDoused);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }
    public static void RpcSetDrawPlayer(this PlayerControl player, PlayerControl target, bool isDoused)
    {
        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SetDrawPlayer, SendOption.Reliable, -1);//RPCによる同期
        writer.Write(player.PlayerId);
        writer.Write(target.PlayerId);
        writer.Write(isDoused);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }
    public static void RpcSetRevealtPlayer(this PlayerControl player, PlayerControl target, bool isDoused)
    {
        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SetRevealedPlayer, SendOption.Reliable, -1);//RPCによる同期
        writer.Write(player.PlayerId);
        writer.Write(target.PlayerId);
        writer.Write(isDoused);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }
    public static void ResetKillCooldown(this PlayerControl player)
    {
        Main.AllPlayerKillCooldown[player.PlayerId] = Options.DefaultKillCooldown; //キルクールをデフォルトキルクールに変更
        switch (player.GetCustomRole())
        {
            case CustomRoles.SerialKiller:
                SerialKiller.ApplyKillCooldown(player.PlayerId); //シリアルキラーはシリアルキラーのキルクールに。
                break;
            case CustomRoles.TimeThief:
                TimeThief.SetKillCooldown(player.PlayerId); //タイムシーフはタイムシーフのキルクールに。
                break;
            /*    case CustomRoles.Mare:
                    Mare.SetKillCooldown(player.PlayerId);
                    break; */
            case CustomRoles.EvilDiviner:
                EvilDiviner.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Jailor:
                Jailor.SetKillCooldown(player.PlayerId); //シリアルキラーはシリアルキラーのキルクールに。
                break;
            case CustomRoles.Morphling:
                Morphling.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Ritualist:
                Ritualist.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Pickpocket:
                Pickpocket.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Agitater:
                Agitater.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Arsonist:
                Main.AllPlayerKillCooldown[player.PlayerId] = Options.ArsonistCooldown.GetFloat(); //アーソニストはアーソニストのキルクールに。
                break;
            case CustomRoles.Inhibitor:
                Main.AllPlayerKillCooldown[player.PlayerId] = Options.InhibitorCDAfterMeetings.GetFloat();
                break;
            case CustomRoles.Chronomancer:
                Chronomancer.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Nullifier:
                Nullifier.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Stealth:
                Stealth.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Sapper:
                Main.AllPlayerKillCooldown[player.PlayerId] = 300f;
                break;
            case CustomRoles.Hitman:
                Main.AllPlayerKillCooldown[player.PlayerId] = Hitman.KillCooldown.GetFloat();
                break;
            case CustomRoles.Mastermind:
                Main.AllPlayerKillCooldown[player.PlayerId] = Mastermind.KillCooldown.GetFloat();
                break;
            case CustomRoles.Gambler:
                Main.AllPlayerKillCooldown[player.PlayerId] = Gambler.KillCooldown.GetFloat();
                break;
            case CustomRoles.RiftMaker:
                Main.AllPlayerKillCooldown[player.PlayerId] = RiftMaker.KillCooldown.GetFloat();
                break;
            case CustomRoles.Puppeteer:
                Main.AllPlayerKillCooldown[player.PlayerId] = Options.PuppeteerKCD.GetFloat();
                break;
            case CustomRoles.Saboteur:
                Main.AllPlayerKillCooldown[player.PlayerId] = Options.SaboteurCDAfterMeetings.GetFloat();
                break;
            case CustomRoles.Revolutionist:
                Main.AllPlayerKillCooldown[player.PlayerId] = Options.RevolutionistCooldown.GetFloat();
                break;
            case CustomRoles.Underdog:
                Main.AllPlayerKillCooldown[player.PlayerId] = Options.UnderdogKillCooldownWithMorePlayersAlive.GetFloat();
                break;
            case CustomRoles.Jackal:
                Jackal.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Sidekick:
                Sidekick.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.PlagueBearer:
                PlagueBearer.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Bandit:
                Bandit.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Pestilence:
                PlagueBearer.SetKillCooldownPestilence(player.PlayerId);
                break;

            case CustomRoles.Councillor:
                Councillor.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.HexMaster:
            case CustomRoles.Wraith:
                Main.AllPlayerKillCooldown[player.PlayerId] = Options.DefaultKillCooldown;
                break;
            case CustomRoles.Parasite:
                Main.AllPlayerKillCooldown[player.PlayerId] = Options.ParasiteCD.GetFloat();
                break;
            case CustomRoles.Refugee:
                Main.AllPlayerKillCooldown[player.PlayerId] = Amnesiac.RefugeeKillCD.GetFloat();
                break;
            case CustomRoles.NSerialKiller:
                NSerialKiller.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.PlagueDoctor:
                PlagueDoctor.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Penguin:
                Penguin.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.WeaponMaster:
                WeaponMaster.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Magician:
                Magician.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Reckless:
                Reckless.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Postman:
                Postman.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.HeadHunter:
                Main.AllPlayerKillCooldown[player.PlayerId] = HeadHunter.KCD;
                break;
            case CustomRoles.Pyromaniac:
                Pyromaniac.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Eclipse:
                Eclipse.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Vengeance:
                Vengeance.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Imitator:
                Imitator.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Werewolf:
                Werewolf.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Traitor:
                Traitor.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Glitch:
                Glitch.SetKillCooldown(player.PlayerId);
                break;
            //case CustomRoles.NWitch:
            //    NWitch.SetKillCooldown(player.PlayerId);
            //    break;
            case CustomRoles.Maverick:
                Maverick.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Jinx:
                Jinx.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Poisoner:
                Poisoner.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Sheriff:
                Sheriff.SetKillCooldown(player.PlayerId); //シェリフはシェリフのキルクールに。
                break;
            case CustomRoles.CopyCat:
                CopyCat.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Minimalism:
                Main.AllPlayerKillCooldown[player.PlayerId] = Options.MNKillCooldown.GetFloat();
                break;
            case CustomRoles.SwordsMan:
                SwordsMan.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Zombie:
                Main.AllPlayerKillCooldown[player.PlayerId] = Options.ZombieKillCooldown.GetFloat();
                Main.AllPlayerSpeed[player.PlayerId] -= Options.ZombieSpeedReduce.GetFloat();
                break;
            case CustomRoles.BoobyTrap:
                Main.AllPlayerKillCooldown[player.PlayerId] = Options.BTKillCooldown.GetFloat();
                break;
            case CustomRoles.Scavenger:
                Main.AllPlayerKillCooldown[player.PlayerId] = Options.ScavengerKillCooldown.GetFloat();
                break;
            case CustomRoles.Bomber:
                if (Options.BomberCanKill.GetBool())
                    Main.AllPlayerKillCooldown[player.PlayerId] = Options.BomberKillCD.GetFloat();
                else
                    Main.AllPlayerKillCooldown[player.PlayerId] = 300f;
                break;
            case CustomRoles.Nuker:
                Main.AllPlayerKillCooldown[player.PlayerId] = 300f;
                break;
            case CustomRoles.Capitalism:
                Main.AllPlayerKillCooldown[player.PlayerId] = Options.CapitalismKillCooldown.GetFloat();
                break;
            case CustomRoles.Pelican:
                Main.AllPlayerKillCooldown[player.PlayerId] = Pelican.KillCooldown.GetFloat();
                break;
            //case CustomRoles.Counterfeiter:
            //    Counterfeiter.SetKillCooldown(player.PlayerId);
            //    break;
            case CustomRoles.Aid:
                Aid.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Witness:
                Main.AllPlayerKillCooldown[player.PlayerId] = Options.WitnessCD.GetFloat();
                break;
            case CustomRoles.Pursuer:
                Pursuer.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.FFF:
                Main.AllPlayerKillCooldown[player.PlayerId] = 15f;
                break;
            case CustomRoles.Medusa:
                Medusa.SetKillCooldown(player.PlayerId);
                break;

            case CustomRoles.Cleaner:
                Main.AllPlayerKillCooldown[player.PlayerId] = Options.CleanerKillCooldown.GetFloat();
                break;
            case CustomRoles.Medic:
                Medic.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Gamer:
                Gamer.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.BallLightning:
                BallLightning.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.DarkHide:
                DarkHide.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Greedier:
                Greedier.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.QuickShooter:
                QuickShooter.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Provocateur:
                Main.AllPlayerKillCooldown[player.PlayerId] = 15f;
                break;
            case CustomRoles.Assassin:
                Assassin.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Undertaker:
                Undertaker.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Sans:
                Sans.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Juggernaut:
                Juggernaut.SetKillCooldown(player.PlayerId);
                break;
            //case CustomRoles.Reverie:
            //    Reverie.SetKillCooldown(player.PlayerId);
            //    break;
            case CustomRoles.Hacker:
                Hacker.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.KB_Normal:
                Main.AllPlayerKillCooldown[player.PlayerId] = SoloKombatManager.KB_ATKCooldown.GetFloat();
                break;
            case CustomRoles.Killer:
                Main.AllPlayerKillCooldown[player.PlayerId] = FFAManager.FFA_KCD.GetFloat();
                break;
            case CustomRoles.BloodKnight:
                BloodKnight.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Totocalcio:
                Totocalcio.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Romantic:
                Romantic.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.VengefulRomantic:
                Main.AllPlayerKillCooldown[player.PlayerId] = Romantic.VengefulKCD.GetFloat();
                break;
            case CustomRoles.RuthlessRomantic:
                Main.AllPlayerKillCooldown[player.PlayerId] = Romantic.RuthlessKCD.GetFloat();
                break;
            case CustomRoles.Gangster:
                Gangster.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Succubus:
                Succubus.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.CursedSoul:
                CursedSoul.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Admirer:
                Admirer.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Amnesiac:
                Amnesiac.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Infectious:
                Infectious.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Monarch:
                Monarch.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Deputy:
                Deputy.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Virus:
                Virus.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Farseer:
                Farseer.SetCooldown(player.PlayerId);
                break;
            case CustomRoles.Dazzler:
                Dazzler.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Deathpact:
                Deathpact.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Devourer:
                Devourer.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Spiritcaller:
                Spiritcaller.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Lurker:
                Lurker.SetKillCooldown(player.PlayerId);
                break;
            case CustomRoles.Crusader:
                Crusader.SetKillCooldown(player.PlayerId);
                break;
        }
        if (player.PlayerId == LastImpostor.currentId)
            LastImpostor.SetKillCooldown();
        if (player.Is(CustomRoles.Mare))
            if (Utils.IsActive(SystemTypes.Electrical)) Main.AllPlayerKillCooldown[player.PlayerId] = Options.MareKillCD.GetFloat();
            else Main.AllPlayerKillCooldown[player.PlayerId] = Options.MareKillCDNormally.GetFloat();

        if (Main.KilledDiseased.ContainsKey(player.PlayerId))
        {
            Main.AllPlayerKillCooldown[player.PlayerId] = Main.AllPlayerKillCooldown[player.PlayerId] + Main.KilledDiseased[player.PlayerId] * Options.DiseasedCDOpt.GetFloat();
            Logger.Info($"kill cd of player set to {Main.AllPlayerKillCooldown[player.PlayerId]}", "Diseased");
        }
        if (Main.KilledAntidote.ContainsKey(player.PlayerId))
        {
            var kcd = Main.AllPlayerKillCooldown[player.PlayerId] - Main.KilledAntidote[player.PlayerId] * Options.AntidoteCDOpt.GetFloat();
            if (kcd < 0) kcd = 0;
            Main.AllPlayerKillCooldown[player.PlayerId] = kcd;
            Logger.Info($"kill cd of player set to {Main.AllPlayerKillCooldown[player.PlayerId]}", "Antidote");
        }
    }
    public static void TrapperKilled(this PlayerControl killer, PlayerControl target)
    {
        Logger.Info($"{target?.Data?.PlayerName}はTrapperだった", "Trapper");
        var tmpSpeed = Main.AllPlayerSpeed[killer.PlayerId];
        Main.AllPlayerSpeed[killer.PlayerId] = Main.MinSpeed;    //tmpSpeedで後ほど値を戻すので代入しています。
        ReportDeadBodyPatch.CanReport[killer.PlayerId] = false;
        killer.MarkDirtySettings();
        _ = new LateTask(() =>
        {
            Main.AllPlayerSpeed[killer.PlayerId] = Main.AllPlayerSpeed[killer.PlayerId] - Main.MinSpeed + tmpSpeed;
            ReportDeadBodyPatch.CanReport[killer.PlayerId] = true;
            killer.MarkDirtySettings();
            RPC.PlaySoundRPC(killer.PlayerId, Sounds.TaskComplete);
        }, Options.TrapperBlockMoveTime.GetFloat(), "Trapper BlockMove");
    }
    public static bool IsDouseDone(this PlayerControl player)
    {
        if (!player.Is(CustomRoles.Arsonist)) return false;
        var count = Utils.GetDousedPlayerCount(player.PlayerId);
        return count.Item1 >= count.Item2;
    }
    public static bool IsDrawDone(this PlayerControl player) //Determine whether the conditions to win are met
    {
        if (!player.Is(CustomRoles.Revolutionist)) return false;
        var count = Utils.GetDrawPlayerCount(player.PlayerId, out var _);
        return count.Item1 >= count.Item2;
    }
    public static void RpcExileV2(this PlayerControl player)
    {
        player.Exiled();
        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(player.NetId, (byte)RpcCalls.Exiled, SendOption.None, -1);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }
    public static void Kill(this PlayerControl killer, PlayerControl target)
    {
        //Used for TOHE's pre-kill judgment

        if (Options.CurrentGameMode == CustomGameMode.SoloKombat) return;

        if (killer.PlayerId == target.PlayerId && killer.shapeshifting)
        {
            _ = new LateTask(() => { killer.RpcMurderPlayer(target, true); }, 1.5f, "Shapeshifting Suicide Delay");
            return;
        }

        killer.RpcMurderPlayer(target, true);
    }
    //public static void RpcMurderPlayerV2(this PlayerControl killer, PlayerControl target)
    //{
    //    if (target == null) target = killer;
    //    if (AmongUsClient.Instance.AmClient)
    //    {
    //        killer.MurderPlayer(target, ResultFlags);
    //    }
    //    MessageWriter messageWriter = AmongUsClient.Instance.StartRpcImmediately(killer.NetId, (byte)RpcCalls.MurderPlayer, SendOption.None, -1);
    //    messageWriter.WriteNetObject(target);
    //    messageWriter.Write((byte)ResultFlags);
    //    AmongUsClient.Instance.FinishRpcImmediately(messageWriter);

    //    Utils.NotifyRoles(SpecifySeer: killer);
    //    Utils.NotifyRoles(SpecifySeer: target);
    //}
    public static bool RpcCheckAndMurder(this PlayerControl killer, PlayerControl target, bool check = false) => CheckMurderPatch.RpcCheckAndMurder(killer, target, check);
    public static void NoCheckStartMeeting(this PlayerControl reporter, GameData.PlayerInfo target, bool force = false)
    { /*サボタージュ中でも関係なしに会議を起こせるメソッド
        targetがnullの場合はボタンとなる*/
        if (Options.DisableMeeting.GetBool() && !force) return;
        ReportDeadBodyPatch.AfterReportTasks(reporter, target);
        MeetingRoomManager.Instance.AssignSelf(reporter, target);
        DestroyableSingleton<HudManager>.Instance.OpenMeetingRoom(reporter);
        reporter.RpcStartMeeting(target);
    }
    public static bool IsModClient(this PlayerControl player) => Main.playerVersion.ContainsKey(player.PlayerId);
    ///<summary>
    ///プレイヤーのRoleBehaviourのGetPlayersInAbilityRangeSortedを実行し、戻り値を返します。
    ///</summary>
    ///<param name="ignoreColliders">trueにすると、壁の向こう側のプレイヤーが含まれるようになります。守護天使用</param>
    ///<returns>GetPlayersInAbilityRangeSortedの戻り値</returns>
    public static List<PlayerControl> GetPlayersInAbilityRangeSorted(this PlayerControl player, bool ignoreColliders = false) => GetPlayersInAbilityRangeSorted(player, pc => true, ignoreColliders);
    ///<summary>
    ///プレイヤーのRoleBehaviourのGetPlayersInAbilityRangeSortedを実行し、predicateの条件に合わないものを除外して返します。
    ///</summary>
    ///<param name="predicate">リストに入れるプレイヤーの条件 このpredicateに入れてfalseを返すプレイヤーは除外されます。</param>
    ///<param name="ignoreColliders">trueにすると、壁の向こう側のプレイヤーが含まれるようになります。守護天使用</param>
    ///<returns>GetPlayersInAbilityRangeSortedの戻り値から条件に合わないプレイヤーを除外したもの。</returns>
    public static List<PlayerControl> GetPlayersInAbilityRangeSorted(this PlayerControl player, Predicate<PlayerControl> predicate, bool ignoreColliders = false)
    {
        var rangePlayersIL = RoleBehaviour.GetTempPlayerList();
        List<PlayerControl> rangePlayers = new();
        player.Data.Role.GetPlayersInAbilityRangeSorted(rangePlayersIL, ignoreColliders);
        foreach (var pc in rangePlayersIL)
        {
            if (predicate(pc)) rangePlayers.Add(pc);
        }
        return rangePlayers;
    }
    public static bool IsNeutralKiller(this PlayerControl player) => player.GetCustomRole().IsNK();
    public static bool IsNeutralBenign(this PlayerControl player) => player.GetCustomRole().IsNB();
    public static bool IsNeutralEvil(this PlayerControl player) => player.GetCustomRole().IsNC();
    public static bool IsNeutralChaos(this PlayerControl player) => player.GetCustomRole().IsNC();
    public static bool IsNonNeutralKiller(this PlayerControl player) => player.GetCustomRole().IsNonNK();
    public static bool IsSnitchTarget(this PlayerControl player) => player.GetCustomRole().IsSnitchTarget();
    public static bool KnowDeathReason(this PlayerControl seer, PlayerControl target)
        => (seer.Is(CustomRoles.Doctor) || seer.Is(CustomRoles.Autopsy)
        || (seer.Data.IsDead && Options.GhostCanSeeDeathReason.GetBool()))
        && target.Data.IsDead || target.Is(CustomRoles.Gravestone) && target.Data.IsDead;
    public static bool KnowDeadTeam(this PlayerControl seer, PlayerControl target)
        => seer.Is(CustomRoles.Necroview)
        && target.Data.IsDead;

    public static bool KnowLivingTeam(this PlayerControl seer, PlayerControl target)
        => seer.Is(CustomRoles.Visionary)
        && !target.Data.IsDead;
    public static string GetRoleInfo(this PlayerControl player, bool InfoLong = false)
    {
        var role = player.GetCustomRole();
        if (role is CustomRoles.Crewmate or CustomRoles.Impostor)
            InfoLong = false;

        var text = role.ToString();

        var Prefix = string.Empty;
        if (!InfoLong)
            switch (role)
            {
                case CustomRoles.Mafia:
                    Prefix = Utils.CanMafiaKill() ? "After" : "Before";
                    break;
            };
        var Info = (role.IsVanilla() ? "Blurb" : "Info") + (InfoLong ? "Long" : string.Empty);
        return GetString($"{Prefix}{text}{Info}");
    }
    public static void SetRealKiller(this PlayerControl target, PlayerControl killer, bool NotOverRide = false)
    {
        if (target == null)
        {
            Logger.Info("target=null", "SetRealKiller");
            return;
        }
        var State = Main.PlayerStates[target.PlayerId];
        if (State.RealKiller.Item1 != DateTime.MinValue && NotOverRide) return; //既に値がある場合上書きしない
        byte killerId = killer == null ? byte.MaxValue : killer.PlayerId;
        RPC.SetRealKiller(target.PlayerId, killerId);
    }
    public static PlayerControl GetRealKiller(this PlayerControl target)
    {
        var killerId = Main.PlayerStates[target.PlayerId].GetRealKiller();
        return killerId == byte.MaxValue ? null : Utils.GetPlayerById(killerId);
    }
    public static PlainShipRoom GetPlainShipRoom(this PlayerControl pc)
    {
        if (!pc.IsAlive() || Pelican.IsEaten(pc.PlayerId)) return null;
        var Rooms = ShipStatus.Instance.AllRooms.ToArray();
        if (Rooms == null) return null;
        foreach (PlainShipRoom room in Rooms)
        {
            if (!room.roomArea) continue;
            if (pc.Collider.IsTouching(room.roomArea))
                return room;
        }
        return null;
    }
    public static Dictionary<string, int> GetAllPlayerLocationsCount()
    {
        Dictionary<string, int> playerRooms = new();
        foreach (PlayerControl pc in Main.AllAlivePlayerControls)
        {
            if (!pc.IsAlive() || Pelican.IsEaten(pc.PlayerId)) return null;
            var Rooms = ShipStatus.Instance.AllRooms.ToArray();
            if (Rooms == null) return null;
            foreach (PlainShipRoom room in Rooms)
            {
                if (!room.roomArea) continue;
                if (pc.Collider.IsTouching(room.roomArea))
                {
                    if (playerRooms.ContainsKey(room.name)) playerRooms[room.name]++;
                    else playerRooms.Add(room.name, 1);
                }
            }
        }
        if (playerRooms.Any()) return playerRooms;
        else return null;
    }

    //汎用
    public static bool Is(this PlayerControl target, CustomRoles role) =>
        role > CustomRoles.NotAssigned ? target.GetCustomSubRoles().Contains(role) : target.GetCustomRole() == role;
    public static bool Is(this PlayerControl target, CustomRoleTypes type) { return target.GetCustomRole().GetCustomRoleTypes() == type; }
    public static bool Is(this PlayerControl target, RoleTypes type) { return target.GetCustomRole().GetRoleTypes() == type; }
    public static bool Is(this PlayerControl target, CountTypes type) { return target.GetCountTypes() == type; }
    public static bool IsAlive(this PlayerControl target)
    {
        //ロビーなら生きている
        //targetがnullならば切断者なので生きていない
        //targetがnullでなく取得できない場合は登録前なので生きているとする
        if (target == null || target.Is(CustomRoles.GM)) return false;
        return GameStates.IsLobby || (target != null && (!Main.PlayerStates.TryGetValue(target.PlayerId, out var ps) || !ps.IsDead));
    }
    public static bool IsExiled(this PlayerControl target)
    {
        return GameStates.InGame || (target != null && (Main.PlayerStates[target.PlayerId].deathReason == PlayerState.DeathReason.Vote));
    }

    ///<summary>Is the player currently protected</summary>
    public static bool IsProtected(this PlayerControl self) => self.protectedByGuardianId > -1;

    public const MurderResultFlags ResultFlags = MurderResultFlags.Succeeded | MurderResultFlags.DecisionByHost;
}