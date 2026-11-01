using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.73.0 미지원 문장 47차 묶음 (원래 규칙이 읽지 못한 처리만):
///  - EX11-027 "you may link this Digimon or 1 [Maquinamon] in your hand to 1 of your other Digimon without paying the cost" —
///    배틀 에어리어의 카드도 링크할 수 있다 (종합 룰 6-5-1-4 · 10-1-1). 링크 카드는 에어리어에 속하지 않으므로 (4-9-4) 이 디지몬은 배틀 에어리어를 떠나고,
///    겹쳐져 있던 진화원 · 링크 카드는 함께 파기된다 (4-7-8). 링크할 디지몬은 링크 조건에 맞아야 한다 (10-1-3-1)
///  - EX11-053 [소멸 시] "play 1 [Omnimon (X Antibody)] from your hand or under your [King Drasil_7D6]s on the field" — 필드(배틀 에어리어 · 육성 에어리어, 3-4-6)의
///    「위그드라실_7D6」의 진화원에서도 · "Then, place this card as the played Digimon's bottom digivolution card" — 트래시의 이 카드를 등장한 디지몬의 진화원 맨 아래에
/// </summary>
internal static partial class SentenceParser
{
    private static (Action<EffectContext>? Act, string? Error)? ParseAction73(string s, CardData card, List<EffectTrigger> triggers)
    {
        s = Regex.Replace(s.Trim().TrimEnd('.'), @"^Then,?\s+", "", RegexOptions.IgnoreCase);
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        Match m;
        // EX11-027 "link this Digimon or 1 [Maquinamon] in your hand to 1 of your other Digimon without paying the cost"
        if ((m = Regex.Match(s, @"^(?:you may )?link this Digimon or (?<p>1 \[[^\]]+\]) in your hand to (?:1|one) of your other Digimon without paying (?:the|its) costs?$", RegexOptions.IgnoreCase)).Success
            && card.Type == CardType.Digimon && ParseNamePick(m.Groups["p"].Value) is { Count: 1 } lp)
        {
            var match = lp.Match;
            return (c => LinkThisOrHand73(c, match, label), null);
        }
        // EX11-053 "play 1 [Omnimon (X Antibody)] from your hand or under your [King Drasil_7D6]s on the field without paying the cost"
        if ((m = Regex.Match(s, @"^(?:you may )?play (?<p>1 \[[^\]]+\]) from your hand or under your (?<h>\[[^\]]+\])s? on the field without paying (?:the|its) costs?$", RegexOptions.IgnoreCase)).Success
            && ParseNamePick(m.Groups["p"].Value) is { Count: 1 } pp)
        {
            var match = pp.Match;
            var hosts = TargetSpec.Names(m.Groups["h"].Value);
            return (c => PlayHandOrUnder73(c, match, hosts, label), null);
        }
        // EX11-053 [소멸 시] "place this card as the played Digimon's bottom digivolution card" — 트래시로 간 이 카드
        if (Regex.IsMatch(s, @"^(?:you may )?place this card as the played Digimon's bottom digivolution card$", RegexOptions.IgnoreCase) && triggers.Contains(EffectTrigger.OnDeletion))
            return (c => TrashedThisUnderPlayed73(c, label), null);
        // EX5-070 (옵션 【메인】) "place this card as its bottom digivolution card" — 앞 문장의 그 디지몬 (공식 한글 EX5-070과 같은 뜻)
        if (Regex.IsMatch(s, @"^place this card as (?:its|that Digimon's) bottom digivolution card$", RegexOptions.IgnoreCase) && card.Type == CardType.Option && triggers.Contains(EffectTrigger.Main))
            return (c => OptionUnderThat73(c, label), null);
        // BT20-003 (진화원 효과) "place 1 of your Tamers with ~ as the bottom digivolution card of this Digimon with no Tamer cards in its digivolution cards"
        //  — 공식 한글 BT20-003과 같은 뜻 (≪마인드 링크≫와 같은 놓기 — 종합 룰 16-28-1)
        if ((m = Regex.Match(s, @"^(?:you may )?place 1 of your Tamers (?<w>with .+?) as the bottom digivolution card of this Digimon with no Tamer cards in its digivolution cards$", RegexOptions.IgnoreCase)).Success
            && ParseCardPick("1 Tamer card " + m.Groups["w"].Value) is { Count: 1 } tp)
        {
            var match = tp.Match;
            return (c => TamerUnderThis73(c, match, label), null);
        }
        return null;
    }

