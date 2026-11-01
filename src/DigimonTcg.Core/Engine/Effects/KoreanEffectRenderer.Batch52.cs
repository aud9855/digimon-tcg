using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.52.0 미지원 문장 26차 묶음의 생성 한글 — 원래 규칙(25차까지)이 옮기지 못한 절 · 사건 · 비용만 (SentenceParser.Batch52와 같은 표)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth52;
    [ThreadStatic] private static int _koGate52;
    [ThreadStatic] private static int _costKoDepth52;
    private static bool KoGate52 => _koGate52 > 0;

    /// <summary>절: 26차 한글을 켠 채로 다시 · 같은 뜻 다른 표기(SentenceParser.ClauseRules52)로</summary>
    private static string? RenderClause52(string rawClause)
    {
        if (_clauseDepth52 > 0) return null;
        _clauseDepth52++;
        _koGate52++;
        try
        {
            if (ClauseSpecialKo52(rawClause) is { } special) return special;
            if (SentenceParser.Hint52.IsMatch(rawClause) && RenderClause(rawClause) is { } again) return again;
            string alt = SentenceParser.ClauseRewrite52(rawClause);
            if (alt == rawClause || RenderClause(alt) is not { } ko) return null;
            // BT9-082 "delete 1 of ~ and all of their ~" — 한 효과: "~ 1마리와 ~ 전부를 소멸시킨다"
            if (Regex.IsMatch(rawClause, @"\bdelete 1 of your opponent's [^.]*?Digimon and all of their [^.]*?Digimon\.", IC))
                ko = Regex.Replace(ko, @"디지몬 1마리를 소멸시킨다\. (?<b>(?:Lv\.\d+ )?[^.]*?디지몬 전부를 소멸시킨다)", "디지몬 1마리와 ${b}");
            return ko;
        }
        finally { _clauseDepth52--; _koGate52--; }
    }

    /// <summary>절 하나로 옮기는 것 (SentenceParser.ClauseSpecial52)</summary>
    private static string? ClauseSpecialKo52(string rawClause)
    {
        string t = rawClause.Trim();
        // BT3-056 ≪흡수진화≫로 레스트시킬 디지몬을 상대의 디지몬으로
        if (Regex.IsMatch(t, @"^\[Your Turn\]\s*\[Once Per Turn\]\s*When your ＜Digisorption＞ effect would suspend one of your Digimon, you may suspend (?:1 of )?your opponent's Digimon with that effect instead\.?$", IC))
            return "【자신의 턴】 [턴에 1회] 자신의 《흡수진화》 효과로 자신의 디지몬을 레스트시킬 때, 그 대신 상대의 디지몬을 레스트시킬 수 있다.";
        // 상대 필드를 보는 색 조건 무시
        var ig = Regex.Match(t, @"^While your opponent has (?:a|an) (?<col>red|blue|yellow|green|black|purple|white) Digimon or Tamer, you (?:can|may) ignore this card's color requirements\.?$", IC);
        if (ig.Success)
        {
            string col = ColorKo52(ig.Groups["col"].Value);
            return $"{col}인 상대의 디지몬/테이머가 있는 동안, 이 카드의 색 조건을 무시할 수 있다.";
        }
        return null;
    }

    private static string ColorKo52(string en) => en.ToLowerInvariant() switch
    {
        "red" => "레드", "blue" => "블루", "yellow" => "옐로", "green" => "그린", "black" => "블랙", "purple" => "퍼플", "white" => "화이트", _ => en,
    };

    /// <summary>사건 (SentenceParser.ParseEventPhrase52)</summary>
    private static string? EventKo52(string ev)
    {
        if (!KoGate52) return null;
        string s = ev.Trim();
        if (Regex.IsMatch(s, @"^an Option card is used or (?:when )?(?:a card is|cards are) added to a security stack$", IC))
            return "옵션 카드를 사용했을 때 또는 시큐리티가 늘어났을 때,";
        if (Regex.IsMatch(s, @"^(?:a card is|cards are) added to (?:a|any) security stack$", IC))
            return "시큐리티가 늘어났을 때,";
        return null;
    }

    /// <summary>비용 (SentenceParser.ParseCost52)</summary>
    private static string? CostKo52(string cost)
    {
        if (!KoGate52) return null;
        string c = cost.Trim();
        Match m;
        if ((m = Regex.Match(c, @"^trashing (?<n>\d+) same-level cards from (?:1|one) of your (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) trait Digimon's digivolution cards$", IC)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value, trait: true);
            return $"특징으로 {x}{J(x, "을", "를")} 가진 자신의 디지몬 1마리의 진화원에서 같은 Lv.의 카드 {m.Groups["n"].Value}장을 파기하는 것으로,";
        }
        if ((m = Regex.Match(c, @"^returning 1 of your opponent's level (?<lv>\d+) Digimon or 1 of your Digimon to (?:the|its owner's|their owners'|your) hands?$", IC)).Success)
            return $"Lv.{m.Groups["lv"].Value}인 상대의 디지몬 1마리 또는 자신의 디지몬 1마리를 패로 되돌리는 것으로,";
        if ((m = Regex.Match(c, @"^placing up to (?<n>\d+) Tamer cards from this Digimon's digivolution cards as your bottom security cards$", IC)).Success)
            return $"이 디지몬의 진화원에서 테이머 카드를 {m.Groups["n"].Value}장까지 시큐리티 아래에 놓는 것으로,";
        if (_costKoDepth52 == 0)
        {
            foreach (Match am in Regex.Matches(c, @" and (?=(?:suspending|trashing|placing|deleting|returning) )", IC))
            {
                string a = c[..am.Index], b = c[(am.Index + am.Length)..];
                _costKoDepth52++;
                string? ka, kb;
                try { ka = CostKo(a); kb = ka == null ? null : CostKo(b); }
                finally { _costKoDepth52--; }
                if (ka == null || kb == null) continue;
                string head = Regex.Replace(ka.TrimEnd(), @"(?:하는|시키는) 것으로,$", m2 => m2.Value.StartsWith("하") ? "하고," : "시키고,");
                if (head == ka.TrimEnd()) continue;
                return $"{head} {kb}";
            }
        }
        return null;
    }
}
