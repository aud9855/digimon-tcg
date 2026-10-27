using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.46.0 20차 묶음의 한글 규칙 (원래 규칙이 옮기지 못한 것만 — KoGate46)</summary>
public static partial class KoreanEffectRenderer
{
    /// <summary>절 전체 고정 표기 (공식 한글 구조에 맞춘 문구 — 이 묶음에서 새로 읽은 절)</summary>
    private static readonly (string Re, string Ko)[] FixedClauseKo46 =
    {
        // BT11-082 (공식 "【서로의 턴】자신의 「노유빈」 전부는 소멸하지 않는다")
        (@"^\[All Turns\]\s*All of your \[Yuu Amano\] can't be deleted\.?$", "【서로의 턴】 자신의 「노유빈」 전부는 소멸하지 않는다."),
        // BT18-086 (공식 "[육성]【서로의 턴】자신의 「루체몬: 사탄 모드」가 배틀 에어리어를 벗어날 때, 이 디지몬을 배틀 에어리어로 이동시키는 것으로, 벗어나지 않는다")
        (@"^\[Breeding\]\s*\[All Turns\]\s*When any of your \[Lucemon: Satan Mode\] would leave the battle area, by moving this Digimon to battle area, it doesn't leave\.?$",
            "[육성] 【서로의 턴】 자신의 「루체몬: 사탄 모드」가 배틀 에어리어를 벗어날 때, 이 디지몬을 배틀 에어리어로 이동시키는 것으로, 벗어나지 않는다."),
        // BT18-086 (공식 "【서로의 턴】화이트 이외의 명칭에 「루체몬」을 포함하는 자신의 디지몬이 있는 동안, DP 0의 자신의 디지몬 전부는 소멸하지 않는다")
        (@"^\[All Turns\]\s*While you have a non-white Digimon with \[Lucemon\] in its name, all of your 0 DP Digimon can't be deleted\.?$",
            "【서로의 턴】 화이트 이외의 명칭에 「루체몬」을 포함하는 자신의 디지몬이 있는 동안, DP 0의 자신의 디지몬 전부는 소멸하지 않는다."),
        // BT22-007 진화원 효과 (공식 "{육성}【서로의 턴】[턴 1회]특징 「이터」를 가진 자신의 디지몬이 자신의 효과 이외로 배틀 에어리어를 벗어날 때, 그 디지몬을 이 디지몬의 진화원 아래에 놓을 수 있다")
        (@"^\[Breeding\]\s*\[All Turns\]\s*\[Once Per Turn\]\s*When any of your \[Eater\] trait Digimon would leave the battle area other than by your effects, you may place them as this Digimon's bottom digivolution cards\.?$",
            "[육성] 【서로의 턴】 [턴에 1회] 특징으로 「이터」를 가진 자신의 디지몬이 자신의 효과 이외로 배틀 에어리어를 벗어날 때, 그 디지몬을 이 디지몬의 진화원 아래에 놓을 수 있다."),
        // EX10-029 링크 효과 (공식 "【링크 시】이 디지몬의 링크 카드 1장을 파기하는 것으로, 상대의 턴 종료까지, 자신의 디지몬 1마리는 《퇴화》의 효과를 받지 않는다") — 영어 원문이 깨졌다
        (@"^By trashing 1 of this Digimon's link cards, effects don't affect 1 of your Digimon until your opponent's turn ends\.?$",
            "【링크 시】 이 디지몬의 링크 카드 1장을 파기하는 것으로, 상대의 턴 종료 시까지, 자신의 디지몬 1마리는 《퇴화》의 효과를 받지 않는다."),
        // EX4-042 (공식 "【자신의 턴】이 디지몬과 명칭에 「나이트몬」/「나이츠몬」을 포함하는 디지몬 전부는 블록당하지 않는다")
        (@"^\[Your Turn\]\s*This Digimon and all of your Digimon with \[Knightmon\] in their names or \[Knightsmon\] in their names are unblockable\.?$",
            "【자신의 턴】 이 디지몬과 명칭에 「나이트몬」/「나이츠몬」을 포함하는 자신의 디지몬 전부는 블록당하지 않는다."),
        // BT3-099 (공식 "【메인】 이 턴 동안, 양 측의 디지몬은 배틀로 소멸하지 않는다")
        (@"^\[Main\]\s*Neither player's Digimon can be deleted in battle for the turn\.?$", "【메인】 이 턴 동안, 양쪽의 디지몬은 배틀로 소멸하지 않는다."),
    };

