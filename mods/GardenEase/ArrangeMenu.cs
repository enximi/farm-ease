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
    private bool batchMode;
    private Vector2? selectionCorner;
    private Vector2 grabOffset;
    private BatchMove? previewSelection, previewPlan;
    private BatchMove.Check? previewCheck;
    private CoverageOverlay.Area[] previewCoverage = Array.Empty<CoverageOverlay.Area>();
    private Vector2 previewTarget;
    private long previewAt;
    private Vector2 BatchTarget => cursor - grabOffset;
    private readonly bool previousViewportFreeze;
    private Vector2 cameraPosition;
    private Point lastViewportPosition;
    private Point layoutSize;
    private bool cameraRestored;
    private string message = "选择作物、牧草、树木、道路或农场设施，按确认开始整理。";
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
        if (WaitingForHost) return;
        if (batchMode) { ConfirmBatch(); return; }
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
    internal void ToggleBatch()
    {
        if (WaitingForHost || !Mod.Menu.CanUse(out _, true)) return;
        if (session.HasSelection) { message = "请先放下或取消当前选中，再切换整理模式。"; showReply = true; return; }
        selectionCorner = null; batchMode = !batchMode; showReply = false;
        message = batchMode ? "批量框选：确认第一个角，再确认对角；最多 256 格。" : "单件整理：选中一个对象后搬移或交换。";
        Game1.playSound("smallSelect");
    }
    private void ConfirmBatch()
    {
        showReply = false;
        if (session.Batch == null && selectionCorner == null)
        { selectionCorner = cursor; message = "移动到矩形的另一个角，再按确认；手柄 B / 键盘 Esc 取消框选。"; return; }
        Rectangle area = selectionCorner is Vector2 corner ? BatchMove.Between(corner, cursor) : session.Batch!.Area;
        if (session.Batch == null)
        {
            if (!BatchMove.ValidArea(session.Farm, area)) { message = "一次最多框选 256 格（如 16 × 16），请缩小范围。"; showReply = true; return; }
            grabOffset = cursor - new Vector2(area.X, area.Y);
            if (multiplayer)
            {
                Mod.Network.Send("SelectBatch", area.Location, GardenNetwork.DescribeArea(session.Farm, area), area);
                return;
            }
            message = session.SelectBatch(area);
            if (session.Batch != null) selectionCorner = null;
        }
        else if (multiplayer)
        {
            var targetArea = session.Batch.At(BatchTarget).DestinationArea;
            if (!BatchMove.ValidArea(session.Farm, targetArea)) { message = "整组选区超出农场边界。"; showReply = true; return; }
            Mod.Network.Send("PlaceBatch", BatchTarget.ToPoint(), GardenNetwork.DescribeArea(session.Farm, targetArea));
        }
        else
        {
            try { message = session.PlaceBatch(BatchTarget); }
            catch (Exception ex) { Mod.Report(ex); session.Cancel(); message = "批量搬移未完成，已尝试恢复原位置。请查看 SMAPI 日志。"; }
        }
        showReply = true;
        Game1.playSound("smallSelect");
    }
    protected override void Back()
    {
        if (multiplayer && WaitingForHost) { ReturnToMainMenu(); return; }
        if (selectionCorner != null) { selectionCorner = null; message = "已取消框选。"; showReply = false; return; }
        if (multiplayer && session.HasSelection) { Mod.Network.Send("Cancel", Point.Zero, ""); return; }
        if (session.HasSelection) { session.Cancel(); message = "已取消选中，对象仍在原位置。"; Game1.playSound("smallSelect"); }
        else ReturnToMainMenu();
    }
    private void ReturnToMainMenu()
    {
        exitThisMenu(false);
        Mod.Menu.Hub?.Open("");
    }
    protected override void Undo()
    {
        if (WaitingForHost) return;
        if (selectionCorner != null) { selectionCorner = null; message = "已取消框选；再次撤销可恢复上一步。"; return; }
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
        else if (reply.Batch && (session.Batch == null || reply.Action == "SelectBatch"))
        {
            selectionCorner = null;
            session.SelectBatch(new Rectangle(reply.X, reply.Y, reply.Width, reply.Height));
            if (session.Batch == null)
            {
                Mod.Network.Send("Cancel", Point.Zero, "");
                message = "本地选区尚未同步，已请求取消；请稍后重新框选。";
            }
        }
        else if (!reply.Batch && (session.Selected == null || reply.Action == "Select"))
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
    internal void MoveMouse(StardewModdingAPI.Events.CursorMovedEventArgs e)
    {
        // Camera scrolling also changes the world-space cursor tile. Only follow
        // actual pointer movement, so edge scrolling cannot drag the selection.
        if (!batchMode || e.NewPosition.ScreenPixels == e.OldPosition.ScreenPixels) return;
        int x = Game1.getMouseX(), y = Game1.getMouseY();
        if (top.Contains(x, y) || bottom.Contains(x, y) || !session.Farm.isTileOnMap(e.NewPosition.Tile)) return;
        cursor = e.NewPosition.Tile; showReply = false;
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
        if (batchMode) { DrawBatch(b); return; }
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

    private BatchMove.Check BatchPreview()
    {
        if (session.Batch == null) return new("请先框选需要整理的区域。", new());
        long now = Environment.TickCount64;
        if (!ReferenceEquals(previewSelection, session.Batch) || previewTarget != BatchTarget || now - previewAt > 100)
        {
            previewSelection = session.Batch; previewTarget = BatchTarget; previewAt = now;
            previewPlan = session.Batch.At(BatchTarget);
            previewCheck = session.CheckBatch(BatchTarget);
            previewCoverage = BatchMove.ValidArea(session.Farm, previewPlan.DestinationArea)
                ? CoverageOverlay.BatchAreas(session.Farm, previewPlan.Entries) : Array.Empty<CoverageOverlay.Area>();
        }
        return previewCheck!;
    }
    private void DrawBatch(SpriteBatch b)
    {
        if (session.Batch == null)
        {
            Rectangle area = BatchMove.Between(selectionCorner ?? cursor, cursor);
            Color color = BatchMove.ValidArea(session.Farm, area) ? Ui.Accent : new Color(244, 107, 99);
            var screen = new Rectangle(area.X * 64 - Game1.viewport.X, area.Y * 64 - Game1.viewport.Y, area.Width * 64, area.Height * 64);
            b.Draw(Game1.staminaRect, screen, color * 0.16f); Ui.Outline(b, screen, color, 3);
        }
        else
        {
            var check = BatchPreview();
            foreach (var area in previewCoverage) CoverageOverlay.DrawArea(b, area,
                check.Reason == null || BatchTarget == session.Batch.Origin, secondary: true);
            foreach (var tile in session.Batch.Entries.Select(e => e.From).Distinct())
                Ui.Outline(b, TileRect(tile), check.Conflicts.Contains(tile) ? new Color(244, 107, 99) : new Color(255, 207, 105), 3);
            foreach (var entry in previewPlan!.Entries)
            {
                Color tint = check.Conflicts.Contains(entry.To) ? new Color(244, 107, 99) : Ui.Accent;
                var r = TileRect(entry.To);
                b.Draw(Game1.staminaRect, r, tint * 0.18f); Ui.Outline(b, r, tint, 3);
                if (entry.To != entry.From) entry.Item.DrawPreview(b, entry.To, tint);
            }
        }
        Ui.Outline(b, TileRect(cursor), Color.White, 2);
    }

    public override void draw(SpriteBatch b)
    {
        Ui.Box(b, top);
        string selection = batchMode ? session.Batch is { } batch ? $" · 批量 {batch.Entries.Length} 个对象" : " · 批量框选"
            : session.Selected == null ? " · 单件整理" : $" · {session.Selected.Name}";
        Ui.Text(b, (multiplayer ? "田园巧整 · 时间继续流逝" : "田园巧整 · 时间已暂停") + selection, top.X + 24, top.Y + 12);
        Ui.Text(b, $"格子 {(int)cursor.X}, {(int)cursor.Y}    可撤销 {UndoCount} 步    退出整理后清空撤销记录", top.X + 24, top.Y + 46, Ui.Muted);
        Ui.Box(b, bottom);
        string status = session.Selected != null
            ? session.InvalidTarget(cursor) ?? (session.SwapTarget(cursor) != null
                ? "可以交换：按 A / Enter 确认，双方状态均保留。"
                : "可以搬入：按 A / Enter 确认，原有状态保留。")
            : message;
        if (batchMode && session.Batch != null) status = BatchPreview().Reason ?? "可以整体搬入：确认后一次搬移，原有状态保留。";
        else if (batchMode && selectionCorner is Vector2 corner)
        {
            var area = BatchMove.Between(corner, cursor);
            status = $"框选 {area.Width} × {area.Height} = {area.Width * area.Height} 格（最多 256）；确认对角完成选择。";
        }
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
        Ui.Text(b, "手柄：方向 / 摇杆选格    A 确认    B 取消    X 撤销    Y 单件 / 批量", bottom.X + 24, bottom.Bottom - 53, Ui.Muted);
        Ui.Text(b, "键盘：方向 / WASD    Enter 确认    Esc 返回    Z 撤销    B 单件 / 批量", bottom.X + 24, bottom.Bottom - 27, Ui.Muted);
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
