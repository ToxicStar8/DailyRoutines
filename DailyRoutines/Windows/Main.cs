using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using DailyRoutines.Modules;
using Dalamud;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using ImGuiNET;
using Newtonsoft.Json;

namespace DailyRoutines.Windows;

public class Main : Window, IDisposable
{
    public class ModuleInfo
    {
        public Type             Module          { get; set; } = null!;
        public string[]?        PrecedingModule { get; set; }
        public string           ModuleName      { get; set; } = null!;
        public string           Title           { get; set; } = null!;
        public string           Description     { get; set; } = null!;
        public string?          Author          { get; set; }
        public bool             WithConfigUI    { get; set; }
        public bool             WithConfig      { get; set; }
        public ModuleCategories Category        { get; set; }
    }

    private static readonly List<ModuleInfo> Modules = [];
    private static readonly Dictionary<ModuleCategories, List<ModuleInfo>> categorizedModules = [];
    private static readonly List<ModuleInfo> ModulesFavorite = [];
    private static readonly List<ModuleInfo> ModulesEnabled = [];

    internal static ImageCarousel? ImageCarousel;

    private const ImGuiWindowFlags ChildFlags = ImGuiWindowFlags.NoScrollbar;

    private static Vector2 LeftTabComponentSize;
    private static Vector2 LogoComponentSize;
    private static Vector2 CategoriesComponentSize;

    private static Vector2 UpperTabComponentSize;
    private static Vector2 SettingsButtonSize;

    private static Vector2 RightTabComponentSize;
    private static Vector2 ChildGameCalendarsSize;
    private static Vector2 ChildGreetingSize;
    private static Vector2 ContactComponentSize;

    private static int SelectedTab;
    private static string GreetingText = string.Empty;
    private static string GreetingName = string.Empty;
    private static string GreetingPlace = string.Empty;
    internal static string SearchString = string.Empty;

    public Main() : base("Daily Routines - 主界面###DailyRoutines-Main")
    {
        Flags = ImGuiWindowFlags.NoScrollbar;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new(650, 400),
        };

        ForceMainWindow = true;
        SelectedTab = Service.Config.DefaultHomePage;

        if (Service.ClientState.ClientLanguage != (ClientLanguage)4)
        {
            WindowName =
                "Daily Routines - 主界面 (本插件不對國際服提供支持 / This Plugin ONLY Provides Help For CN Client)###DailyRoutines-Main";
        }

