using FarmMenu;
using StardewModdingAPI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace TimeEase;

internal sealed class TimeSettingsMenu : TimeMenu
{
    private readonly ModEntry mod;
    private int selected;
    private int daily;
    private int reset;
    private int warning;
    private int custom = 30;
    private string digits = "";
    private string? request;
    private string? requestDay;
    private int extra;
    private int requestOrigin;
    private int firstVisible;
    private string notice = "";
    private Rectangle Panel => new((Game1.uiViewport.Width - Math.Min(820, Game1.uiViewport.Width - 24)) / 2,
        Math.Max(12, (Game1.uiViewport.Height - Math.Min(600, Game1.uiViewport.Height - 24)) / 2),
        Math.Min(820, Game1.uiViewport.Width - 24), Math.Min(600, Game1.uiViewport.Height - 24));
    private int VisibleRows => Math.Clamp((Panel.Height - 190) / 42, 2, 8);
    private Rectangle Row(int i) => request != null
        ? new(Panel.X + 20, Panel.Bottom - 170 + (i - 6) * 46, Panel.Width - 40, 42)
        : new(Panel.X + 20, Panel.Y + 100 + (i - firstVisible) * 42, Panel.Width - 40, 40);
    private void EnsureVisible()
    {
        if (selected < firstVisible) firstVisible = selected;
        if (selected >= firstVisible + VisibleRows) firstVisible = selected - VisibleRows + 1;
        firstVisible = Math.Clamp(firstVisible, 0, 8 - VisibleRows);
    }
    protected override int FocusId => selected;
    internal TimeSettingsMenu(ModEntry mod, bool bonus) : base(mod)
    {
        this.mod = mod;
        daily = mod.Settings.DailyLimitMinutes;
        reset = (int)TimeSpan.Parse(mod.Settings.ResetTime).TotalMinutes;
        warning = mod.Settings.WarningMinutes;
        selected = bonus ? 3 : 0;
        Relayout();
    }
    public override void populateClickableComponentList()
    {
        xPositionOnScreen = Panel.X; yPositionOnScreen = Panel.Y; width = Panel.Width; height = Panel.Height;
        allClickableComponents = new();
        EnsureVisible();
        int first = request == null ? firstVisible : 6;
        int end = request == null ? firstVisible + VisibleRows : 8;
        for (int i = first; i < end; i++)
            allClickableComponents.Add(new ClickableComponent(Row(i), "time.setting." + i)
            { myID = i, upNeighborID = i > first ? i - 1 : -1, downNeighborID = i + 1 < end ? i + 1 : -1,
                leftNeighborID = i, rightNeighborID = i, fullyImmutable = true });
    }
    protected override void Move(Point direction, bool fast)
    {
        if (request != null)
        { selected = Math.Clamp(selected + (direction.Y != 0 ? direction.Y : direction.X), 6, 7); }
        else if (direction.Y != 0) selected = Math.Clamp(selected + direction.Y, 0, 7);
        else if (direction.X != 0) Adjust(direction.X, fast);
        digits = "";
        Relayout();
    }
    protected override void Controller(SButton button)
    {
        if (button == SButton.ControllerA) Confirm();
        else if (button is SButton.ControllerB or SButton.ControllerBack or SButton.ControllerStart) Back();
    }
    public override bool readyToClose() => false;
    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape) { Back(); return; }
        if (key is Keys.Enter or Keys.Space) { Confirm(); return; }
        if (MoveKey(key)) return;
        if (request != null) return;
        if ((key >= Keys.D0 && key <= Keys.D9) || (key >= Keys.NumPad0 && key <= Keys.NumPad9) || key == Keys.Back)
        {
            if (selected is not (0 or 1 or 2 or 5)) return;
            if (key == Keys.Back) digits = digits.Length > 0 ? digits[..^1] : "";
            else if (digits.Length < 4) digits += ((key >= Keys.NumPad0 ? (int)key - (int)Keys.NumPad0 : (int)key - (int)Keys.D0)).ToString();
            int value = int.TryParse(digits, out int parsed) ? parsed : 0;
            if (selected == 0) daily = Math.Clamp(value, 1, 1440);
            if (selected == 1) reset = Math.Clamp(value / 100, 0, 23) * 60 + Math.Clamp(value % 100, 0, 59);
            if (selected == 2) warning = Math.Clamp(value, 0, 1439);
            if (selected == 5) custom = Math.Clamp(value, 1, 1440);
        }
    }
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (request != null) { if (Row(6).Contains(x,y)) { selected = 6; Confirm(); } else if (Row(7).Contains(x,y)) Back(); return; }
        for (int i = firstVisible; i < firstVisible + VisibleRows; i++)
            if (Row(i).Contains(x,y))
            {
                selected = i; digits = ""; Focus();
                if (i is 0 or 1 or 2 or 5)
                {
                    if (x > Row(i).Right - 70) Adjust(1);
                    else if (x > Row(i).Right - 140) Adjust(-1);
                    else if (i == 5) Confirm();
                }
                else Confirm();
                return;
            }
    }
    public override void receiveScrollWheelAction(int direction)
    { if (direction != 0) Move(new(0, direction > 0 ? -1 : 1), false); }
    private void Adjust(int delta, bool fast = false)
    {
        if (selected == 0) daily = Math.Clamp(daily + delta * (fast ? 10 : 1), 1, 1440);
        if (selected == 1) reset = (reset + delta * (fast ? 15 : 1) + 1440) % 1440;
        if (selected == 2) warning = Math.Clamp(warning + delta * (fast ? 5 : 1), 0, 1439);
        if (selected == 5) custom = Math.Clamp(custom + delta * (fast ? 10 : 1), 1, 1440);
    }
    private void Confirm()
    {
        if (request != null)
        {
            if (selected == 7) { Back(); return; }
            notice = mod.AddToday(requestDay!, extra, request);
            if (notice.StartsWith("已")) { request = null; extra = 0; selected = requestOrigin; Relayout(); }
            return;
        }
        if (selected is 3 or 4 or 5)
        {
            requestOrigin = selected;
            extra = selected == 3 ? 15 : selected == 4 ? 30 : custom;
            requestDay = mod.BudgetDay;
            request = Guid.NewGuid().ToString("N");
            selected = 6; Relayout();
        }
        else if (selected == 6) notice = mod.ChangeSettings(daily, $"{reset / 60:00}:{reset % 60:00}", warning);
        else if (selected == 7) Back();
        else Adjust(1);
    }
    private void Back()
    {
        if (request != null) { request = null; extra = 0; selected = requestOrigin; notice = "已取消加时。"; Relayout(); }
        else mod.CloseSettings(this);
    }
    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.staminaRect, new Rectangle(0,0,Game1.uiViewport.Width,Game1.uiViewport.Height), Color.Black * .8f);
        Ui.Box(b, Panel);
        Ui.Text(b, "适时而归 · 设置与今日加时", Panel.X + 24, Panel.Y + 20);
        if (request == null) Ui.Wrapped(b, mod.BudgetStatus, new Rectangle(Panel.X + 24, Panel.Y + 60, Panel.Width - 48, 44));
        if (request != null)
        {
            Ui.Wrapped(b, $"{requestDay} 今天加 {extra} 分钟\n新总额度：{mod.TotalMinutes + extra} 分钟\n实际剩余：{Math.Max(0, Math.Floor((mod.TotalMinutes + extra) - mod.UsedMinutes))} 分钟\n已用保留；每日默认 {mod.Settings.DailyLimitMinutes} 分钟",
                new Rectangle(Panel.X + 24, Panel.Y + 62, Panel.Width - 48, Panel.Height - 240));
            Ui.Box(b, Row(6));
            Ui.Text(b, "确认加时 · A / 回车", Row(6).X + 15, Row(6).Y + 10);
            Ui.Box(b, Row(7)); Ui.Text(b, "取消 · B / Esc", Row(7).X + 15, Row(7).Y + 10);
            Ui.Outline(b, Row(selected), Ui.Accent);
        }
        else
        {
            string[] labels = { $"每日限额：{daily} 分钟", $"重置时刻：{reset / 60:00}:{reset % 60:00}",
                $"提前提醒：{warning} 分钟", "仅今天加 15 分钟", "仅今天加 30 分钟", $"自定义今日加时：{custom} 分钟", "保存每日设置", "返回（未保存设置放弃）" };
            for (int i = firstVisible; i < firstVisible + VisibleRows; i++)
            {
                if (i == selected) { b.Draw(Game1.staminaRect, Row(i), Ui.Accent * .35f); Ui.Outline(b, Row(i), Ui.Accent); }
                Ui.Text(b, labels[i], Row(i).X + 10, Row(i).Y + 10);
                if (i is 0 or 1 or 2 or 5) Ui.Text(b, "−         +", Row(i).Right - 122, Row(i).Y + 10);
            }
        }
        Ui.Wrapped(b, notice.Length > 0 ? notice : $"第 {selected + 1}/8 项 · ↑↓选项，←→逐分钟，LB/RB 快调；A 选择，B 返回。",
            new Rectangle(Panel.X + 24, Panel.Bottom - 78, Panel.Width - 48, 68), Ui.Muted);
        drawMouse(b);
    }
}
