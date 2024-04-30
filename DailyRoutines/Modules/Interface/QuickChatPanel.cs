using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using DailyRoutines.Windows;
using Dalamud.Game.Addon.Events;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Utility;
using ECommons.Automation;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;

namespace DailyRoutines.Modules;

[ModuleDescription("QuickChatPanelTitle", "QuickChatPanelDescription", ModuleCategories.Interface)]
public unsafe class QuickChatPanel : DailyModuleBase
{
    public class SavedMacro : IEquatable<SavedMacro>
    {
        public uint Category { get; set; } // 0 - Individual; 1 - Shared
        public int Position { get; set; }
        public string Name { get; set; } = string.Empty;
        public uint IconID { get; set; } = 0;
        public DateTime LastUpdateTime { get; set; } = DateTime.MinValue;

        public bool Equals(SavedMacro? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Category == other.Category && Position == other.Position;
        }

        public override bool Equals(object? obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj.GetType() == GetType() && Equals((SavedMacro)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Category, Position);
        }
    }

    private enum MacroDisplayMode
    {
        List,
        Buttons
    }

    private static readonly Dictionary<MacroDisplayMode, string> MacroDisplayModeLoc = new()
    {
        { MacroDisplayMode.List, Service.Lang.GetText("QuickChatPanel-List") },
        { MacroDisplayMode.Buttons, Service.Lang.GetText("QuickChatPanel-Buttons") }
    };

    private static char[] SeIconChars = [];
    private static Vector2 ButtonPos = new(0);
    private const float DefaultOverlayWidth = 300f;
    private static IAddonEventHandle? MouseClickHandle;
    private static string MessageInput = string.Empty;
    private static int _dropMacroIndex = -1;
    private static string ItemSearchInput = string.Empty;
    private static Dictionary<string, Item>? ItemNames;
    private static Dictionary<string, Item> _ItemNames = [];

    private static List<string> ConfigSavedMessages = [];
    private static List<SavedMacro> ConfigSavedMacros = [];
    private static float ConfigFontScale = 1.5f;
    private static Vector2 ConfigButtonOffset = new(0);
    private static ushort ConfigButtonSize = 48;
    private static int ConfigButtonIcon = 46;
    private static float ConfigOverlayHeight = 250f;
    private static MacroDisplayMode ConfigOverlayMacroDisplayMode = MacroDisplayMode.Buttons;

    private static AtkUnitBase* AddonChatLog => (AtkUnitBase*)Service.Gui.GetAddonByName("ChatLog");

