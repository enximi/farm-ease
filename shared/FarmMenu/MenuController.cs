using System.Text.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace FarmMenu;

internal sealed class MenuController
{
    private static readonly string[] Providers = { "zzz.TravelEase", "zzz.GardenEase", "zzz.FishingEase", "zzz.StorageEase" };
    internal Mod Mod { get; }
    internal MenuSettings Settings { get; private set; } = new();
    internal FarmMenuApi PublicApi { get; }
    internal IFarmMenuApi? Hub { get; private set; }
    internal bool IsOwner { get; private set; }
    internal Dictionary<string, MenuSection> Sections { get; } = new();
    internal Dictionary<string, MenuAction> Actions { get; } = new();
    internal Dictionary<string, Action> Refreshers { get; } = new();
    private bool nativeReady;
    private readonly string settingsPath;
    private readonly Action reloadFeature;

    internal EaseMenu? ActiveMenu => Game1.activeClickableMenu as EaseMenu
        ?? (Game1.activeClickableMenu as GameMenu)?.GetCurrentPage() as EaseMenu;

    internal MenuController(Mod mod, Action reloadFeature, Action<IFarmMenuApi> register)
    {
        Mod = mod;
        this.reloadFeature = reloadFeature;
        settingsPath = Path.Combine(Path.GetDirectoryName(mod.Helper.DirectoryPath)!, ".farm-ease-menu.json");
        PublicApi = new(this);
        ReloadSettings();
        mod.Helper.Events.GameLoop.GameLaunched += (_, _) => Connect(register);
        mod.Helper.Events.Input.ButtonPressed += OnButton;
        mod.Helper.Events.GameLoop.UpdateTicked += (_, _) => ActiveMenu?.TickInput();
        mod.Helper.Events.Display.MenuChanged += (_, e) =>
        {
            if (e.OldMenu is GameMenu oldMenu && e.NewMenu is GameMenu newMenu
                && oldMenu.GetCurrentPage() is HubPage oldPage && newMenu.GetCurrentPage() is HubPage newPage)
                newPage.RestoreSelection(oldPage);
        };
    }

    private void Connect(Action<IFarmMenuApi> register)
    {
        try
        {
            string owner = Providers.First(Mod.Helper.ModRegistry.IsLoaded);
            IsOwner = owner == Mod.ModManifest.UniqueID;
            Hub = IsOwner ? PublicApi : Mod.Helper.ModRegistry.GetApi<IFarmMenuApi>(owner)
                ?? throw new InvalidOperationException("无法连接农场随心菜单。");
            if (IsOwner)
            {
                NativeMenuIntegration.Install(this);
                nativeReady = true;
                Mod.Helper.ConsoleCommands.Add("farmease", "统一菜单：farmease [reload|status]", (_, args) =>
                {
                    if (args.FirstOrDefault() == "reload") ReloadAll();
                    else if (args.FirstOrDefault() == "status")
                    {
                        var entries = Sections.Values.Where(s => s.ParentId == "").Select(s => (s.Order, s.Title))
                            .Concat(Actions.Values.Where(a => a.SectionId == "").Select(a => (a.Order, Title: a.Title())))
                            .OrderBy(entry => entry.Order).Select(entry => entry.Title);
                        Mod.Monitor.Log($"菜单提供者={owner}; 功能={string.Join(",", entries)}; world={Context.IsWorldReady}; tabs={(Game1.activeClickableMenu as GameMenu)?.tabs.Count(t => t.name == NativeMenuIntegration.TabName)}; saves={Constants.SavesPath}", LogLevel.Info);
                    }
                    else Open("");
                });
            }
            Hub.RegisterRefresh(Mod.ModManifest.UniqueID, () => { ReloadSettings(); reloadFeature(); });
            register(Hub);
            Mod.Monitor.Log("已加入「农场随心」统一菜单：原生菜单最后一个芽苗标签，L3 / F7 直达。", LogLevel.Info);
        }
        catch (Exception error)
        {
            new HarmonyLib.Harmony(Mod.ModManifest.UniqueID + ".Menu").UnpatchAll(Mod.ModManifest.UniqueID + ".Menu");
            Mod.Monitor.Log($"统一菜单初始化失败：{error}", LogLevel.Error);
        }
    }

    internal void ReloadSettings()
    {
        try
        {
            var next = File.Exists(settingsPath)
                ? JsonSerializer.Deserialize<MenuSettings>(File.ReadAllText(settingsPath), MenuSettings.JsonOptions)
                    ?? throw new InvalidDataException("菜单设置不能为空。")
                : new MenuSettings();
            next.Normalize();
            Settings = next;
            if (!File.Exists(settingsPath)) File.WriteAllText(settingsPath, JsonSerializer.Serialize(next, MenuSettings.JsonOptions));
        }
        catch (Exception error) { Mod.Monitor.Log($"菜单设置读取失败，继续使用原设置：{error.Message}", LogLevel.Warn); }
    }

    internal void ReloadAll()
    {
        foreach (Action refresh in Refreshers.Values)
        {
            try { refresh(); }
            catch (Exception error) { Mod.Monitor.Log($"刷新配置失败：{error}", LogLevel.Error); }
        }
        if (ActiveMenu is HubPage page) page.Relayout();
        Notify("配置刷新完成；钓鱼参数从下一条鱼生效。");
    }

    internal bool CanUse(out string reason, bool allowOwnMenu = false)
    {
        reason = "";
        if (!Context.IsWorldReady) reason = "请先进入存档。";
        else if (Game1.eventUp || Game1.isFestival() || Game1.currentMinigame != null || Game1.dialogueUp)
            reason = "请先结束对话、活动或小游戏。";
        else if (Game1.fadeToBlack || Game1.fadeIn || Game1.locationRequest != null || Game1.killScreen || Game1.player.health <= 0)
            reason = "请等当前过渡结束。";
        else if (Game1.player.UsingTool || Game1.player.isRidingHorse() || Game1.player.swimming.Value)
            reason = "请先收起工具、下马或离开水中。";
        else if (Game1.activeClickableMenu != null && !(allowOwnMenu && (ActiveMenu != null || Hub?.IsOpen() == true)))
            reason = "请先关闭当前菜单。";
        else if (!allowOwnMenu && !Game1.player.CanMove) reason = "现在无法操作。";
        return reason.Length == 0;
    }

    internal void Open(string sectionId)
    {
        if (!IsOwner) { Hub?.Open(sectionId); return; }
        if (!nativeReady) { Notify("农场随心菜单尚未就绪。"); return; }
        if (!CanUse(out string reason, true)) { Notify(reason); return; }
        var host = new GameMenu(false);
        int index = host.pages.FindIndex(page => page is HubPage);
        if (index < 0) { Notify("农场随心标签未能创建，请查看 SMAPI 日志。"); return; }
        ((HubPage)host.pages[index]).SelectSection(sectionId);
        Game1.activeClickableMenu = host;
        host.changeTab(index);
    }

    private void OnButton(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady) return;
        if (ActiveMenu is { } menu)
        {
            if (menu is HubPage page && page.IsNativeInput(e.Button)) return;
            Mod.Helper.Input.Suppress(e.Button);
            menu.HandleButton(e.Button);
        }
        else if (IsOwner && (e.Button == Settings.MenuButton || e.Button == Settings.KeyboardMenuButton) && CanUse(out _))
        {
            Mod.Helper.Input.Suppress(e.Button);
            Open("");
        }
    }

    internal void Notify(string message)
    {
        if (Context.IsWorldReady) Game1.addHUDMessage(new HUDMessage(message));
        else Mod.Monitor.Log(message, LogLevel.Info);
    }
}
