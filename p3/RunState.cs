using System.Numerics;

namespace P3;

internal enum RunOutcome { Running, Won, Lost }

internal sealed record CardInstance(int Id, CardSpec Spec);

internal readonly record struct TowerStats(float Damage, float Rate, float SplashRadius, float SlowFactor, float SlowSeconds);

internal readonly record struct ShotTrace(Vector2 End, bool Manual, float SplashRadius, float ExpiresAt);

internal sealed class Enemy(WaveSpec wave, Vector2 position)
{
    public WaveSpec Wave { get; } = wave;
    public Vector2 Position { get; set; } = position;
    public float Health { get; set; } = wave.Health;
    public float SlowRemaining { get; set; }
    public float SlowFactor { get; set; } = 1;
    public float Radius => Wave.Boss ? 32 : 13;
}

internal sealed class RunState
{
    public const float WorldWidth = 960;
    public const float WorldHeight = 384;
    public const float WarningLine = 300;
    public const float BaseLine = 106;
    public static readonly Vector2 TowerPosition = new(78, 192);
    private static readonly float[] Lanes = [96, 192, 288];

    private readonly Random _random;
    private readonly Dictionary<string, CardSpec> _cardSpecs;
    private readonly int[] _spawnedPerWave;
    private readonly List<CardInstance> _deck = [];
    private readonly List<Enemy> _enemies = [];
    private readonly CardInstance?[] _slots;
    private readonly int[] _stones;
    private readonly float[] _mineProgress;
    private int _nextCardId;
    private float _cooldown;

    public RunState(RunConfig config, Random? random = null)
    {
        Config = config;
        _random = random ?? Random.Shared;
        _cardSpecs = config.Cards.ToDictionary(card => card.Id);
        _slots = new CardInstance?[config.TowerSlots];
        _stones = new int[config.Stones.Length];
        _mineProgress = new float[config.Stones.Length];
        _spawnedPerWave = new int[config.Waves.Length];
        Gold = config.StartingGold;
        Integrity = config.BaseIntegrity;
        _slots[0] = AddCard(config.StartingCard);
        RecalculateStats();
    }

    public RunConfig Config { get; }
    public IReadOnlyList<CardInstance> Deck => _deck;
    public IReadOnlyList<CardInstance?> Slots => _slots;
    public IReadOnlyList<Enemy> Enemies => _enemies;
    public IReadOnlyList<int> Stones => _stones;
    public int Gold { get; private set; }
    public int Integrity { get; private set; }
    public int MineLevel { get; private set; } = 1;
    public int ActiveVein { get; private set; }
    public int Kills { get; private set; }
    public int WaveNumber { get; private set; }
    public float Time { get; private set; }
    public TowerStats Stats { get; private set; }
    public ShotTrace? LastShot { get; private set; }
    public RunOutcome Outcome { get; private set; }
    public bool IsRunning => Outcome == RunOutcome.Running;
    public bool UnderPressure => _enemies.Any(enemy => enemy.Position.X <= WarningLine);
    public bool BossAlive => _enemies.Any(enemy => enemy.Wave.Boss);
    public float SecondsToBoss => Math.Max(0, Config.Waves[^1].Time - Time);
    public bool BossApproaching => SecondsToBoss > 0 && SecondsToBoss <= Config.BossWarningSeconds;
    public float MineSpeed => 1 + (MineLevel - 1) * Config.MineUpgradeStep;
    public int UpgradeCost => Config.MineUpgradeCost * MineLevel;
    public bool CanUpgrade => IsRunning && MineLevel < Config.MineMaxLevel && Gold >= UpgradeCost;

    public float MineProgress(int index) => _mineProgress[index] / Config.Stones[index].MineSeconds;
    public bool IsEquipped(int cardId) => SlotFor(cardId) >= 0;

    public void SelectVein(int index)
    {
        if (IsRunning && index >= 0 && index < _stones.Length)
            ActiveVein = index;
    }

    public string? OpenStoneBlocker(int index)
    {
        if (!IsRunning) return "关卡已结束";
        if (_stones[index] == 0) return "还没有石头";
        if (_deck.Count >= Config.DeckCapacity) return "牌组已满";
        if (Gold < Config.Stones[index].Cost) return "金币不足";
        return null;
    }

    public CardInstance? OpenStone(int index)
    {
        if (OpenStoneBlocker(index) is not null)
            return null;

        var stone = Config.Stones[index];
        _stones[index]--;
        Gold -= stone.Cost;
        return AddCard(stone.CardIds[_random.Next(stone.CardIds.Length)]);
    }

    public string DescribePool(int index) => string.Join(" / ", Config.Stones[index].CardIds.Select(id => _cardSpecs[id].Name));

    public bool UpgradeMine()
    {
        if (!CanUpgrade)
            return false;
        Gold -= UpgradeCost;
        MineLevel++;
        return true;
    }

    public bool Equip(int cardId, int slot)
    {
        if (!IsRunning || slot < 0 || slot >= _slots.Length)
            return false;
        var card = _deck.Find(candidate => candidate.Id == cardId);
        if (card is null)
            return false;

        var previousSlot = SlotFor(cardId);
        if (previousSlot == slot)
            return false;
        if (previousSlot >= 0)
            _slots[previousSlot] = _slots[slot];
        _slots[slot] = card;
        RecalculateStats();
        return true;
    }

    public bool Unequip(int slot)
    {
        if (!IsRunning || slot < 0 || slot >= _slots.Length || _slots[slot] is null)
            return false;
        _slots[slot] = null;
        RecalculateStats();
        return true;
    }