    private static string? TryParse73(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse72(rawClause, inherited, card, def);
        if (err == null) return null;
        // 원래 규칙이 읽지 못한 절만: 같은 뜻의 다른 표기로 다시 읽는다 (BT8-066 · BT7-110 · EX5-070 진화원 효과)
        string alt = ClauseRewrite73(rawClause);
        if (alt == rawClause || TryParse72(alt, inherited, card, null) != null) return err;
        if (def != null) TryParse72(alt, inherited, card, def);
        return null;
    }

    /// <summary>같은 뜻 다른 표기 (렌더러도 쓴다)</summary>
    internal static string ClauseRewrite73(string clause)
    {
        string t = clause;
        // BT8-066 "you may digivolve this Digimon into a Digimon card in your hand with the [X Antibody] trait by paying its digivolution cost. When digivolving with this effect, reduce the digivolution cost by 1."
        //  — 공식 한글 BT8-066과 같은 뜻 (이 효과로 진화할 때 코스트 -1)
        t = Regex.Replace(t, @"\byou may digivolve this Digimon into a Digimon card in your hand with (?<w>the \[[^\]]+\] trait) by paying its digivolution cost\. When digivolving with this effect, reduce the digivolution cost by (?<n>\d+)\.?$",
            "this Digimon may digivolve into a Digimon card with ${w} in the hand with the digivolution cost reduced by ${n}.", RegexOptions.IgnoreCase);
        // BT7-110 "Your level 4 Digimon can digivolve into 1 Digimon card in your hand with matching colors and [Ten Warriors] trait for its digivolution cost, ignoring its level."
        //  — 공식 한글 BT7-110: Lv.만 무시하고 같은 색 (색 조건은 본다)
        t = Regex.Replace(t, @"\bYour level (?<lv>\d+) Digimon can digivolve into 1 Digimon card in your hand with matching colou?rs and (?<x>\[[^\]]+\]) trait for its digivolution cost, ignoring its level\.?$",
            "1 of your level ${lv} Digimon may digivolve into a Digimon card with the ${x} trait in your hand for its digivolution cost, ignoring its level.", RegexOptions.IgnoreCase);
        // EX5-070 (진화원 효과) "from this Digimon's digivolution cards, return 1 Digimon card to the hand and place 1 [X Antibody] on top of your security stack"
        //  — 공식 한글 EX5-070: 둘 다 이 디지몬의 진화원에서
        t = Regex.Replace(t, @", from this Digimon's digivolution cards, return (?<p>1 Digimon card) to (?:the|your) hand and place (?<x>1 \[[^\]]+\]) on top of your security stack\.?$",
            ", return ${p} from this Digimon's digivolution cards to the hand. Then, place ${x} from this Digimon's digivolution cards on top of your security stack.", RegexOptions.IgnoreCase);
        return t;
    }

    // ---- 조건 (원래 규칙이 읽지 못한 것만 — ParseEventConditionChain 끝) ----

    private static Func<EffectContext, bool>? ParseEventCondition73(string cond)
    {
        // EX5-070 "If it did" — 공식 "그랬다면" (앞 문장의 처리가 일어났는지)
        if (Regex.IsMatch(cond.Trim(), @"^it did$", RegexOptions.IgnoreCase))
            return c => c.LastStepResolved;
        return null;
    }

