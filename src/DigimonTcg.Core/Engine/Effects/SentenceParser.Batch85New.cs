using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.85.0 59차 묶음 — 테이머 아래 카드를 테이머마다 둔 뒤에 읽을 수 있게 된 문장 (원래 규칙이 읽지 못한 절만):
///  - BT12-109 "~ into a Digimon card with ＜Save＞ in its text placed under one of your Tamers ~" = "from under" · BT19-090 "from under your Tamer" = "from under your Tamers"
///  - BT11-087 "add ~ to your hand, and place ~ under 1 of your Tamers" — 공식 Q&amp;A BT11-087 Q1: 먼저 패에 넣고, 그다음 테이머 아래에 (두 처리)
///  - BT21-012 "If you did, place this Digimon under the played Tamer" · BT27-098 "After, place this Tamer as its bottom digivolution card" (종합 룰 4-7-8 — 그 테이머 아래 카드는 파기)
///  - 놓을 곳을 고르는 "as this Digimon's bottom digivolution card or under any of your red Tamers with inherited effects" (BT21-013 · EX10-039 · BT11-109)
///  - BT21-092 "Place all Digimon cards in 1 of your [Xros Heart] trait Digimon's digivolution cards under 1 of your Tamers" (공식 Q&amp;A BT21-092 Q4606 · Q4607)
///  - AD1-006 "~ you may place up to 4 ~ under 1 of your Tamers and play 1 such card" (공식 Q&amp;A AD1-006 Q6059 ~ Q6063)
///  - BT7-085 · BT7-087 "~ under this Tamer in any order, ~ digivolve this card into ~ as if this card is a level 5 red Digimon" (카드 번호 + 영어 API 문구)
///  - BT12-081 "~. If this Digimon has 4 or more digivolution cards, it may digivolve into [Quartzmon] ~ instead." (카드 번호 + 문구 — 대신: 둘 중 하나)
///  - P-249 "By placing 1 [Hybrid] card ~ as this Digimon's bottom digivolution card or under any of your Tamers with inherited effects, that Digimon or Tamer may digivolve ~" (카드 번호 + 문구)
///  - BT18-096 비용 "placing up to 4 of your Tamers with different colors under 1 of your [Susanoomon] as its bottom digivolution cards" (종합 룰 4-25 「XX가 다른」 — 여러 색 테이머는 다른 부분을 참조)
/// </summary>
internal static partial class SentenceParser
{
    internal const string Bt7085Api85 = "[Main] [Once Per Turn] By placing 5 cards with the [Hybrid] trait from your trash under this Tamer in any order, you may digivolve this card into an [EmperorGreymon] in your hand for its digivolution cost as if this card is a level 5 red Digimon.";
    internal const string Bt7087Api85 = "[Main] [Once Per Turn] You may place 5 cards with [Hybrid] trait from your hand under this Tamer in any order to digivolve it into a [MagnaGarurumon] in your hand for its digivolution cost as if this Tamer is a level 5 blue Digimon.";
    internal const string Bt12081Api85 = "[When Digivolving] You may play 1 level 4 or lower Digimon card with ＜Save＞ in its text from under one of your Tamers without paying the cost. If this Digimon has 4 or more digivolution cards, it may digivolve into [Quartzmon] in your hand or from under one of your Tamers and reduce the digivolution cost by 3 instead.";
    internal const string P249Api85 = "[Start of Your Main Phase] By placing 1 card with the [Hybrid] trait from your hand or trash as this Digimon’s bottom digivolution card or under any of your Tamers with inherited effects, that Digimon or Tamer may digivolve into a [Hybrid] trait Digimon card in the hand with the cost reduced by 2.";
    private const string HybridTamerEvo85 = "⟦85 hybrid-tamer-evo⟧";
    private const string QuartzEvo85 = "⟦85 quartz⟧";
    private const string HybridPlaceEvo85 = "⟦85 hybrid-place-evo⟧";
    internal const string Bt21092Api85 = "[Main] Place all Digimon cards in 1 of your [Xros Heart] trait Digimon's digivolution cards under 1 of your Tamers. Then, you may play 1 Digimon card with the [Xros Heart] trait from your hand with the play cost reduced by 1 for each card this effect placed.";
    private const string XrosUnderPlay85 = "⟦85 xros-under-play⟧";

    /// <summary>원래 규칙이 읽지 못한 절 → 같은 뜻의 다른 표기 (아니면 null)</summary>
    internal static string? Rewrite85(string rawClause, CardData card)
    {
        string t = Regex.Replace(rawClause.Trim(), @"\s+", " ");
        if (card.CardId == "BT7-085" && t == Bt7085Api85) return $"[Main] [Once Per Turn] {HybridTamerEvo85}.";
        if (card.CardId == "BT7-087" && t == Bt7087Api85) return $"[Main] [Once Per Turn] {HybridTamerEvo85}.";
        if (card.CardId == "BT12-081" && t == Bt12081Api85) return $"[When Digivolving] {QuartzEvo85}.";
        if (card.CardId == "P-249" && t == P249Api85) return $"[Start of Your Main Phase] {HybridPlaceEvo85}.";
        if (card.CardId == "BT21-092" && t == Bt21092Api85) return $"[Main] {XrosUnderPlay85}.";   // "그 후" 등장의 감소 = 놓은 장수 × 1 (영어 문장 규칙은 "for each"를 읽지 못해 1만 줄였다)
        string a = t;
        a = Regex.Replace(a, @"\bplaced under (one|1|any) of your Tamers for its digivolution cost\b", "from under $1 of your Tamers for its digivolution cost", I85);   // BT12-109
        a = Regex.Replace(a, @"\bfrom under your Tamer without paying\b", "from under your Tamers without paying", I85);   // BT19-090
        a = Regex.Replace(a, @"\b(?<add>add up to \d+ cards with \[[^\]]+\]) in one of their traits (?<from>from your trash to your hand), and (?<place>place up to \d+ )", "${add} in any of their traits ${from}. Then, ${place}", I85);   // BT11-087
        a = Regex.Replace(a, @"\bgain 1 memory for each placed card\b", "gain 1 memory for each card placed", I85);   // BT18-096
        return a == t ? null : a;
    }

