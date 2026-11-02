using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.84.0 58차 묶음 (원래 규칙이 읽지 못한 절만):
///  - EX9-062 "This card is also treated as level 4 for [Kimeramon]'s assembly." — 어셈블리에서만 Lv.4로도 취급 (GoldfishEngine.Assembly84)
///  - BT19-063 "~ you may delete 1 play cost 3 or lower Digimon or Tamer." — 주인을 말하지 않았다 = 양쪽 (공식 Q&amp;A BT19-063 Q3124: 자신의 디지몬 · 테이머도 소멸시킬 수 있다)
///  - EX13-045 (카드 번호 + 영어 API 문구 그대로일 때만): "If DNA digivolving, this Digimon attacks and all of your Digimon get +10000 DP ~. Then, this Digimon may battle ~"
///    — 공식 일본어 카드 리스트의 처리 순서: 어택 → DP → 배틀 (Q&amp;A EX13-045 Q7356: "그 후"는 조그레스 진화가 아니어도 처리)
///  - EX13-035 (카드 번호 + 문구): 공식 Q&amp;A EX13-035 Q7315 — 먼저 트래시의 그 카드 10장을 덱 아래로 되돌릴지 정하고 (되돌리면 상한 +6, Q7316: 10장 모두), 그 뒤 패 · 트래시에서 2장까지 등장 코스트 합계 상한까지
///  - BT19-102 (카드 번호 + 문구): 다른 디지몬 1마리를 고르고 (Q&amp;A BT19-102 Q3186: 자신 · 상대 어느 쪽이든), 그 진화원의 Lv.4 이하 디지몬 카드 1장을 코스트 없이 등장시키는 것으로 고른 디지몬을 소멸.
///    등장하는 카드는 그 카드의 주인의 배틀 에어리어 (종합 룰 3-1-3-8 — 어느 플레이어의 장소인지 지정하지 않으면 소유자의 장소)
/// </summary>
internal static partial class SentenceParser
{
    private const RegexOptions I84 = RegexOptions.IgnoreCase;

    /// <summary>EX13-045 영어 API 문구 (이 문구 그대로일 때만 바꾼다)</summary>
    internal const string Ex13045Api84 = "[When Digivolving] If DNA digivolving, this Digimon attacks and all of your Digimon get +10000 DP until your opponent's turn ends. Then, this Digimon may battle 1 of your opponent's Digimon.";
    private const string Ex13045Rw84 = "[When Digivolving] If DNA digivolving, this Digimon attacks. If DNA digivolving, all of your Digimon get +10000 DP until your opponent's turn ends. Then, this Digimon may battle 1 of your opponent's Digimon.";

    /// <summary>EX13-035 영어 API 문구</summary>
    internal const string Ex13035Api84 = "[On Play] [When Digivolving] You may play up to 2 Digimon cards with [Chuumon], [Sukamon] or [Etemon] in their names and up to 6 total play cost from your hand or trash without paying the costs. By returning 10 such cards from your trash to the bottom of the deck, add 6 to the play cost maximum.";
    private const string SukamonPlay84 = "⟦84 sukamon-play⟧";

    /// <summary>BT19-102 영어 API 문구</summary>
    internal const string Bt19102Api84 = "[On Play] [When Digivolving] Choose 1 other Digimon. By playing level 4 or lower Digimon card from its digivolution cards, delete the chosen Digimon.";
    private const string SacrificePlay84 = "⟦84 sacrifice-play⟧";

    /// <summary>EX9-062 "This card is also treated as level 4 for [Kimeramon]'s assembly."</summary>
    internal static readonly Regex AsmLevelRe84 = new(@"^This card is also treated as level (?<lv>\d+) for \[(?<x>[^\]]+)\]'s assembly\.?$", I84 | RegexOptions.Compiled);

    /// <summary>BT19-063 "delete up to 1 play cost 3 or lower Digimon or Tamer" (주인 없음 = 양쪽 — Rewrite84가 "you may delete 1 ~"를 바꾼 것)</summary>
    internal static readonly Regex AnySideDigimonOrTamerRe84 = new(@"^delete up to (?:1|one) play cost (?<n>\d+) or (?:lower|less) Digimon or Tamer$", I84 | RegexOptions.Compiled);

