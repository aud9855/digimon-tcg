using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.44.0 18차 묶음의 생성 한글 — 절 · 문장 · 비용 · 조건 · 셈 · 사건 (공식 한글 문구를 따른다)</summary>
public static partial class KoreanEffectRenderer
{
    private static string? ClauseKo44(string clause, string rawClause)
    {
        Match m;
        // 한 문장 절을 18차 문장 한글로 바로 (원래 렌더러가 문장 앞에서 다른 표기로 바꿔 놓쳐 버리는 문장 — BT9-106 "~ by paying its memory cost")
        if ((m = Regex.Match(Regex.Replace(rawClause.Trim(), @"^(?:Inherited|Security) Effect\s*", "", IC), @"^(?<labels>(?:\[[^\]]+\]\s*)+)(?<body>[^.""]+?)\.?$", IC)).Success && SentenceKo44(m.Groups["body"].Value.Trim(), false) is { } one)
            return $"{LabelsKo40(m.Groups["labels"].Value)}{one}.";
        // 옵션 카드의 색 조건 줄 (BT26-080 · BT27-085)
        var oc = Regex.Match(clause, @"^Option Card Colou?r Requirements? (?<c>(?:(?:Red|Blue|Yellow|Green|Black|Purple|White)[ /,]*)+)\.?$", IC);
        if (oc.Success)
            return "옵션 카드 색 조건: " + string.Join("/", Regex.Matches(oc.Groups["c"].Value, @"Red|Blue|Yellow|Green|Black|Purple|White", IC).Select(x => ColorKo(x.Value)));
        // 공식 BT15-057 "【서로의 턴】이 디지몬의 진화원에 「누메몬」/「X항체」가 있는 동안, 이 디지몬은 「【소멸 시】~」의 효과를 얻는다"
        if ((m = SentenceParser.WhileGainsOnDeletionRe44.Match(Regex.Replace(clause.Trim(), "[”“]", "\""))).Success
            && Sentence(m.Groups["body"].Value.Trim(), true) is { } body)
            return $"{(clause.StartsWith("[All Turns]", StringComparison.OrdinalIgnoreCase) ? "【서로의 턴】" : "")}이 디지몬의 진화원에 {NamesKo(m.Groups["x"].Value)}{J(NamesKo(m.Groups["x"].Value), "이", "가")} 있는 동안, 이 디지몬은 「【소멸 시】{body.TrimEnd('.')}.」의 효과를 얻는다.";
        // 공식 BT3-109 "이 턴 동안, 자신의 디지몬 1마리는 「【소멸 시】 ~」의 효과를 얻는다" — 따옴표를 고친 문장 (SentenceParser.ClauseRewrites44)
        string fixedQ = SentenceParser.ClauseRewrites44(clause).FirstOrDefault() ?? clause;
        if ((m = Regex.Match(fixedQ, "^(?<labels>(?:\\[[^\\]]+\\]\\s*)*)(?<t>[^\"]+?) gains? \"(?<q>[^\"]+)\"(?<dur> for the turn| until [^\"]+)?\\.?$", IC)).Success
            && TargetKo(m.Groups["t"].Value) is { } gt && (RenderClause(m.Groups["q"].Value) ?? NoOnPlayKo44(m.Groups["q"].Value)) is { } gq)
        {
            var (dur, _) = SplitDuration(m.Groups["dur"].Value);
            return $"{LabelsKo40(m.Groups["labels"].Value)}{(dur != null ? dur + ", " : "")}{gt}{J(gt, "은", "는")} 「{gq.TrimEnd('.')}.」의 효과를 얻는다.";
        }
        // 두 조건의 지속 효과 — "A 또는 B인 동안, ~"
        if ((m = Regex.Match(clause, @"^(?<labels>(?:\[[^\]]+\]\s*)*)While (?<a>[^,]+), (?<op>or|and) (?<b>[^,]+), (?<rest>(?:this Digimon|it|all of your) .+)$", IC)).Success
            && CondKo(m.Groups["a"].Value) is { } wa && CondKo(m.Groups["b"].Value) is { } wb
            && RenderClause(Cap(m.Groups["rest"].Value)) is { } wr)
        {
            bool or = m.Groups["op"].Value.Equals("or", StringComparison.OrdinalIgnoreCase);
            return $"{LabelsKo40(m.Groups["labels"].Value)}{wa.While.TrimEnd(',')} {(or ? "또는" : "그리고")} {wb.While} {wr}";
        }
        // 상대의 디지몬 제한 (SentenceParser.ParseStatic44) — "진화원을 갖지 않는 상대의 디지몬 전부는 어택과 블록을 할 수 없다"
        if ((m = Regex.Match(clause, @"^(?<labels>(?:\[[^\]]+\]\s*)*)(?:(?:While|If) (?<cond>.+?), )?(?:none of your opponent's (?<t>.+?) can|your opponent's (?<t>.+?) can't) (?<v>attack players|attack or block|attack|block|suspend)\.?$", IC)).Success
            && TargetKo("all of your opponent's " + m.Groups["t"].Value) is { } rt)
        {
            string cond = "";
            if (m.Groups["cond"].Success)
            {
                if (CondKo(m.Groups["cond"].Value) is not { } ck) return null;
                cond = ck.While + " ";
            }
            string v = m.Groups["v"].Value.ToLowerInvariant() switch
            {
                "attack players" => "플레이어에게 어택할 수 없다",
                "attack or block" => "어택과 블록을 할 수 없다",
                "attack" => "어택할 수 없다",
                "block" => "블록할 수 없다",
                _ => "레스트할 수 없다",
            };
            return $"{LabelsKo40(m.Groups["labels"].Value)}{cond}{rt}{J(rt, "은", "는")} {v}.";
        }
        // 공식 BT16-101 "【서로의 턴】이 디지몬의 진화원에 「라피드몬」/「X항체」가 있는 동안, 레스트 상태인 상대의 디지몬 전부를 DP -4000"
        if ((m = Regex.Match(clause, @"^(?<labels>(?:\[[^\]]+\]\s*)*)(?:While (?<cond>.+?), )?(?<who>all of your opponent's (?:[^,]*? )?Digimon(?: with [^,]+?)?) gets? (?<dp>[+-]\d+) DP\.?$", IC)).Success
            && !m.Groups["who"].Value.Contains("Security", StringComparison.OrdinalIgnoreCase)
            && TargetKo(m.Groups["who"].Value) is { } who)
        {
            string cond = "";
            if (m.Groups["cond"].Success)
            {
                if (CondKo(m.Groups["cond"].Value) is not { } ck) return null;
                cond = ck.While + " ";
            }
            return $"{LabelsKo40(m.Groups["labels"].Value)}{cond}{who}{J(who, "을", "를")} DP {m.Groups["dp"].Value}";
        }
        return null;
    }

