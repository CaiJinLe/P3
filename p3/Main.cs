using Godot;

namespace P3;

public partial class Main : Control
{
    private const string AchievementPath = "user://achievements.cfg";
    private const string PauseAchievement = "first_pause_loadout";
    private static readonly Color Ink = new("#e5f1f4");
    private static readonly Color Muted = new("#9ab3c0");
    private static readonly Color Accent = new("#77e4c7");
    private static readonly Color Gold = new("#ffcb79");
    private static readonly Color Danger = new("#ffa79e");
    private static readonly Color PanelColor = new("#172630");
    private static readonly Color Edge = new("#304753");

    private RunConfig _config = null!;
    private RunState? _run;
    private BattleView _battle = null!;
    private VBoxContainer _minePane = null!;
    private VBoxContainer _groundPane = null!;
    private PanelContainer _loadout = null!;
    private VBoxContainer _deckRows = null!;
    private VBoxContainer _slotRows = null!;
    private Button _groundTab = null!;
    private Button _mineTab = null!;
    private Button _loadoutButton = null!;
    private Button _warning = null!;
    private Button _manualButton = null!;
    private Button _discard = null!;
    private Button _upgrade = null!;
    private CheckButton _pause = null!;
    private Label _clock = null!;
    private Label _economy = null!;
    private Label _integrity = null!;
    private Label _mineLevel = null!;
    private Label _groundStats = null!;
    private Label _deckInfo = null!;
    private Label _result = null!;
    private Label _notice = null!;
    private MineControls[] _mineControls = [];
    private int? _selectedCard;
    private bool _ground;
    private bool _manual;
    private bool _achievementUnlocked;
    private float _hudRemaining;
    private float _noticeRemaining;

    private sealed record MineControls(Label Inventory, ProgressBar Progress, Button Focus, Button Open);

    private RunState Run => _run!;
    private bool Paused => _loadout.Visible && _pause.ButtonPressed && Run.IsRunning;

    public override void _Ready()
    {
        Theme = BuildTheme();
        try
        {
            _config = RunConfig.Parse(Godot.FileAccess.GetFileAsString("res://prototype.json"));
            _run = new RunState(_config);
        }
        catch (Exception error)
        {
            GD.PushError($"P3 原型配置加载失败：{error.Message}");
            AddChild(Text($"P3 原型配置加载失败\n{error.Message}", 24, Danger));
            return;
        }

        var achievement = new ConfigFile();
        if (achievement.Load(AchievementPath) == Error.Ok)
            _achievementUnlocked = achievement.GetValue("unlocked", PauseAchievement, false).AsBool();

        BuildInterface();
        StartRun();
    }

