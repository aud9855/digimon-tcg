using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.45.0 19차 묶음의 한글 규칙 (원래 규칙이 옮기지 못한 것만 — KoGate45)</summary>
public static partial class KoreanEffectRenderer
{
    /// <summary>절 전체 고정 표기 (공식 한글 — 이 묶음에서 새로 읽은 [육성] 절)</summary>
    private static readonly (string Re, string Ko)[] FixedClauseKo45 =
    {
        // EX9-005 (공식 "{육성}【메인】[턴 1회]자신의 패에서 「네가몬」이 기술되어 있는 디지몬 카드 1장을 지불하는 코스트 -2 하여 등장시킬 수 있다. 자신의 트래시와 디지몬의 진화원에 있는 「네가몬」 합계 1장마다 이 효과의 코스트 경감 수치 -1. 그 후, 이 디지몬을 등장한 디지몬의 진화원 아래에 놓는다")
        (@"^\[Breeding\]\s*\[Main\]\s*\[Once Per Turn\]\s*You may play 1 Digimon card with \[Negamon\] in its text from your hand with the play cost reduced by 2\. For each \[Negamon\] in your trash or your Digimon's digivolution cards, further reduce it by 1\. Then, place this Digimon as the played Digimon's bottom digivolution card\.?$",
            "[육성] 【메인】 [턴에 1회] 자신의 패에서 「네가몬」이 기술되어 있는 디지몬 카드 1장을 지불하는 코스트 -2 하여 등장시킬 수 있다. 자신의 트래시와 디지몬의 진화원에 있는 「네가몬」 합계 1장마다 이 효과의 코스트 경감 수치 -1. 그 후, 이 디지몬을 등장한 디지몬의 진화원 아래에 놓는다."),
        // EX9-005 (공식 "{육성}【서로의 턴】이 디지몬은 진화할 수 없으며, 효과로 소멸하지 않고, 파기되지 않는다")
        (@"^\[Breeding\]\s*\[All Turns\]\s*This Digimon can't digivolve and effects can't delete or trash it\.?$", "[육성] 【서로의 턴】 이 디지몬은 진화할 수 없으며, 효과로 소멸하지 않고, 파기되지 않는다."),
        // BT22-007 (공식 "{육성}【서로의 턴】배틀 에어리어의 자신의 「마더 이터」 전부는 DP 16000으로 취급한다")
        (@"^\[Breeding\]\s*\[All Turns\]\s*All of your \[Mother Eater\]s in the battle area are treated as having 16000 DP\.?$", "[육성] 【서로의 턴】 배틀 에어리어의 자신의 「마더 이터」 전부는 DP 16000으로 취급한다."),
    };

    private static string? ClauseKo45(string clause, string rawClause)
    {
        foreach (var (re, ko) in FixedClauseKo45)
            if (Regex.IsMatch(clause, re, IC)) return ko;
        // 선택지 줄 "・~" · "• ~" (≪딜레이≫ 아래 효과) — 기호 · [메인] 표시를 떼고 옮긴다 (공식 표기는 "・~")
        var m = Regex.Match(rawClause.Trim(), @"^[・•]\s*(?:\[Main\]\s*)?(?<b>.+)$", RegexOptions.Singleline);
        if (!m.Success) return null;
        string body = m.Groups["b"].Value.Trim();
        if (RenderUpTo44(body) is { } k) return k;
        foreach (var alt in SentenceParser.ClauseRewrites45(body))
            if (alt != body && RenderUpTo44(alt) is { } ko) return ko;
        return null;
    }

