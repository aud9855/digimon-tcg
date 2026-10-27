using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.40.0 미지원 문장 14차 묶음의 생성 한글 — 원래 규칙(13차까지)이 읽지 못한 절 · 문장 · 사건 · 조건 · 대상만 (SentenceParser.Batch40과 같은 읽기)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth40;
    [ThreadStatic] private static int _fallbackDepth40;

    // =====================================================================
    // 1. 절 (RenderClause 끝)
    // =====================================================================

    private static string? RenderClause40(string rawClause)
    {
        if (_clauseDepth40 > 0) return null;
        _clauseDepth40++;
        try
        {
            string clause = Regex.Replace(SentenceParser.NormalizeForRender(rawClause), @"^(?:Inherited|Security) Effect\s*", "", IC).Trim();
            var lead = Regex.Match(clause, @"^(?<labels>(?:\[[^\]]+\]\s*)*)(?<body>.*)$", RegexOptions.Singleline);
            string labels = lead.Groups["labels"].Value, body = lead.Groups["body"].Value.Trim();
            Match m;

            // 효과를 가진 디지몬과 같은 명칭 — 공식 EX1-051 "이 디지몬과 같은 명칭의 다른 자신의 디지몬 전부의 DP를 +2000 한다"
            if ((m = Regex.Match(clause, @"^(?<labels>(?:\[[^\]]+\]\s*)*)All of your (?<other>other )?Digimon with the same name as this Digimon (?<rest>(?:gain|get)s? .+)$", IC)).Success
                && RenderClauseCore($"{m.Groups["labels"].Value}All of your {m.Groups["other"].Value}Digimon {m.Groups["rest"].Value}") is { } aura)
                return Regex.Replace(aura, @"자신의 (?:다른 )?디지몬 전부", m.Groups["other"].Success ? "이 디지몬과 같은 명칭의 다른 자신의 디지몬 전부" : "이 디지몬과 같은 명칭의 자신의 디지몬 전부");

            // 공식 P-074 "이 디지몬이 ~ 카드로 진화할 때, 자신의 시큐리티를 3장까지 파기할 수 있다. 이 효과로 파기한 시큐리티 1장마다 지불하는 진화 코스트 -1"
            if ((m = Regex.Match(body, @"^When this Digimon would digivolve into (?<into>[^.]+?), you may trash up to (?<n>\d+) cards from the top of your security stack\. For each (?:security )?card trashed by this effect, reduce the (?:digivolution |memory )?cost by (?<r>\d+)\.?$", IC)).Success
                && PickKo(Regex.Replace(m.Groups["into"].Value, @"^an? ", "1 ", IC)) is { } pinto)
            {
                string intoKo = Regex.Replace(pinto, @"\s*1장$", "");
                return $"{LabelsKo40(labels)}이 디지몬이 {intoKo}{J(intoKo, "으로", "로")} 진화할 때, 자신의 시큐리티를 {m.Groups["n"].Value}장까지 파기할 수 있다. 이 효과로 파기한 시큐리티 1장마다 지불하는 진화 코스트 -{m.Groups["r"].Value}.";
            }
            // 공식 EX2-064 "Lv.5인 자신의 디지몬이 Lv.6으로 진화할 때, 자신의 디지몬 1마리를 소멸시키는 것으로, 지불하는 진화 코스트 -3"
            if ((m = Regex.Match(body, @"^When one of your Digimon would digivolve from level (?<a>\d+) to level (?<b>\d+), you may delete 1 of your Digimon to reduce the digivolution cost by (?<n>\d+)\.?$", IC)).Success)
                return $"{LabelsKo40(labels)}Lv.{m.Groups["a"].Value}인 자신의 디지몬이 Lv.{m.Groups["b"].Value}{(m.Groups["b"].Value is "3" or "6" ? "으로" : "로")} 진화할 때, 자신의 디지몬 1마리를 소멸시키는 것으로, 지불하는 진화 코스트 -{m.Groups["n"].Value}.";
            // 정해진 코스트 — 공식 BT7-100 · BT7-040
            if (Regex.IsMatch(body, @"^When (?:this card would be used|you would use this card)(?: from your hand)?, its (?:memory |use )?cost is equal to the number of cards in your security stack\.?$", IC))
                return "패의 이 카드를 사용할 때, 지불하는 코스트는 자신의 시큐리티의 매수와 동일해진다.";
            if ((m = Regex.Match(body, @"^When (?:you would digivolve|one of your Digimon would digivolve) into this card(?: from your hand)?, the (?:memory )?cost of the digivolution is equal to the number of cards in your security stack\.(?: If you have 0 security cards, the memory cost is (?<z>\d+)\.?)?$", IC)).Success)
                return "자신의 디지몬이 패의 이 카드로 진화할 때, 지불하는 진화 코스트는 자신의 시큐리티의 매수와 동일해진다." + (m.Groups["z"].Success ? $" 단, 0장일 경우에는 지불하는 진화 코스트는 {m.Groups["z"].Value}이 된다." : "");
            // 공식 EX3-016 "【상대의 턴】진화원을 갖지 않은 상대의 디지몬이 진화할 때, 지불하는 진화 코스트 +1."
            if ((m = Regex.Match(clause, @"^\[Opponent's Turn\]\s*When an opponent's Digimon with no digivolution cards would digivolve, increase the (?:digivolution |memory )?cost by (?<n>\d+)\.?$", IC)).Success)
                return $"【상대의 턴】 진화원을 갖지 않은 상대의 디지몬이 진화할 때, 지불하는 진화 코스트 +{m.Groups["n"].Value}.";
            // BT26-088 "~가 등장할 때, 이 테이머를 레스트시키는 것으로, 지불하는 등장 코스트 -1. 자신의 디지몬이 없다면, 대신에 -2"
            if ((m = Regex.Match(body, @"^When (?<p>any [^,]+?) would be played, by (?<cost>[^,]+?), reduce the (?:play )?costs? by (?<n>\d+)\. If you have no Digimon, instead reduce the (?:play )?costs? by (?<m>\d+)\.?$", IC)).Success
                && PickKo("1 " + Regex.Replace(m.Groups["p"].Value, @"^any (?:of your )?", "", IC).Replace(" cards ", " card ")) is { } fp && CostKo(m.Groups["cost"].Value) is { } fc)
            {
                string who = Regex.Replace(fp, @"\s*1장$", "");
                return $"{LabelsKo40(labels)}자신의 {who}{J(who, "이", "가")} 등장할 때, {fc} 지불하는 등장 코스트 -{m.Groups["n"].Value}. 자신의 디지몬이 없다면, 대신에 지불하는 등장 코스트 -{m.Groups["m"].Value}.";
            }
            // "이 카드가 등장할 때 또는 이 카드로 진화할 때, ~ 지불하는 등장/진화 코스트 -2" (공식 EX5-012)
            if ((m = Regex.Match(body, @"^When this card would be played or digivolved into, (?:if (?<cond>[^.]+?), )?(?:by (?<cost>[^.]+?), )?reduce the play or digivolution cost by (?<n>\d+)\.?$", IC)).Success)
            {
                string? cond = m.Groups["cond"].Success ? CondKo(SentenceParser.NoListCommas40(m.Groups["cond"].Value))?.Then : "";
                string? cost = m.Groups["cost"].Success ? CostKo(m.Groups["cost"].Value) : "";
                if (cond != null && cost != null)
                    return $"{LabelsKo40(labels)}이 카드가 등장할 때 또는 이 카드로 진화할 때, {Mid40(cond, cost)}지불하는 등장/진화 코스트 -{m.Groups["n"].Value}.";
            }
            // 공식 LM-009 "「앙고라몬」이 기술되어 있는 카드가 등장할 때 또는 자신의 디지몬이 「앙고라몬」이 기술되어 있는 카드로 진화할 때, 이 디지몬을 레스트시키는 것으로, 지불하는 등장/진화 코스트 -2"
            if ((m = Regex.Match(body, @"^When (?<p>an? [^,]+?) would be played or (?<who>one of your Digimon|this Digimon) would digivolve into such a card, (?:if (?<cond>[^.]+?), )?(?:by (?<cost>[^.]+?), )?reduce the play or digivolution cost by (?<n>\d+)\.?$", IC)).Success
                && PickKo("1 " + Regex.Replace(m.Groups["p"].Value, @"^an? ", "", IC)) is { } pk)
            {
                string card = Regex.Replace(pk, @"\s*1장$", "");
                string? cond = m.Groups["cond"].Success ? CondKo(m.Groups["cond"].Value)?.Then : "";
                string? cost = m.Groups["cost"].Success ? CostKo(m.Groups["cost"].Value) : "";
                string who = m.Groups["who"].Value.StartsWith("this", StringComparison.OrdinalIgnoreCase) ? "이 디지몬이" : "자신의 디지몬이";
                if (cond != null && cost != null)
                    return $"{LabelsKo40(labels)}{card}{J(card, "이", "가")} 등장할 때 또는 {who} {card}{J(card, "으로", "로")} 진화할 때, {Mid40(cond, cost)}지불하는 등장/진화 코스트 -{m.Groups["n"].Value}.";
            }
            // 공식 LM-019 "「보코몬」 이외의 「감마몬」이 기술되어 있는 자신의 디지몬이 ~"
            if ((m = Regex.Match(clause, @"^(?<a>.*?\bWhen (?:one|any) of your [^,]*?Digimon[^,]*?), other than (?<x>\[[^\]]+\]), (?<b>would .+)$", IC)).Success
                && RenderClauseCore($"{m.Groups["a"].Value} {m.Groups["b"].Value}") is { } ex)
            {
                string x = NamesKo(m.Groups["x"].Value);
                var who = Regex.Match(ex, @"(?<w>[^ 】]+이 기술되어 있는 |명칭에 [^ ]+ 포함하는 )?자신의 디지몬이");
                if (who.Success) return ex[..who.Index] + $"{x} 이외의 " + ex[who.Index..];
            }
            // 진화할 카드 조건이 쉼표 목록 · "other than" (BT25-010 — 공식 BT11-016 "특징으로 ~을 (「수생포유류형」은 제외) 가진")
            if ((m = Regex.Match(clause, @"^(?<labels>(?:\[[^\]]+\]\s*)*)When this Digimon would digivolve into (?<into>.+?)(?:, other than (?<x>\[[^\]]+\]),)? (?<tr>in any of (?:its|their) traits), reduce the (?:digivolution |memory )?cost(?: of the digivolution)? by (?<n>\d+)\.?$", IC)).Success
                && PickKo(Regex.Replace(m.Groups["into"].Value, @"^an? ", "1 ", IC) + " " + m.Groups["tr"].Value + (m.Groups["x"].Success ? " other than " + m.Groups["x"].Value : "")) is { } into)
                return $"{LabelsKo40(m.Groups["labels"].Value)}이 디지몬이 {Regex.Replace(into, @"\s*1장$", "")}{J(Regex.Replace(into, @"\s*1장$", ""), "으로", "로")} 진화할 때, 지불하는 진화 코스트를 -{m.Groups["n"].Value} 한다.";

            foreach (var parts in SentenceParser.ClauseRewrites40(rawClause, new CardData { Type = Regex.IsMatch(clause, @"\bthis Tamer\b", IC) ? CardType.Tamer : CardType.Digimon }))
            {
                if (parts.Count == 0) continue;
                var kos = parts.Select(RenderClauseCore).ToList();
                if (kos.Any(k => k == null)) continue;
                string joined = JoinParts40(kos!, labels);
                // 테이머 자신이 진화할 때 (공식 BT7-089 "이 테이머가 그린인 디지몬으로 진화할 때")
                if (Regex.IsMatch(clause, @"\bWhen this Tamer would digivolve\b", IC)) joined = Regex.Replace(joined.Replace("이 디지몬이 ", "이 테이머가 "), @"(\S+)의 디지몬 카드로 진화할 때", "$1인 디지몬으로 진화할 때");
                return joined;
            }
            return null;
        }
        finally { _clauseDepth40--; }
    }

    /// <summary>
    /// v0.40.0 등장 뒤의 "It gains ＜Rush＞ for the turn" — "it"은 이 효과로 등장한 그 디지몬 (EX13-060: 전에는 "이 디지몬은"으로 옮겼다 — 엔진은 v0.39.0부터 등장한 디지몬)
    /// </summary>
    private static string? FixItAfterPlay40(string rawClause, string? ko)
    {
        if (ko == null || !Regex.IsMatch(rawClause, @"\b[Pp]lay [^.]*\. It gains ＜", IC)) return ko;
        var m = Regex.Match(ko, @"등장시(?:킬 수 있다|킨다)\. (?:이 턴 동안, )?이 디지몬은 《");
        return m.Success ? ko[..m.Index] + Regex.Replace(m.Value, @"이 디지몬은 《$", "그 디지몬은 《") + ko[(m.Index + m.Length)..] : ko;
    }

    private static string Mid40(string cond, string cost) => string.Join(" ", new[] { cond.TrimEnd(','), cost }.Where(x => x.Length > 0)) is { Length: > 0 } mid ? mid + (mid.EndsWith(",") ? " " : ", ") : "";

    private static string LabelsKo40(string labels)
    {
        var ls = Regex.Matches(labels, @"\[([^\]]+)\]").Select(l => LabelKo(l.Groups[1].Value.Trim())).ToList();
        return ls.Count == 0 || ls.Any(l => l == null) ? "" : string.Join(" ", ls) + " ";
    }

    /// <summary>나눈 절의 한글을 잇는다: 같은 머리(라벨 · 조건)는 한 번만 — "~ 동안, 이 디지몬은 A를 얻는다" + "~ 동안, 이 디지몬은 B를 얻는다" → "~ 동안, 이 디지몬은 A를 얻고, B를 얻는다"</summary>
    private static string JoinParts40(List<string> kos, string labels)
    {
        string first = kos[0];
        // [시큐리티]의 지속 효과 (공식 EX8-071 "[시큐리티]【서로의 턴】") — 시큐리티에 앞면으로 있는 동안의 효과
        bool securityStatic = Regex.IsMatch(labels, @"^\[Security\]\s*\[(?:All Turns|Your Turn|Opponent's Turn)\]", IC);
        string Fix(string ko) => securityStatic ? Regex.Replace(ko, @"^【시큐리티】\s*", "[시큐리티]") : ko;
        if (kos.Count == 1) return Fix(first);
        var sb = new List<string> { Fix(first).TrimEnd() };
        for (int i = 1; i < kos.Count; i++)
        {
            string next = Fix(kos[i]);
            // 공통 머리 (", "로 끝나는 가장 긴 공통 앞부분)
            int common = 0;
            string prev = Fix(kos[i - 1]);
            for (int k = 0; k < Math.Min(prev.Length, next.Length) && prev[k] == next[k]; k++)
                if (prev[k] == ',' && k + 1 < next.Length && next[k + 1] == ' ') common = k + 2;
            if (common > 0 && sb[^1].EndsWith("얻는다.") && Regex.IsMatch(next[common..], @"^(?:이 디지몬은 )?"))
            {
                string tail = Regex.Replace(next[common..], @"^이 디지몬은 ", "");
                sb[^1] = sb[^1][..^"얻는다.".Length] + "얻고, " + tail;
                continue;
            }
            // 라벨만 같으면 라벨을 빼고 잇는다 / "패의 이 카드를 등장시킬 때," 같은 머리도 한 번만 (공식 BT16-065)
            string labelKo = LabelsKo40(labels);
            if (labelKo.Length > 0 && next.StartsWith(Fix(labelKo).Trim())) next = next[Fix(labelKo).Trim().Length..].TrimStart();
            if (next.StartsWith("패의 이 카드를 등장시킬 때, ") && first.StartsWith("패의 이 카드를 등장시킬 때, ")) next = next["패의 이 카드를 등장시킬 때, ".Length..];
            sb.Add(next);
        }
        return string.Join(" ", sb);
    }

    // =====================================================================
    // 2. 문장 (Sentence 끝)
    // =====================================================================

    private static string? SentenceFallback40(string s, bool nested)
    {
        if (_fallbackDepth40 >= 3) return null;
        _fallbackDepth40++;
        try
        {
            s = s.Trim().TrimEnd('.').Trim();
            if (Regex.IsMatch(s, @"^Then,?\s", IC)) return null;
            Match m;
            // 여러 Lv. 1마리씩 — 공식 EX7-071 "Lv.3과 Lv.4와 Lv.5의 상대의 디지몬 1마리씩을 소멸시킨다"
            if ((m = Regex.Match(s, @"^Delete (?<t>1 (?:each )?of (?:your opponent's|their) level .+)$", IC)).Success && SentenceParser.MultiLevelPick40(m.Groups["t"].Value) is { } levels)
            {
                var lv = levels.Select(x => $"Lv.{x}").ToList();
                string list = string.Concat(lv.Take(lv.Count - 1).Select(x => x + J(x, "과", "와") + " ")) + lv[^1];
                return $"{list}의 상대의 디지몬 1마리씩을 소멸시킨다";
            }
            // 시큐리티에서 진화 — 공식 BT14-033 "자신의 시큐리티를 전부 확인한다. 이 디지몬을 그중 ~ 디지몬 카드로 코스트를 지불하지 않고 진화시킬 수 있다"
            if ((m = SentenceParser.SecurityDigivolveRe40.Match(s)).Success)
            {
                string pickText = Regex.Replace(Regex.Replace(m.Groups["pick"].Value.Trim(), @"^an? ", "1 ", IC), @"\bDigimon(?! card)\b", "Digimon card", IC);
                if (PickKo(pickText) is not { } pk) return null;
                pk = Regex.Replace(pk, @"\s*1장$", "");
                string who = m.Groups["spec"].Success ? (TargetKo("1 of your " + m.Groups["spec"].Value) ?? "자신의 디지몬 1마리") : "이 디지몬";
                string how = m.Groups["free"].Success ? "코스트를 지불하지 않고" : $"지불하는 진화 코스트 -{m.Groups["n"].Value} 하여";
                return $"자신의 시큐리티를 전부 확인한다. {who}{J(who, "을", "를")} 그중 {pk}{J(pk, "으로", "로")} {how} 진화시킬 수 있다";
            }
            // 상대 시큐리티 공개 — 공식 P-078 · RB1-027
            if ((m = SentenceParser.RevealOppSecurityRe40.Match(s)).Success)
            {
                var parts = new List<string> { "상대의 시큐리티를 위에서부터 1장 오픈한다" };
                bool firstIf = true;
                foreach (Match b in Regex.Matches(m.Groups["ifs"].Value, @"If that card is (?<what>[^,]+), (?<act>[^.]+)\.", IC))
                {
                    string? what = b.Groups["what"].Value.Trim().ToLowerInvariant() switch
                    {
                        "a digimon card" or "a digimon" => "디지몬 카드", "a non-digimon" or "a non-digimon card" or "not a digimon card" => "디지몬 카드 이외",
                        "a tamer card" => "테이머 카드", "an option card" => "옵션 카드", _ => null,
                    };
                    if (what == null || Sentence(Cap(b.Groups["act"].Value.Trim()), nested: true) is not { } act) return null;
                    parts.Add($"{(firstIf ? "그 카드가 " : "")}{what}{(Batchim(what) ? "이라면" : "라면")}, {act}");
                    firstIf = false;
                }
                parts.Add(m.Groups["bottom"].Success ? "오픈한 카드는 시큐리티 위 또는 아래로 뒷면으로 되돌린다" : "오픈한 카드는 상대의 시큐리티 위에 뒷면으로 놓는다");
                return string.Join(". ", parts);
            }
            // 공식 EX4-032 "이 디지몬을 패의 그린을 포함하는 2색의 디지몬 카드로 지불하는 진화 코스트 -2 하여 진화시킬 수 있다"
            if ((m = Regex.Match(s, $@"^(?:this Digimon may digivolve|you may digivolve this Digimon) into an? (?<k>\d+)-colou?r (?<c>{Colors}) (?:Digimon )?card in (?:your|the) hand for its digivolution cost(?: with the digivolution cost reduced by (?<r>\d+))?$", IC)).Success)
                return $"이 디지몬을 패의 {ColorKo(m.Groups["c"].Value)}을 포함하는 {m.Groups["k"].Value}색의 디지몬 카드로 {(m.Groups["r"].Success ? $"지불하는 진화 코스트 -{m.Groups["r"].Value} 하여" : "진화 코스트를 지불하여")} 진화시킬 수 있다";
            // 테이머만 진화 — "자신의 테이머 1명을 패의 ~ 디지몬 카드로 코스트를 지불하지 않고 진화시킬 수 있다" (BT21-091)
            if ((m = SentenceParser.TamersEvoRe40.Match(s)).Success && !Regex.IsMatch(m.Groups["t"].Value, @"\bDigimon\b", IC)
                && PickKo(Regex.Replace(m.Groups["pick"].Value, @"^(?:an?|1) ", "1 ", IC)) is { } tpk)
            {
                string evo = Regex.Replace(tpk, @"\s*1장$", "");
                string from = m.Groups["from"].Value.ToLowerInvariant() switch { "hand or trash" => "패/트래시의", "trash" => "트래시의", _ => "패의" };
                return $"자신의 테이머 1명을 {from} {evo}{J(evo, "으로", "로")} 코스트를 지불하지 않고 진화시킬 수 있다";
            }
            // 덱 위 1장 공개 · 조건에 맞으면 등장 (EX5-042)
            if ((m = SentenceParser.RevealTopPlayRe40.Match(s)).Success && PickKo(Regex.Replace(m.Groups["pick"].Value, @"^an? ", "1 ", IC)) is { } rpk)
            {
                string what = Regex.Replace(rpk, @"\s*1장$", "");
                return $"자신의 덱 위에서부터 1장 오픈한다. 그 카드가 {what}{(Batchim(what) ? "이라면" : "라면")}, 코스트를 지불하지 않고 등장시킨다. 나머지는 패에 추가한다";
            }
            // "Delete this Digimon at the end of the turn" — 공식 BT17-011 "턴 종료 시에 이 디지몬을 소멸시킨다"
            if ((m = Regex.Match(s, @"^Delete (?<t>this Digimon|it|that Digimon) at the end of (?:the|this) turn$", IC)).Success)
                return $"턴 종료 시에 {(m.Groups["t"].Value.Equals("this Digimon", StringComparison.OrdinalIgnoreCase) ? "이 디지몬" : "그 디지몬")}을 소멸시킨다";
            // 공개한 카드의 등장 코스트 합계 — 공식 BT11-044 "그중 ~ 디지몬 카드를 등장 코스트 합계 7까지 코스트를 지불하지 않고 등장시킬 수 있다"
            if (BudgetRevealKo40(s) is { } br) return br;
            // "All Digimon played by this effect gain ＜Blocker＞ until ~" (BT10-084) — "이 효과로 등장한 디지몬 전부는 ~"
            if ((m = Regex.Match(s, @"^All (?:of the )?Digimon played (?:by|with) this effect gain (?<rest>.+)$", IC)).Success
                && Sentence("All of your Digimon gain " + m.Groups["rest"].Value, nested) is { } played && played.Contains("자신의 디지몬 전부"))
                return played.Replace("자신의 디지몬 전부", "이 효과로 등장한 디지몬 전부");
            foreach (var alt in SentenceParser.Rewrites40(s))
                if (alt != s && Sentence(alt, nested) is { } ko) return Regex.Replace(ko, @"테이머가 (\d+)장", "테이머가 $1명");   // "자신의 테이머가 4명 이상 있다면" (공식 BT12-098)
            if (CompoundKo40(s) is { } comp) return comp;
            return null;
        }
        finally { _fallbackDepth40--; }
    }

    /// <summary>"~ any number of X whose total play costs add up to 7 or less among them ~" · "~ up to 7 play cost's total worth of X among them ~" → 1장으로 바꿔 옮긴 뒤 고친다</summary>
    private static string? BudgetRevealKo40(string s)
    {
        var m = Regex.Match(s, @"\b(?:any number of (?<x>.+?) whose total play costs add up to (?<n>\d+) or less|up to (?<n>\d+) play cost's total worth of (?<x>.+?)) among them\b", IC);
        if (!m.Success) return null;
        string x = Regex.Replace(m.Groups["x"].Value.Trim(), @"\bcards\b", "card", IC);
        x = Regex.Replace(x, @"\bin their names\b", "in its name", IC);
        if (PickKo("1 " + x) is not { } pk) return null;
        string one = Regex.Replace(pk, @"\s*1장$", "");
        string probe = s[..m.Index] + "1 " + x + " among them" + s[(m.Index + m.Length)..];
        if (Sentence(probe, nested: true) is not { } ko) return null;
        string from = $"{pk}{J(pk, "을", "를")}";
        if (!ko.Contains(from)) return null;
        return ko.Replace(from, $"{one}{J(one, "을", "를")} 등장 코스트 합계 {m.Groups["n"].Value}까지");
    }

    /// <summary>SentenceParser.Compound40과 같은 나누기 — "A and B" (끝 · 앞의 기간은 양쪽에)</summary>
    private static string? CompoundKo40(string s)
    {
        s = Regex.Replace(s, @",? and their (?<k>＜[^＞]+＞) effects don't affect it(?= until |$)", " and it isn't affected by their ${k} effects", IC);
        var tries = new List<(string Body, string Tail)>();   // 기간이 있으면 양쪽에 붙인 읽기를 먼저 (공식 BT19-035 · EX8-073 "상대의 턴 종료까지 ~하고, ~")
        if (Regex.Match(s, @"^(?<body>.+?),?\s+(?<d>for the turn|until (?:the end of )?your opponent's turn(?: ends)?)$", IC) is { Success: true } tm)
            tries.Add((tm.Groups["body"].Value, " " + tm.Groups["d"].Value));
        if (Regex.Match(s, @"^(?<d>Until (?:the end of )?your opponent's turn(?: ends)?|For the turn), (?<body>.+)$", IC) is { Success: true } lm)
            tries.Add((lm.Groups["body"].Value, " " + char.ToLowerInvariant(lm.Groups["d"].Value[0]) + lm.Groups["d"].Value[1..]));
        tries.Add((s, ""));
        foreach (var (body, tail) in tries)
            foreach (Match m in Regex.Matches(body, @",? and (?=(?:this Digimon|1 of your|all of your|it|gains?|gets?|unsuspends?)\b)", IC))
            {
                string a = body[..m.Index].Trim() + tail, b = body[(m.Index + m.Length)..].Trim() + tail;
                if (Regex.IsMatch(b, @"^(?:gains?|gets?|unsuspends?)\b", IC))
                {
                    if (!Regex.IsMatch(a, @"^(?:this Digimon|it|that Digimon|1 of your [^,]*?Digimon[^,]*?) (?:gains?|gets?|unsuspends?)\b", IC)) continue;
                    b = (Regex.IsMatch(a, @"^this Digimon\b", IC) ? "this Digimon " : "it ") + Regex.Replace(b, @"^(gain|get|unsuspend)\b", "$1s", IC);
                }
                if (a.Length == 0 || b.Length == 0 || Regex.IsMatch(a, @"\battacks?\b", IC)) continue;
                string? ka = Action(Cap(a), Form.And);
                if (ka == null && Sentence(Cap(a), nested: true) is { } sa && sa.EndsWith("한다")) ka = sa[..^2] + "하고";   // "자신의 시큐리티를 셔플하고," (EX6-030)
                if (ka != null && (Action(Cap(b), Form.Plain) ?? Sentence(Cap(b), nested: true)) is { } kb)
                    return $"{ka}, {kb}";
            }
        return null;
    }

    // =====================================================================
    // 3. 사건 (EventKo 끝)
    // =====================================================================

    private static string? EventKo40(string ev)
    {
        ev = ev.Trim();
        Match m;
        if (Regex.IsMatch(ev, @"^(?:one|any) of your Digimon (?:is|becomes|are) suspended by (?:an? )?＜Alliance＞ effects?$", IC))
            return "자신의 디지몬이 《연계》의 효과로 레스트했을 때,";   // 공식 EX4-032
        if (Regex.IsMatch(ev, @"^(?:an|one of your) opponent's Digimon (?:becomes unsuspended|unsuspends|is unsuspended) during (?:a|the|any) main phase$", IC))
            return "상대의 디지몬이 메인 페이즈에서 액티브 상태가 되었을 때,";   // 공식 BT9-089
        if ((m = Regex.Match(ev, @"^(?:one|any) of your Digimon digivolves? into a same-level (?<spec>Digimon(?: with .+)?)$", IC)).Success
            && TargetKo("all of your " + m.Groups["spec"].Value) is { } same)
        {
            // 공식 BT9-092 "자신의 디지몬이 특징으로 「X항체」를 가진 같은 Lv.의 디지몬으로 진화했을 때,"
            string desc = Regex.Replace(same, @"\s*자신의 디지몬 전부$", "");
            return $"자신의 디지몬이 {(desc.Length > 0 ? desc + " " : "")}같은 Lv.의 디지몬으로 진화했을 때,";
        }
        if ((m = Regex.Match(ev, @"^you play (?<a>an? [^,]*?Digimon(?: with .+?)?) or place (?:an? )?(?<x>\[[^\]]+\]) in your battle area$", IC)).Success
            && EventKo("you play " + m.Groups["a"].Value) is { } played)
        {
            // 공식 EX3-034 "특징으로 「4대용」을 가진 자신의 디지몬이 등장했을 때 또는 자신의 「4대용의 시련」이 배틀 에어리어에 놓였을 때,"
            string x = NamesKo(m.Groups["x"].Value);
            return $"{played.TrimEnd(',')} 또는 자신의 {x}{J(x, "이", "가")} 배틀 에어리어에 놓였을 때,";
        }
        if ((m = Regex.Match(ev, @"^this Digimon would digivolve(?<leave> or leave the battle area)?$", IC)).Success)
            return m.Groups["leave"].Success ? "이 디지몬이 진화할 때 또는 배틀 에어리어를 벗어날 때," : "이 디지몬이 진화할 때,";
        if ((m = Regex.Match(ev, $@"^(?:any )?(?<x>{TargetSpec.NameList}) (?:is placed from this Digimon's digivolution cards at the bottom of (?:its owner's|the|your) deck|(?:returns?|is returned|are returned) to the bottom of (?:the|its owner's|your) deck from this Digimon's digivolution cards)$", IC)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value);
            return $"이 디지몬의 진화원에서 {x}{J(x, "이", "가")} 덱 아래로 되돌아갔을 때,";   // 공식 BT11-065
        }
        if (Regex.IsMatch(ev, @"^(?:an effect places|effects place) the top card of this Digimon in its digivolution cards$", IC))
            return "이 디지몬에게 겹쳐져 있는 카드가 이 디지몬의 진화원에 효과로 놓였을 때,";   // 공식 EX5-001
        if (Regex.IsMatch(ev, @"^(?:an effect places|effects place) this Digimon's top stacked card as its bottom digivolution card$", IC))
            return "이 디지몬에 겹쳐져 있는 카드가 위에서부터 1장 이 디지몬의 진화원 아래에 효과로 놓였을 때,";   // 공식 BT22-006
        if (Regex.IsMatch(ev, @"^(?:an effect places|effects place) the top card of (?:one|any) of your Digimon in a Digimon's digivolution cards$", IC))
            return "자신의 디지몬에 겹쳐져 있는 카드가 디지몬의 진화원에 효과로 놓였을 때,";   // 공식 EX5-065
        if ((m = Regex.Match(ev, $@"^(?:a|an|any) (?:(?<color>{Colors}) )?cards? (?:is|are) placed under (?:this Tamer|one of your Tamers|your Tamers)$", IC)).Success)
            return $"이 테이머 아래에 {(m.Groups["color"].Success ? ColorKo(m.Groups["color"].Value) + "인 " : "")}카드가 놓였을 때,";   // 공식 BT10-093
        if ((m = Regex.Match(ev, @"^this card is trashed as a digivolution card by the effect of (?<x>\[[^\]]+\])$", IC)).Success)
            return $"이 카드가 {NamesKo(m.Groups["x"].Value)}의 효과로 진화원에서 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^effects add cards to your opponent's hand or trash cards from under your Tamers$", IC))
            return "효과로 상대의 패에 카드가 추가되었을 때 또는 자신의 테이머 아래의 카드가 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^(?:one|any) of your Digimon's effects adds? (?:cards )?to this Digimon's digivolution cards$", IC))
            return "자신의 디지몬의 효과로 이 디지몬의 진화원이 늘어났을 때,";
        return null;
    }

    // =====================================================================
    // 4. 조건 (CondKo 끝) · 대상 (TargetKo 끝)
    // =====================================================================

    private static Cond? CondKo40(string cond)
    {
        string c0 = cond.Trim();
        Match m;
        if (Regex.IsMatch(c0, @"^(?:digivolved by this effect|this effect digivolved|this Digimon digivolved by this effect)$", IC))
            return new("이 효과로 진화했다면,", "이 효과로 진화한 동안,", "이 효과로 진화했다면,");   // 공식 BT14-033
        if (Regex.IsMatch(c0, @"^you deleted (?:a|any|1) Digimon (?:with|by) this effect$", IC))
            return new("이 효과로 소멸했다면,", "이 효과로 소멸한 동안,", "이 효과로 소멸했다면,");   // 공식 BT14-018
        if (Regex.IsMatch(c0, @"^you have an? Tamer with inherited effects$", IC)) return Have("진화원 효과를 가진 자신의 테이머가");
        if (Regex.IsMatch(c0, @"^you have Tamer with ", IC)) return CondKo(Regex.Replace(c0, @"^you have Tamer with ", "you have a Tamer with ", IC));
        // "digivolved by this effect and you have ~" — 공식 BT14-093 "이 효과로 진화하고, 명칭에 「리키」를 포함하는 자신의 테이머가 있다면,"
        if ((m = Regex.Match(c0, @"^(?<a>digivolved by this effect|this effect digivolved) and (?<b>you .+)$", IC)).Success && CondKo(m.Groups["b"].Value) is { } cb)
            return new($"이 효과로 진화하고, {cb.If}", $"이 효과로 진화하고, {cb.While}", $"이 효과로 진화하고, {cb.Then}");
        // "there is a Digimon with the [Boss] trait" — 공식 BT16-065 "특징으로 「반쵸」를 가진 디지몬이 있다면,"
        if ((m = Regex.Match(c0, @"^(?<who>you|your opponent|there) (?:has|have|is) (?:a|an|1) (?<spec>(?:[^,]*? )?Digimon with .+)$", IC)).Success)
        {
            string who = m.Groups["who"].Value.ToLowerInvariant();
            if (TargetKo($"all of {(who == "your opponent" ? "your opponent's" : "your")} {SentenceParser.NoListCommas40(m.Groups["spec"].Value)}") is { } t)
            {
                string desc = Regex.Replace(t, @"\s*전부$", "");
                if (who == "there") desc = Regex.Replace(desc, @"자신의 디지몬$", "디지몬");
                return Have(desc + J(desc, "이", "가"));
            }
        }
        return null;
    }

    /// <summary>"you have a blue or green Digimon or Tamer" — 공식 BT17-011 "블루/그린인 자신의 디지몬/테이머가 있다면" (SentenceParser.ColorDigimonOrTamer40과 같은 읽기)</summary>
    private static Cond? ColorDigimonOrTamerKo40(string cond)
    {
        var m = Regex.Match(cond.Trim(), $@"^you have (?:a|an) (?<c>(?:{Colors})(?:,? (?:or )?(?:{Colors}))*) Digimon or Tamer(?: in play)?$", IC);
        return m.Success ? Have($"{ColorsKo(m.Groups["c"].Value)}인 자신의 디지몬/테이머가") : null;
    }

    /// <summary>키워드 괄호 속 조건의 다른 표기 (SentenceParser.ParseSpecifiedCard40과 같은 읽기): 바깥 괄호 "[Lv.4 or lower w/[DS] trait]" · "[Deva] or [Four Sovereigns] trait" → "[Deva]/[Four Sovereigns] trait"</summary>
    private static string SpecNormalize40(string s)
    {
        if (Regex.Match(s, @"^\[(?<in>Lv\.\d+.+)\]$", IC) is { Success: true } outer) s = outer.Groups["in"].Value;
        if (Regex.IsMatch(s, @"^\[[^\]]+\](?: or \[[^\]]+\])+ trait$", IC)) s = Regex.Replace(s, @"\] or \[", "]/[");
        return s;
    }

    /// <summary>"2 cards that are either black with [Knightmon] or [DeadlyAxemon] in their names or with the [Twilight] trait" (BT10-058) — SentenceParser.ParseCardPick40과 같은 읽기</summary>
    private static string? PickKo40(string p)
    {
        var m = Regex.Match(p.Trim(), $@"^(?<n>\d+) cards that are either (?<color>(?:{Colors}) )?(?<a>with .+?) or (?<b>with .+)$", IC);
        if (!m.Success) return null;
        string a = Regex.Replace(m.Groups["a"].Value, @"\bin their names\b", "in its name", IC), b = Regex.Replace(m.Groups["b"].Value, @"\bin their names\b", "in its name", IC);
        if (PickKoCore($"1 {m.Groups["color"].Value}card {a}") is not { } ka || PickKoCore($"1 card {b}") is not { } kb) return null;
        return $"{Regex.Replace(ka, @"\s*1장$", "")} 또는 {Regex.Replace(kb, @"\s*1장$", "")} {m.Groups["n"].Value}장";
    }

    private static string? TargetKo40(string phrase, bool bare)
    {
        phrase = phrase.Trim().TrimEnd('.').Trim();
        Match m;
        // 공식 BT13-056 "특징으로 「로얄 나이츠」를 가지거나 그린인 자신의 디지몬 전부"
        if ((m = Regex.Match(phrase, @"^all of your Digimon with the (?<x>\[[^\]]+\]) trait and all (?:of )?your (?<c>(?:red|blue|yellow|green|black|purple|white)) Digimon$", IC)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value, trait: true);
            return $"특징으로 {x}{J(x, "을", "를")} 가지거나 {ColorKo(m.Groups["c"].Value)}인 자신의 디지몬 전부";
        }
        if ((m = Regex.Match(phrase, @"^all of your Digimon with the (?<x>\[[^\]]+\]) trait or with ＜(?<k>[^＞]+)＞$", IC)).Success && KeywordKo(m.Groups["k"].Value, withReminder: false) is { } kw)
        {
            string x = NamesKo(m.Groups["x"].Value, trait: true);
            return $"특징으로 {x}{J(x, "을", "를")} 가지거나 {kw}{J(kw, "을", "를")} 가진 자신의 디지몬 전부";
        }
        // 공식 BT12-098 "≪세이브≫가 기술되어 있는 자신의 디지몬 1마리"
        if ((m = Regex.Match(phrase, @"^(?<pre>.+?Digimon) with ＜(?<k>[^＞]+)＞ in (?:its|their) texts?$", IC)).Success && TargetKoCore(m.Groups["pre"].Value, bare) is { } pre
            && KeywordKo(m.Groups["k"].Value, withReminder: false) is { } kw2)
            return Regex.Replace(pre, @"자신의 (?=디지몬)", $"{kw2}{J(kw2, "이", "가")} 기술되어 있는 자신의 ");
        // 공식 BT14-018 "자신의 「홍염의 아몬」과 「창뢰의 우몬」 전부"
        if ((m = Regex.Match(phrase, @"^all of your (?<a>\[[^\]]+\])s? and (?<b>\[[^\]]+\])s?$", IC)).Success)
        {
            string Tok(string x) => TokenNames.TryGetValue(x.Trim('[', ']'), out var t) ? $"「{t}」" : NamesKo(x);
            string a = Tok(m.Groups["a"].Value), b = Tok(m.Groups["b"].Value);
            return $"자신의 {a}{J(a, "과", "와")} {b} 전부";
        }
        // "all of your Digimon with ＜Fortitude＞ and no digivolution cards" (EX5-042)
        if ((m = Regex.Match(phrase, @"^(?:all of |all )?your Digimon with ＜(?<k>[^＞]+)＞ and no digivolution cards$", IC)).Success && KeywordKo(m.Groups["k"].Value, withReminder: false) is { } fk)
            return $"{fk}{J(fk, "을", "를")} 가지고 진화원이 없는 자신의 디지몬 전부";
        if ((m = Regex.Match(phrase, @"^(?<pre>(?:all of )?your) Tokens and (?<rest>.+Digimon.*)$", IC)).Success)
            return TargetKoCore($"{m.Groups["pre"].Value} Tokens or {m.Groups["rest"].Value}", bare);
        return null;
    }
}
