using System;
using System.Collections.Generic;
using System.Linq;
using ClickLib;
using DailyRoutines.Clicks;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Utility;
using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoLeveQuestsTitle", "AutoLeveQuestsDescription", ModuleCategories.General)]
public class AutoLeveQuests : IDailyModule
{
    public bool Initialized { get; set; }
    public bool WithConfigUI => true;

    private static TaskManager? TaskManager;
    private const string LeveAllowanceSig = "88 05 ?? ?? ?? ?? 0F B7 41 06";

    private static Dictionary<uint, (string, uint)> LeveQuests = [];
    internal static (uint, string, uint)? SelectedLeve; // Leve ID - Leve Name - Leve Job Category
    private static uint LeveMeteDataId;
    private static uint LeveReceiverDataId;
    private static int Allowances;
    private static string SearchString = string.Empty;

    private static int ConfigOperationDelay;

    private static bool IsOnProcessing;

    public void Init()
    {
        Service.ClientState.TerritoryChanged += OnZoneChanged;
        TaskManager ??= new TaskManager { AbortOnTimeout = true, TimeLimitMS = 30000, ShowDebug = false };

        Service.Config.AddConfig(this, "OperationDelay", 0);
        ConfigOperationDelay = Service.Config.GetConfig<int>(this, "OperationDelay");
    }

