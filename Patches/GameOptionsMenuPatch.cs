using AmongUs.GameOptions;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static TOHE.Translator;
using Object = UnityEngine.Object;

namespace TOHE;

[HarmonyPatch(typeof(GameSettingMenu), nameof(GameSettingMenu.InitializeOptions))]
public static class GameSettingMenuPatch
{
    public static void Prefix(GameSettingMenu __instance)
    {
        // Unlocks map/impostor amount changing in online (for testing on your custom servers)
        // オンラインモードで部屋を立て直さなくてもマップを変更できるように変更
        __instance.HideForOnline = new Il2CppReferenceArray<Transform>(0);
    }
}

[HarmonyPatch(typeof(GameOptionsMenu), nameof(GameOptionsMenu.Start))]
[HarmonyPriority(Priority.First)]
public static class GameOptionsMenuPatch
{
    public static void Postfix(GameOptionsMenu __instance)
    {
        foreach (OptionBehaviour ob in __instance.Children.ToArray())
        {
            switch (ob.Title)
            {
                case StringNames.GameVotingTime:
                    ob.Cast<NumberOption>().ValidRange = new FloatRange(0, 600);
                    break;
                case StringNames.GameShortTasks:
                case StringNames.GameLongTasks:
                case StringNames.GameCommonTasks:
                    ob.Cast<NumberOption>().ValidRange = new FloatRange(0, 99);
                    break;
                case StringNames.GameKillCooldown:
                    ob.Cast<NumberOption>().ValidRange = new FloatRange(0, 180);
                    break;
                default:
                    break;
            }
        }
        var template = Object.FindObjectsOfType<StringOption>().FirstOrDefault();
        if (template == null) return;

        var Tint = GameObject.Find("Tint");
        Tint?.SetActive(false);

        var gameSettings = GameObject.Find("Game Settings");
        if (gameSettings == null) return;
        gameSettings.transform.FindChild("GameGroup").GetComponent<Scroller>().ScrollWheelSpeed = 1.3f;

        var gameSettingMenu = Object.FindObjectsOfType<GameSettingMenu>().FirstOrDefault();
        if (gameSettingMenu == null) return;
        List<GameObject> menus = new() { gameSettingMenu.RegularGameSettings, gameSettingMenu.RolesSettings.gameObject };
        List<SpriteRenderer> highlights = new() { gameSettingMenu.GameSettingsHightlight, gameSettingMenu.RolesSettingsHightlight };

        var roleTab = GameObject.Find("RoleTab");
        var gameTab = GameObject.Find("GameTab");
        List<GameObject> tabs = new() { gameTab, roleTab };

        System.Collections.IList list = Enum.GetValues(typeof(TabGroup));
        for (int i = 0; i < list.Count; i++)
        {
            object tab = list[i];
            var obj = gameSettings.transform.parent.Find(tab + "Tab");
            if (obj != null)
            {
                obj.transform.FindChild("../../GameGroup/Text").GetComponent<TMPro.TextMeshPro>().SetText(GetString("TabGroup." + tab));
                continue;
            }

            var tohSettings = Object.Instantiate(gameSettings, gameSettings.transform.parent);
            tohSettings.name = tab + "Tab";
            tohSettings.transform.FindChild("BackPanel").transform.localScale =
            tohSettings.transform.FindChild("Bottom Gradient").transform.localScale = new Vector3(1.6f, 1f, 1f);
            tohSettings.transform.FindChild("BackPanel").transform.localPosition += new Vector3(0.2f, 0f, 0f);
            tohSettings.transform.FindChild("Bottom Gradient").transform.localPosition += new Vector3(0.2f, 0f, 0f);
            tohSettings.transform.FindChild("Background").transform.localScale = new Vector3(1.8f, 1f, 1f);
            tohSettings.transform.FindChild("UI_Scrollbar").transform.localPosition += new Vector3(1.4f, 0f, 0f);
            tohSettings.transform.FindChild("UI_ScrollbarTrack").transform.localPosition += new Vector3(1.4f, 0f, 0f);
            tohSettings.transform.FindChild("GameGroup/SliderInner").transform.localPosition += new Vector3(-0.3f, 0f, 0f);
            var tohMenu = tohSettings.transform.FindChild("GameGroup/SliderInner").GetComponent<GameOptionsMenu>();

            //OptionBehaviourを破棄
            tohMenu.GetComponentsInChildren<OptionBehaviour>().Do(x => Object.Destroy(x.gameObject));

            var scOptions = new List<OptionBehaviour>();
            foreach (OptionItem option in OptionItem.AllOptions.ToArray())
            {
                if (option.Tab != (TabGroup)tab) continue;
                if (option.OptionBehaviour == null)
                {
                    float yoffset = option.IsText ? 100f : 0f;
                    var stringOption = Object.Instantiate(template, tohMenu.transform);
                    scOptions.Add(stringOption);
                    stringOption.OnValueChanged = new Action<OptionBehaviour>((o) => { });
                    stringOption.TitleText.text = option.Name;
                    stringOption.Value = stringOption.oldValue = option.CurrentValue;
                    stringOption.ValueText.text = option.GetString();
                    stringOption.name = option.Name;
                    stringOption.transform.FindChild("Background").localScale = new Vector3(1.6f, 1f, 1f);
                    stringOption.transform.FindChild("Plus_TMP").localPosition += new Vector3(1.4f, yoffset, 0f);
                    stringOption.transform.FindChild("Minus_TMP").localPosition += new Vector3(1.0f, yoffset, 0f);
                    stringOption.transform.FindChild("Value_TMP").localPosition += new Vector3(1.2f, yoffset, 0f);
                    stringOption.transform.FindChild("Value_TMP").GetComponent<RectTransform>().sizeDelta = new Vector2(1.6f, 0.26f);
                    stringOption.transform.FindChild("Title_TMP").localPosition += new Vector3(option.IsText ? 0.25f : 0.1f, option.IsText ? -0.1f : 0f, 0f);
                    stringOption.transform.FindChild("Title_TMP").GetComponent<RectTransform>().sizeDelta = new Vector2(5.5f, 0.37f);

                    option.OptionBehaviour = stringOption;
                }
                option.OptionBehaviour.gameObject.SetActive(true);
            }
            tohMenu.Children = scOptions.ToArray();
            tohSettings.gameObject.SetActive(false);
            menus.Add(tohSettings.gameObject);

            var tohTab = Object.Instantiate(roleTab, roleTab.transform.parent);
            tohTab.transform.FindChild("Hat Button").FindChild("Icon").GetComponent<SpriteRenderer>().sprite = Utils.LoadSprite($"TOHE.Resources.Images.TabIcon_{tab}.png", 100f);
            tabs.Add(tohTab);
            var tohTabHighlight = tohTab.transform.FindChild("Hat Button").FindChild("Tab Background").GetComponent<SpriteRenderer>();
            highlights.Add(tohTabHighlight);
        }

        for (var i = 0; i < tabs.Count; i++)
        {
            tabs[i].transform.localPosition = new(0.8f * (i - 1) - tabs.Count / 3f, tabs[i].transform.localPosition.y, tabs[i].transform.localPosition.z);
            var button = tabs[i].GetComponentInChildren<PassiveButton>();
            if (button == null) continue;
            var copiedIndex = i;
            button.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            Action value = () =>
            {
                for (var j = 0; j < menus.Count; j++)
                {
                    menus[j].SetActive(j == copiedIndex);
                    highlights[j].enabled = j == copiedIndex;
                }
            };
            button.OnClick.AddListener(value);
        }
    }
}

