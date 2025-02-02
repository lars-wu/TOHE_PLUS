using Hazel;
using System.Collections.Generic;
using System.Linq;
using static TOHE.Options;
using static TOHE.Translator;

namespace TOHE.Roles.Crewmate
{
    internal class Spiritualist
    {
        private static readonly int Id = 8100;

        private static List<byte> playerIdList = new();

        public static OptionItem ShowGhostArrowEverySeconds;
        public static OptionItem ShowGhostArrowForSeconds;

        private static Dictionary<byte, long> ShowGhostArrowUntil = new();
        private static Dictionary<byte, long> LastGhostArrowShowTime = new();

        public static byte SpiritualistTarget;

        public static void SetupCustomOption()
        {
            SetupRoleOptions(Id, TabGroup.CrewmateRoles, CustomRoles.Spiritualist);
            ShowGhostArrowEverySeconds = FloatOptionItem.Create(Id + 10, "SpiritualistShowGhostArrowEverySeconds", new(1f, 60f, 1f), 15f, TabGroup.CrewmateRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Spiritualist])
                .SetValueFormat(OptionFormat.Seconds);
            ShowGhostArrowForSeconds = FloatOptionItem.Create(Id + 11, "SpiritualistShowGhostArrowForSeconds", new(1f, 60f, 1f), 2f, TabGroup.CrewmateRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Spiritualist])
                .SetValueFormat(OptionFormat.Seconds);
        }
        public static void Init()
        {
            playerIdList = new();
            SpiritualistTarget = new();
            LastGhostArrowShowTime = new();
            ShowGhostArrowUntil = new();
        }
        public static void Add(byte playerId)
        {
            playerIdList.Add(playerId);
            SpiritualistTarget = byte.MaxValue;
            LastGhostArrowShowTime.Add(playerId, 0);
            ShowGhostArrowUntil.Add(playerId, 0);
        }
        public static bool IsEnable => playerIdList.Any();

        private static bool ShowArrow(byte playerId)
        {
            long timestamp = Utils.GetTimeStamp();

            if (LastGhostArrowShowTime[playerId] == 0 || LastGhostArrowShowTime[playerId] + (long)ShowGhostArrowEverySeconds.GetFloat() <= timestamp)
            {
                LastGhostArrowShowTime[playerId] = timestamp;
                ShowGhostArrowUntil[playerId] = timestamp + (long)ShowGhostArrowForSeconds.GetFloat();
                return true;
            }
            else if (ShowGhostArrowUntil[playerId] >= timestamp)
            {
                return true;
            }

            return false;
        }

        public static void OnReportDeadBody(GameData.PlayerInfo target)
        {
            if (target == null)
            {
                return;
            }

            if (SpiritualistTarget != byte.MaxValue)
                RemoveTarget();

            SpiritualistTarget = target.PlayerId;
        }

        public static void AfterMeetingTasks()
        {
            foreach (byte spiritualist in playerIdList.ToArray())
            {
                PlayerControl player = Main.AllPlayerControls.FirstOrDefault(a => a.PlayerId == spiritualist);
                if (!player.IsAlive())
                {
                    continue;
                }

                LastGhostArrowShowTime[spiritualist] = 0;
                ShowGhostArrowUntil[spiritualist] = 0;

                PlayerControl target = Main.AllPlayerControls.FirstOrDefault(a => a.PlayerId == SpiritualistTarget);
                if (target == null)
                {
                    continue;
                }

                target.Notify("<color=#ffff00>The Spiritualist has an arrow pointing toward you</color>");

                TargetArrow.Add(spiritualist, target.PlayerId);

                var writer = CustomRpcSender.Create("SpiritualistSendMessage", SendOption.None);
                writer.StartMessage(target.GetClientId());
                writer.StartRpc(target.NetId, (byte)RpcCalls.SetName)
                    .Write(GetString("SpiritualistNoticeTitle"))
                    .EndRpc();
                writer.StartRpc(target.NetId, (byte)RpcCalls.SendChat)
                    .Write(GetString("SpiritualistNoticeMessage"))
                    .EndRpc();
                writer.StartRpc(target.NetId, (byte)RpcCalls.SetName)
                    .Write(target.Data.PlayerName)
                    .EndRpc();
                writer.EndMessage();
                writer.SendMessage();
            }
        }

        public static string GetSpiritualistArrow(PlayerControl seer, PlayerControl target = null)
        {
            if (!seer.Is(CustomRoles.Spiritualist) || !seer.IsAlive()) return string.Empty;
            if (target != null && seer.PlayerId != target.PlayerId) return string.Empty;
            if (GameStates.IsMeeting) return string.Empty;
            if (SpiritualistTarget != byte.MaxValue && ShowArrow(seer.PlayerId))
            {
                return Utils.ColorString(seer.GetRoleColor(), TargetArrow.GetArrows(seer, SpiritualistTarget));
            }
            return string.Empty;
        }

        public static void RemoveTarget()
        {
            foreach (byte spiritualist in playerIdList.ToArray())
            {
                TargetArrow.Remove(spiritualist, SpiritualistTarget);
            }

            SpiritualistTarget = byte.MaxValue;
        }
    }
}