    public void ConfigUI()
    {
        ImGui.BeginDisabled(IsOnProcessing);
        ImGui.SetNextItemWidth(80f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(Service.Lang.GetText("AutoLeveQuests-OperationDelay"), ref ConfigOperationDelay, 0, 0,
                           ImGuiInputTextFlags.EnterReturnsTrue))
        {
            ConfigOperationDelay = Math.Max(0, ConfigOperationDelay);
            Service.Config.UpdateConfig(this, "OperationDelay", ConfigOperationDelay);
        }

        ImGui.SameLine();
        ImGuiOm.HelpMarker(Service.Lang.GetText("AutoLeveQuests-OperationDelayHelp"));

        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{Service.Lang.GetText("AutoLeveQuests-SelectedLeve")}");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(300f * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("##SelectedLeve",
                             SelectedLeve == null ? "" : $"{SelectedLeve.Value.Item1} | {SelectedLeve.Value.Item2}"))
        {
            if (ImGui.Button(Service.Lang.GetText("AutoLeveQuests-GetAreaLeveData"))) GetMapLeveQuests();

            ImGui.SetNextItemWidth(-1f);
            ImGui.SameLine();
            ImGui.InputText("##AutoLeveQuests-SearchLeveQuest", ref SearchString, 100);

            ImGui.Separator();
            if (LeveQuests.Any())
            {
                foreach (var leveToSelect in LeveQuests)
                {
                    if (!string.IsNullOrEmpty(SearchString) &&
                        !leveToSelect.Value.Item1.Contains(SearchString, StringComparison.OrdinalIgnoreCase) &&
                        !leveToSelect.Key.ToString().Contains(SearchString, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (ImGui.Selectable($"{leveToSelect.Key} | {leveToSelect.Value.Item1}"))
                        SelectedLeve = (leveToSelect.Key, leveToSelect.Value.Item1, leveToSelect.Value.Item2);
                    if (SelectedLeve != null && ImGui.IsWindowAppearing() &&
                        SelectedLeve.Value.Item1 == leveToSelect.Key)
                        ImGui.SetScrollHereY();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(SelectedLeve == null || LeveMeteDataId == LeveReceiverDataId || LeveMeteDataId == 0 ||
                            LeveReceiverDataId == 0);
        if (ImGui.Button(Service.Lang.GetText("AutoLeveQuests-Start")))
        {
            IsOnProcessing = true;
            Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", AlwaysYes);

            TaskManager.Enqueue(InteractWithMete);
        }

        ImGui.EndDisabled();
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(Service.Lang.GetText("AutoLeveQuests-Stop"))) EndProcessHandler();

        ImGui.BeginDisabled(IsOnProcessing);
        if (ImGui.Button(Service.Lang.GetText("AutoLeveQuests-ObtainLevemeteID")))
            GetCurrentTargetDataID(out LeveMeteDataId);

        ImGui.SameLine();
        ImGui.Text(LeveMeteDataId.ToString());

        ImGui.SameLine();
        ImGui.Spacing();

        ImGui.SameLine();
        if (ImGui.Button(Service.Lang.GetText("AutoLeveQuests-ObtainLeveClientID")))
            GetCurrentTargetDataID(out LeveReceiverDataId);

        ImGui.SameLine();
        ImGui.Text(LeveReceiverDataId.ToString());

        ImGui.EndDisabled();
    }

    public void OverlayUI() { }

    private static void EndProcessHandler()
    {
        TaskManager?.Abort();
        Service.AddonLifecycle.UnregisterListener(AlwaysYes);
        IsOnProcessing = false;
    }

    private static void OnZoneChanged(ushort zone)
    {
        LeveQuests.Clear();
        SelectedLeve = null;
    }

    private static void AlwaysYes(AddonEvent type, AddonArgs args)
    {
        Click.SendClick("select_yes");
    }

    private static void GetMapLeveQuests()
    {
        var currentTerritoryPlaceNameId = Service.Data.GetExcelSheet<TerritoryType>()
                                                 .FirstOrDefault(y => y.RowId == Service.ClientState.TerritoryType)?
                                                 .PlaceName.RawRow.RowId;

        if (currentTerritoryPlaceNameId.HasValue)
        {
            LeveQuests = Service.Data.GetExcelSheet<Leve>()
                                .Where(x => !string.IsNullOrEmpty(x.Name.RawString) &&
                                            x.ClassJobCategory.RawRow.RowId is >= 9 and <= 16
                                            && x.PlaceNameIssued.RawRow.RowId == currentTerritoryPlaceNameId.Value)
                                .ToDictionary(x => x.RowId, x => (x.Name.RawString, x.ClassJobCategory.RawRow.RowId));

            Service.Log.Debug($"成功获取了 {LeveQuests.Count} 个理符任务");
        }
    }

    private static void GetCurrentTargetDataID(out uint targetDataId)
    {
        var currentTarget = Service.Target.Target;
        targetDataId = currentTarget == null ? 0 : currentTarget.DataId;
    }

    private static unsafe bool? InteractWithMete()
    {
        if (TryGetAddonByName<AtkUnitBase>("SelectString", out var addon) && HelpersOm.IsAddonAndNodesReady(addon))
        {
            if (HelpersOm.TryScanSelectStringText(addon, "继续交货", out var index))
            {
                Click.SendClick($"select_string{index + 1}");
                return false;
            }
        }

        if (IsOccupied()) return false;
        if (FindObjectToInteractWith(LeveMeteDataId, out var foundObject))
        {
            TargetSystem.Instance()->InteractWithObject(foundObject);
            if (ConfigOperationDelay > 0) TaskManager.DelayNext(ConfigOperationDelay);
            TaskManager.Enqueue(ClickCraftingLeve);
            return true;
        }

        return false;
    }

    private static unsafe bool FindObjectToInteractWith(uint dataId, out GameObject* foundObject)
    {
        foundObject = null;

        var objAddress = Service.ObjectTable
                                .FirstOrDefault(x => x.DataId == dataId && x.IsTargetable).Address;
        if (objAddress != nint.Zero)
        {
            foundObject = (GameObject*)objAddress;
            return true;
        }

        return false;
    }

    private static unsafe bool? ClickCraftingLeve()
    {
        if (TryGetAddonByName<AtkUnitBase>("SelectString", out var addon) && HelpersOm.IsAddonAndNodesReady(addon))
        {
            if (HelpersOm.TryScanSelectStringText(addon, "制作", out var index))
                Click.SendClick($"select_string{index + 1}");

            TaskManager.Enqueue(ClickLeveQuest);
            return true;
        }

        return false;
    }

    private static unsafe bool? ClickLeveQuest()
    {
        if (SelectedLeve == null) return false;
        Allowances = *(byte*)Service.SigScanner.GetStaticAddressFromSig(LeveAllowanceSig);
        if (Allowances <= 0) EndProcessHandler();

        if (TryGetAddonByName<AtkUnitBase>("JournalDetail", out var addon) && HelpersOm.IsAddonAndNodesReady(addon))
        {
            var handler = new ClickJournalDetailDR();
            handler.Accept((int)SelectedLeve.Value.Item1);

            TaskManager.Enqueue(ClickExit);
            return true;
        }

        return false;
    }

    internal static unsafe bool? ClickExit()
    {
        if (TryGetAddonByName<AtkUnitBase>("GuildLeve", out var addon) &&
            HelpersOm.IsAddonAndNodesReady(addon))
        {
            var handler = new ClickGuildLeveDR();
            handler.Exit();
            addon->Close(true);

            TaskManager.Enqueue(ClickSelectStringExit);
            return true;
        }

        return false;
    }

    private static unsafe bool? ClickSelectStringExit()
    {
        if (SelectedLeve == null) return false;
        if (TryGetAddonByName<AtkUnitBase>("SelectString", out var addon) && HelpersOm.IsAddonAndNodesReady(addon))
        {
            if (HelpersOm.TryScanSelectStringText(addon, "取消", out var index))
                Click.SendClick($"select_string{index + 1}");

            addon->Close(true);
            TaskManager.Enqueue(InteractWithReceiver);

            return true;
        }

        return false;
    }

    private static unsafe bool? InteractWithReceiver()
    {
        if (IsOccupied()) return false;
        if (FindObjectToInteractWith(LeveReceiverDataId, out var foundObject))
        {
            TargetSystem.Instance()->InteractWithObject(foundObject);

            var levesSpan = QuestManager.Instance()->LeveQuestsSpan;
            var qualifiedCount = 0;

            for (var i = 0; i < levesSpan.Length; i++)
                if (LeveQuests.ContainsKey(levesSpan[i].LeveId)) // 判断是否为当前地图的理符
                    qualifiedCount++;

            if (ConfigOperationDelay > 0) TaskManager.DelayNext(ConfigOperationDelay);
            TaskManager.Enqueue(qualifiedCount > 1 ? ClickSelectQuest : InteractWithMete);
            return true;
        }

        return false;
    }

    private static unsafe bool? ClickSelectQuest()
    {
        if (SelectedLeve == null) return false;
        if (TryGetAddonByName<AtkUnitBase>("SelectIconString", out var addon) && HelpersOm.IsAddonAndNodesReady(addon))
        {
            if (HelpersOm.TryScanSelectIconStringText(addon, SelectedLeve.Value.Item2, out var index))
                Click.SendClick($"select_icon_string{index + 1}");

            TaskManager.Enqueue(InteractWithMete);
            return true;
        }

        return false;
    }

    public void Uninit()
    {
        Service.ClientState.TerritoryChanged -= OnZoneChanged;
        EndProcessHandler();
    }
}