    /// <summary>문장 고정 표기</summary>
    private static readonly (string Re, string Ko)[] FixedKo46 =
    {
    };

    private static string? ClauseKo46(string clause, string rawClause)
    {
        foreach (var (re, ko) in FixedClauseKo46)
            if (Regex.IsMatch(clause, re, IC)) return ko;
        // 선택지 줄 "・~" (≪딜레이≫ 아래 효과 — BT17-095 · BT17-097 · BT19-099)
        var m = Regex.Match(rawClause.Trim(), @"^[・•]\s*(?:\[Main\]\s*)?(?<b>.+)$", RegexOptions.Singleline);
        if (m.Success)
        {
            string body = m.Groups["b"].Value.Trim();
            if (Sentence(body) is { } k) return k.TrimEnd('.') + ".";
        }
        return null;
    }

    private static string? SentenceKo46(string s, bool nested)
    {
        if (!KoGate46) return null;
        s = s.Trim().TrimEnd('.');
        foreach (var (re, ko) in FixedKo46)
            if (Regex.IsMatch(s, re, IC)) return ko;
        Match m;
        // DNA 진화: "That Digimon and a card in the hand may DNA digivolve into ~ in the hand" (BT17-095 — 공식 "그 디지몬과 패의 카드로, 패의 ~로 조그레스 진화할 수 있다") ·
        // "1 of your level 6 Digimon and 1 card in the hand ~" (EX6-072 — 공식 "Lv.6의 자신의 디지몬 1마리와 패의 카드 1장으로, 패의 Lv.7의 디지몬 카드로 조그레스 진화할 수 있다")
        if ((m = Regex.Match(s, @"^(?<who>That Digimon|1 of your (?<spec>[^,]*?)Digimon(?<with> with [^,]+?)?) and (?:a|1) card in (?:the|your) hand may DNA digivolve into (?<p>.+?) in (?:the|your) hand$", IC)).Success
            && PickNoCount45(m.Groups["p"].Value) is { } dp)
        {
            string? who = m.Groups["who"].Value.Equals("That Digimon", StringComparison.OrdinalIgnoreCase) ? "그 디지몬과 패의 카드로"
                : TargetKo($"1 of your {m.Groups["spec"].Value}Digimon{m.Groups["with"].Value}") is { } tk ? $"{tk}와 패의 카드 1장으로" : null;
            if (who != null) return $"{who}, 패의 {dp}{J(dp, "으로", "로")} 조그레스 진화할 수 있다";
        }
        // "That Digimon and any of your other Digimon may DNA digivolve into ~ in the hand" (EX12-003 "1 of them and ~")
        if ((m = Regex.Match(s, @"^(?:That Digimon|1 of them) and (?:any of )?your other (?<spec>[^,]*?)Digimon may DNA digivolve into (?<p>.+?) in (?:the|your) hand$", IC)).Success
            && PickNoCount45(m.Groups["p"].Value) is { } dp2)
            return $"그 디지몬과 다른 자신의 {(m.Groups["spec"].Value.Trim().Length > 0 ? m.Groups["spec"].Value.Trim() + " " : "")}디지몬으로, 패의 {dp2}{J(dp2, "으로", "로")} 조그레스 진화할 수 있다";
        // "You may play 1 ~ with a play cost 1 higher than that Digimon from your hand or trash without paying the cost" (BT19-099 — 공식 "자신의 패/트래시에서 그 디지몬보다 등장 코스트가 1 높은 ~ 1장을 코스트를 지불하지 않고 등장시킬 수 있다")
        if ((m = Regex.Match(s, @"^(?<may>you may )?play 1 (?<p>.+?) with a play cost (?<n>\d+) higher than that Digimon from (?<z>your hand or trash|your hand|your trash) without paying the cost$", IC)).Success
            && PickNoCount45(m.Groups["p"].Value) is { } pp)
        {
            string z = m.Groups["z"].Value.ToLowerInvariant() switch { "your hand or trash" => "자신의 패/트래시에서", "your hand" => "자신의 패에서", _ => "자신의 트래시에서" };
            return $"{z} 그 디지몬보다 등장 코스트가 {m.Groups["n"].Value} 높은 {pp} 1장을 코스트를 지불하지 않고 {(m.Groups["may"].Success ? "등장시킬 수 있다" : "등장시킨다")}";
        }
        // "place those Digimon face-up at the bottom of your security stack" (BT19-053 — 공식 "그 디지몬을 시큐리티 아래에 앞면으로 놓을 수 있다") · "place 1 of those Digimon as the top security card" (BT23-080 — 공식 "그 디지몬 1마리를 시큐리티 위에 놓는다")
        if ((m = Regex.Match(s, @"^(?<may>you may )?place (?<w>those Digimon|that Digimon|1 of those Digimon|it|them) (?:(?<up>face[- ]up )?(?:at the (?<pos>bottom|top) of your security stack|as the (?<pos2>top|bottom) security card))$", IC)).Success)
        {
            bool top = (m.Groups["pos"].Success ? m.Groups["pos"].Value : m.Groups["pos2"].Value).Equals("top", StringComparison.OrdinalIgnoreCase);
            string who = m.Groups["w"].Value.StartsWith("1 of", StringComparison.OrdinalIgnoreCase) ? "그 디지몬 1마리를" : "그 디지몬을";
            return $"{who} 시큐리티 {(top ? "위" : "아래")}에 {(m.Groups["up"].Success ? "앞면으로 " : "")}{(m.Groups["may"].Success ? "놓을 수 있다" : "놓는다")}";
        }
        // "1 of your Digimon may get +3000 DP until your opponent's turn ends and attack" (BT26-015)
        if ((m = Regex.Match(s, @"^1 of your Digimon may get (?<dp>[+-]\d+) DP until your opponent's turn ends and attack$", IC)).Success)
            return $"자신의 디지몬 1마리는 상대의 턴 종료 시까지 DP {m.Groups["dp"].Value} 하고, 어택할 수 있다";
        // "Place 1 of your [ADR-02 Searcher]s from in play or from your hand under 1 of your [Mother D-Reaper]s as its bottom digivolution card" (EX2-048 — 공식 "자신의 「ADR-02=서쳐」 1마리 또는 패의 「ADR-02=서쳐」 1장을 자신의 「마더 디・리퍼」 1마리의 진화원 가장 아래에 놓을 수 있다")
        if ((m = Regex.Match(s, @"^Place 1 of your (?<x>\[[^\]]+\])s? from in play or from your hand under 1 of your (?<y>\[[^\]]+\])s? as its bottom digivolution card$", IC)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value), y = NamesKo(m.Groups["y"].Value);
            return $"자신의 {x} 1마리 또는 패의 {x} 1장을 자신의 {y} 1마리의 진화원 가장 아래에 놓을 수 있다";
        }
        return null;
    }


