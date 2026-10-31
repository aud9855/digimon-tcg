using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.48.0 미지원 문장 22차 묶음: 원래 규칙(1~21차)이 읽지 못한 절 · 고르는 효과 · 문장 · 동작만 22차 규칙으로 다시 읽는다.
///  - 같은 뜻 다른 표기 (ClauseRules48): 설명문이 빠진 "＜Draw 1＞ You may ~" (BT13-110) · "don't activate, and they gain ＜Rush＞ for the turn" (BT13-110 ≪딜레이≫) ·
///    "1 level 5 [A], [B] or [C]" (BT19-091) · "a play cost 11 or lower and the [X] trait" (BT20-058) · "1 play cost 12 or lower [X] text or [Y] trait card" (BT26-078) ·
///    "1 each of [A] and [B]" (AD1-003) · "진화원 전부를 덱 아래로, 그 디지몬을 덱 아래로" (BT12-112)
///  - 덱 아래로 (ActionExtra48): 패의 카드 (BT26-009) · 상대 트래시의 카드 (BT23-015 · BT9-083) · 상대 시큐리티 맨 위 (BT26-086)
/// 렌더러도 같은 표 (KoreanEffectRenderer.Batch48.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _clauseDepth48;
    [ThreadStatic] private static int _fallbackDepth48;
    [ThreadStatic] private static int _gate48;
    internal static bool Gate48 => _gate48 > 0;

    /// <summary>같은 뜻 다른 표기 — 절 전체에 차례로 (렌더러도 쓴다)</summary>
    internal static readonly Func<string, string>[] ClauseRules48 =
    {
        // 설명문 뒤 마침표가 없는 ≪드로우 N≫ 다음 문장 (BT13-110 "＜Draw 1＞ (Draw 1 card from your deck.) You may place ~")
        s => Regex.Replace(s, @"(＜Draw \d+＞(?:\s*\([^()]*\))?)\s+(?=You |Then|Place |Play |Add |Return |Delete |Trash )", "$1. "),
        // "Any [On Play] effects on Digimon played with this effect don't activate, and they gain ＜Rush＞ for the turn." (BT13-110)
        s => Regex.Replace(s, @"don't activate, and they gain (＜[^＞]+＞) for the turn", "don't activate. That Digimon gains $1 for the turn", RegexOptions.IgnoreCase),
        // "play 1 level 5 [WarGrowlmon], [Taomon] or [Rapidmon] from" (BT19-091)
        s => Regex.Replace(s, @"play 1 level (?<lv>\d+) (?<n>\[[^\]]+\](?:, \[[^\]]+\])*,? or \[[^\]]+\]) from", "play 1 level ${lv} ${n} Digimon card from", RegexOptions.IgnoreCase),
        // "1 Digimon card with a play cost 11 or lower and the [Cyborg] or [Machine] trait" (BT20-058)
        s => Regex.Replace(s, @"1 Digimon card with a play cost (?<n>\d+) or lower and the (?<t>\[[^\]]+\](?: or \[[^\]]+\])*) trait", "1 Digimon card with the ${t} trait and a play cost of ${n} or less", RegexOptions.IgnoreCase),
        // "1 play cost 12 or lower [Chronomon] text or [Titan] trait card" (BT26-078)
        s => Regex.Replace(s, @"1 play cost (?<n>\d+) or lower (?<x>\[[^\]]+\]) text or (?<t>\[[^\]]+\]) trait card", "1 card with ${x} in its text or the ${t} trait with a play cost of ${n} or less", RegexOptions.IgnoreCase),
        // "play 1 each of [Takato Matsuki] and [Guilmon]" (AD1-003)
        s => Regex.Replace(s, @"play 1 each of (?<a>\[[^\]]+\]) and (?<b>\[[^\]]+\])", "play 1 ${a} and 1 ${b}", RegexOptions.IgnoreCase),
        // "Return all of the digivolution cards of 1 of your opponent's Digimon to the bottom of its owner's deck, and return that Digimon to the bottom of its owner's deck" (BT12-112)
        //  — 그 디지몬에 겹쳐진 카드 전부가 덱 아래로 (진화원 다음에 디지몬 카드)
        s => Regex.Replace(s, @"Return all of the digivolution cards of 1 of your opponent's Digimon to the bottom of its owner's deck, and return that Digimon to the bottom of its owner's deck", "Return 1 of your opponent's Digimon to the bottom of its owner's deck", RegexOptions.IgnoreCase),
    };

    internal static string ClauseRewrite48(string rawClause)
    {
        string s = rawClause;
        foreach (var f in ClauseRules48) s = f(s);
        return s;
    }

    private static string? TryParse48(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse47(rawClause, inherited, card, def);
        if (err == null || _clauseDepth48 > 0) return err;
        _clauseDepth48++;
        _gate48++;
        int before = def?.Script41.Effects.Count ?? 0;
        try
        {
            if (TryParse47(rawClause, inherited, card, null) == null)
            {
                if (def != null) TryParse47(rawClause, inherited, card, def);
                return null;
            }
            string alt = ClauseRewrite48(rawClause);
            if (alt == rawClause || TryParse47(alt, inherited, card, null) != null) return err;
            if (def != null) TryParse47(alt, inherited, card, def);
            return null;
        }
        finally
        {
            _clauseDepth48--;
            _gate48--;
            if (def != null) WrapGate48(def, before);
        }
    }

    /// <summary>22차 규칙으로 등록한 시점 효과는 처리할 때도 문을 연다</summary>
    private static void WrapGate48(CardScriptDefinition def, int from)
    {
        var effects = def.Script41.Effects;
        for (int i = from; i < effects.Count; i++)
        {
            var inner = effects[i].Resolve;
            effects[i] = effects[i] with { Resolve = c => { _gate48++; try { inner(c); } finally { _gate48--; } } };
        }
    }

    /// <summary>고르는 효과 · ≪딜레이≫: 원래 규칙이 읽지 못했을 때만 선택지를 22차 표기로 고쳐 다시 읽는다 (BT13-110)</summary>
    private static string? TryParseChoice48(string rawHeader, IReadOnlyList<string> rawOptions, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParseChoice46(rawHeader, rawOptions, inherited, card, def);
        if (err == null || _gate48 > 0) return err;
        _gate48++;
        int before = def?.Script41.Effects.Count ?? 0;
        try
        {
            var opts = rawOptions.Select(ClauseRewrite48).ToList();
            if (opts.SequenceEqual(rawOptions) || TryParseChoice46(rawHeader, opts, inherited, card, null) != null) return err;
            if (def != null) TryParseChoice46(rawHeader, opts, inherited, card, def);
            return null;
        }
        finally
        {
            _gate48--;
            if (def != null) WrapGate48(def, before);
        }
    }

    // =====================================================================
    // 문장 · 동작 — 원래 규칙이 읽지 못한 것만 (문이 열려 있을 때)
    // =====================================================================

    private static (Step? Step, string? Error)? StepFallback48(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth48 >= 2 || !Gate48) return null;
        _fallbackDepth48++;
        try
        {
            string s = Regex.Replace(sentence.Trim().TrimEnd('.').Trim(), @"^Then,?\s+", "", I);
            bool optional = false;
            var may = Regex.Match(s, @"^You may (?<b>.+)$", I);
            if (may.Success) { optional = true; s = may.Groups["b"].Value; }
            if (ActionExtra48(s, card) is { Act: { } act }) return (new Step(null, act, optional, sentence.Trim()), null);
            return null;
        }
        finally { _fallbackDepth48--; }
    }

    private static (Action<EffectContext>? Act, string? Error)? ParseAction48(string raw, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth48 >= 2 || !Gate48) return null;
        _fallbackDepth48++;
        try { return ActionExtra48(Regex.Replace(raw.Trim().TrimEnd('.'), @"^you may ", "", I), card); }
        finally { _fallbackDepth48--; }
    }

    /// <summary>덱 아래로 되돌리는 동작 (덱 아래 = MainDeck의 끝)</summary>
    private static (Action<EffectContext>? Act, string? Error)? ActionExtra48(string s, CardData card)
    {
        string label = card.Name;
        Match m;
        // "return 1 card in your hand to the bottom of the deck" (BT26-009)
        if ((m = Regex.Match(s, @"^return (?<upto>up to )?(?<k>\d+) cards? (?:in|from) your hand to the bottom of (?:the|your) deck(?: in any order)?$", I)).Success)
        {
            int k = int.Parse(m.Groups["k"].Value); bool upTo = m.Groups["upto"].Success;
            return (c =>
            {
                var hand = c.Owner.Hand.ToList();
                if (hand.Count == 0) { c.LastStepResolved = false; Emit(c, label, "패가 없습니다"); return; }
                var ranked = hand.OrderBy(x => x.Type == CardType.Digimon ? x.Level : 99).ThenBy(x => x.PlayCost).ToList();   // 추천: 낮은 카드부터
                var idx = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Hand, ranked, Math.Min(k, ranked.Count), upTo, $"[{label}] 덱 아래로 되돌릴 패의 카드 {k}장을 고르세요.");
                var moved = new List<CardData>();
                foreach (int i in idx) if (c.Owner.Hand.Remove(ranked[i])) { c.Owner.MainDeck.Add(ranked[i]); moved.Add(ranked[i]); }
                if (moved.Count == 0) c.LastStepResolved = false;
                Diag46.Hit("handToDeckBottom48");
                Emit(c, label, moved.Count > 0 ? $"패의 카드 {moved.Count}장을 덱 아래로" : "되돌린 카드가 없습니다");
            }, null);
        }
        // "return up to 3 non-Digi-Egg cards from their trash to the bottom of the deck" (BT23-015) · "place 10 cards in your opponent's trash at the bottom of their deck in any order" (BT9-083)
        if ((m = Regex.Match(s, @"^(?:return (?<upto>up to )?(?<k>\d+) (?<nde>non-Digi-Egg )?cards? from (?:their|your opponent's) trash to the bottom of (?:the|their) deck|place (?<k>\d+) (?<nde>non-Digi-Egg )?cards? in your opponent's trash at the bottom of their deck)(?: in any order)?$", I)).Success)
        {
            int k = int.Parse(m.Groups["k"].Value); bool upTo = m.Groups["upto"].Success, nonEgg = m.Groups["nde"].Success;
            return (c =>
            {
                var pool = c.Enemy.Trash.Where(x => !nonEgg || x.Type != CardType.Digitama).ToList();
                if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, "상대의 트래시에 카드가 없습니다"); return; }
                // 추천: 상대가 다시 쓰기 쉬운 카드 (레벨 · 코스트가 높은 것)부터
                var ranked = pool.OrderByDescending(x => x.Level).ThenByDescending(x => x.PlayCost).ToList();
                var idx = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Other, ranked, Math.Min(k, ranked.Count), upTo, $"[{label}] 상대의 트래시에서 덱 아래로 되돌릴 카드 {k}장을 고르세요.");
                int n = 0;
                foreach (int i in idx) if (c.Enemy.Trash.Remove(ranked[i])) { c.Enemy.MainDeck.Add(ranked[i]); n++; }
                if (n == 0) c.LastStepResolved = false;
                Diag46.Hit("enemyTrashToDeckBottom48");
                Emit(c, label, n > 0 ? $"상대의 트래시에서 {n}장을 덱 아래로" : "되돌린 카드가 없습니다");
            }, null);
        }
        // "return your opponent's top security card to the bottom of the deck" (BT26-086)
        if (Regex.IsMatch(s, @"^return your opponent's top security card to the bottom of (?:the|their) deck$", I))
        {
            return (c =>
            {
                if (c.Enemy.SecurityStack.Count == 0) { c.LastStepResolved = false; Emit(c, label, "상대의 시큐리티가 없습니다"); return; }
                var top = c.Enemy.SecurityStack[0];
                c.Enemy.SecurityStack.RemoveAt(0);
                c.Enemy.FaceUpSecurity.Remove(top);   // 덱으로 가면 뒷면 (공개 상태 해제)
                c.Enemy.MainDeck.Add(top);
                Diag46.Hit("securityToDeckBottom48");
                Emit(c, label, "상대의 시큐리티 맨 위 카드를 덱 아래로");
            }, null);
        }
        return null;
    }
}
