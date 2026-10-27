using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.42.0 효과로 링크 (공식 ST22-12 "자신의 패 또는 이 디지몬의 진화원에서, 특징 「소셜」/「내비」/「툴」을 가진 디지몬 카드 1장을 이 디지몬에게 지불하는 코스트 -2 하여 링크할 수 있다" ·
/// EX10-070 "자신의 트래시에서, 특징 「어플몬」을 가진 디지몬 카드 1장을 그 디지몬에게 코스트를 지불하지 않고 링크할 수 있다"):
/// "(You may) link 1 [Appmon] trait Digimon card from your hand to 1 of your Digimon without paying the cost" · "~ with the cost reduced by 2" ·
/// "link up to 2 cards from your hand or trash to any of your Digimon ~" · "~ from your hand, trash or this Digimon's digivolution cards to this Digimon ~".
/// 링크 조건 · 링크 코스트는 카드의 것 (영어 데이터 · 공식 한글 문구 — 모르면 링크하지 않는다). 코스트를 낼 때는 메모리가 0 미만이 되지 않을 때만 (턴이 끝나지 않게)
/// </summary>
internal static partial class SentenceParser
{
    private const string LinkZone42 = @"(?:your |this Digimon's |your Digimon's )?(?:hand|trash|digivolution cards)";

    internal static readonly Regex LinkRe42 = new(
        $@"^(?<may>you may )?link (?<n>up to \d+|\d+|an?) (?<pick>.+?) from (?<from>{LinkZone42}(?:(?:,| or|, or) {LinkZone42})*) (?:(?:to|with) (?<t>any of your Digimon|1 of your [^,]*?Digimon(?: on the field)?|1 of those Digimon|this Digimon) ?)?(?:(?<free>without paying (?:the|its) (?:link )?costs?)|with the (?:link )?cost reduced by (?<red>\d+))$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static (Action<EffectContext>? Act, string? Error)? LinkAction42(string s, CardData card, List<EffectTrigger> triggers, string label)
    {
        var m = LinkRe42.Match(s);
        if (!m.Success) return null;
        string n = m.Groups["n"].Value;
        int count = int.TryParse(Regex.Match(n, @"\d+").Value, out int k) ? k : 1;
        string pickText = m.Groups["pick"].Value.Trim();
        Func<CardData, bool>? match = Regex.IsMatch(pickText, @"^cards?$", I) ? _ => true
            : (ParseCardPick("1 " + Regex.Replace(pickText, @"s$", "")) ?? ParseNamePick("1 " + pickText) ?? ParseCardPick("1 " + pickText))?.Match;
        if (match == null) return null;
        string from = m.Groups["from"].Value.ToLowerInvariant();
        bool fromHand = from.Contains("hand"), fromTrash = from.Contains("trash"), fromThisSrc = from.Contains("this digimon's"), fromAllSrc = from.Contains("your digimon's");
        string t = m.Groups["t"].Success ? m.Groups["t"].Value : "any of your Digimon";
        TargetSpec? spec = null;
        bool self = t.Equals("this Digimon", StringComparison.OrdinalIgnoreCase), those = t.Equals("1 of those Digimon", StringComparison.OrdinalIgnoreCase);
        if (!self && !those && !t.StartsWith("any of your", StringComparison.OrdinalIgnoreCase)
            && (spec = TargetSpec.Parse("all of your " + Regex.Replace(Regex.Replace(t, @"^1 of your ", "", I), @" on the field$", "", I))) is not { Opponent: false, AnySide: false })
            return null;
        bool free = m.Groups["free"].Success;
        int red = m.Groups["red"].Success ? int.Parse(m.Groups["red"].Value) : 0;
        return (c =>
        {
            int linked = 0;
            for (int i = 0; i < count; i++)
            {
                // 링크할 디지몬 후보
                var targets = (self ? (c.Self != null && c.Owner.BattleArea.Contains(c.Self) ? new List<CardInstance> { c.Self } : new())
                    : those ? (c.Other != null && c.Owner.BattleArea.Contains(c.Other) ? new List<CardInstance> { c.Other } : c.LastTargets.Where(c.Owner.BattleArea.Contains).ToList())
                    : c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && (spec == null || spec.Filters.All(f => f(d)))).ToList());
                // 링크할 카드 후보: (카드, 있던 곳 — 진화원이면 그 디지몬)
                var pool = new List<(CardData Card, List<CardData>? Zone, CardInstance? Host)>();
                if (fromHand) pool.AddRange(c.Owner.Hand.Where(x => x != c.Card && match(x)).Distinct().Select(x => (x, (List<CardData>?)c.Owner.Hand, (CardInstance?)null)));
                if (fromTrash) pool.AddRange(c.Owner.Trash.Where(match).Distinct().Select(x => (x, (List<CardData>?)c.Owner.Trash, (CardInstance?)null)));
                if (fromThisSrc && c.Self != null) pool.AddRange(c.Self.FaceUpSources.Where(match).Distinct().Select(x => (x, (List<CardData>?)null, (CardInstance?)c.Self)));
                if (fromAllSrc) foreach (var d in c.Owner.BattleArea) pool.AddRange(d.FaceUpSources.Where(match).Distinct().Select(x => (x, (List<CardData>?)null, (CardInstance?)d)));
                int memory = c.Engine.MemoryOf(c.Owner);
                var options = (from p in pool
                               from d in targets
                               where GoldfishEngine.MeetsLinkRequirement(p.Card, d) == true && GoldfishEngine.LinkCostOf(p.Card) is int
                               let cost = free ? 0 : Math.Max(0, GoldfishEngine.LinkCostOf(p.Card)!.Value - red)
                               where cost == 0 || memory - cost >= 0
                               select (p, d, cost)).ToList();
                if (options.Count == 0) break;
                // 추천: 링크 DP가 큰 카드 · DP가 높은 디지몬
                var best = options.OrderByDescending(o => GoldfishEngine.LinkDp(o.p.Card)).ThenByDescending(o => o.d.GetCurrentDP()).First();

                bool took = best.p.Zone != null ? best.p.Zone.Remove(best.p.Card) : best.p.Host!.TakeSource(x => ReferenceEquals(x, best.p.Card)) != null;
                if (!took) break;
                if (best.cost > 0) c.Engine.AddMemory(c.Owner, -best.cost);
                c.Engine.LinkCard(c.Owner, best.d, best.p.Card);
                Diag42.Hit(ref Diag42.Link);
                linked++;
                Emit(c, label, $"[{best.p.Card.Name}]을(를) [{best.d.TopCard.Name}]에 링크 (코스트 {best.cost})");
            }
            if (linked == 0) { c.LastStepResolved = false; Emit(c, label, "링크할 카드가 없습니다"); }
            c.PaidCount = linked;
        }, null);
    }
}