    private static readonly string ColorAlt44 = "red|blue|yellow|green|black|purple|white";

    /// <summary>테이머 조건의 한글: "non-purple" → "퍼플 이외의" · "blue or yellow" → "블루/옐로인" · "with a play cost of 3 or less" → "등장 코스트 3 이하의"</summary>
    private static string? TamerAdjKo44(string adj, string with)
    {
        adj = adj.Trim(); with = with.Trim();
        var parts = new List<string>();
        Match m;
        if (adj.Length > 0)
        {
            if ((m = Regex.Match(adj, $@"^non-(?<c>{ColorAlt44})$", IC)).Success) parts.Add($"{ColorKo(m.Groups["c"].Value)} 이외의");
            else if (Regex.IsMatch(adj, $@"^(?:(?:{ColorAlt44})(?:,\s*|\s+or\s+|\s+and/or\s+)?)+$", IC))
                parts.Add(string.Join("/", Regex.Matches(adj, ColorAlt44, IC).Select(x => ColorKo(x.Value))) + "인");
            else if (Regex.IsMatch(adj, "^suspended$", IC)) parts.Add("레스트 상태인");
            else if (Regex.IsMatch(adj, "^unsuspended$", IC)) parts.Add("액티브 상태인");
            else return null;
        }
        if (with.Length > 0)
        {
            if ((m = Regex.Match(with, @"^with a play cost of (?<n>\d+) or (?<d>less|lower|more|higher)$", IC)).Success)
                parts.Add($"등장 코스트 {m.Groups["n"].Value} {(m.Groups["d"].Value is "less" or "lower" ? "이하" : "이상")}의");
            else return null;
        }
        return string.Join(" ", parts);
    }

    private static string? SentenceKo44(string s, bool nested)
    {
        Match m;
        string then = "";
        if ((m = Regex.Match(s, @"^Then,?\s+(?<r>.+)$", IC)).Success) { then = "그 후, "; s = char.ToUpperInvariant(m.Groups["r"].Value[0]) + m.Groups["r"].Value[1..]; }
        string? ko = SentenceCoreKo44(s, nested);
        return ko == null ? null : then + ko;
    }

