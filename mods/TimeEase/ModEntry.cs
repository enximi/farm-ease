using System.Reflection;
using FarmMenu;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Enums;
using StardewValley;
using StardewValley.Menus;

namespace TimeEase;

public sealed class ModEntry : Mod
{
    private ModConfig config = new();
    private DailySchedule schedule = null!;
    private UsageStore store = null!;
    private SessionClock clock = null!;
    private MenuController menu = null!;
    private bool inSession;
    private bool resting;
    private bool storageHealthy = true;
    private bool configurationHealthy = true;
    private readonly RestBoundary boundary = new();
    private readonly FieldInfo? newDayTask = typeof(Game1).GetField("_newDayTask", BindingFlags.NonPublic | BindingFlags.Static);
    private SaveObserver observer = null!;
    private SavePoint restPoint;
    private bool loadedFromDisk;
    private bool unsupportedNotice;
    private SessionNetwork network = null!;
    private LocalSession local = null!;
    private string? warnedDay;
    private string? exhaustedDay;
    private string? multiplayerWarningDay;
    private long retryAt;
    private long logAt;
    private RestMenu? restMenu;
    private TimeSettingsMenu? settingsMenu;
    private RestMenu? titleRest;
    private int saveAttempt;
    private ScreenPhase? lastSettlementPhase;
    private int bonusMinutes;
    private string? bonusDay;
    private double Limit => TotalMinutes * 60d;
    internal ModConfig Settings => config;
    internal string BudgetDay => schedule.Day(DateTimeOffset.UtcNow);
    internal int TotalMinutes => config.DailyLimitMinutes + (bonusDay == BudgetDay ? bonusMinutes : 0);
    internal double UsedMinutes => clock.Used(DateTimeOffset.UtcNow) / 60d;
    internal string BudgetStatus => $"今天已用 {UsedMinutes:F1} / {TotalMinutes} 分钟，剩余 {Math.Max(0, TotalMinutes - UsedMinutes):F1} 分钟";
    private bool SinglePlayer => !Context.IsMultiplayer && !Context.IsSplitScreen;
    private SavePoint Point => new(Game1.uniqueIDForThisGame.ToString(System.Globalization.CultureInfo.InvariantCulture), (uint)Game1.Date.TotalDays);
    private PlayMode Mode => SinglePlayer ? PlayMode.SinglePlayer
        : !Context.IsOnHostComputer ? PlayMode.RemoteClient
        : Context.HasRemotePlayers ? PlayMode.RemoteHost : PlayMode.LocalHost;
    private bool ShouldRest => !storageHealthy || !configurationHealthy || Exhausted;
    private bool Exhausted => clock.Used(DateTimeOffset.UtcNow) >= Limit;