    private static void OptionUnderThat73(EffectContext c, string label)
    {
        var host = c.LastTargets.FirstOrDefault(d => c.Owner.BattleArea.Contains(d));
        // 사용 중인 옵션은 어느 곳에도 없다 (SentenceParser.PlaceThisUnder37과 같다)
        if (host == null || c.Card.Type != CardType.Option || c.Trigger != EffectTrigger.Main) { c.LastStepResolved = false; return; }
        host.AddBottomSource(c.Card);
        c.ThisCardMoved = true;
        c.RecalculateFieldBuffs();
        Diag46.Hit("optionUnderThat73");
        Emit(c, label, $"이 카드를 [{host.TopCard.Name}]의 진화원 맨 아래에 놓는다");
    }

    private static void TamerUnderThis73(EffectContext c, Func<CardData, bool> match, string label)
    {
        var self = c.Self != null && c.Owner.BattleArea.Contains(c.Self) ? c.Self : null;
        if (self == null || self.SourcesAny(x => x.Type == CardType.Tamer)) { c.LastStepResolved = false; Emit(c, label, "진화원에 테이머 카드가 있어 놓을 수 없습니다"); return; }
        var tamers = c.Owner.Tamers.Where(t => match(t)).ToList();
        if (tamers.Count == 0) { c.LastStepResolved = false; Emit(c, label, "놓을 테이머가 없습니다"); return; }
        var t = tamers.Count == 1 ? tamers[0] : c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, tamers, 1, upTo: false, $"[{c.Card.Name}] [{self.TopCard.Name}]의 진화원 아래에 놓을 테이머를 고르세요.").FirstOrDefault() ?? tamers[0];
        if (!c.Engine.PlaceTamerUnder(c.Owner, t, self, canPrevent: true)) { c.LastStepResolved = false; return; }
        Diag46.Hit("tamerUnderThis73");
        Emit(c, label, $"테이머 [{t.Name}]을(를) [{self.TopCard.Name}]의 진화원 맨 아래에");
    }

    private static void LinkThisOrHand73(EffectContext c, Func<CardData, bool> match, string label)
    {
        var self = c.Self != null && c.Owner.BattleArea.Contains(c.Self) && !GoldfishEngine.IsToken(c.Self.TopCard) ? c.Self : null;
        // 링크할 카드 후보: 이 디지몬(맨 위 카드) · 패의 지정 카드 — 받을 디지몬 = 이 디지몬이 아닌 자신의 디지몬 중 링크 조건에 맞는 것
        List<CardInstance> TargetsFor(CardData x) => c.Owner.BattleArea.Where(d => !ReferenceEquals(d, c.Self) && !GoldfishEngine.IsToken(d.TopCard) && GoldfishEngine.MeetsLinkRequirement(x, d) != false).ToList();
        var hand = c.Owner.Hand.Where(match).Where(x => TargetsFor(x).Count > 0).ToList();
        bool selfOk = self != null && TargetsFor(self.TopCard).Count > 0;
        if (!selfOk && hand.Count == 0) { c.LastStepResolved = false; Emit(c, label, "링크할 카드 · 디지몬이 없습니다"); return; }
        var opts = new List<string>();
        if (hand.Count > 0) opts.Add($"패의 [{hand[0].Name}]");
        if (selfOk) opts.Add($"이 디지몬 [{self!.TopCard.Name}]");
        int pick = opts.Count == 1 ? 0 : c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 무엇을 링크할까요?", opts, 0);   // AI: 필드의 디지몬을 남기는 패 쪽
        bool fromSelf = selfOk && (hand.Count == 0 || pick == 1);
        CardData linkCard;
        if (fromSelf) linkCard = self!.TopCard;
        else linkCard = hand.Count == 1 ? hand[0] : c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, hand, 1, upTo: false, $"[{c.Card.Name}] 링크할 카드를 고르세요.").FirstOrDefault() ?? hand[0];
        var targets = TargetsFor(linkCard).OrderByDescending(d => d.GetCurrentDP()).ToList();
        var d = c.Engine.ChooseTargets(c.Owner, targets, 1, $"[{c.Card.Name}] [{linkCard.Name}]을(를) 링크할 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault() ?? targets[0];
        bool ok = fromSelf ? c.Engine.LinkFieldDigimon73(c.Owner, self!, d) : c.Owner.Hand.Remove(linkCard);
        if (!ok) { c.LastStepResolved = false; Emit(c, label, "링크하지 못했습니다"); return; }
        if (!fromSelf) c.Engine.LinkCard(c.Owner, d, linkCard);
        Diag46.Hit(fromSelf ? "linkThisDigimon73" : "linkFromHand73");
        Emit(c, label, $"{(fromSelf ? "이 디지몬" : "패의")} [{linkCard.Name}]을(를) [{d.TopCard.Name}]에 링크 (코스트 없이)");
    }

    private static void PlayHandOrUnder73(EffectContext c, Func<CardData, bool> match, IReadOnlyList<string> hosts, string label)
    {
        if (c.Owner.BattleArea.Count >= 12) { c.LastStepResolved = false; return; }
        // 필드 = 배틀 에어리어 + 육성 에어리어 (종합 룰 3-4-6)
        var hostList = c.Owner.BattleArea.Append(c.Owner.BreedingArea).Where(d => d != null && hosts.Any(h => CardNames.Is(d!.TopCard, h))).Cast<CardInstance>().ToList();
        var pool = new List<(CardData Card, CardInstance? Host)>();
        foreach (var x in c.Owner.Hand.Where(x => x.Type == CardType.Digimon && match(x) && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Hand))) pool.Add((x, null));
        foreach (var h in hostList)
            foreach (var x in h.SourcesWhere(x => x.Type == CardType.Digimon && match(x) && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Sources)))
                pool.Add((x, h));
        if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, "등장시킬 카드가 없습니다"); return; }
        // AI: 진화원 쪽을 먼저 (패를 남긴다)
        var ranked = pool.OrderBy(p => p.Host == null ? 1 : 0).ToList();
        var chosen = ranked.Count == 1 ? ranked[0].Card : c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, ranked.Select(p => p.Card).ToList(), 1, upTo: false, $"[{c.Card.Name}] 패 또는 진화원에서 등장시킬 카드를 고르세요.").FirstOrDefault() ?? ranked[0].Card;
        var (card, host) = ranked.First(p => ReferenceEquals(p.Card, chosen));
        bool ok = host != null
            ? c.Engine.TryPlayByEffect(c.Owner, card, PlayOrigin.Sources, () => host.TakeSource(y => ReferenceEquals(y, card)) != null)
            : c.Engine.TryPlayByEffect(c.Owner, card, PlayOrigin.Hand, () => c.Owner.Hand.Remove(card));
        if (!ok) { c.LastStepResolved = false; return; }
        c.RecalculateFieldBuffs();
        Diag46.Hit("playHandOrUnder73");
        Emit(c, label, $"{(host != null ? $"[{host.TopCard.Name}]의 진화원" : "패")}의 [{card.Name}] 코스트 없이 등장");
    }

    private static void TrashedThisUnderPlayed73(EffectContext c, string label)
    {
        var played = c.PlayedThisEffect.LastOrDefault(d => c.Owner.BattleArea.Contains(d));
        var card = c.Card;
        if (played == null || card == null || !c.Owner.Trash.Contains(card)) { c.LastStepResolved = false; return; }
        c.Owner.Trash.Remove(card);
        played.AddBottomSource(card);
        c.RecalculateFieldBuffs();
        Diag46.Hit("trashedThisUnderPlayed73");
        Emit(c, label, $"이 카드를 [{played.TopCard.Name}]의 진화원 맨 아래에");
    }
}
