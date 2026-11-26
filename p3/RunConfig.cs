using System.Text.Json;

namespace P3;

internal sealed class RunConfig
{
    public int StartingGold { get; init; }
    public string StartingCard { get; init; } = "";
    public int DeckCapacity { get; init; }
    public int TowerSlots { get; init; }
    public int BaseIntegrity { get; init; }
    public float BaseDamage { get; init; }
    public float BaseShotsPerSecond { get; init; }
    public float ManualDamageMultiplier { get; init; }
    public float ManualRateMultiplier { get; init; }
    public int MineUpgradeCost { get; init; }
    public float MineUpgradeStep { get; init; }
    public int MineMaxLevel { get; init; }
    public float BossWarningSeconds { get; init; }
    public CardSpec[] Cards { get; init; } = [];
    public StoneSpec[] Stones { get; init; } = [];
    public WaveSpec[] Waves { get; init; } = [];

    public static RunConfig Parse(string json)
    {
        var config = JsonSerializer.Deserialize<RunConfig>(json)
            ?? throw new InvalidDataException("原型配置为空。");
        var cardIds = config.Cards.Select(card => card.Id).ToHashSet();
        if (cardIds.Count != config.Cards.Length || !cardIds.Contains(config.StartingCard))
            throw new InvalidDataException("卡牌 ID 必须唯一，且必须包含初始卡。");
        if (config.TowerSlots < 1 || config.DeckCapacity < config.TowerSlots || config.BaseIntegrity < 1
            || config.BaseDamage <= 0 || config.BaseShotsPerSecond <= 0
            || config.StartingGold < 0 || config.ManualDamageMultiplier < 1 || config.ManualRateMultiplier < 1
            || config.MineUpgradeCost < 1 || config.MineUpgradeStep < 0 || config.MineMaxLevel < 1)
            throw new InvalidDataException("炮台、牌组或经济的基础配置无效。");
        if (config.Stones.Length == 0 || config.Stones.Any(stone => stone.MineSeconds <= 0 || stone.Cost < 0
            || stone.CardIds.Length == 0 || stone.CardIds.Any(id => !cardIds.Contains(id))))
            throw new InvalidDataException("矿石的产出时间、价格或卡池无效。");
        if (config.Waves.Length == 0 || !config.Waves[^1].Boss
            || config.Waves.Count(wave => wave.Boss) != 1
            || config.Waves.Any(wave => wave.Time < 0 || wave.Count < 1 || wave.Interval <= 0
                || wave.Health <= 0 || wave.Speed <= 0 || wave.Impact < 1 || wave.Gold < 0)
            || config.Waves.Zip(config.Waves.Skip(1)).Any(pair => pair.First.Time >= pair.Second.Time))
            throw new InvalidDataException("波次需要按时间排序，并以一个 Boss 波次收尾。");
        return config;
    }
}

internal sealed record CardSpec
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public float Damage { get; init; }
    public float Rate { get; init; }
    public float SplashRadius { get; init; }
    public float SlowFactor { get; init; } = 1;
    public float SlowSeconds { get; init; }
}

internal sealed record StoneSpec
{
    public string Name { get; init; } = "";
    public int Cost { get; init; }
    public float MineSeconds { get; init; }
    public string[] CardIds { get; init; } = [];
}

internal sealed record WaveSpec
{
    public float Time { get; init; }
    public int Count { get; init; }
    public float Interval { get; init; }
    public float Health { get; init; }
    public float Speed { get; init; }
    public int Gold { get; init; }
    public int Impact { get; init; }
    public bool Boss { get; init; }
}

