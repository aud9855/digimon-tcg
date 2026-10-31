using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.49.0 미지원 문장 23차 묶음의 생성 한글 — 원래 규칙(22차까지)이 옮기지 못한 절 · 문장 · 조건만 (SentenceParser.Batch49와 같은 표)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth49;
    [ThreadStatic] private static int _koGate49;
    private static bool KoGate49 => _koGate49 > 0;

    /// <summary>절: 23차 문장 한글을 켠 채로 다시 · 같은 뜻 다른 표기(SentenceParser.ClauseRules49)로</summary>
    private static string? RenderClause49(string rawClause)
    {
        if (_clauseDepth49 > 0) return null;
        _clauseDepth49++;
        _koGate49++;
        try
        {
            if (RenderClause(rawClause) is { } again) return again;
            string alt = SentenceParser.ClauseRewrite49(rawClause);
            return alt != rawClause ? RenderClause(alt) : null;
        }
        finally { _clauseDepth49--; _koGate49--; }
    }

    /// <summary>조건 (SentenceParser.ParseEventCondition49)</summary>
    private static Cond? CondKo49(string cond)
    {
        if (!KoGate49) return null;
        Match m;
        string c = cond.Trim();
        if ((m = Regex.Match(c, @"^this effect returned an? (?<c>red|blue|yellow|green|black|purple|white) level (?<lv>\d+) card$", IC)).Success)
        {
            string x = $"이 효과로 Lv.{m.Groups["lv"].Value} {ColorKo49(m.Groups["c"].Value)} 카드를 되돌렸다면,";
            return new(x, x);
        }
        if ((m = Regex.Match(c, @"^(?<n>\d+) or more cards were returned (?:using|by|with) this effect$", IC)).Success)
        {
            string x = $"이 효과로 {m.Groups["n"].Value}장 이상 되돌렸다면,";
            return new(x, x);
        }
        if (Regex.IsMatch(c, @"^your opponent has as many or fewer total Digimon and Tamers as you$", IC))
            return new("상대의 디지몬과 테이머의 합계가 자신 이하일 때,", "상대의 디지몬과 테이머의 합계가 자신 이하인 동안,", "상대의 디지몬과 테이머의 합계가 자신 이하라면,");
        return null;
    }

    private static string ColorKo49(string en) => en.ToLowerInvariant() switch
    {
        "red" => "레드", "blue" => "블루", "yellow" => "옐로", "green" => "그린", "black" => "블랙", "purple" => "퍼플", _ => "화이트",
    };

    /// <summary>문장 (SentenceParser.ActionExtra49 · SecurityAndSources49)</summary>
    private static string? SentenceFallback49(string s, bool nested)
    {
        if (!KoGate49) return null;
        s = s.Trim().TrimEnd('.').Trim();
        // 덱 위 공개 "You may play or use 1 ~ among them without paying the cost / with the cost reduced by 3" (LM-067 · BT26-084)
        var rv = Regex.Match(s, @"^(?<then>Then, )?[Rr]eveal the top (?<n>\d+) cards of your deck\. You may play or use (?<p>.+?)(?: (?:from )?among them)?(?: without paying (?:its|the|their) (?:memory |play |use )?costs?| with the (?:play |use )?cost reduced by (?<red>\d+))\. (?:Return|Place) the (?:rest|remaining cards) (?:to|at|on) the (?<pos>top or bottom|top or the bottom|bottom|top) of the deck(?: in any order)?$", IC);
        if (rv.Success && PickKo(SentenceParser.NormalizePick39(rv.Groups["p"].Value)) is { } rp)
        {
            string pos = rv.Groups["pos"].Value.ToLowerInvariant();
            string restKo = pos.StartsWith("top or") ? "나머지는 원하는 순서대로 덱 위 또는 아래로 되돌린다" : pos == "top" ? "나머지는 원하는 순서대로 덱 위로 되돌린다" : "나머지는 원하는 순서대로 덱 아래로 되돌린다";
            string how = rv.Groups["red"].Success ? $"코스트를 {rv.Groups["red"].Value} 줄여서" : "코스트를 지불하지 않고";
            return $"{(rv.Groups["then"].Success ? "그 후, " : "")}자신의 덱 위에서부터 {rv.Groups["n"].Value}장 오픈한다. 그중 {rp}{J(rp, "을", "를")} {how} 등장시키거나 사용할 수 있다. {restKo}";
        }
        var lead = Regex.Match(s, @"^(?:Then,\s*)?(?:if (?<cond>[^,]+),\s*)?(?<may>you may )?(?<act>.+)$", IC);
        string? act = ActionKo49(lead.Groups["act"].Value, lead.Groups["may"].Success);
        if (act == null) return null;
        string pre = s.StartsWith("Then", StringComparison.OrdinalIgnoreCase) ? "그 후, " : "";
        if (lead.Groups["cond"].Success)
        {
            if (CondKo(lead.Groups["cond"].Value) is not { } ck) return null;
            pre += ck.Then + " ";
        }
        return pre + act;
    }

    private static string? ActionKo49(string a, bool may)
    {
        string end(string verb, string mayVerb) => may ? mayVerb : verb;
        Match m;
        if (Regex.IsMatch(a, @"^return all cards from your or your opponent's trash to the bottom of (?:the|their owners'?|its owner's) decks?$", IC))
            return $"자신 또는 상대의 트래시의 카드를 전부 덱 아래로 {end("되돌린다", "되돌릴 수 있다")}";
        if ((m = Regex.Match(a, @"^return all cards with the (?<t>\[[^\]]+\](?: or \[[^\]]+\])*) traits? and all Option cards from both players' trash(?:es)? to the bottom of (?:their owners'?|its owner's|the) decks?$", IC)).Success)
        {
            string t = NamesKo(m.Groups["t"].Value, trait: true);
            return $"서로의 트래시의 특징으로 {t}{J(t, "을", "를")} 가진 카드와 옵션 카드를 전부 각자의 덱 아래로 {end("되돌린다", "되돌릴 수 있다")}";
        }
        if ((m = Regex.Match(a, @"^play (?<a>1 .+?) from your trash or (?<b>1 .+?) from the digivolution cards of your Digimon in the breeding area without paying (?:its|the) costs?(?: ⟦not:(?<names>[^⟧]+)⟧)?$", IC)).Success
            && PickKo(m.Groups["a"].Value) is { } pa && PickKo(m.Groups["b"].Value) is { } pb)
        {
            string not = m.Groups["names"].Success ? $". 이 효과로 {NamesKo(m.Groups["names"].Value)}{J(NamesKo(m.Groups["names"].Value), "은", "는")} 등장시킬 수 없다" : "";
            return $"자신의 트래시의 {pa}, 또는 육성 에어리어의 자신의 디지몬의 진화원의 {pb}{J(pb, "을", "를")} 코스트를 지불하지 않고 {end("등장시킨다", "등장시킬 수 있다")}{not}";
        }
        if ((m = Regex.Match(a, @"^play (?<n>\d+) Digimon cards with different colou?rs? and (?<rest>.+?) from your hand without paying (?:the|its|their) costs?$", IC)).Success
            && PickKo($"{m.Groups["n"].Value} Digimon cards with {m.Groups["rest"].Value}") is { } pd)
            return $"자신의 패에서 색이 서로 다른 {pd}{J(pd, "을", "를")} 코스트를 지불하지 않고 {end("등장시킨다", "등장시킬 수 있다")}";
        if ((m = Regex.Match(a, @"^play 1 (?<a>\[[^\]]+\]) if (?:that Digimon|it) has (?<x>\[[^\]]+\]) in its name (?<j>and|or) 1 (?<b>\[[^\]]+\]) if (?:that Digimon|it) has (?<y>\[[^\]]+\]) in its name from your hand or trash without paying (?:the|its) costs?$", IC)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value), y = NamesKo(m.Groups["y"].Value), na = NamesKo(m.Groups["a"].Value), nb = NamesKo(m.Groups["b"].Value);
            bool and = m.Groups["j"].Value.Equals("and", StringComparison.OrdinalIgnoreCase);
            return $"그 디지몬의 명칭에 {x}{J(x, "이", "가")} 포함되어 있으면 {na} 1장{(and ? "," : " 또는")} 명칭에 {y}{J(y, "이", "가")} 포함되어 있으면 {nb} 1장을 자신의 패 또는 트래시에서 코스트를 지불하지 않고 {end("등장시킨다", "등장시킬 수 있다")}";
        }
        // ---- 시큐리티 ----
        if (Regex.IsMatch(a, @"^place this Digimon (?:on top of your security stack|as your top security card)(?: face down)?$", IC))
            return $"이 디지몬을 자신의 시큐리티 위에 뒷면으로 {end("놓는다", "놓을 수 있다")}";
        if (Regex.IsMatch(a, @"^trash that Digimon's digivolution cards ⟦after-security⟧$", IC))
            return $"그 디지몬의 진화원을 전부 {end("파기한다", "파기할 수 있다")}";
        if ((m = Regex.Match(a, @"^place this card as (?:the|your) (?<pos>top|bottom) security card$", IC)).Success)
            return $"이 카드를 자신의 시큐리티 {(m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase) ? "위" : "아래")}에 뒷면으로 {end("놓는다", "놓을 수 있다")}";
        if (Regex.IsMatch(a, @"^place 1 of each player's Digimon as the top security cards?$", IC))
            return $"서로의 디지몬 1마리씩을 각자의 시큐리티 위에 뒷면으로 {end("놓는다", "놓을 수 있다")}";
        if ((m = Regex.Match(a, @"^place all level (?<lv>\d+) Digimon(?: face down)? on top of their owners'? security stacks?(?: face down)?(?: in any order)?$", IC)).Success)
            return $"Lv.{m.Groups["lv"].Value} 디지몬 전부를 원하는 순서로 각자의 시큐리티 위에 뒷면으로 {end("놓는다", "놓을 수 있다")}";
        if ((m = Regex.Match(a, @"^reveal all of your opponent's security cards, and (?:(?<trash>trash 1 of them)|place 1 card among them on top of your opponent's deck), then shuffle the rest into their security stack$", IC)).Success)
            return m.Groups["trash"].Success
                ? "상대의 시큐리티를 전부 오픈하고, 그중 1장을 파기한다. 나머지는 뒷면으로 상대의 시큐리티에 되돌리고 섞는다"
                : "상대의 시큐리티를 전부 오픈하고, 그중 1장을 상대의 덱 위에 놓는다. 나머지는 섞어서 시큐리티에 되돌린다";
        if (Regex.IsMatch(a, @"^reveal 1 card of the same level in your hand, then place that card on top of your security stack(?: face down)?$", IC))
            return $"자신의 패에서 같은 Lv.의 카드 1장을 오픈하고, 그 카드를 자신의 시큐리티 위에 뒷면으로 {end("놓는다", "놓을 수 있다")}";
        // ---- 진화원 ----
        if ((m = Regex.Match(a, @"^place up to (?<n>\d+) (?:differently named (?<p1>.+?)|(?<p2>cards|Digimon cards) with different names and (?<rest>.+?)) from your trash (?:as this Digimon's bottom digivolution cards?|under this Digimon as its bottom digivolution cards?)$", IC)).Success)
        {
            string n = m.Groups["n"].Value;
            string? pk = m.Groups["p1"].Success ? PickKo($"up to {n} {m.Groups["p1"].Value}") : PickKo($"up to {n} {m.Groups["p2"].Value} with {m.Groups["rest"].Value}");
            if (pk == null) return null;
            return $"자신의 트래시에서 명칭이 다른 {pk}{J(pk, "을", "를")} 이 디지몬의 진화원 아래에 {end("놓는다", "놓을 수 있다")}";
        }
        if ((m = Regex.Match(a, @"^return up to (?<n>\d+) (?<nde>non-Digi-Egg )?cards with the (?<t>\[[^\]]+\]) trait from this Digimon's digivolution cards to the bottom of (?:your|the) deck(?: in any order)?, if you do, gain 1 memory for each card returned$", IC)).Success)
        {
            string t = NamesKo(m.Groups["t"].Value, trait: true);
            return $"이 디지몬의 진화원에서 {(m.Groups["nde"].Success ? "디지타마 이외의 " : "")}특징으로 {t}{J(t, "을", "를")} 가진 카드 {m.Groups["n"].Value}장까지를 원하는 순서로 덱 아래로 {end("되돌린다", "되돌릴 수 있다")}. 그렇게 했다면, 되돌린 1장마다 메모리 +1";
        }
        if ((m = Regex.Match(a, @"^return the bottom (?<n>\d+) digivolution cards of 1 of your opponent's Digimon to the bottom of (?:the|its owner's|their) deck$", IC)).Success)
            return $"상대의 디지몬 1마리의 진화원을 아래에서부터 {m.Groups["n"].Value}장 덱 아래로 {end("되돌린다", "되돌릴 수 있다")}";
        return null;
    }
}