        RefreshModuleInfo();
        MainSettings.Init();
    }

    public override void Draw()
    {
        if (FontHelper.IsAnyFontBuilding)
        {
            ImGui.SetWindowFontScale(3f);
            var textSize = ImGui.CalcTextSize(Service.Lang.GetText("Settings-FontBuilding"));
            var pos = new Vector2((ImGui.GetWindowWidth() - textSize.X) / 2f, (ImGui.GetWindowHeight() - textSize.Y) / 2f);
            ImGui.SetCursorPos(pos);
            ImGui.Text(Service.Lang.GetText("Settings-FontBuilding"));
            ImGui.SetWindowFontScale(1f);
            return;
        }

        using (FontHelper.UIFont.Push())
        {
            DrawLeftTabComponent();

            ImGui.SameLine();
            using (ImRaii.Group())
            {
                DrawUpperTabComponent();
                DrawRightTabComponent();
            }
        }
    }

    #region 左侧
    private static void DrawLeftTabComponent()
    {
        float width;
        LeftTabComponentSize.Y = ImGui.GetContentRegionAvail().Y;

        using (ImRaii.Child("LeftTabComponentSize", LeftTabComponentSize, false, ChildFlags | ImGuiWindowFlags.NoScrollWithMouse))
        {
            using (ImRaii.Group())
            {
                ScaledDummy(1f, 16f);
                DrawLogoComponent();

                ScaledDummy(1f, 8f);
                DrawContactComponent();

                ScaledDummy(1f, 16f);
                DrawCategoriesComponent();
            }

            width = Math.Max(ImGui.GetItemRectSize().X, 250f * GlobalFontScale);
        }

        LeftTabComponentSize.X = width;
    }

    private static void DrawLogoComponent()
    {
        ImGuiHelpers.CenterCursorFor(LogoComponentSize.X);
        using (ImRaii.Group())
        {
            ImGuiHelpers.CenterCursorFor(72f * GlobalFontScale);
            ImGui.Image(PresetData.Icon.ImGuiHandle, ScaledVector2(72f));

            using (FontHelper.UIFont140.Push())
            {
                ImGuiHelpers.CenterCursorForText("Daily");
                ImGuiOm.Text("Daily");

                ImGui.SetCursorPosY(ImGui.GetCursorPosY() - (8f * GlobalFontScale));
                ImGuiHelpers.CenterCursorForText("Routines");
                ImGuiOm.Text("Routines");
            }

            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - (4f * GlobalFontScale));
            ImGuiHelpers.CenterCursorForText($"[{Plugin.Version}]");
            if (Plugin.Version < MainSettings.LatestVersionInfo.Version)
            {
                ImGui.TextColored(ImGuiColors.DPSRed, $"[{Plugin.Version}]");
                ImGuiOm.TooltipHover(Service.Lang.GetText("LowVersionWarning"));
            }
            else
                ImGuiOm.TextDisabledWrapped($"[{Plugin.Version}]");
        }
        
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (ImGui.IsItemClicked())
        {
            SearchString = string.Empty;
            SelectedTab = 0;
        }

        LogoComponentSize = ImGui.GetItemRectSize();
    }

    private static void DrawContactComponent()
    {
        ImGuiHelpers.CenterCursorFor(ContactComponentSize.X);
        using (ImRaii.Group())
        {
            ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ChildBg));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGuiColors.ParsedBlue);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImGuiColors.TankBlue);

            if (ImGuiOm.ButtonIcon("GitHub", FontAwesomeIcon.CodePullRequest, "GitHub"))
                Util.OpenLink("https://github.com/AtmoOmen/DailyRoutines");

            ImGui.SameLine();
            if (ImGuiOm.ButtonIcon("Bilibili", FontAwesomeIcon.PlayCircle, "Bilibili"))
                Util.OpenLink("https://space.bilibili.com/22008977");

            ImGui.SameLine();
            if (ImGuiOm.ButtonIcon("QQ 群", FontAwesomeIcon.Comments, "QQ 群"))
                Util.OpenLink("https://qm.qq.com/q/QlImB8pn2");

            ImGui.SameLine();
            if (ImGuiOm.ButtonIcon("爱发电", FontAwesomeIcon.Donate, "爱发电"))
                Util.OpenLink("https://afdian.net/a/AtmoOmen");
            ImGuiOm.TooltipHover(Service.Lang.GetText("DonateHelp"));

            ImGui.PopStyleColor(3);
        }
        ContactComponentSize = ImGui.GetItemRectSize();
    }

    private static void DrawCategoriesComponent()
    {
        using (FontHelper.UIFont120.Push())
        {
            var selectedModule = ModuleCategories.无;
            if (SelectedTab > 100)
                selectedModule = (ModuleCategories)(SelectedTab % 100);

            ImGuiHelpers.CenterCursorFor(CategoriesComponentSize.X);
            using (ImRaii.Group())
            {
                if (DrawCategorySelectButton(Service.Lang.GetText("Favorite"), SelectedTab == 3))
                {
                    SearchString = string.Empty;
                    SelectedTab = 3;
                }

                if (DrawCategorySelectButton(Service.Lang.GetText("Enabled"), SelectedTab == 4))
                {
                    SearchString = string.Empty;
                    SelectedTab = 4;
                }

                ScaledDummy(1f, 12f);

                foreach (var category in Enum.GetValues<ModuleCategories>())
                {
                    if (category == ModuleCategories.无) continue;

                    if (DrawCategorySelectButton(category.ToString(), selectedModule == category))
                    {
                        SearchString = string.Empty;
                        SelectedTab = 100 + (int)category;
                    }
                }
            }
            CategoriesComponentSize = ImGui.GetItemRectSize();
        }
    }

    private static bool DrawCategorySelectButton(string text, bool condition)
    {
        var buttonSize = new Vector2(180f * GlobalFontScale, ImGui.CalcTextSize("你好").Y);

        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGuiColors.ParsedBlue);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImGuiColors.TankBlue);
        ImGui.PushStyleColor(ImGuiCol.Button, 
                             condition ? ImGui.ColorConvertFloat4ToU32(ImGuiColors.TankBlue) : ImGui.GetColorU32(ImGuiCol.ChildBg));
        var button = ImGuiOm.ButtonIconWithText(FontAwesomeIcon.None, text, buttonSize);
        ImGui.PopStyleColor(3);

        return button;
    }

    #endregion

    #region 上方

    private static void DrawUpperTabComponent()
    {
        using (FontHelper.UIFont120.Push())
        {
            float height;
            UpperTabComponentSize.X = ImGui.GetContentRegionAvail().X;
            using (ImRaii.Child("ChildUpRight", UpperTabComponentSize, false, ChildFlags | ImGuiWindowFlags.NoScrollWithMouse))
            {
                ScaledDummy(1f, 8f);

                var startCursorPos = ImGui.GetCursorPos();
                var emptyString = string.Empty;

                // 真的输入框
                ImGui.SetCursorPos(startCursorPos with { X = startCursorPos.X + (36f * GlobalFontScale) });
                ImGui.SetNextItemWidth(
                    ImGui.GetContentRegionAvail().X - (24f * GlobalFontScale) - (ImGui.GetStyle().ItemSpacing.X * 2));
                ImGui.PushStyleColor(ImGuiCol.FrameBg, ImGui.GetColorU32(ImGuiCol.ChildBg));
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudWhite);
                ImGui.InputText("###Search", ref SearchString, 128);
                ImGui.PopStyleColor(2);

                // 假的输入框
                ImGui.SetCursorPos(startCursorPos);
                ImGui.BeginDisabled();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - SettingsButtonSize.X - ImGui.GetStyle().ItemSpacing.X);
                ImGui.InputText("###SearchDisplay", ref emptyString, 0, ImGuiInputTextFlags.ReadOnly);
                ImGui.EndDisabled();

                var inputHeight = ImGui.GetItemRectSize().Y;

                // 设置按钮
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ChildBg));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGuiColors.ParsedBlue);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImGuiColors.TankBlue);
                if (ImGui.Button($"{FontAwesomeIcon.Cog.ToIconString()}###Settings",
                                 new(32f * GlobalFontScale, inputHeight)))
                {
                    SelectedTab = 1;
                    SearchString = string.Empty;
                }
                ImGuiOm.TooltipHover(Service.Lang.GetText("Settings"));
                ImGui.PopStyleColor(3);
                SettingsButtonSize = ImGui.GetItemRectSize();

                height = ImGui.GetItemRectSize().Y * 2;

                // 搜素图标
                ImGui.AlignTextToFramePadding();
                ImGui.SameLine();
                ImGui.SetCursorPos(new(startCursorPos.X + 8f * GlobalFontScale, startCursorPos.Y + 4f * GlobalFontScale));

                using var font = ImRaii.PushFont(UiBuilder.IconFont);
                ImGui.Text(FontAwesomeIcon.Search.ToIconString());
            }
            UpperTabComponentSize.Y = height;
        }
    }

    #endregion

    #region 右侧
    private static void DrawRightTabComponent()
    {
        RightTabComponentSize = ImGui.GetContentRegionAvail();
        using (ImRaii.Child("RightTabComponentChild", RightTabComponentSize, false, ChildFlags | (SelectedTab == 0
                                                                                         ? ImGuiWindowFlags.NoScrollWithMouse
                                                                                         : ImGuiWindowFlags.None)))
        {
            // 0 - 主页; 1 - 设置; 2 - 搜索; 3 - 收藏; 4 - 已启用
            // 大于 100 - 模块分类
            if (!string.IsNullOrWhiteSpace(SearchString))
            {
                SelectedTab = 101;
                DrawModules(Modules, true);
                return;
            }

            switch (SelectedTab)
            {
                case 0:
                    DrawHomePage();
                    break;
                case 1:
                    MainSettings.Draw();
                    break;
                case 3:
                    DrawModules(ModulesFavorite);
                    break;
                case 4:
                    DrawModules(ModulesEnabled);
                    break;
                case > 100:
                    var selectedModule = (ModuleCategories)(SelectedTab % 100);
                    if (categorizedModules.TryGetValue(selectedModule, out var modules))
                        DrawModules(modules);
                    break;
            }
        }
    }

    private static void DrawHomePage()
    {
        ImGui.SetScrollHereY();
        using (ImRaii.Group())
        {
            ImGui.Image(PresetData.Icon.ImGuiHandle, ScaledVector2(72f));

            ImGui.SameLine();
            using (ImRaii.Group())
            {
                using (FontHelper.UIFont160.Push())
                    ImGuiOm.Text("Daily Routines");

                ImGuiOm.Text("Help With Some Boring Tasks");
            }
        }
        
        ScaledDummy(1f);

        ImGui.SameLine();
        ImGui.SetCursorPos(new(ImGui.GetContentRegionAvail().X - ChildGreetingSize.X, ImGui.GetCursorStartPos().Y));
        DrawHomePage_GreetingComponent();

        ScaledDummy(1f, 36f);

        using (ImRaii.Group())
        {
            using (ImRaii.Group())
            {
                ImageCarousel.Draw();

                ScaledDummy(1f, 4f);
                DrawHomePage_GameCalendarsComponent();
            }
            
            ImGui.SameLine();
            ScaledDummy(4f, 1f);

            ImGui.SameLine();

            using (ImRaii.Group())
            {
                ScaledDummy(1f, 8f);
                DrawHomePage_PluginInfoComponent();

                ScaledDummy(1f, 8f);
                DrawHomePage_ChangelogComponent();
            }
        }
    }

    private static void DrawHomePage_GreetingComponent()
    {
        if (Throttler.Throttle("Main-HomePage-GetGreetingText", 10000))
        {
            GreetingPlace = Service.ClientState.LocalPlayer?.HomeWorld.GameData?.Name?.RawString ?? "以太空间";
            GreetingName = Service.ClientState.LocalPlayer?.Name.TextValue ?? "光之战士";
            GreetingText = GetGreetingByTime();
        }

        using (FontHelper.UIFont140.Push())
        {
            var greetingObject = ImGui.CalcTextSize($"{GreetingPlace}, {GreetingName}");
            Vector2 size;
            using (ImRaii.Child("HomePage_Greeting", ChildGreetingSize, false, ChildFlags))
            {
                using (ImRaii.Group())
                {
                    var greetingTextSize = ImGui.CalcTextSize(GreetingText);
                    ImGui.SetCursorPosX(greetingObject.X - greetingTextSize.X - ImGui.GetStyle().ItemSpacing.X);
                    ImGui.Text(GreetingText);

                    ImGui.Text($"{GreetingPlace}, {GreetingName}");
                }

                size = ImGui.GetItemRectSize();
            }

            ChildGreetingSize = size;
        }
    }

    private static void DrawHomePage_GameCalendarsComponent()
    {
        if (MainSettings.GameCalendars is not { Count: > 0 }) return;

        using (FontHelper.UIFont80.Push())
        {
            ChildGameCalendarsSize.X = ImageCarousel.ChildSize.X;
            float height;
            using (ImRaii.Child("HomePage_GameEvents", ChildGameCalendarsSize))
            {
                using (ImRaii.Group())
                {
                    foreach (var activity in MainSettings.GameCalendars)
                    {
                        if (Service.Config.IsHideOutdatedEvent && activity.State == 2) continue;
                        var statusStr = activity.State == 2 ? Service.Lang.GetText("GameCalendar-EventEnded") : "";
                        ImGui.PushStyleColor(ImGuiCol.Button, activity.Color);
                        ImGui.BeginDisabled(activity.State == 2);
                        if (ImGuiOm.ButtonCompact($"{activity.Name}{activity.Url}", $"{activity.Name} {statusStr}"))
                            Util.OpenLink($"{activity.Url}");

                        ImGui.EndDisabled();
                        ImGui.PopStyleColor();

                        if (ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            ImGui.TextColored(ImGuiColors.DalamudOrange, "距离");

                            ImGui.SameLine();
                            ImGui.TextColored(ImGuiColors.HealerGreen,
                                              $"{(activity.State is 0 ? Service.Lang.GetText("End") : Service.Lang.GetText("Start"))}");

                            ImGui.SameLine();
                            ImGui.TextColored(ImGuiColors.DalamudOrange, "还有: ");

                            ImGui.SameLine();
                            ImGui.Text($"{activity.DaysLeft} 天");

                            ImGui.TextColored(ImGuiColors.DalamudOrange,
                                              activity.State is 0
                                                  ? $"{Service.Lang.GetText("EndTime")}: "
                                                  : $"{Service.Lang.GetText("StartTime")}: ");

                            ImGui.SameLine();
                            ImGui.Text(activity.State is 0 ? $"{activity.EndTime}" : $"{activity.BeginTime}");

                            ImGui.EndTooltip();
                        }
                    }
                }
                height = ImGui.GetItemRectSize().Y;
            }
            ChildGameCalendarsSize.Y = height;
        }
    }

    private static void DrawHomePage_PluginInfoComponent()
    {
        using (FontHelper.UIFont120.Push())
        {
            using (ImRaii.Group())
            {
                ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("CurrentVersion")}:");

                ImGui.SameLine();
                ImGui.TextColored(Plugin.Version < MainSettings.LatestVersionInfo.Version ? ImGuiColors.DPSRed : ImGuiColors.DalamudWhite, $"{Plugin.Version}");

                if (Plugin.Version < MainSettings.LatestVersionInfo.Version)
                    ImGuiOm.TooltipHover(Service.Lang.GetText("LowVersionWarning"));

                ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("LatestVersion")}:");

                ImGui.SameLine();
                ImGui.Text($"{MainSettings.LatestVersionInfo.Version}");

                ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("LatestDL")}:");

                ImGui.SameLine();
                ImGui.Text($"{MainSettings.LatestVersionInfo.DownloadCount}");

                ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("TotalDL")}:");

                ImGui.SameLine();
                ImGui.Text($"{MainSettings.TotalDownloadCounts}");
            }
        }
    }

    private static void DrawHomePage_ChangelogComponent()
    {
        var imageState0 = 
            ImageHelper.TryGetImage("https://gh.atmoomen.top/DailyRoutines/main/Assets/Images/Changelog.png", 
                                    out var imageWarpper0);

        var imageState1 =
            ImageHelper.TryGetImage("https://gh.atmoomen.top/DailyRoutines/main/Assets/Images/AfdianSponsor.jpg",
                                    out var imageWarpper1);

        var childSize = ImageCarousel.CurrentImageSize + (ImGui.GetStyle().ItemSpacing * 2) + new Vector2(50f) * GlobalFontScale;
        using (ImRaii.Child("HomePage_ChangelogComponent", childSize, false, ChildFlags | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (imageState0)
                if (ImGui.CollapsingHeader(
                        Service.Lang.GetText("Changelog", MainSettings.LatestVersionInfo.PublishTime.ToShortDateString())))
                {
                    ImGui.Image(imageWarpper0.ImGuiHandle, ImageCarousel.CurrentImageSize);
                    if (ImGui.IsItemHovered()) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    if (ImGui.IsItemClicked()) ImGui.OpenPopup("ChangelogPopup");

                    using var popup = ImRaii.Popup("ChangelogPopup");
                    if (popup.Success)
                        ImGui.Image(imageWarpper0.ImGuiHandle, imageWarpper0.Size * 0.8f);
                }

            if (imageState1)
                if (ImGui.CollapsingHeader($"{Service.Lang.GetText("Settings-AfdianSponsor")} (2024/05)"))
                {
                    ImGui.Image(imageWarpper1.ImGuiHandle, ImageCarousel.CurrentImageSize
                                    with
                                    { Y = ImageCarousel.CurrentImageSize.Y + (400f * GlobalFontScale) });
                    if (ImGui.IsItemHovered()) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    if (ImGui.IsItemClicked()) ImGui.OpenPopup("SponsorPopup");

                    using var popup = ImRaii.Popup("SponsorPopup");
                    if (popup.Success)
                        ImGui.Image(imageWarpper1.ImGuiHandle, imageWarpper1.Size * 0.4f);
                }
        }
    }

    private static void DrawModules(IReadOnlyList<ModuleInfo> modules, bool isFromSearch = false)
    {
        using (FontHelper.GetUIFont(0.9f).Push())
        {
            DrawModulesInternal(modules, isFromSearch);
        }
    }

    private static void DrawModulesInternal(IReadOnlyList<ModuleInfo> modules, bool isFromSearch = false)
    {
        for (var i = 0; i < modules.Count; i++)
        {
            var module = modules[i];
            if (!module.Title.Contains(SearchString.Trim(), StringComparison.OrdinalIgnoreCase) &&
                !module.Description.Contains(SearchString.Trim(), StringComparison.OrdinalIgnoreCase) &&
                !module.Module.Name.Contains(SearchString.Trim(), StringComparison.OrdinalIgnoreCase)) continue;

            ImGui.PushID($"{module.Category}-{module.Description}-{module.Title}-{module.Module}");
            DrawModuleUI(module, isFromSearch);
            ImGui.PopID();

            if (i < modules.Count - 1) ImGui.Separator();
        }
    }

    private static void DrawModuleUI(ModuleInfo moduleInfo, bool fromSearch)
    {
        ScaledDummy(1f, 4f);

        if (!Service.Config.ModuleEnabled.TryGetValue(moduleInfo.ModuleName, out var isModuleEnabled)) return;
        if (!Service.ModuleManager.Modules.TryGetValue(moduleInfo.Module, out var moduleInstance)) return;

        if (ImGuiOm.CheckboxColored("", ref isModuleEnabled))
        {
            if (isModuleEnabled) Service.ModuleManager.Load(moduleInstance, true);
            else Service.ModuleManager.Unload(moduleInstance, true);

            Task.Run(() =>
            {
                ModulesEnabled.Clear();
                ModulesEnabled.AddRange(Modules.Where(
                                            x => Service.Config.ModuleEnabled.TryGetValue(x.ModuleName, out var enabled) &&
                                                 enabled));
            });
        }

        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (fromSearch) ImGuiOm.TooltipHover(moduleInfo.Category.ToString());

        DrawModuleContextMenu(moduleInfo);

        ImGui.SameLine();
        var moduleText = $"[{moduleInfo.ModuleName}]";
        var origCursorPosX = ImGui.GetCursorPosX();
        ImGui.SetCursorPosX(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(moduleText).X + (4 * ImGui.GetStyle().ItemSpacing.X));
        ImGui.TextDisabled(moduleText);

        var isWithAuthor = !string.IsNullOrEmpty(moduleInfo.Author);
        if (isWithAuthor)
        {
            ImGui.SameLine();
            var spacing = isWithAuthor && isModuleEnabled ? 20f * GlobalFontScale : -20f * GlobalFontScale;
            ImGui.SetCursorPosX(origCursorPosX + ImGui.CalcTextSize(moduleInfo.Title).X +
                                (ImGui.GetStyle().ItemSpacing.X * 8) + spacing);

            ImGui.TextColored(ImGuiColors.DalamudGrey3, $"{Service.Lang.GetText("Author")}: {moduleInfo.Author}");
        }

        ImGui.SameLine();
        ImGui.SetCursorPosX(origCursorPosX);
        if (isModuleEnabled)
        {
            if (moduleInfo.WithConfigUI)
            {
                if (CollapsingHeader(moduleInfo.Title))
                {
                    ImGui.SetCursorPosX(origCursorPosX);
                    using (ImRaii.Group())
                        moduleInstance.ConfigUI();
                }
            }
            else
                ImGui.TextColored(ImGuiColors.DalamudYellow, moduleInfo.Title);
        }
        else
            ImGui.Text(moduleInfo.Title);

        ScaledDummy(1f, 4f);

        // 模块描述
        ImGui.SetCursorPosX(origCursorPosX);
        ImGuiOm.TextDisabledWrapped(moduleInfo.Description);

        // 前置模块
        ImGui.SetCursorPosX(origCursorPosX);
        using (ImRaii.Group())
        {
            if (moduleInfo.PrecedingModule is { Length: > 0 })
            {
                ImGuiOm.TextDisabledWrapped($"({Service.Lang.GetText("PrecedingModules")}:");
                for (var i = 0; i < moduleInfo.PrecedingModule.Length; i++)
                {
                    var pModule = moduleInfo.PrecedingModule[i];

                    ImGui.SameLine();
                    ImGui.TextColored(ImGuiColors.DalamudYellow, pModule);

                    if (ImGui.IsItemHovered())
                        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

                    if (ImGui.IsItemClicked())
                        SearchString = pModule;

                    if (i < moduleInfo.PrecedingModule.Length - 1)
                    {
                        ImGui.SameLine();
                        ImGui.TextDisabled("/");
                    }
                }

                ImGui.SameLine(0, 0);
                ImGuiOm.TextDisabledWrapped(")");
            }
        }

        return;

        bool CollapsingHeader(string title)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, isModuleEnabled ? ImGuiColors.DalamudYellow : ImGuiColors.DalamudWhite);
            ImGui.PushStyleColor(ImGuiCol.Header, ImGui.ColorConvertFloat4ToU32(new Vector4(0)));
            var collapsingHeader = ImGui.CollapsingHeader(title);
            ImGui.PopStyleColor(2);

            return collapsingHeader;
        }
    }

    private static void DrawModuleContextMenu(ModuleInfo moduleInfo)
    {
        using var popup = ImRaii.ContextPopupItem($"ContextMenu_{moduleInfo.Title}_{moduleInfo.Description}_{moduleInfo.Module.Name}");
        if (!popup.Success) return;
        using (FontHelper.UIFont120.Push())
        {
            ImGui.Text($"{moduleInfo.Title}");
        }

        using (FontHelper.UIFont80.Push())
        {
            ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("Settings-ModuleInfoCategory")}:");

            ImGui.SameLine();
            ImGui.Text($"{moduleInfo.Category}");

            ImGui.SameLine();
            ImGui.Text("/");

            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("Settings-ModuleInfoAuthor")}:");

            ImGui.SameLine();
            ImGui.Text($"{moduleInfo.Author ?? "AtmoOmen"}");

            if (OnlineStatsManager.ModuleUsageStats.TryGetValue(moduleInfo.Title, out var amount))
            {
                ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("Settings-EnabledAmount")}:");

                ImGui.SameLine();
                ImGui.Text($"{amount + (Service.Config.ModuleEnabled[moduleInfo.Module.Name] ? 1 : 0)}");
            }

            ImGui.Separator();
            ScaledDummy(1f);

            var isFavorite = Service.Config.ModuleFavorites.Contains(moduleInfo.Module.Name);
            if (ImGui.Selectable($"    {(isFavorite ? "\u2605 " : "")}{Service.Lang.GetText("Favorite")}", isFavorite, ImGuiSelectableFlags.DontClosePopups))
            {
                if (!Service.Config.ModuleFavorites.Remove(moduleInfo.Module.Name))
                    Service.Config.ModuleFavorites.Add(moduleInfo.Module.Name);

                ModulesFavorite.Clear();
                ModulesFavorite.AddRange(Modules.Where(x => Service.Config.ModuleFavorites.Contains(x.Module.Name)));
            }

            ImGui.Separator();

            if (moduleInfo.PrecedingModule != null)
            {
                if (ImGui.Selectable($"    {Service.Lang.GetText("Settings-EnableAllPModules")}", false, ImGuiSelectableFlags.DontClosePopups))
                {
                    foreach (var pModuleType in moduleInfo.Module.GetCustomAttribute<PrecedingModuleAttribute>().Modules)
                        Service.ModuleManager.Load(pModuleType, true);

                    ModulesFavorite.Clear();
                    ModulesFavorite.AddRange(Modules.Where(x => Service.Config.ModuleFavorites.Contains(x.Module.Name)));
                }

                ImGui.Separator();

                if (ImGui.Selectable($"    {Service.Lang.GetText("Settings-DisableAllPModules")}", false, ImGuiSelectableFlags.DontClosePopups))
                {
                    foreach (var pModuleType in moduleInfo.Module.GetCustomAttribute<PrecedingModuleAttribute>().Modules)
                        Service.ModuleManager.Unload(pModuleType, true);

                    ModulesFavorite.Clear();
                    ModulesFavorite.AddRange(Modules.Where(x => Service.Config.ModuleFavorites.Contains(x.Module.Name)));
                }

                ImGui.Separator();
            }

            if (ImGuiOm.Selectable($"    {Service.Lang.GetText("Settings-ResetModule")}"))
            {
                Task.Run(() =>
                {
                    if (!Service.Config.ModuleEnabled.TryGetValue(moduleInfo.Module.Name, out var isModuleEnabled))
                        return;

                    var module = Service.ModuleManager.Modules[moduleInfo.Module];
                    if (isModuleEnabled) Service.ModuleManager.Unload(module);

                    if (moduleInfo.WithConfig)
                        File.Delete(Path.Join(Service.PluginInterface.ConfigDirectory.FullName, $"{moduleInfo.Module.Name}.json"));

                    if (isModuleEnabled) Service.ModuleManager.Load(module);

                    NotifyHelper.NotificationSuccess(Service.Lang.GetText("Settings-ResetModuleSuccessNotice", moduleInfo.Title));
                });
            }

            if (moduleInfo.WithConfig)
            {
                ImGui.Separator();
                if (ImGuiOm.Selectable($"    {Service.Lang.GetText("Settings-ModuleConfiguration")}"))
                    OpenFileOrFolder
                        (Path.Join(Service.PluginInterface.ConfigDirectory.FullName, $"{moduleInfo.Module.Name}.json"));
            }

            if (moduleInfo.WithConfigUI)
            {
                ImGui.Separator();
                if (ImGuiOm.Selectable($"    {Service.Lang.GetText("Settings-ShowOverlayConfig")}"))
                {
                    var module = Service.ModuleManager.Modules[moduleInfo.Module];
                    module.OverlayConfig ??= new(module);
                    module.OverlayConfig.IsOpen ^= true;
                }
            }
        }
    }
    #endregion

    private static void RefreshModuleInfo()
    {
        Task.Run(() =>
        {
            ImageHelper.GetImage("https://gh.atmoomen.top/DailyRoutines/main/Assets/Images/icon.png");

            var allModules = Assembly.GetExecutingAssembly().GetTypes()
                                     .Where(t => typeof(DailyModuleBase).IsAssignableFrom(t) &&
                                                 t is { IsClass: true, IsAbstract: false })
                                     .Select(type => new ModuleInfo
                                     {
                                         Module = type,
                                         PrecedingModule = type.GetCustomAttribute<PrecedingModuleAttribute>()?.Modules
                                                               .Select(t => t.Name + "Title")
                                                               .Select(title => Service.Lang.GetText(title))
                                                               .ToArray(),
                                         ModuleName = type.Name,
                                         Title = Service.Lang.GetText(
                                             type.GetCustomAttribute<ModuleDescriptionAttribute>()?.TitleKey ??
                                             "DevModuleTitle"),
                                         Description = Service.Lang.GetText(
                                             type.GetCustomAttribute<ModuleDescriptionAttribute>()?.DescriptionKey ??
                                             "DevModuleDescription"),
                                         Category = type.GetCustomAttribute<ModuleDescriptionAttribute>()?.Category ??
                                                    ModuleCategories.一般,
                                         Author = ((DailyModuleBase)Activator.CreateInstance(type)!).Author,
                                         WithConfigUI = type
                                                        .GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                                                    BindingFlags.DeclaredOnly)
                                                        .Any(m => m.Name == "ConfigUI" &&
                                                                  m.DeclaringType != typeof(DailyModuleBase)),
                                         WithConfig = File.Exists(
                                             Path.Join(Service.PluginInterface.ConfigDirectory.FullName,
                                                       $"{type.Name}.json")),
                                     })
                                     .ToList();

            Modules.AddRange(allModules);
            allModules.GroupBy(m => m.Category).ToList().ForEach(group =>
            {
                categorizedModules[group.Key] =
                    [.. group.OrderBy(m => m.Title)];
            });

            ModulesFavorite.Clear();
            ModulesFavorite.AddRange(allModules.Where(x => Service.Config.ModuleFavorites.Contains(x.Module.Name)));
            ModulesEnabled.Clear();
            ModulesEnabled.AddRange(allModules.Where(
                                        x => Service.Config.ModuleEnabled.TryGetValue(x.ModuleName, out var enabled) &&
                                             enabled));
        });
    }

    private static string GetGreetingByTime()
    {
        return DateTime.Now.Hour switch
        {
            >= 18 => "晚上好",
            >= 17 => "傍晚好",
            >= 14 => "下午好",
            >= 11 => "中午好",
            >= 9 => "上午好",
            >= 7 => "早上好",
            >= 5 => "清晨好",
            _ => "凌晨好",
        };
    }

    public void Dispose()
    {
        MainSettings.Uninit();
        Service.Config.Save();
    }
}

