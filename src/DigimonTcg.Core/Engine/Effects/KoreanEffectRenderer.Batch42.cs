using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.42.0 미지원 문장 16차 묶음의 생성 한글 — 원래 규칙(15차까지)이 읽지 못한 절 · 문장만 (SentenceParser.Batch42와 같은 읽기)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth42;
    [ThreadStatic] private static int _fallbackDepth42;

    /// <summary>수 뒤의 "으로/로" — 0 · 3 · 6 (영 · 삼 · 육)은 "으로"</summary>
    private static string Ro42(string n) => n.Length > 0 && n[^1] is '0' or '3' or '6' ? "으로" : "로";

    // =====================================================================
    // 1. 절 (RenderClause 끝)
    // =====================================================================

    private static string? RenderClause42(string rawClause)
    {
        if (_clauseDepth42 > 0) return null;
        _clauseDepth42++;
        try
        {
            string clause = Regex.Replace(SentenceParser.NormalizeForRender(rawClause), @"^(?:Inherited|Security) Effect\s*", "", IC).Trim();
            Match m;
            // 진화할 카드 쪽 — 공식 BT2-111 "자신의 트래시가 10장 이상일 동안, 자신의 「임프몬」은 패의 이 카드에 진화 조건을 무시하고 진화 코스트 4로 진화할 수 있다"
            //  · RB1-009 "진화원에 명칭에 「감마몬」을 포함하는 카드가 있는 자신의 「감마몬」은 패의 이 카드로 진화 조건을 무시하고 진화 코스트 3으로 진화할 수 있다"
            if ((m = SentenceParser.EvoSideRe42.Match(clause)).Success)
            {
                string cond = "";
                if (m.Groups["cond"].Success)
                {
                    if (CondKo(m.Groups["cond"].Value) is not { } ck) return null;
                    cond = ck.While + " ";
                }
                string who = NamesKo(m.Groups["who"].Value);
                if (m.Groups["srcname"].Success) who = $"진화원에 명칭에 {NamesKo(m.Groups["srcname"].Value)}{J(NamesKo(m.Groups["srcname"].Value), "을", "를")} 포함하는 카드가 있는 자신의 {who}";
                else who = "자신의 " + who;
                string n = m.Groups["n"].Value;
                return $"{cond}{who}{J(who, "은", "는")} 패의 이 카드로 진화 조건을 무시하고 진화 코스트 {n}{Ro42(n)} 진화할 수 있다.";
            }
            // 진화하는 디지몬 쪽 — 공식 BT23-026 "【자신의 턴】자신의 「다테 마키코」가 있는 동안, 이 디지몬은 패의 「안티라몬」으로 진화 조건을 무시하고 진화 코스트 3으로 진화할 수 있다"
            //  · ST7-03 "【자신의 턴】 Lv.6 이상인 상대의 디지몬이 있을 때, 이 디지몬은 ~"
            if ((m = SentenceParser.HolderSideRe42.Match(clause)).Success)
            {
                string cond = "";
                if (m.Groups["cond"].Success)
                {
                    if (CondKo(m.Groups["cond"].Value) is not { } ck) return null;
                    cond = (clause.StartsWith("If", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(clause, @"^(?:\[[^\]]+\]\s*)*If\b", IC) ? ck.If : ck.While) + " ";
                }
                string p = Regex.Replace(m.Groups["pick"].Value.Trim(), @"^(?:an?|1) ", "", IC);
                string? into = Regex.IsMatch(p, @"^\[[^\]]+\](?: or \[[^\]]+\])*$") ? NamesKo(p) : PickKo("1 " + p) is { } pk ? Regex.Replace(pk, @"\s*1장$", "") + " 1장" : null;
                if (into == null) return null;
                bool hand = Regex.IsMatch(clause, @" (?:in|from) (?:your|the) hand for ", IC);
                string n = m.Groups["n"].Value;
                return $"{LabelsKo40(m.Groups["labels"].Value)}{cond}이 디지몬은 {(hand ? "패의 " : "")}{into}{J(into, "으로", "로")} 진화 조건을 무시하고 진화 코스트 {n}{Ro42(n)} 진화할 수 있다.";
            }
            // 소멸시킨 디지몬의 등장 코스트만큼 — 공식 BT13-103 "【자신의 턴】명칭에 「벨페몬」을 포함하는 카드가 등장할 때, 명칭에 「기즈몬」을 포함하는 자신의 디지몬 1마리를 소멸시키는 것으로,
            //  소멸시킨 디지몬의 등장 코스트의 수치만큼, 지불하는 등장 코스트를 마이너스한다"
            if ((m = SentenceParser.DeletedCostRe42.Match(clause)).Success)
            {
                string t = m.Groups["t"].Value.Trim();
                string? who = TargetKo("1 of your " + t);
                var neg = Regex.Match(t, @"^play cost (?<n>\d+) or lower Digimon with (?<a>\[[^\]]+\]) in its digivolution cards and (?<b>\[[^\]]+\]) in its text$", IC);
                if (who == null && neg.Success)
                {
                    string a = NamesKo(neg.Groups["a"].Value), b = NamesKo(neg.Groups["b"].Value);
                    who = $"진화원에 {a}{J(a, "이", "가")} 있고 {b}{J(b, "이", "가")} 기술되어 있는 등장 코스트 {neg.Groups["n"].Value} 이하의 자신의 디지몬 1마리";
                }
                if (who == null) return null;
                string head;
                if (m.Groups["self"].Success) head = "이 카드가 등장할 때, ";
                else if (PickKo("1 " + Regex.Replace(m.Groups["p"].Value.Trim(), @"^(?:an?|any) ", "", IC)) is { } pk) head = $"{LabelsKo40(m.Groups["labels"].Value)}{Regex.Replace(pk, @"\s*1장$", "")}{J(Regex.Replace(pk, @"\s*1장$", ""), "이", "가")} 등장할 때, ";
                else return null;
                return $"{head}{who}{J(who, "을", "를")} 소멸시키는 것으로, 소멸시킨 디지몬의 등장 코스트의 수치만큼, 지불하는 등장 코스트를 마이너스한다.";
            }
            foreach (var alt in SentenceParser.ClauseRewrites42(rawClause, new CardData { Type = CardType.Digimon }))
                if (alt != rawClause && (RenderClauseCore(alt) ?? RenderClause40(alt) ?? RenderClause41(alt)) is { } ko)
                    return ko;
            return null;
        }
        finally { _clauseDepth42--; }
    }

    // =====================================================================
    // 2. 문장 (Sentence 끝)
    // =====================================================================

    private static string? SentenceFallback42(string s, bool nested)
    {
        if (_fallbackDepth42 >= 3) return null;
        _fallbackDepth42++;
        try
        {
            s = s.Trim().TrimEnd('.').Trim();
            if (Regex.IsMatch(s, @"^Then,?\s", IC)) return null;
            Match m;
            // 공식 BT6-087 "자신의 시큐리티를 위에서부터 2장 파기한다"
            if ((m = Regex.Match(s, @"^Trash (?:the top (?<n>\d+) cards of your security stack|your top (?<n>\d+) security cards)$", IC)).Success)
                return $"자신의 시큐리티를 위에서부터 {m.Groups["n"].Value}장 파기한다";
            if (Regex.IsMatch(s, @"^Trash all (?:of )?your security cards$", IC))
                return "자신의 시큐리티를 전부 파기한다";
            // 공식 BT3-034 "자신의 시큐리티의 내용을 위에서부터 1장 확인한다"
            if (Regex.IsMatch(s, @"^Look at the top card of your security stack$", IC))
                return "자신의 시큐리티의 내용을 위에서부터 1장 확인한다";
            // 공식 BT9-034 "자신의 시큐리티를 위에서부터 1장 확인하고, 그 카드를 패에 추가할 수 있다"
            if (Regex.IsMatch(s, @"^Look at the top card of your security stack, and you may add it to (?:your|you|the) hand$", IC))
                return "자신의 시큐리티를 위에서부터 1장 확인하고, 그 카드를 패에 추가할 수 있다";
            // 공식 BT3-034 "그 카드를 패에 추가하는 것으로, 《1 드로우》"
            if ((m = Regex.Match(s, @"^You may add that card to (?:your|the) hand to (?<a>＜Draw \d+＞)(?:\s*\([^)]*\))?$", IC)).Success && Sentence(m.Groups["a"].Value, true) is { } dk)
                return $"그 카드를 패에 추가하는 것으로, {dk}";
            // 공식 EX4-013 "이 턴 종료 시, 이 디지몬을 패로 되돌린다"
            if ((m = Regex.Match(s, @"^Return (?<t>this Digimon|it|that Digimon) to (?:your|its owner's|the) hand at the end of (?:the|this) turn$", IC)).Success)
                return $"이 턴 종료 시, {(m.Groups["t"].Value.Equals("this Digimon", StringComparison.OrdinalIgnoreCase) ? "이" : "그")} 디지몬을 패로 되돌린다";
            // 공식 BT21-030 "상대의 디지몬 1마리에 겹쳐져 있는 카드를 위에서부터 10장 파기한다"
            if ((m = Regex.Match(s, @"^Trash the top (?<n>\d+) stacked cards of (?<t>.+)$", IC)).Success && TargetKo(m.Groups["t"].Value) is { } st)
                return $"{st}에 겹쳐져 있는 카드를 위에서부터 {m.Groups["n"].Value}장 파기한다";
            // 공식 BT13-098 "Lv.을 무시하고 진화시킨다" (BT27-064 — 그 디지몬을 이 카드로)
            if ((m = Regex.Match(s, @"^(?<t>it|that Digimon) digivolves into this card,? ignoring (?:its )?level$", IC)).Success)
                return "그 디지몬을 이 카드로 Lv.을 무시하고 진화시킨다";
            // 공식 EX10-013 "{육성}【진화 시】이 디지몬을 이동시킬 수 있다"
            if ((m = Regex.Match(s, @"^This Digimon (?<may>may )?moves?(?: to (?:your|the) battle area)?$", IC)).Success)
                return m.Groups["may"].Success ? "이 디지몬을 이동시킬 수 있다" : "이 디지몬을 이동시킨다";
            if (LinkKo42(s) is { } lk) return lk;
            if (InsteadKo42(s, nested) is { } ins) return ins;
            if (ReduceKo42(s, nested) is { } red) return red;
            foreach (var alt in SentenceParser.Rewrites42(s))
                if (alt != s && Sentence(alt, nested) is { } ko) return ko;
            return null;
        }
        finally { _fallbackDepth42--; }
    }

    /// <summary>
    /// "대신" (SentenceParser.InsteadStep42와 같은 합친 문장) — 공식 EX3-072 "~ 소멸시킨다. 자신의 디지몬 1마리를 소멸시키는 것으로, 대신 ~"
    /// </summary>
    private static string? InsteadKo42(string s, bool nested)
    {
        int at = s.IndexOf(SentenceParser.InsteadMark42.Trim(), StringComparison.Ordinal);
        if (at < 0) return null;
        string a = s[..at].Trim(), second = s[(at + SentenceParser.InsteadMark42.Trim().Length)..].Trim();
        if (Sentence(a, nested) is not { } aKo) return null;
        Match m;
        if ((m = SentenceParser.InsteadByRe42.Match(second)).Success)
        {
            string b = SentenceParser.InsteadB42(a, m.Groups["b"].Value);
            if (CostKo(m.Groups["cost"].Value) is not { } costKo || Sentence(b, true) is not { } bKo) return null;
            return $"{aKo.TrimEnd('.')}. {costKo} 대신 {bKo}";
        }
        return null;
    }

    /// <summary>
    /// 앞 문장의 코스트를 더 줄인다 (SentenceParser.ReduceStep42와 같은 합친 문장) — 공식 BT21-076 "서로의 트래시 합계 10장마다 이 효과로 지불하는 코스트 -1" ·
    /// EX5-062 "이 효과로 파기한 1장마다 이 효과의 코스트 경감 수치 -1" · EX5-043 "~ 있다면, 이 효과의 코스트 경감 수치 -3"
    /// </summary>
    private static string? ReduceKo42(string s, bool nested)
    {
        int at = s.IndexOf(SentenceParser.ReduceMark42.Trim(), StringComparison.Ordinal);
        if (at < 0) return null;
        string a = s[..at].Trim(), second = s[(at + SentenceParser.ReduceMark42.Trim().Length)..].Trim();
        // "~ play 1 [ADVENTURE] trait card from your hand" (코스트를 낸다 — AD1-019): "감소 0"으로 옮기고 그 말을 뺀다
        string? aKo = Sentence(a, nested)
            ?? (SentenceParser.ReducedSentence42(a, 0) is { } a0 && Sentence(a0, nested) is { } k0 ? Regex.Replace(k0, @"지불하는 (?:등장 |진화 )?코스트 -0 하여 |(?:, )?지불하는 (?:등장 |진화 )?코스트를 -0 하고", "") : null);
        if (aKo == null) return null;
        bool further = Regex.IsMatch(second, @"\bfurther reduce it\b", IC);
        Match m;
        string? head = null;
        if ((m = SentenceParser.ReduceForRe42.Match(second)).Success)
        {
            string cnt = m.Groups["cnt"].Value.Trim();
            head = Regex.IsMatch(cnt, @"^(?:card|one) (?:trashed|placed|returned) by this effect$|^card this effect (?:trashed|placed|returned)$", IC)
                ? (cnt.Contains("trashed") ? "이 효과로 파기한 1장마다" : cnt.Contains("placed") ? "이 효과로 놓은 1장마다" : "이 효과로 되돌린 1장마다")
                : CounterKo(cnt);
        }
        else if ((m = SentenceParser.ReduceIfRe42.Match(second)).Success)
            head = CondKo(m.Groups["c"].Value)?.Then;
        if (head == null) return null;
        string n = m.Groups["n"].Value;
        return $"{aKo.TrimEnd('.')}. {head} {(further ? $"이 효과의 코스트 경감 수치 -{n}" : $"이 효과로 지불하는 코스트 -{n}")}";
    }

    /// <summary>효과로 링크 — 공식 ST22-12 "자신의 패 또는 이 디지몬의 진화원에서, 특징 「소셜」/「내비」/「툴」을 가진 디지몬 카드 1장을 이 디지몬에게 지불하는 코스트 -2 하여 링크할 수 있다"</summary>
    private static string? LinkKo42(string s)
    {
        var m = SentenceParser.LinkRe42.Match(s);
        if (!m.Success) return null;
        var zones = Regex.Matches(m.Groups["from"].Value, @"(?:your |this Digimon's |your Digimon's )?(?:hand|trash|digivolution cards)", IC)
            .Select(z => z.Value.ToLowerInvariant() switch
            {
                var v when v.Contains("this digimon's") => "이 디지몬의 진화원",
                var v when v.Contains("your digimon's") => "자신의 디지몬의 진화원",
                var v when v.Contains("hand") => "자신의 패",
                var v when v.Contains("trash") => "자신의 트래시",
                _ => "",
            }).Where(z => z.Length > 0).ToList();
        if (zones.Count == 0) return null;
        // "your hand, trash" — 같은 "자신의"는 한 번만
        string where = string.Join(" 또는 ", zones.Select((z, i) => i > 0 && zones[i - 1].StartsWith("자신의 ") && z.StartsWith("자신의 ") && !z.Contains("디지몬") ? z[4..] : z));
        string pickText = m.Groups["pick"].Value.Trim();
        string n = m.Groups["n"].Value;
        int count = int.TryParse(Regex.Match(n, @"\d+").Value, out int k) ? k : 1;
        bool upTo = n.StartsWith("up to", StringComparison.OrdinalIgnoreCase);
        string? what = Regex.IsMatch(pickText, @"^cards?$", IC) ? "카드" : PickKo("1 " + Regex.Replace(pickText, @"s$", "")) is { } pk ? Regex.Replace(pk, @"\s*1장$", "") : null;
        if (what == null) return null;
        string t = m.Groups["t"].Success ? m.Groups["t"].Value : "any of your Digimon";
        string? to = t.Equals("this Digimon", StringComparison.OrdinalIgnoreCase) ? "이 디지몬"
            : t.Equals("1 of those Digimon", StringComparison.OrdinalIgnoreCase) ? "그 디지몬"
            : t.StartsWith("any of your", StringComparison.OrdinalIgnoreCase) ? "자신의 디지몬"
            : TargetKo(Regex.Replace(t, @" on the field$", "", IC));
        if (to == null) return null;
        string cost = m.Groups["free"].Success ? "코스트를 지불하지 않고" : $"지불하는 코스트 -{m.Groups["red"].Value} 하여";
        bool may = m.Groups["may"].Success || Regex.IsMatch(s, @"^you may\b", IC);
        return $"{where}에서, {what} {count}장{(upTo ? "까지" : "")}{(upTo ? "를" : "을")} {to}에게 {cost} 링크{(may ? "할 수 있다" : "한다")}";
    }

    [ThreadStatic] private static int _counterDepth42;

    /// <summary>v0.42.0 셈 (SentenceParser.ParseCounter42와 같은 읽기) — 공식 BT12-057 "레스트 상태인 디지몬과 테이머 2마리마다" · BT18-079 "상대의 디지몬과 테이머의 색 1색마다" · BT6-086 "자신과 상대의 테이머 1명당" · BT7-108 "특징으로 「하이브리드체」를 갖는 자신의 디지몬과 자신의 테이머 1명마다"</summary>
    private static string? CounterKo42(string counter)
    {
        if (_counterDepth42 > 1) return null;
        _counterDepth42++;
        try
        {
            string s = counter.Trim().TrimEnd('.');
            Match m;
            string Per(Match x) => x.Groups["n"].Success ? x.Groups["n"].Value : "1";
            if ((m = Regex.Match(s, @"^(?:(?<n>\d+) )?(?:of (?<w>your opponent's|their|your) )?suspended Digimon (?:and/or|and|or) Tamers(?: (?<w2>your opponent has|you have))?(?: in play)?$", IC)).Success)
            {
                string w = (m.Groups["w"].Value + m.Groups["w2"].Value).ToLowerInvariant();
                string who = w.Contains("opponent") || w.Contains("their") ? "상대의 " : w.Contains("you") ? "자신의 " : "";
                return $"레스트 상태인 {who}디지몬과 테이머 {Per(m)}마리(명)마다";
            }
            if ((m = Regex.Match(s, @"^(?:(?<n>\d+) )?of (?:your Tamers'|their) colou?rs$", IC)).Success) return $"자신의 테이머의 색 {Per(m)}색마다";
            if (Regex.IsMatch(s, @"^colou?r (?:among|of) your opponent's Digimon and Tamers$", IC)) return "상대의 디지몬과 테이머의 색 1색마다";
            if ((m = Regex.Match(s, $@"^(?<a>{Colors})(?: and/or| or|,) (?<b>{Colors}) Tamer you have(?: in play)?$", IC)).Success)
                return $"{ColorKo(m.Groups["a"].Value)}/{ColorKo(m.Groups["b"].Value)}인 자신의 테이머 1명마다";
            if (Regex.IsMatch(s, @"^Digimon your opponent has with no digivolution cards$", IC)) return "진화원을 갖지 않은 상대의 디지몬 1마리마다";
            if (Regex.IsMatch(s, @"^Tamer(?: in play)?$", IC)) return "자신과 상대의 테이머 1명마다";
            if ((m = Regex.Match(s, @"^of this Digimon's level (?<lv>\d+) digivolution cards$", IC)).Success) return $"이 디지몬의 Lv.{m.Groups["lv"].Value}인 진화원 1장마다";
            if (Regex.IsMatch(s, @"^of this Digimon's colou?rs?$", IC)) return "이 디지몬의 색 1색마다";
            if (Regex.IsMatch(s, @"^of your link cards$", IC)) return "자신의 링크 카드 1장마다";
            if ((m = Regex.Match(s, @"^(?:card|one) (?:this effect )?(?<v>trashed|placed|returned)(?: by this effect)?$", IC)).Success)
                return m.Groups["v"].Value.ToLowerInvariant() switch { "trashed" => "이 효과로 파기한 1장마다", "placed" => "이 효과로 놓은 1장마다", _ => "이 효과로 되돌린 1장마다" };
            if ((m = Regex.Match(s, $@"^of your (?:(?<c>{Colors}) )?Tamers with different names$", IC)).Success)
                return $"명칭이 서로 다른 {(m.Groups["c"].Success ? ColorKo(m.Groups["c"].Value) + "인 " : "")}자신의 테이머 1명마다";
            if (Regex.IsMatch(s, @"^Option cards? in (?:you|your) and your opponent's trash(?:es)?$", IC)) return "서로의 트래시의 옵션 카드 1장마다";
            if ((m = Regex.Match(s, @"^(?:digivolution card of (?:1|one) of your (?<x>\[[^\]]+\])s?|of (?:1|one) of your (?<x>\[[^\]]+\])(?:'s|s') digivolution cards)$", IC)).Success)
                return $"자신의 {NamesKo(m.Groups["x"].Value)} 1마리의 진화원 1장마다";
            if ((m = Regex.Match(s, @"^(?<other>other )?(?<x>(?:[^,]*? )?Digimon with .+?) you have(?: in play)?$", IC)).Success
                && TargetKo("all of your " + (m.Groups["other"].Success ? "other " : "") + m.Groups["x"].Value, bare: true) is { } tk)
                return Regex.Replace(tk, @"\s*전부$", "") + " 1마리마다";
            if ((m = Regex.Match(s, @"^(?<a>.+?) and each (?<b>.+)$", IC)).Success)
            {
                string a = m.Groups["a"].Value, b = m.Groups["b"].Value;
                string tail = Regex.Match(b, @"(?: you have)? in play$", IC).Value;
                if (!a.StartsWith("of ", StringComparison.OrdinalIgnoreCase) && tail.Length > 0 && !Regex.IsMatch(a, @"\byou have\b", IC)) a += tail.Contains("you have") ? tail : " you have" + tail;
                var ka = CounterKo(a) ?? CounterKo(Regex.Replace(a, @" in play$", "", IC));
                var kb = CounterKo(b) ?? CounterKo(Regex.Replace(b, @" in play$", "", IC));
                if (ka != null && kb != null) return Regex.Replace(ka, @" 1(?:마리|명|장)마다$", "") + "과 " + kb;
            }
            return null;
        }
        finally { _counterDepth42--; }
    }

    /// <summary>v0.42.0 조건 — 공식 BT14-101 "명칭에 「신태일」을 포함하는 자신의 테이머와 DP 10000 이상의 상대의 디지몬이 있는 동안"</summary>
    private static Cond? CondKo42(string cond)
    {
        var m = Regex.Match(cond.Trim(), @"^you have a Tamer with (?<x>\[[^\]]+\]) in its name and your opponent has a (?:Digimon with (?<dp>\d+) DP or more|(?<dp>\d+) DP or higher Digimon)(?: in play)?$", IC);
        if (!m.Success) return null;
        string x = NamesKo(m.Groups["x"].Value);
        return Have($"명칭에 {x}{J(x, "을", "를")} 포함하는 자신의 테이머와 DP {m.Groups["dp"].Value} 이상의 상대의 디지몬이");
    }
}