    public override void Entry(IModHelper helper)
    {
        store = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "TimeEase"));
        Reload();
        observer = new(ModManifest.UniqueID + ".SaveEvidence",
            reason => Monitor.Log($"保存完成检查不可用：{reason} 本次仅提醒，不自动拦截或退出。", LogLevel.Warn));
        observer.Install();
        network = new(this);
        local = new(this);
        Diagnostic("observer_ready", $"supported={observer.Supported} game_version={Game1.version}");
        menu = new(this, Reload, api => api.RegisterAction("", "time.status", () => "适时而归",
            Status, () => OpenSettings(false), () => true, 80));
        helper.ConsoleCommands.Add("timeease", "适时而归：timeease [status|diagnose|settings|reload]；F11 打开设置。", (_, args) =>
        {
            if (args.FirstOrDefault() == "reload") Reload();
            else if (args.FirstOrDefault() == "settings") OpenSettings(false);
            Monitor.Log(Status(), LogLevel.Info);
            Diagnostic("snapshot", $"observer_supported={observer.Supported} session={inSession} resting={resting} pending={boundary.HasPending} phase={(inSession ? Phase().ToString() : "Title")} " + BudgetFields());
        });
        helper.Events.Specialized.LoadStageChanged += (_, e) =>
        {
            if (Context.ScreenId != 0) return;
            if (e.NewStage == LoadStage.SaveParsed) loadedFromDisk = true;
            else if (e.NewStage is LoadStage.CreatedBasicInfo or LoadStage.ReturningToTitle or LoadStage.None)
                loadedFromDisk = false;
        };
        helper.Events.GameLoop.SaveLoaded += (_, _) => { if (Context.ScreenId == 0) Begin(); };
        helper.Events.GameLoop.Saving += (_, _) => BeginSave();
        helper.Events.GameLoop.SaveCreating += (_, _) => BeginSave();
        helper.Events.GameLoop.Saved += (_, _) => Saved();
        helper.Events.GameLoop.SaveCreated += (_, _) => Saved();
        helper.Events.GameLoop.ReturnedToTitle += (_, _) =>
        {
            if (Context.ScreenId != 0) return;
            local.Reset("returned_to_title");
            network.Reset("returned_to_title");
            if (clock.Running) TryStorage(() => clock.Stop(DateTimeOffset.UtcNow));
            inSession = resting = false;
            restMenu = null;
            loadedFromDisk = false;
            boundary.Clear();
            unsupportedNotice = false;
            settingsMenu = null;
            Reload();
        };
        helper.Events.Input.ButtonPressed += (_, e) =>
        {
            if (Context.IsSplitScreen && Context.ScreenId != 0) return;
            if (helper.Input.IsSuppressed(e.Button)) return;
            if (OwnMenu is { } own && own.HandleController(e.Button))
            { helper.Input.Suppress(e.Button); return; }
            bool title = Game1.activeClickableMenu is TitleMenu && (TitleMenu.subMenu == null || ReferenceEquals(TitleMenu.subMenu, titleRest));
            bool available = title || resting || Context.IsPlayerFree;
            if (available && (e.Button == SButton.F11 || (title && e.Button == SButton.LeftStick) || (title && e.Button == SButton.MouseLeft
                && TitleSettingsButton.Contains(Game1.getMouseX(), Game1.getMouseY()))))
            { helper.Input.Suppress(e.Button); OpenSettings(false); }
        };
        helper.Events.Display.RenderedActiveMenu += (_, e) =>
        {
            if (Game1.activeClickableMenu is TitleMenu && TitleMenu.subMenu == null)
            { Ui.Box(e.SpriteBatch, TitleSettingsButton); Ui.Text(e.SpriteBatch, "适时而归 · L3 / F11", TitleSettingsButton.X + 14, TitleSettingsButton.Y + 15); }
        };
        helper.Events.GameLoop.UpdateTicking += Update;
        Monitor.Log($"适时而归：{config.DailyLimitMinutes} 分钟，{config.TimeZoneId} {config.ResetTime} 重置。记录：{store.FilePath}。远程联机为待人工验证预览；纯本地分屏保存后全屏退出为预览。", LogLevel.Info);
    }

    public override object GetApi() => menu.PublicApi;

    private void Reload()
    {
        if (inSession || Context.IsWorldReady)
        {
            Monitor.Log("请先正常保存并返回标题，再刷新计时配置。", LogLevel.Warn);
            return;
        }
        try
        {
            var next = Helper.ReadConfig<ModConfig>();
            var nextSchedule = next.Validate();
            // Finish any pending clean-exit write before replacing the clock.
            if (clock != null && !TryStorage(() => clock.Stop(DateTimeOffset.UtcNow))) return;
            config = next;
            schedule = nextSchedule;
            configurationHealthy = true;
        }
        catch (Exception error)
        {
            configurationHealthy = false;
            schedule ??= config.Validate();
            Monitor.Log($"计时配置无效；请在标题画面修正后 timeease reload。{error.Message}", LogLevel.Error);
        }
        clock = new(store, schedule);
        TryStorage(clock.Refresh);
        RefreshBonus();
    }

    private void Begin()
    {
        if (inSession || !config.Enabled) return;
        inSession = true;
        TryStorage(clock.Refresh);
        RefreshBonus();
        boundary.Loaded(Point, schedule.Day(DateTimeOffset.UtcNow), loadedFromDisk);
        lastSettlementPhase = null;
        Diagnostic("session_loaded", $"origin={(loadedFromDisk ? "disk" : "new")} game_day={Point.Day} " + BudgetFields());
        TryStorage(() => clock.Start(DateTimeOffset.UtcNow));
        if (!SinglePlayer && Context.IsMainPlayer && loadedFromDisk && observer.Check())
        { boundary.Clear(); CoordinatedCheckpoint(); }
        ProcessBoundary();
        if (!resting) Notify(Status());
    }

    private void BeginSave()
    {
        if (Context.ScreenId != 0 || !config.Enabled) return;
        local.Reset("saving_started");
        network.Reset("saving_started");
        observer.Begin(boundary.BeginSave(Point));
        saveAttempt++;
        lastSettlementPhase = null;
        Diagnostic("save_begin", $"attempt={saveAttempt} game_day={Point.Day}");
        unsupportedNotice = false;
    }

    private void Saved()
    {
        if (Context.ScreenId != 0 || !config.Enabled) return;
        Sample(force: true);
        if (Context.IsMultiplayer && !Context.IsMainPlayer)
        {
            boundary.Clear();
            Diagnostic("client_save_lifecycle", "authority=waiting_for_host_checkpoint");
            return;
        }
        bool valid = observer.Check() && Game1.saveOnNewDay && !SaveGame.CancelToTitle && Game1.gameMode != 9;
        bool observed = boundary.SaveObserved;
        bool committed = valid && boundary.Saved(Point, schedule.Day(DateTimeOffset.UtcNow));
        Diagnostic("save_evidence", $"attempt={saveAttempt} observed={observed} native_steps={observer.ObservedSteps} committed={committed} observer_supported={observer.Supported} save_on_new_day={Game1.saveOnNewDay} cancelled={SaveGame.CancelToTitle} game_mode={Game1.gameMode}");
        if (!committed)
        {
            boundary.Clear();
            if (ShouldRest) Unsupported("游戏已发出保存结束通知，但适时而归未能核实写盘完成；这不表示存档损坏。本次不会拦截或自动退出，请确认原版保存完成后自行休息。");
        }
        if (!SinglePlayer && Context.IsMainPlayer && committed)
        { boundary.Clear(); CoordinatedCheckpoint(); }
        // Never show a menu here. Saved can fire before ShippingMenu's outro
        // and before the multiplayer wakeup/finished barriers have completed.
    }

    private ScreenPhase Phase()
    {
        if (newDayTask == null || Game1.exitToTitle || Game1.gameMode == 9 || SaveGame.CancelToTitle)
            return ScreenPhase.Unsafe;
        if (newDayTask.GetValue(null) is Task task)
            return task.IsFaulted || task.IsCanceled ? ScreenPhase.Unsafe : ScreenPhase.NativeTransition;
        if (!Context.IsWorldReady || Game1.game1.IsSaving || Game1.newDay || Game1.game1.isLocalMultiplayerNewDayActive
            || Game1.showingEndOfNightStuff || Game1.endOfNightMenus.Count > 0
            || Game1.activeClickableMenu is SaveGameMenu or ShippingMenu || Game1.newDaySync.hasInstance())
            return ScreenPhase.NativeTransition;
        if (Game1.gameMode != Game1.playingGameMode || Game1.eventUp || Game1.farmEvent != null
            || Game1.currentMinigame != null || Game1.locationRequest != null || Game1.dialogueUp
            || Game1.overlayMenu != null || Game1.nextClickableMenu.Count > 0
            || (Game1.activeClickableMenu != null && !ReferenceEquals(Game1.activeClickableMenu, restMenu)
                && !(resting && ReferenceEquals(Game1.activeClickableMenu, settingsMenu))))
            return ScreenPhase.Unsafe;
        return ScreenPhase.Ready;
    }

    private void ProcessBoundary()
    {
        if (!inSession || !boundary.HasPending || resting) return;
        if (!observer.Check())
        {
            boundary.Clear();
            if (ShouldRest) Unsupported("保存实现未受支持，本次只提醒。");
            return;
        }
        var phase = Phase();
        if (lastSettlementPhase != phase)
        {
            lastSettlementPhase = phase;
            Diagnostic("settlement_phase", $"attempt={saveAttempt} phase={phase} menu={Game1.activeClickableMenu?.GetType().Name ?? "none"} saving={Game1.game1.IsSaving} new_day={Game1.newDay} end_of_night={Game1.showingEndOfNightStuff} sync_active={Game1.newDaySync.hasInstance()}");
        }
        boundary.Report(Context.ScreenId, Point, phase);
        var decision = boundary.Evaluate(schedule.Day(DateTimeOffset.UtcNow), Mode, false,
            new[] { 0 }, ShouldRest);
        if (decision is BoundaryDecision.Rest or BoundaryDecision.Continue or BoundaryDecision.Unsupported)
            Diagnostic(phase == ScreenPhase.Ready && decision != BoundaryDecision.Unsupported ? "settlement_complete" : "boundary_released", $"attempt={saveAttempt} decision={decision} phase={phase} " + BudgetFields());
        if (decision == BoundaryDecision.Rest) Rest();
        else if (decision == BoundaryDecision.Unsupported && ShouldRest)
            Unsupported("当前模式或结算边界未受支持，本次不强制拦截；请确认原版保存成功后主动休息。");
    }

    private void Unsupported(string reason)
    {
        if (unsupportedNotice) return;
        unsupportedNotice = true;
        Diagnostic("block_refused", "reason=" + reason);
        Notify(reason);
    }

    private void Update(object? sender, UpdateTickingEventArgs e)
    {
        if (Context.ScreenId == 0) OwnMenu?.TickController();
        local.Tick();
        if (Context.ScreenId != 0 || !config.Enabled) return;
        network.Tick();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (Environment.TickCount64 >= retryAt)
        {
            retryAt = Environment.TickCount64 + 1000;
            RefreshBonus();
            if (inSession && !resting)
            {
                if (!clock.Running) TryStorage(() => clock.Start(now));
                Sample();
                Remind(now);
            }
            else TryStorage(clock.Refresh);
            if (resting && settingsMenu == null && storageHealthy && configurationHealthy && !Exhausted)
            {
                ReleaseRest();
                Notify("游玩额度已恢复可用。");
            }
        }
        CheckTitle();
        ProcessBoundary();
        if (resting)
        {
            // A foreign menu, another save, an error, or a mode change revokes
            // our exit permission. Do not overwrite it or discard its progress.
            if (!SinglePlayer || Point != restPoint || Phase() != ScreenPhase.Ready)
            {
                ReleaseRest();
                Unsupported("原生流程或其他 Mod 改变了休息边界，已保留该流程；不会自动退出。");
            }
            else if (Game1.activeClickableMenu == null) Game1.activeClickableMenu = restMenu;
        }
    }

    private void Sample(bool force = false)
    {
        if (clock.Running) TryStorage(() => clock.Sample(DateTimeOffset.UtcNow, force));
    }

    private void Rest()
    {
        if (!SinglePlayer || Phase() != ScreenPhase.Ready) return;
        resting = true;
        Diagnostic("block_enter", $"reason={RestReason} " + BudgetFields());
        restPoint = Point;
        TryStorage(() => clock.Stop(DateTimeOffset.UtcNow));
        restMenu = new(this, RestMessage, ReturnToTitle, () => OpenSettings(false), () => OpenSettings(true));
        Game1.activeClickableMenu = restMenu;
        restMenu.snapToDefaultClickableComponent();
    }

    private void ReturnToTitle()
    {
        if (resting && SinglePlayer && Point == restPoint && Phase() == ScreenPhase.Ready)
        {
            Diagnostic("block_exit", "reason=user_return_to_title");
            Game1.ExitToTitle();
        }
        else Unsupported("退出边界已改变，请先完成原生保存/结算流程；没有自动退出。");
    }

    private void ReleaseRest()
    {
        Diagnostic("block_exit", "reason=quota_or_boundary_changed " + BudgetFields());
        resting = false;
        if (ReferenceEquals(Game1.activeClickableMenu, restMenu)) Game1.activeClickableMenu = null;
        restMenu = null;
        boundary.Clear();
        TryStorage(() => clock.Start(DateTimeOffset.UtcNow));
    }

    private void Remind(DateTimeOffset now)
    {
        string day = schedule.Day(now);
        double remaining = Limit - clock.Used(now);
        if (!SinglePlayer && multiplayerWarningDay != day)
        {
            multiplayerWarningDay = day;
            Notify("联机预览要求全员同版：保存结算后，房主满额会结束房间，客机满额只离开该客机。纯本地分屏满额后等待全屏结算，由主屏统一回标题；混合远程与分屏仅提醒。");
        }
        if (remaining <= 0 && exhaustedDay != day)
        {
            exhaustedDay = day;
            Diagnostic("quota_reached", BudgetFields());
            Notify(SinglePlayer && observer.Supported ? "今日额度已用完。请完成今天并上床保存；确认保存与结算完成后进入休息界面。"
                : "今日额度已用完。联机请与大家完成保存结算；同版协调成功后，房主满额结束房间、客机满额离开。无法确认时只提醒。");
        }
        else if (remaining > 0 && config.WarningMinutes > 0 && remaining <= config.WarningMinutes * 60 && warnedDay != day)
        {
            warnedDay = day;
            Notify($"今日剩余约 {Math.Ceiling(remaining / 60)} 分钟，可以准备收尾了。");
        }
    }

    private bool TryStorage(Action action)
    {
        try { action(); storageHealthy = true; return true; }
        catch (Exception error)
        {
            bool wasHealthy = storageHealthy;
            storageHealthy = false;
            if (Environment.TickCount64 >= logAt)
            {
                logAt = Environment.TickCount64 + 30000;
                Monitor.Log($"额度记录读写失败，保留已有记录并重试；进行中的一天仍可正常保存：{error.Message}", LogLevel.Error);
            }
            if (wasHealthy && inSession && Context.IsWorldReady)
                Notify("额度记录暂时无法读写，请正常保存；仅在确认安全的保存边界暂停等待记录恢复。");
            return false;
        }
    }

    private TimeMenu? OwnMenu => (Game1.activeClickableMenu is TitleMenu ? TitleMenu.subMenu : Game1.activeClickableMenu) as TimeMenu;
    private Microsoft.Xna.Framework.Rectangle TitleSettingsButton => new(Game1.uiViewport.Width - 265, 24, 240, 54);
    private void RefreshBonus()
    {
        string day = BudgetDay;
        TryStorage(() => { bonusMinutes = store.ReadBonus(day); bonusDay = day; });
    }
    internal string AddToday(string day, int minutes, string request)
    {
        if (day != BudgetDay) return "日期已重置，请重新确认今天的加时。";
        try
        {
            bonusMinutes = store.Extend(day, minutes, request);
            bonusDay = day;
            Diagnostic("bonus_added", $"minutes={minutes} today_bonus={bonusMinutes} " + BudgetFields());
            return "已保存今日加时。" + BudgetStatus;
        }
        catch (Exception e) { return "加时未确认成功，可重试：" + e.Message; }
    }
    internal string ChangeSettings(int daily, string reset, int warning)
    {
        var next = new ModConfig { Enabled = config.Enabled, DailyLimitMinutes = daily, ResetTime = reset,
            TimeZoneId = config.TimeZoneId, WarningMinutes = warning };
        try
        {
            next.Validate();
            Helper.WriteConfig(next);
            string currentReset = config.ResetTime;
            config = next;
            if (inSession) config.ResetTime = currentReset;
            else Reload();
            Diagnostic("settings_saved", $"daily_minutes={daily} reset_requested={reset} reset_effective={config.ResetTime} warning_minutes={warning} " + BudgetFields());
            return "已保存每日设置；" + BudgetStatus + (inSession && reset != currentReset ? "。新重置时刻返回标题后生效。" : "。");
        }
        catch (Exception e) { return "未更改设置：" + e.Message; }
    }
    internal void OpenSettings(bool bonus)
    {
        if (Context.IsSplitScreen && Context.ScreenId != 0)
        { Notify("分屏的共享限额与今日加时请在主屏打开设置；保存结算完成后全屏统一返回标题。"); return; }
        bool title = Game1.activeClickableMenu is TitleMenu && Game1.gameMode == 0
            && (TitleMenu.subMenu == null || ReferenceEquals(TitleMenu.subMenu, titleRest) || ReferenceEquals(TitleMenu.subMenu, settingsMenu));
        bool safeWorld = Context.IsWorldReady && !Game1.game1.IsSaving && !Game1.newDay
            && !Game1.showingEndOfNightStuff && !Game1.newDaySync.hasInstance()
            && Game1.endOfNightMenus.Count == 0 && newDayTask?.GetValue(null) == null
            && !Game1.eventUp && Game1.currentMinigame == null && !Game1.dialogueUp
            && Game1.activeClickableMenu is not (SaveGameMenu or ShippingMenu)
            && (Context.IsPlayerFree || menu.ActiveMenu is HubPage || resting || ReferenceEquals(Game1.activeClickableMenu, settingsMenu));
        if (!title && !safeWorld)
        { Notify("请先完成当前保存、结算或对话，再打开限时设置。"); return; }
        settingsMenu = new(this, bonus);
        if (title) TitleMenu.subMenu = settingsMenu;
        else Game1.activeClickableMenu = settingsMenu;
        settingsMenu.snapToDefaultClickableComponent();
        Diagnostic("settings_open", $"source={(title ? "title" : resting ? "rest" : "game")} bonus_page={bonus} screen={Context.ScreenId}");
    }
    internal void CloseSettings(TimeSettingsMenu closing)
    {
        if (!ReferenceEquals(settingsMenu, closing)) return;
        settingsMenu = null;
        if (Game1.activeClickableMenu is TitleMenu)
        { TitleMenu.subMenu = null; CheckTitle(); return; }
        if (resting)
        {
            Game1.activeClickableMenu = restMenu;
            restMenu?.snapToDefaultClickableComponent();
            if (!ShouldRest) ReleaseRest();
        }
        else if (ReferenceEquals(Game1.activeClickableMenu, closing)) Game1.activeClickableMenu = null;
    }
    private void CheckTitle()
    {
        if (inSession || Game1.gameMode != 0 || Game1.activeClickableMenu is not TitleMenu || settingsMenu != null) return;
        if (TitleMenu.subMenu != null && !ReferenceEquals(TitleMenu.subMenu, titleRest)) return;
        if (ShouldRest)
        {
            titleRest ??= new(this, RestMessage, () => OpenSettings(false), () => OpenSettings(false), () => OpenSettings(true), "每日设置");
            if (!ReferenceEquals(TitleMenu.subMenu, titleRest))
            {
                Diagnostic("title_block", $"reason={RestReason} " + BudgetFields());
                TitleMenu.subMenu = titleRest;
                titleRest.snapToDefaultClickableComponent();
            }
        }
        else if (ReferenceEquals(TitleMenu.subMenu, titleRest)) TitleMenu.subMenu = null;
    }

    private string RestMessage() => !configurationHealthy ? "计时配置无效。请返回标题修正配置，再执行 timeease reload。"
        : !storageHealthy ? "额度记录暂时无法读取或保存。已有记录不会清零；恢复后自动重试。可安全返回标题。"
        : $"今日 {TotalMinutes} 分钟额度已用完。\n{config.TimeZoneId} 每日 {config.ResetTime} 重置。\n已在保存/载入边界停下，可以放心休息。";

    private string Status() => !config.Enabled ? "适时而归已停用。"
        : $"{BudgetStatus}；{config.TimeZoneId} {config.ResetTime} 重置。菜单、暂停与后台计时，标题与休息界面不计时。"
            + (!configurationHealthy || !storageHealthy ? " 配置或记录异常，请检查 SMAPI 日志。" : "")
            + (!SinglePlayer ? " 联机及纯本地分屏保存后退出为预览；混合远程与分屏仅提醒。" : "")
            + (observer != null && !observer.Supported ? " 保存证据检查不可用，仅提醒。" : "");

    private void CoordinatedCheckpoint()
    {
        if (Context.IsSplitScreen && !Context.HasRemotePlayers) local.Checkpoint(Point);
        else network.Checkpoint(Point);
    }

    internal bool NetworkEnabled => config.Enabled;
    internal bool NetworkNeedsRest => storageHealthy && configurationHealthy && Exhausted;
    internal string NetworkBudgetDay => BudgetDay;
    internal SavePoint NetworkPoint => Point;
    internal ScreenPhase NetworkPhase => Phase();
    internal bool NetworkCanPause => Context.IsWorldReady && newDayTask?.GetValue(null) == null
        && !Game1.game1.IsSaving && !Game1.newDay && !Game1.game1.isLocalMultiplayerNewDayActive
        && !SaveGame.CancelToTitle && Game1.gameMode == Game1.playingGameMode;
    internal void NetworkNotice(string message) => Notify(message);
    internal void NetworkLog(string name, string fields) => Diagnostic(name, fields);

    private string RestReason => !configurationHealthy ? "configuration_error" : !storageHealthy ? "storage_error" : "quota";
    private string BudgetFields() => FormattableString.Invariant($"budget_day={BudgetDay} used_seconds={clock.Used(DateTimeOffset.UtcNow):F1} limit_minutes={TotalMinutes} mode={Mode}");
    private void Diagnostic(string name, string fields) => Monitor.Log($"[TimeEase] event={name} {fields}", LogLevel.Info);

    private void Notify(string message)
    {
        Monitor.Log(message, LogLevel.Info);
        if (Context.IsWorldReady) Game1.addHUDMessage(new HUDMessage(message, HUDMessage.newQuest_type) { timeLeft = 10000 });
    }
}
