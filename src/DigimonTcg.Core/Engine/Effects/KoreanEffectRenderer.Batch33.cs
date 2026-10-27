using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.33.0 미지원 문장 7차 묶음의 생성 한글 (SentenceParser.Batch33 · Batch33b와 같은 범위) — 공식 한글 표기를 따른다:
/// "블루인 다른 자신의 디지몬 1마리를 이 디지몬의 진화원 아래에 놓는 것으로," (BT15-029) · "이 디지몬을 특징으로 「Legend-Arms」를 가지거나 블랙인 자신의 다른 디지몬 1마리의 진화원 아래에 놓는 것으로," (ST13-03) ·
/// "이 테이머와 자신의 트래시의 「가르고몬」과 「래피드몬」 1장씩을 자신의 「테리어몬」 1마리의 진화원 아래에 놓는 것으로," (ST17-10) ·
/// "진화원을 갖지 않은 디지몬 1마리를 시큐리티 아래에 놓는 것으로," (EX8-028) · "이 디지몬에 겹쳐져 있는 카드를 위에서부터 1장 시큐리티 위에 놓는 것으로," (P-153) ·
/// "그 디지몬을 패의 「메탈가루몬」으로 진화 조건을 무시하고 코스트를 지불하지 않고 진화시킬 수 있다" (BT15-091) · "그 테이머를 이 카드로 진화 조건을 무시하고 진화 코스트 3으로 진화시킨다" (BT18-053) ·
/// "이 디지몬에 Lv.이 같은 카드가 2장 이상 겹쳐져 있다면," (BT22-047) · "이 디지몬의 진화원에 특징으로 「디지대」를 가진 테이머 카드가 있다면," (BT15-058) ·
/// "배틀 이외로 소멸하고 있었다면," (BT19-006) · "이 디지몬이 효과로 등장하고 있었다면," (BT15-022) · "진화원 매수가 이 디지몬 이상의 상대의 디지몬이 없다면," (BT14-029)
/// </summary>
public static partial class KoreanEffectRenderer
{
    /// <summary>지금 그리는 절의 "When ~,"이 사건의 디지몬이 있는 사건인지 (뒤 조건의 "it" = 그 디지몬)</summary>
    [ThreadStatic] private static bool _eventOther;
    /// <summary>지금 그리는 절의 주어가 이 테이머인지 ("If this Tamer has ~, it may digivolve ~")</summary>
    [ThreadStatic] private static bool _tamerSubject;

    // =====================================================================
    // 비용
    // =====================================================================

    /// <summary>비용 (CostKo32 다음)</summary>
    private static string? CostKo33(string cost)
    {
        Match m;
        string fixedCost = SentenceParser.NormalizeCost33(cost);
        if (fixedCost != cost) return CostKo(fixedCost);

        // 이 디지몬 · 이 테이머 → 다른 자신의 디지몬의 진화원 아래 (ST13-03 · BT10-059 · BT22-018 · P-142 · P-112 · BT13-103 · EX10-066)
        if ((m = Regex.Match(cost, @"^placing this (?<what>Digimon(?: card)?|Tamer|card) (?<dest>(?:as|under) .+)$", RegexOptions.IgnoreCase)).Success
            && PlaceDestKo33(m.Groups["dest"].Value) is { } d1 && !d1.StartsWith("이 디지몬", StringComparison.Ordinal))
        {
            if (d1.EndsWith("의 진화원 아래")) _mentionedTarget = true;   // 뒤 문장의 "it" · "that Digimon" = 놓은 곳의 디지몬
            return $"{(m.Groups["what"].Value.StartsWith("Tamer", StringComparison.OrdinalIgnoreCase) ? "이 테이머를" : "이 디지몬을")} {d1}에 놓는 것으로,";
        }
        if (Regex.IsMatch(cost, @"^placing this Digimon as its bottom digivolution card$", RegexOptions.IgnoreCase))
            return "이 디지몬을 그 디지몬의 진화원 아래에 놓는 것으로,";
        if (Regex.IsMatch(cost, @"^returning this Tamer to (?:the|your|its owner's) hand$", RegexOptions.IgnoreCase))
            return "이 테이머를 패로 되돌리는 것으로,";
        // 공식 (EX6-003): "자신의 시큐리티를 위에서부터 1장 패에 추가하는 것으로,"
        if (Regex.IsMatch(cost, @"^adding (?:the top card of your security stack|your top security card) to (?:the|your) hand$", RegexOptions.IgnoreCase))
            return "자신의 시큐리티를 위에서부터 1장 패에 추가하는 것으로,";
        // 이 테이머와 트래시의 [X] · [Y] 1장씩 (ST17-10 · BT12-089 · BT17-080 · BT17-085)
        if ((m = Regex.Match(cost, @"^placing this Tamer and (?:1 )?(?<a>\[[^\]]+\]) and (?:1 )?(?<b>\[[^\]]+\]) from your trash(?: in any order)? (?<dest>.+?)(?: in any order)?$", RegexOptions.IgnoreCase)).Success
            && PlaceDestKo33(Regex.Replace(m.Groups["dest"].Value, @"\bdigivolution cards\b", "digivolution card", RegexOptions.IgnoreCase)) is { } d2)
            return $"이 테이머와 자신의 트래시의 {PairKo33(m.Groups["a"].Value, m.Groups["b"].Value)} 1장씩을 {d2}에 놓는 것으로,";
        // 패 · 트래시의 [X] · [Y] 1장씩 (BT15-091 · BT14-090 · BT18-053 · BT12-015 · P-022)
        if ((m = Regex.Match(cost, @"^placing (?:1 )?(?<a>\[[^\]]+\]) and (?:1 )?(?<b>\[[^\]]+\]) from your (?<z>trash|hand)(?<ord> in any order)? (?<dest>.+?)(?<ord2> in any order)?$", RegexOptions.IgnoreCase)).Success
            && PlaceDestKo33(Regex.Replace(m.Groups["dest"].Value, @"\bdigivolution cards\b", "digivolution card", RegexOptions.IgnoreCase)) is { } d3)
        {
            bool hand = m.Groups["z"].Value.Equals("hand", StringComparison.OrdinalIgnoreCase);
            string pair = PairKo33(m.Groups["a"].Value, m.Groups["b"].Value);
            if (d3 == "덱 아래") return $"자신의 {(hand ? "패" : "트래시")}의 {pair} 1장씩을 원하는 순서대로 덱 아래로 되돌리는 것으로,";
            return $"자신의 {(hand ? "패" : "트래시")}에서 {pair} 1장씩을 {d3}에 놓는 것으로,";
        }
        // 다른 자신의 디지몬 · 테이머 → 이 디지몬(다른 디지몬)의 진화원 아래 / 덱 아래 (BT15-029 · BT22-028 · BT27-031 · BT23-040 · BT26-097 · P-024 · BT11-072)
        if ((m = Regex.Match(cost, @"^placing (?<t>(?:1|one) of your .+?) (?<dest>(?:as|under|at) .+)$", RegexOptions.IgnoreCase)).Success
            && PlaceDestKo33(m.Groups["dest"].Value) is { } d4 && PieceKo33(m.Groups["t"].Value) is { } piece
            && SetMentioned33(!d4.StartsWith("이 디지몬", StringComparison.Ordinal)))
            return d4 == "덱 아래" ? $"{piece}{J(piece, "을", "를")} 덱 아래로 되돌리는 것으로," : $"{piece}{J(piece, "을", "를")} {d4}에 놓는 것으로,";
        // 패 · 트래시의 카드 → 테이머 아래 / 다른 자신의 디지몬의 진화원 아래 / 이 디지몬의 진화원 위
        if ((m = Regex.Match(cost, @"^placing (?<pick>.+?) (?:from|in) (?<from>your hand or (?:your )?trash|your trash or (?:your )?hand|your hand|your trash) (?<fd>face down )?(?<dest>(?:as|under|on top of) .+?)(?: in any order)?$", RegexOptions.IgnoreCase)).Success
            && PickKo(m.Groups["pick"].Value) is { } pk && PlaceDestKo33(m.Groups["dest"].Value) is { } d5)
        {
            string from = m.Groups["from"].Value.ToLowerInvariant();
            string zone = from.Contains("hand") && from.Contains("trash") ? "자신의 패/트래시에서" : from.Contains("hand") ? "자신의 패에서" : "자신의 트래시에서";
            return $"{zone} {pk}{J(pk, "을", "를")} {d5}에 {(m.Groups["fd"].Success ? "뒷면으로 " : "")}놓는 것으로,";
        }
        // 배틀 에어리어의 이 카드 · 시큐리티 맨 위 카드 · 진화원 → 테이머 아래 (ST23-15 · BT26-025 · P-152 · BT10-009)
        if ((m = Regex.Match(cost, @"^placing this card from the battle area (?<fd>face down )?(?<dest>under .+)$", RegexOptions.IgnoreCase)).Success && PlaceDestKo33(m.Groups["dest"].Value) is { } d6)
            return $"배틀 에어리어의 이 카드를 {d6}에 {(m.Groups["fd"].Success ? "뒷면으로 " : "")}놓는 것으로,";
        if ((m = Regex.Match(cost, @"^placing your top security card (?<fd>face down )?(?<dest>under .+)$", RegexOptions.IgnoreCase)).Success && PlaceDestKo33(m.Groups["dest"].Value) is { } d7)
            return $"자신의 시큐리티를 위에서부터 1장 {d7}에 {(m.Groups["fd"].Success ? "뒷면으로 " : "")}놓는 것으로,";
        if ((m = Regex.Match(cost, @"^placing (?:(?<all>all digivolution cards) from under this Digimon|(?<pick>.+?) (?:in|from) this Digimon's digivolution cards) (?<dest>under .+)$", RegexOptions.IgnoreCase)).Success
            && PlaceDestKo33(m.Groups["dest"].Value) is { } d8)
        {
            if (m.Groups["all"].Success) return $"이 디지몬의 진화원 전부를 {d8}에 놓는 것으로,";
            if (SourcePickKo32(m.Groups["pick"].Value) is { } sp) return $"이 디지몬의 진화원에서 {sp}{J(sp, "을", "를")} {d8}에 놓는 것으로,";
        }
        // 트래시 · 패 → 이 디지몬의 진화원 위 (EX10-021 · BT13-088 · EX9-057)
        if ((m = Regex.Match(cost, @"^placing (?<pick>.+?) from (?<from>your trash|your hand or (?:your )?trash|your hand) (?:as this Digimon's top digivolution cards?|on top of this (?:Digimon|card)'s digivolution cards)$", RegexOptions.IgnoreCase)).Success
            && PickKo(m.Groups["pick"].Value) is { } tp)
        {
            string from = m.Groups["from"].Value.ToLowerInvariant();
            string zone = from.Contains("hand") && from.Contains("trash") ? "자신의 패/트래시에서" : from.Contains("hand") ? "자신의 패에서" : "자신의 트래시에서";
            return $"{zone} {tp}{J(tp, "을", "를")} 이 디지몬의 진화원 위에 놓는 것으로,";
        }
        // 시큐리티: 이 디지몬 (BT26-022 · BT19-048) · 디지몬 1마리 (EX8-028 · EX9-056 · ST22-06 · BT18-034 · LM-020 · BT25-044 · BT24-040)
        if ((m = Regex.Match(cost, @"^placing this Digimon as the (?<fu>face-up )?(?<pos>top|bottom) security card$", RegexOptions.IgnoreCase)).Success)
            return $"이 디지몬을 시큐리티 {PosKo(m.Groups["pos"].Value)}에 {(m.Groups["fu"].Success ? "앞면으로 " : "")}놓는 것으로,";
        if ((m = Regex.Match(cost, @"^placing (?<t>(?:1|one) .+?) (?:as the (?<pos>top|bottom) security card|on (?<pos>top) of (?:its owner's|your|their owner's) security stack)$", RegexOptions.IgnoreCase)).Success
            && SecurityPieceKo33(m.Groups["t"].Value) is { } sec)
            return $"{sec}{J(sec, "을", "를")} 시큐리티 {PosKo(m.Groups["pos"].Value)}에 놓는 것으로,";
        // 맨 위 카드 → 시큐리티 (EX13-032 · P-153 · BT9-044 · BT17-098)
        if ((m = Regex.Match(cost, @"^placing (?:(?<self>its top stacked card|this Digimon's top (?:stacked )?card|the top (?:stacked )?card of this Digimon)|the top (?:stacked )?card of (?<t>(?:1|one|any) of your .+?)) (?:as (?:the|your) (?<pos>top|bottom) security card|on (?<pos>top) of your security stack)(?<fd> face down)?$", RegexOptions.IgnoreCase)).Success)
        {
            string? who = m.Groups["t"].Success ? TargetKo(Regex.Replace(m.Groups["t"].Value, @"^(?:any|one) of", "all of", RegexOptions.IgnoreCase), bare: true) : "이 디지몬";
            if (who == null) return null;
            return $"{who}에 겹쳐져 있는 카드를 위에서부터 1장 {(m.Groups["t"].Success ? "" : "자신의 ")}시큐리티 {PosKo(m.Groups["pos"].Value)}에 {(m.Groups["fd"].Success ? "뒤집어서 " : "")}놓는 것으로,";
        }
        // 패 → 시큐리티 (EX5-054)
        if ((m = Regex.Match(cost, @"^placing (?<pick>.+?) from your hand (?:on (?<pos>top) of your security stack|at the (?<pos>top|bottom|top or bottom) of your security stack|as (?:the|your) (?<pos>top|bottom|top or bottom) security card)$", RegexOptions.IgnoreCase)).Success
            && PickKo(m.Groups["pick"].Value) is { } hp)
            return $"자신의 패에서 {hp}{J(hp, "을", "를")} 시큐리티 {PosKo(m.Groups["pos"].Value)}에 놓는 것으로,";
        // 다른 디지몬의 맨 위 카드 → 그 진화원 아래 (P-225)
        if ((m = Regex.Match(cost, @"^placing the top (?:stacked )?card of (?<t>(?:any|1|one) of your .+?) as its bottom digivolution card$", RegexOptions.IgnoreCase)).Success
            && TargetKo(Regex.Replace(m.Groups["t"].Value, @"^(?:any|one) of", "1 of", RegexOptions.IgnoreCase), bare: true) is { } topWho)
            return $"{topWho}에 겹쳐져 있는 카드를 위에서부터 1장 그 디지몬의 진화원 아래에 놓는 것으로,";
        // 다른 디지몬의 진화원 → 덱 아래 (BT11-070 · P-094)
        if ((m = Regex.Match(cost, @"^placing (?<pick>.+?) from (?<t>(?:1|one|any) of your .+?)'s digivolution cards (?:at|to) the bottom of (?:the|your|its owner's|their owners'?) decks?$", RegexOptions.IgnoreCase)).Success
            && SourcePickKo32(m.Groups["pick"].Value) is { } vp && (TargetKo(Regex.Replace(m.Groups["t"].Value, @"^(?:any|one) of", "1 of", RegexOptions.IgnoreCase))
                ?? (Regex.Match(m.Groups["t"].Value, $@"^(?:any|1|one) of your (?<x>{TargetSpec.NameList})s?$", RegexOptions.IgnoreCase) is { Success: true } vn ? $"자신의 {NamesKo(vn.Groups["x"].Value)} 1마리" : null)) is { } vw)
            return $"{vw}의 진화원에서 {vp}{J(vp, "을", "를")} 덱 아래로 되돌리는 것으로,";
        return null;
    }