public class MainSettings
{
    public class VersionInfo
    {
        public Version  Version       { get; set; } = new();
        public DateTime PublishTime   { get; set; } = DateTime.MinValue;
        public string   Changelog     { get; set; } = string.Empty;
        public int      DownloadCount { get; set; }
    }

    public class GameEvent
    {
        public uint                ID            { get; set; }
        public DalamudLinkPayload? LinkPayload   { get; set; }
        public uint                LinkPayloadID { get; set; }
        public string              Name          { get; set; } = string.Empty;
        public string              Url           { get; set; } = string.Empty;
        public DateTime            BeginTime     { get; set; } = DateTime.MinValue;
        public DateTime            EndTime       { get; set; } = DateTime.MaxValue;
        public Vector4             Color         { get; set; }

        /// <summary>
        ///     0 - 正在进行; 1 - 未开始; 2 - 已结束
        /// </summary>
        public uint State { get; set; }

        /// <summary>
        ///     如果已结束, 则为 -1
        /// </summary>
        public int DaysLeft { get; set; } = int.MaxValue;
    }

    public class GameNews
    {
        public string Title         { get; set; } = string.Empty;
        public string Url           { get; set; } = string.Empty;
        public string PublishDate   { get; set; } = string.Empty;
        public string Summary       { get; set; } = string.Empty;
        public string HomeImagePath { get; set; } = string.Empty;
        public int    SortIndex     { get; set; }
    }