    private static string? SentenceCoreKo44(string s, bool nested)
    {
        Match m;
        // 비용 뒤 결과 조건 (SentenceParser.IfNotMark44로 합친 문장)
        if ((m = Regex.Match(s, $@"^(?<a>.+?) {Regex.Escape(SentenceParser.IfNotMark44)} (?<b>.+)$", IC)).Success
            && Sentence(m.Groups["a"].Value, true) is { } ka && Sentence(m.Groups["b"].Value, true) is { } kb)
            return $"{ka.TrimEnd('.')}. {kb}";
        // 공식 P-044 "상대의 디지몬 1마리, 또는 DP 5000 이하의 상대 디지몬 2마리를 레스트시킨다"
        if ((m = Regex.Match(s, @"^(?<v>Suspend|Delete) (?<a>(?:up to )?\d+ of your opponent's [^,]+?), or (?<b>(?:up to )?\d+ of your opponent's .+)$", IC)).Success
            && TargetKo(m.Groups["a"].Value) is { } ta && TargetKo(m.Groups["b"].Value) is { } tb)
            return $"{ta} 또는, {tb}{J(tb, "을", "를")} {(m.Groups["v"].Value.Equals("Suspend", StringComparison.OrdinalIgnoreCase) ? "레스트시킨다" : "소멸시킨다")}";
        // 양쪽의 테이머 — 공식 LM-010 "테이머 1명을 레스트시킨다" · BT12-111 "테이머 전부를 패로 되돌린다" · BT18-080 "블루/옐로/블랙/화이트인 등장 코스트 3 이하의 테이머 1명을 소멸시킨다"
        if ((m = Regex.Match(s, $@"^(?<v>Suspend|Delete|Unsuspend|Return) (?<n>1|all|up to \d+) (?<adj>(?:non-(?:{ColorAlt44})|(?:(?:{ColorAlt44})(?:,\s*|\s+or\s+|\s+and/or\s+)?)+|suspended|unsuspended)\s+)?Tamers?(?<with> with a play cost of \d+ or (?:less|lower|more|higher))?(?<to> to (?:their owners'|its owner's|the|their) hands?| to the bottom of (?:their owners'|its owner's|the|their) decks?)?$", IC)).Success
            && TamerAdjKo44(m.Groups["adj"].Value, m.Groups["with"].Value) is { } adjKo)
        {
            string n = m.Groups["n"].Value.ToLowerInvariant();
            string cnt = n == "all" ? "전부" : n.StartsWith("up to") ? $"{Regex.Match(n, @"\d+").Value}명까지" : $"{n}명";
            string obj = $"{(adjKo.Length > 0 ? adjKo + " " : "")}테이머 {cnt}";
            string verb = m.Groups["v"].Value.ToLowerInvariant() switch
            {
                "suspend" => "레스트시킨다",
                "delete" => "소멸시킨다",
                "unsuspend" => "액티브로 한다",
                _ => m.Groups["to"].Value.Contains("hand", StringComparison.OrdinalIgnoreCase) ? "패로 되돌린다" : "덱 아래로 되돌린다",
            };
            return $"{obj}{J(obj, "을", "를")} {verb}";
        }
        // 공식 BT12-057 "이 디지몬 이외의 디지몬 전부와, 서로의 테이머 전부를 레스트시킨다" · EX10-023 "다른 디지몬과 테이머 전부를 레스트시킨다"
        if ((m = Regex.Match(s, @"^(?<v>Suspend|Unsuspend) all other Digimon and (?<all>all )?Tamers$", IC)).Success)
        {
            string verb = m.Groups["v"].Value.Equals("Suspend", StringComparison.OrdinalIgnoreCase) ? "레스트시킨다" : "액티브로 한다";
            return m.Groups["all"].Success ? $"이 디지몬 이외의 디지몬 전부와, 서로의 테이머 전부를 {verb}" : $"다른 디지몬과 테이머 전부를 {verb}";
        }
        // 공식 BT9-069 "디지몬과 테이머 2장까지를 액티브로 한다"
        if ((m = Regex.Match(s, @"^(?<v>Suspend|Unsuspend) (?<up>up to )?(?<n>\d+) Digimon and/or Tamers$", IC)).Success)
            return $"디지몬과 테이머 {m.Groups["n"].Value}장{(m.Groups["up"].Success ? "까지" : "")}를 {(m.Groups["v"].Value.Equals("Suspend", StringComparison.OrdinalIgnoreCase) ? "레스트시킨다" : "액티브로 한다")}";
        // 공식 BT15-101 "상대의 턴 종료까지 상대의 디지몬/테이머 3마리(명)는 레스트할 수 없다" · LM-010 "상대의 턴 종료까지 상대의 테이머 전부는 액티브가 되지 않는다"
        if ((m = Regex.Match(s, @"^(?<n>\d+|all) of (?:your opponent's|their) (?<k>Tamers|Digimon and Tamers) (?:can't|cannot) (?<v>unsuspend|suspend)(?<dur> .+)$", IC)).Success)
        {
            var (dur, rest) = SplitDuration(m.Groups["dur"].Value);
            if (dur == null || rest.Trim().Length > 0) return null;
            string n = m.Groups["n"].Value;
            bool all = n.Equals("all", StringComparison.OrdinalIgnoreCase);
            string who = m.Groups["k"].Value.StartsWith("Digimon", StringComparison.OrdinalIgnoreCase)
                ? $"상대의 디지몬/테이머 {(all ? "전부" : n + "마리(명)")}"
                : $"상대의 테이머 {(all ? "전부" : n + "명")}";
            bool un = m.Groups["v"].Value.Equals("unsuspend", StringComparison.OrdinalIgnoreCase);
            return $"{dur} {who}{J(who, "은", "는")} {(un ? "액티브가 되지 않는다" : "레스트할 수 없다")}";
        }
        // 공식 BT15-086 "특징으로 「머신형」/「사이보그형」/「SoC」를 가진 자신의 디지몬 1마리에게 《마인드 링크》" · EX11-070 "이 테이머는 ~ 자신의 디지몬 1마리에게 《마인드 링크》할 수 있다"
        if ((m = Regex.Match(s, @"^(?<may>(?:This Tamer|you) may )?＜Mind Link＞ (?:to|with) (?<t>(?:1|one) of your Digimon with .+)$", IC)).Success
            && TargetKo(Regex.Replace(m.Groups["t"].Value, @"\bwith (?<x>\[[^\]]+\](?:,\s*\[[^\]]+\])*(?:,? or \[[^\]]+\])) trait$", "with the ${x} trait", IC)) is { } mt)
        {
            string head = m.Groups["may"].Value.StartsWith("This Tamer", StringComparison.OrdinalIgnoreCase) ? "이 테이머는 " : "";
            return $"{head}{mt}에게 《마인드 링크》{(m.Groups["may"].Success ? "할 수 있다" : "")} (이 테이머를 진화원에 테이머 카드가 없는 디지몬의 진화원 아래에 놓는다)";
        }
        // 공식 BT13-077 "상대는, 선택한 디지몬을 어택시킨다"
        if (Regex.IsMatch(s, @"^Your opponent (?:must )?attacks? with (?:the chosen Digimon|it|that Digimon)$", IC))
            return "상대는, 선택한 디지몬을 어택시킨다";
        // 공식 BT17-075 "상대는 본인의 패에서 테이머 카드 1장을 코스트를 지불하지 않고 등장시킬 수 있다"
        if (Regex.IsMatch(s, @"^Your opponent may play 1 Tamer card from their hand without paying (?:the|its)(?: memory)? cost$", IC))
            return "상대는 본인의 패에서 테이머 카드 1장을 코스트를 지불하지 않고 등장시킬 수 있다";
        // 공식 BT8-019 "상대는 상대의 디지몬 1마리를 고른다. 이 디지몬과 고른 상대의 디지몬 이외의 서로의 디지몬 전부를 소멸시킨다"
        if (Regex.IsMatch(s, @"^Your opponent chooses 1 of their Digimon$", IC)) return "상대는 본인의 디지몬 1마리를 고른다";
        if (Regex.IsMatch(s, @"^Delete all Digimon other than this Digimon and your opponent's chosen Digimon$", IC)) return "이 디지몬과 고른 상대의 디지몬 이외의 서로의 디지몬 전부를 소멸시킨다";
        if (Regex.IsMatch(s, @"^Delete all of their other Digimon$", IC)) return "선택한 디지몬 이외의 상대의 디지몬 전부를 소멸시킨다";
        if ((m = Regex.Match(s, @"^For (?:each|every) Digimon deleted by this effect, (?<core>.+)$", IC)).Success && Sentence(Cap(m.Groups["core"].Value), true) is { } fe)
            return $"이 효과로 소멸한 디지몬 1마리마다 {fe}";
        // 공식 EX4-069 "가장 등장 코스트가 높은 서로의 디지몬 1마리씩을 선택한다. 선택하지 않은 디지몬 전부를 소멸시킨다" · BT20-102 "서로의 디지몬 1마리씩을 선택하고, 선택한 디지몬 이외의 디지몬 전부를 소멸시킨다"
        if ((m = SentenceParser.ChooseEachDeleteRe44.Match(s)).Success)
            return m.Groups["hi"].Success ? "가장 등장 코스트가 높은 서로의 디지몬 1마리씩을 선택하고, 선택하지 않은 디지몬 전부를 소멸시킨다" : "서로의 디지몬 1마리씩을 선택하고, 선택한 디지몬 이외의 디지몬 전부를 소멸시킨다";
        // 《진격》 (문장 안)
        if (Regex.IsMatch(s, @"^＜Blitz＞$", IC)) return KeywordKo("Blitz", withReminder: true);
        // 공식 EX4-059 "상대의 턴 종료까지 이 디지몬과 Lv.5 이하의 자신의 디지몬 1마리는 「【소멸 시】이 카드를 코스트를 지불하지 않고 등장시킬 수 있다.」의 효과를 얻는다"
        if ((m = Regex.Match(s, "^(?<pre>Until [^,\"]+, )?this Digimon and (?<t>1 of your [^\"]+?) gain \"(?<q>[^\"]+)\"(?<post> (?:until|for) [^\"]+)?$", IC)).Success
            && TargetKo(m.Groups["t"].Value) is { } t2 && RenderClause(m.Groups["q"].Value) is { } q2)
        {
            var (dur, _) = SplitDuration(m.Groups["pre"].Success ? m.Groups["pre"].Value.Trim().TrimEnd(',') : m.Groups["post"].Value);
            return $"{(dur != null ? dur + " " : "")}이 디지몬과 {t2}{J(t2, "은", "는")} 「{q2.TrimEnd('.')}.」의 효과를 얻는다";
        }
        // 【소멸 시】 "Play this card from your trash without paying its memory cost" (BT2-083)
        if ((m = Regex.Match(s, @"^(?<may>you may )?play this card from (?:your|the) trash without paying (?:the|its)(?: memory| play)? cost$", IC)).Success)
            return $"트래시의 이 카드를 코스트를 지불하지 않고 등장시{(m.Groups["may"].Success ? "킬 수 있다" : "킨다")}";
        // 시큐리티 디지몬의 DP — 공식 BT15-092 "자신의 턴 종료까지 상대의 디지몬과 시큐리티 디지몬 전부를 DP -5000" · BT9-043 "자신의 시큐리티 1장마다 이 턴 동안 상대의 디지몬과 시큐리티 디지몬 전부를 DP-1000"
        if ((m = Regex.Match(s, @"^(?:For (?:each|every) (?<cnt>[^,]+), (?<core>.+)|(?<core>.+? (?:for the turn|until [^.]+?)) for (?:each|every) (?<cnt>.+))$", IC)).Success
            && SentenceParser.SecDpRe44.IsMatch(Regex.Replace(m.Groups["core"].Value, @"\band security Digimon\b", "and Security Digimon"))
            && CounterKo(m.Groups["cnt"].Value) is { } sck && SecDpKo44(Regex.Replace(m.Groups["core"].Value, @"\band security Digimon\b", "and Security Digimon")) is { } sk)
            return $"{sck} {sk}";
        if (SecDpKo44(Regex.Replace(s, @"\band security Digimon\b", "and Security Digimon")) is { } sdk) return sdk;
        // 공식 BT16-069 "상대의 디지몬/테이머 1마리(명) 아래에서 선택하여 3장 파기한다" · LM-005 "파기한 1장마다 상대의 디지몬/테이머 아래에서 선택하여 1장 파기한다"
        if ((m = Regex.Match(s, @"^For (?:each one|each card trashed(?: by this effect)?), (?<core>.+)$", IC)).Success && SentenceParser.TrashUnderRe44.IsMatch(m.Groups["core"].Value)
            && TrashUnderKo44(m.Groups["core"].Value, perOne: true) is { } tu1)
            return $"이 효과로 파기한 1장마다 {tu1}";
        if (TrashUnderKo44(s, perOne: false) is { } tu) return tu;
        // 두 조건 (SentenceParser.MoreStep44): "If A, or if B, X" · "If A, and B, X" · "If this Digimon has [X] in its name, or the [Y] trait, X"
        if ((m = Regex.Match(s, @"^If (?<a>[^,]+), (?<op>or if|or|and) (?<b>[^,]+), (?<rest>.+)$", IC)).Success)
        {
            string a = m.Groups["a"].Value, b = m.Groups["b"].Value;
            if (Regex.IsMatch(b, @"^the \[", IC) && Regex.Match(a, @"^(?<subj>this Digimon) has ", IC) is { Success: true } sm) b = $"{sm.Groups["subj"].Value} has {b}";
            if (CondKo(a) is { } kca && CondKo(b) is { } kcb && Sentence(Cap(m.Groups["rest"].Value), true) is { } kcr)
            {
                bool or = m.Groups["op"].Value.StartsWith("or", StringComparison.OrdinalIgnoreCase);
                return $"{kca.Then.TrimEnd(',')} {(or ? "또는" : "그리고")} {kcb.Then} {kcr}";
            }
        }
        if ((m = Regex.Match(s, @"^For each of your opponent's Digimon suspended by this effect, (?<core>.+)$", IC)).Success && Sentence(Cap(m.Groups["core"].Value), true) is { } fsk)
            return $"이 효과로 레스트한 상대의 디지몬 1마리마다 {fsk}";
        // 두 장 등장 (BT16-083 · EX2-012)
        if (((m = Regex.Match(s, @"^(?<may>you may )?play (?<a>1 [^,]+?) from your (?<za>hand|trash) and (?<b>1 [^,]+?) from your (?<zb>hand|trash) without paying the(?:ir)? (?:memory )?costs?$", IC)).Success
             || (m = Regex.Match(s, @"^(?<may>you may )?play (?<a>1 \[[^\]]+\]) and (?<b>1 \[[^\]]+\]) from your (?<za>hand and/or trash|hand or trash) without paying the(?:ir)? (?:memory )?costs?$", IC)).Success)
            && PickKo(m.Groups["a"].Value) is { } pa && PickKo(m.Groups["b"].Value) is { } pb)
        {
            static string Z(string z) => z.Contains("trash", StringComparison.OrdinalIgnoreCase) && z.Contains("hand", StringComparison.OrdinalIgnoreCase) ? "자신의 패 또는 트래시에서" : z.Contains("trash", StringComparison.OrdinalIgnoreCase) ? "자신의 트래시에서" : "자신의 패에서";
            string za = m.Groups["za"].Value, zb = m.Groups["zb"].Success ? m.Groups["zb"].Value : za;
            string body = za == zb ? $"{Z(za)} {pa}{J(pa, "과", "와")} {pb}{J(pb, "을", "를")}" : $"{Z(za)} {pa}{J(pa, "과", "와")} {Z(zb)} {pb}{J(pb, "을", "를")}";
            return $"{body} 코스트를 지불하지 않고 등장시{(m.Groups["may"].Success ? "킬 수 있다" : "킨다")}";
        }
        // 그 밖의 작은 동작 (SentenceParser.MoreAction44)
        if (Regex.IsMatch(s, @"^Return 1 card from your hand to the top of your deck$", IC)) return "자신의 패 1장을 덱 위로 되돌린다";
        if ((m = Regex.Match(s, @"^Draw cards until there are (?<n>\d+) cards in your hand$", IC)).Success) return $"자신의 패가 {m.Groups["n"].Value}장이 될 때까지 드로우한다";
        if (Regex.IsMatch(s, @"^Trash your top or bottom security card$", IC)) return "자신의 시큐리티를 위 또는 아래에서 1장 파기한다";
        if ((m = Regex.Match(s, @"^gain (?<n>\d+) memory if that Digimon is level (?<a>\d+) or (?<d>higher|lower), and ＜Draw (?<dr>\d+)＞ if it is level (?<lv>\d+)$", IC)).Success)
            return $"그 디지몬이 Lv.{m.Groups["a"].Value} {(m.Groups["d"].Value.Equals("higher", StringComparison.OrdinalIgnoreCase) ? "이상" : "이하")}이라면 메모리 +{m.Groups["n"].Value}, Lv.{m.Groups["lv"].Value}이라면 《{m.Groups["dr"].Value} 드로우》";
        if ((m = Regex.Match(s, @"^(?<v>Return|Delete|Suspend) 1 of your opponent's level (?<a>\d+), 1 of their level (?<b>\d+) and 1 of their level (?<c>\d+) Digimon(?<to> to .+)?$", IC)).Success)
        {
            string obj = $"상대의 Lv.{m.Groups["a"].Value} 디지몬 1마리, Lv.{m.Groups["b"].Value} 디지몬 1마리, Lv.{m.Groups["c"].Value} 디지몬 1마리";
            string verb = m.Groups["v"].Value.ToLowerInvariant() switch { "delete" => "소멸시킨다", "suspend" => "레스트시킨다", _ => m.Groups["to"].Value.Contains("hand", StringComparison.OrdinalIgnoreCase) ? "패로 되돌린다" : "덱 아래로 되돌린다" };
            return $"{obj}를 {verb}";
        }
        if ((m = Regex.Match(s, @"^Return (?<a>1 of your opponent's .+?Digimon) and (?<b>1 of their .+?Tamers) to the bottom of their owners' decks$", IC)).Success
            && TargetKo(m.Groups["a"].Value) is { } ra)
        {
            string tamerKo = Regex.IsMatch(m.Groups["b"].Value, @"\bsuspended\b", IC) ? "레스트 상태인 상대의 테이머 1명" : "상대의 테이머 1명";
            return $"{ra}{J(ra, "과", "와")} {tamerKo}을 덱 아래로 되돌린다";
        }
        if ((m = Regex.Match(s, @"^(?<may>you may )?trash (?:(?<up>up to )|any )?(?<n>\d+) (?<kind>digivolution cards?|Option cards?) (?:of|from) (?<who>1 of your opponent's Digimon|your opponent's Digimon|1 Digimon's digivolution cards)$", IC)).Success)
        {
            string who = m.Groups["who"].Value.ToLowerInvariant();
            string from = who.StartsWith("1 of") ? "상대의 디지몬 1마리의 진화원에서" : who.Contains("1 digimon's") ? "디지몬 1마리의 진화원에서" : "상대의 디지몬의 진화원에서";
            string what = m.Groups["kind"].Value.StartsWith("Option", StringComparison.OrdinalIgnoreCase) ? "옵션 카드" : "카드";
            return $"{from} 선택하여 {what} {m.Groups["n"].Value}장{(m.Groups["up"].Success ? "까지" : "")}을 파기{(m.Groups["may"].Success ? "할 수 있다" : "한다")}";
        }
        // 공식 BT9-106 "자신의 디지몬 1마리를, 트래시의 명칭에 「데크스」를 포함하는 디지몬 카드 1장으로 코스트를 지불하여 진화시킨다"
        if ((m = Regex.Match(s, @"^Digivolve (?<t>1 of your Digimon|this Digimon) into (?<p>.+?) from your (?<z>trash|hand) by paying (?:its|the) (?:memory |digivolution )?cost$", IC)).Success
            && TargetKo(m.Groups["t"].Value) is { } dt
            && (PickKo("1 " + Regex.Replace(m.Groups["p"].Value, @"^(?:an?|1) ", "", IC))
                ?? (Regex.Match(m.Groups["p"].Value, $@"^an? Digimon card with (?<x>{TargetSpec.NameList}) in its name$", IC) is { Success: true } pn
                    ? $"명칭에 {NamesKo(pn.Groups["x"].Value)}{J(NamesKo(pn.Groups["x"].Value), "을", "를")} 포함하는 디지몬 카드 1장" : null)) is { } dp)
            return $"{dt}{J(dt, "을", "를")}, {(m.Groups["z"].Value.Equals("trash", StringComparison.OrdinalIgnoreCase) ? "트래시" : "패")}의 {dp}{J(dp, "으로", "로")} 코스트를 지불하여 진화시킨다";
        if (Regex.IsMatch(s, @"^1 of your Digimon may change (?:its )?orientation$", IC))
            return "자신의 디지몬 1마리의 방향을 바꿀 수 있다 (액티브 ↔ 레스트)";
        // 공식 BT19-075 "상대는 본인의 패가 5장이 되도록 패를 파기한다" · EX2-043 "서로는 각각 패가 5장이 되도록 파기한다" · BT20-077 "자신의 패가 4장이 되도록 파기한다"
        if ((m = SentenceParser.HandTrimRe44.Match(s)).Success)
        {
            string who = m.Groups["who"].Value.ToLowerInvariant(), n = m.Groups["n"].Value;
            return who.StartsWith("your opponent") ? $"상대는 본인의 패가 {n}장이 되도록 패를 파기한다"
                : who.StartsWith("both") || who.StartsWith("all") ? $"서로는 각각 패가 {n}장이 되도록 파기한다"
                : $"자신의 패가 {n}장이 되도록 파기한다";
        }
        // 공식 BT12-106 "상대의 카드 전부는 다음 상대의 액티브 페이즈에서는, 액티브가 되지 않는다"
        if (Regex.IsMatch(s, @"^(?:Your opponent's cards|All of your opponent's Digimon and Tamers) (?:don't|can't) unsuspend during their next unsuspend phase$", IC))
            return "상대의 카드 전부는 다음 상대의 액티브 페이즈에서는, 액티브가 되지 않는다";
        if (Regex.IsMatch(s, @"^Cards this effect suspended (?:can't|don't) unsuspend during your opponent's next unsuspend phase$", IC))
            return "다음 상대의 액티브 페이즈에서는, 이 효과로 레스트한 카드는 액티브가 되지 않는다";
        // 공식 BT19-075 "이 효과로 파기한 2장마다 상대의 테이머 1명을 소멸시킨다"
        if ((m = Regex.Match(s, @"^For (?:each|every) (?<n>\d+) cards? (?:trashed by this effect|this effect trashed), (?<core>.+)$", IC)).Success && Sentence(Cap(m.Groups["core"].Value), true) is { } fen)
            return $"이 효과로 파기한 {m.Groups["n"].Value}장마다 {fen}";
        // 공식 BT20-084 "자신의 「시스터몬 시엘」 1마리를 이 카드로 코스트를 지불하지 않고 진화시킬 수 있다"
        if ((m = Regex.Match(s, @"^(?<t>1 of your .+?) may digivolve into this card(?: (?<free>without paying the cost)| with the digivolution cost reduced by (?<red>\d+))?$", IC)).Success
            && TargetKo(Regex.Replace(m.Groups["t"].Value, @"(?<x>\])s$", "${x}")) is { } et)
            return $"{et}{J(et, "을", "를")} 이 카드로 {(m.Groups["free"].Success ? "코스트를 지불하지 않고 " : m.Groups["red"].Success ? $"진화 코스트를 -{m.Groups["red"].Value} 하여 " : "")}진화시킬 수 있다";
        // 공식 BT17-101 "자신의 디지몬 2마리로 이 카드로 조그레스 진화할 수 있다"
        if ((m = Regex.Match(s, @"^2 of your Digimon may DNA digivolve into this card(?<free> without paying the cost)?$", IC)).Success)
            return $"자신의 디지몬 2마리로 이 카드로 {(m.Groups["free"].Success ? "코스트를 지불하지 않고 " : "")}조그레스 진화할 수 있다";
        // 공식 EX1-066 "비어 있는 자신의 육성 에어리어에 디지타마 카드 1장을 부화시킨다"
        if (Regex.IsMatch(s, @"^Hatch (?:(?:1|a|one) Digi-Egg(?: card)? )?(?:in|into|to) (?:an empty space in )?your breeding area$", IC))
            return "비어 있는 자신의 육성 에어리어에 디지타마 카드 1장을 부화시킨다";
        // 공식 BT3-109 "이 효과로 등장한 디지몬의 【등장 시】 효과는 발휘하지 않는다"
        if (Regex.IsMatch(s, @"^(?:Any )?\[On Play\] effects on (?:Digimon|cards) played (?:with|by) this effect don't activate$", IC))
            return "이 효과로 등장한 디지몬의 【등장 시】 효과는 발휘하지 않는다";
        // 공식 BT17-074 "자신의 패에서 Lv.5 이하의 「에오스몬」 또는 화이트인 등장 코스트 4 이하의 테이머 카드 1장을 등장 코스트 2로 등장시킬 수 있다"
        if ((m = Regex.Match(s, @"^(?<may>you may )?play (?<p>\d+ .+?) from your hand for a (?:play |memory )?cost of (?<n>\d+)$", IC)).Success && PickKo(m.Groups["p"].Value) is { } pp)
            return $"자신의 패에서 {pp}{J(pp, "을", "를")} 등장 코스트 {m.Groups["n"].Value}{Ro42(m.Groups["n"].Value)} 등장시{(m.Groups["may"].Success ? "킬 수 있다" : "킨다")}";
        // 두 동작 (SentenceParser.CompoundAction44): "Gain 1 memory, then hatch ~" · "delete 1 Tamer, and unsuspend this Digimon" · "suspend this Tamer and ＜Draw 1＞"
        foreach (Match sep in Regex.Matches(s, @"(?:,? and then|, then|,? and|,) (?=hatch\b|have\b|＜)|(?<=^(?:trash your top or bottom security card|return 1 card from your hand to the top of your deck))(?: and|,) (?=gain\b|＜)", IC))   // 원래 규칙이 옮기는 "A and B"는 건드리지 않는다 — 부화 · 키워드 · "have" · 새 동작 뒤만
        {
            string left = s[..sep.Index].Trim(), right = s[(sep.Index + sep.Length)..].Trim();
            if (left.Length == 0 || right.Length == 0 || Regex.IsMatch(right, @"^(?:\d+|all|up to \d+) of\b", IC)) continue;
            if (_compound44 > 0) break;
            _compound44++;
            try
            {
                if (Sentence(Cap(left), true) is { } kl && Sentence(Cap(right), true) is { } kr)
                    return $"{Conj38(kl)} {kr}";
            }
            finally { _compound44--; }
        }
        return null;
    }