    public bool Discard(int cardId)
    {
        return IsRunning && !IsEquipped(cardId) && _deck.RemoveAll(card => card.Id == cardId) > 0;
    }

    public void Advance(float seconds, bool manual, Vector2? aim)
    {
        if (!IsRunning || seconds <= 0)
            return;

        Time += seconds;
        ProduceStones(seconds);
        SpawnDueEnemies();
        MoveEnemies(seconds);
        if (!IsRunning)
            return;

        _cooldown = Math.Max(0, _cooldown - seconds);
        if (_cooldown <= 0)
        {
            if (manual && aim is { } point)
                Fire(FindTarget(point), point, true);
            else if (!manual && FindTarget() is { } enemy)
                Fire(enemy, enemy.Position, false);
        }

        if (_spawnedPerWave[^1] == Config.Waves[^1].Count && _enemies.Count == 0)
            Outcome = RunOutcome.Won;
    }

    private void ProduceStones(float seconds)
    {
        var stone = Config.Stones[ActiveVein];
        _mineProgress[ActiveVein] += seconds * MineSpeed;
        while (_mineProgress[ActiveVein] >= stone.MineSeconds)
        {
            _mineProgress[ActiveVein] -= stone.MineSeconds;
            _stones[ActiveVein]++;
        }
    }

    private void SpawnDueEnemies()
    {
        for (var i = 0; i < Config.Waves.Length; i++)
        {
            var wave = Config.Waves[i];
            while (_spawnedPerWave[i] < wave.Count && Time >= wave.Time + _spawnedPerWave[i] * wave.Interval)
            {
                var lane = wave.Boss ? Lanes[1] : Lanes[(_spawnedPerWave[i] + i) % Lanes.Length];
                _enemies.Add(new Enemy(wave, new Vector2(WorldWidth - 45, lane)));
                _spawnedPerWave[i]++;
                WaveNumber = Math.Max(WaveNumber, i + 1);
            }
        }
    }

    private void MoveEnemies(float seconds)
    {
        for (var i = _enemies.Count - 1; i >= 0; i--)
        {
            var enemy = _enemies[i];
            var slowedTime = Math.Min(seconds, enemy.SlowRemaining);
            var movementTime = slowedTime * enemy.SlowFactor + seconds - slowedTime;
            enemy.Position -= new Vector2(enemy.Wave.Speed * movementTime, 0);
            enemy.SlowRemaining = Math.Max(0, enemy.SlowRemaining - seconds);
            if (enemy.Position.X > BaseLine)
                continue;

            Integrity = Math.Max(0, Integrity - enemy.Wave.Impact);
            _enemies.RemoveAt(i);
            if (Integrity == 0 || enemy.Wave.Boss)
            {
                Outcome = RunOutcome.Lost;
                return;
            }
        }
    }

    private Enemy? FindTarget(Vector2? aim = null)
    {
        Enemy? target = null;
        var closest = float.PositiveInfinity;
        foreach (var enemy in _enemies)
        {
            var distance = aim is { } point ? Vector2.DistanceSquared(point, enemy.Position) : enemy.Position.X;
            var reach = enemy.Radius + 18;
            if (distance >= closest || (aim is not null && distance > reach * reach))
                continue;
            closest = distance;
            target = enemy;
        }
        return target;
    }

    private void Fire(Enemy? target, Vector2 aim, bool manual)
    {
        var damage = Stats.Damage * (manual ? Config.ManualDamageMultiplier : 1);
        var rate = Stats.Rate * (manual ? Config.ManualRateMultiplier : 1);
        _cooldown = 1 / rate;
        LastShot = new ShotTrace(target?.Position ?? aim, manual, target is null ? 0 : Stats.SplashRadius, Time + 0.13f);
        if (target is null)
            return;

        for (var i = _enemies.Count - 1; i >= 0; i--)
        {
            var enemy = _enemies[i];
            var direct = enemy == target;
            if (!direct && (Stats.SplashRadius <= 0
                || Vector2.DistanceSquared(enemy.Position, target.Position) > Stats.SplashRadius * Stats.SplashRadius))
                continue;

            enemy.Health -= direct ? damage : damage * 0.55f;
            if (enemy.Health <= 0)
            {
                Gold += enemy.Wave.Gold;
                Kills++;
                _enemies.RemoveAt(i);
            }
            else if (Stats.SlowSeconds > 0)
            {
                enemy.SlowFactor = Stats.SlowFactor;
                enemy.SlowRemaining = Stats.SlowSeconds;
            }
        }
    }

    private CardInstance AddCard(string specId)
    {
        var card = new CardInstance(++_nextCardId, _cardSpecs[specId]);
        _deck.Add(card);
        return card;
    }

    private int SlotFor(int cardId) => Array.FindIndex(_slots, card => card?.Id == cardId);

    private void RecalculateStats()
    {
        var damage = Config.BaseDamage;
        var rate = Config.BaseShotsPerSecond;
        float splash = 0, slow = 1, duration = 0;
        foreach (var card in _slots)
        {
            if (card is null)
                continue;
            damage += card.Spec.Damage;
            rate += card.Spec.Rate;
            splash = Math.Max(splash, card.Spec.SplashRadius);
            slow = Math.Min(slow, card.Spec.SlowFactor);
            duration = Math.Max(duration, card.Spec.SlowSeconds);
        }
        Stats = new TowerStats(damage, rate, splash, slow, duration);
    }
}

