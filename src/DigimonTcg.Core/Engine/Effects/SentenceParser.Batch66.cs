using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.66.0 미지원 문장 40차 묶음 (파서 쪽) — 원래 규칙(39차까지)이 읽지 못한 절만:
///  - EX2-039 "When this card is trashed from your deck, if it wasn't trashed by [EX2-039 Impmon]'s effect, ~" — 이 카드를 파기한 효과가 적힌 카드를 본다
///    (엔진 GoldfishEngine.ThisTrashedByCard66 — 효과 밖이면 null)
///  - BT15-060 · BT17-068 "While this card is revealed from the deck, its name is also treated as [Omnimon]" · "~ this card is also treated as level 6"
///    — 덱에서 공개한 카드를 고를 때만 그 명칭 · Lv.로도 본다 (RevealAlias66)
///  - BT7-097 · BT3-030 진화원의 디지몬 카드를 다른 디지몬으로서 등장 — 앞의 규칙이 읽는 표기로
///  - BT21-087 · EX2-072 · BT25-078 덱 위 공개 후 "A 하거나 B 한다" (둘 중 하나 — ⟦rvor66:K⟧)
///  - BT18-078 상대의 디지몬 / 테이머 1마리(명)의 원래 색을 화이트 이외의 1색으로 (⟦recolor66⟧ — 엔진 GoldfishEngine.Batch66)
/// </summary>
internal static partial class SentenceParser
{
    /// <summary>바꿔 쓴 절을 다시 읽는 중 (같은 규칙으로 또 바꿔 쓰지 않는다)</summary>
    [ThreadStatic] private static int _depth66;

    private static string? TryParse66(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse65(rawClause, inherited, card, def);
        if (err == null || _depth66 > 0) return err;
        string t = Normalize58(rawClause);
        // BT15-060 · BT17-068 — 덱에서 공개한 카드를 고르는 모든 공개 효과가 카드 문장에서 직접 읽는다 (등록할 효과는 없다)
        if (RevealAlias66.ClauseRe.IsMatch(t)) return null;
        string alt = ClauseRewrite66(t);
        if (alt == t) return err;
        _depth66++;
        try
        {
            if (TryParse65(alt, inherited, card, null) != null) return err;
            if (def != null) TryParse65(alt, inherited, card, def);
            return null;
        }
        finally { _depth66--; }
    }

    // ---- 덱 위 공개 후 "A 하거나 B 한다" ----

    /// <summary>공개 후 둘 중 하나: Verb = play · add · place (이 디지몬의 진화원 맨 아래) · evo (자신의 디지몬 1마리가 코스트 없이 진화)</summary>
    internal sealed record RevealOr66(int N, string VerbA, string PickA, string VerbB, string PickB, string Rest, bool MayB);

    private static readonly ConcurrentDictionary<string, int> RevealOrIds66 = new();
    private static readonly ConcurrentDictionary<int, RevealOr66> RevealOrSpecs66 = new();

    internal static RevealOr66? RevealOrSpec66(int id) => RevealOrSpecs66.TryGetValue(id, out var s) ? s : null;

    private static string RevealOrMark66(RevealOr66 spec)
    {
        string key = $"{spec.N}|{spec.VerbA}|{spec.PickA}|{spec.VerbB}|{spec.PickB}|{spec.Rest}|{spec.MayB}";
        int id = RevealOrIds66.GetOrAdd(key, _ => { int k; lock (RevealOrSpecs66) { k = RevealOrSpecs66.Count + 1; RevealOrSpecs66[k] = spec; } return k; });
        return $"⟦rvor66:{id}⟧";
    }