    [ThreadStatic] private static int _compound44;

    /// <summary>"~. Any [On Play] effects on Digimon played with this effect don't activate." — 공식 BT3-109 "이 효과로 등장한 디지몬의 【등장 시】 효과는 발휘하지 않는다"</summary>
    private static string? NoOnPlayKo44(string q)
    {
        var m = Regex.Match(q.Trim(), @"^(?<a>.+?\.)\s+(?:Any )?\[On Play\] effects on (?:Digimon|cards) played (?:with|by) this effect don't activate\.?$", IC);
        if (!m.Success || RenderClause(m.Groups["a"].Value) is not { } a) return null;
        return $"{a.TrimEnd('.')}. 이 효과로 등장한 디지몬의 【등장 시】 효과는 발휘하지 않는다.";
    }

    private static string? TrashUnderKo44(string s, bool perOne)
    {
        var m = SentenceParser.TrashUnderRe44.Match(s.Trim().TrimEnd('.'));
        if (!m.Success) return null;
        string where = m.Groups["one"].Success ? "상대의 디지몬/테이머 1마리(명) 아래에서" : "상대의 디지몬/테이머 아래에서";
        return $"{where} 선택하여 {m.Groups["n"].Value}장 파기{(m.Groups["may"].Success ? "할 수 있다" : "한다")}";
    }

