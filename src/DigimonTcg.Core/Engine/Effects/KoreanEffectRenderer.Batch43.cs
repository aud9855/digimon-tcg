using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.43.0 미지원 문장 17차 묶음의 생성 한글 — 원래 규칙(16차까지)이 옮기지 못한 절 · 문장만 (SentenceParser.Batch43과 같은 읽기)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth43;
    [ThreadStatic] private static int _fallbackDepth43;

    /// <summary>
    /// 영어 데이터가 깨진 링크 카드의 하단 문장 (SentenceParser.CardTextFix43와 같은 문장 — 원문이 정확히 같을 때만, 그 카드에만 있는 문장):
    /// BT23-009 · ST22-08 · EX10-024 · BT23-052
    /// </summary>
    private static readonly Dictionary<string, string> BrokenLinkText43 = new(StringComparer.Ordinal)
    {
        ["[Once Per Turn] This Digimon may attack a player."] = "[End of Your Turn] [Once Per Turn] This Digimon may attack a player.",
        ["[Once Per Turn] This Digimon may attack."] = "[End of Your Turn] [Once Per Turn] This Digimon may attack.",
        ["[When Attacking] By trashing 1 of this Digimon's link cards,"] = "[When Attacking] By trashing 1 of this Digimon's link cards, ＜De-Digivolve 1＞ 1 of your opponent's Digimon.",
        ["This Digimon gains and until your opponent's turn ends."] = "This Digimon gains ＜Reboot＞ and ＜Blocker＞ until your opponent's turn ends.",
    };

    // =====================================================================
    // 1. 절 (RenderClause 앞 · 끝)
    // =====================================================================

    /// <summary>깨진 원문이면 고친 문장을 옮긴다 (원래 규칙보다 먼저 — 깨진 원문의 한글은 틀리다)</summary>
    private static string? RenderBrokenText43(string rawClause)
    {
        string t = Regex.Replace(rawClause.Trim(), @"^Inherited Effect\s*", "", IC);
        if (!BrokenLinkText43.TryGetValue(t, out var fixedText)) return null;
        return RenderClauseCore(fixedText) ?? RenderClause40(fixedText) ?? RenderClause41(fixedText) ?? RenderClause42(fixedText);
    }

    private static string? RenderClause43(string rawClause)
    {
        if (_clauseDepth43 > 0) return null;
        _clauseDepth43++;
        try
        {
            string clause = Regex.Replace(SentenceParser.NormalizeForRender(rawClause), @"^(?:Inherited|Security) Effect\s*", "", IC).Trim();
            Match m;
            // 공식 BT17-078 "[패]【카운터】《블래스트 조그레스《「워그레이몬」+「메탈가루몬」》》(지정된 자신의 디지몬 1마리와 패의 지정된 카드 1장으로, 이 카드로 조그레스 진화할 수 있다)"
            if ((m = Regex.Match(rawClause.Trim(), @"^\[Hand\]\s*\[Counter\]\s*＜Blast DNA Digivolve\s*\(\s*(?<a>\[[^\]]+\])\s*\+\s*(?<b>\[[^\]]+\])\s*\)＞", IC)).Success)
                return $"[패]【카운터】《블래스트 조그레스《{NamesKo(m.Groups["a"].Value)}+{NamesKo(m.Groups["b"].Value)}》》(지정된 자신의 디지몬 1마리와 패의 지정된 카드 1장으로, 이 카드로 조그레스 진화할 수 있다)";
            // 공식 BT13-017 "【서로의 턴】명칭에 「시스터몬」을 포함하거나 특징으로 「로얄 나이츠」를 가진 다른 자신의 디지몬 1마리마다 자신의 디지몬 전부를 DP +1000"
            if ((m = SentenceParser.ForEachStaticRe43.Match(clause)).Success && CounterKo(m.Groups["cnt"].Value) is { } ck)
            {
                string subj = m.Groups["subj"].Value.Trim();
                string? who = Regex.IsMatch(subj, @"^(?:this Digimon|it)$", IC) ? "이 디지몬"
                    : TargetKo(Regex.Replace(subj, @"^all of your (?<x>\[[^\]]+\](?: or \[[^\]]+\])*)s?$", "all of your ${x} Digimon", IC));
                if (who != null)
                    return $"{LabelsKo40(m.Groups["labels"].Value)}{ck} {who}{J(who, "을", "를")} DP +{m.Groups["n"].Value} 한다.";
            }
            foreach (var alt in SentenceParser.ClauseRewrites43(rawClause, new CardData { Type = CardType.Digimon }))
                if (alt != rawClause && (RenderClauseCore(alt) ?? RenderClause40(alt) ?? RenderClause41(alt) ?? RenderClause42(alt)) is { } ko)
                    return ko;
            return null;
        }
        finally { _clauseDepth43--; }
    }

    // =====================================================================
    // 2. 문장 (Sentence 끝)
    // =====================================================================

    private static string? SentenceFallback43(string s, bool nested)
    {
        if (_fallbackDepth43 >= 3) return null;
        _fallbackDepth43++;
        try
        {
            s = s.Trim().TrimEnd('.').Trim();
            Match m;
            // 공식 BT21-084 "자신의 디지몬 1마리를 패의 디지몬 카드로 어플 합체할 수 있다"
            if ((m = Regex.Match(s, @"^(?:Then, )?(?<who>1 of your (?<spec>[^,]*?)Digimon(?<with> with [^,]+?)?) may app fuse into (?<pick>.+?) in (?:the|your) (?<from>hand|trash)$", IC)).Success)
            {
                string? who = TargetKo(m.Groups["who"].Value);
                string pickText = Regex.Replace(m.Groups["pick"].Value, @"\s*\(App Name\)", "", IC);
                string? what = Regex.IsMatch(pickText, @"^an? Digimon card$", IC) ? "디지몬 카드" : PickKo("1 " + Regex.Replace(pickText, @"^an? ", "", IC)) is { } pk ? Regex.Replace(pk, @"\s*1장$", "") : null;
                if (who == null || what == null) return null;
                string zone = m.Groups["from"].Value.Equals("trash", StringComparison.OrdinalIgnoreCase) ? "트래시" : "패";
                return $"{who}{J(who, "을", "를")} {zone}의 {what}{J(what, "으로", "로")} 어플 합체할 수 있다";
            }
            // 공식 EX6-006 "자신의 디지타마 덱 위에서부터 1장을 이 디지몬의 진화원 아래에 놓고, 자신의 디지몬 전부를 소멸시킨다"
            if (Regex.IsMatch(s, @"^Place the top card of your Digi-Egg deck (?:as this Digimon's bottom digivolution card|under this Digimon as its bottom digivolution card)$", IC))
                return "자신의 디지타마 덱 위에서부터 1장을 이 디지몬의 진화원 아래에 놓는다";
            if ((m = Regex.Match(s, @"^(?<a>Place the top card of your Digi-Egg deck .+?) and (?<b>delete .+)$", IC)).Success
                && Sentence(m.Groups["a"].Value, true) is { } pa && Sentence(char.ToUpperInvariant(m.Groups["b"].Value[0]) + m.Groups["b"].Value[1..], true) is { } pb)
                return $"{Regex.Replace(pa, @"놓는다$", "놓고")}, {pb}";
            // 공식 BT14-038 "자신의 트래시에서 「에테몬」 1장을 시큐리티 아래에 놓는다"
            if ((m = Regex.Match(s, @"^(?:You may )?[Pp]lace (?<pick>\d+ .+?) from your trash (?:at the (?<pos>bottom|top) of|on (?<pos>top) of|under) your security stack$", IC)).Success
                && PickKo(m.Groups["pick"].Value) is { } pk2)
                return $"자신의 트래시에서 {pk2}{J(pk2, "을", "를")} 시큐리티 {(m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase) ? "위" : "아래")}에 놓{(s.StartsWith("You may", StringComparison.OrdinalIgnoreCase) ? "을 수 있다" : "는다")}";
            // 공식 BT7-104 "그 디지몬이 갖는 진화원 1장마다 《1 드로우》"
            if ((m = Regex.Match(s, @"^(?:Then, )?(?<core>.+?) for (?:each|every) of that Digimon's digivolution cards$", IC)).Success && Sentence(m.Groups["core"].Value, true) is { } core)
                return $"그 디지몬이 갖는 진화원 1장마다 {core}";
            // "gains +1000 DP for the turn for each ~" (BT25-086) → "gets"
            if ((m = Regex.Match(s, @"^(?<t>.+?) gains (?<dp>[+-]\d+ DP .+)$", IC)).Success && Sentence($"{m.Groups["t"].Value} gets {m.Groups["dp"].Value}", nested) is { } g)
                return g;
            // "with the 13000 DP or more" (BT13-111)
            if (Regex.IsMatch(s, @"\bwith the \d+ DP or ", IC) && Sentence(Regex.Replace(s, @"\bwith the (?=\d+ DP or )", "with ", IC), nested) is { } w)
                return w;
            // 공식 BT26-090 "패의 특징 「TS」를 가진 옵션 카드 1장을 사용할 수 있다" (코스트를 낸다)
            if ((m = Regex.Match(s, @"^(?:(?<by>By [^,]+), )?(?<may>you may )?use (?<pick>\d+ .*?Option cards?.*?) from your hand(?: with the (?:use |memory )?cost reduced by (?<red>\d+))?$", IC)).Success
                && PickKo(m.Groups["pick"].Value) is { } op)
            {
                string by = "";
                if (m.Groups["by"].Success)
                {
                    if (CostKo(Regex.Replace(m.Groups["by"].Value, @"^By ", "", IC)) is not { } costKo) return null;
                    by = costKo + " ";
                }
                string red = m.Groups["red"].Success ? $"지불하는 코스트 -{m.Groups["red"].Value} 하여 " : "";
                return $"{by}자신의 패에서 {op}{J(op, "을", "를")} {red}사용{(m.Groups["may"].Success ? "할 수 있다" : "한다")}";
            }
            foreach (var alt in SentenceParser.Rewrites43(s))
                if (alt != s && Sentence(alt, nested) is { } ko) return ko;
            return null;
        }
        finally { _fallbackDepth43--; }
    }

    /// <summary>비용 — 공식 BT21-085 "특징 「아머체」를 가진 자신의 디지몬 1마리에 겹쳐져 있는 카드를 위에서부터 1장 파기하는 것으로,"</summary>
    private static string? CostKo43(string cost)
    {
        // 공식 EX9-057 "자신의 트래시와 디지몬의 진화원에서 「네가몬」 합계 4장을 디지타마 덱 아래로 되돌리는 것으로,"
        var rm = Regex.Match(cost.Trim(), @"^returning (?<n>\d+) (?<x>\[[^\]]+\]) from your trash or your Digimon's digivolution cards to the bottom of (?:the|your) Digi-Egg deck$", IC);
        if (rm.Success)
            return $"자신의 트래시와 디지몬의 진화원에서 {NamesKo(rm.Groups["x"].Value)} 합계 {rm.Groups["n"].Value}장을 디지타마 덱 아래로 되돌리는 것으로,";
        var m = Regex.Match(cost.Trim(), @"^trashing the top stacked card of (?:1|one) of your (?<t>.+)$", IC);
        if (m.Success && TargetKo("1 of your " + m.Groups["t"].Value) is { } t)
            return $"{t}에 겹쳐져 있는 카드를 위에서부터 1장 파기하는 것으로,";
        // "suspending this Tamer and trashing the top stacked card of ~" — 두 비용
        var two = Regex.Match(cost.Trim(), @"^(?<a>.+?) and (?<b>trashing the top stacked card of .+)$", IC);
        if (two.Success && CostKo(two.Groups["a"].Value) is { } a && CostKo43(two.Groups["b"].Value) is { } b && Regex.IsMatch(a, @"는 것으로,$"))
            return $"{Regex.Replace(a, @"는 것으로,$", "고,")} {b}";
        return null;
    }

    /// <summary>사건 — 공식 BT21-094 "특징 「아머체」를 가진 자신의 디지몬에 겹쳐져 있는 카드가 위에서부터 파기되었을 때"</summary>
    private static string? EventKo43(string ev)
    {
        // 공식 BT13-007 "특징으로 「로얄 나이츠」를 가진 옵션 카드가 배틀 에어리어에 놓였을 때"
        var om = Regex.Match(ev.Trim(), @"^an? Option card (?:with the (?<x>\[[^\]]+\](?:(?:,\s*|\s+)(?:or\s+|and/or\s+|and\s+)?\[[^\]]+\])*) traits? )?(?:is|are) placed in (?:the|your) battle area$", IC);
        if (om.Success)
            return om.Groups["x"].Success ? $"특징으로 {NamesKo(om.Groups["x"].Value, trait: true)}{J(NamesKo(om.Groups["x"].Value, trait: true), "을", "를")} 가진 옵션 카드가 배틀 에어리어에 놓였을 때," : "옵션 카드가 배틀 에어리어에 놓였을 때,";
        var m = Regex.Match(ev.Trim(), @"^the top stacked card of (?:any of |one of |1 of )?your (?<t>.+?) (?:is|gets) trashed$", IC);
        if (m.Success && TargetKo("all of your " + m.Groups["t"].Value, bare: true) is { } t)
            return $"{Regex.Replace(t, @"\s*전부$", "")}에 겹쳐져 있는 카드가 위에서부터 파기되었을 때,";
        return null;
    }

    /// <summary>조건 — 공식 BT8-100 "2색 이상인 자신의 디지몬 또는 진화원에 2색 이상인 카드를 가진 자신의 디지몬이 있다면"</summary>
    private static Cond? CondKo43(string cond)
    {
        if (Regex.IsMatch(cond.Trim(), @"^you have a Digimon with 2 or more colou?rs or with 2 or more colou?rs in its digivolution cards$", IC))
            return Have("2색 이상인 자신의 디지몬 또는 진화원에 2색 이상인 카드를 가진 자신의 디지몬이");
        if (Regex.IsMatch(cond.Trim(), @"^you have a Digimon in play with 2 or more colou?rs, or with 2 or more colou?rs in its digivolution cards$", IC))
            return Have("2색 이상인 자신의 디지몬 또는 진화원에 2색 이상인 카드를 가진 자신의 디지몬이");
        return null;
    }

    /// <summary>셈 — "상대의 메모리 1마다" (BT25-086) · "이 디지몬의 진화원의 색 1색마다" (AD1-013) · "~ 다른 자신의 디지몬 1마리마다" (BT13-017)</summary>
    private static string? CounterKo43(string counter)
    {
        string s = counter.Trim().TrimEnd('.');
        Match m;
        if (Regex.IsMatch(s, @"^(?:point of )?memory (?:your opponent has|on your opponent's side)$", IC)) return "상대의 메모리 1마다";
        if ((m = Regex.Match(s, @"^colou?r in (?:this (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) trait Digimon's|this Digimon's|its) digivolution cards$", IC)).Success)
            return m.Groups["x"].Success ? $"특징으로 {NamesKo(m.Groups["x"].Value, trait: true)}{J(NamesKo(m.Groups["x"].Value, trait: true), "을", "를")} 가진 이 디지몬의 진화원의 색 1색마다" : "이 디지몬의 진화원의 색 1색마다";
        if ((m = Regex.Match(s, @"^other (?<x>(?:[^,]*? )?Digimon with .+?) you have(?: in play)?$", IC)).Success
            && TargetKo("all of your " + m.Groups["x"].Value, bare: true) is { } tk)
            return Regex.Replace(Regex.Replace(tk, @"\s*전부$", ""), @"자신의 디지몬$", "다른 자신의 디지몬") + " 1마리마다";
        return null;
    }
}