[HarmonyPatch(typeof(GameOptionsMenu), nameof(GameOptionsMenu.Update))]
public class GameOptionsMenuUpdatePatch
{
    private static float _timer = 1f;

    public static void Postfix(GameOptionsMenu __instance)
    {
        if (__instance.transform.parent.parent.name == "Game Settings") return;
        System.Collections.IList list = Enum.GetValues(typeof(TabGroup));
        for (int i = 0; i < list.Count; i++)
        {
            object tab = list[i];
            string tabcolor = tab switch
            {
                TabGroup.SystemSettings => Main.ModColor,
                TabGroup.GameSettings => "#59ef83",
                TabGroup.TaskSettings => "#EF59AF",
                TabGroup.ImpostorRoles => "#f74631",
                TabGroup.CrewmateRoles => "#8cffff",
                TabGroup.NeutralRoles => "#7f8c8d",
                TabGroup.Addons => "#ff9ace",
                TabGroup.OtherRoles => "#76b8e0",
                //      TabGroup.CovenRoles => "#8e44ad",
                _ => "#ffffff",
            };
            if (__instance.transform.parent.parent.name != tab + "Tab") continue;
            __instance.transform.FindChild("../../GameGroup/Text").GetComponent<TMPro.TextMeshPro>().SetText($"<color={tabcolor}>" + GetString("TabGroup." + tab) + "</color>");

            _timer += Time.deltaTime;
            if (_timer < 0.1f) return;
            _timer = 0f;

            float numItems = __instance.Children.Length;
            var offset = 2.7f;

            foreach (OptionItem option in OptionItem.AllOptions.ToArray())
            {
                if ((TabGroup)tab != option.Tab) continue;
                if (option?.OptionBehaviour == null || option.OptionBehaviour.gameObject == null) continue;

                var enabled = true;
                var parent = option.Parent;

                enabled = AmongUsClient.Instance.AmHost &&
                    !option.IsHiddenOn(Options.CurrentGameMode);

                var opt = option.OptionBehaviour.transform.Find("Background").GetComponent<SpriteRenderer>();
                opt.size = new(5.0f, 0.45f);
                while (parent != null && enabled)
                {
                    enabled = parent.GetBool() && !parent.IsHiddenOn(Options.CurrentGameMode);
                    parent = parent.Parent;
                    opt.color = new(0f, 1f, 0f);
                    opt.size = new(4.8f, 0.45f);
                    opt.transform.localPosition = new Vector3(0.11f, 0f);
                    option.OptionBehaviour.transform.Find("Title_TMP").transform.localPosition = new Vector3(-1.08f, 0f);
                    option.OptionBehaviour.transform.FindChild("Title_TMP").GetComponent<RectTransform>().sizeDelta = new Vector2(5.1f, 0.28f);
                    if (option.Parent?.Parent != null)
                    {
                        opt.color = new(0f, 0f, 1f);
                        opt.size = new(4.6f, 0.45f);
                        opt.transform.localPosition = new Vector3(0.24f, 0f);
                        option.OptionBehaviour.transform.Find("Title_TMP").transform.localPosition = new Vector3(-0.88f, 0f);
                        option.OptionBehaviour.transform.FindChild("Title_TMP").GetComponent<RectTransform>().sizeDelta = new Vector2(4.9f, 0.28f);
                        if (option.Parent?.Parent?.Parent != null)
                        {
                            opt.color = new(1f, 0f, 0f);
                            opt.size = new(4.4f, 0.45f);
                            opt.transform.localPosition = new Vector3(0.37f, 0f);
                            option.OptionBehaviour.transform.Find("Title_TMP").transform.localPosition = new Vector3(-0.68f, 0f);
                            option.OptionBehaviour.transform.FindChild("Title_TMP").GetComponent<RectTransform>().sizeDelta = new Vector2(4.7f, 0.28f);
                        }
                    }
                }

                if (option.IsText)
                {
                    opt.color = new(0, 0, 0);
                    opt.transform.localPosition = new(100f, 100f, 100f);
                }

                option.OptionBehaviour.gameObject.SetActive(enabled);
                if (enabled)
                {
                    offset -= option.IsHeader ? 0.7f : 0.5f;
                    option.OptionBehaviour.transform.localPosition = new Vector3(
                        option.OptionBehaviour.transform.localPosition.x,
                        offset,
                        option.OptionBehaviour.transform.localPosition.z);

                    if (option.IsHeader)
                    {
                        numItems += 0.3f;
                    }
                }
                else
                {
                    numItems--;
                }
            }
            __instance.GetComponentInParent<Scroller>().ContentYBounds.max = (-offset) - 1.5f;
        }
    }
}