    /// <summary>뒤 문장의 "it"이 그 디지몬인지 (놓은 곳이 이 디지몬이면 이 디지몬)</summary>
    private static bool SetMentioned33(bool value) { _mentionedTarget = value; return true; }

    private static string PosKo(string pos) => pos.ToLowerInvariant() switch { "top" => "위", "bottom" => "아래", _ => "위 또는 아래" };

    /// <summary>[X] · [Y] → 「X」과 「Y」</summary>
    private static string PairKo33(string a, string b)
    {
        string x = NamesKo(a), y = NamesKo(b);
        return $"{x}{J(x.Trim('「', '」'), "과", "와")} {y}";
    }

    /// <summary>놓는 곳 → "이 디지몬의 진화원 아래" · "자신의 「파피몬」 1마리의 진화원 아래" · "특징으로 「글로잉 던」을 가진 자신의 테이머 아래" · "이 테이머 아래" · "덱 아래"</summary>
    private static string? PlaceDestKo33(string dest)
    {
        dest = dest.Trim();
        Match m;
        if (Regex.IsMatch(dest, @"^(?:as (?:this Digimon's|its) bottom digivolution cards?|as the bottom digivolution cards? of this Digimon|at the bottom of this (?:Digimon|card)'s digivolution cards|under this Digimon as its bottom digivolution cards?)$", RegexOptions.IgnoreCase))
            return "이 디지몬의 진화원 아래";
        if (Regex.IsMatch(dest, @"^(?:as this Digimon's top digivolution cards?|on top of this (?:Digimon|card)'s digivolution cards)$", RegexOptions.IgnoreCase))
            return "이 디지몬의 진화원 위";
        if (Regex.IsMatch(dest, @"^at the bottom of (?:your|the|its owner's) deck$", RegexOptions.IgnoreCase))
            return "덱 아래";
        if (Regex.IsMatch(dest, @"^under this Tamer$", RegexOptions.IgnoreCase)) return "이 테이머 아래";
        if ((m = Regex.Match(dest, $@"^under (?:any|1|one) of your (?:(?<names>{TargetSpec.NameList})s?|(?<spec>[^,]*?)Tamers?(?: with (?<w>.+))?)$", RegexOptions.IgnoreCase)).Success)
        {
            bool one = Regex.IsMatch(dest, @"^under (?:1|one) of", RegexOptions.IgnoreCase);
            string tail = one ? " 1명의 아래" : " 아래";
            if (m.Groups["names"].Success) return $"자신의 {NamesKo(m.Groups["names"].Value)}{tail}";
            string spec = m.Groups["spec"].Value.Trim();
            var mods = new List<string>();
            Match s;
            if ((s = Regex.Match(spec, $@"^(?<x>{TargetSpec.NameList}) trait$", RegexOptions.IgnoreCase)).Success) { string x = NamesKo(s.Groups["x"].Value, trait: true); mods.Add($"특징으로 {x}{J(x, "을", "를")} 가진"); }
            else if ((s = Regex.Match(spec, $@"^(?<c>(?:{Colors})(?: or (?:{Colors}))*)$", RegexOptions.IgnoreCase)).Success) mods.Add($"{ColorsKo(s.Groups["c"].Value)}인");
            else if (spec.Length > 0) return null;
            if (m.Groups["w"].Success)
            {
                if (WithKo("with " + Regex.Replace(m.Groups["w"].Value, @"^with ", "", RegexOptions.IgnoreCase)) is { } wk) mods.Add(wk);
                else return null;
            }
            return $"{(mods.Count > 0 ? string.Join(" ", mods) + " " : "")}자신의 테이머{tail}";
        }
        if ((m = Regex.Match(dest, @"^(?:as (?<t>(?:any|1|one) of your .+?)'s? bottom digivolution cards?|as the bottom digivolution cards? of (?<t>(?:any|1|one) of your .+?)|under (?<t>(?:any|1|one) of your .+?) as (?:its|their) bottom digivolution cards?)$", RegexOptions.IgnoreCase)).Success)
        {
            string t = m.Groups["t"].Value.Trim();
            bool any = Regex.IsMatch(t, @"^any of", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"^(?:any|one) of your", "1 of your", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\s+(?:in play|on the field)$", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\]s\b", "]");
            t = Regex.Replace(t, @"\bin any of their traits\b", "in any of its traits", RegexOptions.IgnoreCase);
            string? who;
            if ((m = Regex.Match(t, $@"^1 of your (?<other>other )?Digimon that's (?<c>{Colors}) or has the (?<x>{TargetSpec.NameList}) traits?$", RegexOptions.IgnoreCase)).Success)
            {
                string x = NamesKo(m.Groups["x"].Value, trait: true);
                who = $"특징으로 {x}{J(x, "을", "를")} 가지거나 {ColorKo(m.Groups["c"].Value)}인 자신의 {(m.Groups["other"].Success ? "다른 " : "")}디지몬 1마리";
            }
            else if ((m = Regex.Match(t, $@"^1 of your (?<x>{TargetSpec.NameList})$", RegexOptions.IgnoreCase)).Success)
                who = $"자신의 {NamesKo(m.Groups["x"].Value)} 1마리";
            else who = TargetKo(t);
            if (who == null) return null;
            if (any) who = Regex.Replace(who, @" 1마리$", "");
            return $"{who}의 진화원 아래";
        }
        return null;
    }