    private static string? SecDpKo44(string s)
    {
        var m = SentenceParser.SecDpRe44.Match(s.Trim().TrimEnd('.'));
        if (!m.Success) return null;
        var (dur, rest) = SplitDuration(m.Groups["dur"].Value);
        if (dur == null || rest.Trim().Length > 0) return null;
        string who = m.Groups["who"].Value.Trim();
        string dp = m.Groups["dp"].Value;
        string obj;
        if (!m.Groups["a"].Success)
            obj = $"{(who.Equals("your", StringComparison.OrdinalIgnoreCase) ? "자신" : "상대")}의 시큐리티 디지몬 전부";
        else if (who.Length == 0 && Regex.IsMatch(m.Groups["a"].Value, @"^all of (?:your opponent's|their) Digimon$", IC))
            obj = "상대의 디지몬과 시큐리티 디지몬 전부";
        else if (TargetKo(m.Groups["a"].Value) is { } ta && who.Length > 0)
            obj = $"{ta}{J(ta, "과", "와")} {(who.Equals("your", StringComparison.OrdinalIgnoreCase) ? "자신" : "상대")}의 시큐리티 디지몬 전부";
        else return null;
        return $"{dur} {obj}{J(obj, "을", "를")} DP {dp}";
    }

    [ThreadStatic] private static int _costKoDepth44;

