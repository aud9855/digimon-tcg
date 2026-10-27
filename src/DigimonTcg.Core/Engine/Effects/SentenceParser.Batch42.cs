using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.42.0 미지원 문장 16차 묶음: 조건이 붙은 진화 조건 무시 진화 (지속 효과) · 효과로 진화 조건 무시 진화의 다른 표기 · 셈 · 겹쳐진 카드 보호 ·
/// 시큐리티 표기 · [육성] 시점 · "대신에" 등.
/// 원래 규칙(1~15차)이 읽지 못한 절 · 문장 · 동작만 16차 규칙으로 다시 읽는다 (엔진: GoldfishEngine.Batch42.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _clauseDepth42;
    [ThreadStatic] private static int _fallbackDepth42;

    // =====================================================================
    // 1. 절 — 원래 규칙이 읽지 못한 절만 (TryParse)
    // =====================================================================

    private static string? TryParse42(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        // 영어 데이터의 진화원 칸이 "Card Effect(s) ~"로 시작하는 메인 칸의 복사본 (BT6-084 · BT7-083 · BT23-077 · ST12-13 「시스터몬」 — 공식 진화원 효과 없음): 진화원 효과로 등록하지 않는다
        if (inherited && InheritedIsMainCopy42(card)) return null;
        string? err = TryParse41(rawClause, inherited, card, def);
        if (err == null || _clauseDepth42 > 0) return err;
        _clauseDepth42++;
        try
        {
            if (inherited && BrokenDuplicate42(rawClause, card)) return null;   // 영어 데이터의 깨진 중복 문장 (EX13-001 · EX13-003 · EX13-004 · EX13-005) — 메인 칸의 "Inherited Effect ~"가 등록한다
            if (SpecialEvoClause42(rawClause, inherited, card, def)) return null;
            if (BreedingClause42(rawClause, inherited, card, def)) return null;
            if (DeletedCostClause42(rawClause, inherited, card, def)) return null;
            foreach (var alt in ClauseRewrites42(rawClause, card))
            {
                if (alt == rawClause) continue;
                if (TryParse41(alt, inherited, card, null) != null) continue;
                if (def != null) TryParse41(alt, inherited, card, def);
                return null;
            }
            return err;
        }
        finally { _clauseDepth42--; }
    }

    /// <summary>진화원 칸이 "Card Effect(s)"로 시작하는 메인 칸 복사본인지 (영어 데이터의 오류 — 공식 한글 데이터는 진화원 효과 없음)</summary>
    internal static bool InheritedIsMainCopy42(CardData card) =>
        card.Type == CardType.Digimon && (card.InheritedEffectText ?? "").TrimStart().StartsWith("Card Effect(s)", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 영어 데이터의 깨진 중복 문장: 진화원 칸 문장에 이름이 빠졌고 ("a Digimon card with in the hand" · "with or the [Holy Beast] trait" · "1 card with from your hand"),
    /// 메인 칸에 같은 효과의 온전한 "Inherited Effect ~" 문장이 있다 (EX13-001 · EX13-003 · EX13-004 · EX13-005 — 디지타마). 깨진 문장의 낱말이 온전한 문장 안에 차례대로 모두 있을 때만
    /// </summary>
    internal static bool BrokenDuplicate42(string rawClause, CardData card)
    {
        string broken = rawClause.Trim();
        if (!Regex.IsMatch(broken, @"\bwith (?:in|or|from) (?:the |your )?", I)) return false;
        foreach (Match good in Regex.Matches(card.EffectText ?? "", @"Inherited Effect\s*(?<c>[^\r\n]+)", I))
        {
            var gw = Regex.Split(good.Groups["c"].Value.ToLowerInvariant(), @"[^a-z0-9\[\]]+").Where(w => w.Length > 0).ToList();
            var bw = Regex.Split(broken.ToLowerInvariant(), @"[^a-z0-9\[\]]+").Where(w => w.Length > 0).ToList();
            int i = 0;
            foreach (var w in gw) if (i < bw.Count && bw[i] == w) i++;
            if (i == bw.Count && bw.Count >= 8 && gw.Count - bw.Count <= 10) return true;
        }
        return false;
    }

    /// <summary>절 전체의 같은 뜻 다른 표기 (렌더러도 쓴다) — 원래 규칙으로 읽혀야 쓴다</summary>
    internal static IEnumerable<string> ClauseRewrites42(string rawClause, CardData card)
    {
        string c = rawClause.Trim();
        var lead = Regex.Match(c, @"^(?<labels>(?:Inherited Effect\s*|Security Effect\s*)?(?:\[[^\]]+\]\s*)*)(?<body>.*)$", RegexOptions.Singleline);
        string labels = lead.Groups["labels"].Value, body = lead.Groups["body"].Value.Trim();
        var sentences = Regex.Split(body, @"(?<=\.)\s+(?=[A-Z＜])");
        bool changed = false;
        var outs = new List<string>();
        foreach (var s in sentences)
        {
            string t = s.Trim();
            string end = t.EndsWith(".") ? "." : "";
            string core = t.TrimEnd('.');
            string? alt = Rewrites42(core).LastOrDefault();
            if (alt != null && alt != core) { changed = true; outs.Add(alt + end); }
            else outs.Add(t);
        }
        if (changed) yield return labels + string.Join(" ", outs);
    }

    // =====================================================================
    // 2. 문장 — 원래 규칙이 읽지 못한 문장만 (ParseStep 끝)
    // =====================================================================

    private static (Step? Step, string? Error)? StepFallback42(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth42 >= 3) return null;
        _fallbackDepth42++;
        try
        {
            if (InsteadStep42(sentence, card, triggers) is { } inst) return (inst, null);
            if (ReduceStep42(sentence, card, triggers) is { } red) return (red, null);
            string s = Regex.Replace(sentence.Trim().TrimEnd('.').Trim(), @"^Then,?\s+", "", I);
            foreach (var alt in Rewrites42(s))
            {
                if (alt == s) continue;
                var r = ParseStep(alt, card, triggers);
                if (r.Error == null && r.Step != null) return (r.Step with { Text = sentence.Trim() }, null);
            }
            return null;
        }
        finally { _fallbackDepth42--; }
    }

    /// <summary>같은 뜻의 다른 표기 (렌더러도 쓴다). 여러 규칙을 차례로 겹쳐 적용한 결과도 낸다</summary>
    internal static IEnumerable<string> Rewrites42(string s)
    {
        string acc = s;
        foreach (var f in RewriteRules42)
        {
            string next = f(acc);
            if (next != acc)
            {
                yield return next;
                acc = next;
            }
        }
    }

    private static readonly Func<string, string>[] RewriteRules42 =
    {
        // ---- 효과로 진화 조건 무시 진화 ----
        // "You can digivolve your [Agumon] into 1 [Agumon - Bond of Bravery] in your hand for its digivolution cost, ignoring its level" (BT6-087 · BT6-088 — 공식 "자신의 「아구몬」 1마리를 Lv.을 무시하고 진화 코스트를 지불하여 ~로 진화시킨다")
        t => Regex.Replace(t, @"^You (?:can|may) digivolve your (?<x>\[[^\]]+\]) into (?:an? |1 )?(?<rest>.+? in your hand .+)$", "1 of your ${x} may digivolve into ${rest}", I),
        // "you may digivolve it into a [Beelzemon: Blast Mode] in your trash for a digivolution cost of 3" (EX2-065) · "you may digivolve that Digimon into this card ~" (BT13-028)
        t => Regex.Replace(t, @"^(?<pre>(?:If [^,]+, )?)you (?:can|may) digivolve (?<who>it|that Digimon) into (?<rest>.+)$", m =>
            m.Groups["rest"].Value.StartsWith("this card", StringComparison.OrdinalIgnoreCase)
                ? $"{m.Groups["pre"].Value}{m.Groups["who"].Value} digivolves into {m.Groups["rest"].Value}"
                : $"{m.Groups["pre"].Value}{m.Groups["who"].Value} may digivolve into {m.Groups["rest"].Value}", I),
        // "This Digimon can digivolve into ~ in your hand ~" (효과 문장 P-029 · P-030) → "may"
        t => Regex.Replace(t, @"^(?<pre>(?:If [^,]+, )?)(?<who>This Digimon|this Digimon|it) can digivolve into (?<rest>.+? (?:in|from) (?:your|the) (?:hand|trash).*)$", "${pre}${who} may digivolve into ${rest}", I),
        // "1 of your [Agumon] digivolves into this card" · "1 of your [Kudamon] may digivolve into ~" (BT22-013 · BT13-098) → "1 of your [Agumon] Digimon ~"
        t => Regex.Replace(t, @"\b(?<n>1|one) of your (?<x>\[[^\]]+\](?: or \[[^\]]+\])*)s? (?=(?:may )?digivolves?\b)", "1 of your ${x} Digimon ", I),
        // 코스트 표기: "for a cost of 3" · "for a memory cost of 2" · "for the digivolution cost" (P-092 · P-029 · BT13-098)
        t => Regex.Replace(t, @"(?<=\bdigivolves? into .+?) for a (?:memory |digivolution )?cost of (?<n>\d+)", " for a digivolution cost of ${n}", I),
        t => Regex.Replace(t, @"(?<=\bdigivolves? into .+?) for (?:the|its) (?:memory |digivolution )?cost(?=,| ignoring|$)", " for its digivolution cost", I),
        // "ignoring this card's digivolution requirements" (효과 문장) → "ignoring its digivolution requirements"
        t => Regex.Replace(t, @"(?<=\bmay digivolve into .+?), ignoring this card's digivolution requirements", ", ignoring its digivolution requirements", I),
        // "If it does," · "If this Digimon does," (P-029 · P-030 — 공식 "그렇게 했을 때") → "If digivolved by this effect,"
        t => Regex.Replace(t, @"^If (?:it|this Digimon) does, ", "If digivolved by this effect, ", I),
        // "If there're ~" (BT13-098 원문)
        t => Regex.Replace(t, @"^If there're\b", "If there are", I),
        // "you may suspend this Tamer to ~" (EX2-065 — 공식 "이 테이머를 레스트시키는 것으로, ~") → "By suspending this Tamer, ~"
        t => Regex.Replace(t, @"^you may suspend this Tamer to (?<a>[a-z].+)$", "By suspending this Tamer, ${a}", I),
        // ---- 원문 표기 ----
        // "While this Digimon has [Shoutmon] in its name it gains ~" (BT11-015 · BT12-082 — 쉼표가 빠졌다)
        t => Regex.Replace(t, @"^While (?<c>this Digimon (?:has|is) [^,]+?) (?=(?:it|this Digimon) (?:gets|gains)\b)", "While ${c}, ", I),
        // "If this Digimon has the [CS], ~" (BT23-002 — "trait"이 빠졌다)
        t => Regex.Replace(t, @"\bhas the (?<x>\[[^\]]+\])(?=,)", "has the ${x} trait", I),
        // "Your other [Gaossmon] all get +3000 DP" (BT5-008)
        t => Regex.Replace(t, @"^Your other (?<x>\[[^\]]+\]) all get\b", "All of your other ${x} get", I),
        // "When this Digimon deletes your opponent's Digimon in battle. gain 1 memory" (BT20-029 원문의 마침표)
        t => Regex.Replace(t, @"\bin battle\. (?=[a-z])", "in battle, "),
        // "switch the attack target to 1 of your [Eosmon]" (BT17-074 · BT17-075) → "~ 1 of your [Eosmon] Digimon"
        t => Regex.Replace(t, @"\bswitch the (?:attack target|target of (?:the )?attack) to 1 of your (?<x>\[[^\]]+\])$", "switch the attack target to 1 of your ${x} Digimon", I),
        // ---- 셈 ----
        // "For each card this effect trashed, delete 1 of your opponent's Digimon" (BT26-083) → "~ for each card this effect trashed" (앞 동작이 파기한 매수)
        t => Regex.Replace(t, @"^For each card this effect trashed, (?<a>.+)$", m => $"{char.ToUpperInvariant(m.Groups["a"].Value[0])}{m.Groups["a"].Value[1..]} for each card this effect trashed", I),
        // 원문 오타 "your opponents Level 5 or lower Digimon" (BT7-108)
        t => Regex.Replace(t, @"\byour opponents (?=(?:Level|level) \d)", "your opponent's ", I),
        // ---- 시큐리티 ----
        // "trash the top 2 cards of your security stack" · "trash your top 2 security cards" — 여러 장 (아래 ParseAction42)
        // "Look at the top card of your security stack, and you may add it to your hand" (BT9-034 — 공식 "자신의 시큐리티를 위에서부터 1장 확인하고, 그 카드를 패에 추가할 수 있다") → 맨 위 카드를 패에 추가할 수 있다
        t => Regex.Replace(t, @"^Look at the top card of your security stack, and you may add it to (?:your|the) hand$", "You may add your top security card to the hand", I),
        // "You may add that card to your hand to ＜Draw 1＞" (BT3-034 — 공식 "그 카드를 패에 추가하는 것으로, 《1 드로우》") → 비용
        t => Regex.Replace(t, @"^You may add that card to (?:your|the) hand to (?<a>＜Draw \d+＞)(?:\s*\([^)]*\))?$", "By adding the top card of your security stack to the hand, ${a}", I),
        // ---- 겹쳐진 카드 보호 (공식 BT21-060 "상대의 턴 종료까지 이 디지몬에 겹쳐져 있는 카드는 상대의 효과로 파기되지 않는다") ----
        // "Until your opponent's turn ends, their effects can't trash this Digimon's stacked cards" (BT21-060)
        t => Regex.Replace(t, @"^(?<d>Until [^,]+, )?(?:their|your opponent's) effects can't trash (?:this Digimon's|its) stacked cards$", "${d}this Digimon can't have its stacked cards trashed by your opponent's effects", I),
        // "~, your opponent's effects can't reduce this Digimon's DP or trash its stacked cards" (BT26-085)
        t => Regex.Replace(t, @"^(?<d>Until [^,]+, )?(?:their|your opponent's) effects can't reduce this Digimon's DP or trash its stacked cards$", "${d}this Digimon can't have its DP reduced by your opponent's effects and can't have its stacked cards trashed by your opponent's effects", I),
        // "until ~, their effects can't reduce the DP of 1 of your Digimon, trash any of its stacked cards, or return them to hands or decks" (BT26-029)
        t => Regex.Replace(t, @"^(?<d>Until [^,]+, )(?:their|your opponent's) effects can't reduce the DP of (?<t>1 of your [^,]*?Digimon), trash any of its stacked cards, or return (?:them|it) to hands or decks$",
            "${d}${t} can't have its DP reduced by your opponent's effects, can't have its stacked cards trashed by your opponent's effects and can't be returned to hands or decks by your opponent's effects", I),
        // "your opponent's effects can't trash any of your Digimon's stacked cards until their turn ends" (EX12-059)
        t => Regex.Replace(t, @"^(?:their|your opponent's) effects can't trash any of your Digimon's stacked cards (?<d>until .+)$", "All of your Digimon can't have their stacked cards trashed by your opponent's effects ${d}", I),
    };

    // =====================================================================
    // 3. 동작 — 원래 규칙이 읽지 못한 동작만 (ParseAction 끝)
    // =====================================================================

    private static (Action<EffectContext>? Act, string? Error)? ParseAction42(string raw, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth42 >= 3) return null;
        _fallbackDepth42++;
        try
        {
            string s = raw.Trim().TrimEnd('.');
            string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
            if (SecurityAction42(s, card, triggers, label) is { } sec) return sec;
            if (StackAction42(s, card, triggers, label) is { } st) return st;
            if (MoveAction42(s, label) is { } mv) return mv;
            if (LinkAction42(s, card, triggers, label) is { } lk) return lk;
            if (EvoIntoThisAction42(s, card, triggers, label) is { } evo) return evo;
            if (CountAction42(s, card, triggers, label) is { } cnt) return cnt;
            foreach (var alt in Rewrites42(s))
            {
                if (alt == s) continue;
                var r = ParseAction(alt, card, triggers);
                if (r.Error == null && r.Act != null) return r;
            }
            return null;
        }
        finally { _fallbackDepth42--; }
    }

    // ---- [패] [메인]: "that Digimon digivolves into this card, ignoring level" (BT27-064) ----

    private static (Action<EffectContext>? Act, string? Error)? EvoIntoThisAction42(string s, CardData card, List<EffectTrigger> triggers, string label)
    {
        // 공식 BT13-098 "Lv.을 무시하고 진화시킨다" — 진화 코스트는 이 카드의 진화 코스트, 색 조건은 본다
        var m = Regex.Match(s, @"^(?<t>it|that Digimon) digivolves into this card,? ignoring (?:its )?level$", I);
        if (!m.Success || card.Type != CardType.Digimon) return null;
        bool security = triggers.Contains(EffectTrigger.Security);
        var evoBase = TargetSpec.Parse("that Digimon");
        if (evoBase == null) return null;
        return (c =>
        {
            var d = Pick(evoBase, c, security).FirstOrDefault(x => c.Owner.BattleArea.Contains(x));
            if (d == null || !c.Owner.Hand.Contains(c.Card) || EffectRules.IsDigivolveLocked(c.Engine.State, d)
                || !(d.Colors.Contains(c.Card.EvoRequirementColor) || CardScripts.MeetsAlternativeEvolution(c.Card, d)))
            { c.LastStepResolved = false; Emit(c, label, "진화할 디지몬이 없습니다"); return; }
            c.Engine.EvolveByEffect(c.Owner, d, c.Card, c.Card.EvoCost, c.Owner.Hand);
            c.LastTargets = new List<CardInstance> { d };
            Emit(c, label, $"[{d.TopCard.Name}]이(가) 이 카드로 진화 (Lv. 무시, 코스트 {c.Card.EvoCost})");
        }, null);
    }
}