    public override void _Process(double delta)
    {
        if (_run is null)
            return;

        var seconds = (float)delta;
        if (_noticeRemaining > 0)
        {
            _noticeRemaining -= seconds;
            if (_noticeRemaining <= 0)
                _notice.Hide();
        }

        var wasRunning = Run.IsRunning;
        if (!Paused && wasRunning)
            Run.Advance(Math.Min(seconds, 0.10f), _manual, _battle.ReadAim());
        if (wasRunning && !Run.IsRunning)
        {
            SetManual(false);
            RefreshLoadout();
            UpdateHud();
        }

        _hudRemaining -= seconds;
        if (_hudRemaining <= 0)
        {
            UpdateHud();
            _hudRemaining = 0.09f;
        }
        if (_ground)
            _battle.QueueRedraw();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (_run is null || @event is not InputEventKey { Pressed: true, Echo: false } key
            || key.CtrlPressed || key.AltPressed || key.MetaPressed)
            return;

        switch (key.PhysicalKeycode)
        {
            case Key.Q: ShowGround(false); break;
            case Key.E: ShowGround(true); break;
            case Key.B: ToggleLoadout(); break;
            case Key.F when _ground: SetManual(!_manual); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    private void BuildInterface()
    {
        var frame = Column(12);
        AddChild(frame);
        frame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        frame.OffsetLeft = 24;
        frame.OffsetRight = -24;
        frame.OffsetTop = 20;
        frame.OffsetBottom = -18;

        var header = Row(22);
        frame.AddChild(header);
        var title = Text("P3  防线原型", 27, Ink);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        _clock = Text("", 18, Ink);
        _economy = Text("", 18, Gold);
        _integrity = Text("", 18, Accent);
        header.AddChild(_clock);
        header.AddChild(_economy);
        header.AddChild(_integrity);

        var navigation = Row(9);
        frame.AddChild(navigation);
        _mineTab = Action("地下采矿  [Q]", () => ShowGround(false), true);
        _groundTab = Action("地面防线  [E]", () => ShowGround(true), true);
        _loadoutButton = Action("换卡  [B]", ToggleLoadout, true);
        navigation.AddChild(_mineTab);
        navigation.AddChild(_groundTab);
        navigation.AddChild(_loadoutButton);
        navigation.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        navigation.AddChild(Action("重新开局", StartRun));

        _warning = Action("", () => ShowGround(true));
        frame.AddChild(_warning);

        var body = Row(12);
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        frame.AddChild(body);
        var mainPane = Column(0);
        mainPane.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddChild(mainPane);
        _minePane = BuildMinePane();
        mainPane.AddChild(_minePane);
        _groundPane = BuildGroundPane();
        mainPane.AddChild(_groundPane);
        _loadout = BuildLoadout();
        body.AddChild(_loadout);

        _result = Text("", 24, Accent);
        _result.Hide();
        frame.AddChild(_result);
        _notice = Text("", 16, Accent);
        _notice.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _notice.Hide();
        frame.AddChild(_notice);
    }

    private VBoxContainer BuildMinePane()
    {
        var pane = Column(14);
        pane.SizeFlagsVertical = SizeFlags.ExpandFill;
        var summary = Row(18);
        summary.AddChild(Text("地下矿场", 23, Ink));
        _mineLevel = Text("", 17, Muted);
        summary.AddChild(_mineLevel);
        pane.AddChild(summary);

        var veins = Row(12);
        veins.SizeFlagsVertical = SizeFlags.ExpandFill;
        pane.AddChild(veins);
        _mineControls = new MineControls[_config.Stones.Length];
        for (var i = 0; i < _config.Stones.Length; i++)
        {
            var index = i;
            var stone = _config.Stones[index];
            var panel = Panel(20);
            panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            veins.AddChild(panel);
            var column = Column(13);
            panel.AddChild(column);

            var oreColor = index % 2 == 0 ? Gold : new Color("#a1ccf6");
            var ore = Text(index % 2 == 0 ? "◆" : "⬟", 58, oreColor);
            ore.HorizontalAlignment = HorizontalAlignment.Center;
            column.AddChild(ore);
            column.AddChild(Text(stone.Name, 25, oreColor));
            var pool = Text($"牌池  {Run.DescribePool(index)}", 16, Muted);
            pool.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            column.AddChild(pool);
            var inventory = Text("", 18, Ink);
            column.AddChild(inventory);
            var progress = new ProgressBar
            {
                MinValue = 0, MaxValue = 1, Step = 0, ShowPercentage = false,
                CustomMinimumSize = new Vector2(0, 8),
            };
            progress.AddThemeStyleboxOverride("background", Box(new Color("#101c26"), Edge, 4));
            progress.AddThemeStyleboxOverride("fill", Box(oreColor, oreColor, 4));
            column.AddChild(progress);
            var focus = Action("开采这条矿脉", () => { Run.SelectVein(index); UpdateHud(); }, true);
            var open = Action("", () => OpenStone(index));
            column.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
            column.AddChild(focus);
            column.AddChild(open);
            _mineControls[index] = new MineControls(inventory, progress, focus, open);
        }

        _upgrade = Action("", () =>
        {
            if (Run.UpgradeMine())
                Notify($"矿场升到 Lv.{Run.MineLevel}，两种矿脉的产出速度都提高了。");
            UpdateHud();
        });
        pane.AddChild(_upgrade);
        return pane;
    }

    private VBoxContainer BuildGroundPane()
    {
        var pane = Column(9);
        pane.SizeFlagsVertical = SizeFlags.ExpandFill;
        _battle = new BattleView
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 220),
            MouseFilter = MouseFilterEnum.Ignore,
            ClipContents = true,
        };
        pane.AddChild(_battle);
        var controls = Row(16);
        pane.AddChild(controls);
        _manualButton = Action("手操炮台  [F]", () => SetManual(!_manual), true);
        controls.AddChild(_manualButton);
        _groundStats = Text("", 16, Muted);
        _groundStats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _groundStats.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        controls.AddChild(_groundStats);
        return pane;
    }

