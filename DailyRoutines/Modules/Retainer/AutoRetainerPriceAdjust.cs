using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DailyRoutines.Clicks;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game;
using Dalamud.Game.AddonLifecycle;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Internal.Notifications;
using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoRetainerPriceAdjustTitle", "AutoRetainerPriceAdjustDescription", ModuleCategories.Retainer)]
public unsafe partial class AutoRetainerPriceAdjust : IDailyModule
{
    public bool Initialized { get; set; }
    public bool WithConfigUI => true;

    private static TaskManager? TaskManager;

    private static int ConfigPriceReduction;
    private static int ConfigLowestPrice;
    private static int ConfigMaxPriceReduction;
    private static bool ConfigSeparateNQAndHQ;

    private static int CurrentItemPrice;
    private static int CurrentMarketLowestPrice;
    private static uint CurrentItemSearchItemID;
    private static bool IsCurrentItemHQ;
    private static RetainerManager.RetainerList.Retainer* CurrentRetainer;

    public void Init()
    {
        TaskManager ??= new TaskManager { AbortOnTimeout = true, TimeLimitMS = 10000, ShowDebug = false };

        Service.Config.AddConfig(this, "PriceReduction", 1);
        Service.Config.AddConfig(this, "LowestAcceptablePrice", 100);
        Service.Config.AddConfig(this, "SeparateNQAndHQ", false);
        Service.Config.AddConfig(this, "MaxPriceReduction", 0);

        ConfigPriceReduction = Service.Config.GetConfig<int>(this, "PriceReduction");
        ConfigLowestPrice = Service.Config.GetConfig<int>(this, "LowestAcceptablePrice");
        ConfigSeparateNQAndHQ = Service.Config.GetConfig<bool>(this, "SeparateNQAndHQ");
        ConfigMaxPriceReduction = Service.Config.GetConfig<int>(this, "MaxPriceReduction");

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerSellList", OnRetainerSellList);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerSell", OnRetainerSell);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "RetainerSell", OnRetainerSell);

        Service.Framework.Update += OnUpdate;

        Initialized = true;
    }

    public void ConfigUI()
    {
        ImGui.Text($"{Service.Lang.GetText("ConflictKey")}: {Service.Config.ConflictKey}");

        ImGui.SetNextItemWidth(150f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(
                $"{Service.Lang.GetText("AutoRetainerPriceAdjust-SinglePriceReductionValue")}##AutoRetainerPriceAdjust-SinglePriceReductionValue",
                ref ConfigPriceReduction, 100))
        {
            ConfigPriceReduction = Math.Max(1, ConfigPriceReduction);
            Service.Config.UpdateConfig(this, "SinglePriceReductionValue", ConfigPriceReduction);
        }


        ImGui.SetNextItemWidth(150f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(
                $"{Service.Lang.GetText("AutoRetainerPriceAdjust-LowestAcceptablePrice")}##AutoRetainerPriceAdjust-LowestAcceptablePrice",
                ref ConfigLowestPrice, 100))
        {
            ConfigLowestPrice = Math.Max(1, ConfigLowestPrice);
            Service.Config.UpdateConfig(this, "LowestAcceptablePrice", ConfigLowestPrice);
        }

        ImGui.SetNextItemWidth(150f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(
                $"{Service.Lang.GetText("AutoRetainerPriceAdjust-MaxPriceReduction")}##AutoRetainerPriceAdjust-MaxPriceReduction",
                ref ConfigMaxPriceReduction, 100))
        {
            ConfigMaxPriceReduction = Math.Max(0, ConfigMaxPriceReduction);
            Service.Config.UpdateConfig(this, "MaxPriceReduction", ConfigMaxPriceReduction);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Service.Lang.GetText("AutoRetainerPriceAdjust-MaxPriceReductionInputHelp"));

        if (ImGui.Checkbox(
                $"{Service.Lang.GetText("AutoRetainerPriceAdjust-SeparateNQAndHQ")}##AutoRetainerPriceAdjust-SeparateNQAndHQ",
                ref ConfigSeparateNQAndHQ))
            Service.Config.UpdateConfig(this, "SeparateNQAndHQ", ConfigSeparateNQAndHQ);

        ImGuiOm.HelpMarker(Service.Lang.GetText("AutoRetainerPriceAdjust-SeparateNQAndHQHelp"));
    }

    public void OverlayUI() { }

    private static void OnUpdate(Framework framework)
    {
        if (!TaskManager.IsBusy) return;

        if (Service.KeyState[Service.Config.ConflictKey])
        {
            TaskManager.Abort();
            P.PluginInterface.UiBuilder.AddNotification(Service.Lang.GetText("ConflictKey-InterruptMessage"),
                                                        "Daily Routines", NotificationType.Success);
        }
    }

    private static void OnRetainerSell(AddonEvent eventType, AddonArgs addonInfo)
    {
        switch (eventType)
        {
            case AddonEvent.PostSetup:
                if (TaskManager.IsBusy) return;
                // 点击比价
                TaskManager.Enqueue(ClickComparePrice);
                TaskManager.AbortOnTimeout = false;
                TaskManager.DelayNext(500);
                // 获取当前最低价，并退出
                TaskManager.Enqueue(GetLowestPrice);
                TaskManager.AbortOnTimeout = true;
                TaskManager.DelayNext(100);
                // 填写最低价
                TaskManager.Enqueue(FillLowestPrice);
                break;
            case AddonEvent.PreFinalize:
                if (TaskManager.NumQueuedTasks <= 1)
                    TaskManager.Abort();
                break;
        }
    }

    private static void OnRetainerSellList(AddonEvent type, AddonArgs args)
    {
        var activeRetainer = RetainerManager.Instance()->GetActiveRetainer();
        if (CurrentRetainer == null || CurrentRetainer != activeRetainer)
            CurrentRetainer = activeRetainer;
        else
            return;

        GetSellListItems(out var itemCount);
        if (itemCount == 0) return;

        for (var i = 0; i < itemCount; i++)
        {
            EnqueueSingleItem(i);
            CurrentMarketLowestPrice = 0;
        }
    }

    private static void EnqueueSingleItem(int index)
    {
        // 点击物品
        TaskManager.Enqueue(() => ClickSellingItem(index));
        TaskManager.DelayNext(100);
        // 点击修改价格
        TaskManager.Enqueue(ClickAdjustPrice);
        TaskManager.DelayNext(100);
        // 点击比价
        TaskManager.Enqueue(ClickComparePrice);
        TaskManager.DelayNext(500);
        TaskManager.AbortOnTimeout = false;
        // 获取当前最低价，并退出
        TaskManager.Enqueue(GetLowestPrice);
        TaskManager.AbortOnTimeout = true;
        TaskManager.DelayNext(100);
        // 填写最低价
        TaskManager.Enqueue(FillLowestPrice);
        TaskManager.DelayNext(800);
    }

    private static void GetSellListItems(out uint availableItems)
    {
        availableItems = 0;
        if (TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var addon) && HelpersOm.IsAddonAndNodesReady(addon))
        {
            for (var i = 0; i < 20; i++)
                if (InventoryManager.Instance()->GetInventoryContainer(InventoryType.RetainerMarket)->GetInventorySlot(
                        i)->ItemID != 0)
                    availableItems++;
        }
    }

    private static bool? ClickSellingItem(int index)
    {
        if (TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var addon) && HelpersOm.IsAddonAndNodesReady(addon))
        {
            var handler = new ClickRetainerSellListDR((nint)addon);
            handler.ItemEntry(index);
            return true;
        }

        return false;
    }

    private static bool? ClickAdjustPrice()
    {
        if (TryGetAddonByName<AtkUnitBase>("ContextMenu", out var addon) && HelpersOm.IsAddonAndNodesReady(addon))
        {
            var handler = new ClickRetainerSellListContextMenuDR((nint)addon);
            handler.AdjustPrice();

            return true;
        }

        return false;
    }

    private static bool? ClickComparePrice()
    {
        if (TryGetAddonByName<AtkUnitBase>("RetainerSell", out var addon) && HelpersOm.IsAddonAndNodesReady(addon))
        {
            CurrentItemPrice = addon->AtkValues[5].Int;
            IsCurrentItemHQ = Marshal.PtrToStringUTF8((nint)addon->AtkValues[1].String).Contains(''); // HQ 符号

            var handler = new ClickRetainerSellDR((nint)addon);
            handler.ComparePrice();

            return true;
        }

        return false;
    }

    private static bool? GetLowestPrice()
    {
        if (TryGetAddonByName<AddonItemSearchResult>("ItemSearchResult", out var addon) &&
            HelpersOm.IsAddonAndNodesReady(&addon->AtkUnitBase))
        {
            var ui = &addon->AtkUnitBase;
            if (!TryGetAddonByName<AtkUnitBase>("ItemHistory", out var historyAddon))
            {
                var handler = new ClickItemSearchResultDR();
                handler.History();
            }

            if (!HelpersOm.IsAddonAndNodesReady(historyAddon)) return false;

            CurrentItemSearchItemID = (uint)ui->AtkValues[0].Int;
            var errorMessage = addon->ErrorMessage->NodeText.ExtractText();
            if (errorMessage.Contains("没有搜索到任何结果"))
            {
                if (historyAddon->GetTextNodeById(11)->AtkResNode.IsVisible)
                {
                    CurrentMarketLowestPrice = 0;
                    ui->Close(true);
                    return true;
                }

                if (historyAddon->GetComponentListById(10)->ItemRendererList == null) return false;

                var result = ScanItemHistory(historyAddon);
                if (result.Any())
                {
                    if (ConfigSeparateNQAndHQ && IsCurrentItemHQ)
                    {
                        CurrentMarketLowestPrice = result.Where(x => x.HQ).OrderByDescending(x => x.Price).FirstOrDefault().Price;
                        if (CurrentMarketLowestPrice == 0)
                            CurrentMarketLowestPrice = result.OrderByDescending(x => x.Price).FirstOrDefault().Price;
                    }
                    else
                    {
                        CurrentMarketLowestPrice = result.OrderByDescending(x => x.Price).FirstOrDefault().Price;
                    }
                    ui->Close(true);
                    return true;
                }

                CurrentMarketLowestPrice = 0;
                ui->Close(true);
                return true;
            }

            if (addon->Results->ItemRendererList == null) return false;

            // 区分 HQ 和 NQ
            if (ConfigSeparateNQAndHQ && IsCurrentItemHQ)
            {
                var foundHQItem = false;
                for (var i = 0; i < 12 && !foundHQItem; i++)
                {
                    if (!TryScanItemSearchResult(addon, i, out var result)) break;
                    foundHQItem = result.isHQ;
                    if (!foundHQItem) continue;
                    CurrentMarketLowestPrice = result.Price;
                }

                if (!foundHQItem)
                {
                    if (!TryScanItemSearchResult(addon, 0, out var result)) return false;
                    CurrentMarketLowestPrice = result.Price;
                }
            }
            else
            {
                if (!TryScanItemSearchResult(addon, 0, out var result)) return false;
                CurrentMarketLowestPrice = result.Price;
            }

            ui->Close(true);
            return true;
        }

        return false;
    }

    private static bool? FillLowestPrice()
    {
        if (TryGetAddonByName<AddonRetainerSell>("RetainerSell", out var addon) &&
            HelpersOm.IsAddonAndNodesReady(&addon->AtkUnitBase))
        {
            var ui = &addon->AtkUnitBase;
            var priceComponent = addon->AskingPrice;
            var isOriginalPriceValid = int.TryParse(priceComponent->AtkComponentInputBase.AtkTextNode->NodeText.ExtractText(), out var originalPrice);
            var handler = new ClickRetainerSellDR((nint)addon);

            if (isOriginalPriceValid && CurrentMarketLowestPrice - ConfigPriceReduction == originalPrice)
            {
                handler.Decline();
                ui->Close(true);

                return true;
            }

            // 低于最低价
            if (CurrentMarketLowestPrice - ConfigPriceReduction < ConfigLowestPrice)
            {
                var message = Service.Lang.GetSeString("AutoRetainerPriceAdjust-WarnMessageReachLowestPrice",
                                                       SeString.CreateItemLink(
                                                           CurrentItemSearchItemID,
                                                           IsCurrentItemHQ
                                                               ? ItemPayload.ItemKind.Hq
                                                               : ItemPayload.ItemKind.Normal), CurrentMarketLowestPrice,
                                                       CurrentItemPrice, ConfigLowestPrice);
                Service.Chat.Print(message);

                handler.Decline();
                ui->Close(true);

                return true;
            }

            // 超过可接受的降价值
            if (ConfigMaxPriceReduction != 0 && CurrentItemPrice - CurrentMarketLowestPrice > ConfigLowestPrice)
            {
                var message = Service.Lang.GetSeString("AutoRetainerPriceAdjust-MaxPriceReductionMessage",
                                                       SeString.CreateItemLink(
                                                           CurrentItemSearchItemID,
                                                           IsCurrentItemHQ
                                                               ? ItemPayload.ItemKind.Hq
                                                               : ItemPayload.ItemKind.Normal), CurrentMarketLowestPrice,
                                                       CurrentItemPrice, ConfigMaxPriceReduction);
                Service.Chat.Print(message);

                handler.Decline();
                ui->Close(true);

                return true;
            }

            priceComponent->SetValue(CurrentMarketLowestPrice - ConfigPriceReduction);
            handler.Confirm();
            ui->Close(true);

            return true;
        }

        return false;
    }

    public static bool TryScanItemSearchResult(
        AddonItemSearchResult* addon, int index, out (int Price, bool isHQ) result)
    {
        result = (0, false);
        if (index < 0 || addon == null) return false;

        var list = addon->Results->ItemRendererList;
        if (list == null) return false;
        var itemEntry = addon->Results->ItemRendererList[index].AtkComponentListItemRenderer;
        if (itemEntry == null) return false;

        var listing = itemEntry->AtkComponentButton.AtkComponentBase;

        var priceText =
            SanitizeManager.Sanitize(listing.GetTextNodeById(5)->GetAsAtkTextNode()->NodeText.ExtractText());
        if (string.IsNullOrEmpty(priceText)) return false;
        if (!int.TryParse(priceText.Replace(",", ""), out result.Price)) return false;

        result.isHQ = listing.GetImageNodeById(3)->GetAsAtkImageNode()->AtkResNode.IsVisible;

        return true;
    }

    public static List<(bool HQ, int Price, int Amount)> ScanItemHistory(AtkUnitBase* addon)
    {
        var result = new List<(bool HQ, int Price, int Amount)>();
        var list = addon->GetComponentListById(10);

        for (var i = 0; i < list->ListLength; i++)
        {
            var listing = list->ItemRendererList[i].AtkComponentListItemRenderer->AtkComponentButton.AtkComponentBase.UldManager.NodeList;
            var isHQ = listing[8]->IsVisible;
            if (!int.TryParse(SanitizeManager.Sanitize(listing[6]->GetAsAtkTextNode()->NodeText.ExtractText()).Replace(",", ""), out var price)) continue;
            if (!int.TryParse(listing[5]->GetAsAtkTextNode()->NodeText.ExtractText(), out var amount)) continue;
            result.Add((isHQ, price, amount));
        }

        return result;
    }

    public void Uninit()
    {
        Service.Framework.Update -= OnUpdate;
        Service.AddonLifecycle.UnregisterListener(OnRetainerSellList);
        Service.AddonLifecycle.UnregisterListener(OnRetainerSell);
        TaskManager?.Abort();

        Initialized = false;
    }

    [GeneratedRegex("[^0-9]")]
    private static partial Regex AutoRetainerPriceAdjustRegex();
}