    internal const string Bt10093Api85 = "[Your Turn] [Once Per Turn] When you would play 1 level 4 or higher Digimon card with [Bagra Army] trait, by placing up to 3 purple Digimon cards from under your Tamers in the digivolution cards of the Digimon card played, reduce the memory cost of that Digimon by 2 for each card placed.";
    internal const string Bt25090Api85 = "[Start of Your Turn] If you have 2 or less memory, set it to 3. [All Turns] When any Digimon suspend, by suspending this Tamer, you may place the top 2 cards of your deck face down under this Tamer. [Your Turn] [Once Per Turn] When you would use [Glowing Dawn] trait Option cards, by trashing the bottom face-down card from under any of your Tamers, reduce the cost by 1.";

    /// <summary>"[Your Turn] [Once Per Turn] When you would use an Option card with the [X] trait / [X] trait Option cards, by ~, reduce the cost by N" (BT25-049 · BT25-090)</summary>
    internal static readonly Regex UseOptionCostRe85 = new(
        @"^(?<labels>(?:\[(?:Your Turn|All Turns|Once Per Turn)\]\s*)*)When you would use (?:an Option card with the \[(?<x>[^\]]+)\] trait|\[(?<x>[^\]]+)\] trait Option cards?), by (?<cost>[^,]+?), reduce the cost by (?<n>\d+)\.?$", I85 | RegexOptions.Compiled);

    private static string? TryParse85(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse84(rawClause, inherited, card, def);
        if (err == null) return null;
        string t0 = Regex.Replace(rawClause.Trim(), @"\s+", " ");
        // BT10-093 [자신의 턴] [턴에 1회] 특징 「바그라군」 Lv.4 이상 디지몬 카드를 등장시킬 때, 테이머 아래의 퍼플 디지몬 카드 3장까지를 그 카드의 진화원에 놓는 것으로 1장마다 등장 코스트 -2
        //  (공식 Q&A BT10-093 Q2024: 여러 테이머 아래의 카드를 함께 놓아도 된다)
        if (!inherited && card.CardId == "BT10-093" && t0 == Bt10093Api85)
        {
            if (def != null) def.FieldPlayCostModifier(BagraUnderPlayCost85());
            return null;
        }
        // 옵션 사용 코스트 감소 (필드)
        if (!inherited && UseOptionCostRe85.Match(Normalize(t0)) is { Success: true } uo)
            return UseOptionCost85(uo, card, null) != null ? err : UseOptionCost85(uo, card, def);
        // BT25-090 (영어 API에 효과 세 개가 한 줄) — 효과마다 읽는다
        if (!inherited && card.CardId == "BT25-090" && t0 == Bt25090Api85)
        {
            var parts = Regex.Split(t0, @"(?<=\.) (?=\[(?:All Turns|Your Turn)\])").ToList();
            if (parts.Count != 3) return err;
            foreach (var p in parts)
                if ((UseOptionCostRe85.Match(Normalize(p)) is { Success: true } uo2 ? UseOptionCost85(uo2, card, null) : TryParse84(p, inherited, card, null)) != null) return err;
            if (def != null)
                foreach (var p in parts)
                {
                    if (UseOptionCostRe85.Match(Normalize(p)) is { Success: true } uo3) UseOptionCost85(uo3, card, def);
                    else TryParse84(p, inherited, card, def);
                }
            Diag46.Hit("splitClause85");
            return null;
        }
        // 원래 규칙이 읽지 못한 절만
        if (Rewrite85(rawClause, card) is not { } alt || TryParse84(alt, inherited, card, null) != null) return err;
        if (def != null)
        {
            TryParse84(alt, inherited, card, def);
            Diag46.Hit("rewrite85");
        }
        return null;
    }

    /// <summary>옵션 사용 코스트 감소 등록 (읽으면 null)</summary>
    private static string? UseOptionCost85(Match uo, CardData card, CardScriptDefinition? def)
    {
        string x = uo.Groups["x"].Value;
        if (ParseCost(uo.Groups["cost"].Value, card) is not { } pay) return $"미지원 비용 (옵션 사용 코스트 감소: by {uo.Groups["cost"].Value})";
        string labels = uo.Groups["labels"].Value;
        int n = int.Parse(uo.Groups["n"].Value);
        def?.FieldPlayCostModifier(new FieldPlayCostModifier(card.Type == CardType.Digimon || card.Type == CardType.Tamer ? EffectSource.Main : EffectSource.Main,
            o => (o.Type == CardType.Option || o.IsDual) && TargetSpec.HasTrait(o, x), n,
            new PlayCostOption(n, pay.CanPay, pay.Pay, pay.Recommend, pay.Describe, uo.Groups["cost"].Value), Optional: false,
            OncePerTurn: labels.Contains("[Once Per Turn]", StringComparison.OrdinalIgnoreCase), YourTurnOnly: labels.Contains("[Your Turn]", StringComparison.OrdinalIgnoreCase), Breeding: false)
            { UseOption85 = true });
        return null;
    }