    private PanelContainer BuildLoadout()
    {
        var panel = Panel(14);
        panel.CustomMinimumSize = new Vector2(362, 0);
        var column = Column(8);
        panel.AddChild(column);
        column.AddChild(Text("炮台配装", 22, Ink));
        _pause = new CheckButton { Text = "暂停时间" };
        _pause.Toggled += OnPauseToggled;
        column.AddChild(_pause);
        _deckInfo = Text("", 15, Muted);
        _deckInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_deckInfo);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(scroll);
        var list = Column(7);
        list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(list);
        _slotRows = Column(5);
        list.AddChild(_slotRows);
        list.AddChild(new HSeparator());
        _deckRows = Column(5);
        list.AddChild(_deckRows);
        _discard = Action("弃置所选未装备卡", DiscardSelected);
        column.AddChild(_discard);
        return panel;
    }

    private void StartRun()
    {
        _run = new RunState(_config);
        _battle.Run = Run;
        _selectedCard = null;
        _loadout.Hide();
        _loadoutButton.SetPressedNoSignal(false);
        _pause.SetPressedNoSignal(false);
        _notice.Hide();
        _noticeRemaining = 0;
        ShowGround(false);
        RefreshLoadout();
        UpdateHud();
    }

    private void ShowGround(bool ground)
    {
        _ground = ground;
        _groundPane.Visible = ground;
        _minePane.Visible = !ground;
        _groundTab.SetPressedNoSignal(ground);
        _mineTab.SetPressedNoSignal(!ground);
        if (!ground)
            SetManual(false);
        _battle.QueueRedraw();
        UpdateHud();
    }

    private void SetManual(bool enabled)
    {
        _manual = enabled && _ground && Run.IsRunning;
        _battle.Manual = _manual;
        _manualButton.SetPressedNoSignal(_manual);
        _manualButton.Text = _manual ? "恢复自动  [F]" : "手操炮台  [F]";
        UpdateHud();
    }

    private void ToggleLoadout()
    {
        _loadout.Visible = !_loadout.Visible;
        _loadoutButton.SetPressedNoSignal(_loadout.Visible);
        if (_loadout.Visible)
            RefreshLoadout();
        UpdateHud();
    }

    private void OpenStone(int index)
    {
        if (Run.OpenStone(index) is not { } card)
            return;
        _selectedCard = card.Id;
        Notify($"开出「{card.Spec.Name}」 · {card.Spec.Description}。按 B 打开换卡。");
        RefreshLoadout();
        UpdateHud();
    }

    private void UseSlot(int slot)
    {
        if (_selectedCard is { } id && Run.Equip(id, slot))
        {
            Notify($"槽位 {slot + 1} 已装上「{Run.Slots[slot]!.Spec.Name}」。炮台攻击已更新。");
            _selectedCard = null;
        }
        else
        {
            _selectedCard = Run.Slots[slot]?.Id;
        }
        RefreshLoadout();
        UpdateHud();
    }

    private void DiscardSelected()
    {
        if (_selectedCard is not { } id || !Run.Discard(id))
            return;
        _selectedCard = null;
        RefreshLoadout();
        UpdateHud();
    }

    private void RefreshLoadout()
    {
        ClearChildren(_slotRows);
        ClearChildren(_deckRows);
        for (var i = 0; i < Run.Slots.Count; i++)
        {
            var index = i;
            var card = Run.Slots[i];
            var row = Row(6);
            _slotRows.AddChild(row);
            var slot = Action($"槽 {i + 1}  ·  {card?.Spec.Name ?? "空槽"}", () => UseSlot(index));
            slot.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            slot.Disabled = !Run.IsRunning || (card is null && _selectedCard is null);
            row.AddChild(slot);
            var unload = Action("卸下", () =>
            {
                if (Run.Unequip(index))
                    Notify($"槽位 {index + 1} 已清空，卡牌仍在牌组中。");
                RefreshLoadout();
                UpdateHud();
            });
            unload.Disabled = card is null || !Run.IsRunning;
            row.AddChild(unload);
        }

        foreach (var card in Run.Deck)
        {
            var button = Action($"{card.Spec.Name}  ·  {card.Spec.Description}", () =>
            {
                _selectedCard = _selectedCard == card.Id ? null : card.Id;
                RefreshLoadout();
            }, true);
            if (Run.IsEquipped(card.Id))
                button.Text += "  [已装]";
            button.TooltipText = button.Text;
            button.ClipText = true;
            button.Alignment = HorizontalAlignment.Left;
            button.SetPressedNoSignal(_selectedCard == card.Id);
            button.Disabled = !Run.IsRunning;
            _deckRows.AddChild(button);
        }

        _deckInfo.Text = _selectedCard is null ? "先选一张卡，再选择炮台槽位。" : "已选卡牌，点击目标槽位装备。";
        _discard.Disabled = _selectedCard is not { } selected || Run.IsEquipped(selected) || !Run.IsRunning;
    }

    private void UpdateHud()
    {
        _clock.Text = $"{FormatTime(Run.Time)}   波次 {Run.WaveNumber}/{_config.Waves.Length}";
        _economy.Text = $"金币 {Run.Gold}   卡牌 {Run.Deck.Count}/{_config.DeckCapacity}";
        _integrity.Text = $"基地 {Run.Integrity}/{_config.BaseIntegrity}";
        _mineLevel.Text = $"Lv.{Run.MineLevel}    产速 ×{Run.MineSpeed:0.00}";
        _loadoutButton.Text = $"换卡  [B]  {Run.Slots.Count(card => card is not null)}/{Run.Slots.Count}";
        _clock.AddThemeColorOverride("font_color", Paused ? Gold : Ink);
        _pause.Disabled = !Run.IsRunning;
        _manualButton.Disabled = !Run.IsRunning;

        for (var i = 0; i < _mineControls.Length; i++)
        {
            var controls = _mineControls[i];
            var blocker = Run.OpenStoneBlocker(i);
            controls.Inventory.Text = $"库存  {Run.Stones[i]} 块" + (Run.Stones[i] > 0 && blocker is not null ? $"  ·  {blocker}" : "");
            controls.Progress.Value = Run.MineProgress(i);
            controls.Focus.SetPressedNoSignal(i == Run.ActiveVein);
            controls.Focus.Disabled = !Run.IsRunning || i == Run.ActiveVein;
            controls.Focus.Text = i == Run.ActiveVein ? "正在开采" : "开采这条矿脉";
            controls.Open.Disabled = blocker is not null;
            controls.Open.TooltipText = blocker ?? "从这块石头的卡池随机获得一张卡";
            controls.Open.Text = $"敲开一块  ·  {_config.Stones[i].Cost} 金币";
        }

        _upgrade.Disabled = !Run.CanUpgrade;
        _upgrade.Text = Run.MineLevel == _config.MineMaxLevel ? "矿场已满级" : $"升级矿场  ·  {Run.UpgradeCost} 金币";
        var rate = Run.Stats.Rate * (_manual ? _config.ManualRateMultiplier : 1);
        var damage = Run.Stats.Damage * (_manual ? _config.ManualDamageMultiplier : 1);
        var aim = _manual ? "在战场内按住鼠标左键瞄准射击" : "自动攻击最接近基地的敌人";
        _groundStats.Text = $"{aim}   伤害 {damage:0.#}   射速 {rate:0.00} 发/秒";

        var alert = Run.IsRunning && (Run.UnderPressure || Run.BossApproaching || Run.BossAlive);
        _warning.Disabled = !alert || _ground || Paused;
        _warning.Text = Run.Outcome switch
        {
            RunOutcome.Won => "关卡结束 · 防线守住了",
            RunOutcome.Lost => "关卡结束 · 防线失守",
            _ when Paused => "已暂停 · 关上换卡面板或取消勾选即可继续",
            _ when Run.UnderPressure && Run.BossApproaching => $"敌人越过警戒线 · Boss 还有 {Math.Ceiling(Run.SecondsToBoss)} 秒到达  [E]",
            _ when Run.UnderPressure && Run.BossAlive => "Boss 在场 · 敌人越过警戒线  [E]",
            _ when Run.UnderPressure => "敌人越过警戒线 · 回地面手操炮台  [E]",
            _ when Run.BossAlive => "Boss 已到达战场 · 回地面防守  [E]",
            _ when Run.BossApproaching => $"Boss 将在 {Math.Ceiling(Run.SecondsToBoss)} 秒后出现 · 准备手操  [E]",
            _ => $"炮台自动防守中 · 距 Boss 还有 {FormatTime(Run.SecondsToBoss)}",
        };
        if (_manual && !alert && !Paused)
            _warning.Text = $"正在手操 · 距 Boss 还有 {FormatTime(Run.SecondsToBoss)}";
        _warning.AddThemeColorOverride("font_color", alert ? Gold : Ink);
        _warning.AddThemeColorOverride("font_disabled_color", alert || Paused ? Gold : Muted);

        _result.Visible = !Run.IsRunning;
        _result.Text = $"{(Run.Outcome == RunOutcome.Won ? "防线守住了" : "防线失守")}   击杀 {Run.Kills}   用时 {FormatTime(Run.Time)}";
        _result.AddThemeColorOverride("font_color", Run.Outcome == RunOutcome.Won ? Accent : Danger);
    }

    private void OnPauseToggled(bool enabled)
    {
        if (enabled && !_achievementUnlocked)
        {
            _achievementUnlocked = true;
            var achievement = new ConfigFile();
            achievement.Load(AchievementPath);
            achievement.SetValue("unlocked", PauseAchievement, true);
            if (achievement.Save(AchievementPath) is var saveError && saveError != Error.Ok)
                GD.PushWarning($"P3 成就存档写入失败：{saveError}");
            Notify("成就解锁 · 慢慢想", 6);
        }
        UpdateHud();
    }

    private void Notify(string message, float remaining = 4)
    {
        _notice.Text = message;
        _noticeRemaining = remaining;
        _notice.Show();
    }

    private static string FormatTime(float seconds)
    {
        var time = (int)Math.Floor(seconds);
        return $"{time / 60:00}:{time % 60:00}";
    }

    private static void ClearChildren(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static VBoxContainer Column(int separation)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", separation);
        return column;
    }

    private static HBoxContainer Row(int separation)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", separation);
        return row;
    }

    private static Label Text(string value, int size, Color color)
    {
        var label = new Label { Text = value };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static Button Action(string value, System.Action handler, bool toggle = false)
    {
        var button = new Button { Text = value, ToggleMode = toggle, CustomMinimumSize = new Vector2(0, 41) };
        button.Pressed += handler;
        return button;
    }

    private static PanelContainer Panel(int margin)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Box(PanelColor, Edge, 12, margin, margin));
        return panel;
    }

    private static Theme BuildTheme()
    {
        var font = new SystemFont { FontNames = ["Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC"] };
        var theme = new Theme { DefaultFont = font, DefaultFontSize = 16 };
        theme.SetColor("font_color", "Label", Ink);
        theme.SetColor("font_color", "Button", Ink);
        theme.SetColor("font_hover_color", "Button", Ink);
        theme.SetColor("font_pressed_color", "Button", Accent);
        theme.SetColor("font_disabled_color", "Button", Muted);
        theme.SetStylebox("normal", "Button", Box(new Color("#21343f"), Edge, 7, 13, 8));
        theme.SetStylebox("hover", "Button", Box(new Color("#2d4450"), Accent, 7, 13, 8));
        theme.SetStylebox("pressed", "Button", Box(new Color("#21433f"), Accent, 7, 13, 8));
        theme.SetStylebox("disabled", "Button", Box(new Color("#172630"), Edge, 7, 13, 8));
        return theme;
    }

    private static StyleBoxFlat Box(Color color, Color border, int radius, int x = 0, int y = 0)
    {
        return new StyleBoxFlat
        {
            BgColor = color, BorderColor = border,
            BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1, BorderWidthLeft = 1,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            ContentMarginLeft = x, ContentMarginRight = x, ContentMarginTop = y, ContentMarginBottom = y,
        };
    }
}

