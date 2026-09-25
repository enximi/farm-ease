using FarmMenu;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;

namespace TravelEase;

// Defer Back's normal journal action until release so holding it never opens a
// menu first. Outside free gameplay the native key remains untouched.
internal sealed class SleepShortcut
{
    private const SButton Button = SButton.ControllerBack;
    private sealed class Hold { internal double Elapsed; internal bool Triggered; }
    private readonly PerScreen<Hold?> holds = new();
    private readonly ModEntry mod;
    internal SleepShortcut(ModEntry mod) => this.mod = mod;
    internal void Clear() => holds.Value = null;

    internal bool Press(SButton button)
    {
        if (button != Button || !mod.Config.EnableSleepShortcut || !mod.CanUse(out _)
            || Game1.player.IsSitting() || Game1.player.passedOut || Game1.newDay
            || Game1.player.timeWentToBed.Value != 0) return false;
        // Existing customized shortcuts retain priority over this default.
        if (Button == mod.Menu.Settings.MenuButton || Button == mod.Menu.Settings.KeyboardMenuButton
            || Button == mod.Config.HomeButton || Button == mod.Config.KeyboardHomeButton) return false;
        if (holds.Value == null) holds.Value = new();
        mod.Helper.Input.Suppress(Button);
        return true;
    }

    internal void Release(SButton button)
    {
        if (button != Button || holds.Value is not { } hold) return;
        Clear();
        if (!hold.Triggered && mod.CanUse(out _) && Game1.dayOfMonth > 0)
            Game1.activeClickableMenu = new QuestLog();
    }

    internal void Update()
    {
        if (holds.Value is not { } hold) return;
        if (!(mod.Helper.Input.IsDown(Button) || mod.Helper.Input.IsSuppressed(Button))) { Clear(); return; }
        if (hold.Triggered) { mod.Helper.Input.Suppress(Button); return; }
        if (!Game1.game1.IsActive || !mod.CanUse(out _) || Game1.player.IsSitting()) { Clear(); return; }
        mod.Helper.Input.Suppress(Button);
        hold.Elapsed += Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds;
        if (hold.Elapsed < mod.Config.SleepHoldMilliseconds) return;
        hold.Triggered = true;
        mod.Sleep.Start();
    }

    internal void Draw(SpriteBatch batch)
    {
        if (holds.Value is not { Triggered: false } hold || hold.Elapsed < 150) return;
        int width = 360, x = (Game1.uiViewport.Width - width) / 2, y = Game1.uiViewport.Height - 130;
        batch.Draw(Game1.staminaRect, new Rectangle(x - 16, y - 12, width + 32, 76), Color.Black * 0.8f);
        Ui.Text(batch, "一键睡觉 · 松开取消", x, y, Color.White);
        batch.Draw(Game1.staminaRect, new Rectangle(x, y + 42, width, 6), Color.Gray);
        batch.Draw(Game1.staminaRect, new Rectangle(x, y + 42,
            (int)(width * Math.Min(1, hold.Elapsed / mod.Config.SleepHoldMilliseconds)), 6), Ui.Accent);
    }
}
