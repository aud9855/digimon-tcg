using System;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.53.0 미지원 문장 27차 묶음의 생성 한글 — 원래 규칙(26차까지)이 옮기지 못한 절 · 문장 · 사건 · 비용만 (SentenceParser.Batch53과 같은 표)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth53;
    [ThreadStatic] private static int _koGate53;
    private static bool KoGate53 => _koGate53 > 0;

    /// <summary>절: 27차 한글을 켠 채로 다시 · 같은 뜻 다른 표기(SentenceParser.ClauseRules53)로</summary>
    private static string? RenderClause53(string rawClause)
    {
        if (_clauseDepth53 > 0) return null;
        _clauseDepth53++;
        _koGate53++;
        try
        {
            if (ClauseSpecialKo53(rawClause) is { } special) return special;
            string alt = SentenceParser.ClauseRewrite53(rawClause);
            if (SentenceParser.Hint53.IsMatch(alt) && RenderClause(alt) is { } again) return again;
            if (alt == rawClause || RenderClause(alt) is not { } ko) return null;
            // BT25-060 "~ gains ＜Piercing＞ and ＜Blocker＞, and your opponent's Digimon effects don't affect it" — 같은 디지몬 ("그 디지몬"이 아니다)
            return ko.Replace("를 얻고, 그 디지몬은 상대의 디지몬의 효과를 받지 않는다", "를 얻고, 상대의 디지몬의 효과를 받지 않는다");
        }
        finally { _clauseDepth53--; _koGate53--; }
    }

    /// <summary>절 하나로 옮기는 것 (SentenceParser.ClauseSpecial53)</summary>
    private static string? ClauseSpecialKo53(string rawClause)
    {
        string t = rawClause.Trim();
        Match m;
        if ((m = Regex.Match(t, @"^\[Opponent's Turn\]\s*\[Once Per Turn\]\s*When your security (?:stack )?is checked, if that card is a Digimon card with the (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) trait, play it without battling and without paying the cost\.?$", IC)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value, trait: true);
            return $"【상대의 턴】 [턴에 1회] 자신의 시큐리티가 체크되었을 때, 그 카드가 특징으로 {x}{J(x, "을", "를")} 가진 디지몬 카드라면, 배틀을 진행하지 않고, 그 카드를 코스트를 지불하지 않고 등장시킨다.";
        }
        if (Regex.IsMatch(t, @"^\[Your Turn\]\s*While your opponent has no unsuspended Digimon, your ＜Vortex＞ (?:Digimon )?can also attack (?:players|your opponent)\.?$", IC))
            return "【자신의 턴】 상대에게 액티브 상태인 디지몬이 없는 동안, 자신의 《볼텍스》는 플레이어에게도 어택할 수 있다.";
        return null;
    }

    /// <summary>사건 (SentenceParser.ParseEventPhrase53)</summary>
    private static string? EventKo53(string ev)
    {
        if (!KoGate53) return null;
        if (Regex.IsMatch(ev.Trim(), @"^this Digimon deletes (?:another|other) Digimon$", IC))
            return "이 디지몬이 다른 디지몬을 소멸시켰을 때,";   // 공식 BT16-061
        return null;
    }

    /// <summary>비용 (SentenceParser.ParseCost53)</summary>
    private static string? CostKo53(string cost)
    {
        if (!KoGate53) return null;
        string c = cost.Trim();
        if (Regex.IsMatch(c, @"^trashing (?:1 of )?your Digimon in (?:the|your) breeding area$", IC))
            return "육성 에어리어의 자신의 디지몬 1마리를 파기하는 것으로,";
        if (Regex.IsMatch(c, @"^placing 1 other Digimon or Tamer as the top or bottom security card$", IC))
            return "다른 디지몬/테이머 1마리(명)를 시큐리티 위 또는 아래에 놓는 것으로,";   // 공식 P-187
        return null;
    }

    /// <summary>문장 (SentenceParser.ParseAction53)</summary>
    private static string? SentenceFallback53(string s, bool nested)
    {
        if (!KoGate53) return null;
        s = s.Trim().TrimEnd('.').Trim();
        var lead = Regex.Match(s, @"^(?:Then,\s*)?(?:if (?<cond>[^,]+),\s*)?(?<inst>instead,?\s*)?(?<may>you may )?(?<act>.+)$", IC);
        string? act = ActionKo53(lead.Groups["act"].Value, lead.Groups["may"].Success);
        if (act == null) return null;
        string pre = s.StartsWith("Then", StringComparison.OrdinalIgnoreCase) ? "그 후, " : "";
        if (lead.Groups["cond"].Success)
        {
            if (CondKo(lead.Groups["cond"].Value) is not { } ck) return null;
            pre += ck.Then + " ";
        }
        if (lead.Groups["inst"].Success) pre += "대신 ";
        return pre + act;
    }

    private static string? ActionKo53(string a, bool may)
    {
        string end(string verb, string mayVerb) => may ? mayVerb : verb;
        Match m;
        if ((m = Regex.Match(a, @"^delete up to (?<n>\d+) play cost's total worth of your opponent's Digimon and(?:/or)? Tamers$", IC)).Success)
            return $"등장 코스트 합계 {m.Groups["n"].Value} 이하가 되도록 상대의 디지몬과 테이머를 선택하고, 선택한 디지몬과 테이머 전부를 {end("소멸시킨다", "소멸시킬 수 있다")}";   // 공식 P-094
        if (Regex.IsMatch(a, @"^delete 1 of each of your opponent's Digimon with different colou?rs$", IC))
            return $"색이 서로 다른 상대의 디지몬 1마리씩을 {end("소멸시킨다", "소멸시킬 수 있다")}";   // 공식 EX9-074
        if ((m = Regex.Match(a, @"^play (?<p>1 .+?) from your hand to (?:an empty space in )?your (?:empty )?breeding area for a play cost of (?<n>\d+)$", IC)).Success && PickKo(m.Groups["p"].Value) is { } pk)
            return $"자신의 패에서 {pk}{J(pk, "을", "를")} 육성 에어리어에 등장 코스트 {m.Groups["n"].Value}으로 {end("등장시킨다", "등장시킬 수 있다")}";   // 공식 BT16-090
        return null;
    }
}
