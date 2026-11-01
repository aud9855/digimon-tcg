using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.60.0 AI 추천 (읽기 · 한글은 그대로 — 비용을 낼지, 어느 카드로 낼지만):
///  - ≪디지버스트≫: 뒤의 효과가 트래시에서 쓰는 카드("Play 1 purple level 3 Digimon card from your trash" ST6-13)와 맞는 진화원을 먼저 파기 —
///    파기할 진화원은 주인이 고른다 (종합 룰 16-14-1 · 공식 Q&amp;A ST6-13 Q1: ≪디지버스트≫로 파기한 Lv.3 카드를 곧바로 등장시킬 수 있다)
///  - "By deleting N of your ~, play this card" (EX4-011 · EX10-011 · EX10-054 [트래시]): 소멸시킬 디지몬보다 등장시킬 이 카드가 나으면 (Lv.이 같거나 높고 DP가 크면) 낸다.
///    전에는 Lv.3 이하 디지몬만 소멸시키는 추천이라 이 카드들은 AI가 쓰지 않았다
/// </summary>
internal static partial class SentenceParser
{
    /// <summary>by-형 비용의 AI 추천 · 내기를 뒤의 효과에 맞춘다 (바꿀 것이 없으면 그대로)</summary>
    private static (Action<EffectContext> Pay, Func<EffectContext, bool> Recommend) CostForRest60(string costText, string rest, CardData card,
        Action<EffectContext> pay, Func<EffectContext, bool> recommend)
    {
        costText = costText.Trim().TrimEnd('.');
        // ≪디지버스트≫ 뒤의 효과가 트래시에서 고르는 카드
        if (Regex.IsMatch(costText, @"^trashing \d+ of this Digimon's digivolution cards$", I)
            && Regex.Match(rest, @"\b(?:play|add|return|place)s? (?<pick>.+?) from (?:your |the )?(?:hand or )?trash\b", I) is { Success: true } um
            && (ParseCardPick(um.Groups["pick"].Value) ?? ParseNamePick(um.Groups["pick"].Value)) is { } use)
        {
            var match = use.Match;
            return (c =>
            {
                var outer = c.Engine.BurstUse60;
                c.Engine.BurstUse60 = match;
                try { pay(c); }
                finally { c.Engine.BurstUse60 = outer; }
            }, recommend);
        }
        // 자신의 디지몬을 소멸시켜 이 카드를 등장
        var pm = Regex.Match(rest, @"^(?:you may )?play this card\b(?:.*?\breduced by (?<r>\d+))?", I);
        var dm = Regex.Match(costText, @"^[Dd]eleting (?<n>\d+|one) of your (?<other>other )?(?<t>.+)$");
        if (pm.Success && dm.Success && card.Type == CardType.Digimon)
        {
            int n = dm.Groups["n"].Value == "one" ? 1 : int.Parse(dm.Groups["n"].Value);
            int reduce = pm.Groups["r"].Success ? int.Parse(pm.Groups["r"].Value) : 0;
            bool free = Regex.IsMatch(rest, @"without paying (?:its|the) (?:memory |play )?cost", I);
            string t = Regex.Replace(dm.Groups["t"].Value, @"\bwith a digivolution card and ", "with 1 or more digivolution cards and ", I);
            t = Regex.Replace(t, @"\btheir (names?|texts?)\b", "its $1", I);
            Func<CardInstance, bool>? filter = null;
            if (Regex.IsMatch(t, @"^\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*$"))
            {
                var names = Regex.Matches(t, @"\[([^\]]+)\]").Select(x => x.Groups[1].Value).ToList();
                filter = d => names.Any(x => TargetSpec.NameHas(d, x));
            }
            else if (TargetSpec.Parse("all of your " + t) is { ContextFilters.Count: 0, Opponent: false, AnySide: false } spec) filter = d => spec.Filters.All(f => f(d));
            if (filter == null) return (pay, recommend);
            return (pay, c => recommend(c) || UpgradeWorth60(c, card, n, filter, free, reduce));
        }
        return (pay, recommend);
    }

    /// <summary>소멸시킬 디지몬 N마리(Lv. · DP 낮은 순)보다 등장시킬 카드가 나은지 — Lv.이 같거나 높고, 등장시킬 카드의 DP(【등장 시】가 있으면 +2000)가 소멸시킬 디지몬 DP 합 이상</summary>
    private static bool UpgradeWorth60(EffectContext c, CardData played, int n, Func<CardInstance, bool> filter, bool free, int reduce)
    {
        var victims = c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && !ReferenceEquals(d, c.Self) && filter(d))
            .OrderBy(d => d.TopCard.Level).ThenBy(d => d.GetCurrentDP()).Take(n).ToList();
        if (victims.Count < n) return false;
        if (victims.Any(d => d.TopCard.Level > played.Level)) return false;
        if (!free && Math.Max(0, played.PlayCost - reduce) > Math.Max(0, c.Engine.MemoryOf(c.Owner))) return false;   // 등장 코스트를 낼 메모리
        bool onPlay = CardScripts.For(played)?.Effects.Any(e => e.Trigger == EffectTrigger.OnPlay) == true;
        int value = played.DP + (onPlay ? 2000 : 0);
        bool worth = value >= victims.Sum(d => d.TopCard.DP);
        return worth && Diag46.HitTrue("upgradeDeleteCost60");
    }
}
