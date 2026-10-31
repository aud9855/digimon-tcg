using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.50.0 미지원 문장 24차 묶음의 생성 한글 — 원래 규칙(23차까지)이 옮기지 못한 절 · 문장 · 조건 · 비용 · 셈만 (SentenceParser.Batch50과 같은 표)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth50;
    [ThreadStatic] private static int _koGate50;
    private static bool KoGate50 => _koGate50 > 0;

    /// <summary>절: 24차 한글을 켠 채로 다시 · 같은 뜻 다른 표기(SentenceParser.ClauseRules50)로</summary>
    private static string? RenderClause50(string rawClause)
    {
        if (_clauseDepth50 > 0) return null;
        _clauseDepth50++;
        _koGate50++;
        try
        {
            // "· ~" 선택지 줄 (EX12-066 ~ EX12-068) — "・~"와 같다
            string clause = Regex.Replace(rawClause, @"^\s*·\s*", "・");
            if (RenderClause(clause) is { } again) return again;
            // 고르는 효과 머리말 "[Your Turn] When ~, by ~, activate 1 of the effects below:" (EX12-066 ~ EX12-068) — 사건 · 비용은 원래 규칙으로 옮기고 본문만 바꾼다
            if (Regex.Match(clause, @"^(?<head>(?:\s*\[[^\]]+\])*\s*When [^,]+, (?:by [^,]+, )?)(?<may>you may )?activate (?:1|one) of the (?:effects below|following effects)[:.]?$", IC) is { Success: true } ch)
            {
                const string probe = "gain 1 memory.";
                if (RenderClause(ch.Groups["head"].Value + probe) is { } hk && hk.EndsWith("메모리를 +1 한다."))
                    return hk[..^"메모리를 +1 한다.".Length] + (ch.Groups["may"].Success ? "이하의 효과에서 1개를 골라 발휘할 수 있다." : "이하의 효과에서 1개를 골라 발휘한다.");
                return null;
            }
            string alt = SentenceParser.ClauseRewrite50(clause);
            if (alt == clause || RenderClause(alt) is not { } ko) return null;
            // "can only attack suspended Digimon" (EX11-011 · EX8-016) — "레스트 상태의 디지몬에게만 어택할 수 있다"
            if (Regex.IsMatch(clause, @"\bcan only attack suspended Digimon\b", IC))
                ko = ko.Replace("플레이어에게 어택할 수 없다", "레스트 상태의 디지몬에게만 어택할 수 있다");
            return ko;
        }
        finally { _clauseDepth50--; _koGate50--; }
    }

    /// <summary>조건 (SentenceParser.ParseEventCondition50)</summary>
    private static Cond? CondKo50(string cond)
    {
        if (!KoGate50) return null;
        Match m;
        if ((m = Regex.Match(cond.Trim(), @"^there are (?<n>\d+) or more (?<x>\[[^\]]+\]) trait cards under this Tamer$", IC)).Success)
        {
            string t = NamesKo(m.Groups["x"].Value, trait: true);
            string head = $"이 테이머 아래에 특징으로 {t}{J(t, "을", "를")} 가진 카드가 {m.Groups["n"].Value}장 이상 있";
            return new(head + "을 때,", head + "는 동안,", head + "다면,");
        }
        if (Regex.IsMatch(cond.Trim(), @"^attacking a Digimon with (?:DP higher than|more DP than|higher DP than) this Digimon$", IC))
            return new("이 디지몬보다 DP가 높은 디지몬에게 어택하고 있을 때,", "이 디지몬보다 DP가 높은 디지몬에게 어택하고 있는 동안,", "이 디지몬보다 DP가 높은 디지몬에게 어택하고 있다면,");
        return null;
    }

    /// <summary>셈 (SentenceParser.ParseCounter50)</summary>
    private static string? CounterKo50(string counter)
    {
        if (!KoGate50) return null;
        if (Regex.IsMatch(counter.Trim(), @"^suspended Digimon or Tamers?$", IC)) return "레스트 상태의 디지몬/테이머 1마리(명)마다,";
        return null;
    }

    /// <summary>비용 (SentenceParser.ParseCost50)</summary>
    private static string? CostKo50(string cost)
    {
        if (!KoGate50) return null;
        Match m;
        if ((m = Regex.Match(cost.Trim(), @"^returning 1 of your (?<n>\[[^\]]+\]) to the bottom of the deck and trashing its digivolution cards$", IC)).Success)
        {
            string n = NamesKo(m.Groups["n"].Value);
            return $"자신의 {n} 1마리를 덱 아래로 되돌리고 그 진화원은 파기하는 것으로,";
        }
        if ((m = Regex.Match(cost.Trim(), @"^returning (?<n>\d+) cards with different names and (?<rest>.+?) from this Digimon's digivolution cards to the bottom of the deck$", IC)).Success
            && PickKo($"{m.Groups["n"].Value} cards with {m.Groups["rest"].Value}") is { } pk)
            return $"이 디지몬의 진화원에서 명칭이 서로 다른 {pk}{J(pk, "을", "를")} 덱 아래로 되돌리는 것으로,";
        return null;
    }

    /// <summary>문장 (SentenceParser.ActionExtra50 · ParseStatic50)</summary>
    private static string? SentenceFallback50(string s, bool nested)
    {
        if (!KoGate50) return null;
        s = s.Trim().TrimEnd('.').Trim();
        // 고르는 효과 머리말 "When ~, by ~, activate 1 of the effects below:" (EX12-066 ~ EX12-068) — 절의 사건은 원래 규칙이 옮긴다
        var lead = Regex.Match(s, @"^(?:Then,\s*)?(?:if (?<cond>[^,]+),\s*)?(?<may>you may )?(?<act>.+)$", IC);
        string? act = ActionKo50(lead.Groups["act"].Value, lead.Groups["may"].Success);
        if (act == null) return null;
        string pre = s.StartsWith("Then", StringComparison.OrdinalIgnoreCase) ? "그 후, " : "";
        if (lead.Groups["cond"].Success)
        {
            if (CondKo(lead.Groups["cond"].Value) is not { } ck) return null;
            pre += ck.Then + " ";
        }
        return pre + act;
    }

    private static string? ActionKo50(string a, bool may)
    {
        string end(string verb, string mayVerb) => may ? mayVerb : verb;
        Match m;
        // 지속: "This Digimon can't suspend" (「석화」 토큰)
        if (Regex.IsMatch(a, @"^this Digimon can't suspend$", IC))
            return "이 디지몬은 레스트할 수 없다";
        // "they play 1 [Petrification] Token⦃~⦄" — 상대가 등장시킨다
        if ((m = Regex.Match(a, @"^(?:they|your opponent) plays? (?<rest>(?:\d+|a|an) \[[^\]]+\] Tokens?⦃[^⦄]*⦄(?: without paying (?:the|its) (?:memory )?costs?)?)$", IC)).Success
            && Sentence("Play " + m.Groups["rest"].Value, nested: true) is { } tk)
            return "상대는 " + tk.TrimEnd('.', ' ');
        // 색 / 명칭이 서로 다른 카드를 테이머 아래에
        if ((m = Regex.Match(a, @"^place up to (?<n>\d+) (?<pick>.+?) cards? with different (?<what>names|colou?rs) from (?<from>your hand or (?:your )?trash|your trash|your hand) under (?<t>this Tamer|1 of your Tamers)(?: ⟦plus-other-tamers:(?<plus>\d+)⟧)?$", IC)).Success
            && PickKo($"up to {m.Groups["n"].Value} {m.Groups["pick"].Value} cards") is { } pk)
        {
            string from = m.Groups["from"].Value.ToLowerInvariant() switch { var f when f.Contains("hand") && f.Contains("trash") => "자신의 패/트래시에서", var f when f.Contains("hand") => "자신의 패에서", _ => "자신의 트래시에서" };
            string what = m.Groups["what"].Value.StartsWith("name", StringComparison.OrdinalIgnoreCase) ? "명칭" : "색";
            string dest = m.Groups["t"].Value.Equals("this Tamer", StringComparison.OrdinalIgnoreCase) ? "이 테이머 아래에" : "자신의 테이머 1명의 아래에";
            string plus = m.Groups["plus"].Success ? $". 다른 자신의 테이머 1명마다 이 효과로 놓을 수 있는 매수 상한 +{m.Groups["plus"].Value}" : "";
            return $"{from} {what}이 서로 다른 {pk}{J(pk, "을", "를")} {dest} {end("놓는다", "놓을 수 있다")}{plus}";
        }
        if ((m = Regex.Match(a, @"^1 of your Tamers with (?<n>\d+) or more cards under it may digivolve into (?<x>\[[^\]]+\]) in the (?<from>hand or trash|hand|trash), ignoring (?:its )?digivolution requirements and without paying (?:the|its) costs?$", IC)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value);
            string z = m.Groups["from"].Value.ToLowerInvariant() switch { "hand or trash" => "패/트래시의", "hand" => "패의", _ => "트래시의" };
            return $"아래에 카드가 {m.Groups["n"].Value}장 이상 있는 자신의 테이머 1명을 {z} {x}{J(x, "으로", "로")} 진화 조건을 무시하고 코스트를 지불하지 않고 진화시킬 수 있다";
        }
        if (Regex.IsMatch(a, @"^return this Tamer to the bottom of (?:the|your|its owner's) deck$", IC))
            return $"이 테이머를 덱 아래로 {end("되돌린다", "되돌릴 수 있다")}";
        if ((m = Regex.Match(a, @"^delete 1 of your opponent's Tamers or (?<spec>[^,]+?) Digimon$", IC)).Success
            && TargetKo($"1 of your opponent's {m.Groups["spec"].Value} Digimon") is { } dk)
            return $"상대의 테이머 1명 또는 {dk}{J(dk, "을", "를")} {end("소멸시킨다", "소멸시킬 수 있다")}";
        if (Regex.IsMatch(a, @"^declare 1 card category and reveal the top card of your opponent's deck for category immunity$", IC))
            return "카드 종류 1개를 선언한다. 그 후, 상대의 덱 위에서부터 1장 오픈하고, 그 카드가 선언한 종류라면, 턴 종료 시까지 이 디지몬은 이 디지몬 이외의 그 종류의 효과를 받지 않는다. 오픈한 카드는 상대의 덱 위 또는 아래로 되돌린다";
        if (Regex.IsMatch(a, @"^activate (?:1|one) of this Digimon's \[Security\] effects$", IC))
            return $"이 디지몬의 【시큐리티】 효과 1개를 {end("발휘한다", "발휘할 수 있다")}";
        if ((m = Regex.Match(a, @"^delete (?<n>\d+) of your opponent's Digimon or Tamers, then trash the top (?<k>\d+) cards of your opponent's security stack minus 1 for each card deleted$", IC)).Success)
            return $"상대의 디지몬/테이머 {m.Groups["n"].Value}마리(명)를 소멸시킨다. 그 후, 상대의 시큐리티를 위에서부터 {m.Groups["k"].Value}장 파기한다. 이 효과로 소멸시킨 1마리(명)마다 파기하는 매수 -1";
        if ((m = Regex.Match(a, @"^place up to (?<n>\d+) of your other (?<spec>[^,]*?)Digimon as this Digimon's bottom digivolution cards ⟦then-return-other-level:(?<lv>\d+)⟧$", IC)).Success
            && TargetKo($"up to {m.Groups["n"].Value} of your other {m.Groups["spec"].Value}Digimon") is { } tg)
            return $"{tg}{J(tg, "을", "를")} 이 디지몬의 진화원 아래에 놓을 수 있다. 그 후, Lv.{m.Groups["lv"].Value} 이하의 다른 디지몬 전부를 패로 되돌린다. 이 효과로 놓은 1장마다 되돌리는 Lv. 상한 +1";
        return null;
    }
}