    internal static string ConflictKeySearchString = string.Empty;
    internal static readonly HttpClient client = new();
    internal static int TotalDownloadCounts;
    internal static VersionInfo LatestVersionInfo = new();
    internal static List<GameEvent> GameCalendars = [];
    internal static readonly List<GameNews> GameNewsList = [];

    internal static Dictionary<int, string> PagesInfo = new()
    {
        { 0, "主页" },
        { 1, "设置" },
        { 3, "收藏" },
    };

    internal static void Init()
    {
        ObtainNecessityInfo();
        Service.ClientState.Login += OnLogin;
    }

    internal static void Draw()
    {
        DrawGlobalConfig();

        ImGui.Separator();

        DrawTooltips();
    }

    internal static void DrawGlobalConfig()
    {
        // 语言
        ImGuiOm.TextIcon(FontAwesomeIcon.Globe, Service.Lang.GetText("Language"));

        ImGui.SameLine();
        ImGui.BeginDisabled();
        ImGui.SetNextItemWidth(180f * GlobalFontScale);
        using (ImRaii.Combo("##LanguagesList", "简体中文")) { }
        ImGui.EndDisabled();

        ImGui.Spacing();

        // 模块配置
        ImGuiOm.TextIcon(FontAwesomeIcon.FolderOpen, Service.Lang.GetText("ModulesConfig"));

        ImGui.SameLine();
        if (ImGui.Button(Service.Lang.GetText("OpenFolder")))
            OpenFileOrFolder(Service.PluginInterface.ConfigDirectory.FullName);

        ImGuiOm.TooltipHover(Service.Lang.GetText("ModulesConfigHelp"));

        ImGui.Spacing();

        // 打断热键
        ImGuiOm.TextIcon(FontAwesomeIcon.Keyboard, Service.Lang.GetText("ConflictKey"));

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f * GlobalFontScale);
        using (var combo = ImRaii.Combo("##GlobalConflictHotkey", Service.Config.ConflictKey.ToString()))
        {
            if (combo.Success)
            {
                ImGui.SetNextItemWidth(-1f);
                ImGui.InputTextWithHint("##ConflictKeySearchBar", $"{Service.Lang.GetText("PleaseSearch")}...",
                                        ref ConflictKeySearchString, 20);

                ImGui.Separator();

                var validKeys = Service.KeyState.GetValidVirtualKeys();
                foreach (var keyToSelect in validKeys)
                {
                    if (!string.IsNullOrWhiteSpace(ConflictKeySearchString) && !keyToSelect.GetFancyName()
                            .Contains(ConflictKeySearchString, StringComparison.OrdinalIgnoreCase)) continue;

                    if (ImGui.Selectable(keyToSelect.GetFancyName()))
                    {
                        Service.Config.ConflictKey = keyToSelect;
                        Service.Config.Save();
                    }
                }
            }
        }
        ImGuiOm.TooltipHover(Service.Lang.GetText("ConflictKeyHelp"));

