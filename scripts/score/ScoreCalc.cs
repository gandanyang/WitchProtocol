namespace MagicThunder.Score;

/// <summary>
/// 结算分数纯函数（可探针）：击杀分 + 波次奖励 + Boss 奖励。
/// 只做算术，不持有状态；分数构成后续在此扩展（连击/无伤等）。
/// </summary>
public static class ScoreCalc
{
    public const int BasePerKill = 100;
    public const int BossBonus = 2000;

    public static int WaveClearBonus(int waveIndex) => 500 * (waveIndex + 1);

    public static int Compute(int kills, int waveBonusTotal, bool bossKilled)
        => kills * BasePerKill + waveBonusTotal + (bossKilled ? BossBonus : 0);
}
