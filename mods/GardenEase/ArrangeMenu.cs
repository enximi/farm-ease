using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using FarmMenu;

namespace GardenEase;

internal sealed class ArrangeMenu : EaseMenu
{
    private readonly ModEntry Mod;
    private readonly MoveSession session;
    private readonly bool multiplayer = Context.IsMultiplayer;
    private int remoteUndoCount;
    private bool showReply;
    internal bool WaitingForHost { get; set; }
    private int UndoCount => multiplayer ? remoteUndoCount : session.UndoCount;
    private Vector2 cursor;
    private readonly bool previousViewportFreeze;
    private Vector2 cameraPosition;
    private Point lastViewportPosition;
    private Point layoutSize;
    private bool cameraRestored;
    private string message = "选择作物、树木、道路或农场设施，按确认开始整理。";
    private Rectangle top, bottom;

    internal ArrangeMenu(ModEntry mod, Farm farm) : base(mod.Menu)
    {
        Mod = mod;
        session = new MoveSession(farm);
        cursor = Game1.player.Tile + new Vector2(0, 1);
        previousViewportFreeze = Game1.viewportFreeze;
        Game1.viewportFreeze = true;
        cameraPosition = new Vector2(Game1.viewport.X, Game1.viewport.Y);
        lastViewportPosition = new Point(Game1.viewport.X, Game1.viewport.Y);
        Layout();
        if (multiplayer) { message = "正在连接房主，申请整理农场。"; Mod.Network.Open(this); }
    }