    /// <summary>카드 번호 + 영어 API 문구 그대로인 절 → 같은 뜻의 다른 표기 (아니면 null)</summary>
    internal static string? Rewrite84(string rawClause, CardData card)
    {
        string t = Regex.Replace(rawClause.Trim(), @"\s+", " ");
        if (card.CardId == "EX13-045" && t == Ex13045Api84) return Ex13045Rw84;
        if (card.CardId == "EX13-035" && t == Ex13035Api84) return $"[On Play] [When Digivolving] {SukamonPlay84}.";
        if (card.CardId == "BT19-102" && t == Bt19102Api84) return $"[On Play] [When Digivolving] {SacrificePlay84}.";
        // BT19-063 "you may delete 1 play cost 3 or lower Digimon or Tamer" (주인 없음) = "delete up to 1 ~" — 할지는 고르는 창에서 ("할 수 있다"를 영어 문장 점수로 묻지 않는다: 주인이 없는 소멸은 자신의 것으로 읽어 AI가 언제나 거절했다)
        string a = Regex.Replace(t, @"\byou may delete (?:1|one) (?=play cost \d+ or (?:lower|less) Digimon or Tamer\.)", "delete up to 1 ", I84);
        return a == t ? null : a;
    }

    private static string? TryParse84(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse83(rawClause, inherited, card, def);
        if (err == null) return null;
        // 원래 규칙이 읽지 못한 절만
        if (!inherited && card.Type == CardType.Digimon && AsmLevelRe84.Match(Normalize58(rawClause).Trim()) is { Success: true } asm)
        {
            def?.AsmLevelAlias84(asm.Groups["x"].Value.Trim(), int.Parse(asm.Groups["lv"].Value));
            return null;
        }
        if (Rewrite84(rawClause, card) is not { } alt || TryParse83(alt, inherited, card, null) != null) return err;
        if (def != null)
        {
            TryParse83(alt, inherited, card, def);
            Diag46.Hit("rewrite84");
        }
        return null;
    }

    private static (Action<EffectContext>? Act, string? Error)? ParseAction84(string s, CardData card, List<EffectTrigger> triggers)
    {
        s = Regex.Replace(s.Trim().TrimEnd('.'), @"^Then,?\s+", "", I84);
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        Match m;
        // BT19-063 양쪽의 등장 코스트 N 이하 디지몬 · 테이머 1마리(명) 소멸
        if ((m = AnySideDigimonOrTamerRe84.Match(s)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c => DeleteAnySideDigimonOrTamer84(c, n, label), null);
        }
        if (s == SukamonPlay84 && card.CardId == "EX13-035") return (c => SukamonPlay84Act(c, label), null);
        if (s == SacrificePlay84 && card.CardId == "BT19-102") return (c => SacrificePlay84Act(c, label), null);
        return null;
    }