    /// <summary>비용 (SentenceParser.ParseCost44와 같은 표기)</summary>
    private static string? CostKo44(string cost)
    {
        if (!KoGate44) return null;
        if (_costKoDepth44 > 0) return null;
        _costKoDepth44++;
        try
        {
            string s = cost.Trim();
            Match m;
            // 공식 BT13-080 "육성 에어리어의 Lv.2의 자신의 디지몬 1마리를 소멸시키는 것으로,"
            if ((m = Regex.Match(s, @"^deleting (?:1|one) of your level (?<lv>\d+) Digimon (?:in|from) (?:the|your) breeding area$", IC)).Success)
                return $"육성 에어리어의 Lv.{m.Groups["lv"].Value}의 자신의 디지몬 1마리를 소멸시키는 것으로,";
            if ((m = Regex.Match(s, @"^trashing (?<n>\d+) of your Digimon's link cards?$", IC)).Success)
                return $"자신의 디지몬의 링크 카드 {m.Groups["n"].Value}장을 파기하는 것으로,";
            if ((m = Regex.Match(s, @"^trashing (?<p>\d+ (?:Digimon |Option |Tamer )?cards? with (?:the )?\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])* traits?)$", IC)).Success
                && CostKo($"trashing {m.Groups["p"].Value} in your hand") is { } th)
                return th;
            if (Regex.IsMatch(s, @"^placing (?:1|one) of your Digimon cards with ", IC) && CostKo(Regex.Replace(s, @"^placing (?:1|one) of your Digimon cards with ", "placing 1 Digimon card with ", IC)) is { } pl)
                return pl;
            // 공식 EX3-035 "자신의 트래시에서 「홀리드라몬」과 「청룡몬」과 「메기드라몬」 1장씩을 덱 아래로 되돌리는 것으로,"
            if ((m = Regex.Match(s, @"^returning (?<list>1 \[[^\]]+\](?:,? (?:and )?1 \[[^\]]+\])+) from your trash to the bottom of (?:your|the) deck(?: in any order)?$", IC)).Success)
            {
                var names = Regex.Matches(m.Groups["list"].Value, @"\[[^\]]+\]").Select(x => NamesKo(x.Value)).ToList();
                string joined = string.Join("", names.Take(names.Count - 1).Select(n => n + J(n, "과", "와") + " ")) + names[^1];
                return $"자신의 트래시에서 {joined} 1장씩을 덱 아래로 되돌리는 것으로,";
            }
            if ((m = Regex.Match(s, @"^returning (?<t>1 of your opponent's .+?) to the bottom of (?:the|its owner's|their) deck$", IC)).Success && TargetKo(m.Groups["t"].Value) is { } ot)
                return $"{ot}{J(ot, "을", "를")} 덱 아래로 되돌리는 것으로,";
            return null;
        }
        finally { _costKoDepth44--; }
    }

