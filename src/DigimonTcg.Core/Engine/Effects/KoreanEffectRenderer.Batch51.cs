using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.51.0 미지원 문장 25차 묶음의 생성 한글 — 원래 규칙(24차까지)이 옮기지 못한 절 · 문장 · 조건 · 비용만 (SentenceParser.Batch51과 같은 표)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth51;
    [ThreadStatic] private static int _koGate51;
    private static bool KoGate51 => _koGate51 > 0;

    /// <summary>절: 25차 한글을 켠 채로 다시 · 같은 뜻 다른 표기(SentenceParser.ClauseRules51)로</summary>
    private static string? RenderClause51(string rawClause)
    {
        if (_clauseDepth51 > 0) return null;
        _clauseDepth51++;
        _koGate51++;
        try
        {
            if (SentenceParser.Hint51.IsMatch(rawClause) && RenderClause(rawClause) is { } again)
                // LM-026 공식 "~ 코스트를 지불하지 않고 등장시킨다. 이 효과로 등장했다면, 이 디지몬을 등장한 디지몬의 진화원 아래에 놓는다"
                return again.Replace("코스트를 지불하지 않고 등장시키는 것으로, 이 디지몬을 등장한 디지몬의 진화원 아래에 놓는다", "코스트를 지불하지 않고 등장시킨다. 이 효과로 등장했다면, 이 디지몬을 등장한 디지몬의 진화원 아래에 놓는다");
            // BT6-065 "Reveal the top 5 cards ~. You may use 1 Option card with a memory cost of 7 among them without paying its memory cost. Trash the remaining cards. If you don't use an Option card with this effect, ~"
            if (Regex.Match(rawClause, @"^(?<labels>(?:\[[^\]]+\]\s*)*)Reveal the top (?<n>\d+) cards of your deck\. You may use 1 Option card with a memory cost of (?<c>\d+) among them without paying its memory cost\. Trash the remaining cards\. If you don't use an Option card with this effect, (?<rest>.+?)\.?$", IC) is { Success: true } bt6
                && Sentence(Cap(bt6.Groups["rest"].Value)) is { } restKo)
                return $"{LabelsKo40(bt6.Groups["labels"].Value)}자신의 덱 위에서부터 {bt6.Groups["n"].Value}장 오픈한다. 그중 사용 코스트 {bt6.Groups["c"].Value}의 옵션 카드 1장을 코스트를 지불하지 않고 사용할 수 있다. 남은 카드는 파기한다. 이 효과로 옵션 카드를 사용하지 않았을 때, {restKo.TrimEnd('.', ' ')}.";
            string alt = SentenceParser.ClauseRewrite51(rawClause);
            if (alt == rawClause || RenderClause(alt) is not { } ko) return null;
            // P-087 "When you would play [Pulsemon]" — 등장시킬 때 (등장 직전)
            if (Regex.Match(rawClause, @"\bWhen you would play (?<x>\[[^\]]+\])", IC) is { Success: true } wp)
            {
                string x = NamesKo(wp.Groups["x"].Value);
                ko = ko.Replace($"자신의 {x}{J(x, "이", "가")} 등장했을 때,", $"자신이 {x}{J(x, "을", "를")} 등장시킬 때,");
            }
            // EX2-055 "등장 코스트를 99 줄인다" → "등장 코스트를 0으로 한다"
            ko = Regex.Replace(ko, @"(?:등장 )?코스트(?:를)? -?99(?: (?:줄인다|감소))?", "등장 코스트를 0으로 한다");
            return ko;
        }
        finally { _clauseDepth51--; _koGate51--; }
    }

    /// <summary>조건 (SentenceParser.ParseEventCondition51 · ParseStaticCondition51)</summary>
    private static Cond? CondKo51(string cond)
    {
        if (!KoGate51) return null;
        string s = cond.Trim();
        Match m;
        if (Regex.IsMatch(s, @"^you(?:'ve| have) digivolved (?:a Digimon )?this turn$", IC))
            return new("이 턴에 자신이 디지몬을 1회 이상 진화시키고 있는 경우,", "이 턴에 자신이 디지몬을 1회 이상 진화시키고 있는 동안,", "이 턴에 자신이 디지몬을 1회 이상 진화시키고 있다면,");   // 공식 BT1-007
        if ((m = Regex.Match(s, @"^the level (?<lv>\d+) cards in this Digimon's digivolution cards have (?<n>\d+) or more colou?rs(?: among them)?$", IC)).Success)
        {
            string head = $"이 디지몬의 진화원에 있는 Lv.{m.Groups["lv"].Value}인 카드의 색의 합계가 {m.Groups["n"].Value}색 이상";   // 공식 BT9-067
            return new(head + "이라면,", head + "인 동안,", head + "이라면,");
        }
        if ((m = Regex.Match(s, @"^they have (?<n>\d+) or more colou?rs(?: among them)?$", IC)).Success)
            return new($"{m.Groups["n"].Value}색 이상이라면,", $"{m.Groups["n"].Value}색 이상인 동안,", $"{m.Groups["n"].Value}색 이상이라면,");
        if ((m = Regex.Match(s, @"^a Digimon with (?<n>\d+) DP or more (?:is|was) deleted by this effect$", IC)).Success)
            return new($"이 효과로 DP {m.Groups["n"].Value} 이상의 디지몬을 소멸시켰다면,", $"이 효과로 DP {m.Groups["n"].Value} 이상의 디지몬을 소멸시킨 동안,", $"이 효과로 DP {m.Groups["n"].Value} 이상의 디지몬을 소멸시켰다면,");
        if ((m = Regex.Match(s, @"^there are (?<n>\d+) or more (?:total )?cards in both players' trash(?:es)?$", IC)).Success)
            return new($"서로의 트래시의 합계가 {m.Groups["n"].Value}장 이상이라면,", $"서로의 트래시의 합계가 {m.Groups["n"].Value}장 이상인 동안,", $"서로의 트래시의 합계가 {m.Groups["n"].Value}장 이상이라면,");   // 공식 BT21-089
        if ((m = Regex.Match(s, @"^(?<n>\d+) (?<x>\[[^\]]+\])s? (?:are|is) placed in your battle area$", IC)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value);
            return new($"배틀 에어리어에 자신의 {x}{J(x, "이", "가")} {m.Groups["n"].Value}장 있다면,", $"배틀 에어리어에 자신의 {x}{J(x, "이", "가")} {m.Groups["n"].Value}장 있는 동안,", $"배틀 에어리어에 자신의 {x}{J(x, "이", "가")} {m.Groups["n"].Value}장 있다면,");   // 공식 BT17-100
        }
        if ((m = Regex.Match(s, @"^this card was played by (?<x>\[[^\]]+\])'s effect$", IC)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value);
            return new($"{x}의 효과로 등장하고 있었다면,", $"{x}의 효과로 등장한 동안,", $"{x}의 효과로 등장하고 있었다면,");   // 공식 EX3-025
        }
        if ((m = Regex.Match(s, @"^you have (?:a|an|1 or more) (?<pick>(?:Digimon |Tamer |Option )?card with .+?) in your trash$", IC)).Success && PickKo("1 " + m.Groups["pick"].Value) is { } tpk)
            return Have($"자신의 트래시에 {Regex.Replace(tpk, @"\s*1장$", "")}{J(Regex.Replace(tpk, @"\s*1장$", ""), "이", "가")}");
        if ((m = Regex.Match(s, @"^(?<x>\[[^\]]+\]) is in your battle area$", IC)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value);
            return Have($"자신의 {x}{J(x, "이", "가")} 배틀 에어리어에");   // 공식 EX3-033
        }
        if ((m = Regex.Match(s, @"^(?<a>.+?) ⟦or⟧ (?<b>.+)$", IC)).Success && CondKo(m.Groups["a"].Value) is { } oa && CondKo(m.Groups["b"].Value) is { } ob)
            return new($"{oa.Then.TrimEnd(',')} 또는 {ob.If}", $"{oa.Then.TrimEnd(',')} 또는 {ob.While}", $"{oa.Then.TrimEnd(',')} 또는 {ob.Then}");
        if ((m = Regex.Match(s, @"^(?<a>.+?), or if (?<b>.+)$", IC)).Success && CondKo(m.Groups["a"].Value) is { } ca && CondKo(m.Groups["b"].Value) is { } cb)
            return new($"{ca.Then.TrimEnd(',')} 또는 {cb.If}", $"{ca.Then.TrimEnd(',')} 또는 {cb.While}", $"{ca.Then.TrimEnd(',')} 또는 {cb.Then}");
        if ((m = Regex.Match(s, @"^(?<a>.+?), or (?<x>\[[^\]]+\]) is in your battle area$", IC)).Success && CondKo(m.Groups["a"].Value) is { } c1)
        {
            string x = NamesKo(m.Groups["x"].Value);
            string tail = $"자신의 배틀 에어리어에 {x}{J(x, "이", "가")} 있";
            return new($"{c1.Then.TrimEnd(',')} 또는 {tail}을 때,", $"{c1.Then.TrimEnd(',')} 또는 {tail}는 동안,", $"{c1.Then.TrimEnd(',')} 또는 {tail}다면,");
        }
        return null;
    }

    /// <summary>비용 (SentenceParser.ParseCost51)</summary>
    private static string? CostKo51(string cost)
    {
        if (!KoGate51) return null;
        string c = cost.Trim();
        Match m;
        if ((m = Regex.Match(c, @"^playing (?<pick>1 .+?) from this Digimon's digivolution cards(?<trash> or from your trash)?(?: without paying (?:the|its) cost)?$", IC)).Success
            && (PickKo(m.Groups["pick"].Value) ?? NamesPickKo51(m.Groups["pick"].Value)) is { } pk)
            return $"이 디지몬의 진화원{(m.Groups["trash"].Success ? " 또는 자신의 트래시" : "")}에서 {pk}{J(pk, "을", "를")} 코스트를 지불하지 않고 등장시키는 것으로,";
        if ((m = Regex.Match(c, @"^playing 1 card with the same level as one of your (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) trait Digimon from that Digimon's digivolution cards without paying (?:the|its) cost$", IC)).Success)
        {
            string t = NamesKo(m.Groups["x"].Value, trait: true);
            return $"특징으로 {t}{J(t, "을", "를")} 가진 자신의 디지몬의 진화원에서 그 디지몬과 같은 Lv.의 카드 1장을 코스트를 지불하지 않고 등장시키는 것으로,";
        }
        if ((m = Regex.Match(c, @"^playing 1 play cost (?<n>\d+) or (?:lower|less) Digimon card from under (?:this Tamer|your Tamers) without paying (?:the|its) cost$", IC)).Success)
            return $"이 테이머 아래에서 등장 코스트 {m.Groups["n"].Value} 이하의 디지몬 카드 1장을 코스트를 지불하지 않고 등장시키는 것으로,";
        if ((m = Regex.Match(c, @"^returning (?<list>1 \[[^\]]+\](?: plus 1 \[[^\]]+\])+) from your trash to the bottom of (?:your|the) deck(?: in any order)?$", IC)).Success)
        {
            var names = Regex.Matches(m.Groups["list"].Value, @"\[[^\]]+\]").Select(x => NamesKo(x.Value)).ToList();
            return $"자신의 트래시에서 {string.Concat(names.Take(names.Count - 1).Select(n => n + J(n, "과", "와") + " "))}{names[^1]} 1장씩을 덱 아래로 되돌리는 것으로,";   // 공식 EX3-035
        }
        if ((m = Regex.Match(c, @"^placing (?<pick>.+?) from your hand or trash (?:in|into) (?:the|your) battle area$", IC)).Success && PickKo(m.Groups["pick"].Value) is { } op)
            return $"자신의 패/트래시에서 {op}{J(op, "을", "를")} 배틀 에어리어에 놓는 것으로,";
        if ((m = Regex.Match(c, @"^trashing (?<n>\d+) of your Option cards (?<w>with .+?) in the battle area$", IC)).Success && PickKo($"{m.Groups["n"].Value} Option cards {m.Groups["w"].Value}") is { } ow)
            return $"자신의 배틀 에어리어의 {ow}{J(ow, "을", "를")} 파기하는 것으로,";
        if ((m = Regex.Match(c, @"^revealing up to (?<n>\d+) (?<pick>.+?) in your hand and placing them at the bottom of your deck(?: in any order)?$", IC)).Success && PickKo($"up to {m.Groups["n"].Value} {m.Groups["pick"].Value}") is { } rv)
            return $"자신의 패의 {rv}{J(rv, "을", "를")} 오픈하고 좋아하는 순서로 덱 아래에 놓는 것으로,";
        if ((m = Regex.Match(c, @"^revealing (?<pick>1 .+?) from your hand and placing it on top of your deck$", IC)).Success && PickKo(m.Groups["pick"].Value) is { } rt)
            return $"자신의 패의 {rt}{J(rt, "을", "를")} 오픈하고 덱 위에 놓는 것으로,";
        if ((m = Regex.Match(c, @"^placing (?<n>\d+) (?<x>\[[^\]]+\])s? from your trash as this Digimon's bottom digivolution cards?$", IC)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value);
            return $"자신의 트래시에서 {x} {m.Groups["n"].Value}장을 좋아하는 순서로 이 디지몬의 진화원 아래에 놓는 것으로,";
        }
        if ((m = Regex.Match(c, @"^trashing (?<n>\d+) digivolution cards from the bottom of 1 of your (?<x>\[[^\]]+\])$", IC)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value);
            return $"자신의 {x} 1마리의 진화원을 아래에서부터 {m.Groups["n"].Value}장 이상 파기하는 것으로,";   // 공식 EX2-055
        }
        return null;
    }

    private static string? NamesPickKo51(string pick)
    {
        var m = Regex.Match(pick.Trim(), @"^1 (?<x>\[[^\]]+\](?:(?:,|,? or) \[[^\]]+\])*)$", IC);
        return m.Success ? $"{NamesKo(m.Groups["x"].Value)} 1장" : null;
    }

    /// <summary>문장 (SentenceParser.ActionExtra51)</summary>
    private static string? SentenceFallback51(string s, bool nested)
    {
        if (!KoGate51) return null;
        s = s.Trim().TrimEnd('.').Trim();
        var lead = Regex.Match(s, @"^(?:Then,\s*)?(?:if (?<cond>[^,]+),\s*)?(?<may>you may )?(?<act>.+)$", IC);
        string? act = ActionKo51(lead.Groups["act"].Value, lead.Groups["may"].Success);
        if (act == null) return null;
        string pre = s.StartsWith("Then", StringComparison.OrdinalIgnoreCase) ? "그 후, " : "";
        if (lead.Groups["cond"].Success)
        {
            if (CondKo(lead.Groups["cond"].Value) is not { } ck) return null;
            pre += ck.Then + " ";
        }
        return pre + act;
    }

    private static string? ActionKo51(string a, bool may)
    {
        string end(string verb, string mayVerb) => may ? mayVerb : verb;
        Match m;
        if ((m = Regex.Match(a, @"^this Digimon may attack with ＜Security A\. \+(?<n>\d+)＞ for the attack$", IC)).Success)
            return $"이 디지몬으로 어택할 수 있다. 그 어택 동안 이 디지몬은 《시큐리티 어택 +{m.Groups["n"].Value}》을 얻는다";
        if (Regex.IsMatch(a, @"^Delete your opponent's Digimon until they have as many as the number of your security cards$", IC))
            return $"자신의 시큐리티 매수와 상대의 디지몬 마리수가 같아지도록, 상대의 디지몬을 {end("소멸시킨다", "소멸시킬 수 있다")}";   // 공식 BT19-094
        if (Regex.IsMatch(a, @"^delete all Digimon and Tamers$", IC))
            return $"디지몬과 테이머 전부를 {end("소멸시킨다", "소멸시킬 수 있다")}";   // 공식 BT5-110
        if ((m = Regex.Match(a, @"^return (?<pick>1 .+?) from under (?:one of your Tamers|your Tamers|1 of your Tamers) to (?:your|the) hand$", IC)).Success && PickKo(m.Groups["pick"].Value) is { } pk)
            return $"자신의 테이머 아래에서 {pk}{J(pk, "을", "를")} 패로 {end("되돌린다", "되돌릴 수 있다")}";
        if (Regex.IsMatch(a, @"^place this Digimon as that Digimon's bottom digivolution cards?$", IC))
            return $"이 디지몬을 등장한 디지몬의 진화원 아래에 {end("놓는다", "놓을 수 있다")}";
        if (Regex.IsMatch(a, @"^place 1 card from your hand face down as (?:any of those Digimon's|that Digimon's) bottom digivolution card$", IC))
            return $"자신의 패 1장을 그 디지몬 중 1마리의 진화원 아래에 뒷면으로 {end("놓는다", "놓을 수 있다")}";
        if (Regex.IsMatch(a, @"^At the end of (?:the|this) turn, return the Digimon played by this effect to (?:the|your|its owner's) hand$", IC))
            return "이 효과로 등장한 디지몬은 턴 종료 시에 패로 되돌아간다";
        if ((m = Regex.Match(a, @"^＜Draw (?<n>\d+)＞ for each card (?:placed at the bottom of your deck|returned by this effect)$", IC)).Success)
            return $"되돌린 1장마다, 《{m.Groups["n"].Value} 드로우》";   // 공식 BT12-048
        if ((m = Regex.Match(a, @"^use 1 Option card with a memory cost of (?<n>\d+) among them without paying its memory cost$", IC)).Success)
            return $"그중 사용 코스트 {m.Groups["n"].Value}인 옵션 카드 1장을 코스트를 지불하지 않고 {end("사용한다", "사용할 수 있다")}";
        if (Regex.IsMatch(a, @"^you win the game$", IC))
            return "자신은 게임에서 승리한다";   // 공식 BT17-100
        if ((m = Regex.Match(a, @"^all of your opponent's Digimon gain ＜Security A\. -(?<n>\d+)＞ ⟦instead-of:(?<o>\d+)⟧ until the end of your opponent's turn$", IC)).Success)
            return $"대신에 상대의 턴 종료까지 상대의 디지몬 전부는 《시큐리티 어택 -{m.Groups["n"].Value}》을 얻는다";
        return null;
    }
}
