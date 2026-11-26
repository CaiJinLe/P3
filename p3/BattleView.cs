using Godot;
using WorldPoint = System.Numerics.Vector2;

namespace P3;

internal partial class BattleView : Control
{
    private static readonly Color Field = new("#182735");
    private static readonly Color Lane = new("#213544");
    private static readonly Color Grid = new("#304756");
    private static readonly Color Ink = new("#dcebf0");
    private static readonly Color Teal = new("#63d9c3");
    private static readonly Color Amber = new("#f2b867");
    private static readonly Color Red = new("#e2786e");
    private static readonly Color BossRed = new("#f47883");

    internal RunState? Run { get; set; }
    internal bool Manual { get; set; }

    private float FieldScale => Math.Max(0.001f, Math.Min(Size.X / RunState.WorldWidth, Size.Y / RunState.WorldHeight));
    private Vector2 FieldOffset => (Size - new Vector2(RunState.WorldWidth, RunState.WorldHeight) * FieldScale) / 2;

    internal WorldPoint? ReadAim()
    {
        if (!Manual || !IsVisibleInTree() || !Input.IsMouseButtonPressed(MouseButton.Left))
            return null;
        var point = (GetLocalMousePosition() - FieldOffset) / FieldScale;
        if (point.X < RunState.BaseLine || point.Y < 0 || point.X > RunState.WorldWidth || point.Y > RunState.WorldHeight)
            return null;
        return new WorldPoint(point.X, point.Y);
    }

    public override void _Draw()
    {
        if (Run is not { } run)
            return;

        DrawSetTransform(FieldOffset, 0, Vector2.One * FieldScale);
        DrawRect(new Rect2(0, 0, RunState.WorldWidth, RunState.WorldHeight), Field);
        for (var y = 48; y <= 240; y += 96)
            DrawRect(new Rect2(RunState.BaseLine, y, RunState.WorldWidth - RunState.BaseLine - 22, 96), Lane, false, 1);
        for (var x = 160; x < RunState.WorldWidth; x += 64)
            DrawLine(new Vector2(x, 48), new Vector2(x, 336), Grid, 1);

        DrawRect(new Rect2(18, 48, RunState.BaseLine - 18, 288), new Color("#203844"));
        DrawLine(new Vector2(RunState.BaseLine, 48), new Vector2(RunState.BaseLine, 336), Teal, 2);
        for (var y = 48; y < 324; y += 16)
            DrawLine(new Vector2(RunState.WarningLine, y), new Vector2(RunState.WarningLine, y + 9), Amber, 2);

        var font = GetThemeDefaultFont();
        DrawString(font, new Vector2(24, 32), "基地", HorizontalAlignment.Left, -1, 16, Ink);
        DrawString(font, new Vector2(RunState.WarningLine + 10, 32), "警戒线", HorizontalAlignment.Left, -1, 16, Amber);
        DrawString(font, new Vector2(RunState.WorldWidth - 96, 32), "来袭方向", HorizontalAlignment.Left, -1, 16, Ink);
        DrawLine(new Vector2(900, 357), new Vector2(858, 357), Grid, 3);
        DrawLine(new Vector2(858, 357), new Vector2(870, 350), Grid, 3);
        DrawLine(new Vector2(858, 357), new Vector2(870, 364), Grid, 3);

        var tower = ToVector(RunState.TowerPosition);
        var barrel = new Vector2(1, 0);
        if (run.LastShot is { } lastShot && lastShot.ExpiresAt > run.Time)
        {
            var end = ToVector(lastShot.End);
            barrel = (end - tower).Normalized();
            var shotColor = lastShot.Manual ? Amber : Teal;
            DrawLine(tower + barrel * 25, end, shotColor, lastShot.Manual ? 4 : 2, true);
            if (lastShot.SplashRadius > 0)
                DrawArc(end, lastShot.SplashRadius, 0, Mathf.Tau, 32, Amber, 2, true);
        }
        else if (Manual)
        {
            var aim = (GetLocalMousePosition() - FieldOffset) / FieldScale;
            if (new Rect2(0, 0, RunState.WorldWidth, RunState.WorldHeight).HasPoint(aim))
            {
                barrel = (aim - tower).Normalized();
                DrawArc(aim, 11, 0, Mathf.Tau, 20, Amber, 1.5f, true);
                DrawLine(aim + new Vector2(-17, 0), aim + new Vector2(-6, 0), Amber, 1);
                DrawLine(aim + new Vector2(6, 0), aim + new Vector2(17, 0), Amber, 1);
            }
        }

        DrawCircle(tower, 23, new Color("#0d1d27"));
        DrawArc(tower, 23, 0, Mathf.Tau, 32, Manual ? Amber : Teal, 2, true);
        DrawLine(tower, tower + barrel * 32, Manual ? Amber : Teal, 7, true);
        DrawCircle(tower, 9, Ink);

        foreach (var enemy in run.Enemies)
        {
            var at = ToVector(enemy.Position);
            var radius = enemy.Radius;
            DrawCircle(at, radius + 3, new Color("#101e29"));
            DrawCircle(at, radius, enemy.Wave.Boss ? BossRed : Red);
            if (enemy.SlowRemaining > 0)
                DrawArc(at, radius + 4, 0, Mathf.Tau, 22, new Color("#92d5fa"), 2, true);
            DrawCircle(at + new Vector2(-radius * 0.3f, -radius * 0.12f), radius * 0.2f, Field);
            var hp = enemy.Health / enemy.Wave.Health;
            var top = at + new Vector2(-radius, -radius - 10);
            DrawRect(new Rect2(top, new Vector2(radius * 2, 4)), Field);
            DrawRect(new Rect2(top, new Vector2(radius * 2 * hp, 4)), enemy.Wave.Boss ? Amber : Ink);
        }
    }

    private static Vector2 ToVector(WorldPoint point) => new(point.X, point.Y);
}