    private void Layout()
    {
        layoutSize = new Point(Game1.uiViewport.Width, Game1.uiViewport.Height);
        int w = Math.Min(880, Game1.uiViewport.Width - 40);
        top = new((Game1.uiViewport.Width - w) / 2, 20, w, 86);
        bottom = new(top.X, Game1.uiViewport.Height - 146, w, 126);
    }
    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) => Layout();
    internal override void TickInput()
    {
        if (!ReferenceEquals(Game1.currentLocation, session.Farm) || multiplayer != Context.IsMultiplayer) { exitThisMenu(false); return; }
        base.TickInput();
        if (ReferenceEquals(Game1.activeClickableMenu, this)) FollowCursor();
    }
    protected override void Move(Point direction)
    {
        var next = cursor + new Vector2(direction.X, direction.Y);
        if (!session.Farm.isTileOnMap(next)) return;
        cursor = next;
        showReply = false;
    }

    private void FollowCursor()
    {
        float viewWidth = Game1.viewport.Width, viewHeight = Game1.viewport.Height;
        if (viewWidth <= 0 || viewHeight <= 0) return;
        if (layoutSize != new Point(Game1.uiViewport.Width, Game1.uiViewport.Height)) Layout();

        // Resize/zoom can reposition the game's viewport. Adopt that position instead of
        // pulling the camera back to a stale origin; retain subpixel motion on normal frames.
        var actualPosition = new Point(Game1.viewport.X, Game1.viewport.Y);
        if (actualPosition != lastViewportPosition)
            cameraPosition = new Vector2(actualPosition.X, actualPosition.Y);

        // UI panels and the world can have different scales. Reserve a full tile plus
        // padding below/above the panels before setting the vertical tracking limits.
        float uiToWorld = viewHeight / Math.Max(1, Game1.uiViewport.Height);
        float visibleTop = Math.Clamp((top.Bottom + 16) * uiToWorld + 32, 0, viewHeight);
        float visibleBottom = Math.Clamp((bottom.Top - 16) * uiToWorld - 32, 0, viewHeight);
        // Start one tile before the panels, leaving room for the short easing lag
        // while a direction is held down.
        float topLimit = Math.Max(viewHeight * 0.2f, visibleTop + 64);
        float bottomLimit = Math.Min(viewHeight * 0.8f, visibleBottom - 64);
        if (topLimit > bottomLimit)
            topLimit = bottomLimit = (visibleTop + visibleBottom) / 2;

        Vector2 cursorCenter = cursor * 64 + new Vector2(32);
        Vector2 screenPosition = cursorCenter - cameraPosition;
        // Only the overflow beyond the central region moves the camera. Returning to
        // that region stops it immediately, rather than continuing toward an old target.
        Vector2 target = cameraPosition + new Vector2(
            screenPosition.X - Math.Clamp(screenPosition.X, viewWidth * 0.2f, viewWidth * 0.8f),
            screenPosition.Y - Math.Clamp(screenPosition.Y, topLimit, bottomLimit));
        var mapLimit = new Vector2(
            Math.Max(0, session.Farm.Map.Layers[0].LayerWidth * 64 - viewWidth),
            Math.Max(0, session.Farm.Map.Layers[0].LayerHeight * 64 - viewHeight));
        target = Vector2.Clamp(target, Vector2.Zero, mapLimit);

        // Frame-rate independent easing: about 95% of the motion finishes in 0.2 seconds.
        float elapsed = (float)Math.Max(0, Game1.currentGameTime.ElapsedGameTime.TotalSeconds);
        float blend = 1f - MathF.Exp(-elapsed / 0.065f);
        cameraPosition = Vector2.Clamp(Vector2.Lerp(cameraPosition, target, blend), Vector2.Zero, mapLimit);
        if (Vector2.DistanceSquared(cameraPosition, target) < 0.25f) cameraPosition = target;
        Game1.viewport.X = (int)MathF.Round(cameraPosition.X);
        Game1.viewport.Y = (int)MathF.Round(cameraPosition.Y);
        lastViewportPosition = new Point(Game1.viewport.X, Game1.viewport.Y);
    }
    protected override void Confirm()
    {
        if (multiplayer != Context.IsMultiplayer) { exitThisMenu(false); return; }
        if (multiplayer)
        {
            showReply = false;
            string action = session.Selected == null ? "Select" : "Place";
            Mod.Network.Send(action, cursor.ToPoint(), GardenNetwork.Describe(session.Farm, cursor, session.Selected?.ObjectLayer));
            return;
        }
        try
        {
            int before = session.UndoCount;
            message = session.Selected == null ? session.Select(cursor) : session.Place(cursor);
            Game1.playSound(session.UndoCount > before ? "dwop" : "smallSelect");
        }
        catch (Exception ex) { Mod.Report(ex); session.Cancel(); message = "搬移未完成，已尝试恢复原位置。请查看 SMAPI 日志。"; }
    }
    protected override void Back()
    {
        if (multiplayer && WaitingForHost) { ReturnToMainMenu(); return; }
        if (multiplayer && session.Selected != null) { Mod.Network.Send("Cancel", Point.Zero, ""); return; }
        if (session.Selected != null) { session.Cancel(); message = "已取消选中，对象仍在原位置。"; Game1.playSound("smallSelect"); }
        else ReturnToMainMenu();
    }
    private void ReturnToMainMenu()
    {
        exitThisMenu(false);
        Mod.Menu.Hub?.Open("");
    }
    protected override void Undo()
    {
        if (multiplayer) { Mod.Network.Send("Undo", Point.Zero, ""); return; }
        try
        {
            message = session.Undo(out Vector2? restored);
            if (restored is Vector2 tile) cursor = tile;
            Game1.playSound("smallSelect");
        }
        catch (Exception ex) { Mod.Report(ex); message = "撤销未完成，请查看 SMAPI 日志。"; }
    }
    internal void Receive(GardenReply reply)
    {
        WaitingForHost = false;
        if (!reply.Open)
        {
            if (ReferenceEquals(Game1.activeClickableMenu, this)) exitThisMenu(false);
            Mod.Menu.Notify(reply.Message);
            return;
        }
        remoteUndoCount = reply.UndoCount;
        if (reply.Action == "Ping") return;
        message = reply.Message;
        showReply = true;
        if (!reply.Selected) session.Cancel();
        else if (session.Selected == null || reply.Action == "Select")
        {
            session.Cancel();
            session.Select(new Vector2(reply.X, reply.Y));
        }
        if (reply.Restored) cursor = new Vector2(reply.X, reply.Y);
        Game1.playSound("smallSelect");
    }
    protected override void Click(int x, int y)
    {
        if (top.Contains(x, y) || bottom.Contains(x, y)) return;
        Vector2 tile = Mod.Helper.Input.GetCursorPosition().Tile;
        if (!session.Farm.isTileOnMap(tile)) return;
        cursor = tile;
        Confirm();
    }

    internal void DrawWorld(SpriteBatch b)
    {
        // This callback is in world space; UI scale and game zoom may differ.
        int minX = Math.Max(0, Game1.viewport.X / 64), minY = Math.Max(0, Game1.viewport.Y / 64);
        int maxX = Math.Min(session.Farm.Map.Layers[0].LayerWidth - 1, (Game1.viewport.X + Game1.viewport.Width) / 64);
        int maxY = Math.Min(session.Farm.Map.Layers[0].LayerHeight - 1, (Game1.viewport.Y + Game1.viewport.Height) / 64);
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
                if (session.Selected == null || session.Selected.Dirt != null
                    ? session.Farm.doesTileHaveProperty(x, y, "Diggable", "Back") != null
                    : session.Farm.isTilePlaceable(new Vector2(x, y), session.Selected.Passable))
                    Ui.Outline(b, TileRect(new Vector2(x, y)), Color.White * 0.12f, 1);
        if (session.Source is Vector2 source) Ui.Outline(b, TileRect(source), new Color(255, 207, 105), 4);
        bool valid = session.Selected == null || session.InvalidTarget(cursor) == null;
        var swapTarget = session.SwapTarget(cursor);
        bool swapping = swapTarget != null;
        CoverageOverlay.Draw(b, session.Farm, session.Selected, cursor, valid || session.Source == cursor);
        if (session.Source is Vector2 original)
            CoverageOverlay.Draw(b, session.Farm, swapTarget, original, valid, secondary: true);
        Color tint = valid ? (swapping ? new Color(255, 207, 105) : Ui.Accent) : new Color(244, 107, 99);
        Rectangle r = TileRect(cursor);
        b.Draw(Game1.staminaRect, r, tint * 0.24f);
        if (!swapping && session.Source != cursor) session.Selected?.DrawPreview(b, cursor, tint);
        Ui.Outline(b, r, tint, 4);
    }
    private static Rectangle TileRect(Vector2 tile) => new((int)tile.X * 64 - Game1.viewport.X, (int)tile.Y * 64 - Game1.viewport.Y, 64, 64);

    public override void draw(SpriteBatch b)
    {
        Ui.Box(b, top);
        string selection = session.Selected == null ? "" : $" · {session.Selected.Name}";
        Ui.Text(b, (multiplayer ? "田园巧整 · 时间继续流逝" : "田园巧整 · 时间已暂停") + selection, top.X + 24, top.Y + 12);
        Ui.Text(b, $"格子 {(int)cursor.X}, {(int)cursor.Y}    可撤销 {UndoCount} 步    退出整理后清空撤销记录", top.X + 24, top.Y + 46, Ui.Muted);
        Ui.Box(b, bottom);
        string status = session.Selected != null
            ? session.InvalidTarget(cursor) ?? (session.SwapTarget(cursor) != null
                ? "可以交换：按 A / Enter 确认，双方状态均保留。"
                : "可以搬入：按 A / Enter 确认，原有状态保留。")
            : message;
        if (showReply) status = message;
        if (WaitingForHost) status = "正在等待房主确认，请稍候……";
        string coverageHint = CoverageOverlay.Hint(session.Selected);
        string otherCoverageHint = CoverageOverlay.Hint(session.SwapTarget(cursor));
        if (coverageHint.Length == 0) coverageHint = otherCoverageHint;
        else if (otherCoverageHint.Length > 0 && otherCoverageHint != coverageHint)
            coverageHint = "蓝色：洒水范围；金色：稻草人保护范围";
        if (!showReply && !WaitingForHost && session.Selected != null && (session.Source == cursor || session.InvalidTarget(cursor) == null) && coverageHint.Length > 0)
            status += "\n" + coverageHint;
        Ui.Wrapped(b, status, new Rectangle(bottom.X + 24, bottom.Y + 15, bottom.Width - 48, 65));
        Ui.Text(b, "方向键 / 左摇杆 选格    A 确认    B 取消 / 返回    X 撤销", bottom.X + 24, bottom.Bottom - 53, Ui.Muted);
        Ui.Text(b, "键盘：方向键 / WASD    Enter 确认    Esc 返回    Z 撤销", bottom.X + 24, bottom.Bottom - 27, Ui.Muted);
        drawMouse(b);
    }

    internal void RestoreCamera()
    {
        if (cameraRestored) return;
        cameraRestored = true;
        if (multiplayer) Mod.Network.Close(this);
        session.Cancel();
        Game1.viewportFreeze = previousViewportFreeze;
        if (Context.IsWorldReady) Game1.UpdateViewPort(true, Game1.player.getStandingPosition().ToPoint());
    }
    protected override void cleanupBeforeExit() { RestoreCamera(); base.cleanupBeforeExit(); }
}