    /// <summary>대상 (TargetSpec.Parse44와 같은 표기) — 공식 ST22-08 "자신의 디지몬 1마리의 DP 이하의 상대의 디지몬 1마리" · BT23-097 "자신의 패의 매수 이상의 Lv.인 상대의 디지몬 1마리"</summary>
    private static string? TargetKo44(string phrase, bool bare)
    {
        if (!KoGate44) return null;
        phrase = phrase.Trim().TrimEnd('.').Trim();
        Match m;
        // "1 of your non-white Digimon" — 공식 BT14-097 "화이트 이외의 자신의 디지몬 1마리"
        if ((m = Regex.Match(phrase, @"^(?<pre>.*?\bof your(?: opponent's)? )non-(?<c>red|blue|yellow|green|black|purple|white) (?<rest>Digimon.*)$", IC)).Success
            && TargetKo(m.Groups["pre"].Value + m.Groups["rest"].Value, bare) is { } nck)
            return $"{ColorKo(m.Groups["c"].Value)} 이외의 {nck}";
        if ((m = Regex.Match(phrase, @"^(?<pre>.+?Digimon) with (?<a>.+?) or (?<b>with .+)$", IC)).Success
            && TargetKo(m.Groups["pre"].Value + " with " + m.Groups["a"].Value, bare) is { } ka && TargetKo(m.Groups["pre"].Value + " " + m.Groups["b"].Value, bare) is { } kb)
            return $"{ka} 또는 {kb}";
        if ((m = Regex.Match(phrase, $@"^(?<pre>.+?Digimon) with (?<x>{TargetSpec.NameList}) in (?:any|one) of (?:its|their) traits$", IC)).Success
            && TargetKo(m.Groups["pre"].Value, bare) is { } kt)
        {
            string x = NamesKo(m.Groups["x"].Value, trait: true);
            return $"특징에 {x}{J(x, "을", "를")} 포함하는 {kt}";
        }
        // 공식 BT15-081 "자신의 「리바이어몬」 또는 진화원에 「X항체」가 있는 자신의 디지몬 1마리" · BT24-080 (이름 또는 특징)
        if ((m = Regex.Match(phrase, @"^(?<n>\d+) of your (?<a>\[[^\]]+\](?: or \[[^\]]+\])+) Digimon$", IC)).Success)
        {
            // 이름이 아니면 특징 (BT24-080 「마룡형」/「사룡형」)
            var parts = Regex.Matches(m.Groups["a"].Value, @"\[([^\]]+)\]").Select(x => x.Groups[1].Value).ToList();
            string ko = string.Join("/", parts.Select(x => NameKo(x) is { } nk && nk != x ? $"「{nk}」" : $"「{TraitKo(x)}」").Distinct());
            return $"자신의 {ko} 디지몬 {m.Groups["n"].Value}마리";
        }
        if ((m = Regex.Match(phrase, @"^(?<n>\d+) of your (?<a>\[[^\]]+\]) or (?<b>Digimon with .+)$", IC)).Success
            && TargetKo($"{m.Groups["n"].Value} of your {m.Groups["b"].Value}", bare) is { } ob)
            return $"자신의 {NamesKo(m.Groups["a"].Value)} 또는 {ob}";
        if ((m = Regex.Match(phrase, @"^(?<head>.+?) without (?:any )?cards under (?:it|them)$", IC)).Success
            && TargetKo(Regex.Replace(m.Groups["head"].Value, @"\bDigimon or Tamers\b", "Digimon", IC), bare) is { } hw)
        {
            string body = Regex.IsMatch(m.Groups["head"].Value, @"\bDigimon or Tamers\b", IC) ? Regex.Replace(hw, @"디지몬 (\d+)마리", "디지몬/테이머 $1마리(명)") : hw;
            return $"아래에 카드가 없는 {body}";
        }
        if (Regex.IsMatch(phrase, @"\bthe Digimon this effect played\b", IC))
            return TargetKo(Regex.Replace(phrase, @"\bthe Digimon this effect played\b", "the Digimon played by this effect", IC), bare);
        if ((m = Regex.Match(phrase, @"^(?<head>.+?) with (?:(?<dp>as much or less DP as|DP less than or equal to) (?:1|one|any) of your (?<t>.+?)|as much DP as (?:1|one) of your (?<t2>.+?) or less|(?<lv>as high or lower a level as|a level less than or equal to) (?:1|one|any) of your (?<t>.+?))$", IC)).Success
            && TargetKo(m.Groups["head"].Value, bare) is { } head)
        {
            string t = m.Groups["t2"].Success ? m.Groups["t2"].Value : m.Groups["t"].Value;
            if (TargetKo("1 of your " + t) is not { } own) return null;
            return $"{own}의 {(m.Groups["lv"].Success ? "Lv." : "DP")} 이하인 {head}";
        }
        if ((m = Regex.Match(phrase, @"^(?<head>.+?) with a level (?:as high or higher as|as high as|equal to or higher than|greater than or equal to) the number of cards in (?<w>your|their|your opponent's) hand(?: or higher)?$", IC)).Success
            && TargetKo(m.Groups["head"].Value, bare) is { } hh)
            return $"{(m.Groups["w"].Value.Equals("your", StringComparison.OrdinalIgnoreCase) ? "자신" : "상대")}의 패의 매수 이상의 Lv.인 {hh}";
        if ((m = Regex.Match(phrase, @"^(?<head>.+?) with the same colou?r as any of this Digimon's digivolution cards$", IC)).Success
            && TargetKo(m.Groups["head"].Value, bare) is { } hc)
            return $"이 디지몬의 진화원 중 어느 카드와 같은 색인 {hc}";
        return null;
    }

