using System;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.59.0 미지원 문장 33차 묶음의 생성 한글 — 원래 규칙(32차까지)이 옮기지 못한 절만 (SentenceParser.Batch59와 같은 표)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth59;
    [ThreadStatic] private static int _koGate59;
    private static bool KoGate59 => _koGate59 > 0;

    private static string? RenderClause59(string rawClause)
    {
        if (_clauseDepth59 > 0) return null;
        string t = Regex.Replace(rawClause.Replace(' ', ' ').Replace('’', '\'').Trim(), @"^Inherited Effect\s*", "", IC);
        string alt = SentenceParser.ClauseRewrite59(t);
        // 33차 표현이 없으면 다시 옮기지 않는다 (같은 결과 — 시간만 든다)
        if (alt == t && !SentenceParser.Hint59.IsMatch(t)) return null;
        _clauseDepth59++;
        _koGate59++;
        try { return RenderClause(alt); }
        finally { _clauseDepth59--; _koGate59--; }
    }

    /// <summary>문장 (SentenceParser.ParseAction59와 같은 표)</summary>
    private static string? SentenceFallback59(string s, bool nested)
    {
        if (!KoGate59) return null;
        s = s.Trim().TrimEnd('.').Trim();
        string pre = "";
        var then = Regex.Match(s, @"^Then,?\s+(?<r>.+)$", IC);
        if (then.Success) { pre = "그 후, "; s = then.Groups["r"].Value; }
        bool may = false;
        var mayM = Regex.Match(s, @"^you may (?<r>.+)$", IC);
        if (mayM.Success) { may = true; s = mayM.Groups["r"].Value; }
        Match m;
        // ST17-13
        if ((m = Regex.Match(s, @"^＜De-Digivolve (?<n>\d+)＞ 1 Digimon$", IC)).Success && !may)
            return $"{pre}디지몬 1마리를 《퇴화 {m.Groups["n"].Value}》 (위에서부터 {m.Groups["n"].Value}장 파기한다. Lv.3의 카드 이후는 파기할 수 없다) 한다.";
        if (Regex.IsMatch(s, @"^At the end of the battle, 1 of your Digimon may digivolve into this card without paying (?:the|its) (?:digivolution |memory )?cost$", IC) && !may)
            return $"{pre}배틀 종료 시, 자신의 디지몬 1마리를 이 카드로 진화 코스트를 지불하지 않고 진화시킬 수 있다.";
        // BT20-037
        if (Regex.IsMatch(s, @"^none of (?:their|your opponent's) Digimon or Tamers can activate \[On Play\] effects until the end of their turn$", IC) && !may)
            return $"{pre}상대의 턴 종료 시까지, 상대의 디지몬과 테이머는 【등장 시】 효과를 발휘할 수 없다.";
        // BT5-018
        if ((m = Regex.Match(s, @"^trash (?<p>1 .+?) (?:in|from) your hand ⟦dp59⟧$", IC)).Success && PickKo(m.Groups["p"].Value) is { } pk)
            return $"{pre}자신의 패에서 {pk}{J(pk, "을", "를")} 파기하여, 이 턴 동안, 이 디지몬을 파기한 카드의 DP만큼 DP + {(may ? "할 수 있다" : "한다")}.";
        // BT21-086
        if ((m = Regex.Match(s, @"^1 of your (?<x>\[[^\]]+\])s may suspend$", IC)).Success && !may)
        {
            string x = NamesKo(m.Groups["x"].Value);
            return $"{pre}자신의 {x} 1명을 레스트시킬 수 있다.";
        }
        // EX4-011 · LM-006 (트래시)
        if (Regex.IsMatch(s, @"^play this card with the play cost reduced by the play cost of the returned Tamer$", IC) && !may)
            return $"{pre}이 카드를 지불하는 등장 코스트를 되돌린 테이머의 등장 코스트만큼 줄여 등장시킨다.";
        // BT14-006 · BT24-007
        if (Regex.IsMatch(s, @"^this Digimon may digivolve into that card ⟦ht59:[^⟧]+⟧$", IC) && !may)
            return $"{pre}이 디지몬을 그 카드로 진화시킬 수 있다.";
        if ((m = Regex.Match(s, @"^play 1 of them with the play cost reduced by (?<n>\d+) ⟦ht59:[^⟧]+⟧$", IC)).Success)
            return $"{pre}그중 1장을 지불하는 등장 코스트 -{m.Groups["n"].Value} 하여 등장{(may ? "시킬 수 있다" : "시킨다")}.";
        // BT16-072 · BT16-073
        if ((m = Regex.Match(s, @"^play (?<p>1 Tamer card with \[[^\]]+\] in its text) ⟦nosame59⟧ from your trash without paying (?:the|its) cost$", IC)).Success && PickKo(m.Groups["p"].Value) is { } tk)
            return $"{pre}자신의 트래시에서 자신의 테이머와 같은 명칭이 아닌 {tk}{J(tk, "을", "를")} 코스트를 지불하지 않고 등장{(may ? "시킬 수 있다" : "시킨다")}.";
        // BT16-031 (바꿔 쓴 "return 1 red or purple level 6 or lower 2-color Digimon card from your trash to the hand")
        if ((m = Regex.Match(s, @"^return 1 (?<a>red|blue|yellow|green|black|purple|white) or (?<b>red|blue|yellow|green|black|purple|white) level (?<lv>\d+) or lower (?<k>\d+)-colou?r Digimon card from your trash to (?:the|your) hand$", IC)).Success)
            return $"{pre}자신의 트래시에서 {ColorKo37(m.Groups["a"].Value)} 또는 {ColorKo37(m.Groups["b"].Value)}인 Lv.{m.Groups["lv"].Value} 이하의 {m.Groups["k"].Value}색 디지몬 카드 1장을 패에 {(may ? "되돌릴 수 있다" : "되돌린다")}.";
        // BT27-035
        if (Regex.IsMatch(s, @"^return (?:their|your opponent's) top security card to the bottom of (?:the|their|its owner's) deck$", IC))
            return $"{pre}상대의 시큐리티 맨 위의 카드를 덱 아래로 {(may ? "되돌릴 수 있다" : "되돌린다")}.";
        return null;
    }

    /// <summary>비용 (SentenceParser.ParseCost59와 같은 표)</summary>
    private static string? CostKo59(string cost)
    {
        if (!KoGate59) return null;
        cost = cost.Trim().TrimEnd('.');
        Match m;
        if ((m = Regex.Match(cost, @"^returning (?:random )?cards (?:from their hand )?(?:to the bottom of (?:their owner's|their|the) deck )?without looking(?: at them)?(?: to the bottom of (?:the|their|their owner's) deck)? (?:until they have only|so that it has) (?<n>\d+)(?: cards)? left$", IC)).Success)
            return $"상대의 패를 보지 않고 {m.Groups["n"].Value}장이 될 때까지 무작위로 덱 아래로 되돌리는 것으로,";
        if (Regex.IsMatch(cost, @"^returning 1 of your Tamers to the bottom of the deck ⟦pc59⟧$", IC))
            return "자신의 테이머 1명을 덱 아래로 되돌리는 것으로,";
        return null;
    }
}