[HarmonyPatch(typeof(StringOption), nameof(StringOption.OnEnable))]
public class StringOptionEnablePatch
{
    public static bool Prefix(StringOption __instance)
    {
        var option = OptionItem.AllOptions.FirstOrDefault(opt => opt.OptionBehaviour == __instance);
        if (option == null) return true;

        __instance.OnValueChanged = new Action<OptionBehaviour>((o) => { });
        __instance.TitleText.text = option.GetName();
        __instance.Value = __instance.oldValue = option.CurrentValue;
        __instance.ValueText.text = option.GetString();

        return false;
    }
}

[HarmonyPatch(typeof(StringOption), nameof(StringOption.Increase))]
public class StringOptionIncreasePatch
{
    public static bool Prefix(StringOption __instance)
    {
        var option = OptionItem.AllOptions.FirstOrDefault(opt => opt.OptionBehaviour == __instance);
        if (option == null) return true;

        option.SetValue(option.CurrentValue + (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 5 : 1));
        return false;
    }
    public static void Postfix(StringOption __instance) => OptionShower.GetText();
}

[HarmonyPatch(typeof(StringOption), nameof(StringOption.Decrease))]
public class StringOptionDecreasePatch
{
    public static bool Prefix(StringOption __instance)
    {
        var option = OptionItem.AllOptions.FirstOrDefault(opt => opt.OptionBehaviour == __instance);
        if (option == null) return true;

        option.SetValue(option.CurrentValue - (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 5 : 1));
        return false;
    }
    public static void Postfix(StringOption __instance) => OptionShower.GetText();
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSyncSettings))]
public class RpcSyncSettingsPatch
{
    public static void Postfix()
    {
        OptionItem.SyncAllOptions();
    }
}
[HarmonyPatch(typeof(RolesSettingsMenu), nameof(RolesSettingsMenu.Start))]
public static class RolesSettingsMenuPatch
{
    public static void Postfix(RolesSettingsMenu __instance)
    {
        foreach (OptionBehaviour ob in __instance.Children.ToArray())
        {
            switch (ob.Title)
            {
                case StringNames.EngineerCooldown:
                    ob.Cast<NumberOption>().ValidRange = new FloatRange(0, 180);
                    break;
                case StringNames.ShapeshifterCooldown:
                    ob.Cast<NumberOption>().ValidRange = new FloatRange(0, 180);
                    break;
                default:
                    break;
            }
        }
    }
}
[HarmonyPatch(typeof(NormalGameOptionsV07), nameof(NormalGameOptionsV07.SetRecommendations))]
public static class SetRecommendationsPatch
{
    public static bool Prefix(NormalGameOptionsV07 __instance, int numPlayers, bool isOnline)
    {
        numPlayers = Mathf.Clamp(numPlayers, 4, 15);
        __instance.PlayerSpeedMod = __instance.MapId == 4 ? 1.5f : 1.25f;
        __instance.CrewLightMod = 0.5f;
        __instance.ImpostorLightMod = 1.25f;
        __instance.KillCooldown = 27.5f;
        __instance.NumCommonTasks = 1;
        __instance.NumLongTasks = 3;
        __instance.NumShortTasks = 4;
        __instance.NumEmergencyMeetings = 1;
        if (!isOnline)
            __instance.NumImpostors = NormalGameOptionsV07.RecommendedImpostors[numPlayers];
        __instance.KillDistance = 1;
        __instance.DiscussionTime = 0;
        __instance.VotingTime = 120;
        __instance.IsDefaults = true;
        __instance.ConfirmImpostor = false;
        __instance.VisualTasks = false;

        __instance.roleOptions.SetRoleRate(RoleTypes.Shapeshifter, 0, 0);
        __instance.roleOptions.SetRoleRate(RoleTypes.Scientist, 0, 0);
        __instance.roleOptions.SetRoleRate(RoleTypes.GuardianAngel, 0, 0);
        __instance.roleOptions.SetRoleRate(RoleTypes.Engineer, 0, 0);
        __instance.roleOptions.SetRoleRecommended(RoleTypes.Shapeshifter);
        __instance.roleOptions.SetRoleRecommended(RoleTypes.Scientist);
        __instance.roleOptions.SetRoleRecommended(RoleTypes.GuardianAngel);
        __instance.roleOptions.SetRoleRecommended(RoleTypes.Engineer);

        if (Options.CurrentGameMode == CustomGameMode.SoloKombat || Options.CurrentGameMode == CustomGameMode.FFA) //SoloKombat & FFA
        {
            __instance.CrewLightMod = __instance.ImpostorLightMod = 1.25f;
            __instance.NumImpostors = 3;
            __instance.NumCommonTasks = 0;
            __instance.NumLongTasks = 0;
            __instance.NumShortTasks = 0;
            __instance.KillCooldown = 0f;
        }
        return false;
    }
}