    private static string? SentenceKo45(string s, bool nested)
    {
        if (!KoGate45) return null;
        s = s.Trim().TrimEnd('.');
        foreach (var (re, ko) in FixedKo45)
            if (Regex.IsMatch(s, re, IC)) return ko;
        if (s.Contains(SentenceParser.RevealMark45)) return RevealGroupKo45(s);
        Match m;
        // BT10-039 (공식 "~ 옵션 카드 1장을 색 조건을 무시하고 코스트를 지불하지 않고 사용할 수 있다")
        if (Regex.IsMatch(s, @"\bwithout meeting its colou?r requirements or paying its memory cost$", IC)
            && Sentence(Regex.Replace(s, @"\bwithout meeting its colou?r requirements or paying its memory cost$", "without paying the cost", IC), nested) is { } useKo
            && useKo.Contains("코스트를 지불하지 않고"))
            return useKo.Replace("코스트를 지불하지 않고", "색 조건을 무시하고 코스트를 지불하지 않고");
        // BT20-095 (공식 "육성 에어리어의 Lv.3 이상의 자신의 디지몬 1마리를 배틀 에어리어로 이동시키는 것으로, 그 디지몬을 패/트래시의 특징으로 「크로니클」을 가진 디지몬 카드로 코스트를 지불하지 않고 진화시킬 수 있다")
        if ((m = Regex.Match(s, @"^By (?<cost>moving your level \d+ or higher Digimon from the breeding area to the battle area), it may digivolve into (?<p>an? .+?) in the hand or trash without paying the cost$", IC)).Success
            && CostKo(m.Groups["cost"].Value) is { } mk && PickNoCount45(m.Groups["p"].Value) is { } mp)
            return $"{mk} 그 디지몬을 패/트래시의 {mp}{J(mp, "으로", "로")} 코스트를 지불하지 않고 진화시킬 수 있다";
        // EX9-055 (공식 "자신의 패/트래시에서 비어 있는 자신의 육성 에어리어에 「아바도몬 코어」 1장을 코스트를 지불하지 않고 등장시킬 수 있다")
        if ((m = Regex.Match(s, @"^(?<may>you may )?play 1 (?<x>\[[^\]]+\]) from your hand or trash to your empty breeding area without paying the cost$", IC)).Success)
            return $"자신의 패/트래시에서 비어 있는 자신의 육성 에어리어에 {NamesKo(m.Groups["x"].Value)} 1장을 코스트를 지불하지 않고 {(m.Groups["may"].Success ? "등장시킬 수 있다" : "등장시킨다")}";
        // 등장 코스트 합계까지 (공식 EX8-064 "자신의 트래시에서 특징으로 「NSo」를 가진 디지몬 카드를 등장 코스트 합계 10까지 코스트를 지불하지 않고 등장시킬 수 있다")
        if ((m = Regex.Match(s, @"^(?<may>you may )?play up to (?<n>\d+) play cost's total worth of (?<p>.+?) from (?<z>your hand or trash|your hand or your trash|your hand|your trash|this Digimon's digivolution cards) without paying (?:the|their|its) costs?$", IC)).Success
            && PickNoCount45(m.Groups["p"].Value) is { } tp)
        {
            string z = m.Groups["z"].Value.ToLowerInvariant();
            string zk = z.Contains("digivolution") ? "이 디지몬의 진화원에서" : z.Contains("hand") && z.Contains("trash") ? "자신의 패/트래시에서" : z.Contains("hand") ? "자신의 패에서" : "자신의 트래시에서";
            return $"{zk} {tp}{J(tp, "을", "를")} 등장 코스트 합계 {m.Groups["n"].Value}까지 코스트를 지불하지 않고 {(m.Groups["may"].Success ? "등장시킬 수 있다" : "등장시킨다")}";
        }
        // "reduce the play costs of all of your opponent's Digimon by 1 until the end of the turn" (BT16-060 — 공식 "턴 종료까지 상대의 디지몬 전부를 등장 코스트 -1")
        if ((m = Regex.Match(s, @"^reduce the play costs of all of your opponent's Digimon by (?<n>\d+) (?:until the end of the turn|for the turn)$", IC)).Success)
            return $"턴 종료까지 상대의 디지몬 전부를 등장 코스트 -{m.Groups["n"].Value}";
        return null;
    }

    private static string ColorKo45(string c) => c.ToLowerInvariant() switch
    {
        "red" => "레드", "blue" => "블루", "yellow" => "옐로", "green" => "그린", "black" => "블랙", "purple" => "퍼플", "white" => "화이트", _ => c,
    };

    private static string? PickNoCount45(string x)
    {
        string p = Regex.IsMatch(x, @"^(?:\d+|an?) ", IC) ? Regex.Replace(x, @"^an? ", "1 ", IC) : "1 " + x;
        return PickKo(p) is { } k ? Regex.Replace(k, @"\s*1장$", "") : null;
    }

