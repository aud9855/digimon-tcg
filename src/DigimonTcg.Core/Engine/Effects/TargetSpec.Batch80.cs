using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.80.0 원래 규칙이 읽지 못한 대상만: "~ Digimon other than their highest play cost Digimon" (BT23-024 — SentenceParser.Batch80가 대상 뒤로 옮긴 표기)
/// — 그 쪽 배틀 에어리어에서 등장 코스트가 가장 높은 디지몬(같으면 모두)을 뺀다. 전체 처리라 조건은 그때그때 본다 (종합 룰 15-11-2-3-2 · 15-11-2-3-3)
/// </summary>
public sealed partial class TargetSpec
{
    /// <summary>v0.80.0 ContextFilters가 있어도 전체 처리의 조건을 그때그때 다시 본다 (조건이 이 효과의 처리로 바뀌지 않는 대상)</summary>
    internal bool Dynamic80 { get; private set; }

    private static TargetSpec? Parse80(string phrase)
    {
        phrase = phrase.Trim().TrimEnd('.').Trim();
        var m = Regex.Match(phrase, @"^(?<base>.+?Digimon) " + Regex.Escape(SentenceParser.NotHighestCostMarker80) + "$", RegexOptions.IgnoreCase);
        if (!m.Success || Parse(m.Groups["base"].Value) is not { IsThis: false, IsThat: false, Opponent: true } spec || spec.Selector != null) return null;
        spec.ContextFilters.Add((c, d) =>
        {
            var side = c.Enemy.BattleArea.Contains(d) ? c.Enemy : c.Owner;
            int max = side.BattleArea.Where(x => x.TamerProxyOf == null).Select(x => x.TopCard.PlayCost).DefaultIfEmpty(0).Max();
            return d.TopCard.PlayCost < max;
        });
        spec.Dynamic80 = true;
        return spec;
    }
}