        ImGui.Spacing();

        // 匿名数据上传
        ImGuiOm.TextIcon(FontAwesomeIcon.Database, Service.Lang.GetText("Settings-AllowAnonymousUpload"));

        ImGui.SameLine();
        var allowState = Service.Config.AllowAnonymousUpload;
        if (ImGui.Checkbox("###AllowAnonymousUpload", ref allowState))
        {
            Service.Config.AllowAnonymousUpload ^= true;
            Service.Config.Save();

            if (Service.Config.AllowAnonymousUpload)
            {
                Task.Run(async () =>
                             await OnlineStatsManager.UploadEntry(
                                 new OnlineStatsManager.ModulesState(OnlineStatsManager.GetEncryptedMachineCode())));
            }
        }
        ImGuiOm.TooltipHover(Service.Lang.GetText("Settings-AllowAnonymousUploadHelp"), 25f);

        // 游戏活动日历
        ImGuiOm.TextIcon(FontAwesomeIcon.Calendar, Service.Lang.GetText("Settings-SendCalendarToCharWhenLogin"));

        ImGui.SameLine();
        var checkboxBool = Service.Config.SendCalendarToChatWhenLogin;
        if (ImGui.Checkbox("###SendCalendarToCharWhenLogin", ref checkboxBool))
        {
            Service.Config.SendCalendarToChatWhenLogin ^= true;
            Service.Config.Save();
        }

