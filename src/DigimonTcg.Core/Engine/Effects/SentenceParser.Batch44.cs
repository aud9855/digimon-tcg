using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.44.0 미지원 문장 18차 묶음 (넓게 훑기): 원래 규칙(1~17차)이 읽지 못한 절 · 문장 · 동작 · 비용 · 조건 · 셈만 18차 규칙으로 다시 읽는다
/// (엔진: GoldfishEngine.Batch44.cs). 렌더러도 같은 순서 (KoreanEffectRenderer.Batch44.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _clauseDepth44;
    [ThreadStatic] private static int _fallbackDepth44;

    // =====================================================================
    // 1. 절 — 원래 규칙이 읽지 못한 절만 (TryParse)
    // =====================================================================

    /// <summary>
    /// 18차 규칙의 대상 · 셈 · 조건 · 비용 · 한글을 켜는 문 — 원래 규칙(17차까지)이 읽지 못한 절 · 문장을 다시 읽을 때만 연다
    /// (켜 두면 원래 규칙이 다른 길로 읽던 문장의 처리가 바뀔 수 있다 — 수정 전 지원 문장은 그대로 둔다)
    /// </summary>
    [ThreadStatic] private static int _gate44;
    internal static bool Gate44 => _gate44 > 0;

    private static string? TryParse44(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse43(rawClause, inherited, card, def);
        if (err == null || _clauseDepth44 > 0) return err;
        _clauseDepth44++;
        _gate44++;
        int before = def?.Script41.Effects.Count ?? 0;
        try
        {
            // 18차 대상 · 셈 · 조건 · 비용으로 원래 규칙을 다시
            if (TryParse43(rawClause, inherited, card, null) == null)
            {
                if (def != null) TryParse43(rawClause, inherited, card, def);
                return null;
            }
            if (ExtraClause44(rawClause, inherited, card, def)) return null;
            foreach (var alt in ClauseRewrites44(rawClause))
            {
                if (alt == rawClause) continue;
                if (TryParse43(alt, inherited, card, null) != null) continue;
                if (def != null) TryParse43(alt, inherited, card, def);
                return null;
            }
            return err;
        }
        finally
        {
            _clauseDepth44--;
            _gate44--;
            if (def != null) WrapGate44(def, before);
        }
    }

    /// <summary>18차 규칙으로 등록한 시점 효과는 처리할 때도 문을 연다 (처리 중에 다시 읽는 문장 — 셈만큼 바꾼 DP · 상한 등)</summary>
    private static void WrapGate44(CardScriptDefinition def, int from)
    {
        var effects = def.Script41.Effects;
        for (int i = from; i < effects.Count; i++)
        {
            var inner = effects[i].Resolve;
            effects[i] = effects[i] with { Resolve = c => { _gate44++; try { inner(c); } finally { _gate44--; } } };
        }
    }

    /// <summary>고르는 효과: 원래 규칙이 읽지 못했을 때만 18차 규칙(문을 연 채로)으로 다시 읽는다</summary>
    private static string? TryParseChoice44(string rawHeader, IReadOnlyList<string> rawOptions, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParseChoiceCore(rawHeader, rawOptions, inherited, card, def);
        if (err == null || _gate44 > 0) return err;
        _gate44++;
        int before = def?.Script41.Effects.Count ?? 0;
        try
        {
            if (TryParseChoiceCore(rawHeader, rawOptions, inherited, card, null) != null) return err;
            if (def != null) TryParseChoiceCore(rawHeader, rawOptions, inherited, card, def);
            return null;
        }
        finally
        {
            _gate44--;
            if (def != null) WrapGate44(def, before);
        }
    }

    /// <summary>절 전체의 같은 뜻 다른 표기 (렌더러도 쓴다) — 절 규칙 → 문장마다 규칙. 원래 규칙으로 읽혀야 쓴다</summary>
    internal static IEnumerable<string> ClauseRewrites44(string rawClause)
    {
        string c = rawClause.Trim();
        string whole = c;
        foreach (var f in ClauseRules44) whole = f(whole);
        if (whole != c) yield return whole;
        var lead = Regex.Match(whole, @"^(?<labels>(?:Inherited Effect\s*|Security Effect\s*)?(?:\[[^\]]+\]\s*)*)(?<body>.*)$", RegexOptions.Singleline);
        string labels = lead.Groups["labels"].Value, body = lead.Groups["body"].Value.Trim();
        var sentences = Regex.Split(body, @"(?<=\.)\s+(?=[A-Z＜・•])");
        bool changed = false;
        var outs = new List<string>();
        foreach (var s in sentences)
        {
            string t = s.Trim();
            string end = t.EndsWith(".") ? "." : "";
            string core = t.TrimEnd('.');
            string? alt = Rewrites44(core).LastOrDefault();
            if (alt != null && alt != core) { changed = true; outs.Add(alt + end); }
            else outs.Add(t);
        }
        if (changed) yield return labels + string.Join(" ", outs);
    }

    /// <summary>절 전체 규칙 (문장 경계를 넘는 표기)</summary>
    private static readonly Func<string, string>[] ClauseRules44 =
    {
        // 짝 없는 닫는 괄호: "＜Decode ([Aegiomon])＞ (When ~ without paying the cost.))" (BT24-014 · P-213)
        t => t.EndsWith("))") && t.Count(ch => ch == ')') > t.Count(ch => ch == '(') ? t[..^1] : t,
        t => Regex.Replace(t, @"^(?<a>(?:Inherited Effect\s*)?＜[^＞]+＞)\)\s*", "${a} "),
        // 준 효과의 따옴표 표기: 굽은 따옴표 (RB1-030) · ''" ~ "'' (BT26-057) · "[On Deletion]" Lose 2 memory" (BT6-102) · "[On Deletion] "Play ~."" (BT3-109)
        t => Regex.Replace(t, "[”“]", "\""),
        t => t.Replace("''\" ", "\"").Replace("''\"", "\"").Replace("\"''", "\""),
        t => Regex.Replace(t, "\"(?<l>\\[[^\\]\"]+\\])\" (?=[A-Z])", "\"${l} "),
        t => Regex.Replace(t, "\"(?<l>\\[[^\\]\"]+\\]) \"(?<b>[^\"]+)\"\"", "\"${l} ${b}\""),
        // "gain(s) the following effect for the turn: "~"" (BT1-104 · BT1-112) · "~ gains "~", until the end of ~" (BT12-065) · "gets +2000 DP and "~"" (BT6-096)
        t => Regex.Replace(t, "\\b(?<v>gains?) the following effect(?<d> for the turn)?: (?<q>\"[^\"]+\")", "${v} ${q}${d}", I),
        t => Regex.Replace(t, "(?<q>\"[^\"]+\"), (?=until )", "${q} ", I),
        t => Regex.Replace(t, "\\b(?<g>gets? [+-]\\d+ DP) and \"", "${g} and gains \"", I),
        // "Trash the top card of your opponent's security stack. Add 1 to the number of cards trashed by this effect for every 10 cards in your opponent's trash." (EX2-073) — 1장 + 셈만큼 더
        t => Regex.Replace(t, @"(?<a>Trash the top card of your opponent's security stack\.)\s+Add 1 to the number of cards trashed by this effect for every (?<c>[^.]+)\.", "${a} For every ${c}, trash the top card of your opponent's security stack.", I),
        // "Choose 1 of each player's Digimon with the highest play cost. Delete all other Digimon." (EX4-069) — 한 문장으로
        t => Regex.Replace(t, @"\b(?<a>Choose 1 of (?:each player's|both players') Digimon(?: with the highest play cost)?)\.\s+Delete all other Digimon\b", "${a} and delete all other Digimon", I),
        // "By X, A. If this effect didn't ~, B." — 비용 뒤 결과 조건을 앞 문장에 붙인다 (StepExtra44: 비용을 냈을 때만 B를 본다)
        t => Regex.Replace(t, @"(?<a>(?:(?:^|(?<=[.\]]\s))(?:Then,?\s+)?By|(?<=, )by) [^.]+?)\.\s+(?<b>If (?:this effect didn't|you don't delete|this effect did not)\b[^.]+\.?)", "${a} " + IfNotMark44 + " ${b}", I),
    };

    private static string Cap44(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>"By X, A ⟦ifnot44⟧ If this effect didn't ~, B" — ClauseRules44가 합친 문장 (비용 뒤 결과 조건)</summary>
    internal const string IfNotMark44 = "⟦ifnot44⟧";

    // =====================================================================
    // 2. 문장 — 원래 규칙이 읽지 못한 문장만 (ParseStep 끝)
    // =====================================================================

    private static (Step? Step, string? Error)? StepFallback44(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth44 >= 3 || !Gate44) return null;   // 18차 규칙은 원래 규칙이 읽지 못한 절을 다시 읽을 때만
        _fallbackDepth44++;
        try
        {
            if (StepExtra44(sentence, card, triggers) is { } ex) return (ex, null);
            string s = Regex.Replace(sentence.Trim().TrimEnd('.').Trim(), @"^Then,?\s+", "", I);
            foreach (var alt in Rewrites44(s))
            {
                if (alt == s) continue;
                var r = ParseStep(alt, card, triggers);
                if (r.Error == null && r.Step != null) return (r.Step with { Text = sentence.Trim() }, null);
            }
            return null;
        }
        finally { _fallbackDepth44--; }
    }

    /// <summary>같은 뜻의 다른 표기 (렌더러도 쓴다). 여러 규칙을 차례로 겹쳐 적용한 결과도 낸다</summary>
    internal static IEnumerable<string> Rewrites44(string s)
    {
        string acc = s;
        foreach (var f in RewriteRules44)
        {
            string next = f(acc);
            if (next != acc)
            {
                yield return next;
                acc = next;
            }
        }
    }

    private static readonly Func<string, string>[] RewriteRules44 =
    {
        // 원문 오타 · 빠진 말
        t => Regex.Replace(t, @"^his Digimon\b", "This Digimon"),                                         // EX1-042
        t => Regex.Replace(t, @"\btrait trait\b", "trait", I),                                             // BT13-050 · BT26-005 · EX5-064
        t => Regex.Replace(t, @"\b1 or your\b", "1 of your", I),                                           // P-215
        t => Regex.Replace(t, @"\bIs in\b", "is in"),                                                      // BT18-024
        t => Regex.Replace(t, @"\blevel (?<n>\d+) of lower\b", "level ${n} or lower", I),                 // BT9-078
        t => Regex.Replace(t, @"\b1 of your their Digimon\b", "1 of their Digimon", I),                    // BT18-099
        t => Regex.Replace(t, @"\bUntil the end of opponent's turn\b", "Until the end of your opponent's turn", I),
        t => Regex.Replace(t, @"\breducing the digivolution cost reduced by\b", "with the digivolution cost reduced by", I),   // P-112
        t => Regex.Replace(t, @"\bthe \[(?<x>[^\]]+)\] in the hand\b", "the [${x}] trait in the hand", I),                    // BT23-053 "or the [CS] in the hand"
        t => Regex.Replace(t, @"\bmust attacks\b", "attacks", I),                                          // BT18-069
        // "the DNA digivolved Digimon may attack" (BT20-036 · BT20-043 · EX12-001 · EX8-027 · EX8-060) — 앞 문장의 DNA 진화로 생긴 디지몬
        t => Regex.Replace(t, @"\b(?:the|that) DNA digivolved Digimon\b", "that Digimon", I),
        // "with the [Ghost] trait and [LIBERATOR] in the hand" (BT23-098 — 공식 "특징 「고스트형」과 「LIBERATOR」를 가진")
        t => Regex.Replace(t, @"\bwith the (?<a>\[[^\]]+\]) trait and (?<b>\[[^\]]+\]) in the hand\b", "with the ${a} and ${b} traits in the hand", I),
        // 조건이 붙은 주어: "This Digimon with an [SoC] trait Tamer card in its digivolution cards may ~" (BT16-061 · BT16-076) · "this Digimon with [Leon Alexander] in its digivolution cards may ~" (BT17-036) ·
        //  "This Digimon with the [Hybrid] or [Ten Warriors] trait may attack a player" (BT18-088) → "If ~, this Digimon may ~"
        t => Regex.Replace(t, @"^(?<pre>(?:Then, )?)[Tt]his Digimon with (?<x>an? \[[^\]]+\] trait Tamer card) in its digivolution cards may ", "${pre}If this Digimon has ${x} in its digivolution cards, this Digimon may "),
        t => Regex.Replace(t, @"^(?<pre>(?:Then, )?)[Tt]his Digimon with (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) in its digivolution cards may ", "${pre}If ${x} is in this Digimon's digivolution cards, this Digimon may "),
        t => Regex.Replace(t, @"^(?<pre>(?:Then, )?)[Tt]his Digimon with the (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) trait may ", "${pre}If this Digimon has the ${x} trait, this Digimon may "),
        // "This [VB] trait Digimon and any of your other Digimon may DNA digivolve into ~" (EX12-001 진화원 효과) → "If this Digimon has the [VB] trait, this Digimon and ~"
        t => Regex.Replace(t, @"^(?<pre>(?:Then, )?)This (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) trait Digimon and (?<rest>(?:any of )?your other .+ may DNA digivolve into .+)$", "${pre}If this Digimon has the ${x} trait, this Digimon and ${rest}", I),
        // "Your Digimon can't be blocked by your opponent's Digimon this turn" (BT6-028)
        t => Regex.Replace(t, @"^(?<a>Your Digimon can't be blocked by your opponent's Digimon) this turn$", "${a} for the turn", I),
        // "1 of your Digimon with [Greymon] in its name can attack your opponent" (BT9-095 — 공식 "상대 플레이어에게 어택할 수 있다")
        t => Regex.Replace(t, @"^(?<a>1 of your .+?) can attack your opponent$", "${a} may attack a player", I),
        // "return 1 Tamer card and 1 [Hybrid] trait Digimon card from your trash to the hand" (BT17-017 · BT17-028) — 1장씩
        t => Regex.Replace(t, @"^(?<may>You may )?[Rr]eturn (?<a>1 [^,]+?) and (?<b>1 [^,]+?) from your trash to (?:the|your) hand$", m => $"{(m.Groups["may"].Success ? "You may r" : "R")}eturn {m.Groups["a"].Value} from your trash to the hand and return {m.Groups["b"].Value} from your trash to the hand", I),
        // "1 [Numemon] or level 3 Digimon card" (BT15-040) · "1 [Suzune Kazuki] or level 4 or lower ~" (EX11-017) · "1 play cost 4 or higher [Mirei Mikagura] or play cost 4 or higher Tamer card ~" (BT22-089) — 둘째 고르기에도 "1"
        t => Regex.Replace(t, @"\b1 (?<a>(?:play cost \d+ or (?:higher|lower) )?\[[^\]]+\]) or (?=(?:level \d+|play cost \d+)\b)", "1 ${a} or 1 ", I),
        // 상한 늘리기의 다른 표기 (앞 문장의 Lv. · 등장 코스트 상한): BT10-108 · BT14-079 · BT14-081 · BT4-110 · BT13-035 · BT13-064 · BT12-083 · ST19-11 · ST19-15
        t => Regex.Replace(t, @"^(?<pre>(?:If|For (?:each|every)) [^,]+), (?:add|increase) \+?(?<n>\d+) to the level of the Digimon you (?:can|may) select with this effect$", "${pre}, add ${n} to this effect's level maximum", I),
        t => Regex.Replace(t, @"^(?<pre>(?:If|For (?:each|every)) [^,]+), increase the level by (?<n>\d+)$", "${pre}, add ${n} to this effect's level maximum", I),
        t => Regex.Replace(t, @"^(?<pre>(?:If|For (?:each|every)) [^,]+), add \+?(?<n>\d+) to the level this effect (?:can|may) choose$", "${pre}, add ${n} to this effect's level maximum", I),
        t => Regex.Replace(t, @"^(?<pre>(?:If|For (?:each|every)) [^,]+), add \+?(?<n>\d+) to the maximum play cost of the Digimon you (?:can|may) choose with this effect$", "${pre}, add ${n} to this effect's play cost maximum", I),
        t => Regex.Replace(t, @"^(?<pre>(?:If|For (?:each|every)) [^,]+), add \+?(?<n>\d+) to the maximum level of the (?:card|Digimon) you (?:can|may) (?:play|delete) (?:by|with) this effect$", "${pre}, add ${n} to this effect's level maximum", I),
        t => Regex.Replace(t, @"^(?<pre>(?:If|For (?:each|every)) [^,]+), increase the maximum level this effect can choose by (?<n>\d+)$", "${pre}, add ${n} to this effect's level maximum", I),
        t => Regex.Replace(t, @"^(?<pre>If [^,]+), increase the DP reduction of this effect by -(?<n>\d+)$", "${pre}, add ${n} to this effect's DP reduction", I),
        t => Regex.Replace(t, @"\bwith \[(?<x>[^\]]+)\] in its name in their names\b", "with [${x}] in their names", I),
        // 다음 액티브 페이즈: "During your opponent's next unsuspend phase, all of your opponent's Digimon with 5000 DP or less don't unsuspend" (BT14-047 · EX5-041)
        t => Regex.Replace(t, @"^During your opponent's next unsuspend phase, (?<x>.+?) (?:don't|can't) unsuspend$", "${x} don't unsuspend during their next unsuspend phase", I),
        // "Cards this effect suspended can't unsuspend ~" (BT16-046) · "Your opponent's cards suspended with this effect can't unsuspend ~" (BT8-102) · "Digimon suspended by this effect don't unsuspend ~" (RB1-023) — 앞 문장의 대상
        t => Regex.Replace(t, @"^(?:Cards this effect suspended|Your opponent's cards suspended (?:with|by) this effect|Digimon suspended (?:by|with) this effect) (?<v>can't|don't) unsuspend\b", "They ${v} unsuspend", I),
        t => Regex.Replace(t, @"^They don't unsuspend (?=until )", "They can't unsuspend ", I),
        t => Regex.Replace(t, @"^They can't unsuspend during your opponent's next unsuspend phase$", "They don't unsuspend during their next unsuspend phase", I),
        // "by paying its digivolution cost" (BT9-109 · BT8-066 · BT9-106 — 공식 "코스트를 지불하여 ~로 진화시킬 수 있다") → "for its digivolution cost"
        t => Regex.Replace(t, @"^(?<a>(?:This|this|that|That|1 of your) .*?)\b(?:can|may) digivolve into (?<p>.+?) by paying (?:its|the) (?:digivolution |memory )?cost$", "${a}may digivolve into ${p} for its digivolution cost"),
        t => Regex.Replace(t, @"^(?<pre>(?:you may )?)digivolve (?<t>this Digimon|1 of your Digimon) into (?<p>.+?) by paying (?:its|the) (?:digivolution |memory )?cost$", m => $"{Cap44(m.Groups["t"].Value)} may digivolve into {m.Groups["p"].Value} for its digivolution cost", I),
        t => Regex.Replace(t, @"\bmay digivolve into (?<p>a Digimon card) in your hand (?<w>with the \[[^\]]+\] trait) for its digivolution cost$", "may digivolve into ${p} ${w} in your hand for its digivolution cost", I),
        // "By placing 1 Digimon card with [Numemon] in its name, from your trash as ~" (BT14-039 — 쉼표)
        t => Regex.Replace(t, @"(?<=\bin (?:its|their) names?), (?=from your (?:trash|hand)\b)", " ", I),
        // 겹쳐진 카드 보호의 다른 표기: "Your opponent's effects can't reduce this unsuspended Digimon's DP, return its stacked cards to the hand or deck, or trash them" (EX13-023)
        t => Regex.Replace(t, @"^Your opponent's effects can't reduce this unsuspended Digimon's DP, return its stacked cards to the hand or deck, or trash them$",
            "While this Digimon is unsuspended, it can't have its DP reduced by your opponent's effects, can't be returned to the hand or deck by your opponent's effects and can't have its stacked cards trashed by your opponent's effects", I),
        // "Your opponent's effects don't affect this Digimon with [Takato Matsuki] trait in its digivolution cards and it gets +5000 DP" (AD1-008) — 조건이 붙은 두 술어
        t => Regex.Replace(t, @"^Your opponent's effects don't affect this Digimon with (?<x>\[[^\]]+\])(?: trait)? in its digivolution cards and it gets (?<dp>[+-]\d+ DP)$",
            "While ${x} is in this Digimon's digivolution cards, this Digimon isn't affected by your opponent's effects and gets ${dp}", I),
        // "from under your Tamer" (BT19-090 — 단수)
        t => Regex.Replace(t, @"\bfrom under your Tamer\b(?!s)", "from under your Tamers", I),
        // "Activate this Digimon's [When Digivolving] effect" (EX2-038)
        t => Regex.Replace(t, @"\bactivate this Digimon's \[When Digivolving\] effect$", "activate 1 of this Digimon's [When Digivolving] effects", I),
        // "with 6000 DP or less and ＜Blocker＞" (EX1-067) → "with ＜Blocker＞ and 6000 DP or less"
        t => Regex.Replace(t, @"\bwith (?<dp>\d+ DP or (?:less|more)) and (?<kw>＜[^＞]+＞)", "with ${kw} and ${dp}", I),
        // "with a play cost of 10 or higher" (BT13-075)
        t => Regex.Replace(t, @"\bwith a play cost of (?<n>\d+) or higher\b", "with a play cost of ${n} or more", I),
        // "this Digimon can't attack and your opponent's effects don't affect it" (EX10-021)
        t => Regex.Replace(t, @"\bthis Digimon can't attack and your opponent's effects don't affect it$", "this Digimon can't attack and isn't affected by your opponent's effects", I),
        // "their effects can't return 1 of your ~ to hands or decks or affect it with ＜De-Digivolve＞ effects" (BT21-074 · P-215)
        t => Regex.Replace(t, @"^(?<d>Until [^,]+, )their effects can't return (?<t>(?:1|that) .+?|that Digimon) to hands or decks or affect it with ＜De-Digivolve＞ effects$",
            "${d}${t} can't be returned to hands or decks by your opponent's effects and isn't affected by your opponent's ＜De-Digivolve＞ effects", I),
        // "While you have a Tamer in play this Digimon gains ＜Blocker＞" (EX2-017 — 쉼표가 빠짐)
        t => Regex.Replace(t, @"^While (?<c>[^,]+?) (?=this Digimon gains )", "While ${c}, ", I),
        // "~ to have all of your opponent's Security Digimon get -2000 DP for the turn" (BT9-084) — "have X get" = "X get"
        t => Regex.Replace(t, @"^(?<pre>By [^,]+, )?have (?=\S.* (?:get|gets|gain|gains) )", "${pre}", I),
        // "all of your opponent's Digimon and security Digimon" (BT15-092 — 소문자)
        t => Regex.Replace(t, @"\band security Digimon\b", "and Security Digimon"),
        // "From the digivolution cards of this Digimon with [MetalSeadramon] or [X Antibody] in its digivolution cards, you may play 1 ~" (BT20-028 — 공식 "진화원에 「메탈시드라몬」/「X항체」가 있는 이 디지몬의 진화원에서")
        t => Regex.Replace(t, @"^From the digivolution cards of this Digimon with (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) in its digivolution cards, you may play (?<p>.+?) without paying the cost$",
            "If ${x} is in this Digimon's digivolution cards, you may play ${p} from this Digimon's digivolution cards without paying the cost", I),
        // "you may switch the target of an attack to this Digimon" (BT9-044)
        t => Regex.Replace(t, @"\bswitch the target of an attack to\b", "switch the attack target to", I),
        // "Until the end of your opponent's turn, this Digimon can't be returned to the hand or deck and it gets +3000 DP" (EX8-015) — 두 문장으로
        t => Regex.Replace(t, @"^(?<d>Until [^,]+), (?<a>this Digimon can't be returned to (?:the|its owner's) hand or deck) and it (?<b>gets [+-]\d+ DP)$", "${d}, ${a}. ${d}, this Digimon ${b}", I),
        // 색 목록의 쉼표: "red, green, purple or white Digimon" (BT18-080) → "red or green or purple or white"
        t => Regex.Replace(t, @"\b(?<a>(?:red|blue|yellow|green|black|purple|white)(?:, (?:red|blue|yellow|green|black|purple|white))+),? (?:or|and/or) (?<b>red|blue|yellow|green|black|purple|white)\b(?= (?:level \d+ or (?:lower|higher) )?(?:Digimon|Tamer))",
            m => m.Groups["a"].Value.Replace(", ", " or ") + " or " + m.Groups["b"].Value, I),
        // "None of your opponent's Tamers can unsuspend until ~" (LM-010) · 지속 "Your opponent's Tamers don't unsuspend" (BT5-058 — 공식 "상대의 테이머 전부는 액티브가 되지 않는다")
        t => Regex.Replace(t, @"^None of (?<w>your opponent's|their) Tamers can unsuspend (?<d>until .+)$", "All of ${w} Tamers can't unsuspend ${d}", I),
        t => Regex.Replace(t, @"^Your opponent's Tamers don't unsuspend$", "None of your opponent's Tamers can unsuspend", I),
        // "gets -1000 for the turn" (BT19-078 — DP가 빠짐)
        t => Regex.Replace(t, @"\bgets? (?<n>[+-]\d+) (?=for the turn|until )", "gets ${n} DP ", I),
        // "From your hand and without paying the cost, you may play 1 ~ (from your hand) (without paying the cost)" (BT22-012 · BT22-022 · BT22-046 · BT16-072)
        t => Regex.Replace(t, @"(?<pre>^|, )from your (?<z>hand|trash) and without paying the cost, you may play (?<p>.+?)(?: from your \k<z>)?(?: without paying the cost)?$",
            m => (m.Groups["pre"].Value.Length == 0 ? "You" : ", you") + $" may play {m.Groups["p"].Value} from your {m.Groups["z"].Value} without paying the cost", I),
    };

    // =====================================================================
    // 3. 동작 — 원래 규칙이 읽지 못한 동작만 (ParseAction 끝)
    // =====================================================================

    private static (Action<EffectContext>? Act, string? Error)? ParseAction44(string raw, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth44 >= 3 || !Gate44) return null;   // 18차 규칙은 원래 규칙이 읽지 못한 절을 다시 읽을 때만
        _fallbackDepth44++;
        try
        {
            string s = raw.Trim().TrimEnd('.');
            string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
            if (ActionExtra44(s, card, triggers, label) is { } ex) return ex;
            foreach (var alt in Rewrites44(s))
            {
                if (alt == s) continue;
                var r = ParseAction(alt, card, triggers);
                if (r.Error == null && r.Act != null) return r;
            }
            return null;
        }
        finally { _fallbackDepth44--; }
    }

    // ---- 아래 셋은 SentenceParser.Batch44Extra.cs 에서 채운다
}