    /// <summary>조건 — 공식 BT2-083 "이 카드가 진화원을 가지고 있었다면"</summary>
    private static Cond? CondKo44(string cond)
    {
        if (!KoGate44) return null;
        string c0 = cond.Trim();
        Match m;
        // 공식 ST19-11 "디지몬이 3마리 이상 있다면"
        if ((m = Regex.Match(c0, @"^there are (?<n>\d+) or more Digimon(?: in play)?$", IC)).Success)
            return Have($"디지몬이 {m.Groups["n"].Value}마리 이상");
        if (Regex.IsMatch(c0, @"^this (?:card|Digimon) had (?:any )?digivolution cards$", IC))
            return new Cond("이 카드가 진화원을 가지고 있었을 때,", "이 카드가 진화원을 가지고 있었던 동안,", "이 카드가 진화원을 가지고 있었다면,");
        if ((m = Regex.Match(c0, $@"^(?<x>{TargetSpec.NameList})(?: in (?:its|their) names?)? (?:was|were) in this Digimon's digivolution cards when it was deleted$", IC)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value);
            return new Cond($"이 디지몬의 진화원에 {x}{J(x, "이", "가")} 있었을 때,", $"이 디지몬의 진화원에 {x}{J(x, "이", "가")} 있었던 동안,", $"이 디지몬의 진화원에 {x}{J(x, "이", "가")} 있었다면,");
        }
        return null;
    }

    private static string? EventKo44(string ev) => null;

    /// <summary>셈 — 공식 LM-010 "레스트 상태인 테이머 1명마다" · BT9-069 "액티브 상태의 상대 디지몬과 테이머 1장마다" · BT17-075 "테이머 2명마다"</summary>
    private static string? CounterKo44(string counter)
    {
        if (!KoGate44) return null;
        string s = counter.Trim().TrimEnd('.');
        Match m;
        if (Regex.IsMatch(s, @"^suspended Tamers?(?: in play)?$", IC)) return "레스트 상태인 테이머 1명마다";
        if (Regex.IsMatch(s, @"^suspended Digimon your opponent has(?: in play)?$", IC)) return "레스트 상태인 상대의 디지몬 1마리마다";
        if (Regex.IsMatch(s, @"^Tamers? your opponent has(?: in play)?$", IC)) return "상대의 테이머 1명마다";
        if ((m = Regex.Match(s, @"^(?<c>(?:red|blue|yellow|green|black|purple|white)(?: (?:or|and/or) (?:red|blue|yellow|green|black|purple|white))*) Tamers you have(?: in play)?$", IC)).Success)
            return CounterKo($"{m.Groups["c"].Value} Tamer you have in play");
        if ((m = Regex.Match(s, @"^(?:(?<n>\d+) )?colou?rs? (?<who>your opponent's Digimon and Tamers|your Digimon and Tamers|Digimon and Tamers|your Tamers with the (?<x>\[[^\]]+\]) trait) have$", IC)).Success)
        {
            string n = m.Groups["n"].Success ? m.Groups["n"].Value : "1";
            string who = m.Groups["who"].Value.ToLowerInvariant();
            string w = who.StartsWith("your opponent's") ? "상대의 디지몬과 테이머가 가진"
                : who.StartsWith("your digimon") ? "자신의 디지몬과 테이머가 가진"
                : who.StartsWith("your tamers") ? $"특징으로 {NamesKo(m.Groups["x"].Value, trait: true)}{J(NamesKo(m.Groups["x"].Value, trait: true), "을", "를")} 가진 자신의 테이머의"
                : "디지몬과 테이머가 가진";
            return $"{w} 색 {n}색마다";
        }
        if (Regex.IsMatch(s, @"^colou?r in (?:both players' )?trashes$", IC)) return "서로의 트래시에 있는 색 1색마다";
        // 공식 EX3-045 "특징으로 「식물형」/「요정형」을 포함하는 레스트 상태의 다른 자신의 디지몬 1마리마다"
        if ((m = Regex.Match(s, $@"^(?<other>other )?suspended Digimon with (?<x>{TargetSpec.NameList}) in (?:one|any) of (?:their|its) traits you have(?: in play)?$", IC)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value, trait: true);
            return $"특징으로 {x}{J(x, "을", "를")} 포함하는 레스트 상태의 {(m.Groups["other"].Success ? "다른 " : "")}자신의 디지몬 1마리마다";
        }
        // 공식 BT23-075 "육성 에어리어의 자신의 「마더 이터」의 진화원 1장마다"
        if ((m = Regex.Match(s, $@"^of your (?<x>{TargetSpec.NameList})(?:'s|’s) digivolution cards in the breeding area$", IC)).Success)
            return $"육성 에어리어의 자신의 {NamesKo(m.Groups["x"].Value)}의 진화원 1장마다";
        // 공식 EX9-054 "자신의 트래시와 디지몬의 진화원에 있는 「네가몬」 합계 2장마다"
        if ((m = Regex.Match(s, $@"^(?:(?<n>\d+) )?(?<x>{TargetSpec.NameList}) in your trash (?:or|and) your Digimon's digivolution cards$", IC)).Success)
            return $"자신의 트래시와 디지몬의 진화원에 있는 {NamesKo(m.Groups["x"].Value)} 합계 {(m.Groups["n"].Success ? m.Groups["n"].Value : "1")}장마다";
        // 공식 BT12-083 "색이 서로 다른 자신의 테이머 1명마다"
        if (Regex.IsMatch(s, @"^Tamer you have in play with a different colou?r$", IC)) return "색이 서로 다른 자신의 테이머 1명마다";
        if ((m = Regex.Match(s, @"^(?<n>\d+) Tamers$", IC)).Success) return $"테이머 {m.Groups["n"].Value}명마다";
        if ((m = Regex.Match(s, @"^(?:of )?(?<w>your opponent's|their|your) unsuspended Digimon and Tamers$", IC)).Success)
            return m.Groups["w"].Value.Equals("your", StringComparison.OrdinalIgnoreCase) ? "액티브 상태의 자신의 디지몬과 테이머 1장마다" : "액티브 상태의 상대 디지몬과 테이머 1장마다";
        if ((m = Regex.Match(s, @"^(?<n>\d+) unsuspended Digimon and/or Tamers your opponent has(?: in play)?$", IC)).Success)
            return $"액티브 상태의 상대 디지몬과 테이머 {m.Groups["n"].Value}장마다";
        return null;
    }
}