        ImGuiOm.TextIcon(FontAwesomeIcon.CalendarAlt, Service.Lang.GetText("Settings-HideOutdatedEvents"));
        
        ImGui.SameLine();
        var checkboxBool2 = Service.Config.IsHideOutdatedEvent;
        if (ImGui.Checkbox("###HideOutdatedEvents", ref checkboxBool2))
        {
            Service.Config.IsHideOutdatedEvent ^= true;
            Service.Config.Save();
        }

        // 启用 TTS
        ImGuiOm.TextIcon(FontAwesomeIcon.Microphone, Service.Lang.GetText("Settings-EnableTTS"));

        ImGui.SameLine();
        var enableTTS = Service.Config.EnableTTS;
        ImGui.SetNextItemWidth(150f * GlobalFontScale);
        if (ImGui.Checkbox("###EnableTTS", ref enableTTS))
        {
            Service.Config.EnableTTS = enableTTS;
            Service.Config.Save();
        }

        // 界面文本字号
        ImGuiOm.TextIcon(FontAwesomeIcon.Font, Service.Lang.GetText("Settings-InterfaceFontSize"));

        ImGui.SameLine();
        var fontTemp = Service.Config.InterfaceFontSize;
        ImGui.SetNextItemWidth(150f * GlobalFontScale);
        if (ImGui.InputFloat("###InterfaceFontInput", ref fontTemp, 0, 0, "%.1f"))
            fontTemp = Math.Clamp(fontTemp, 8, 48);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            Service.Config.InterfaceFontSize = fontTemp;
            Service.Config.Save();