    /// <summary>BT10-093 등장 코스트 감소 — 비용: 테이머 아래의 퍼플 디지몬 카드(앞면) 3장까지를 등장하는 카드의 진화원으로 (PendingUnderPlayed), 놓은 1장마다 -2</summary>
    private static FieldPlayCostModifier BagraUnderPlayCost85()
    {
        static List<CardData> Avail(EffectContext c) => c.Owner.UnderTamers.Where(x => !c.Owner.IsFaceDownUnderTamer(x) && c.Owner.HostOfUnderTamer(x) != null && x.Type == CardType.Digimon && x.AllColors.Contains(CardColor.Purple)).ToList();
        var pay = new PlayCostOption(2, c => Avail(c).Count > 0,
            c =>
            {
                var pool = Avail(c).OrderBy(x => x.Level).ToList();
                var picks = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, pool, Math.Min(3, pool.Count), upTo: true, $"[{c.Card.Name}] 테이머 아래에서 등장하는 카드의 진화원에 놓을 퍼플 디지몬 카드를 고르세요 (3장까지).");
                c.PaidCount = 0;
                foreach (var x in picks)
                    if (c.Owner.RemoveUnderTamer85(x)) { c.Engine.PendingUnderPlayed.Add(x); c.PaidCount++; }
                Diag46.Hit("bagraUnderPlayCost85");
            },
            _ => true, "테이머 아래의 퍼플 디지몬 카드 3장까지를 진화원에 놓아");
        return new FieldPlayCostModifier(EffectSource.Main, x => x.Type == CardType.Digimon && !x.IsDual && x.Level >= 4 && TargetSpec.HasTrait(x, "Bagra Army"), 2, pay,
            Optional: false, OncePerTurn: true, YourTurnOnly: true, Breeding: false)
        {
            Amount = c => 2 * Math.Min(3, Avail(c).Count),
            PaidAmount42 = c => 2 * Math.Max(0, c.PaidCount),
        };
    }

    /// <summary>v0.85.0 사건: EX10-056 "any of your opponent's Digimon or Tamers digivolve or effects place cards under them" — 상대의 디지몬/테이머가 진화 · 효과로 진화원 · 테이머 아래에 카드</summary>
    private static (List<EffectTrigger> Triggers, Func<EffectContext, bool>? Condition)? ParseEventPhrase85(string ev) =>
        Regex.IsMatch(ev.Trim(), @"^any of your opponent's Digimon or Tamers digivolve or effects place cards under them$", I85)
            ? (new List<EffectTrigger> { EffectTrigger.OpponentDigimonDigivolved, EffectTrigger.OpponentSourcesAdded, EffectTrigger.OpponentUnderTamerPlaced85 }, null)
            : null;

    // =====================================================================
    // 처리
    // =====================================================================

    private static readonly Regex DualDestRe85 = new(
        @"^Place (?<pick>.+?) from (?<from>your hand or (?:your )?trash|your trash|your hand) (?:under (?<d>this Digimon|(?:1|one|any) of your (?:.+? )?Digimon) as its bottom digivolution cards?|as (?<d2>this Digimon|(?:1|one|any) of your (?:.+? )?Digimon)'s bottom digivolution cards?) or under (?<t>.+)$", I85 | RegexOptions.Compiled);

    private static (Action<EffectContext>? Act, string? Error)? ParseAction85(string s, CardData card, List<EffectTrigger> triggers)
    {
        s = Regex.Replace(s.Trim().TrimEnd('.'), @"^Then,?\s+", "", I85);
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        Match m;
        if (s == HybridTamerEvo85 && card.CardId is "BT7-085" or "BT7-087" && card.Type == CardType.Tamer)
        {
            bool fromHand = card.CardId == "BT7-087";
            string name = fromHand ? "MagnaGarurumon" : "EmperorGreymon";
            var col = fromHand ? CardColor.Blue : CardColor.Red;
            return (c => HybridTamerEvo85Act(c, label, fromHand, name, col), null);
        }
        if (s == QuartzEvo85 && card.CardId == "BT12-081") return (c => QuartzEvo85Act(c, label), null);
        // BT19-090 "attack a player with 1 of your Digimon" (공식 "자신의 디지몬 1마리로 플레이어에게 어택할 수 있다") = "1 of your Digimon may attack a player"
        if ((m = Regex.Match(s, @"^attack a player with (?<t>(?:1|one) of your (?:.+? )?Digimon)$", I85)).Success)
            return ParseAction($"{m.Groups["t"].Value} may attack a player", card, triggers) is { Error: null } atk ? atk : null;
        // BT19-090 (고르는 효과의 항목) "from under your Tamer" = "from under your Tamers"
        if (Regex.IsMatch(s, @"\bfrom under your Tamer without paying\b", I85))
            return ParseAction(Regex.Replace(s, @"\bfrom under your Tamer without paying\b", "from under your Tamers without paying", I85), card, triggers) is { Error: null } bt19 ? bt19 : null;
        if (s == HybridPlaceEvo85 && card.CardId == "P-249") return (c => HybridPlaceEvo85Act(c, label), null);
        if (s == XrosUnderPlay85 && card.CardId == "BT21-092" && TargetSpec.Parse("1 of your [Xros Heart] trait Digimon") is { } xhT)
            return (c =>
            {
                AllSourcesUnderTamer85(c, label, xhT);
                int placed = c.PaidCount;
                // 그 후, 패의 특징 「크로스 하트」 디지몬 카드 1장을 놓은 1장마다 등장 코스트 -1 하여 등장시킬 수 있다 (코스트를 낸다 — 메모리가 상대 쪽으로 넘어가지 않는 카드만 추천)
                int CostOf(CardData x) => c.Engine.ReducedPlayCost(c.Owner, x, Math.Max(0, x.PlayCost - placed), x.PlayCost);
                var pool = c.Owner.Hand.Where(x => x.Type == CardType.Digimon && !x.IsDual && TargetSpec.HasTrait(x, "Xros Heart") && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Hand)).OrderByDescending(x => x.PlayCost).ToList();
                if (pool.Count == 0 || c.Owner.BattleArea.Count >= 12) return;
                var rec = pool.Where(x => CostOf(x) <= Math.Max(0, c.OwnerMemory)).ToList();
                var x = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, rec.Count > 0 ? rec.Concat(pool.Except(rec)).ToList() : pool, 1, upTo: true,
                    $"[{c.Card.Name}] 등장시킬 「크로스 하트」 디지몬 카드를 고르세요 (등장 코스트 -{placed} · 고르지 않아도 된다).").FirstOrDefault();
                if (!c.Engine.IsAskingPlayer79(c.Owner)) x = rec.FirstOrDefault();
                if (x == null) return;
                int cost = CostOf(x);
                if (!c.Engine.TryPlayByEffect(c.Owner, x, PlayOrigin.Hand, () => c.Owner.Hand.Remove(x), payCost: cost)) return;
                Emit(c, label, $"[{x.Name}] 등장 (코스트 {cost} 지불)");
            }, null);
        // BT21-012 "place this Digimon under the played Tamer"
        if (Regex.IsMatch(s, @"^place this Digimon under the played Tamer$", I85) && card.Type == CardType.Digimon)
            return (c =>
            {
                var host = c.Engine.LastTamerPlayedByEffect76;
                if (c.Self == null || host == null || !c.Owner.Tamers.Any(t => ReferenceEquals(t, host)) || !c.Owner.BattleArea.Contains(c.Self)
                    || !c.Engine.PlaceDigimonUnderTamers41(c.Owner, c.Self, c.Owner, fixedHost: host))
                { c.LastStepResolved = false; Emit(c, label, "이 디지몬을 놓을 테이머가 없습니다"); return; }
                Diag46.Hit("selfUnderPlayedTamer85");
                Emit(c, label, $"이 디지몬을 등장한 테이머 [{host.Name}] 아래에", attackerRemoved: ReferenceEquals(c.Self, c.Other));
            }, null);
        // BT27-098 "place this Tamer as its bottom digivolution card" — 이 효과로 등장한 디지몬
        if (Regex.IsMatch(s, @"^place this Tamer as (?:its|that Digimon's) bottom digivolution card$", I85) && card.Type == CardType.Tamer)
            return (c =>
            {
                var d = c.PlayedThisEffect.LastOrDefault(x => c.Owner.BattleArea.Contains(x)) ?? c.LastTargets.FirstOrDefault(x => c.Owner.BattleArea.Contains(x));
                if (d == null || !c.Owner.Tamers.Any(t => ReferenceEquals(t, c.Card)) || !c.Engine.PlaceTamerUnder(c.Owner, c.Card, d, canPrevent: true))
                { c.LastStepResolved = false; Emit(c, label, "이 테이머를 놓을 디지몬이 없습니다"); return; }
                c.LastTargets = new List<CardInstance> { d };
                Diag46.Hit("thisTamerUnderPlayed85");
                Emit(c, label, $"이 테이머를 [{d.TopCard.Name}]의 진화원 맨 아래에");
            }, null);
        // AD1-006 "From this Digimon's digivolution cards, you may place up to 4 ~ under 1 of your Tamers and play 1 such card without paying the cost"
        if ((m = Regex.Match(s, @"^From this Digimon's digivolution cards, you may place up to (?<n>\d+) (?<pick>.+?) cards under (?:1|one) of your Tamers and play 1 such card without paying the cost$", I85)).Success
            && ParseCardPick("1 " + m.Groups["pick"].Value + " card") is { } adPick)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c => PlaceSourcesThenPlay85(c, label, n, adPick.Match), null);
        }
        // 놓을 곳을 고른다: 디지몬의 진화원 맨 아래 또는 테이머 아래 (BT21-013 · EX10-039 · BT11-109)
        if ((m = DualDestRe85.Match(s)).Success
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } dPick
            && ParseUnderHost85(m.Groups["t"].Value) is { ThisTamer: false, Enemy: false } hostSpec)
        {
            string d = m.Groups["d"].Success ? m.Groups["d"].Value : m.Groups["d2"].Value;
            bool self = d.Equals("this Digimon", StringComparison.OrdinalIgnoreCase);
            var dSpec = self ? null : TargetSpec.Parse(Regex.Replace(d, @"^any of ", "1 of ", I85));
            if (!self && (dSpec == null || dSpec.Opponent || dSpec.AnySide)) return null;
            string from = m.Groups["from"].Value.ToLowerInvariant();
            bool upTo = IsUpTo(m.Groups["pick"].Value);
            return (c => DualDest85(c, label, dPick.Count, dPick.Match, upTo, from.Contains("hand"), from.Contains("trash"), self, dSpec, hostSpec), null);
        }
        return null;
    }

    /// <summary>BT18-096 비용: "placing up to 4 of your Tamers with different colors under 1 of your [Susanoomon] as its bottom digivolution cards" (놓은 장수 = PaidCount)</summary>
    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost85(string cost, CardData card)
    {
        // BT19-090 "unsuspending 1 of your [Shoutmon EX6] and 1 of your [ShootingStarmon]" — 공식 "1마리씩을 액티브로 하는 것으로": 레스트 상태인 그 디지몬 1마리씩 (서로 다른 디지몬)
        var u = Regex.Match(cost.Trim(), @"^unsuspending (?:1|one) of your (?<a>\[[^\]]+\]) and (?:1|one) of your (?<b>\[[^\]]+\])$", I85);
        if (u.Success)
        {
            string a = TargetSpec.Names(u.Groups["a"].Value)[0], b = TargetSpec.Names(u.Groups["b"].Value)[0];
            (CardInstance A, CardInstance B)? Pair(EffectContext c)
            {
                var ra = c.Owner.BattleArea.Where(d => d.IsSuspended && CardNames.Is(d, a)).ToList();
                var rb = c.Owner.BattleArea.Where(d => d.IsSuspended && CardNames.Is(d, b)).ToList();
                foreach (var x in ra) foreach (var y in rb) if (!ReferenceEquals(x, y)) return (x, y);
                return null;
            }
            return (c => Pair(c) != null,
                    c =>
                    {
                        if (Pair(c) is not { } p) { c.CostFailed = true; return; }
                        var ra = c.Owner.BattleArea.Where(d => d.IsSuspended && CardNames.Is(d, a)).ToList();
                        var x = ra.Count == 1 ? p.A : c.Engine.ChooseTargets(c.Owner, ra, 1, $"[{c.Card.Name}] 액티브로 할 [{a}]을(를) 고르세요.", optional: false, preselected: p.A).FirstOrDefault() ?? p.A;
                        var rb = c.Owner.BattleArea.Where(d => d.IsSuspended && CardNames.Is(d, b) && !ReferenceEquals(d, x)).ToList();
                        if (rb.Count == 0) { c.CostFailed = true; return; }
                        var y = rb.Count == 1 ? rb[0] : c.Engine.ChooseTargets(c.Owner, rb, 1, $"[{c.Card.Name}] 액티브로 할 [{b}]을(를) 고르세요.", optional: false, preselected: rb[0]).FirstOrDefault() ?? rb[0];
                        c.Engine.Unsuspend(x); c.Engine.Unsuspend(y);
                        Diag46.Hit("unsuspendPair85");
                        c.Log($"[{c.Card.Name}] [{x.TopCard.Name}] · [{y.TopCard.Name}]을(를) 액티브로 했습니다 (비용)");
                    },
                    _ => true, $"[{a}] · [{b}] 1마리씩을 액티브로 하여");
        }
        var m = Regex.Match(cost.Trim(), @"^placing up to (?<n>\d+) of your Tamers with different colou?rs under (?<t>(?:1|one) of your .+?) as its bottom digivolution cards?$", I85);
        if (!m.Success) return null;
        Func<EffectContext, CardInstance, bool>? ok = null;
        if (Regex.Match(m.Groups["t"].Value, @"^(?:1|one) of your (?<x>\[[^\]]+\])s?$", I85) is { Success: true } nm)
        {
            var xs = TargetSpec.Names(nm.Groups["x"].Value);
            ok = (_, d) => xs.Any(x => CardNames.Is(d, x));
        }
        else if (TargetSpec.Parse(m.Groups["t"].Value) is { Opponent: false, AnySide: false } spec)
            ok = (c, d) => spec.Filters.All(f => f(d)) && spec.ContextFilters.All(f => f(c, d));
        if (ok == null) return null;
        int n = int.Parse(m.Groups["n"].Value);
        List<CardInstance> Hosts(EffectContext c) => c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && ok(c, d)).ToList();
        return (c => Hosts(c).Count > 0 && c.Owner.Tamers.Count > 0,
                c =>
                {
                    c.PaidCount = 0;
                    var hosts = Hosts(c);
                    var d = hosts.Count == 1 ? hosts[0] : c.Engine.ChooseTargets(c.Owner, hosts, 1, $"[{c.Card.Name}] 테이머를 진화원 아래에 놓을 디지몬을 고르세요.", optional: false, preselected: hosts.FirstOrDefault()).FirstOrDefault();
                    if (d == null) { c.CostFailed = true; return; }
                    var chosen = new List<CardData>();
                    // 1명씩 고른다 — 지금까지 고른 테이머와 색이 서로 다를 수 있는 테이머만 (종합 룰 4-25-2)
                    while (chosen.Count < n)
                    {
                        var can = c.Owner.Tamers.Distinct().Where(t => !chosen.Any(x => ReferenceEquals(x, t)) && ColorsDistinct85(chosen.Append(t).ToList()))
                            .OrderBy(t => t.InheritedEffectText.Trim().Length > 0 ? 0 : 1).ToList();
                        if (can.Count == 0) break;
                        var fakes = can.Select(t => new CardInstance(t, 0) { IsSuspended = !c.Owner.HasActiveTamer(t) }).ToList();
                        var pick = c.Engine.ChooseTargets(c.Owner, fakes, 1, $"[{c.Card.Name}] [{d.TopCard.Name}]의 진화원 아래에 놓을 테이머를 고르세요 ({chosen.Count + 1}/{n} — 색이 서로 달라야 합니다, 고르지 않으면 그만).", optional: chosen.Count > 0, preselected: null).FirstOrDefault();
                        if (pick == null) break;
                        chosen.Add(can[fakes.IndexOf(pick)]);
                    }
                    foreach (var t in chosen)
                        if (c.Owner.BattleArea.Contains(d) && c.Engine.PlaceTamerUnder(c.Owner, t, d, canPrevent: true)) c.PaidCount++;
                    if (c.PaidCount == 0) { c.CostFailed = true; return; }
                    c.LastTargets = new List<CardInstance> { d };
                    Diag46.Hit("tamersUnderDifferentColors85");
                    c.Log($"[{c.Card.Name}] 색이 서로 다른 테이머 {c.PaidCount}명을 [{d.TopCard.Name}]의 진화원 아래에 놓았습니다 (비용)");
                },
                _ => true, $"색이 서로 다른 테이머 {n}명까지를 진화원 아래에 놓아");
    }

    /// <summary>테이머들의 색이 서로 다를 수 있는지 (여러 색 테이머는 다른 부분을 참조 — 종합 룰 4-25-2: 서로 다른 색을 1개씩 줄 수 있는지)</summary>
    internal static bool ColorsDistinct85(IReadOnlyList<CardData> tamers)
    {
        var used = new HashSet<CardColor>();
        bool Assign(int i)
        {
            if (i == tamers.Count) return true;
            foreach (var col in tamers[i].AllColors.Distinct())
            {
                if (!used.Add(col)) continue;
                if (Assign(i + 1)) return true;
                used.Remove(col);
            }
            return false;
        }
        return Assign(0);
    }

    /// <summary>AD1-006: 이 디지몬의 진화원에서 조건에 맞는 디지몬 카드 n장까지를 테이머 1명 아래에 (테이머가 있으면 가능한 한 1장 이상 — Q&amp;A Q6062), 그 뒤 남은 진화원에서 그 카드 1장을 코스트 없이 등장 (Q6063 테이머가 없으면 등장만)</summary>
    private static void PlaceSourcesThenPlay85(EffectContext c, string label, int n, Func<CardData, bool> match)
    {
        var self = c.Self;
        if (self == null) { c.LastStepResolved = false; return; }
        var pool = self.FaceUpSources.Where(x => x.Type == CardType.Digimon && match(x)).ToList();
        if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, "진화원에 놓거나 등장시킬 카드가 없습니다"); return; }
        var placed = new List<CardData>();
        if (c.Owner.Tamers.Count > 0)
        {
            // 추천: 등장시킬 카드 1장(등장 코스트가 가장 높은 것)을 남기고 나머지를 놓는다 — 1장뿐이면 그 카드를 놓는다 (Q6062)
            var keep = pool.Where(x => c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Sources)).OrderByDescending(x => x.PlayCost).ThenByDescending(x => x.Level).FirstOrDefault();
            var ranked = pool.OrderBy(x => ReferenceEquals(x, keep) ? 1 : 0).ThenBy(x => x.PlayCost).ToList();
            int max = Math.Min(n, ranked.Count);
            int rec = pool.Count == 1 ? 1 : Math.Min(max, ranked.Count - (keep != null ? 1 : 0));
            var idx = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.DigivolutionCards, ranked, max, upTo: true,
                $"[{c.Card.Name}] 테이머 아래에 놓을 진화원을 고르세요 (1 ~ {max}장 — 테이머가 있으면 1장 이상).");
            var picks = (idx.Count == 0 ? Enumerable.Range(0, Math.Max(1, rec)) : idx).Where(i => i < ranked.Count).Select(i => ranked[i]).ToList();
            if (!c.Engine.IsAskingPlayer79(c.Owner)) picks = ranked.Take(Math.Max(1, rec)).ToList();
            var host = UnderHost85(c, null, picks);
            if (host != null)
                foreach (var x in picks)
                    if (self.TakeSource(y => ReferenceEquals(y, x)) != null) { c.Engine.PlaceUnderTamer85(c.Owner, host, x); placed.Add(x); }
            if (placed.Count > 0) Emit(c, label, $"진화원 [{string.Join(", ", placed.Select(x => x.Name))}]을(를) [{host!.Name}] 아래에");
        }
        c.RecalculateFieldBuffs();
        // 남은 진화원에서 1장 등장 ("1枚をコストを支払わずに登場できる" — 특징 조건을 만족하는 카드만, Q6059)
        var playable = self.FaceUpSources.Where(x => x.Type == CardType.Digimon && match(x) && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Sources)).OrderByDescending(x => x.PlayCost).ThenByDescending(x => x.Level).ToList();
        if (playable.Count == 0 || c.Owner.BattleArea.Count >= 12) { if (placed.Count == 0) c.LastStepResolved = false; return; }
        var p = c.Engine.ChooseCards(c.Owner, CardChoiceZone.DigivolutionCards, playable, 1, upTo: true, $"[{c.Card.Name}] 코스트 없이 등장시킬 진화원을 고르세요 (고르지 않아도 된다).").FirstOrDefault();
        if (p == null) return;
        if (c.Engine.TryPlayByEffect(c.Owner, p, PlayOrigin.Sources, () => self.TakeSource(y => ReferenceEquals(y, p)) != null))
        {
            Diag46.Hit("sourcesUnderTamerThenPlay85");
            Emit(c, label, $"진화원 [{p.Name}] 코스트 없이 등장");
        }
    }

    /// <summary>BT21-092: 자신의 디지몬 1마리의 진화원의 디지몬 카드 전부를 테이머 1명 아래에 (놓은 장수 = PaidCount — "for each card this effect placed")</summary>
    private static void AllSourcesUnderTamer85(EffectContext c, string label, TargetSpec spec)
    {
        c.PaidCount = 0;
        var cands = c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && spec.Filters.All(f => f(d)) && spec.ContextFilters.All(f => f(c, d)) && d.FaceUpSources.Any(x => x.Type == CardType.Digimon) && !c.Engine.StackProtected42(c.Owner, d))
            .OrderByDescending(d => d.FaceUpSources.Count(x => x.Type == CardType.Digimon)).ToList();
        if (cands.Count == 0 || c.Owner.Tamers.Count == 0) { c.LastStepResolved = false; Emit(c, label, "놓을 진화원 · 테이머가 없습니다"); return; }
        var d = cands.Count == 1 ? cands[0] : c.Engine.ChooseTargets(c.Owner, cands, 1, $"[{c.Card.Name}] 진화원의 디지몬 카드를 테이머 아래에 놓을 디지몬을 고르세요.", optional: false, preselected: cands[0]).FirstOrDefault() ?? cands[0];
        var cards = d.GetEvolutionStackBottomUp().AsEnumerable().Reverse().Where(x => x.Type == CardType.Digimon && !d.IsFaceDown(x)).ToList();   // 위 → 아래
        var host = UnderHost85(c, null, cards);
        if (host == null) { c.LastStepResolved = false; return; }
        var placed = new List<CardData>();
        foreach (var x in cards)
            if (d.TakeSource(y => ReferenceEquals(y, x)) != null) { c.Engine.PlaceUnderTamer85(c.Owner, host, x); placed.Add(x); }
        c.PaidCount = placed.Count;
        c.LastTargets = new List<CardInstance> { d };
        if (placed.Count == 0) c.LastStepResolved = false;
        c.RecalculateFieldBuffs();
        Diag46.Hit("allSourcesUnderTamer85");
        Emit(c, label, placed.Count > 0 ? $"[{d.TopCard.Name}]의 진화원 [{string.Join(", ", placed.Select(x => x.Name))}]을(를) [{host.Name}] 아래에" : "놓을 카드가 없습니다");
    }

    /// <summary>패 · 트래시의 카드를 디지몬의 진화원 맨 아래 또는 테이머 1명 아래에 (BT21-013 · EX10-039 · BT11-109 — 고른 카드는 모두 한 곳에)</summary>
    private static void DualDest85(EffectContext c, string label, int count, Func<CardData, bool> match, bool upTo, bool hand, bool trash, bool self, TargetSpec? dSpec, UnderHostSpec85 hostSpec)
    {
        var digis = self ? (c.Self != null && c.Owner.BattleArea.Contains(c.Self) ? new List<CardInstance> { c.Self } : new List<CardInstance>())
            : c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && dSpec!.Filters.All(f => f(d)) && dSpec.ContextFilters.All(f => f(c, d))).OrderByDescending(d => d.TopCard.Level).ToList();
        var tamers = c.Engine.RankUnderTamerHosts85(c.Owner, c.Owner, Array.Empty<CardData>(), hostSpec.Filter);
        if (c.Owner.Tamers.Distinct().Count(hostSpec.Filter) == 1) tamers = c.Owner.Tamers.Distinct().Where(hostSpec.Filter).ToList();
        if (digis.Count + tamers.Count == 0) { c.LastStepResolved = false; Emit(c, label, "놓을 곳(디지몬 · 테이머)이 없습니다"); return; }
        var pool = new List<PoolCard>();
        if (trash) pool.AddRange(PoolFrom(c.Owner.Trash, x => match(x) && !ReferenceEquals(x, c.Card)));
        if (hand) pool.AddRange(PoolFrom(c.Owner.Hand, x => match(x) && !ReferenceEquals(x, c.Card)));
        if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, "놓을 카드가 없습니다"); return; }
        var zone = hand && trash ? CardChoiceZone.HandOrTrash : hand ? CardChoiceZone.Hand : CardChoiceZone.Trash;
        var picks = c.Engine.ChooseCardIndexes(c.Owner, zone, pool.Select(p => p.Card).ToList(), Math.Min(count, pool.Count), upTo, $"[{c.Card.Name}] 진화원 아래 또는 테이머 아래에 놓을 카드를 고르세요.").Select(i => pool[i]).ToList();
        if (picks.Count == 0) { c.LastStepResolved = false; return; }
        // 놓을 곳: 디지몬 + 테이머 (추천: 그 테이머에서 곧 진화할 수 있거나 테이머 아래 카드를 쓰는 테이머면 그 테이머, 아니면 디지몬)
        var tamerFakes = tamers.Select(t => new CardInstance(t, 0) { IsSuspended = !c.Owner.HasActiveTamer(t) }).ToList();
        bool preferTamer = tamers.Count > 0 && (digis.Count == 0 || c.Engine.RankUnderTamerHosts85(c.Owner, c.Owner, picks.Select(p => p.Card).ToList(), hostSpec.Filter).FirstOrDefault() is { } best
            && ((best.EffectText ?? "").Contains("under this Tamer", StringComparison.OrdinalIgnoreCase) || c.Owner.Hand.Any(h => h.Type == CardType.Digimon && c.Engine.TamerEvolutionCostFor(c.Owner, h, best) != null)));
        var dests = preferTamer ? tamerFakes.Concat(digis).ToList() : digis.Concat(tamerFakes).ToList();
        var dest = dests.Count == 1 ? dests[0] : c.Engine.ChooseTargets(c.Owner, dests, 1, $"[{c.Card.Name}] 카드를 놓을 곳(자신의 디지몬의 진화원 맨 아래 또는 테이머 아래)을 고르세요.", optional: false, preselected: null).FirstOrDefault() ?? dests[0];
        int ti = tamerFakes.IndexOf(dest);
        var names = new List<string>();
        foreach (var p in picks)
        {
            if (!TakeFromPool(c, p)) continue;
            if (ti >= 0) c.Engine.PlaceUnderTamer85(c.Owner, tamers[ti], p.Card);
            else dest.AddBottomSource(p.Card);
            names.Add(p.Card.Name);
        }
        if (names.Count == 0) { c.LastStepResolved = false; return; }
        if (ti < 0) c.LastTargets = new List<CardInstance> { dest };
        c.LastCard = ti >= 0 ? tamers[ti] : null;
        c.RecalculateFieldBuffs();
        Diag46.Hit(ti >= 0 ? "dualDestTamer85" : "dualDestDigimon85");
        Emit(c, label, $"[{string.Join(", ", names)}]을(를) {(ti >= 0 ? $"[{tamers[ti].Name}] 아래" : $"[{dest.TopCard.Name}]의 진화원 맨 아래")}에");
    }

    /// <summary>BT7-085 · BT7-087: 「하이브리드체」 카드 5장을 이 테이머 아래에 (원하는 순서) 놓는 것으로, 이 테이머를 Lv.5 디지몬으로 취급하여 패의 그 카드로 진화 코스트를 지불하여 진화할 수 있다</summary>
    private static void HybridTamerEvo85Act(EffectContext c, string label, bool fromHand, string name, CardColor col)
    {
        var tamer = c.Card;
        if (!c.Owner.Tamers.Any(t => ReferenceEquals(t, tamer))) { c.LastStepResolved = false; return; }
        var evo = c.Owner.Hand.Where(x => x.Type == CardType.Digimon && CardNames.Is(x, name) && x.EvoRequirementLevel == 5 && (x.EvoRequirementColor == col || x.AllColors.Contains(col)))
            .OrderByDescending(x => x.DP).FirstOrDefault();
        var zoneList = fromHand ? c.Owner.Hand : c.Owner.Trash;
        var pool = zoneList.Where(x => TargetSpec.HasTrait(x, "Hybrid") && !ReferenceEquals(x, evo)).ToList();
        if (pool.Count < 5) { c.LastStepResolved = false; Emit(c, label, $"{(fromHand ? "패" : "트래시")}에 특징 「하이브리드체」 카드가 5장 없습니다"); return; }
        int cost = evo?.EvoCost ?? 0;
        bool rec = evo != null && (!fromHand || c.Owner.Hand.Count - 6 >= 0);
        if (!c.Engine.AskYesNo(c.Owner, null, evo != null
                ? $"[{tamer.Name}] {(fromHand ? "패" : "트래시")}의 「하이브리드체」 카드 5장을 이 테이머 아래에 놓고 [{evo.Name}](으)로 진화할까요? (진화 코스트 {cost})"
                : $"[{tamer.Name}] 패에 [{name}]이(가) 없습니다. 그래도 「하이브리드체」 카드 5장을 이 테이머 아래에 놓을까요?", rec))
        { c.LastStepResolved = false; return; }
        var picks = c.Engine.ChooseCards(c.Owner, fromHand ? CardChoiceZone.Hand : CardChoiceZone.Trash, pool.OrderBy(x => x.Level).ToList(), 5, upTo: false,
            $"[{tamer.Name}] 이 테이머 아래에 놓을 「하이브리드체」 카드 5장을 놓을 순서대로 고르세요 (먼저 고른 카드가 위).");
        if (picks.Count < 5) { c.LastStepResolved = false; return; }
        foreach (var x in picks) { zoneList.Remove(x); c.Engine.PlaceUnderTamer85(c.Owner, tamer, x); }
        Diag46.Hit("hybridTamerEvo85");
        if (evo == null || !c.Owner.Hand.Contains(evo)) { Emit(c, label, "「하이브리드체」 카드 5장을 이 테이머 아래에 (진화할 카드 없음)"); return; }
        if (c.Engine.EvolveTamerByEffect(c.Owner, tamer, evo, cost) is { } inst) { c.LastTargets = new List<CardInstance> { inst }; Emit(c, label, $"이 테이머를 Lv.5 디지몬으로 취급하여 [{evo.Name}](으)로 진화 (코스트 {cost})"); }
        else { c.LastStepResolved = false; Emit(c, label, $"[{evo.Name}](으)로 진화할 수 없습니다"); }
    }

    /// <summary>BT12-081: 테이머 아래에서 《세이브》가 적힌 Lv.4 이하 디지몬 카드 1장을 코스트 없이 등장 — 진화원이 4장 이상이면 대신 패 · 테이머 아래의 「쿼츠몬」으로 진화 코스트 -3 하여 진화할 수 있다</summary>
    private static void QuartzEvo85Act(EffectContext c, string label)
    {
        var self = c.Self;
        var savePick = ParseCardPick("1 level 4 or lower Digimon card with ＜Save＞ in its text");
        var playPool = savePick == null ? new List<CardData>() : c.Owner.UnderTamers.Where(x => !c.Owner.IsFaceDownUnderTamer(x) && c.Owner.HostOfUnderTamer(x) != null && x.Type == CardType.Digimon && savePick.Value.Match(x) && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.UnderTamers))
            .OrderByDescending(x => x.PlayCost).ThenByDescending(x => x.DP).ToList();
        var evoPool = new List<(CardData Card, List<CardData> Zone)>();
        if (self != null && c.Owner.BattleArea.Contains(self) && self.EvolutionSources.Count >= 4)
        {
            foreach (var x in c.Owner.Hand.Where(x => x.Type == CardType.Digimon && CardNames.Is(x, "Quartzmon") && ActionValidator.ValidateEvolution(self, x).CanExecute)) evoPool.Add((x, c.Owner.Hand));
            foreach (var x in c.Owner.UnderTamers.Where(x => !c.Owner.IsFaceDownUnderTamer(x) && c.Owner.HostOfUnderTamer(x) != null && x.Type == CardType.Digimon && CardNames.Is(x, "Quartzmon") && ActionValidator.ValidateEvolution(self, x).CanExecute)) evoPool.Add((x, c.Owner.UnderTamers));
        }
        var options = new List<string>();
        if (evoPool.Count > 0) options.Add("「쿼츠몬」으로 진화 (진화 코스트 -3)");
        if (playPool.Count > 0 && c.Owner.BattleArea.Count < 12) options.Add("테이머 아래에서 《세이브》 디지몬 등장");
        if (options.Count == 0) { c.LastStepResolved = false; Emit(c, label, "등장 · 진화할 카드가 없습니다"); return; }
        options.Add("하지 않는다");
        int pick = c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 효과를 고르세요{(evoPool.Count > 0 ? " (진화원 4장 이상 — 대신 「쿼츠몬」으로 진화할 수 있다)" : "")}.", options, 0);
        if (pick < 0 || pick >= options.Count - 1) { c.LastStepResolved = false; return; }
        if (options[pick].StartsWith("「", StringComparison.Ordinal))
        {
            var (evo, zone) = evoPool.Count == 1 ? evoPool[0] : evoPool[Math.Max(0, c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Other, evoPool.Select(e => e.Card).ToList(), 1, false, $"[{c.Card.Name}] 진화할 「쿼츠몬」을 고르세요.").DefaultIfEmpty(0).First())];
            int cost = Math.Max(0, evo.EvoCost - (c.Engine.CanReduceEvoCost(c.Owner) ? 3 : 0));
            c.Engine.EvolveByEffect(c.Owner, self!, evo, cost, zone);   // 패 또는 테이머 아래에서 바로
            c.LastTargets = new List<CardInstance> { self! };
            Diag46.Hit("quartzInstead85");
            Emit(c, label, $"대신 [{evo.Name}](으)로 진화 (코스트 {cost})");
            return;
        }
        var x2 = playPool.Count == 1 ? playPool[0] : c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, playPool, 1, upTo: false, $"[{c.Card.Name}] 테이머 아래에서 코스트 없이 등장시킬 카드를 고르세요.").FirstOrDefault() ?? playPool[0];
        if (c.Engine.TryPlayByEffect(c.Owner, x2, PlayOrigin.UnderTamers, () => c.Owner.RemoveUnderTamer85(x2))) Emit(c, label, $"테이머 아래의 [{x2.Name}] 코스트 없이 등장");
        else c.LastStepResolved = false;
    }

    /// <summary>P-249: 패/트래시의 「하이브리드체」 카드 1장을 이 디지몬의 진화원 맨 아래 또는 진화원 효과를 가진 테이머 아래에 놓는 것으로, 그 디지몬/테이머를 패의 「하이브리드체」 디지몬 카드로 코스트 -2 하여 진화할 수 있다</summary>
    private static void HybridPlaceEvo85Act(EffectContext c, string label)
    {
        bool Hybrid(CardData x) => TargetSpec.HasTrait(x, "Hybrid");
        var pool = PoolFrom(c.Owner.Trash, Hybrid).Concat(PoolFrom(c.Owner.Hand, Hybrid)).ToList();
        var self = c.Self != null && c.Owner.BattleArea.Contains(c.Self) ? c.Self : null;
        var tamers = c.Owner.Tamers.Distinct().Where(t => !string.IsNullOrWhiteSpace(t.InheritedEffectText)).ToList();
        if (pool.Count == 0 || self == null && tamers.Count == 0) { c.LastStepResolved = false; Emit(c, label, "놓을 카드 · 곳이 없습니다"); return; }
        bool reduce = c.Engine.CanReduceEvoCost(c.Owner);
        List<(CardData Evo, int Cost)> EvosFor(CardInstance? d, CardData? t, CardData? placing) =>
            c.Owner.Hand.Where(x => x.Type == CardType.Digimon && Hybrid(x) && !ReferenceEquals(x, placing))
                .Select(x => (x, cost: d != null ? (ActionValidator.ValidateEvolution(d, x).CanExecute ? x.EvoCost : (int?)null) : c.Engine.TamerEvolutionCostFor(c.Owner, x, t!)))
                .Where(e => e.cost != null).Select(e => (e.x, Math.Max(0, e.cost!.Value - (reduce ? 2 : 0)))).ToList();
        bool anyEvo = (self != null && EvosFor(self, null, null).Count > 0) || tamers.Any(t => EvosFor(null, t, null).Count > 0);
        if (!c.Engine.AskYesNo(c.Owner, self, $"[{c.Card.Name}] 「하이브리드체」 카드 1장을 이 디지몬의 진화원 맨 아래 또는 테이머 아래에 놓고 진화할까요?", anyEvo))
        { c.LastStepResolved = false; return; }
        var card = pool[Math.Max(0, c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.HandOrTrash, pool.Select(p => p.Card).ToList(), 1, false, $"[{c.Card.Name}] 놓을 「하이브리드체」 카드를 고르세요.").DefaultIfEmpty(0).First())];
        var fakes = tamers.Select(t => new CardInstance(t, 0) { IsSuspended = !c.Owner.HasActiveTamer(t) }).ToList();
        var dests = new List<CardInstance>();
        bool tamerFirst = self == null || tamers.Any(t => EvosFor(null, t, card.Card).Count > 0) && (EvosFor(self, null, card.Card).Count == 0);
        if (tamerFirst) { dests.AddRange(fakes); if (self != null) dests.Add(self); }
        else { dests.Add(self!); dests.AddRange(fakes); }
        var dest = dests.Count == 1 ? dests[0] : c.Engine.ChooseTargets(c.Owner, dests, 1, $"[{c.Card.Name}] 카드를 놓을 곳(이 디지몬의 진화원 맨 아래 또는 진화원 효과를 가진 테이머 아래)을 고르세요.", optional: false, preselected: null).FirstOrDefault() ?? dests[0];
        int ti = fakes.IndexOf(dest);
        if (!TakeFromPool(c, card)) { c.LastStepResolved = false; return; }
        if (ti >= 0) c.Engine.PlaceUnderTamer85(c.Owner, tamers[ti], card.Card);
        else dest.AddBottomSource(card.Card);
        c.RecalculateFieldBuffs();
        Diag46.Hit("hybridPlaceEvo85");
        var evos = ti >= 0 ? EvosFor(null, tamers[ti], null) : EvosFor(dest, null, null);
        if (evos.Count == 0) { Emit(c, label, $"[{card.Card.Name}]을(를) 놓았습니다 (진화할 카드 없음)"); return; }
        int k = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Hand, evos.Select(e => e.Evo).ToList(), 1, upTo: true, $"[{c.Card.Name}] 진화할 「하이브리드체」 디지몬 카드를 고르세요 (코스트 -2 · 고르지 않아도 된다).").DefaultIfEmpty(-1).First();
        if (!c.Engine.IsAskingPlayer79(c.Owner)) k = 0;
        if (k < 0 || k >= evos.Count) return;
        var (evo, cost) = evos[k];
        if (ti >= 0)
        {
            if (c.Engine.EvolveTamerByEffect(c.Owner, tamers[ti], evo, cost) is { } inst) { c.LastTargets = new List<CardInstance> { inst }; Emit(c, label, $"테이머 [{tamers[ti].Name}] → [{evo.Name}] 진화 (코스트 {cost})"); }
        }
        else
        {
            c.Engine.EvolveByEffect(c.Owner, dest, evo, cost, c.Owner.Hand);
            c.LastTargets = new List<CardInstance> { dest };
            Emit(c, label, $"[{dest.TopCard.Name}] 진화 (코스트 {cost})");
        }
    }
}