    private static string? CostKo46(string cost)
    {
        if (!KoGate46) return null;
        Match m;
        // "digivolving that Digimon into ~ in your hand without paying the cost" (BT17-097 — 공식 "그 디지몬을 패의 ~로 코스트를 지불하지 않고 진화시키는 것으로") ·
        // "digivolving it into [Chronomon: Destroy Mode] in the hand or trash without paying the cost" (BT26-085) · [트래시] "digivolving it into this card ~" (BT17-065 — 공식 "그 디지몬을 이 카드로 코스트를 지불하지 않고 진화시키는 것으로")
        if ((m = Regex.Match(cost.Trim(), @"^digivolving (?<who>that Digimon|it|this Digimon) into (?<p>.+?)(?: in (?:the|your) (?<z>hand or trash|hand|trash))?(?<free> without paying (?:the|its) cost)?$", IC)).Success)
        {
            string who = m.Groups["who"].Value.Equals("this Digimon", StringComparison.OrdinalIgnoreCase) ? "이 디지몬을" : "그 디지몬을";
            string? into = Regex.IsMatch(m.Groups["p"].Value, @"^this card$", IC) ? "이 카드" : PickNoCount45(m.Groups["p"].Value);
            if (into == null) return null;
            string z = !m.Groups["z"].Success ? "" : m.Groups["z"].Value.ToLowerInvariant() switch { "hand or trash" => "패/트래시의 ", "hand" => "패의 ", _ => "트래시의 " };
            return $"{who} {z}{into}{J(into, "으로", "로")} {(m.Groups["free"].Success ? "코스트를 지불하지 않고 " : "")}진화시키는 것으로,";
        }
        // [육성] "moving this Digimon to battle area" (BT18-086)
        if (Regex.IsMatch(cost.Trim(), @"^moving this Digimon to (?:the )?battle area$", IC)) return "이 디지몬을 배틀 에어리어로 이동시키는 것으로,";
        // 진화원 아래에 놓기 (EX3-013 · EX1-073 · P-096)
        if (PlaceKo46(cost.Trim(), cost: true) is { } pk) return pk;
        return null;
    }