            RebuildInterfaceFont();
        }

        // 默认页面
        ImGuiOm.TextIcon(FontAwesomeIcon.Home, Service.Lang.GetText("Settings-DefaultHomePage"));

        ImGui.SameLine();
        var defaultHomePage = Service.Config.DefaultHomePage;
        var previewString = defaultHomePage > 100 ? 
                                ((ModuleCategories)(defaultHomePage % 100)).ToString() : 
                                PagesInfo[defaultHomePage];

        ImGui.SetNextItemWidth(150f * GlobalFontScale);
        using (var combo = ImRaii.Combo("###DefaultHomePageSelectCombo", previewString))
        {
            if (combo.Success)
            {
                foreach (var buttonInfo in PagesInfo)
                {
                    if (ImGuiOm.Selectable(buttonInfo.Value))
                    {
                        Service.Config.DefaultHomePage = buttonInfo.Key;
                        Service.Config.Save();
                    }
                }

                foreach (var buttonInfo in Enum.GetValues<ModuleCategories>())
                {
                    if (buttonInfo == ModuleCategories.无) continue;

                    if (ImGuiOm.Selectable(buttonInfo.ToString()))
                    {
                        Service.Config.DefaultHomePage = (int)buttonInfo + 100;
                        Service.Config.Save();
                    }
                }
            }
        }
    }

    internal static void DrawTooltips()
    {
        ImGui.TextColored(ImGuiColors.DalamudYellow, $"{Service.Lang.GetText("Settings-TipMessage0")}:");
        ImGui.TextWrapped(Service.Lang.GetText("Settings-TipMessage1"));
        ImGui.TextWrapped(Service.Lang.GetText("Settings-TipMessage2"));
        ImGui.TextWrapped(Service.Lang.GetText("Settings-TipMessage3"));
    }

    internal static void ObtainNecessityInfo()
    {
        Task.Run(async () =>
        {
            ImageHelper.GetImage("https://gh.atmoomen.top/DailyRoutines/main/Assets/Images/Changelog.png");
            ImageHelper.GetImage("https://gh.atmoomen.top/DailyRoutines/main/Assets/Images/AfdianSponsor.jpg");
            await GetGameCalendar();
            await GetGameNews();
            TotalDownloadCounts = await GetTotalDownloadsAsync();
            LatestVersionInfo = await GetLatestVersionAsync("AtmoOmen", "DailyRoutines");

            RebuildInterfaceFont();
        });
    }

    internal static void RebuildInterfaceFont()
    {
        FontHelper.GetUIFont(0.9f);
        for (var i = 0.6f; i < 1.8f; i += 0.2f)
            FontHelper.GetUIFont(i);
    }

    private static void OnLogin()
    {
        if (!Service.Config.SendCalendarToChatWhenLogin) return;
        if (GameCalendars.Any(x => x.BeginTime <= DateTime.Now && DateTime.Now <= x.EndTime))
        {
            Service.Chat.Print(new SeStringBuilder()
                               .AddUiForeground("[Daily Routines]", 34)
                               .AddUiForeground(
                                   $" {DateTime.Now.ToShortDateString()} {Service.Lang.GetText("GameCalendar")}", 2)
                               .Build());

            var orderNumber = 1;
            foreach (var gameEvent in GameCalendars)
            {
                if (gameEvent.State != 0) continue;
                var message = new SeStringBuilder().AddUiForeground($"{orderNumber}. ", 2)
                                                   .Add(gameEvent.LinkPayload)
                                                   .AddUiForeground($"{gameEvent.Name}", 25)
                                                   .Add(RawPayload.LinkTerminator)
                                                   .AddUiForeground(
                                                       $" ({Service.Lang.GetText("GameCalendar-EndTimeMessage",
                                                                                 gameEvent.DaysLeft)})", 2)
                                                   .Build();

                Service.Chat.Print(message);
                orderNumber++;
            }
        }
    }

    internal static async Task<int> GetTotalDownloadsAsync()
    {
        const string url = "https://gh.atmoomen.top/DailyRoutines/main/Assets/downloads.txt";
        var response = await client.GetStringAsync(url);
        return int.TryParse(response, out var totalDownloads) ? totalDownloads : 0;
    }

    internal static async Task<VersionInfo> GetLatestVersionAsync(string userName, string repoName)
    {
        var url = $"https://api.github.com/repos/{userName}/{repoName}/releases/latest";
        client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");
        var response = await client.GetStringAsync(url);
        var latestRelease = JsonConvert.DeserializeObject<FileFormat.GitHubRelease>(response);

        var totalDownloads = 0;
        var version = new VersionInfo();
        foreach (var asset in latestRelease.assets) totalDownloads += asset.download_count * 2;

        version.Version = new Version(latestRelease.tag_name);
        version.PublishTime = latestRelease.published_at;
        version.Changelog = MarkdownToPlainText(latestRelease.body);
        version.DownloadCount = totalDownloads;

        ImageHelper.GetImage("https://gh.atmoomen.top/DailyRoutines/main/Assets/Images/Changelog.png");

        return version;
    }

    internal static async Task GetGameCalendar()
    {
        const string url = "https://apiff14risingstones.web.sdo.com/api/home/active/calendar/getActiveCalendarMonth";
        var response = await client.GetStringAsync(url);
        var result = JsonConvert.DeserializeObject<FileFormat.RSActivityCalendar>(response);

        if (result.data.Count > 0)
        {
            foreach (var activity in GameCalendars)
                Service.LinkPayloadManager.Unregister(activity.LinkPayloadID);
            GameCalendars.Clear();

            foreach (var activity in result.data)
            {
                var currentTime = DateTime.Now;
                var beginTime = UnixSecondToDateTime(activity.begin_time);
                var endTime = UnixSecondToDateTime(activity.end_time);
                var gameEvent = new GameEvent
                {
                    ID = activity.id,
                    LinkPayload = Service.LinkPayloadManager.Register(OpenGameEventLinkPayload, out var linkPayloadID),
                    LinkPayloadID = linkPayloadID,
                    Name = activity.name,
                    Url = activity.url,
                    BeginTime = beginTime,
                    EndTime = endTime,
                    Color = DarkenColor(HexToVector4(activity.color), 0.3f),
                    State = currentTime < beginTime ? 1U :
                            currentTime <= endTime ? 0U : 2U,
                    DaysLeft = currentTime < beginTime ? (beginTime - DateTime.Now).Days :
                               currentTime <= endTime ? (endTime - DateTime.Now).Days : int.MaxValue,
                };

                GameCalendars.Add(gameEvent);
            }

            GameCalendars = [..GameCalendars.OrderBy(x => x.DaysLeft)];
        }
    }

    internal static void OpenGameEventLinkPayload(uint commandID, SeString message)
    {
        var link = GameCalendars.FirstOrDefault(x => x.LinkPayloadID == commandID)?.Url;
        if (!string.IsNullOrWhiteSpace(link))
            Util.OpenLink(link);
    }

    internal static async Task GetGameNews()
    {
        const string url =
            "https://cqnews.web.sdo.com/api/news/newsList?gameCode=ff&CategoryCode=5309,5310,5311,5312,5313&pageIndex=0&pageSize=5";

        var response = await client.GetStringAsync(url);
        var result = JsonConvert.DeserializeObject<FileFormat.RSGameNews>(response);

        if (result.Data.Count > 0)
        {
            GameNewsList.Clear();
            foreach (var activity in result.Data)
            {
                var gameNews = new GameNews
                {
                    Title = activity.Title,
                    Url = activity.Author,
                    SortIndex = activity.SortIndex,
                    Summary = activity.Summary,
                    HomeImagePath = activity.HomeImagePath,
                    PublishDate = activity.PublishDate,
                };

                GameNewsList.Add(gameNews);
                ImageHelper.GetImage(activity.HomeImagePath);
            }

            Main.ImageCarousel = new(GameNewsList);
        }
    }

    public static void Uninit()
    {
        Service.ClientState.Login -= OnLogin;
        foreach (var gameEvent in GameCalendars)
            Service.PluginInterface.RemoveChatLinkHandler(gameEvent.ID);
    }
}