    public override void Init()
    {
        #region Config Init

        AddConfig("SavedMessages", ConfigSavedMessages);
        ConfigSavedMessages = GetConfig<List<string>>("SavedMessages");

        AddConfig("SavedMacros", ConfigSavedMacros);
        ConfigSavedMacros = GetConfig<List<SavedMacro>>("SavedMacros");

        AddConfig("FontScale", 1.5f);
        ConfigFontScale = GetConfig<float>("FontScale");

        AddConfig("ButtonOffset", ConfigButtonOffset);
        ConfigButtonOffset = GetConfig<Vector2>("ButtonOffset");

        AddConfig("ButtonSize", ConfigButtonSize);
        ConfigButtonSize = GetConfig<ushort>("ButtonSize");

        AddConfig("ButtonIcon", ConfigButtonIcon);
        ConfigButtonIcon = GetConfig<int>("ButtonIcon");

        AddConfig("OverlayHeight", ConfigOverlayHeight);
        ConfigOverlayHeight = GetConfig<float>("OverlayHeight");

        AddConfig("OverlayMacroDisplayMode", ConfigOverlayMacroDisplayMode);
        ConfigOverlayMacroDisplayMode = GetConfig<MacroDisplayMode>("OverlayMacroDisplayMode");

        #endregion

        var tempSeIconList = new List<char>();
        foreach (SeIconChar seIconChar in Enum.GetValues(typeof(SeIconChar)))
            tempSeIconList.Add((char)seIconChar);
        SeIconChars = [.. tempSeIconList];
        ItemNames ??= LuminaCache.Get<Item>()
                                 .Where(x => !string.IsNullOrEmpty(x.Name.RawString))
                                 .GroupBy(x => x.Name.RawString)
                                 .ToDictionary(x => x.Key, x => x.First());

        _ItemNames = ItemNames.Take(10).ToDictionary(x => x.Key, x => x.Value);

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "ChatLog", OnAddon);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, "ChatLog", OnAddon);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "ChatLog", OnAddon);
        if (AddonChatLog != null) OnAddon(AddonEvent.PostSetup, null);

        Overlay ??= new Overlay(this);
        Overlay.Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
        Overlay.Flags &= ~ImGuiWindowFlags.AlwaysAutoResize;
        Overlay.Flags &= ~ImGuiWindowFlags.NoScrollbar;

        TaskManager ??= new TaskManager { AbortOnTimeout = true, TimeLimitMS = 5000, ShowDebug = false };
    }

    public override void ConfigUI()
    {
        // 左半边
        ImGui.BeginGroup();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("QuickChatPanel-Messages")}:");

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("QuickChatPanel-Macro")}:");

        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("QuickChatPanel-ButtonOffset")}:");

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("QuickChatPanel-ButtonSize")}:");

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("QuickChatPanel-ButtonIcon")}:");

        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("QuickChatPanel-FontScale")}:");

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("QuickChatPanel-OverlayHeight")}:");

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange,
                          $"{Service.Lang.GetText("QuickChatPanel-OverlayMacroDisplayMode")}:");
        ImGui.EndGroup();

        ImGui.SameLine();

        // 右半边
        ImGui.BeginGroup();
        ImGui.SetNextItemWidth(240f * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("###MessagesCombo",
                             Service.Lang.GetText("QuickChatPanel-SavedMessagesAmountText", ConfigSavedMessages.Count)))
        {
            ImGui.InputText("###MessageToSaveInput", ref MessageInput, 1000);
            ImGui.SameLine();
            if (ImGuiOm.ButtonIcon("###MessagesInputAdd", FontAwesomeIcon.Plus))
            {
                if (ConfigSavedMessages.Contains(MessageInput)) return;
                ConfigSavedMessages.Add(MessageInput);

                UpdateConfig("SavedMessages", ConfigSavedMessages);
            }

            if (ConfigSavedMessages.Count > 0) ImGui.Separator();

            var messagesToDelete = new List<string>();
            foreach (var message in ConfigSavedMessages)
            {
                ImGuiOm.ButtonSelectable(message);

                if (ImGui.BeginPopupContextItem())
                {
                    if (ImGuiOm.ButtonSelectable(Service.Lang.GetText("Delete")))
                        messagesToDelete.Add(message);

                    ImGui.EndPopup();
                }
            }

            if (messagesToDelete.Count > 0)
            {
                messagesToDelete.ForEach(x => ConfigSavedMessages.Remove(x));
                UpdateConfig("SavedMessages", ConfigSavedMessages);
            }

            ImGui.EndCombo();
        }

        ImGui.SetNextItemWidth(240f * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("###MacroCombo",
                             Service.Lang.GetText("QuickChatPanel-SavedMacrosAmountText", ConfigSavedMacros.Count),
                             ImGuiComboFlags.HeightLargest))
        {
            var module = RaptureMacroModule.Instance();
            var leftChildSize = new Vector2(200 * ImGuiHelpers.GlobalScale, 300 * ImGuiHelpers.GlobalScale);
            if (ImGui.BeginChild("IndividualMacroComboSelect", leftChildSize))
            {
                ImGui.Text(Service.Lang.GetText("QuickChatPanel-IndividualMacros"));
                ImGui.Separator();

                var individualSpan = module->IndividualSpan;
                for (var i = 0; i < individualSpan.Length; i++)
                {
                    var macro = individualSpan.GetPointer(i);
                    if (macro == null) continue;

                    var name = macro->Name.ExtractText();
                    var icon = ImageHelper.GetIcon(macro->IconId);
                    if (string.IsNullOrEmpty(name) || icon == null) continue;

                    var currentSavedMacro = (*macro).ToSavedMacro();
                    currentSavedMacro.Position = i;
                    currentSavedMacro.Category = 0;

                    ImGui.PushID($"{currentSavedMacro.Category}-{currentSavedMacro.Position}");
                    if (ImGuiOm.SelectableImageWithText(icon.ImGuiHandle, new(24), name,
                                                        ConfigSavedMacros.Contains(currentSavedMacro),
                                                        ImGuiSelectableFlags.DontClosePopups))
                    {
                        if (!ConfigSavedMacros.Remove(currentSavedMacro))
                        {
                            ConfigSavedMacros.Add(currentSavedMacro);
                            UpdateConfig("SavedMacros", ConfigSavedMacros);
                        }
                    }

                    if (ConfigSavedMacros.Contains(currentSavedMacro) && ImGui.BeginPopupContextItem())
                    {
                        ImGui.TextColored(ImGuiColors.DalamudOrange,
                                          $"{Service.Lang.GetText("QuickChatPanel-LastUpdateTime")}:");

                        ImGui.SameLine();
                        ImGui.Text(
                            $"{ConfigSavedMacros.FirstOrDefault(x => x.Category == 0 && x.Position == i)?.LastUpdateTime}");

                        ImGui.Separator();

                        if (ImGuiOm.SelectableTextCentered(Service.Lang.GetText("Refresh")))
                        {
                            var currentIndex = ConfigSavedMacros.IndexOf(currentSavedMacro);
                            if (currentIndex != -1)
                            {
                                ConfigSavedMacros[currentIndex] = currentSavedMacro;
                                UpdateConfig("SavedMacros", ConfigSavedMacros);
                            }
                        }

                        ImGui.EndPopup();
                    }

                    ImGui.PopID();
                }

                ImGui.EndChild();
            }

            ImGui.SameLine();
            if (ImGui.BeginChild("SharedMacroComboSelect", leftChildSize))
            {
                ImGui.Text(Service.Lang.GetText("QuickChatPanel-SharedMacros"));
                ImGui.Separator();

                var individualSpan = module->SharedSpan;
                for (var i = 0; i < individualSpan.Length; i++)
                {
                    var macro = individualSpan.GetPointer(i);
                    if (macro == null) continue;

                    var name = macro->Name.ExtractText();
                    var icon = ImageHelper.GetIcon(macro->IconId);
                    if (string.IsNullOrEmpty(name) || icon == null) continue;

                    var currentSavedMacro = (*macro).ToSavedMacro();
                    currentSavedMacro.Position = i;
                    currentSavedMacro.Category = 1;

                    ImGui.PushID($"{currentSavedMacro.Category}-{currentSavedMacro.Position}");
                    if (ImGuiOm.SelectableImageWithText(icon.ImGuiHandle, new(24), name,
                                                        ConfigSavedMacros.Contains(currentSavedMacro),
                                                        ImGuiSelectableFlags.DontClosePopups))
                    {
                        if (!ConfigSavedMacros.Remove(currentSavedMacro))
                        {
                            ConfigSavedMacros.Add(currentSavedMacro);
                            UpdateConfig("SavedMacros", ConfigSavedMacros);
                        }
                    }

                    if (ConfigSavedMacros.Contains(currentSavedMacro) && ImGui.BeginPopupContextItem())
                    {
                        ImGui.TextColored(ImGuiColors.DalamudOrange,
                                          $"{Service.Lang.GetText("QuickChatPanel-LastUpdateTime")}:");

                        ImGui.SameLine();
                        ImGui.Text(
                            $"{ConfigSavedMacros.FirstOrDefault(x => x.Category == 1 && x.Position == i)?.LastUpdateTime}");

                        ImGui.Separator();

                        if (ImGuiOm.SelectableTextCentered(Service.Lang.GetText("Refresh")))
                        {
                            if (ConfigSavedMacros.Remove(currentSavedMacro))
                            {
                                ConfigSavedMacros.Add(currentSavedMacro);
                                UpdateConfig("SavedMacros", ConfigSavedMacros);
                            }
                        }

                        ImGui.EndPopup();
                    }

                    ImGui.PopID();
                }

                ImGui.EndChild();
            }

            ImGui.EndCombo();
        }

        ImGui.Spacing();

        ImGui.SetNextItemWidth(150f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputFloat2("###ButtonOffsetInput", ref ConfigButtonOffset, "%.1f",
                              ImGuiInputTextFlags.EnterReturnsTrue))
            UpdateConfig("ButtonOffset", ConfigButtonOffset);

        ImGui.SetNextItemWidth(100f * ImGuiHelpers.GlobalScale);
        var intConfigButtonSize = (int)ConfigButtonSize;
        if (ImGui.InputInt("###ButtonSizeInput", ref intConfigButtonSize, 0, 0, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            ConfigButtonSize = (ushort)Math.Clamp(intConfigButtonSize, 1, 65535);
            UpdateConfig("ButtonSize", ConfigButtonSize);
        }

        ImGui.SetNextItemWidth(100f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("###ButtonIconInput", ref ConfigButtonIcon, 0, 0, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            ConfigButtonIcon = Math.Max(ConfigButtonIcon, 1);
            UpdateConfig("ButtonIcon", ConfigButtonIcon);
        }

        ImGui.SameLine();
        if (ImGuiOm.ButtonIcon("OpenIconBrowser", FontAwesomeIcon.Search,
                               Service.Lang.GetText("QuickChatPanel-OpenIconBrowser")))
            Chat.Instance.SendMessage("/xldata icon");

        ImGui.Spacing();

        ImGui.SetNextItemWidth(100f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputFloat("###FontScaleInput", ref ConfigFontScale, 0, 0, "%.1f",
                             ImGuiInputTextFlags.EnterReturnsTrue))
        {
            ConfigFontScale = (float)Math.Clamp(ConfigFontScale, 0.1, 10f);
            UpdateConfig("FontScale", ConfigFontScale);
        }

        ImGui.SetNextItemWidth(100f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputFloat("###OverlayHeightInput", ref ConfigOverlayHeight, 0, 0, "%.1f",
                             ImGuiInputTextFlags.EnterReturnsTrue))
        {
            ConfigOverlayHeight = Math.Clamp(ConfigOverlayHeight, 100f, 10000f);
            UpdateConfig("OverlayHeight", ConfigOverlayHeight);
        }

        ImGui.SetNextItemWidth(100f * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("###OverlayMacroDisplayModeCombo", MacroDisplayModeLoc[ConfigOverlayMacroDisplayMode]))
        {
            foreach (MacroDisplayMode mode in Enum.GetValues(typeof(MacroDisplayMode)))
                if (ImGui.Selectable(MacroDisplayModeLoc[mode], mode == ConfigOverlayMacroDisplayMode))
                {
                    ConfigOverlayMacroDisplayMode = mode;
                    UpdateConfig("OverlayMacroDisplayMode", ConfigOverlayMacroDisplayMode);
                }

            ImGui.EndCombo();
        }

        ImGui.EndGroup();
    }

    public override void OverlayUI()
    {
        if (Service.ClientState.LocalPlayer == null)
        {
            Overlay.IsOpen = false;
            return;
        }

        var textInputNode = AddonChatLog->GetNodeById(5);
        if (textInputNode == null) return;

        var buttonPos = new Vector2(textInputNode->X + textInputNode->Width, textInputNode->ScreenY) +
                        ConfigButtonOffset;

        ImGui.SetWindowPos(buttonPos with { Y = buttonPos.Y - ImGui.GetWindowSize().Y - 5 });

        if (ImGui.BeginTabBar("###QuickChatPanel", ImGuiTabBarFlags.Reorderable))
        {
            // 消息
            if (ImGui.BeginTabItem(Service.Lang.GetText("QuickChatPanel-Messages")))
            {
                var maxTextWidth = 300f * ImGuiHelpers.GlobalScale;
                if (ImGui.BeginChild("MessagesChild", ImGui.GetContentRegionAvail(), false))
                {
                    PresetFont.Axis14.Push();
                    ImGui.SetWindowFontScale(ConfigFontScale);
                    for (var i = 0; i < ConfigSavedMessages.Count; i++)
                    {
                        var message = ConfigSavedMessages[i];

                        var textWidth = ImGui.CalcTextSize(message).X;
                        maxTextWidth = Math.Max(textWidth + 64, maxTextWidth);

                        ImGuiOm.SelectableTextCentered(message);

                        if (ImGui.IsKeyDown(ImGuiKey.LeftShift))
                        {
                            if (ImGui.BeginDragDropSource())
                            {
                                if (ImGui.SetDragDropPayload("MessageReorder", nint.Zero, 0)) _dropMacroIndex = i;
                                ImGui.TextColored(ImGuiColors.DalamudYellow, message);
                                ImGui.EndDragDropSource();
                            }

                            if (ImGui.BeginDragDropTarget())
                            {
                                if (_dropMacroIndex >= 0 ||
                                    ImGui.AcceptDragDropPayload("MessageReorder").NativePtr != null)
                                {
                                    SwapMessages(_dropMacroIndex, i);
                                    _dropMacroIndex = -1;
                                }

                                ImGui.EndDragDropTarget();
                            }
                        }

                        if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) ImGui.SetClipboardText(message);

                        if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) Chat.Instance.SendMessage(message);

                        ImGuiOm.TooltipHover(Service.Lang.GetText("QuickChatPanel-SendMessageHelp"));

                        if (i != ConfigSavedMessages.Count - 1)
                            ImGui.Separator();
                    }

                    PresetFont.Axis14.Pop();
                    ImGui.SetWindowFontScale(1f);
                    ImGui.EndChild();
                }

                ImGui.SetWindowSize(new(Math.Max(DefaultOverlayWidth, maxTextWidth),
                                        ConfigOverlayHeight * ImGuiHelpers.GlobalScale));
                ImGui.EndTabItem();
            }

            // 宏
            if (ImGui.BeginTabItem(Service.Lang.GetText("QuickChatPanel-Macro")))
            {
                var maxTextWidth = 300f * ImGuiHelpers.GlobalScale;
                if (ImGui.BeginChild("MacroChild", ImGui.GetContentRegionAvail(), false))
                {
                    PresetFont.Axis14.Push();
                    ImGui.SetWindowFontScale(ConfigFontScale);
                    ImGui.BeginGroup();
                    for (var i = 0; i < ConfigSavedMacros.Count; i++)
                    {
                        var macro = ConfigSavedMacros[i];

                        var name = macro.Name;
                        var icon = ImageHelper.GetIcon(macro.IconID);
                        if (string.IsNullOrEmpty(name) || icon == null) continue;

                        switch (ConfigOverlayMacroDisplayMode)
                        {
                            case MacroDisplayMode.List:
                                if (ImGuiOm.SelectableImageWithText(icon.ImGuiHandle, new(24), name, false))
                                {
                                    var gameMacro =
                                        RaptureMacroModule.Instance()->GetMacro(macro.Category, (uint)macro.Position);
                                    RaptureShellModule.Instance()->ExecuteMacro(gameMacro);
                                }

                                break;
                            case MacroDisplayMode.Buttons:
                                if (ButtonImageWithTextVertical(icon, name))
                                {
                                    var gameMacro =
                                        RaptureMacroModule.Instance()->GetMacro(macro.Category, (uint)macro.Position);
                                    RaptureShellModule.Instance()->ExecuteMacro(gameMacro);
                                }

                                break;
                        }

                        if (ImGui.IsKeyDown(ImGuiKey.LeftShift))
                        {
                            if (ImGui.BeginDragDropSource())
                            {
                                if (ImGui.SetDragDropPayload("MacroReorder", nint.Zero, 0)) _dropMacroIndex = i;
                                ImGui.TextColored(ImGuiColors.DalamudYellow, name);
                                ImGui.EndDragDropSource();
                            }

                            if (ImGui.BeginDragDropTarget())
                            {
                                if (_dropMacroIndex >= 0 ||
                                    ImGui.AcceptDragDropPayload("MacroReorder").NativePtr != null)
                                {
                                    SwapMacros(_dropMacroIndex, i);
                                    _dropMacroIndex = -1;
                                }

                                ImGui.EndDragDropTarget();
                            }
                        }

                        switch (ConfigOverlayMacroDisplayMode)
                        {
                            case MacroDisplayMode.List:
                                if (i != ConfigSavedMacros.Count - 1)
                                    ImGui.Separator();
                                break;
                            case MacroDisplayMode.Buttons:
                                if ((i + 1) % 5 != 0) ImGui.SameLine();
                                else
                                {
                                    ImGui.SameLine();
                                    ImGui.Dummy(new(20 * ConfigFontScale));
                                }

                                break;
                        }
                    }

                    ImGui.EndGroup();
                    maxTextWidth = ImGui.GetItemRectSize().X;

                    ImGui.SetWindowFontScale(1f);
                    PresetFont.Axis14.Pop();
                    ImGui.EndChild();
                }

                ImGui.SetWindowSize(new(Math.Max(DefaultOverlayWidth, maxTextWidth),
                                        ConfigOverlayHeight * ImGuiHelpers.GlobalScale));
                ImGui.EndTabItem();
            }

            // 系统音
            if (ImGui.BeginTabItem(Service.Lang.GetText("QuickChatPanel-SystemSound")))
            {
                var maxTextWidth = 300f * ImGuiHelpers.GlobalScale;
                PresetFont.Axis14.Push();
                ImGui.SetWindowFontScale(ConfigFontScale);
                ImGui.BeginGroup();
                for (var i = 1U; i < 17U; i++)
                {
                    ImGuiOm.SelectableCentered($"        {(i > 9 ? "" : "  ")}<se.{i}>          ###PlaySound{i}");

                    if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                        UIModule.PlayChatSoundEffect(i);
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        Chat.Instance.SendMessage($"<se.{i}><se.{i}>");

                    ImGuiOm.TooltipHover(Service.Lang.GetText("QuickChatPanel-SystemSoundHelp"));
                }
                ImGui.EndGroup();
                maxTextWidth = ImGui.GetItemRectSize().X;
                ImGui.SetWindowFontScale(1f);
                PresetFont.Axis14.Pop();

                ImGui.SetWindowSize(new(Math.Max(DefaultOverlayWidth, maxTextWidth),
                                        ConfigOverlayHeight * ImGuiHelpers.GlobalScale));
                ImGui.EndTabItem();
            }

            // 游戏物品
            if (ImGui.BeginTabItem(Service.Lang.GetText("QuickChatPanel-GameItems")))
            {
                var maxTextWidth = 300f * ImGuiHelpers.GlobalScale;
                if (ImGui.BeginChild("GameItemChild", ImGui.GetContentRegionAvail(), false))
                {
                    PresetFont.Axis14.Push();
                    ImGui.SetWindowFontScale(ConfigFontScale);

                    ImGui.SetNextItemWidth(-1f);
                    ImGui.InputTextWithHint("###GameItemSearchInput", Service.Lang.GetText("PleaseSearch"),
                                            ref ItemSearchInput, 100);

                    if (ImGui.IsItemDeactivatedAfterEdit())
                    {
                        if (!string.IsNullOrWhiteSpace(ItemSearchInput) && ItemSearchInput.Length > 1)
                        {
                            _ItemNames = ItemNames
                                         .Where(
                                             x => x.Key.Contains(ItemSearchInput, StringComparison.OrdinalIgnoreCase))
                                         .ToDictionary(x => x.Key, x => x.Value);
                        }
                    }

                    ImGui.Separator();

                    var longestText = string.Empty;
                    foreach (var (itemName, item) in _ItemNames)
                    {
                        if (itemName.Length > longestText.Length) longestText = itemName;
                        if (ImGuiOm.SelectableImageWithText(ImageHelper.GetIcon(item.Icon).ImGuiHandle,
                                                            new(24),
                                                            itemName, false))
                            Service.Chat.Print(new SeStringBuilder().AddItemLink(item.RowId).Build());
                    }

                    maxTextWidth = ImGui.CalcTextSize(longestText).X + 200f * ImGuiHelpers.GlobalScale;

                    ImGui.SetWindowFontScale(1f);
                    PresetFont.Axis14.Pop();
                    ImGui.EndChild();
                }

                ImGui.SetWindowSize(new(Math.Max(DefaultOverlayWidth, maxTextWidth),
                                        ConfigOverlayHeight * ImGuiHelpers.GlobalScale));

                ImGui.EndTabItem();
            }

            // 特殊物品符号
            if (ImGui.BeginTabItem(Service.Lang.GetText("QuickChatPanel-SpecialIconChar")))
            {
                var maxTextWidth = 300f * ImGuiHelpers.GlobalScale;
                if (ImGui.BeginChild("SeIconChild", ImGui.GetContentRegionAvail(), false))
                {
                    PresetFont.Axis14.Push();
                    ImGui.SetWindowFontScale(ConfigFontScale);

                    ImGui.BeginGroup();
                    for (var i = 0; i < SeIconChars.Length; i++)
                    {
                        var icon = SeIconChars[i];

                        if (ImGui.Button($"{icon}", new(48 * ConfigFontScale))) ImGui.SetClipboardText(icon.ToString());

                        ImGuiOm.TooltipHover($"0x{(int)icon:X4}");

                        if ((i + 1) % 7 != 0) ImGui.SameLine();
                        else
                        {
                            ImGui.SameLine();
                            ImGui.Dummy(new(20 * ConfigFontScale));
                        }
                    }

                    ImGui.EndGroup();

                    maxTextWidth = ImGui.GetItemRectSize().X;
                    ImGui.SetWindowFontScale(1f);
                    PresetFont.Axis14.Pop();
                    ImGui.EndChild();
                }

                ImGui.SetWindowSize(new(Math.Max(DefaultOverlayWidth, maxTextWidth),
                                        ConfigOverlayHeight * ImGuiHelpers.GlobalScale));
                ImGui.EndTabItem();
            }

            ImGui.SameLine();
            if (ImGuiOm.ButtonIcon("OpenQuickChatPanelSettings", FontAwesomeIcon.Cog))
            {
                WindowManager.Main.IsOpen ^= true;
                if (WindowManager.Main.IsOpen)
                {
                    Main.SearchString = Service.Lang.GetText("QuickChatPanelTitle");
                    return;
                }

                Main.SearchString = string.Empty;
            }

            ImGui.SameLine();
            ImGuiOm.HelpMarker(Service.Lang.GetText("QuickChatPanelTitle-DragHelp"));

            ImGui.EndTabBar();
        }
    }

    private void OnAddon(AddonEvent type, AddonArgs? args)
    {
        if (!EzThrottler.Throttle("QuickChatPanel-UIAdjust")) return;
        switch (type)
        {
            case AddonEvent.PostSetup:
            case AddonEvent.PostRefresh:
                if (Service.ClientState.LocalPlayer == null || AddonChatLog == null) return;
                FreeNode();

                var textInputNode = AddonChatLog->GetNodeById(5);
                var collisionNode = AddonChatLog->GetNodeById(15);
                if (textInputNode == null || collisionNode == null) return;

                ButtonPos = new Vector2(textInputNode->X + textInputNode->Width - ConfigButtonSize - 6,
                                        textInputNode->Y - 3) + ConfigButtonOffset;

                AtkResNode* iconNode = null;
                for (var i = 0; i < AddonChatLog->UldManager.NodeListCount; i++)
                {
                    var node = AddonChatLog->UldManager.NodeList[i];
                    if (node->NodeID == 10001)
                    {
                        iconNode = node;
                        break;
                    }
                }

                if (iconNode is null)
                    MakeIconNode(10001, ButtonPos, ConfigButtonIcon);
                else
                {
                    iconNode->SetPositionFloat(ButtonPos.X, ButtonPos.Y);
                    iconNode->SetHeight(ConfigButtonSize);
                    iconNode->SetWidth(ConfigButtonSize);
                    ((AtkImageNode*)iconNode)->LoadIconTexture(ConfigButtonIcon, 0);
                }

                break;
            case AddonEvent.PreFinalize:
                FreeNode();
                break;
        }
    }

    private void MakeIconNode(uint nodeId, Vector2 position, int icon)
    {
        var imageNode = AddonHelper.MakeImageNode(nodeId, new AddonHelper.PartInfo(0, 0, 64, 64));
        imageNode->AtkResNode.NodeFlags = NodeFlags.AnchorTop | NodeFlags.AnchorLeft | NodeFlags.Visible |
                                          NodeFlags.Enabled | NodeFlags.EmitsEvents;
        imageNode->WrapMode = 1;
        imageNode->Flags = (byte)ImageNodeFlags.AutoFit;

        imageNode->LoadIconTexture(icon, 0);
        imageNode->AtkResNode.ToggleVisibility(true);

        imageNode->AtkResNode.SetWidth(ConfigButtonSize);
        imageNode->AtkResNode.SetHeight(ConfigButtonSize);
        imageNode->AtkResNode.SetPositionShort((short)position.X, (short)position.Y);

        AddonHelper.LinkNodeAtEnd((AtkResNode*)imageNode, AddonChatLog);

        imageNode->AtkResNode.NodeFlags |= NodeFlags.RespondToMouse | NodeFlags.EmitsEvents | NodeFlags.HasCollision;
        AddonChatLog->UpdateCollisionNodeList(true);
        MouseClickHandle ??=
            Service.AddonEvent.AddEvent((nint)AddonChatLog, (nint)(&imageNode->AtkResNode), AddonEventType.MouseClick,
                                        OnEvent);
    }

    private void OnEvent(AddonEventType atkEventType, IntPtr atkUnitBase, IntPtr atkResNode)
    {
        Overlay.IsOpen ^= true;
    }

    private static void FreeNode()
    {
        if (AddonChatLog == null) return;

        for (var i = 0; i < AddonChatLog->UldManager.NodeListCount; i++)
        {
            var node = AddonChatLog->UldManager.NodeList[i];
            if (node->NodeID == 10001)
            {
                AddonHelper.UnlinkAndFreeImageNode((AtkImageNode*)node, AddonChatLog);
                Service.AddonEvent.RemoveEvent(MouseClickHandle);
                MouseClickHandle = null;
            }
        }
    }

    private static bool ButtonImageWithTextVertical(IDalamudTextureWrap icon, string text)
    {
        ImGui.PushID($"{text}_{icon}");
        var iconSize = icon.Size;
        var textSize = ImGui.CalcTextSize(text);
        var windowDrawList = ImGui.GetWindowDrawList();
        var cursorScreenPos = ImGui.GetCursorScreenPos();
        var padding = ImGui.GetStyle().FramePadding.X;
        var spacing = 3f * ImGuiHelpers.GlobalScale;
        var buttonWidth = Math.Max(iconSize.X, textSize.X) + (padding * 2);
        var buttonHeight = iconSize.Y + textSize.Y + (padding * 2) + spacing;

        var result = ImGui.Button(string.Empty, new Vector2(buttonWidth, buttonHeight));

        var iconStartPos =
            new Vector2(cursorScreenPos.X + ((buttonWidth - iconSize.X) / 2), cursorScreenPos.Y + padding);
        var iconEndPos = iconStartPos + iconSize;
        var iconPos = new Vector2(cursorScreenPos.X + ((buttonWidth - iconSize.X) / 2), cursorScreenPos.Y + padding);
        var textPos = new Vector2(cursorScreenPos.X + ((buttonWidth - textSize.X) / 2),
                                  iconPos.Y + iconSize.Y + spacing);

        windowDrawList.AddImage(icon.ImGuiHandle, iconStartPos, iconEndPos);
        windowDrawList.AddText(textPos, ImGui.GetColorU32(ImGuiCol.Text), text);

        ImGui.PopID();

        return result;
    }

    private void SwapMacros(int index1, int index2)
    {
        (ConfigSavedMacros[index1], ConfigSavedMacros[index2]) = (ConfigSavedMacros[index2], ConfigSavedMacros[index1]);

        TaskManager.Abort();

        TaskManager.DelayNext(500);
        TaskManager.Enqueue(() => { UpdateConfig("SavedMacros", ConfigSavedMacros); });
    }

    private void SwapMessages(int index1, int index2)
    {
        (ConfigSavedMessages[index1], ConfigSavedMessages[index2]) =
            (ConfigSavedMessages[index2], ConfigSavedMessages[index1]);

        TaskManager.Abort();

        TaskManager.DelayNext(500);
        TaskManager.Enqueue(() => { UpdateConfig("SavedMessages", ConfigSavedMessages); });
    }

    public override void Uninit()
    {
        FreeNode();

        Service.AddonLifecycle.UnregisterListener(OnAddon);

        base.Uninit();
    }
}