    /// <summary>진화원 아래에 놓기 (SentenceParser.Batch46Src와 같은 읽기) — cost면 "~ 놓는 것으로,"</summary>
    private static string? PlaceKo46(string s, bool cost)
    {
        bool distinct = Regex.IsMatch(s, @"\b(?:and|with) different card numbers\b", IC);
        string n = Regex.Replace(s, @"\s*\b(?:and|with) different card numbers\b", "", IC);
        n = Regex.Replace(n, @"\b(?<a>red|blue|yellow|green|black|purple|white) and/or (?<b>red|blue|yellow|green|black|purple|white)\b", "${a} or ${b}", IC);
        n = Regex.Replace(n, @"\byour hand and/or (?:your )?trash\b", "your hand or trash", IC);
        var m = Regex.Match(n, @"^(?:placing|(?<may>you may )?place) (?<up>up to )?(?<k>\d+) (?<p>.+?) from (?<from>your hand or (?:your )?trash|under your Tamers or (?:from )?(?:your )?trash|your hand|your trash|under your Tamers) (?:under (?<t>.+?) as (?:its|their) bottom digivolution cards?|as (?<t>.+?)'s bottom digivolution cards?|in (?<self>this card's|this Digimon's|its) digivolution cards)$", IC);
        if (!m.Success || PickNoCount45(m.Groups["p"].Value) is not { } p) return null;
        string from = m.Groups["from"].Value.ToLowerInvariant() switch
        {
            "your hand or trash" or "your hand or your trash" => "자신의 패/트래시에서",
            "your hand" => "자신의 패에서",
            "your trash" => "자신의 트래시에서",
            "under your tamers" => "자신의 테이머 아래에서",
            _ => "자신의 테이머 아래 또는 트래시에서",
        };
        string target;
        if (m.Groups["self"].Success || Regex.IsMatch(m.Groups["t"].Value, @"^this Digimon$", IC)) target = "이 디지몬";
        else if (Regex.IsMatch(m.Groups["t"].Value, @"^that Digimon$", IC)) target = "그 디지몬";
        else if (TargetKo(m.Groups["t"].Value) is { } tk) target = tk;
        else return null;
        string count = $"{m.Groups["k"].Value}장{(m.Groups["up"].Success ? "까지" : "")}";
        string body = $"{from} {(distinct ? "카드 넘버가 서로 다른 " : "")}{p} {count}{J(count, "을", "를")} {target}의 진화원 아래에 ";
        return cost ? body + "놓는 것으로," : body + (m.Groups["may"].Success ? "놓을 수 있다" : "놓는다");
    }

    private static string? EventKo46(string ev)
    {
        if (!KoGate46) return null;
        // "When your effects add to decks" (BT26-015 — 공식 표기 없음: "자신의 효과로 덱에 카드가 추가되었을 때")
        if (Regex.IsMatch(ev.Trim(), @"^(?:your effects|one of your effects|an effect of yours) adds? (?:cards )?to (?:decks|a deck|the decks?|your deck)$", IC))
            return "자신의 효과로 덱에 카드가 추가되었을 때,";
        // "When any of your Digimon digivolve into a Digimon with ＜Save＞ in its text" (EX10-067 — 공식 "자신의 디지몬이 《세이브》가 기술되어 있는 디지몬으로 진화했을 때")
        var m = Regex.Match(ev.Trim(), @"^(?:any of|one of) your Digimon digivolves? into a Digimon with (?<x>.+? in its (?:text|name))$", IC);
        if (m.Success && PickNoCount45("Digimon card with " + m.Groups["x"].Value) is { } dk && dk.EndsWith(" 카드", StringComparison.Ordinal))
            return $"자신의 디지몬이 {dk[..^3]}{J(dk[..^3], "으로", "로")} 진화했을 때,";
        return null;
    }

    private static Cond? CondKo46(string cond)
    {
        if (!KoGate46) return null;
        return null;
    }

    private static string? CounterKo46(string counter)
    {
        if (!KoGate46) return null;
        return null;
    }

    private static string? TargetKo46(string phrase, bool bare)
    {
        if (!KoGate46) return null;
        return null;
    }
}