    /// <summary>덱 위 공개 묶음 (SentenceParser.Batch45Reveal — 공식 표기 "자신의 덱 위에서부터 3장 오픈한다. 그중 ~. 오픈한 카드는 덱 위 또는 아래로만 되돌린다")</summary>
    private static string? RevealGroupKo45(string s)
    {
        var parts = s.Split(SentenceParser.RevealMark45).Select(x => x.Trim().TrimEnd('.').Trim()).ToList();
        var outs = new List<string>();
        Match m;
        if (!(m = Regex.Match(parts[0], @"^(?<then>Then, )?(?<may>You may )?(?:[Rr]eveal the top (?<n>\d+) cards of your deck|(?<opp>Your opponent reveals the top (?<n>\d+) cards of their deck))$")).Success) return null;
        outs.Add($"{(m.Groups["then"].Success ? "그 후, " : "")}{(m.Groups["opp"].Success ? "상대" : "자신")}의 덱 위에서부터 {m.Groups["n"].Value}장 {(m.Groups["may"].Success ? "오픈할 수 있다" : "오픈한다")}");
        string? post = null;
        int endIdx = parts.Count - 1;
        if ((m = Regex.Match(parts[^1], @"^Then, (?<inner>.+) for each card added to (?:the|your) hand by this effect$", IC)).Success)
        {
            if (Sentence(Cap45(m.Groups["inner"].Value), true) is not { } ik) return null;
            post = $"그 후, 이 효과로 패에 추가한 카드 1장마다, {ik}";
            endIdx--;
        }
        foreach (var mid in parts.Skip(1).Take(endIdx - 1))
        {
            if (RevealMidKo45(mid) is not { } mk) return null;
            outs.Add(mk);
        }
        string end = parts[endIdx];
        string? endKo =
            Regex.IsMatch(end, @"^(?:Return|Place) the (?:revealed )?cards (?:to|at) (?:either )?the top or (?:the )?bottom of the deck(?: in any order)?$", IC) ? "오픈한 카드는 덱 위 또는 아래로만 되돌린다"
            : Regex.IsMatch(end, @"^Trash the revealed cards$", IC) ? "오픈한 카드는 파기한다"
            : Regex.IsMatch(end, @"^Trash the (?:rest|remaining cards?)$", IC) ? "남은 카드는 파기한다"
            : Regex.IsMatch(end, @"^Place the (?:cards|rest|remaining cards) (?:at|on) the top of (?:the|your) deck in any order$", IC) ? "오픈한 카드는 원하는 순서대로 덱 위로 되돌린다"
            : Regex.IsMatch(end, @"^(?:Place|Return) the (?:rest|remaining cards) (?:at|to) the bottom of (?:the|your) deck(?: in any order)?$", IC) ? "남은 카드는 원하는 순서대로 덱 아래로 되돌린다"
            : Regex.IsMatch(end, @"^Add the (?:remaining cards?|rest) to (?:the|your) hand$", IC) ? "남은 카드는 패에 추가한다"
            : null;
        if (endKo == null) return null;
        outs.Add(endKo);
        if (post != null) outs.Add(post);
        return string.Join(". ", outs);
    }

    /// <summary>마지막 글자에 받침이 있는지 (조사 고르기)</summary>
    private static bool HasBatchim45(string w) => w.Length > 0 && w[^1] >= 0xAC00 && w[^1] <= 0xD7A3 && (w[^1] - 0xAC00) % 28 != 0;