    /// <summary>놓는 자신의 디지몬 · 테이머 → "블루인 다른 자신의 디지몬 1마리" · "자신의 「미시마 에리카」 1명" · "다른 자신의 디지몬/테이머 1마리(명)"</summary>
    private static string? PieceKo33(string phrase)
    {
        string p = Regex.Replace(phrase.Trim(), @"^(?:one|any) of your\b", "1 of your", RegexOptions.IgnoreCase);
        p = Regex.Replace(p, @"\bin play (?=\[)", "", RegexOptions.IgnoreCase);
        p = Regex.Replace(p, @"\]s$", "]");
        Match m;
        if ((m = Regex.Match(p, $@"^1 of your (?<x>{TargetSpec.NameList})$", RegexOptions.IgnoreCase)).Success)
        {
            // 테이머 이름이면 "1명" (공식 BT23-040 · BT11-072)
            bool tamer = TargetSpec.Names(m.Groups["x"].Value).All(IsTamerName33);
            return $"자신의 {NamesKo(m.Groups["x"].Value)} 1{(tamer ? "명" : "마리")}";
        }
        if ((m = Regex.Match(p, $@"^1 of your Tamers? with (?<x>{TargetSpec.NameList}) in (?:its|their) names?$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return $"명칭에 {x}{J(x, "을", "를")} 포함하는 자신의 테이머 1명"; }
        if (Regex.IsMatch(p, @"^1 of your other Digimon or Tamers?$", RegexOptions.IgnoreCase)) return "다른 자신의 디지몬/테이머 1마리(명)";
        if (TargetKo(p) is { } t) return Regex.Replace(t, @"(자신|상대)의 다른 ", "다른 $1의 ");
        return null;
    }

    /// <summary>시큐리티에 놓는 디지몬 → "진화원을 갖지 않은 디지몬 1마리" · "다른 디지몬 1마리" · "가장 DP가 낮은 상대의 디지몬 1마리" · "Lv.6의 자신의 디지몬 1마리"</summary>
    private static string? SecurityPieceKo33(string phrase)
    {
        string p = Regex.Replace(phrase.Trim(), @"^one\b", "1", RegexOptions.IgnoreCase);
        if (Regex.IsMatch(p, @"\bTamers?\b|(?<!digivolution )\bcards?\b|\bfrom\b", RegexOptions.IgnoreCase)) return null;
        if (TargetKo(p) is not { } t) return null;
        t = Regex.Replace(t, @"Lv\.(\d+)인 ", "Lv.$1의 ");
        return Regex.Replace(t, @"(자신|상대)의 다른 ", "다른 $1의 ");
    }

    internal static bool IsTamerName33(string name) =>
        Regex.IsMatch(name, @"^(?:Takuya Kanbara|Koji Minamoto|Erika Mishima|Analogman|Tai Kamiya|Matt Ishida|Davis Motomiya|Ken Ichijoji|Shu-Chong Wong|Kosuke Kisakata|Kyoko Kuremi|Dan Yuki|Kanan Yuki|Menoa Bellucci|Takato Matsuki|Marcus Damon|Nene Amano|Shuu Yulin|Zoe Orimoto|Tommy Himi|Koichi Kimura)$", RegexOptions.IgnoreCase);

    // =====================================================================
    // 조건
    // =====================================================================

    /// <summary>조건 (CondKo32 다음)</summary>
    private static Cond? CondKo33(string cond)
    {
        Match m;
        string c0 = cond.Trim();
        // 표기 정리 (SentenceParser.ParseDigimonCondition33과 같다)
        string fx = Regex.Replace(c0, @"^(?:this Digimon|it) had\b", "this Digimon has", RegexOptions.IgnoreCase);
        fx = Regex.Replace(fx, @"\bin its name in its name$", "in its name", RegexOptions.IgnoreCase);
        fx = Regex.Replace(fx, @"\bIs in\b", "is in");
        if (fx != c0 && !Regex.IsMatch(c0, @"^(?:this Digimon|it) had\b", RegexOptions.IgnoreCase)) return CondKo(fx);
        bool had = Regex.IsMatch(c0, @"^(?:this Digimon|it) had\b", RegexOptions.IgnoreCase);
        if (had && CondKo(fx) is { } past)   // [소멸 시] "이 디지몬이 특징으로 「종족불명」을 가지고 있었다면," (공식 BT17-053)
        {
            string pt = Regex.Replace(Regex.Replace(past.Then, @"가진다면,$|가질 때,$", "가지고 있었다면,"), @"포함될 때,$", "포함되어 있었다면,");
            return new(pt, past.While, pt);
        }

        // 스택 — 공식 (BT22-047): "이 디지몬에 Lv.이 같은 카드가 2장 이상 겹쳐져 있다면,"
        if ((m = Regex.Match(c0, @"^this Digimon's stack has (?<n>\d+) or more same-level cards$", RegexOptions.IgnoreCase)).Success)
            return Have33($"이 디지몬에 Lv.이 같은 카드가 {m.Groups["n"].Value}장 이상 겹쳐져");
        // 진화원 안의 카드 — 공식 (BT15-058 · BT7-011 · EX1-019 · P-145): "이 디지몬의 진화원에 특징으로 「디지대」를 가진 테이머 카드가 있다면,"
        if ((m = Regex.Match(c0, @"^(?<p>.+?) (?:is |are |was |were )?in (?:this Digimon's|its) digivolution cards?$", RegexOptions.IgnoreCase)).Success
            && SourcePickCondKo33(m.Groups["p"].Value) is { } sp)
            return m.Groups[0].Value.Contains(" was in ") || m.Groups[0].Value.Contains(" were in ")
                ? new($"이 디지몬의 진화원에 {sp}{J(sp, "이", "가")} 있었을 때,", $"이 디지몬의 진화원에 {sp}{J(sp, "이", "가")} 있었던 동안,", $"이 디지몬의 진화원에 {sp}{J(sp, "이", "가")} 있었다면,")
                : Have($"이 디지몬의 진화원에 {sp}{J(sp, "이", "가")}");
        // "this Digimon has a red digivolution card" · "~ a purple card in its digivolution cards" · "~ an [Agumon] digivolution card" · "~ a non-red card ~" · "~ 4 or more level 6 cards ~"
        //  — 공식 (BT12-030 · BT8-082 · P-010 · BT10-001): "이 디지몬의 진화원에 블루인 카드가 있다면," · "~ 「아구몬」이 있는 동안," · "~ 레드 이외의 카드가 있는 동안,"
        if ((m = Regex.Match(c0, @"^(?:this Digimon|it) has (?:an? |(?<n>\d+) or more )(?<p>.+?) (?:digivolution cards?|cards? in its digivolution cards)$", RegexOptions.IgnoreCase)).Success
            && SourcePickCondKo33(Regex.Replace(m.Groups["p"].Value, @"\bcards?$", "", RegexOptions.IgnoreCase).Trim() + " card") is { } hp)
        {
            string body = m.Groups["n"].Success ? $"{hp}{J(hp, "이", "가")} {m.Groups["n"].Value}장 이상" : $"{hp}{J(hp, "이", "가")}";
            return Have($"이 디지몬의 진화원에 {body}");
        }
        if ((m = Regex.Match(c0, @"^(?:this Digimon|it) has (?<n>\d+) digivolution cards?$", RegexOptions.IgnoreCase)).Success)
            return Be($"이 디지몬의 진화원이 {m.Groups["n"].Value}장");
        if ((m = Regex.Match(c0, @"^there are (?<n>\d+) or more digivolution cards under this Digimon$", RegexOptions.IgnoreCase)).Success)
            return Have($"이 디지몬의 진화원이 {m.Groups["n"].Value}장 이상");
        if ((m = Regex.Match(c0, $@"^there are (?<n>\d+) or more (?<x>{TargetSpec.NameList}) in this Digimon's digivolution cards$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return Have($"이 디지몬의 진화원에 {x}{J(x, "이", "가")} {m.Groups["n"].Value}장 이상"); }
        if ((m = Regex.Match(c0, @"^(?:this Digimon|it) has (?<n>\d+)(?<more> or more)? link cards?$", RegexOptions.IgnoreCase)).Success)
            return Be($"이 디지몬의 링크 카드가 {m.Groups["n"].Value}장{(m.Groups["more"].Success ? " 이상" : "")}");
        // 공식 (BT17-102): "이 디지몬의 명칭이 「코로몬」이라면,"
        if ((m = Regex.Match(c0, $@"^this Digimon's name is (?<x>{TargetSpec.NameList})$", RegexOptions.IgnoreCase)).Success)
            return Be($"이 디지몬의 명칭이 {NamesKo(m.Groups["x"].Value)}");
        // 공식 (EX3-005 · EX3-047): "이 디지몬이 【등장 시】 효과를 가진다면," · "~ 가진 동안,"
        if ((m = Regex.Match(c0, @"^(?:this Digimon|it) has an? \[(?<t>On Play|On Deletion|When Digivolving|When Attacking|Main)\] effect$", RegexOptions.IgnoreCase)).Success)
        {
            string l = LabelKo(m.Groups["t"].Value) ?? m.Groups["t"].Value;
            return new($"이 디지몬이 {l} 효과를 가질 때,", $"이 디지몬이 {l} 효과를 가진 동안,", $"이 디지몬이 {l} 효과를 가진다면,");
        }
        if ((m = Regex.Match(c0, $@"^this Digimon is (?<c>{Colors}) and has (?<n>\d+) DP or (?<dir>more|less)$", RegexOptions.IgnoreCase)).Success)
            return Be($"이 디지몬이 {ColorKo(m.Groups["c"].Value)}이고 DP {m.Groups["n"].Value} {Dir(m.Groups["dir"].Value)}");
        if ((m = Regex.Match(c0, $@"^this Digimon has (?:the )?(?<x>{TargetSpec.NameList}) traits? and is level (?<n>\d+) or (?<dir>higher|lower)$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value, trait: true);
            return Be($"이 디지몬이 특징으로 {x}{J(x, "을", "를")} 가진 Lv.{m.Groups["n"].Value} {Dir(m.Groups["dir"].Value)}의 디지몬");
        }
        // "this Digimon has [Imperialdramon] in its name or the [Free] trait" — 공식 (ST12-04 · EX3-006): "이 디지몬이 명칭에 「헉몬」을 포함하거나 특징으로 「로얄 나이츠」를 가진 동안,"
        if ((m = Regex.Match(c0, @"^(?:this Digimon|it) has (?<rest>(?:the )?\[.+)$", RegexOptions.IgnoreCase)).Success
            && Regex.IsMatch(m.Groups["rest"].Value, @"\b(?:in (?:its|any of its) (?:names?|texts?|traits)|traits?)\b", RegexOptions.IgnoreCase)
            && FiltersKo("with " + Regex.Replace(Regex.Replace(m.Groups["rest"].Value, @"\bor \[(?=[^\]]+\] traits?$)", "or the [", RegexOptions.IgnoreCase), @"^the ", "", RegexOptions.IgnoreCase)) is { Count: > 0 } fk)
        {
            string desc = string.Join(" ", fk);
            string stem = Regex.Replace(desc, @"(?:가진|갖는)$", "가지");
            stem = Regex.Replace(stem, @"포함하는$", "포함하");
            stem = Regex.Replace(stem, @"있는$", "있");
            return new($"이 디지몬이 {desc} 디지몬일 때,", $"이 디지몬이 {stem}는 동안,".Replace("가지는 동안", "가진 동안"), $"이 디지몬이 {stem}{(stem.EndsWith("가지") ? "는" : "")}다면,".Replace("가지는다면", "가진다면"));
        }

        // ---- 자신 · 상대 필드 ----
        // 공식 (BT13-002 · BT18-002 · BT10-049 · EX4-025): "다른 자신의 디지몬이 있는 동안," · "블루인 다른 자신의 디지몬이 있는 동안," · "특징으로 「크로스 하트」를 가진 자신의 다른 디지몬 또는 테이머가 있는 동안,"
        if ((m = Regex.Match(Regex.Replace(c0, @"\s+in play$", "", RegexOptions.IgnoreCase), @"^you have another (?<rest>.+)$", RegexOptions.IgnoreCase)).Success)
        {
            string rest = m.Groups["rest"].Value;
            Match r;
            if ((r = Regex.Match(rest, $@"^Digimon or Tamer with the (?<x>{TargetSpec.NameList}) traits?$", RegexOptions.IgnoreCase)).Success)
            { string x = NamesKo(r.Groups["x"].Value, trait: true); return Have($"특징으로 {x}{J(x, "을", "를")} 가진 자신의 다른 디지몬 또는 테이머가"); }
            if (TargetKo("all of your other " + rest, bare: true) is { } ot)
            {
                string s = Regex.Replace(ot, @"(자신|상대)의 다른 ", "다른 $1의 ");
                return Have($"{s}{J(s, "이", "가")}");
            }
        }
        if (Regex.IsMatch(c0, @"^your opponent has no Digimon with (?:with )?as many or more digivolution cards (?:as|than) this Digimon$", RegexOptions.IgnoreCase))
            return NotHave("진화원 매수가 이 디지몬 이상의 상대의 디지몬이");
        if ((m = Regex.Match(c0, @"^(?:they have|your opponent has) (?<n>\d+) or (?<dir>more|fewer|less) cards in (?:their )?hand$|^(?:their|your opponent's) hand has (?<n>\d+) or (?<dir>more|fewer|less) cards$", RegexOptions.IgnoreCase)).Success)
            return Be($"상대의 패가 {m.Groups["n"].Value}장 {Dir(m.Groups["dir"].Value)}");
        if ((m = Regex.Match(c0, @"^they have (?<n>\d+) or (?<dir>more|fewer|less) cards in their trash$", RegexOptions.IgnoreCase)).Success)
            return Be($"상대의 트래시가 {m.Groups["n"].Value}장 {Dir(m.Groups["dir"].Value)}");
        if ((m = Regex.Match(c0, @"^they have (?<n>\d+) or (?<dir>more|fewer|less) security cards$", RegexOptions.IgnoreCase)).Success)
            return Be($"상대의 시큐리티가 {m.Groups["n"].Value}장 {Dir(m.Groups["dir"].Value)}");
        if ((m = Regex.Match(c0, @"^they have (?<n>\d+) or (?<dir>more|fewer|less) memory$", RegexOptions.IgnoreCase)).Success)
            return Be($"상대의 메모리가 {m.Groups["n"].Value} {Dir(m.Groups["dir"].Value)}");
        // 공식 (EX3-062): "자신/상대의 트래시가 5장 이상 있다면," · (EX4-006) "서로의 트래시 합계가 20장 이상이라면,"
        if ((m = Regex.Match(c0, @"^either player has (?<n>\d+) or more cards in their trash$", RegexOptions.IgnoreCase)).Success)
            return Have($"자신/상대의 트래시가 {m.Groups["n"].Value}장 이상");
        if ((m = Regex.Match(c0, @"^the total trashes of both players'? add up to (?<n>\d+) or more cards$", RegexOptions.IgnoreCase)).Success)
            return Be($"서로의 트래시 합계가 {m.Groups["n"].Value}장 이상");
        // 공식 (BT6-089 · P-129 · ST7-11): "자신의 시큐리티의 매수가 상대의 시큐리티보다 적을 때," · "자신의 시큐리티 장수가 상대보다 많다면," · "~ 상대의 시큐리티 매수 이하일 때,"
        if ((m = Regex.Match(c0, @"^you have (?<dir>fewer|more) security cards than your opponent(?: does)?$", RegexOptions.IgnoreCase)).Success)
        {
            string a = m.Groups["dir"].Value.Equals("fewer", StringComparison.OrdinalIgnoreCase) ? "적" : "많";
            return new($"자신의 시큐리티 매수가 상대보다 {a}을 때,", $"자신의 시큐리티 매수가 상대보다 {a}은 동안,", $"자신의 시큐리티 매수가 상대보다 {a}다면,");
        }
        if (Regex.IsMatch(c0, @"^the number of cards in your security stack is less than or equal to your opponent's$", RegexOptions.IgnoreCase))
            return Be("자신의 시큐리티 매수가 상대의 시큐리티 매수 이하");
        // 공식 (EX5-031): "서로의 시큐리티 합계가 6장 이하라면,"
        if ((m = Regex.Match(c0, @"^there(?: are|'re) (?<n>\d+) or (?<dir>fewer|less|more) total cards in both players' security stacks$", RegexOptions.IgnoreCase)).Success)
            return Be($"서로의 시큐리티 합계가 {m.Groups["n"].Value}장 {Dir(m.Groups["dir"].Value)}");
        // 공식 (P-022): "자신의 「최산해」와 「서정우」가 있을 때,"
        if ((m = Regex.Match(c0, @"^you have (?<a>\[[^\]]+\]) and (?<b>\[[^\]]+\])(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        { string b = NamesKo(m.Groups["b"].Value); return Have($"자신의 {PairKo33(m.Groups["a"].Value, m.Groups["b"].Value)}{J(b, "이", "가")}"); }
        // 공식 (EX5-063): "상대의 디지몬과 테이머의 합계 마리(명)수가 자신의 디지몬과 테이머의 합계 마리(명)수 이상이라면,"
        if (Regex.IsMatch(c0, @"^your opponent has as many or more total Digimon and Tamers as you(?: do)?$", RegexOptions.IgnoreCase))
            return Be("상대의 디지몬과 테이머의 합계 마리(명)수가 자신의 디지몬과 테이머의 합계 마리(명)수 이상");
        if (Regex.IsMatch(c0, @"^(?:your opponent|they) (?:has|have) a Tamer(?: in play)?$", RegexOptions.IgnoreCase)) return Have("상대의 테이머가");
        // 공식 (RB1-026): "테이머가 있는 동안,"
        if (Regex.IsMatch(c0, @"^there(?:'s| is) a Tamer(?: in play)?$", RegexOptions.IgnoreCase)) return Have("테이머가");
        // 공식 (BT15-051): "레스트 상태인 디지몬이 있다면,"
        if (Regex.IsMatch(c0, @"^there(?:'s| is) a suspended Digimon(?: in play)?$", RegexOptions.IgnoreCase)) return Have("레스트 상태인 디지몬이");
        if ((m = Regex.Match(c0, @"^there are (?<n>\d+) or more total Digimon(?: in play)?$", RegexOptions.IgnoreCase)).Success)
            return Have($"디지몬이 합계 {m.Groups["n"].Value}마리 이상");
        // 공식 (BT8-083): "자신의 트래시에 명칭에 「묘티스몬」을 포함하는 카드가 있을 때," · (BT7-072) "자신의 트래시에 「아이즈몬: 스캐터모드」가 있다면,"
        if ((m = Regex.Match(c0, @"^there(?:'s| is) (?<p>an? .+?) in your trash$", RegexOptions.IgnoreCase)).Success
            && PickKo(Regex.Replace(m.Groups["p"].Value, @"^an? ", "1 ", RegexOptions.IgnoreCase)) is { } tk)
        { string t = Regex.Replace(tk, @" 1장$", ""); return Have($"자신의 트래시에 {t}{J(t, "이", "가")}"); }
        if ((m = Regex.Match(c0, $@"^(?<x>{TargetSpec.NameList}) is in your trash$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return Have($"자신의 트래시에 {x}{J(x, "이", "가")}"); }
        // 자신의 테이머 — 공식 (BT11-038 · EX6-016 · BT11-080): "퍼플인 자신의 디지몬/테이머가 있다면,"
        if ((m = Regex.Match(c0, $@"^you (?<neg>don't have|do not have|have no|have) (?:an? |any )?(?:(?<col>(?:{Colors})(?: or (?:{Colors}))*) )?Tamers?(?: with (?<with>.+?))?(?: in play| on the field)?$", RegexOptions.IgnoreCase)).Success)
        {
            bool neg = !m.Groups["neg"].Value.Equals("have", StringComparison.OrdinalIgnoreCase);
            string pre = m.Groups["col"].Success ? $"{ColorsKo(m.Groups["col"].Value)}인 " : "";
            if (m.Groups["with"].Success)
            {
                if (WithKo("with " + m.Groups["with"].Value) is not { } wk) return null;
                pre = wk + " " + pre;
            }
            string s = $"{pre}자신의 테이머가";
            return neg ? NotHave(s) : Have(s);
        }
        if ((m = Regex.Match(c0, $@"^you have an? (?<c>{Colors}) Digimon or (?:(?<c2>{Colors}) )?Tamer(?: in play| on the field)?$", RegexOptions.IgnoreCase)).Success)
            return Have($"{ColorKo(m.Groups["c"].Value)}인 자신의 디지몬/테이머가");
        // 공식 (ST13-02 · P-097): "특징으로 「Legend-Arms」를 가지거나 블랙인 자신의 디지몬이 있을 때,"
        if ((m = Regex.Match(c0, $@"^you have an? Digimon that's (?<c>{Colors}) or has the (?<x>{TargetSpec.NameList}) traits?(?: in play)?$|^you have an? Digimon with the (?<x>{TargetSpec.NameList}) traits? or an? (?<c>{Colors}) Digimon(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value, trait: true); return Have($"특징으로 {x}{J(x, "을", "를")} 가지거나 {ColorKo(m.Groups["c"].Value)}인 자신의 디지몬이"); }
        if ((m = Regex.Match(c0, @"^you have an? Digimon(?: in play)? with (?<n>\d+) or more colou?rs(?: in play)?$", RegexOptions.IgnoreCase)).Success)
            return Have($"{m.Groups["n"].Value}색 이상인 자신의 디지몬이");
        if ((m = Regex.Match(c0, $@"^you have an? Digimon(?: in play)? with (?<x>{TargetSpec.NameList}) in its name or (?:with )?(?<y>{TargetSpec.NameList}) in its digivolution cards(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value), y = NamesKo(m.Groups["y"].Value);
            return Have($"명칭에 {x}{J(x, "을", "를")} 포함하거나 진화원에 {y}{J(y, "을", "를")} 가진 자신의 디지몬이");
        }
        if ((m = Regex.Match(c0, @"^you have an? Digimon card (?<with>with .+?) in play$", RegexOptions.IgnoreCase)).Success && WithKo(m.Groups["with"].Value) is { } dck)
            return Have($"{dck} 자신의 디지몬이");
        // 공식 (BT21-057 · BT17-089): "자신의 「신태일」 또는 특징 「어드벤처」를 가진 자신의 테이머가 있다면," · "자신의 「아르고몬」 또는 명칭에 「아구몬」/「그레이몬」을 포함하는 옐로인 자신의 디지몬이 있다면,"
        if ((m = Regex.Match(c0, $@"^you have (?<x>{TargetSpec.NameList}) or an? (?<rest>.+)$", RegexOptions.IgnoreCase)).Success
            && OrRestKo33(m.Groups["rest"].Value) is { } orRest)
            return Have($"자신의 {NamesKo(m.Groups["x"].Value)} 또는 {orRest}{J(orRest, "이", "가")}");
        // 공식 (BT12-090): "명칭에 「서정우」를 포함하는 자신의 테이머 또는 특징으로 「프리」를 가진 자신의 디지몬이 있다면,"
        if ((m = Regex.Match(c0, @"^you have an? Digimon (?<a>with .+?) or an? Tamer (?<b>with .+?)(?: in play)?$", RegexOptions.IgnoreCase)).Success
            && WithKo(m.Groups["a"].Value) is { } wa && WithKo(m.Groups["b"].Value) is { } wb)
            return Have($"{wb} 자신의 테이머 또는 {wa} 자신의 디지몬이");
        // 공식 (ST20-03 계열): "특징 「어드벤처」를 가진 자신의 테이머의 색 합계가 3색 이상이라면,"
        if ((m = Regex.Match(c0, $@"^your Tamers with the (?<x>{TargetSpec.NameList}) traits? have (?<n>\d+) or more (?:total )?colou?rs$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value, trait: true); return Be($"특징으로 {x}{J(x, "을", "를")} 가진 자신의 테이머의 색 합계가 {m.Groups["n"].Value}색 이상"); }
        // 공식 (BT8-004): "자신의 디지몬 전부가 레스트 상태인 동안,"
        if (Regex.IsMatch(c0, @"^all of your Digimon are suspended$", RegexOptions.IgnoreCase)) return Be("자신의 디지몬 전부가 레스트 상태");
        if (Regex.IsMatch(c0, @"^you have a suspended Tamer(?: in play)?$", RegexOptions.IgnoreCase)) return Have("레스트 상태인 자신의 테이머가");
        // 색 조건 무시의 조건
        if ((m = Regex.Match(c0, $@"^you have (?:an? )?(?:level (?<lv>\d+) )?(?<x>{TargetSpec.NameList})(?<trait> traits?)?(?: Digimon)?$", RegexOptions.IgnoreCase)).Success && (m.Groups["lv"].Success || m.Groups["trait"].Success))
        {
            string x = NamesKo(m.Groups["x"].Value, trait: m.Groups["trait"].Success);
            string lv = m.Groups["lv"].Success ? $"Lv.{m.Groups["lv"].Value}의 " : "";
            return m.Groups["trait"].Success ? Have($"특징으로 {x}{J(x, "을", "를")} 가진 {lv}자신의 디지몬이") : Have($"{lv}자신의 {x}{J(x, "이", "가")}");
        }
        if ((m = Regex.Match(c0, $@"^you have an? (?<c>{Colors}) (?<x>{TargetSpec.NameList}) traits? Digimon$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value, trait: true); return Have($"특징으로 {x}{J(x, "을", "를")} 가진 {ColorKo(m.Groups["c"].Value)}인 자신의 디지몬이"); }
        if ((m = Regex.Match(c0, $@"^you have an? Digimon or Tamer with (?<x>{TargetSpec.NameList}) in its text(?: on the field| in play)?$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return Have($"{x}{J(x, "이", "가")} 기술되어 있는 자신의 디지몬/테이머가"); }
        if ((m = Regex.Match(c0, $@"^you have an? Tamer or (?<x>{TargetSpec.NameList}) trait Digimon$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value, trait: true); return Have($"자신의 테이머 또는 특징으로 {x}{J(x, "을", "를")} 가진 자신의 디지몬이"); }
        if (Regex.IsMatch(c0, @"^you have an? Tamer with inherited effects$", RegexOptions.IgnoreCase)) return Have("진화원 효과를 가진 자신의 테이머가");
        if ((m = Regex.Match(c0, $@"^you have an? Digimon with (?<x>{TargetSpec.NameList}) in its name the field$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return Have($"명칭에 {x}{J(x, "을", "를")} 포함하는 자신의 디지몬이"); }

        // ---- 효과 상황 ----
        // 공식 (BT19-006 · EX4-053): "배틀 이외로 소멸하고 있었다면," · (BT17-068) "이 디지몬이 효과로 소멸하고 있었다면,"
        if (Regex.IsMatch(c0, @"^(?:not deleted (?:in|by) battle|deleted other than (?:in|by) battle)$", RegexOptions.IgnoreCase))
            return new("배틀 이외로 소멸하고 있었다면,", "배틀 이외로 소멸한 동안,", "배틀 이외로 소멸하고 있었다면,");
        if (Regex.IsMatch(c0, @"^deleted by (?:an )?effects?$", RegexOptions.IgnoreCase))
            return new("이 디지몬이 효과로 소멸하고 있었다면,", "이 디지몬이 효과로 소멸한 동안,", "이 디지몬이 효과로 소멸하고 있었다면,");
        if (Regex.IsMatch(c0, @"^(?:that Digimon|it) was deleted by (?:an )?effects?$", RegexOptions.IgnoreCase))
            return new("그 디지몬이 효과로 소멸했을 때,", "그 디지몬이 효과로 소멸한 동안,", "그 디지몬이 효과로 소멸했다면,");
        // 공식 (BT15-022 · EX6-013): "이 디지몬이 효과로 등장하고 있었다면," · "진화원에서 등장하고 있었다면,"
        if (Regex.IsMatch(c0, @"^played by (?:an )?effects?$", RegexOptions.IgnoreCase))
            return new("이 디지몬이 효과로 등장하고 있었다면,", "이 디지몬이 효과로 등장한 동안,", "이 디지몬이 효과로 등장하고 있었다면,");
        if (Regex.IsMatch(c0, @"^played or digivolved by (?:an )?effects?$", RegexOptions.IgnoreCase))
            return new("효과로 등장/진화하고 있었다면,", "효과로 등장/진화한 동안,", "효과로 등장/진화하고 있었다면,");
        if (Regex.IsMatch(c0, @"^played from digivolution cards$", RegexOptions.IgnoreCase))
            return new("진화원에서 등장하고 있었다면,", "진화원에서 등장하고 있었다면,", "진화원에서 등장하고 있었다면,");   // v0.46.0 "When played from digivolution cards, ~" (BT7-018 · EX3-015 · EX3-017)도 공식 "등장하고 있었다면"
        // 공식 (BT17-090 · BT12-097): "이 테이머가 레스트 상태라면," · "이 테이머 아래의 카드가 2장 이하라면,"
        if (Regex.IsMatch(c0, @"^this Tamer\b", RegexOptions.IgnoreCase)) _tamerSubject = true;   // 뒤 문장의 "it" = 이 테이머
        if (Regex.IsMatch(c0, @"^this Tamer is suspended$", RegexOptions.IgnoreCase)) return Be("이 테이머가 레스트 상태");
        if ((m = Regex.Match(c0, $@"^this Tamer has (?<n>\d+) or (?<dir>more|fewer|less) (?:(?<x>{TargetSpec.NameList}) text )?cards under it$", RegexOptions.IgnoreCase)).Success)
        {
            string x = m.Groups["x"].Success ? NamesKo(m.Groups["x"].Value) : null!;
            return m.Groups["x"].Success ? Be($"이 테이머 아래의 {x}{J(x, "이", "가")} 기술되어 있는 카드가 {m.Groups["n"].Value}장 {Dir(m.Groups["dir"].Value)}")
                : Be($"이 테이머 아래의 카드가 {m.Groups["n"].Value}장 {Dir(m.Groups["dir"].Value)}");
        }
        // 공식 (BT15-049): "상대의 디지몬이 어택 중이라면,"
        if (Regex.IsMatch(c0, @"^this Digimon is attacking$", RegexOptions.IgnoreCase)) return Be("이 디지몬이 어택 중");
        if (Regex.IsMatch(c0, @"^one of (?:their|your opponent's) Digimon is attacking$", RegexOptions.IgnoreCase)) return Be("상대의 디지몬이 어택 중");
        // 공식 (EX8-002): "메모리가 0이라면,"
        if ((m = Regex.Match(c0, @"^you have (?<n>\d+) memory$", RegexOptions.IgnoreCase)).Success) return Be($"메모리가 {m.Groups["n"].Value}");
        // 공식 (BT16-007): "그 디지몬이 특징으로 「프리」를 가지거나 옐로라면,"
        if ((m = Regex.Match(c0, $@"^(?:any of them|it|that Digimon) (?:are|is) (?<c>{Colors})$", RegexOptions.IgnoreCase)).Success)
            return Have($"그중 {ColorKo(m.Groups["c"].Value)}인 디지몬이");
        if ((m = Regex.Match(c0, $@"^(?:it|that Digimon) has the (?<x>{TargetSpec.NameList}) trait or is (?<c>{Colors})$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value, trait: true), c = ColorKo(m.Groups["c"].Value);
            return new($"그 디지몬이 특징으로 {x}{J(x, "을", "를")} 가지거나 {c}일 때,", $"그 디지몬이 특징으로 {x}{J(x, "을", "를")} 가지거나 {c}인 동안,", $"그 디지몬이 특징으로 {x}{J(x, "을", "를")} 가지거나 {c}{(Batchim(c) ? "이라면" : "라면")},");
        }
        if ((m = Regex.Match(c0, @"^(?:any of them|it|that Digimon) (?:have|has) (?<rest>(?:the )?\[.+)$", RegexOptions.IgnoreCase)).Success
            && FiltersKo("with " + Regex.Replace(Regex.Replace(m.Groups["rest"].Value, @"\btheir\b", "its", RegexOptions.IgnoreCase), @"^the ", "", RegexOptions.IgnoreCase)) is { Count: > 0 } themK)
        {
            bool any = c0.StartsWith("any of them", StringComparison.OrdinalIgnoreCase);
            string desc = string.Join(" ", themK);
            return any ? Have($"그중 {desc} 디지몬이") : Be($"그 디지몬이 {desc} 디지몬");
        }
        // 공식 (BT17-053): "그 디지몬이 Lv.5 이상이라면," · (EX2-057) "등장시킨 디지몬이 「마린엔젤몬」일 때,"
        if ((m = Regex.Match(c0, @"^(?:that Digimon|it) is level (?<n>\d+) or (?<dir>higher|lower)$", RegexOptions.IgnoreCase)).Success)
            return Be($"그 디지몬이 Lv.{m.Groups["n"].Value} {Dir(m.Groups["dir"].Value)}");
        if ((m = Regex.Match(c0, $@"^(?<who>that Digimon|the attacking Digimon|the Digimon that played) is (?<x>{TargetSpec.NameList})$", RegexOptions.IgnoreCase)).Success)
        {
            string who = m.Groups["who"].Value.ToLowerInvariant() switch { "the attacking digimon" => "어택한 디지몬이", "the digimon that played" => "등장시킨 디지몬이", _ => "그 디지몬이" };
            return Be($"{who} {NamesKo(m.Groups["x"].Value)}");
        }
        // 앞 문장의 결과 — 공식 (BT6-017 · EX2-067 · BT12-018 · ST17-04 · BT18-101): "이 효과로 옵션 카드를 사용하지 않았을 때," · "이 효과로 상대의 디지몬이 소멸하지 않았을 때," ·
        // "이 효과로 소멸하지 않았다면," · "이 효과로 자신의 디지몬이 소멸했다면,"
        if ((m = Regex.Match(c0, @"^(?:this effect (?<v>played|returned|digivolved|digivolve)|you played)$", RegexOptions.IgnoreCase)).Success)
        {
            string v = m.Groups["v"].Value.ToLowerInvariant() switch { "returned" => "되돌렸", "digivolved" or "digivolve" => "진화했", _ => "등장시켰" };
            return new($"이 효과로 {v}을 때,", $"이 효과로 {v}을 때,", $"이 효과로 {v}다면,");
        }
        if ((m = Regex.Match(c0, @"^(?:you didn't|you don't|you did not|you do not|this effect didn't (?<v>trash|return|flip))$", RegexOptions.IgnoreCase)).Success)
        {
            string v = m.Groups["v"].Value.ToLowerInvariant() switch { "trash" => "이 효과로 파기하지 않았", "return" => "이 효과로 되돌리지 않았", "flip" => "이 효과로 앞면으로 하지 않았", _ => "그렇게 하지 않았" };
            return new($"{v}을 때,", $"{v}을 때,", $"{v}다면,");
        }
        if (Regex.IsMatch(c0, @"^this didn't delete$", RegexOptions.IgnoreCase))
            return new("이 효과로 소멸하지 않았을 때,", "이 효과로 소멸하지 않은 동안,", "이 효과로 소멸하지 않았다면,");
        if (Regex.IsMatch(c0, @"^(?:no opponent's Digimon was deleted by this effect|an opponent's Digimon wasn't deleted by this effect)$", RegexOptions.IgnoreCase))
            return new("이 효과로 상대의 디지몬이 소멸하지 않았을 때,", "이 효과로 상대의 디지몬이 소멸하지 않은 동안,", "이 효과로 상대의 디지몬이 소멸하지 않았다면,");
        if (Regex.IsMatch(c0, @"^no Digimon was returned by this effect$", RegexOptions.IgnoreCase))
            return new("이 효과로 디지몬이 되돌아가지 않았을 때,", "이 효과로 디지몬이 되돌아가지 않은 동안,", "이 효과로 디지몬이 되돌아가지 않았다면,");
        if (Regex.IsMatch(c0, @"^this effect deleted one of your Digimon$", RegexOptions.IgnoreCase))
            return new("이 효과로 자신의 디지몬이 소멸했을 때,", "이 효과로 자신의 디지몬이 소멸한 동안,", "이 효과로 자신의 디지몬이 소멸했다면,");

        // 두 조건 — 공식 (BT16-020 · BT11-031 · BT12-029): "상대의 패가 8장 이상이거나, 이 디지몬의 진화원이 3장 이상이라면," · "~가 있고, 상대의 디지몬이 2마리 이상 있다면,"
        foreach (Match sep in Regex.Matches(c0, @",? (?<w>or|and) (?:if )?"))
        {
            string left = c0[..sep.Index].Trim(), right = c0[(sep.Index + sep.Length)..].Trim();
            if (left.Length == 0 || right.Length == 0) continue;
            if (left.EndsWith("]") && right.StartsWith("[") && !Regex.IsMatch(right, @"^\[[^\]]+\] (?:is|are|was) in\b", RegexOptions.IgnoreCase)) continue;
            if (CondKo(left) is not { } a) continue;
            var b = CondKo(right);
            if (b == null && Regex.IsMatch(left, @"^you have\b", RegexOptions.IgnoreCase) && !Regex.IsMatch(right, @"^(?:you|your|this|it|there|they|their)\b", RegexOptions.IgnoreCase))
                b = CondKo("you have " + right);
            if (b == null) continue;
            bool and = sep.Groups["w"].Value.Equals("and", StringComparison.OrdinalIgnoreCase);
            string la = Join33(a.Then, and);
            return new($"{la} {b.If}", $"{Join33(a.While, and)} {b.While}", $"{la} {b.Then}");
        }
        return null;
    }

    /// <summary>"~있다면," → "~있고," / "~있거나," (두 조건을 잇는다)</summary>
    private static string Join33(string first, bool and)
    {
        string s = first.TrimEnd(',');
        s = Regex.Replace(s, @"(?:이라면|라면|일 때|인 동안)$", and ? "이고" : "이거나");
        s = Regex.Replace(s, @"(있다면|있을 때|있는 동안)$", and ? "있고" : "있거나");
        s = Regex.Replace(s, @"(없다면|없을 때|없는 동안)$", and ? "없고" : "없거나");
        s = Regex.Replace(s, @"(가진다면|가질 때|가진 동안|가지는 동안)$", and ? "가지고" : "가지거나");
        s = Regex.Replace(s, @"([가-힣])(?:다면|을 때|ㄹ 때)$", and ? "$1고" : "$1거나");
        return s + ",";
    }

    private static Cond Have33(string subjectWithVerbStem) =>
        new($"{subjectWithVerbStem} 있을 때,", $"{subjectWithVerbStem} 있는 동안,", $"{subjectWithVerbStem} 있다면,");

    /// <summary>진화원 안의 카드 → "특징으로 「디지대」를 가진 테이머 카드" · "「아구몬」" · "블루인 카드" · "레드 이외의 카드" · "특징으로 「하이브리드체」를 갖는 카드 또는 「우정훈」"</summary>
    private static string? SourcePickCondKo33(string pick)
    {
        string p = pick.Trim();
        if (Regex.IsMatch(p, $@"^{TargetSpec.NameList}$")) return NamesKo(p);
        foreach (Match or in Regex.Matches(p, @"\s+or\s+"))
        {
            string left = p[..or.Index].Trim(), right = p[(or.Index + or.Length)..].Trim();
            bool rightName = Regex.IsMatch(right, $@"^{TargetSpec.NameList}$"), leftName = Regex.IsMatch(left, $@"^{TargetSpec.NameList}$");
            if (!rightName && !leftName) continue;
            if (leftName && rightName) continue;
            var a = SourcePickCondKo33(left); var b = SourcePickCondKo33(right);
            if (a != null && b != null) return $"{a} 또는 {b}";
        }
        string single = Regex.Replace(p, @"^(?:an?|1) ", "", RegexOptions.IgnoreCase);
        single = Regex.Replace(single, @"\bcards\b", "card", RegexOptions.IgnoreCase);
        if (!Regex.IsMatch(single, @"\bcard\b", RegexOptions.IgnoreCase)) return null;
        // "non-red card" — 공식 (BT10-001): "레드 이외의 카드"
        if (Regex.Match(single, $@"^non-(?<c>{Colors}) card$", RegexOptions.IgnoreCase) is { Success: true } nc) return $"{ColorKo(nc.Groups["c"].Value)} 이외의 카드";
        // "blue card" — 공식 (BT12-030 · BT8-082): "블루인 카드"
        if (Regex.Match(single, $@"^(?<c>{Colors}) card$", RegexOptions.IgnoreCase) is { Success: true } cc) return $"{ColorKo(cc.Groups["c"].Value)}인 카드";
        if (Regex.Match(single, $@"^(?<x>{TargetSpec.NameList}) card$", RegexOptions.IgnoreCase) is { Success: true } xc) return NamesKo(xc.Groups["x"].Value);
        if (PickKo("1 " + single) is not { } ko) return null;
        ko = Regex.Replace(ko, @" 1장$", "");
        return Regex.Replace(ko, @"(레드|블루|옐로|그린|블랙|퍼플|화이트)의 ", "$1인 ");
    }

    /// <summary>"you have [X] or a ~"의 뒤쪽: "a Tamer with the [ADVENTURE] trait" → "특징 「어드벤처」를 가진 자신의 테이머" · "a yellow Digimon with [Agumon] or [Greymon]" → "명칭에 ~을 포함하는 옐로인 자신의 디지몬"</summary>
    private static string? OrRestKo33(string rest)
    {
        Match m;
        rest = rest.Trim();
        if ((m = Regex.Match(rest, @"^Tamer (?<w>with .+)$", RegexOptions.IgnoreCase)).Success && WithKo(m.Groups["w"].Value) is { } tw) return $"{tw} 자신의 테이머";
        if ((m = Regex.Match(rest, $@"^(?<c>{Colors}) Digimon with (?<x>{TargetSpec.NameList})(?: in (?:its|their) names?)?$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return $"명칭에 {x}{J(x, "을", "를")} 포함하는 {ColorKo(m.Groups["c"].Value)}인 자신의 디지몬"; }
        if (TargetKo("all of your " + rest, bare: true) is { } t) return t;
        return null;
    }

    // =====================================================================
    // 사건
    // =====================================================================

    /// <summary>"When ~," 사건 (EventKo32 다음)</summary>
    private static string? EventKo33(string ev)
    {
        Match m;
        string fx = Regex.Replace(ev, @"^another of your Digimon\b", "one of your other Digimon", RegexOptions.IgnoreCase);
        fx = Regex.Replace(fx, @"^your security is reduced$", "your security stack is removed from", RegexOptions.IgnoreCase);
        if (fx != ev) return EventKo(fx);
        string listed = Regex.Replace(ev, @"\],\s+(?=\[)", "] or [");
        listed = Regex.Replace(listed, @"^(?:one of |any of )?your cards (?<w>with .+?) (?:is|are) played$", "you play a card ${w}", RegexOptions.IgnoreCase);
        if (listed != ev && EventKo(listed) is { } fromList) return fromList;

        // 디지몬 · 테이머가 떠나려 할 때 — 공식 (BT23-058 계열): "자신의 디지몬/테이머가 상대의 효과로 배틀 에어리어를 벗어날 때,"
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?<other>other )?(?:(?<x>{TargetSpec.NameList}) trait )?Digimon or Tamers would leave the battle area(?<q>.*)$", RegexOptions.IgnoreCase)).Success
            && LeaveQualifierKo(m.Groups["q"].Value) is { } lq)
        {
            string pre = m.Groups["x"].Success ? $"특징으로 {NamesKo(m.Groups["x"].Value, trait: true)}{J(NamesKo(m.Groups["x"].Value, trait: true), "을", "를")} 가진 " : "";
            return $"{pre}{(m.Groups["other"].Success ? "다른 " : "")}자신의 디지몬/테이머가 {lq}배틀 에어리어를 벗어날 때,";
        }
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?<x>{TargetSpec.NameList})s? would be returned to (?:hands|your hand|the hand) or (?:decks|your deck|the deck)$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return $"자신의 {x}{J(x, "이", "가")} 패/덱으로 되돌아갈 때,"; }
        // 공식 (BT14-030 계열): "다른 디지몬이 패로 되돌아갔을 때," · (BT17-099) "자신의 테이머가 소멸했을 때 또는 패로 되돌아갔을 때,"
        if (Regex.IsMatch(ev, @"^another Digimon is returned to (?:the|its owner's|a) hand$", RegexOptions.IgnoreCase)) return "다른 디지몬이 패로 되돌아갔을 때,";
        // 공식 (P-146): "이 디지몬이 배틀에서 소멸할 때," · (ST22-06) "자신이 옵션 카드를 사용했을 때,"
        if (Regex.IsMatch(ev, @"^this Digimon would be deleted (?:by|in) battle$", RegexOptions.IgnoreCase)) return "이 디지몬이 배틀에서 소멸할 때,";
        if (Regex.IsMatch(ev, @"^you use (?:an )?Option cards?$", RegexOptions.IgnoreCase)) return "자신이 옵션 카드를 사용했을 때,";
        if (Regex.IsMatch(ev, @"^(?:one of |any of )?your Tamers (?:is|are) deleted or returned to (?:the|your) hand$", RegexOptions.IgnoreCase)) return "자신의 테이머가 소멸했을 때 또는 패로 되돌아갔을 때,";
        if (Regex.IsMatch(ev, @"^(?:any of |one of )?your opponent's Digimon leaves? the battle area$", RegexOptions.IgnoreCase)) return "상대의 디지몬이 배틀 에어리어를 벗어났을 때,";
        if (Regex.IsMatch(ev, @"^(?:an effect deletes|effects delete) (?:one of |any of )?your other Digimon or Tamers$", RegexOptions.IgnoreCase)) return "다른 자신의 디지몬/테이머가 효과로 소멸했을 때,";
        if (Regex.IsMatch(ev, @"^(?:effects delete|an effect deletes) (?:a )?Digimon$", RegexOptions.IgnoreCase)) return "디지몬이 효과로 소멸했을 때,";
        if (Regex.IsMatch(ev, @"^(?:any of |one of )?your opponent's Digimon (?:is|are) deleted or their security stack is removed from$", RegexOptions.IgnoreCase)) return "상대의 디지몬이 소멸했을 때 또는 상대의 시큐리티가 줄어들었을 때,";
        // 시큐리티 — 공식 (BT16-056 계열): "상대의 시큐리티에 카드가 놓였을 때," · (BT15-084) "효과로 자신의 시큐리티가 줄어들었을 때," · (BT16-033) "이 디지몬이 상대의 시큐리티를 체크했을 때,"
        if (Regex.IsMatch(ev, @"^(?:a card is|cards are) added to your opponent's security stack$", RegexOptions.IgnoreCase)) return "상대의 시큐리티에 카드가 놓였을 때,";
        if (Regex.IsMatch(ev, @"^an effect removes cards? from your security stack$|^effects remove cards from your security stack$", RegexOptions.IgnoreCase)) return "효과로 자신의 시큐리티가 줄어들었을 때,";
        if (Regex.IsMatch(ev, @"^(?:a card is|cards are) trashed from your security stack by (?:an )?effects?$", RegexOptions.IgnoreCase)) return "자신의 시큐리티가 효과로 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^this Digimon checks (?:your opponent's|their) security stack$", RegexOptions.IgnoreCase)) return "이 디지몬이 상대의 시큐리티를 체크했을 때,";
        // 진화 · 어택
        if (Regex.IsMatch(ev, @"^(?:an|one of your) opponent's Digimon digivolves or attacks$", RegexOptions.IgnoreCase)) return "상대의 디지몬이 진화/어택했을 때,";
        if (Regex.IsMatch(ev, @"^(?:an|one of your) opponent's Digimon digivolves or (?:an effect places|effects place) digivolution cards under (?:an|one of your) opponent's Digimon$", RegexOptions.IgnoreCase))
            return "상대의 디지몬이 진화했을 때 또는 효과로 상대의 디지몬의 진화원이 늘어났을 때,";
        if (Regex.IsMatch(ev, @"^(?:one of |any of )?your Tamers digivolves?$", RegexOptions.IgnoreCase)) return "자신의 테이머가 진화했을 때,";
        if (Regex.IsMatch(ev, @"^(?:one of |any of )?your Digimon digivolves? from (?:the|your) trash$", RegexOptions.IgnoreCase)) return "자신의 디지몬이 트래시에서 진화했을 때,";
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your Digimon with (?<n>\d+) or more same-level cards in its stack attacks$", RegexOptions.IgnoreCase)).Success)
            return $"Lv.이 같은 카드가 {m.Groups["n"].Value}장 이상 겹쳐져 있는 자신의 디지몬이 어택했을 때,";
        if ((m = Regex.Match(ev, @"^your (?<a>Digimon with .+?) or when your opponent's (?<b>.+?) attacks$", RegexOptions.IgnoreCase)).Success
            && WithKo(Regex.Replace(m.Groups["a"].Value, @"^Digimon ", "", RegexOptions.IgnoreCase)) is { } aw && TargetKo("all of your opponent's " + m.Groups["b"].Value, bare: true) is { } bk)
            return $"{aw} 자신의 디지몬 또는 {bk}{J(bk, "이", "가")} 어택했을 때,";
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your (?<spec>.+?Digimon)'s attack targets? (?:changes?|is switched|are switched)$", RegexOptions.IgnoreCase)).Success
            && TargetKo("all of your " + m.Groups["spec"].Value, bare: true) is { } sk)
            return $"{sk}의 어택 대상이 변경되었을 때,";
        // 등장
        if ((m = Regex.Match(ev, @"^(?:any )?other Digimon (?<with>with .+?) (?:is|are) played$", RegexOptions.IgnoreCase)).Success && WithKo(m.Groups["with"].Value) is { } ow)
            return $"{ow} 다른 디지몬이 등장했을 때,";
        if (Regex.IsMatch(ev, @"^(?:an effect plays|effects play) (?:an|one of your) opponent's Digimon or Tamers?$", RegexOptions.IgnoreCase)) return "효과로 상대의 디지몬/테이머가 등장했을 때,";
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?:(?<x>{TargetSpec.NameList}) text Digimon|Digimon (?<with>with .+?)) (?:is|are) played or deleted$", RegexOptions.IgnoreCase)).Success)
        {
            string? w = m.Groups["x"].Success ? $"{NamesKo(m.Groups["x"].Value)}{J(NamesKo(m.Groups["x"].Value), "이", "가")} 기술되어 있는" : WithKo(m.Groups["with"].Value);
            if (w != null) return $"{w} 자신의 디지몬이 등장했을 때 또는 소멸했을 때,";
        }
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?<x>{TargetSpec.NameList}) text or (?<t>{TargetSpec.NameList}) trait Digimon (?:is|are) played$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value), t = NamesKo(m.Groups["t"].Value, trait: true);
            return $"{x}{J(x, "이", "가")} 기술되어 있거나 특징으로 {t}{J(t, "을", "를")} 가진 자신의 디지몬이 등장했을 때,";
        }
        // 트래시 → 패 · 덱
        if ((m = Regex.Match(ev, @"^(?:an? )?(?<pick>.+?cards?) (?:returns?|is returned|are returned) from your trash to (?:the|your) hand$", RegexOptions.IgnoreCase)).Success
            && PickKo("1 " + Regex.Replace(m.Groups["pick"].Value, @"\bcards\b", "card", RegexOptions.IgnoreCase)) is { } th)
        { string t = Regex.Replace(th, @" 1장$", ""); return $"자신의 트래시에서 {t}{J(t, "이", "가")} 패로 되돌아갔을 때,"; }
        if (Regex.IsMatch(ev, @"^(?:a card is|cards are) returned to (?:the|your) deck from your trash$", RegexOptions.IgnoreCase)) return "자신의 트래시에서 카드가 덱으로 되돌아갔을 때,";
        // "A or B" — 공식 (ST22-06): "자신이 옵션 카드를 사용했을 때 또는, 자신의 시큐리티가 줄어들었을 때,"
        foreach (Match or in Regex.Matches(ev, @" or (?=(?:your|you|an?|one of|any of|effects?)\b)", RegexOptions.IgnoreCase))
        {
            if (EventKo(ev[..or.Index]) is { } ea && EventKo(ev[(or.Index + or.Length)..]) is { } eb && ea.EndsWith(","))
                return $"{ea} 또는 {eb}".Replace(",, 또는", ", 또는");
        }
        // "When a card with [Jellymon] in its text is trashed from your hand"
        if ((m = Regex.Match(ev, @"^(?:an? )?(?<pick>cards? with .+?) (?:is|are) trashed from your hand$", RegexOptions.IgnoreCase)).Success
            && PickKo("1 " + Regex.Replace(m.Groups["pick"].Value, @"^cards", "card", RegexOptions.IgnoreCase)) is { } hk)
        { string t = Regex.Replace(hk, @" 1장$", ""); return $"자신의 패에서 {t}{J(t, "이", "가")} 파기되었을 때,"; }
        return null;
    }

    // =====================================================================
    // 문장 (동작)
    // =====================================================================

    /// <summary>문장 하나 (원래 규칙이 그리지 못한 것 — Sentence의 마지막)</summary>
    private static string? SentenceKo33(string s, bool may)
    {
        Match m;
        string t = SentenceParser.NormalizeAction33(s);
        if (t != s.Trim().TrimEnd('.')) return Sentence(may ? "You may " + Uncap(t) : t, nested: true);
        // 진화 조건 무시 진화 — 공식 (BT15-091 · BT17-053 · BT17-085): "그 디지몬을 패의 「메탈가루몬」으로 진화 조건을 무시하고 코스트를 지불하지 않고 진화시킬 수 있다" ·
        // "~ 진화 조건을 무시하고 진화 코스트 4로 진화시킬 수 있다"
        if ((m = Regex.Match(t, @"^(?<t>that Digimon|it|this Digimon|this Tamer|(?:1|one) of your .+?) may digivolve into (?:an? |1 )?(?<pick>.+?) (?:in|from) (?:the |your )?(?<from>hand or (?:the |your )?trash|hand|trash)(?<tail>(?:,? ?(?:ignoring (?:its |this card's |the )?(?:digivolution requirements|level)|for (?:a digivolution cost of (?<cost>\d+)|its digivolution cost)|without paying (?:the|its) (?:memory |digivolution )?cost|with the (?:digivolution )?cost reduced by (?<red>\d+)|and))+)$", RegexOptions.IgnoreCase)).Success
            && Regex.IsMatch(m.Groups["tail"].Value, @"\bignoring\b", RegexOptions.IgnoreCase))
        {
            string who = m.Groups["t"].Value.ToLowerInvariant() switch
            {
                "that digimon" => "그 디지몬", "it" => _tamerSubject ? "이 테이머" : _mentionedTarget || _itOther35 ? "그 디지몬" : "이 디지몬", "this digimon" => "이 디지몬", "this tamer" => "이 테이머",
                _ => TargetKo(m.Groups["t"].Value) ?? (Regex.Match(m.Groups["t"].Value, $@"^(?:1|one) of your (?<x>{TargetSpec.NameList})$", RegexOptions.IgnoreCase) is { Success: true } nm ? $"자신의 {NamesKo(nm.Groups["x"].Value)} 1마리" : ""),
            };
            if (who.Length == 0) return null;
            string? pk = EvoPickKo33(m.Groups["pick"].Value);
            if (pk == null) return null;
            string from = m.Groups["from"].Value.ToLowerInvariant();
            string zone = from.Contains("hand") && from.Contains("trash") ? "패/트래시의" : from.Contains("hand") ? "패의" : "트래시의";
            string tail = m.Groups["tail"].Value;
            string ign = Regex.IsMatch(tail, @"ignoring (?:its )?level", RegexOptions.IgnoreCase) ? "Lv.을 무시하고" : "진화 조건을 무시하고";
            string pay = Regex.IsMatch(tail, @"without paying", RegexOptions.IgnoreCase) ? "코스트를 지불하지 않고"
                : m.Groups["cost"].Success ? $"진화 코스트 {m.Groups["cost"].Value}{J(m.Groups["cost"].Value, "으로", "로")}"
                : m.Groups["red"].Success ? $"지불하는 진화 코스트를 -{m.Groups["red"].Value} 하여" : "진화 코스트를 지불하여";   // v0.36.0 "with the cost reduced by 1" (EX13-071)
            return $"{who}{J(who, "을", "를")} {zone} {pk}{J(pk, "으로", "로")} {ign} {pay} 진화시킬 수 있다";
        }
        // 테이머를 이 카드로 진화 — 공식 (BT18-053 · BT12-015): "그 테이머를 이 카드로 진화 조건을 무시하고 진화 코스트 3으로 진화시킨다" · "그 테이머를 레드인 Lv.4의 디지몬으로 취급하여 이 카드로 진화시킨다"
        if ((m = Regex.Match(t, @"^(?:that Tamer|it) digivolves into this card for (?:a )?digivolution cost (?:of )?(?<n>\d+),? ignoring (?:its |this card's |the )?digivolution requirements$", RegexOptions.IgnoreCase)).Success)
            return $"그 테이머를 이 카드로 진화 조건을 무시하고 진화 코스트 {m.Groups["n"].Value}{J(m.Groups["n"].Value, "으로", "로")} 진화시킨다";
        if ((m = Regex.Match(t, $@"^Digivolve (?:it |that Tamer )?into this card as if (?:that card|the Tamer|it) is a level (?<lv>\d+) (?<c>{Colors}) Digimon for (?:a digivolution cost of (?<n>\d+)|the digivolution cost|its digivolution cost)$", RegexOptions.IgnoreCase)).Success)
            return $"그 테이머를 {ColorKo(m.Groups["c"].Value)}인 Lv.{m.Groups["lv"].Value}의 디지몬으로 취급하여 이 카드로 {(m.Groups["n"].Success ? $"진화 코스트 {m.Groups["n"].Value}{J(m.Groups["n"].Value, "으로", "로")} " : "")}진화시킨다";
        // 놓는다 — 비용 한글 "~놓는 것으로," → "~놓는다" / "~놓을 수 있다"
        if ((m = Regex.Match(t, @"^Place (?<rest>.+)$", RegexOptions.IgnoreCase)).Success && !Regex.IsMatch(t, @"\bface[- ]down\b", RegexOptions.IgnoreCase)
            && CostKo("placing " + m.Groups["rest"].Value) is { } ck && ck.EndsWith("것으로,"))
        {
            string stem = ck[..^"것으로,".Length].TrimEnd();
            // "~놓는" → "~놓는다" / "~놓을 수 있다" · "~되돌리는" → "~되돌린다"
            if (stem.EndsWith("놓는")) return stem[..^2] + (may ? "놓을 수 있다" : "놓는다");
            if (stem.EndsWith("되돌리는")) return stem[..^4] + (may ? "되돌릴 수 있다" : "되돌린다");
        }
        if (Regex.IsMatch(t, @"^Trash both players' top security cards?$", RegexOptions.IgnoreCase)) return "서로의 시큐리티를 위에서부터 1장 파기한다";
        if ((m = Regex.Match(t, @"^(?:the )?(?:Digimon played by this effect|Digimon this effect played) gains? (?<kws>＜[^＞]+＞(?:\s*,?\s*(?:and\s*)?＜[^＞]+＞)*)(?<dur> for the turn| until (?:the end of )?(?:your opponent's|their) turn(?: ends)?)?$", RegexOptions.IgnoreCase)).Success
            && KeywordListKo(m.Groups["kws"].Value) is { } pk2)
        {
            string dur = m.Groups["dur"].Value.Contains("turn ends") || m.Groups["dur"].Value.Contains("opponent") || m.Groups["dur"].Value.Contains("their") ? "상대의 턴 종료 시까지 " : "이 턴 동안 ";
            return $"{dur}이 효과로 등장한 디지몬은 {pk2}{J(Regex.Match(pk2, @"《([^》]+)》[^《]*$").Groups[1].Value, "을", "를")} 얻는다";
        }
        // "You may play 1 ~ from your hand with the cost reduced by 4" (BT26-022) — 공식 표기 "지불하는 코스트 -4 하여 등장시킬 수 있다"
        if ((m = Regex.Match(t, @"^play (?<p>\d+ .+?) from (?<z>your trash|your hand|your hand or trash) with the cost reduced by (?<n>\d+)$", RegexOptions.IgnoreCase)).Success && PickKo(m.Groups["p"].Value) is { } pp)
        {
            string z = m.Groups["z"].Value.Contains("hand", StringComparison.OrdinalIgnoreCase) ? (m.Groups["z"].Value.Contains("trash", StringComparison.OrdinalIgnoreCase) ? "자신의 패/트래시에서" : "자신의 패에서") : "자신의 트래시에서";
            return $"{z} {pp}{J(pp, "을", "를")} 지불하는 코스트 -{m.Groups["n"].Value} 하여 {(may ? "등장시킬 수 있다" : "등장시킨다")}";
        }
        // 공식 (BT18-101): "상대의 디지몬 1마리와 테이머 1명을 소멸시킨다"
        if (Regex.IsMatch(t, @"^Delete 1 of your opponent's Digimon and 1 of their Tamers$", RegexOptions.IgnoreCase)) return "상대의 디지몬 1마리와 테이머 1명을 소멸시킨다";
        // 공식 (BT11-070): "자신의 덱 위에서부터 3장 오픈한다. 그중 「벰몬」 1장을 이 디지몬의 진화원 아래에 놓는다. 나머지는 파기한다."
        if ((m = Regex.Match(t, @"^Reveal the top (?<n>\d+) cards of your deck\.? Place (?<p>.+?) among them under this Digimon as its bottom digivolution cards?\.? (?<rest>.+?)$", RegexOptions.IgnoreCase)).Success
            && PickKo(m.Groups["p"].Value) is { } rp)
        {
            string rest = m.Groups["rest"].Value.TrimEnd('.');
            string restKo = Regex.IsMatch(rest, @"^Trash the (?:rest|remaining cards)$", RegexOptions.IgnoreCase) ? "나머지는 파기한다" : Regex.IsMatch(rest, @"bottom", RegexOptions.IgnoreCase) ? "나머지는 덱 아래로 되돌린다" : null!;
            if (restKo != null) return $"자신의 덱 위에서부터 {m.Groups["n"].Value}장 오픈한다. 그중 {rp}{J(rp, "을", "를")} 이 디지몬의 진화원 아래에 놓는다. {restKo}";
        }
        return null;
    }

    private static string Uncap(string s) => s.Length > 0 ? char.ToLowerInvariant(s[0]) + s[1..] : s;

    /// <summary>진화할 카드 → "「메탈가루몬」" · "명칭에 「가이오몬」을 포함하는 등장 코스트 13 이상의 카드" · "특징 「Ver.2」를 가진 디지몬 카드"</summary>
    private static string? EvoPickKo33(string pick)
    {
        string p = pick.Trim();
        if (Regex.IsMatch(p, $@"^{TargetSpec.NameList}$")) return NamesKo(p);
        string q = Regex.IsMatch(p, @"^(?:\d+|an?) ", RegexOptions.IgnoreCase) ? Regex.Replace(p, @"^an? ", "1 ", RegexOptions.IgnoreCase) : "1 " + p;
        if (PickKo(q) is not { } ko) return null;
        return Regex.Replace(ko, @" 1장$", "");
    }
}