    // BT21-087 "Reveal the top 3 cards of your deck. Among them, play 1 [Vemmon] without paying the cost or add 1 card with [Vemmon] in its text to the hand. Trash the rest."
    internal static readonly Regex RevealPlayOrAddRe66 = new(@"\bReveal the top (?<n>\d+) cards of your deck\. Among them, play (?<a>1 [^.,]+?) without paying (?:the|its) cost or add (?<b>1 [^.,]+?) to (?:the|your) hand\. (?<rest>Trash the rest|Return the rest to the bottom of the deck)\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // EX2-072 "Reveal the top 5 cards of your deck. You may digivolve 1 of your Digimon into 1 non-white Digimon card among them without paying its memory cost. If you don't, add 1 Digimon card among them to your hand. Place the remaining cards at the bottom of your deck in any order."
    internal static readonly Regex RevealEvoElseAddRe66 = new(@"\bReveal the top (?<n>\d+) cards of your deck\. You may digivolve 1 of your Digimon into (?<a>1 [^.,]+?) among them without paying (?:its|the) (?:memory )?cost\. If you don't, add (?<b>1 [^.,]+?) among them to (?:the|your) hand\. (?<rest>Place the remaining cards at the bottom of your deck in any order|Trash the rest)\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // BT25-078 "Reveal the top 3 cards of your deck. Among them, add 1 card with [Three Musketeers] in its text to the hand, or you may place 1 [Three Musketeers] trait card as this Digimon's bottom digivolution card. Return the rest to the bottom of the deck."
    internal static readonly Regex RevealAddOrPlaceRe66 = new(@"\bReveal the top (?<n>\d+) cards of your deck\. Among them, add (?<a>1 [^.,]+?) to (?:the|your) hand, or you may place (?<b>1 [^.,]+?) as this Digimon's bottom digivolution card\. (?<rest>Return the rest to the bottom of the deck|Trash the rest)\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string RestOf66(string rest) => rest.StartsWith("Trash", StringComparison.OrdinalIgnoreCase) ? "trash" : "bottom";

    // BT18-078 "Until the end of your opponent's turn, change 1 of their Digimon or Tamers into a color other than white."
    internal static readonly Regex RecolorRe66 = new(@"\bUntil the end of your opponent's turn, change 1 of (?:their|your opponent's) Digimon or Tamers into a color other than white\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>같은 뜻 다른 표기 · 이 묶음의 동작 표시 — 절 전체에 차례로 (렌더러도 쓴다)</summary>
    internal static readonly Func<string, string>[] ClauseRules66 =
    {
        // BT7-097 "Choose up to 2 Digimon cards in the digivolution cards of one of your Digimon and play them as other Digimon without paying their memory costs."
        //  — 자신의 디지몬 1마리의 진화원에서 2장까지 (같은 디지몬의 진화원)
        s => Regex.Replace(s, @"\bChoose up to (?<n>\d+) Digimon cards in the digivolution cards of (?:one|1) of your Digimon and play them as other Digimon without paying their memory costs\.?$",
            "Play up to ${n} Digimon cards from 1 of your Digimon's digivolution cards without paying their costs.", RegexOptions.IgnoreCase),
        // BT3-030 "You may play 1 level 4 or lower digivolution card of 1 of your Digimon cards as another Digimon without paying its memory cost." — 그 진화원의 디지몬 카드 (공식 한글 카드 문장 기준)
        s => Regex.Replace(s, @"\bYou may play 1 (?<lv>level \d+ or lower) digivolution card of 1 of your Digimon cards as another Digimon without paying its memory cost\.?$",
            "You may play 1 ${lv} Digimon card from 1 of your Digimon's digivolution cards without paying the cost.", RegexOptions.IgnoreCase),
        // BT9-104 "Reveal the top 3 cards of your deck. You may digivolve 1 of your Digimon into a Digimon card with the [X Antibody] trait without its memory cost. Trash the rest."
        //  — 그중 (공식 한글 카드 문장 기준)
        s => Regex.Replace(s, @"\bReveal the top (?<n>\d+) cards of your deck\. You may digivolve 1 of your Digimon into (?<p>an? [^.]+?) without its memory cost\. (?<rest>Trash the rest\.)",
            "Reveal the top ${n} cards of your deck. Among them, 1 of your Digimon may digivolve into ${p} without paying its memory cost. ${rest}", RegexOptions.IgnoreCase),
        // BT24-043 "Add 1 Digimon card with the [Shaman] trait or with [Beast], [Animal] or [Sovereign] in any of its traits (other than [Sea Animal]), in any of its traits and 1 card with the [TS] trait ~"
        //  — 겹쳐 적힌 ", in any of its traits"를 빼고 "1 X 카드 또는 1 Y 카드" (둘 중 하나에 맞는 디지몬 카드)
        s => Regex.Replace(s, @"\bAdd 1 Digimon card with the (?<a>\[[^\]]+\]) trait or with (?<b>\[[^\]]+\](?:, \[[^\]]+\])*,? or \[[^\]]+\]) in any of its traits (?<ex>\(other than \[[^\]]+\]\)), in any of its traits and ",
            "Add 1 Digimon card with the ${a} trait or 1 Digimon card with ${b} in any of its traits ${ex} and ", RegexOptions.IgnoreCase),
        // BT25-074 "You may play 1 play cost 12 or lower [D-Brigade] or [ACCEL] trait Digimon card among them with the cost reduced by 5." — 공개한 디지몬 카드를 코스트를 줄여 등장 (v0.49.0 "play or use ~ with the cost reduced by"와 같은 처리 — 고르기가 디지몬 카드뿐)
        s => Regex.Replace(s, @"(?<=\bReveal the top \d+ cards of your deck\. )You may play (?<p>1 [^.]+? Digimon card) among them with the (?:play )?cost reduced by (?<r>\d+)\.",
            "You may play or use ${p} among them with the cost reduced by ${r}.", RegexOptions.IgnoreCase),
        // P-097 "By placing this card under ~, reveal the top 3 cards of your deck. Place those cards at either the top or bottom of your deck in any order." — 공개한 카드를 덱 위 또는 아래로
        s => Regex.Replace(s, @"\b(?<pre>By placing this card under [^.]+? as its bottom digivolution card), reveal the top (?<n>\d+) cards of your deck\. Place those cards at either the top or bottom of your deck in any order\.",
            "${pre}, reveal the top ${n} cards of your deck, and return them to the top or bottom of the deck.", RegexOptions.IgnoreCase),
        // EX2-049 "You may suspend this Digimon to Reveal the top 5 cards of your deck. Place 1 [ADR-02 Searcher] among them under 1 of your [Mother D-Reaper]s as its bottom digivolution card."
        //  — 「마더 디・리퍼」는 디지타마 (육성 에어리어)도 — 그 이름의 자신의 디지몬 1마리의 진화원 맨 아래에 (공식 한글 카드 문장 기준)
        s => Regex.Replace(s, @"\bYou may suspend this Digimon to Reveal the top (?<n>\d+) cards of your deck\. Place (?<p>1 \[[^\]]+\]) among them under 1 of your \[(?<t>[^\]]+)\]s as its bottom digivolution card\. Place the remaining cards at the bottom of your deck in any order\.?$",
            m => "By suspending this Digimon, " + RevealOrMark66(new RevealOr66(int.Parse(m.Groups["n"].Value), "host:" + m.Groups["t"].Value, m.Groups["p"].Value, "none", "", "bottom", false)) + ".", RegexOptions.IgnoreCase),
        // EX6-025 "Add 1 of each [Gokuumon], [Sagomon], [Cho-Hakkaimon] and [Shakamon] among them to the hand." — 1장씩
        s => Regex.Replace(s, @"\bAdd 1 of each (?<x>\[[^\]]+\](?:, \[[^\]]+\])*,? and \[[^\]]+\]) among them to (?<h>the|your) hand\.",
            m => "Add " + string.Join(" and ", Regex.Matches(m.Groups["x"].Value, @"\[[^\]]+\]").Select(x => "1 " + x.Value)) + $" among them to {m.Groups["h"].Value} hand.", RegexOptions.IgnoreCase),
        // P-021 "If you have [Mimi Tachikawa] in play, you may play a [Palmon] from your hand without paying its memory cost to return 1 of your [Mimi Tachikawa] cards to its owner's hand."
        //  — 등장시키는 것으로 되돌린다 (공식 한글 카드 문장 기준) — 등장시켰을 때만
        s => Regex.Replace(s, @"\bIf you have (?<t>\[[^\]]+\]) in play, you may play an? (?<p>\[[^\]]+\]) from your hand without paying its memory cost to return 1 of your (?<t2>\[[^\]]+\]) cards to its owner's hand\.?$",
            "If you have ${t} in play, ⟦playret66:${p}|${t2}⟧.", RegexOptions.IgnoreCase),
        s => RevealPlayOrAddRe66.Replace(s, m => RevealOrMark66(new RevealOr66(int.Parse(m.Groups["n"].Value), "play", m.Groups["a"].Value, "add", m.Groups["b"].Value, RestOf66(m.Groups["rest"].Value), false)) + "."),
        s => RevealEvoElseAddRe66.Replace(s, m => RevealOrMark66(new RevealOr66(int.Parse(m.Groups["n"].Value), "evo", m.Groups["a"].Value, "add", m.Groups["b"].Value, RestOf66(m.Groups["rest"].Value), false)) + "."),
        s => RevealAddOrPlaceRe66.Replace(s, m => RevealOrMark66(new RevealOr66(int.Parse(m.Groups["n"].Value), "add", m.Groups["a"].Value, "place", m.Groups["b"].Value, RestOf66(m.Groups["rest"].Value), true)) + "."),
        s => RecolorRe66.Replace(s, "⟦recolor66⟧."),
        // P-156 "Choose 1 Tamer. You may play 1 Digimon card with the same color as the chosen Tamer with a play cost of 3 or less from your hand or trash without paying the cost."
        //  — 어느 쪽의 테이머든 1명 (공식 한글 카드 문장 기준) · 같은 색 = 고른 테이머의 색 중 하나라도 가진 카드
        s => Regex.Replace(s, @"\bChoose 1 Tamer\. You may play 1 Digimon card with the same color as the chosen Tamer with a play cost of (?<n>\d+) or less from your hand or trash without paying the cost\.?$", "⟦tamercolorplay66:${n}⟧.", RegexOptions.IgnoreCase),
        // BT13-046 "~, gain 3 memory, and reveal 1 card from your hand. If you revealed a yellow card, place it on top of your security stack face down. If not, return it to your hand."
        s => Regex.Replace(s, @", gain (?<g>\d+) memory, and reveal 1 card from your hand\. If you revealed an? (?<col>red|blue|yellow|green|black|purple|white) card, place it on top of your security stack face down\. If not, return it to your hand\.?$", ", ⟦gainrevsec66:${g}:${col}⟧.", RegexOptions.IgnoreCase),
    };

    internal static string ClauseRewrite66(string rawClause)
    {
        string s = rawClause;
        foreach (var f in ClauseRules66) s = f(s);
        return s;
    }

    /// <summary>EX2-039 "it wasn't trashed by [EX2-039 Impmon]'s effect" — 이 카드를 파기한 효과가 그 카드 번호의 카드에 적힌 효과가 아니다 (효과 밖 · 다른 카드의 효과면 만족)</summary>
    private static Func<EffectContext, bool>? ParseEventCondition66(string cond)
    {
        Match m;
        if ((m = Regex.Match(cond.Trim(), @"^it (?<neg>wasn't|was not|was) trashed by \[(?<id>[A-Z]+\d*-\d+)(?: [^\]]+)?\]'s effects?$", RegexOptions.IgnoreCase)).Success)
        {
            string id = m.Groups["id"].Value;
            bool neg = !m.Groups["neg"].Value.Equals("was", StringComparison.OrdinalIgnoreCase);
            return c =>
            {
                bool by = c.Engine.ThisTrashedByCard66 is { } x && SameCardNo66(x.CardId, id);
                Diag46.Hit(by ? "trashedByCard66" : "trashedByOther66");
                return by != neg;
            };
        }
        return null;
    }

    /// <summary>카드 번호가 같은가 ("EX2-039" · "EX2-039_P1" 같은 다른 그림도)</summary>
    internal static bool SameCardNo66(string cardId, string no) =>
        cardId.Equals(no, StringComparison.OrdinalIgnoreCase)
        || cardId.StartsWith(no, StringComparison.OrdinalIgnoreCase) && cardId.Length > no.Length && !char.IsDigit(cardId[no.Length]);

    /// <summary>이 묶음의 동작 (원래 규칙이 읽지 못한 문장만 — ParseStep에서 앞의 묶음 다음)</summary>
    private static (Action<EffectContext>? Act, string? Error)? ParseAction66(string s, CardData card, List<EffectTrigger> triggers)
    {
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        s = Regex.Replace(s.Trim(), @"^Then,?\s+", "", RegexOptions.IgnoreCase);
        Match m;
        if ((m = Regex.Match(s, @"^⟦rvor66:(?<id>\d+)⟧$")).Success)
            return RevealOrAction66(int.Parse(m.Groups["id"].Value), label, card, triggers);
        if ((m = Regex.Match(s, @"^⟦playret66:\[(?<p>[^\]]+)\]\|\[(?<t>[^\]]+)\]⟧$")).Success)
            return PlayThenReturnTamer66(m.Groups["p"].Value, m.Groups["t"].Value, label);
        if (s == "⟦recolor66⟧")
            return (c => RecolorAction66(c, label), null);
        if ((m = Regex.Match(s, @"^⟦tamercolorplay66:(?<n>\d+)⟧$")).Success)
        {
            int max = int.Parse(m.Groups["n"].Value);
            return (c => TamerColorPlay66(c, label, max), null);
        }
        if ((m = Regex.Match(s, @"^(?:gain|Gain )?⟦gainrevsec66:(?<g>\d+):(?<col>[a-z]+)⟧$", RegexOptions.IgnoreCase)).Success)
        {
            int g = int.Parse(m.Groups["g"].Value);
            var col = Enum.Parse<CardColor>(m.Groups["col"].Value, ignoreCase: true);
            return (c => GainRevealToSecurity66(c, label, g, col), null);
        }
        return null;
    }

    /// <summary>P-156 테이머 1명(어느 쪽이든)을 고르고, 자신의 패 · 트래시에서 그 테이머와 같은 색의 등장 코스트 max 이하 디지몬 카드 1장을 코스트 없이 등장시킬 수 있다</summary>
    private static void TamerColorPlay66(EffectContext c, string label, int max)
    {
        var tamers = c.Owner.Tamers.Distinct().Select(t => (Z: c.Owner, T: t)).Concat(c.Enemy.Tamers.Distinct().Select(t => (Z: c.Enemy, T: t))).ToList();
        if (tamers.Count == 0) { c.LastStepResolved = false; Emit(c, label, "고를 테이머가 없습니다"); return; }
        List<(CardData Card, List<CardData> From)> Pool(IReadOnlyList<CardColor> cols) =>
            c.Owner.Hand.Select(x => (x, c.Owner.Hand)).Concat(c.Owner.Trash.Select(x => (x, c.Owner.Trash)))
                .Where(p => p.x.Type == CardType.Digimon && p.x.PlayCost <= max && p.x.AllColors.Any(cols.Contains)
                    && c.Engine.CanPlayByEffect(c.Owner, p.x, ReferenceEquals(p.Item2, c.Owner.Hand) ? PlayOrigin.Hand : PlayOrigin.Trash))
                .Select(p => (p.x, p.Item2)).GroupBy(p => (p.x, p.Item2)).Select(g => g.First()).ToList();
        // 추천: 등장시킬 수 있는 카드 중 가장 좋은 카드(Lv. · DP)가 있는 테이머
        int rec = tamers.Select((x, i) => (i, best: Pool(x.Z.TamerColorsOf66(x.T)).Select(p => p.Card.Level * 100000 + p.Card.DP).DefaultIfEmpty(-1).Max())).OrderByDescending(x => x.best).First().i;
        var labels = tamers.Select(x => $"{(ReferenceEquals(x.Z, c.Owner) ? "자신" : "상대")}의 테이머 [{x.T.Name}] ({string.Join("/", x.Z.TamerColorsOf66(x.T).Select(ColorKo66))})").ToList();
        int pick = Math.Clamp(c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 테이머 1명을 고르세요.", labels, rec), 0, tamers.Count - 1);
        var cols = tamers[pick].Z.TamerColorsOf66(tamers[pick].T);
        var pool = Pool(cols).OrderByDescending(p => p.Card.Level).ThenByDescending(p => p.Card.DP).ToList();
        Diag46.Hit("tamerColorPlay66");
        if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, $"테이머 [{tamers[pick].T.Name}]와 같은 색의 등장시킬 카드가 없습니다"); return; }
        var chosen = c.Engine.ChooseCards(c.Owner, CardChoiceZone.HandOrTrash, pool.Select(p => p.Card).ToList(), 1, upTo: true, $"[{c.Card.Name}] 코스트 없이 등장시킬 카드를 고르세요 (테이머 [{tamers[pick].T.Name}]와 같은 색 · 등장 코스트 {max} 이하).").FirstOrDefault();
        if (chosen == null) { c.LastStepResolved = false; return; }
        var from = pool.First(p => ReferenceEquals(p.Card, chosen)).From;
        if (c.Owner.BattleArea.Count >= 12 || !c.Engine.TryPlayByEffect(c.Owner, chosen, ReferenceEquals(from, c.Owner.Hand) ? PlayOrigin.Hand : PlayOrigin.Trash, () => from.Remove(chosen)))
        { c.LastStepResolved = false; Emit(c, label, $"[{chosen.Name}]을(를) 등장시킬 수 없습니다"); return; }
        Emit(c, label, $"테이머 [{tamers[pick].T.Name}] 선택 → [{chosen.Name}] 코스트 없이 등장");
    }

    /// <summary>
    /// BT13-046 메모리 +g 하고, 자신의 패 1장을 공개한다. 그 색의 카드라면 시큐리티 맨 위에 뒷면으로 (아니면 패로 그대로).
    /// 공개할 카드는 주인이 고른다 — 추천: 그 색의 카드 중 Lv.가 가장 낮은 카드 (시큐리티에 놓기 위해)
    /// </summary>
    private static void GainRevealToSecurity66(EffectContext c, string label, int g, CardColor col)
    {
        c.AddMemory(g);
        if (c.Owner.Hand.Count == 0) { c.LastStepResolved = false; Emit(c, label, $"메모리 +{g} · 공개할 패가 없습니다"); return; }
        var ranked = c.Owner.Hand.Distinct().OrderBy(x => x.AllColors.Contains(col) ? 0 : 1).ThenBy(x => x.Level).ThenBy(x => x.PlayCost).ToList();
        var x = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, ranked, 1, upTo: false, $"[{c.Card.Name}] 공개할 패 1장을 고르세요 ({ColorKo66(col)} 카드면 시큐리티 맨 위에).").FirstOrDefault() ?? ranked[0];
        Diag46.Hit("handRevealToSecurity66");
        if (!x.AllColors.Contains(col)) { Emit(c, label, $"메모리 +{g} · 패의 [{x.Name}] 공개 → {ColorKo66(col)}이 아니어서 패로"); return; }
        if (c.Engine.SecurityAddBlocked65(c.Owner)) { c.LastStepResolved = false; Emit(c, label, $"메모리 +{g} · 패의 [{x.Name}] 공개 → 시큐리티에 카드를 추가할 수 없어 패로"); return; }   // v0.65.0 BT9-103
        c.Owner.Hand.Remove(x);
        c.Owner.SecurityStack.Insert(0, x);
        c.Engine.NotifySecurityAdded(c.Owner, x);
        Emit(c, label, $"메모리 +{g} · 패의 [{x.Name}] 공개 → 시큐리티 맨 위에");
    }

    /// <summary>
    /// P-021 "you may play a [Palmon] from your hand without paying its memory cost to return 1 of your [Mimi Tachikawa] cards to its owner's hand" —
    /// 패의 그 카드를 코스트 없이 등장시키는 것으로 (등장시켰을 때만) 자신의 그 테이머 1명을 패로 되돌린다 (공식 한글 카드 문장 기준)
    /// </summary>
    private static (Action<EffectContext>? Act, string? Error) PlayThenReturnTamer66(string playName, string tamerName, string label) =>
        (c =>
        {
            var x = c.Owner.Hand.FirstOrDefault(h => CardNames.Is(h, playName) && h.Type == CardType.Digimon && c.Engine.CanPlayByEffect(c.Owner, h, PlayOrigin.Hand));
            if (x == null || c.Owner.BattleArea.Count >= 12) { c.LastStepResolved = false; Emit(c, label, $"패에 등장시킬 [{playName}]이(가) 없습니다"); return; }
            if (!c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 패의 [{x.Name}]을(를) 코스트 없이 등장시키고 [{tamerName}] 1명을 패로 되돌릴까요?", true)) { c.LastStepResolved = false; return; }
            if (!c.Engine.TryPlayByEffect(c.Owner, x, PlayOrigin.Hand, () => c.Owner.Hand.Remove(x))) { c.LastStepResolved = false; Emit(c, label, $"[{x.Name}]을(를) 등장시킬 수 없습니다"); return; }
            Diag46.Hit("playReturnTamer66");
            var t = c.Owner.Tamers.FirstOrDefault(y => CardNames.Is(y, tamerName));
            bool back = t != null && c.Engine.ReturnTamerToHand(c.Owner, t);
            Emit(c, label, $"[{x.Name}] 코스트 없이 등장{(back ? $" → 테이머 [{t!.Name}] 패로" : "")}");
        }, null);

    private static (Action<EffectContext>? Act, string? Error) RevealOrAction66(int id, string label, CardData card, List<EffectTrigger> triggers)
    {
        if (RevealOrSpec66(id) is not { } spec) return (null, "미지원 공개 (표시 없음)");
        var pa = ParseCardPick(spec.PickA) ?? ParseNamePick(spec.PickA);
        var pb = spec.VerbB == "none" ? (0, (Func<CardData, bool>)(_ => false)) : ParseCardPick(spec.PickB) ?? ParseNamePick(spec.PickB);
        if (pa == null) return (null, $"미지원 공개 조건 ({spec.PickA})");
        if (pb == null) return (null, $"미지원 공개 조건 ({spec.PickB})");
        string? hostName = spec.VerbA.StartsWith("host:", StringComparison.Ordinal) ? spec.VerbA[5..] : null;
        // EX2-049 "under 1 of your [Mother D-Reaper]s" — 그 이름의 자신의 디지몬 (배틀 에어리어 또는 육성 에어리어 — 「마더 디・리퍼」는 디지타마)
        List<CardInstance> Hosts(EffectContext c) => hostName == null ? new List<CardInstance>()
            : c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && CardNames.Is(d.TopCard, hostName)).Concat(c.Owner.BreedingArea is { } br && CardNames.Is(br.TopCard, hostName) ? new[] { br } : Array.Empty<CardInstance>()).ToList();
        if ((spec.VerbA == "place" || spec.VerbB == "place") && card.Type != CardType.Digimon) return (null, "미지원: 디지몬이 아닌 카드의 진화원 놓기");
        var matchA = RevealAlias66.Wrap(pa.Value.Match);
        var matchB = RevealAlias66.Wrap(pb.Value.Match);
        return (c =>
        {
            var deck = c.Owner.MainDeck;
            var revealed = deck.Take(spec.N).ToList();
            deck.RemoveRange(0, revealed.Count);
            Diag46.Hit("revealOr66");
            bool Can(string verb, CardData x) => verb switch
            {
                "play" => x.Type is CardType.Digimon or CardType.Tamer && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Revealed) && c.Owner.BattleArea.Count < 12,
                "evo" => x.Type == CardType.Digimon && RevealEvoTargets32(c, true, x).Count > 0,
                "place" => c.Self != null && c.Owner.BattleArea.Contains(c.Self),
                "none" => false,
                _ when hostName != null && verb == spec.VerbA => true,   // 놓을 곳이 없어도 고른다 (그 카드는 파기 — SentenceParser.ParseReveal의 Place와 같다)
                _ => true,
            };
            var poolA = revealed.Where(x => matchA(x) && Can(spec.VerbA, x)).Distinct().ToList();
            var poolB = revealed.Where(x => matchB(x) && Can(spec.VerbB, x)).Distinct().ToList();
            string What(string verb) => verb switch { "play" => "코스트 없이 등장", "evo" => "코스트 없이 진화", "place" => "이 디지몬의 진화원 맨 아래에", _ when hostName != null && verb == spec.VerbA => $"[{hostName}]의 진화원 맨 아래에", _ => "패에 추가" };
            // 고른다: A · B 중 할 수 있는 쪽 (둘 다면 묻는다 — 추천 A: 등장 · 진화가 먼저 적힌 효과, BT25-078은 패에 추가)
            string? verb = null;
            if (poolA.Count > 0 && poolB.Count > 0)
            {
                var opts = new List<string> { $"{What(spec.VerbA)} ({poolA.Count}장 중 1장)", $"{What(spec.VerbB)} ({poolB.Count}장 중 1장)" };
                verb = Math.Clamp(c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 공개한 카드 [{string.Join(", ", revealed.Select(x => x.Name))}]로 무엇을 할까요?", opts, RevealOrRecommend66(c, spec, poolA, poolB)), 0, 1) == 0 ? spec.VerbA : spec.VerbB;
            }
            else if (poolA.Count > 0) verb = spec.VerbA;
            else if (poolB.Count > 0 && !(spec.MayB && !c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 공개한 카드를 {What(spec.VerbB)} 놓을까요?", true))) verb = spec.VerbB;
            CardData? taken = null;
            if (verb != null)
            {
                // 추천: 높은 Lv. · DP부터 — 패에 넣는 쪽은 다른 쪽(등장 · 진화)으로도 쓸 수 있는 카드를 뒤로 (그 카드는 그쪽을 고르면 된다)
                var other = verb == spec.VerbA ? matchB : matchA;
                var pool = (verb == spec.VerbA ? poolA : poolB).OrderBy(x => verb == "add" && other(x) ? 1 : 0).ThenByDescending(x => x.Level).ThenByDescending(x => x.DP).ToList();
                taken = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, pool, 1, upTo: false, $"[{c.Card.Name}] 공개한 카드 중 {What(verb)}할 카드를 고르세요.").FirstOrDefault();
            }
            if (taken != null) revealed.Remove(taken);
            // 나머지를 먼저 보낸다 (공개 효과의 처리 순서 — 등장 · 진화의 [등장 시] · [진화 시]가 덱을 볼 수 있게, SentenceParser.ParseReveal과 같다)
            if (spec.Rest == "trash") c.Owner.Trash.AddRange(revealed);
            else deck.AddRange(revealed);
            string restText = revealed.Count == 0 ? "" : spec.Rest == "trash" ? $" · 나머지 {revealed.Count}장 파기" : $" · 나머지 {revealed.Count}장 덱 아래로";
            if (taken == null)
            {
                c.LastStepResolved = false;
                Emit(c, label, $"덱 위 {revealed.Count}장 공개 → 고를 카드가 없습니다{restText}");
                return;
            }
            string done;
            switch (verb)
            {
                case "play":
                    if (c.Engine.TryPlayByEffect(c.Owner, taken, PlayOrigin.Revealed, null)) done = $"[{taken.Name}] 코스트 없이 등장";
                    else { if (spec.Rest == "trash") c.Owner.Trash.Add(taken); else deck.Add(taken); done = $"[{taken.Name}] 등장시킬 수 없음"; }
                    break;
                case "evo":
                    {
                        var bases = RevealEvoTargets32(c, true, taken);
                        var d = bases.Count <= 1 ? bases.FirstOrDefault() : c.Engine.ChooseTargets(c.Owner, bases, 1, $"[{c.Card.Name}] [{taken.Name}]으로 진화할 디지몬을 고르세요.", optional: false, preselected: bases.OrderByDescending(x => x.TopCard.Level).ThenByDescending(x => x.EvolutionSources.Count).First()).FirstOrDefault() ?? bases[0];
                        var from = new List<CardData> { taken };
                        if (d != null) c.Engine.EvolveByEffect(c.Owner, d, taken, 0, from);
                        if (from.Count > 0) { from.Clear(); if (spec.Rest == "trash") c.Owner.Trash.Add(taken); else deck.Add(taken); done = $"[{taken.Name}]으로 진화할 수 없음"; }
                        else done = $"[{taken.Name}]으로 진화";
                        break;
                    }
                case "place":
                    if (c.Self != null && c.Owner.BattleArea.Contains(c.Self)) { c.Self.AddBottomSource(taken); c.RecalculateFieldBuffs(); done = $"[{taken.Name}] 이 디지몬의 진화원 맨 아래에"; }
                    else { c.Owner.Trash.Add(taken); done = $"[{taken.Name}] 놓을 곳이 없어 파기"; }
                    break;
                case { } hv when hostName != null && hv == spec.VerbA:
                    {
                        var hosts = Hosts(c);
                        var h = hosts.Count <= 1 ? hosts.FirstOrDefault() : c.Engine.ChooseTargets(c.Owner, hosts, 1, $"[{c.Card.Name}] [{taken.Name}]을(를) 진화원 아래에 놓을 디지몬을 고르세요.", optional: false, preselected: hosts[0]).FirstOrDefault() ?? hosts[0];
                        if (h != null) { h.AddBottomSource(taken); c.RecalculateFieldBuffs(); done = $"[{taken.Name}] [{h.TopCard.Name}]의 진화원 맨 아래에"; Diag46.Hit("revealPlaceHost66"); }
                        else { c.Owner.Trash.Add(taken); done = $"[{taken.Name}] 놓을 곳이 없어 파기"; }
                        break;
                    }
                default:
                    c.Owner.Hand.Add(taken);
                    done = $"[{taken.Name}] 패에 추가";
                    break;
            }
            c.LastCard = taken;
            Emit(c, label, $"덱 위 {spec.N}장 공개 → {done}{restText}");
        }, null);
    }

    /// <summary>A · B 둘 다 할 수 있을 때 추천 (0 = A): 등장 · 진화는 Lv.가 가장 높은 카드가 B의 것 이상이면 A. 패에 추가 · 진화원은 패가 4장 이하면 패</summary>
    private static int RevealOrRecommend66(EffectContext c, RevealOr66 spec, List<CardData> poolA, List<CardData> poolB)
    {
        if (spec.VerbA is "play" or "evo") return 0;
        if (spec.VerbA == "add" && spec.VerbB == "place") return c.Owner.Hand.Count <= 4 ? 0 : 1;
        return 0;
    }

    // ---- AI 추천: 패의 카드를 진화원 아래에 놓는 비용 ----

    private static readonly Regex HandSrcCostRe66 = new(@"^placing (?<pick>.+?) (?:in|from) (?:your hand(?: or (?:your )?trash)?|your trash or (?:your )?hand)(?: face down)? (?:as (?:this Digimon's|its) bottom digivolution cards?|at the bottom of (?:this Digimon's|its) digivolution cards|under this Digimon as its bottom digivolution cards?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// v0.66.0 패의 카드를 이 디지몬의 진화원 아래에 놓는 비용 (패가 놓을 매수 + 1장 이하일 때): 전의 "Lv.6 이상 · 진화원 4장 이상" 대신 뒤의 효과의 값으로 정한다 (RestValue66).
    /// 패가 넉넉하면 (놓을 매수 + 2장 이상) 지금처럼 낸다
    /// </summary>
    private static Func<EffectContext, bool> CostForRest66(string costText, string rest, Func<EffectContext, bool> recommend)
    {
        if (!HandSrcCostRe66.IsMatch(costText.Trim().TrimEnd('.'))) return recommend;
        var value = RestValue66(rest);
        return c =>
        {
            var outer = c.HandCostValue66;
            c.HandCostValue66 = value;
            try { return recommend(c); }
            finally { c.HandCostValue66 = outer; }
        };
    }

    /// <summary>
    /// 패 1장(진화원이 되어 진화원 효과는 남는다)과 바꿀 만한 효과인지:
    ///  - 상대의 디지몬을 소멸 · 되돌리기 · ≪퇴화≫ · 레스트 · 진화원 파기 · 행동 제한 — 받을 상대의 디지몬이 있을 때
    ///  - ≪드로우≫ (패의 수가 그대로 · 진화원이 는다) · 등장 · 효과 발휘
    ///  - ≪리커버리≫ — 시큐리티가 3장 이하일 때
    ///  - 이 디지몬을 지키는 효과 — 지킬 디지몬의 값 (쌓인 카드 수와 Lv.-3 중 큰 쪽 — 잃을 카드 수 · 진화 단계)이 3 이상일 때 (Lv.6이면 1장짜리도)
    ///  - 그 밖의 DP · 키워드 · 메모리 — 내지 않는다 (패가 넉넉할 때만)
    /// </summary>
    internal static Func<EffectContext, bool> RestValue66(string rest)
    {
        string r = rest.Trim();
        if (Regex.IsMatch(r, @"\b(?:delete|return)\b[^.]*\bopponent's\b|＜De-Digivolve \d+＞|\bsuspend (?:1|up to \d+) of (?:your opponent's|their)\b|\btrash [^.]*digivolution cards? (?:of|from) [^.]*opponent's|\bopponent's Digimon(?: or Tamers)? can't\b|\bgive 1 of your opponent's Digimon\b", RegexOptions.IgnoreCase))
            return c => c.Enemy.BattleArea.Any(d => d.TamerProxyOf == null && !c.Engine.IsUnaffectedBy(c.Owner, d)) && Diag46.HitTrue("handCostRemoval66");
        if (Regex.IsMatch(r, @"^＜Draw \d+＞", RegexOptions.IgnoreCase) || Regex.IsMatch(r, @"\b(?:play|activate)\b", RegexOptions.IgnoreCase))
            return _ => Diag46.HitTrue("handCostGain66");
        if (Regex.IsMatch(r, @"＜Recovery \+\d+", RegexOptions.IgnoreCase))
            return c => c.Owner.SecurityStack.Count <= 3 && Diag46.HitTrue("handCostRecovery66");
        if (Regex.IsMatch(r, @"\b(?:prevent effects from deleting|can't be deleted|isn't affected|aren't affected|doesn't leave|can't leave|can't return)\b", RegexOptions.IgnoreCase))
            return c => c.Self is { } d && c.Owner.BattleArea.Contains(d) && Math.Max(1 + d.EvolutionSources.Count, d.TopCard.Level - 3) >= 3 && Diag46.HitTrue("handCostProtect66");
        return _ => false;
    }

    // ---- BT18-078 상대의 디지몬 / 테이머 1마리(명)의 원래 색을 화이트 이외의 1색으로 ----

    private static readonly CardColor[] NonWhite66 = { CardColor.Red, CardColor.Blue, CardColor.Yellow, CardColor.Green, CardColor.Black, CardColor.Purple };

    private static void RecolorAction66(EffectContext c, string label)
    {
        int until = Until(c, Duration.OpponentsTurnEnd);
        var digimon = c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null && !c.Engine.IsUnaffectedBy(c.Owner, d)).ToList();
        var tamers = c.Enemy.Tamers.Distinct().ToList();
        if (digimon.Count + tamers.Count == 0) { c.LastStepResolved = false; Emit(c, label, "대상이 없습니다"); return; }
        // 추천: DP가 가장 높은 상대의 디지몬 (없으면 첫 테이머)
        var labels = digimon.Select(d => $"디지몬 [{d.TopCard.Name}] (DP {d.GetCurrentDP():N0})").Concat(tamers.Select(t => $"테이머 [{t.Name}]")).ToList();
        int rec = digimon.Count > 0 ? digimon.Select((d, i) => (d, i)).OrderByDescending(x => x.d.GetCurrentDP()).First().i : 0;
        int pick = Math.Clamp(c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 원래 색을 바꿀 상대의 디지몬 또는 테이머를 고르세요.", labels, rec), 0, labels.Count - 1);
        IReadOnlyList<CardColor> before = pick < digimon.Count ? digimon[pick].TopCard.AllColors : tamers[pick - digimon.Count].AllColors;
        // 색 추천: 지금 색이 아닌 색 중 상대의 필드 · 패에 가장 적은 색 (상대의 색 조건을 깨기 쉽게)
        var colorRank = NonWhite66.OrderBy(col => before.Contains(col) ? 1 : 0)
            .ThenBy(col => c.Enemy.BattleArea.Count(d => d.TopCard.AllColors.Contains(col)) + c.Enemy.Tamers.Count(t => t.AllColors.Contains(col)) + c.Enemy.Hand.Count(x => x.AllColors.Contains(col)))
            .ToList();
        int recCol = Array.IndexOf(NonWhite66, colorRank[0]);
        int colIdx = Math.Clamp(c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 어느 색으로 바꿀까요? (화이트 이외의 1색)", NonWhite66.Select(ColorKo66).ToList(), recCol), 0, NonWhite66.Length - 1);
        var color = NonWhite66[colIdx];
        Diag46.Hit("recolor66");
        if (pick < digimon.Count)
        {
            var d = digimon[pick];
            d.BaseColor66 = (color, until);
            c.LastTargets = new List<CardInstance> { d };
            c.RecalculateFieldBuffs();
            Emit(c, label, $"상대의 [{d.TopCard.Name}]의 원래 색을 {ColorTo66(color)} (상대의 턴 종료까지)");
            return;
        }
        var t = tamers[pick - digimon.Count];
        c.Enemy.TamerColors66.RemoveAll(x => ReferenceEquals(x.Tamer, t));
        c.Enemy.TamerColors66.Add((t, color, until));
        c.RecalculateFieldBuffs();
        Emit(c, label, $"상대의 테이머 [{t.Name}]의 원래 색을 {ColorTo66(color)} (상대의 턴 종료까지)");
    }

    /// <summary>"~으로/로" — 그린 · 블랙은 받침이 있다</summary>
    private static string ColorTo66(CardColor col) => ColorKo66(col) + (col is CardColor.Green or CardColor.Black ? "으로" : "로");

    internal static string ColorKo66(CardColor col) => col switch
    {
        CardColor.Red => "레드", CardColor.Blue => "블루", CardColor.Yellow => "옐로", CardColor.Green => "그린",
        CardColor.Black => "블랙", CardColor.Purple => "퍼플", CardColor.White => "화이트", _ => col.ToString(),
    };
}

/// <summary>
/// v0.66.0 BT15-060 "While this card is revealed from the deck, its name is also treated as [Omnimon]." · BT17-068 "~ this card is also treated as level 6." —
/// 덱에서 공개한 카드를 고를 때 (공개 효과의 "그중 ~") 그 카드를 원래 모습과 함께 그 명칭 · Lv.의 카드로도 본다 (둘 중 하나라도 맞으면 고를 수 있다).
/// 공개한 카드가 패 · 필드 등으로 간 뒤에는 원래 카드 그대로 ("오픈되어 있는 동안")
/// </summary>
internal static class RevealAlias66
{
    internal static readonly Regex ClauseRe = new(@"^While this card is revealed from (?:the|your) deck, (?:its name is also treated as \[(?<x>[^\]]+)\]|this card is also treated as level (?<lv>\d+))\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InTextRe = new(@"While this card is revealed from (?:the|your) deck, (?:its name is also treated as \[(?<x>[^\]]+)\]|this card is also treated as level (?<lv>\d+))", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private sealed class Box { public CardData? Alias; }
    private static readonly ConditionalWeakTable<CardData, Box> Cache = new();

    /// <summary>공개되어 있는 동안의 다른 모습 (없으면 null) — 명칭 · Lv.만 바꾼 복사본</summary>
    internal static CardData? Alias(CardData card)
    {
        if (card.EffectText.IndexOf("revealed from", StringComparison.OrdinalIgnoreCase) < 0) return null;
        return Cache.GetValue(card, Make).Alias;
    }

    private static Box Make(CardData card)
    {
        var m = InTextRe.Match(card.EffectText);
        if (!m.Success) return new Box();
        var a = card.CloneShallow66();
        if (m.Groups["x"].Success) { a.Name = m.Groups["x"].Value; a.EnglishName = null; }
        if (m.Groups["lv"].Success) a.Level = int.Parse(m.Groups["lv"].Value);
        return new Box { Alias = a };
    }

    /// <summary>공개한 카드를 고르는 조건 — 원래 카드 또는 공개 중의 모습이 맞으면</summary>
    internal static Func<CardData, bool> Wrap(Func<CardData, bool> match) => x =>
    {
        if (match(x)) return true;
        if (Alias(x) is not { } a || !match(a)) return false;
        Diag46.Hit("revealAlias66");
        return true;
    };
}