    private static string Cap45(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string? RevealMidKo45(string mid)
    {
        Match m;
        if ((m = Regex.Match(mid, @"^For each (?<x>.+?) among (?:them|the revealed cards), (?<inner>.+)$", IC)).Success
            && PickNoCount45(m.Groups["x"].Value) is { } fx && Sentence(Cap45(m.Groups["inner"].Value), true) is { } fi)
            return $"그중 {fx} 1장마다 {fi}";
        if ((m = Regex.Match(mid, @"^For each (?<counter>[^,]+), (?<inner>.+\bamong (?:them|the revealed cards)\b.*)$", IC)).Success
            && CounterKo(m.Groups["counter"].Value) is { } ck && RevealMidKo45(m.Groups["inner"].Value) is { } ci)
            return $"{ck} {Regex.Replace(ci, "^그중 ", "그중 ")}";
        if ((m = Regex.Match(mid, @"^Choose 1 (?<x>.+?) among them, and (?<inner>.+)$", IC)).Success && PickNoCount45(m.Groups["x"].Value) is { } cx)
        {
            string filled = Regex.Replace(Regex.Replace(m.Groups["inner"].Value,
                @"whose play cost is less than or equal to the chosen card's play cost", "with a play cost of 987 or less", IC),
                @"\bup to its play cost's total worth of\b", "up to 987 play cost's total worth of", IC);
            if (Sentence(Cap45(filled), true) is not { } ck2 || !ck2.Contains("987")) return null;
            ck2 = Regex.Replace(ck2, @"등장 코스트 987 이하인", "선택한 카드의 등장 코스트 이하인");
            ck2 = Regex.Replace(ck2, @"등장 코스트 합계 987까지", "선택한 카드의 등장 코스트 합계까지");
            return $"그중 {cx} 1장을 선택하고, {ck2}";
        }
        if ((m = Regex.Match(mid, @"^(?<a>.+?) with as high or lower a play cost as 1 (?<x>.+?) among them$", IC)).Success
            && PickNoCount45(m.Groups["x"].Value) is { } mx && Sentence(Cap45(m.Groups["a"].Value) + " with a play cost of 987 or less", true) is { } ma && ma.Contains("등장 코스트 987 이하인"))
            return ma.Replace("등장 코스트 987 이하인", $"그중 {mx} 1장의 등장 코스트 이하인");
        if ((m = Regex.Match(mid, @"^If all of them are (?<c>red|blue|yellow|green|black|purple|white)(?: cards)?, (?<inner>.+)$", IC)).Success
            && Sentence(Cap45(m.Groups["inner"].Value), true) is { } ii)
            return $"오픈한 카드가 전부 {ColorKo45(m.Groups["c"].Value)}{(HasBatchim45(ColorKo45(m.Groups["c"].Value)) ? "이라면" : "라면")}, {ii}";
        if (Regex.IsMatch(mid, @"^Place 1 of them (?:on top of|at the top of) your security stack(?: face down)?$", IC))
            return "그중 1장을 자신의 시큐리티 위에 뒷면으로 놓는다";
        if ((m = Regex.Match(mid, @"^(?<may>you may )?play 1 (?<x>.+?) among (?:them|the revealed cards) without paying (?:its|the) (?:memory |play )?cost$", IC)).Success
            && PickNoCount45(m.Groups["x"].Value) is { } px)
            return $"그중 {px} 1장을 코스트를 지불하지 않고 {(m.Groups["may"].Success ? "등장시킬 수 있다" : "등장시킨다")}";
        if ((m = Regex.Match(mid, @"^(?:you may )?play any number of (?<x>.+?) whose (?:total )?play costs add up to (?<n>\d+) or less among them without paying (?:its|the|their) (?:memory |play )?costs?$", IC)).Success
            && PickNoCount45(m.Groups["x"].Value) is { } bx)
            return $"그중 {bx}를 등장 코스트 합계 {m.Groups["n"].Value}까지 코스트를 지불하지 않고 등장시킬 수 있다";
        if ((m = Regex.Match(mid, @"^(?<inner>.+) for each Digimon played (?:with|by) this effect$", IC)).Success && Sentence(Cap45(m.Groups["inner"].Value), true) is { } pi)
            return $"이 효과로 등장한 디지몬 1마리마다 {pi}";
        if ((m = Regex.Match(mid, @"^Add all (?<x>.+?) among them to (?:the|your) hand$", IC)).Success && PickNoCount45(m.Groups["x"].Value) is { } ax)
            return $"그중 {ax} 전부를 패에 추가한다";
        return null;
    }

    /// <summary>고정 표기 (공식 한글)</summary>
    private static readonly (string Re, string Ko)[] FixedKo45 =
    {
        // BT12-057 · EX10-023 · BT6-092
        (@"^All other Digimon and Tamers don't unsuspend$", "이 디지몬 이외의 디지몬과 테이머 전부는 액티브가 되지 않는다"),
        (@"^Other than this Digimon, no Digimon or Tamers can unsuspend (?:in|during) the unsuspend phase$", "액티브 페이즈에서는, 이 디지몬 이외의 디지몬과 테이머 전부는 액티브가 되지 않는다"),
        (@"^your opponent's Tamers don't unsuspend (?:in|during) their unsuspend phase$", "상대의 테이머 전부는 액티브 페이즈에서는 액티브가 되지 않는다"),
        // EX11-070 진화원 효과 (한국판 미발매 — 공식 표기 방식: 「~」가 기술되어 있는 · DP ~ 미만이 되지 않는다 · 겹쳐진 카드)
        (@"^This Digimon with \[Maquinamon\] in its text can't have less than 1000 DP, and your opponent's effects can't trash its stacked cards$", "「마키나몬」이 기술되어 있는 이 디지몬은 DP가 1000 미만이 되지 않으며, 상대의 효과로 겹쳐진 카드가 파기되지 않는다"),
        // EX3-069 (공식 "이 효과로 등장한 디지몬은 Lv.7로 진화할 수 없고, 다음 상대의 턴 종료 시에 소멸한다")
        (@"^The Digimon played by this effect can't digivolve to level 7, and at the end of your opponent's turn, delete that Digimon$", "이 효과로 등장한 디지몬은 Lv.7로 진화할 수 없고, 다음 상대의 턴 종료 시에 소멸한다"),
        // P-206 (공식 "자신의 패에서 에어리어의 자신의 디지몬과 같은 색의 테이머 카드 1장을 등장 코스트 -4하여 등장시킬 수 있다")
        (@"^You may play 1 Tamer card with the same colou?r as any of your Digimon(?: on the field| in play)? from your hand with the play cost reduced by 4$", "자신의 패에서 에어리어의 자신의 디지몬과 같은 색의 테이머 카드 1장을 지불하는 등장 코스트 -4하여 등장시킬 수 있다"),
        // BT16-094 (공식 "자신의 패에서 「4대용의 시련」 1장을 배틀 에어리어에 놓거나, 특징으로 「4대용」을 가진 카드 1장을 파기할 수 있다")
        (@"^Place 1 \[Trial of the Four Great Dragons\] from your hand in the battle area, or you may trash 1 \[Four Great Dragons\] trait card in your hand$", "자신의 패에서 「4대용의 시련」 1장을 배틀 에어리어에 놓거나, 특징으로 「4대용」을 가진 카드 1장을 파기할 수 있다"),
        (@"^If you did either, 1 of your opponent's Digimon gets -7000 DP for the turn$", "그랬다면, 턴 종료까지 상대의 디지몬 1마리를 DP -7000"),
    };

    private static string? CostKo45(string cost)
    {
        if (!KoGate45) return null;
        string s = cost.Trim();
        Match m;
        // BT18-100 (공식 "명칭에 「루체몬」을 포함하는 자신의 디지몬 1마리를 트래시의 명칭에 「루체몬」을 포함하는 디지몬 카드로 진화시키는 것으로, ~ 이 효과로 진화할 때, 지불하는 진화 코스트 -3")
        if ((m = Regex.Match(s, @"^digivolving (?<t>1 of your Digimon(?: with [^,]+?)?) into (?<p>an? .+?) from your trash with the digivolution cost reduced by (?<n>\d+)$", IC)).Success
            && TargetKo(m.Groups["t"].Value) is { } tk && PickNoCount45(m.Groups["p"].Value) is { } pk)
            return $"{tk}{J(tk, "을", "를")} 트래시의 {pk}{J(pk, "으로", "로")} 지불하는 진화 코스트 -{m.Groups["n"].Value}하여 진화시키는 것으로,";
        // BT20-095 (공식 "육성 에어리어의 Lv.3 이상의 자신의 디지몬 1마리를 배틀 에어리어로 이동시키는 것으로,")
        if ((m = Regex.Match(s, @"^moving your level (?<lv>\d+) or higher Digimon from the breeding area to the battle area$", IC)).Success)
            return $"육성 에어리어의 Lv.{m.Groups["lv"].Value} 이상의 자신의 디지몬 1마리를 배틀 에어리어로 이동시키는 것으로,";
        return null;
    }

    private static Cond? CondKo45(string cond)
    {
        if (!KoGate45) return null;
        Match m;
        string c0 = cond.Trim();
        // 앞 문장의 결과 (공식 BT9-076 "파기한 카드가 퍼플이라면," · P-212 "이 효과로 특징 「3총사」/「TS」를 가진 카드를 파기했다면," · BT8-111 "이 효과로 4장 이상 파기했을 때,")
        if ((m = Regex.Match(c0, @"^you trash an? (?<c>red|blue|yellow|green|black|purple|white) card$", IC)).Success)
        {
            string ck = ColorKo45(m.Groups["c"].Value);
            string t = $"파기한 카드가 {ck}{(HasBatchim45(ck) ? "이라면" : "라면")},";
            return new Cond(t, t);
        }
        if ((m = Regex.Match(c0, @"^this effect trashed a card with the (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) traits?$", IC)).Success)
        {
            string n = NamesKo(m.Groups["x"].Value, trait: true);
            string t = $"이 효과로 특징으로 {n}{J(n, "을", "를")} 가진 카드를 파기했다면,";
            return new Cond(t, t);
        }
        if ((m = Regex.Match(c0, @"^you (?<v>trash|return|place) (?<n>\d+)(?<more> or more)? cards (?:with|using|by) this effect$", IC)).Success)
        {
            string v = m.Groups["v"].Value.ToLowerInvariant() switch { "trash" => "파기했을 때,", "return" => "되돌렸을 때,", _ => "놓았을 때," };
            string t = $"이 효과로 {m.Groups["n"].Value}장{(m.Groups["more"].Success ? " 이상" : "")} {v}";
            return new Cond(t, t);
        }
        if ((m = Regex.Match(c0, @"^there are (?<n>\d+) or more total (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) in your trash and your Digimon's digivolution cards$", IC)).Success)
        {
            string n = NamesKo(m.Groups["x"].Value);
            return new Cond($"자신의 트래시와 디지몬의 진화원에 {n}{J(n, "이", "가")} 합계 {m.Groups["n"].Value}장 이상 있다면,", $"자신의 트래시와 디지몬의 진화원에 {n}{J(n, "이", "가")} 합계 {m.Groups["n"].Value}장 이상 있는 동안,");
        }
        if ((m = Regex.Match(c0, @"^an? (?<x>.+? card) isn't in this Digimon's digivolution cards$", IC)).Success && PickNoCount45(m.Groups["x"].Value) is { } nx)
            return new Cond($"이 디지몬의 진화원에 {nx}{J(nx, "이", "가")} 없다면,", $"이 디지몬의 진화원에 {nx}{J(nx, "이", "가")} 없는 동안,");
        // "you have [Mother Eater] in the breeding area" (BT23-074 — 공식 "육성 에어리어에 자신의 「마더 이터」가 있다면,")
        if ((m = Regex.Match(cond.Trim(), @"^you have (?<x>\[[^\]]+\]) in the breeding area$", IC)).Success)
        {
            string n = NamesKo(m.Groups["x"].Value);
            return new Cond($"육성 에어리어에 자신의 {n}{J(n, "이", "가")} 있다면,", $"육성 에어리어에 자신의 {n}{J(n, "이", "가")} 있는 동안,");
        }
        return null;
    }

    private static string? EventKo45(string ev)
    {
        if (!KoGate45) return null;
        Match m;
        // "When one of your [Myotismon] is deleted" (BT15-098 — 공식 "자신의 「묘티스몬」이 소멸했을 때")
        if ((m = Regex.Match(ev.Trim(), @"^one of your (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) is deleted$", IC)).Success)
        {
            string n = NamesKo(m.Groups["x"].Value);
            return $"자신의 {n}{J(n, "이", "가")} 소멸했을 때,";
        }
        return null;
    }

    private static string? CounterKo45(string counter)
    {
        if (!KoGate45) return null;
        Match m;
        // "of your [Iliad] or [TS] trait Digimon or Tamers" (BT26-081)
        if ((m = Regex.Match(counter.Trim(), @"^of your (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) trait Digimon (?:or|and/or|and) Tamers$", IC)).Success)
        {
            string n = NamesKo(m.Groups["x"].Value, trait: true);
            return $"특징으로 {n}{J(n, "을", "를")} 가진 자신의 디지몬/테이머 1마리(명)마다";
        }
        return null;
    }

    private static string? TargetKo45(string phrase, bool bare)
    {
        if (!KoGate45) return null;
        return null;
    }
}
