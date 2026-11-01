using System;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.68.0 AI 비용 추천 — 강한 효과 (상대의 디지몬 원래 명칭 · DP 변경 · 소멸 · 되돌리기 · ≪퇴화≫)의 비용:
///  - "By trashing 1 ~ from your hand (or ~), ~" (EX13-031 등): 전에는 패가 낼 매수 + 2장 이상일 때만 냈다 → 받을 상대의 디지몬이 있으면 패가 1장이어도 낸다
///  - "When this card would be played from your hand, by deleting 1 of your ~, reduce the play cost by N" (BT8-043): 전에는 Lv.4 이하만 소멸시켰다 →
///    줄이지 않으면 메모리가 모자라고 (상대 쪽으로 넘어간다) 줄이면 낼 수 있을 때, 소멸시킬 디지몬의 Lv.가 등장시킬 카드 이하라면 낸다
///  - 패 → 진화원 비용 (RestValue66)에도 원래 명칭 · DP 변경을 더한다
/// </summary>
internal static partial class SentenceParser
{
    private static readonly Regex HandTrashOneCost68 = new(@"^trashing (?:1|one) (?!.*\bup to\b).+? (?:in|from) your hand(?: or .+)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>강한 효과이고 지금 받을 상대의 디지몬이 있는지 (없으면 null — 강한 효과가 아니다)</summary>
    internal static Func<EffectContext, bool>? StrongRest68(string rest)
    {
        string r = rest.Trim();
        var mk = Regex.Match(r, @"⟦chg67:(?<id>\d+)⟧");
        if (mk.Success && Spec67(int.Parse(mk.Groups["id"].Value)) is { } v && int.TryParse(v[2], out int dp))
            return c => c.Enemy.BattleArea.Any(d => d.TamerProxyOf == null && !c.Engine.IsUnaffectedBy(c.Owner, d) && d.GetCurrentDP() > dp) && Diag46.HitTrue("costStrongRename68");
        if (Regex.IsMatch(r, @"^(?:you may )?(?:delete|return) (?:1|one|up to \d+|all) of (?:your opponent's|their) (?:[^.]*?)Digimon\b|^(?:you may )?＜De-Digivolve \d+＞ (?:1|one) of (?:your opponent's|their) Digimon", RegexOptions.IgnoreCase))
            return c => c.Enemy.BattleArea.Any(d => d.TamerProxyOf == null && !c.Engine.IsUnaffectedBy(c.Owner, d)) && Diag46.HitTrue("costStrongRemoval68");
        return null;
    }

    /// <summary>"By trashing 1 ~ from your hand, (강한 효과)" — 받을 상대의 디지몬이 있으면 패가 적어도 낸다</summary>
    private static Func<EffectContext, bool> CostForRest68(string costText, string rest, Func<EffectContext, bool> recommend)
    {
        // BT18-033 "by returning 1 ~ from your trash to the bottom of the deck, play this card in your breeding area" — 트래시의 카드로 디지몬 1장을 코스트 없이 (비어 있는 육성 에어리어)
        if (Regex.IsMatch(rest.Trim(), @"^play this card in your breeding area without paying (?:the|its) cost\.?$", RegexOptions.IgnoreCase)
            && Regex.IsMatch(costText.Trim(), @"^returning (?:1|one) .+ from your trash to the bottom of (?:the|your) deck$", RegexOptions.IgnoreCase))
            return c => recommend(c) || c.Owner.BreedingArea == null && Diag46.HitTrue("costBreedingPlay68");
        if (!HandTrashOneCost68.IsMatch(costText.Trim().TrimEnd('.')) || StrongRest68(rest) is not { } strong) return recommend;
        return c => recommend(c) || strong(c);
    }

    /// <summary>
    /// 자신의 디지몬을 소멸시켜 등장 코스트를 줄이는 비용 (BT8-043 등): 원래 추천에 더해, 줄이지 않으면 낼 수 없고 (메모리가 모자람) 줄이면 낼 수 있으며,
    /// 소멸시킬 디지몬 (가장 Lv. · DP가 낮은 후보)의 Lv.가 등장시킬 카드의 Lv. 이하일 때 낸다
    /// </summary>
    internal static Func<EffectContext, bool> PlayCostRecommend68(string costText, int reduce, Func<EffectContext, bool> recommend)
    {
        var m = Regex.Match(costText.Trim().TrimEnd('.'), @"^deleting (?:1|one) of your (?<t>.+)$", RegexOptions.IgnoreCase);
        if (!m.Success || reduce < 4) return recommend;
        var spec = TargetSpec.Parse("all of your " + Regex.Replace(m.Groups["t"].Value, @"\bDigimon$", "", RegexOptions.IgnoreCase).Trim() + " Digimon");
        if (spec == null || spec.Opponent || spec.AnySide) return recommend;
        return c =>
        {
            if (recommend(c)) return true;
            if (c.Card == null || c.Card.Type != CardType.Digimon) return false;
            var victim = c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && spec.Filters.All(f => f(d))).OrderBy(d => d.TopCard.Level).ThenBy(d => d.GetCurrentDP()).FirstOrDefault();
            if (victim == null || victim.TopCard.Level > c.Card.Level) return false;
            int full = CardScripts.EffectivePlayCost(c.Owner, c.Card, c.Enemy);
            int mem = c.OwnerMemory;
            return mem < full && mem >= full - reduce && Diag46.HitTrue("playCostDeleteWorth68");
        };
    }
}