public class ImageCarousel(IReadOnlyList<MainSettings.GameNews> newsList)
{
    public IReadOnlyList<MainSettings.GameNews> News           { get; set; } = newsList;
    public float                                ChangeInterval { get; set; } = 5.0f;
    public Vector2                              ChildSize      { get; set; }

    public readonly Vector2 CurrentImageSize = ScaledVector2(375, 200);

    private int currentIndex;
    private long lastImageChangeTime;

    private void PreDraw()
    {
        if (Environment.TickCount64 - lastImageChangeTime > ChangeInterval * 1000)
        {
            currentIndex = (currentIndex + 1) % News.Count;
            lastImageChangeTime = Environment.TickCount64;
        }

        var singleCharSize = ImGui.CalcTextSize("测");
        var itemSpacing = ImGui.GetStyle().ItemSpacing;
        ChildSize = new Vector2(CurrentImageSize.X + (2 * itemSpacing.X), CurrentImageSize.Y + (singleCharSize.Y * 2.5f));
    }

    public void Draw()
    {
        if (News.Count == 0) return;
        PreDraw();

        using (ImRaii.Child("NewsImageCarousel", ChildSize, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            var news = News[currentIndex];
            if (ImageHelper.TryGetImage(news.HomeImagePath, out var imageHandle))
            {
                ImGui.Image(imageHandle.ImGuiHandle, CurrentImageSize);
            }
            else
            {
                ImGui.Dummy(CurrentImageSize);
            }

            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            if (ImGui.IsItemClicked())
                Util.OpenLink(news.Url);

            ImGui.Indent(2f * GlobalFontScale);
            ImGui.TextWrapped(news.Title);
            ImGui.Unindent(2f * GlobalFontScale);
        }
    }
}