    /// <summary>양쪽의 등장 코스트 n 이하 디지몬 · 테이머 중 1장 소멸 (추천: 상대의 것 — 등장 코스트 · DP가 높은 것부터, 자신의 것은 뒤)</summary>
    private static void DeleteAnySideDigimonOrTamer84(EffectContext c, int n, string label)
    {
        var pool = new List<(PlayerZones Zone, CardInstance? Digimon, CardData? Tamer)>();
        foreach (var (zone, mine) in new[] { (c.Enemy, false), (c.Owner, true) })
        {
            var ds = zone.BattleArea.Where(d => d.TamerProxyOf == null && d.PlayCost45 <= n && !c.Engine.IsUnaffectedBy(c.Owner, d))
                .OrderByDescending(d => mine ? -d.PlayCost45 : d.PlayCost45).ThenByDescending(d => mine ? -d.GetCurrentDP() : d.GetCurrentDP());
            var ts = zone.Tamers.Distinct().Where(t => t.PlayCost <= n).OrderByDescending(t => mine ? -t.PlayCost : t.PlayCost);
            var side = ds.Select(d => (zone, (CardInstance?)d, (CardData?)null)).Concat(ts.Select(t => (zone, (CardInstance?)null, (CardData?)t))).ToList();
            pool.AddRange(side);
        }
        Diag46.Hit("anySideDigimonOrTamer84");
        if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, "소멸시킬 디지몬 · 테이머가 없습니다"); return; }
        // AI: 상대의 것이 없으면 자신의 것은 소멸시키지 않는다 ("할 수 있다")
        bool asking = c.Engine.IsAskingPlayer79(c.Owner);
        if (!asking && !ReferenceEquals(pool[0].Zone, c.Enemy)) { c.LastStepResolved = false; Emit(c, label, "상대의 대상이 없어 소멸시키지 않았습니다"); return; }
        // 플레이어: 마지막 선택지 = 소멸시키지 않는다 ("할 수 있다"). 추천 = 상대의 것이 있으면 그것, 없으면 하지 않는다
        var options = pool.Select(x => $"{(ReferenceEquals(x.Zone, c.Owner) ? "자신" : "상대")}의 {(x.Digimon != null ? $"디지몬 [{x.Digimon.TopCard.Name}] (등장 코스트 {x.Digimon.PlayCost45})" : $"테이머 [{x.Tamer!.Name}] (등장 코스트 {x.Tamer.PlayCost})")}").ToList();
        options.Add("소멸시키지 않는다");
        int i = !asking ? 0 : c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 소멸시킬 등장 코스트 {n} 이하의 디지몬 · 테이머를 고르세요 (자신 · 상대 어느 쪽이든 — 고르지 않아도 된다).",
            options, ReferenceEquals(pool[0].Zone, c.Enemy) ? 0 : pool.Count);
        if (i < 0 || i >= pool.Count) { c.LastStepResolved = false; Emit(c, label, "소멸시키지 않았습니다"); return; }
        var (z, d0, t0) = pool[i];
        if (t0 != null)
        {
            c.Engine.DeleteTamer(z, t0);
            c.LastTargets = new List<CardInstance>();
            Emit(c, label, $"{(ReferenceEquals(z, c.Owner) ? "자신" : "상대")}의 테이머 [{t0.Name}] 소멸");
            return;
        }
        bool gone = ReferenceEquals(z, c.Enemy) ? c.DeleteEnemy(d0!) : c.Engine.DeleteFrom(c.Owner, d0!);
        c.LastTargets = new List<CardInstance> { d0! };
        if (!gone) c.LastStepResolved = false;
        Emit(c, label, gone ? $"{(ReferenceEquals(z, c.Owner) ? "자신" : "상대")}의 [{d0!.TopCard.Name}] 소멸" : $"[{d0!.TopCard.Name}]은(는) 소멸하지 않았습니다");
    }

    private static bool IsSukamonName84(CardData x) => new[] { "Chuumon", "Sukamon", "Etemon" }.Any(n => CardNames.Contains(x, n));

    /// <summary>
    /// EX13-035: ① 트래시의 명칭에 「츄몬」/「스카몬」/「에테몬」을 포함하는 카드 10장을 덱 아래로 되돌릴지 (되돌리면 상한 6 → 12 — Q&amp;A Q7315 · Q7316)
    /// ② 패 · 트래시에서 그 명칭을 포함하는 디지몬 카드를 2장까지, 등장 코스트 합계 상한까지 코스트 없이 등장 (1장씩 고른다)
    /// </summary>
    private static void SukamonPlay84Act(EffectContext c, string label)
    {
        int budget = 6;
        var trashSuch = c.Owner.Trash.Where(IsSukamonName84).ToList();
        if (trashSuch.Count >= 10)
        {
            // 추천: 되돌린 뒤에도 6을 넘는 조합(2장까지 · 합계 12 이하)이 남으면 되돌린다 — 등장 코스트가 낮은 카드부터 되돌린다
            var back = trashSuch.OrderBy(x => x.Type == CardType.Digimon ? x.PlayCost : -1).ThenBy(x => x.Level).Take(10).ToList();
            var left = c.Owner.Hand.Concat(c.Owner.Trash.Where(x => !back.Any(b => ReferenceEquals(b, x)))).Where(x => x.Type == CardType.Digimon && !x.IsDual && IsSukamonName84(x)).Select(x => x.PlayCost).OrderByDescending(x => x).ToList();
            int best = BestTwoUnder84(left, 12);
            bool recommend = best > Math.Min(6, BestTwoUnder84(c.Owner.Hand.Concat(c.Owner.Trash).Where(x => x.Type == CardType.Digimon && !x.IsDual && IsSukamonName84(x)).Select(x => x.PlayCost).ToList(), 6));
            if (c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 트래시에서 명칭에 「츄몬」/「스카몬」/「에테몬」을 포함하는 카드 10장을 덱 아래로 되돌려 이 효과의 등장 코스트 상한을 +6 (6 → 12) 할까요?", recommend))
            {
                var ranked = back.Concat(trashSuch.Where(x => !back.Any(b => ReferenceEquals(b, x)))).ToList();
                var idx = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Trash, ranked, 10, upTo: false, $"[{c.Card.Name}] 덱 아래로 되돌릴 카드 10장을 고르세요 (고른 순서대로 아래에 놓는다).");
                var picked = idx.Select(k => ranked[k]).ToList();
                if (picked.Count == 10)
                {
                    foreach (var x in picked) { c.Owner.Trash.Remove(x); c.Owner.MainDeck.Add(x); }
                    budget += 6;
                    Diag46.Hit("sukamonReturn84");
                    Emit(c, label, "트래시의 카드 10장을 덱 아래로 되돌려 등장 코스트 상한 +6 (12)");
                }
            }
        }
        int spent = 0, count = 0;
        while (count < 2 && c.Owner.BattleArea.Count < 12 && !c.State.IsGameOver)
        {
            var pool = c.Owner.Hand.Distinct().Select(x => (Card: x, Origin: PlayOrigin.Hand))
                .Concat(c.Owner.Trash.Distinct().Select(x => (Card: x, Origin: PlayOrigin.Trash)))
                .Where(p => p.Card.Type == CardType.Digimon && !p.Card.IsDual && IsSukamonName84(p.Card) && spent + p.Card.PlayCost <= budget)
                .OrderByDescending(p => p.Card.PlayCost).ThenByDescending(p => p.Card.Level).ToList();
            if (pool.Count == 0) break;
            var ks = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.HandOrTrash, pool.Select(p => p.Card).ToList(), 1, true,
                $"[{c.Card.Name}] 등장시킬 카드를 고르세요 ({count}/2장 · 등장 코스트 합계 {spent}/{budget} — 고르지 않으면 끝).");
            if (ks.Count == 0) break;
            var (x, origin) = pool[ks[0]];
            Func<bool> take = origin == PlayOrigin.Hand ? () => c.Owner.Hand.Remove(x) : () => c.Owner.Trash.Remove(x);
            if (!c.Engine.TryPlayByEffect(c.Owner, x, origin, take)) break;
            spent += x.PlayCost;
            count++;
            Diag46.Hit("sukamonPlay84");
        }
        if (count == 0) c.LastStepResolved = false;
        else c.RecalculateFieldBuffs();
    }

    /// <summary>등장 코스트 목록에서 2장까지 골라 cap 이하로 만들 수 있는 가장 큰 합</summary>
    private static int BestTwoUnder84(List<int> costs, int cap)
    {
        int best = 0;
        for (int i = 0; i < costs.Count; i++)
        {
            if (costs[i] <= cap) best = Math.Max(best, costs[i]);
            for (int j = i + 1; j < costs.Count; j++)
                if (costs[i] + costs[j] <= cap) best = Math.Max(best, costs[i] + costs[j]);
        }
        return best;
    }

    /// <summary>
    /// BT19-102: 다른 디지몬 1마리를 고르고 (자신 · 상대), 그 진화원의 Lv.4 이하 디지몬 카드 1장을 코스트 없이 등장 (그 카드의 주인의 배틀 에어리어 — 종합 룰 3-1-3-8)시키면 고른 디지몬을 소멸.
    /// 추천 (AI): 상대의 디지몬 중 DP가 가장 높은 것 — 상대에게 Lv.4 이하 디지몬 1마리를 주고 큰 디지몬을 없앤다. 상대 쪽에 고를 것이 없으면 하지 않는다
    /// </summary>
    private static void SacrificePlay84Act(EffectContext c, string label)
    {
        static bool Playable(CardData x) => x.Type == CardType.Digimon && !x.IsDual && x.Level is > 0 and <= 4;
        var others = c.Enemy.BattleArea.Concat(c.Owner.BattleArea)
            .Where(d => !ReferenceEquals(d, c.Self) && d.TamerProxyOf == null && d.FaceUpSources.Any(Playable)).ToList();
        Diag46.Hit("sacrificePlay84");
        bool asking = c.Engine.IsAskingPlayer79(c.Owner);
        var ranked = others.OrderByDescending(d => c.Enemy.BattleArea.Contains(d) ? 1 : 0).ThenByDescending(d => d.GetCurrentDP()).ToList();
        if (!asking) ranked = ranked.Where(d => c.Enemy.BattleArea.Contains(d)).ToList();
        if (ranked.Count == 0) { c.LastStepResolved = false; Emit(c, label, "진화원에 Lv.4 이하 디지몬 카드가 있는 다른 디지몬이 없습니다"); return; }
        var chosen = c.Engine.ChooseTargets(c.Owner, ranked, 1, $"[{c.Card.Name}] 다른 디지몬 1마리를 고르세요 (자신 · 상대 어느 쪽이든 — 그 진화원의 Lv.4 이하 디지몬 카드를 등장시키면 고른 디지몬을 소멸).", true, null).FirstOrDefault();
        if (chosen == null) { c.LastStepResolved = false; return; }
        var zone = c.Enemy.BattleArea.Contains(chosen) ? c.Enemy : c.Owner;
        var cands = chosen.FaceUpSources.Where(Playable).Distinct().Where(x => c.Engine.CanPlayByEffect(zone, x, PlayOrigin.Sources))
            .OrderBy(x => ReferenceEquals(zone, c.Enemy) ? x.DP : -x.DP).ToList();   // 상대 쪽이면 약한 카드를 내준다
        if (cands.Count == 0) { c.LastStepResolved = false; Emit(c, label, $"[{chosen.TopCard.Name}]의 진화원에서 등장시킬 수 있는 카드가 없습니다"); return; }
        var k = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.DigivolutionCards, cands, 1, upTo: true,
            $"[{c.Card.Name}] [{chosen.TopCard.Name}]의 진화원에서 코스트 없이 등장시킬 Lv.4 이하 디지몬 카드를 고르세요 ({(ReferenceEquals(zone, c.Owner) ? "자신" : "상대")}의 배틀 에어리어에 등장 — 등장시키면 [{chosen.TopCard.Name}] 소멸).");
        if (k.Count == 0) { c.LastStepResolved = false; return; }
        var x = cands[k[0]];
        if (!c.Engine.TryPlayByEffect(zone, x, PlayOrigin.Sources, () => chosen.TakeSource(y => ReferenceEquals(y, x)) != null)
            || !zone.BattleArea.Any(d => ReferenceEquals(d.TopCard, x)))
        { c.LastStepResolved = false; Emit(c, label, $"[{x.Name}]을(를) 등장시키지 못해 소멸시키지 않았습니다"); return; }
        c.RecalculateFieldBuffs();
        if (!zone.BattleArea.Contains(chosen)) { Emit(c, label, $"[{x.Name}] 등장 — [{chosen.TopCard.Name}]은(는) 이미 배틀 에어리어에 없습니다"); return; }
        bool gone = ReferenceEquals(zone, c.Enemy) ? c.DeleteEnemy(chosen) : c.Engine.DeleteFrom(c.Owner, chosen);
        c.LastTargets = new List<CardInstance> { chosen };
        if (!gone) c.LastStepResolved = false;
        Emit(c, label, $"[{x.Name}] 등장 ({(ReferenceEquals(zone, c.Owner) ? "자신" : "상대")}의 배틀 에어리어) → [{chosen.TopCard.Name}] {(gone ? "소멸" : "소멸하지 않았습니다")}");
    }
}
