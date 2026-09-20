using FarmMenu;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace TravelEase;

public sealed class ModEntry : Mod
{
    internal ModConfig Config { get; private set; } = new();
    internal MenuController Menu { get; private set; } = null!;
    internal TravelService Travel { get; private set; } = null!;
    private sealed class HoldState { internal SButton Button = SButton.None; internal double Elapsed; internal bool Triggered; }
    private readonly PerScreen<HoldState> holds = new(() => new());
    private SButton heldHome { get => holds.Value.Button; set => holds.Value.Button = value; }
    private double homeElapsed { get => holds.Value.Elapsed; set => holds.Value.Elapsed = value; }
    private bool homeTriggered { get => holds.Value.Triggered; set => holds.Value.Triggered = value; }

    public override void Entry(IModHelper helper)
    {
        ReloadConfig();
        Travel = new(this);
        Menu = new(this, ReloadConfig, RegisterMenu);
        helper.Events.Input.ButtonPressed += OnButton;
        helper.Events.GameLoop.UpdateTicked += OnUpdate;
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => ResetSession();
        helper.Events.GameLoop.DayStarted += (_, _) => ResetSession();
        helper.Events.Player.Warped += (_, e) => { if (e.IsLocalPlayer) ResetHold(); };
        helper.Events.Display.RenderedHud += OnHud;
        helper.ConsoleCommands.Add("travelease", "随心往返：travelease [reload|home]", (_, args) =>
        {
            if (args.FirstOrDefault() == "reload") ReloadConfig();
            else if (args.FirstOrDefault() == "home") Travel.GoHome();
            else Menu.Hub?.Open("travel");
        });
    }
    public override object GetApi() => Menu.PublicApi;

    private void RegisterMenu(IFarmMenuApi api)
    {
        api.RegisterSection("travel", "", "随心往返", "回家、前往各地，或返回上一次的位置。", 10);
        api.RegisterAction("travel", "travel.home", () => "回家", () => $"长按 {Ui.Button(Config.HomeButton)} / {Ui.Button(Config.KeyboardHomeButton)} 也可回家，松开取消。",
            () => Travel.GoHome(), () => true, 10);
        api.RegisterSection("travel.destinations", "travel", "选择目的地", "选择固定落脚区域，自动避开障碍。", 20);
        api.RegisterAction("travel", "travel.return", () => "返回上个位置", () => Travel.CanReturn ? "返回上次传送前的稳定地点。过夜后清空。" : "先使用一次传送，才有可以返回的位置。",
            () => Travel.Return(), () => Travel.CanReturn, 30);
        api.RegisterAction("travel.destinations", "travel.farm", () => "家门口", () => "回到当前住宅门外，自动避开障碍。", () => Travel.GoHome(), () => true, 0);
        AddDestination("巴士站", "BusStop", 23, 9, 10);
        AddDestination("小镇", "Town", 43, 57, 20);
        AddDestination("海滩", "Beach", 20, 5, 30);
        AddDestination("山上", "Mountain", 31, 20, 40);
        AddDestination("森林", "Forest", 58, 17, 50);

        void AddDestination(string label, string location, int x, int y, int order)
            => api.RegisterAction("travel.destinations", "travel." + location, () => label,
                () => $"传送至{label}，不消耗物品，自动检查附近空位。",
                () => Travel.Go(location, new Vector2(x, y), label), () => true, order);
    }

    internal bool CanUse(out string reason, bool allowOwnMenu = false) => Menu.CanUse(out reason, allowOwnMenu);
    internal void Notify(string message) => Menu.Notify(message);
    internal void Report(Exception error) => Monitor.Log(error.ToString(), LogLevel.Error);

    private void ReloadConfig()
    {
        try { var next = Helper.ReadConfig<ModConfig>(); next.Normalize(); Config = next; ResetHold(); }
        catch (Exception error) { Monitor.Log($"配置读取失败，继续使用原配置：{error.Message}", LogLevel.Warn); }
    }

    private void OnButton(object? sender, ButtonPressedEventArgs e)
    {
        // The shared menu owns its shortcuts; a conflicting custom home key never hijacks it.
        if (e.Button == Menu.Settings.MenuButton || e.Button == Menu.Settings.KeyboardMenuButton) return;
        if ((e.Button == Config.HomeButton || e.Button == Config.KeyboardHomeButton) && CanUse(out _))
        {
            Helper.Input.Suppress(e.Button);
            heldHome = e.Button;
            homeElapsed = 0;
            homeTriggered = false;
        }
    }

    private void OnUpdate(object? sender, UpdateTickedEventArgs e)
    {
        if (heldHome == SButton.None) return;
        if (!(Helper.Input.IsDown(heldHome) || Helper.Input.IsSuppressed(heldHome)) || !CanUse(out _)) { ResetHold(); return; }
        Helper.Input.Suppress(heldHome);
        homeElapsed += Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds;
        if (!homeTriggered && homeElapsed >= Config.HomeHoldMilliseconds) { homeTriggered = true; Travel.GoHome(); }
    }

    private void OnHud(object? sender, RenderedHudEventArgs e)
    {
        if (heldHome == SButton.None || homeTriggered || homeElapsed <= 0) return;
        int w = 280, x = (Game1.uiViewport.Width - w) / 2, y = Game1.uiViewport.Height - 130;
        e.SpriteBatch.Draw(Game1.staminaRect, new Rectangle(x - 16, y - 12, w + 32, 76), Color.Black * 0.8f);
        Ui.Text(e.SpriteBatch, "回家 · 松开取消", x, y, Color.White);
        e.SpriteBatch.Draw(Game1.staminaRect, new Rectangle(x, y + 42, w, 6), Color.Gray);
        e.SpriteBatch.Draw(Game1.staminaRect, new Rectangle(x, y + 42, (int)(w * Math.Min(1, homeElapsed / Config.HomeHoldMilliseconds)), 6), Ui.Accent);
    }
    private void ResetHold() { heldHome = SButton.None; homeElapsed = 0; homeTriggered = false; }
    private void ResetSession() { ResetHold(); Travel.Clear(); }
}
