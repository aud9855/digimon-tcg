using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.38.0 미지원 문장 12차 묶음의 생성 한글 — 공식 표기를 따른다:
/// "놓은 카드와 같은 Lv.의 상대의 디지몬 전부" (BT18-042) · "소멸한 디지몬의 Lv. 이하의 액티브 상태인 상대의 디지몬 1마리" (BT11-076) ·
/// "파기한 카드의 Lv. 이하의" (BT13-109) · "등장한 디지몬의 등장 코스트 이하의" (BT10-106) · "이 효과로 등장한 디지몬의 DP 이하의" (BT6-110) ·
/// "자신의 시큐리티 매수 이하의 Lv.의" (EX6-028) · "이 디지몬의 진화원의 매수 이하의 Lv.의" (RB1-031)
/// </summary>
public static partial class KoreanEffectRenderer
{
    private static string RefKo38(string w)
    {
        string x = w.ToLowerInvariant();
        if (x.Contains("played with this effect") || x.Contains("played by this effect")) return "이 효과로 등장한 디지몬";
        if (x.Contains("played")) return "등장한 디지몬";
        if (x.Contains("placed")) return "놓은 카드";
        if (x.Contains("returned card")) return "되돌린 카드";
        if (x.Contains("returned")) return "되돌린 디지몬";
        if (x.Contains("trashed")) return "파기한 카드";
        if (x.Contains("deleted")) return "소멸한 디지몬";
        if (x.Contains("chosen")) return "선택한 디지몬";
        if (x == "that card") return "그 카드";
        return "그 디지몬";
    }

    private static (Match M, string Ko)? FilterKo38(string rest)
    {
        Match m;
        string tail = @"(?:'s (?:level|play cost|DP))?";
        string rw = TargetSpec.RefWords38;
        if ((m = Regex.Match(rest, $@"^(?:with |and )?(?:the same level as|a level equal to) {rw}{tail}", IC)).Success)
        { string r = RefKo38(m.Groups["r"].Value); return (m, $"{r}{J(r, "과", "와")} 같은 Lv.의"); }
        if ((m = Regex.Match(rest, $@"^(?:and )?{TargetSpec.LevelLe38} {rw}{tail}", IC)).Success)
            return (m, $"{RefKo38(m.Groups["r"].Value)}의 Lv. 이하의");
        if ((m = Regex.Match(rest, $@"^(?:and )?{TargetSpec.CostLe38} {rw}{tail}", IC)).Success)
            return (m, $"{RefKo38(m.Groups["r"].Value)}의 등장 코스트 이하의");
        if ((m = Regex.Match(rest, $@"^(?:and )?{TargetSpec.DpLe38} {rw}{tail}", IC)).Success)
            return (m, $"{RefKo38(m.Groups["r"].Value)}의 DP 이하의");
        if ((m = Regex.Match(rest, @"^(?:with |and |or )?＜(?<k>[^＞]+?)\s*＞ in (?:its|their) texts?", IC)).Success && KeywordKo(m.Groups["k"].Value, false) is { } kk)
            return (m, $"{kk}{J(kk, "이", "가")} 기술되어 있는");
        if ((m = Regex.Match(rest, @"^(?:with |and )?(?:as many or more|equal or more) digivolution cards (?:as|than) (?<w>that Digimon|the chosen Digimon|it\b)", IC)).Success)
            return (m, $"진화원 매수가 {(m.Groups["w"].Value.Contains("chosen", StringComparison.OrdinalIgnoreCase) ? "선택한 디지몬" : "그 디지몬")} 이상인");
        if ((m = Regex.Match(rest, $@"^(?:and )?{TargetSpec.LevelLe38} the number of cards in (?<src>your security stack|your or (?:their|your opponent's) security stack|this Digimon's digivolution cards)", IC)).Success)
        {
            string src = m.Groups["src"].Value.ToLowerInvariant();
            string ko = src.StartsWith("this") ? "이 디지몬의 진화원의 매수" : src.Contains(" or ") ? "자신/상대의 시큐리티의 매수" : "자신의 시큐리티 매수";
            return (m, $"{ko} 이하의 Lv.의");
        }
        return null;
    }
}

public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _fallbackDepth38;

    /// <summary>v0.38.0 원래 규칙이 읽지 못한 문장만 다른 표기로 (SentenceParser.StepFallback38과 같은 읽기)</summary>
    private static string? SentenceFallback38(string s, bool nested)
    {
        if (_fallbackDepth38 >= 3) return null;
        _fallbackDepth38++;
        try
        {
            s = s.Trim().TrimEnd('.').Trim();
            if (Regex.IsMatch(s, @"^Then,?\s", IC)) return null;   // SentenceCore가 뒤를 다시 읽는다
            foreach (var alt in SentenceParser.Rewrites38(s))
                if (alt != s && SentenceCore(alt, nested) is { } ko) return ko;
            if (SentenceKo38b(s) is { } b38) return b38;
            var may38 = Regex.Match(s, @"^you may (?<r>.+)$", IC);
            if (SentenceKo38c(may38.Success ? Cap(may38.Groups["r"].Value) : s, may38.Success) is { } c38) return c38;
            if (TamerUnsuspendKo38(s) is { } tko) return tko;
            if (ThisMayUnsuspendKo38(s) is { } tm) return tm;
            if (DeleteChosenAndKo38(s) is { } dc) return dc;
            if (ChooseAndKo38(s) is { } ca) return ca;
            if (SplitKo38(s) is { } split) return split;
            return null;
        }
        finally { _fallbackDepth38--; }
    }

    /// <summary>"~한다" → "~하고," · "~놓는다" → "~놓고," (공식 BT11-017 "이 디지몬을 액티브로 하고, ~")</summary>
    private static string Conj38(string ko)
    {
        ko = ko.TrimEnd('.', ' ');
        if (ko.EndsWith("는다", StringComparison.Ordinal)) return ko[..^2] + "고,";
        if (!ko.EndsWith("다", StringComparison.Ordinal) || ko.Length < 2) return ko + ",";
        char prev = ko[^2];
        if (prev >= '가' && prev <= '힣' && (prev - '가') % 28 == 4)   // 받침 ㄴ (한다 → 하고, 시킨다 → 시키고)
            return ko[..^2] + (char)(prev - 4) + "고,";
        return ko[..^1] + "고,";
    }

    private static string? SplitKo38(string s)
    {
        foreach (Match sep in Regex.Matches(s, @",? and then |, and |, then | and (?=(?:it|they|this Digimon|that Digimon) (?:gets?|gains?|can|unsuspends?|isn't|doesn't)\b|(?:unsuspends?|gain|gains|draw|delete|suspend|return|trash)\b)", IC))
        {
            string left = s[..sep.Index].Trim(), right = s[(sep.Index + sep.Length)..].Trim();
            if (left.Length == 0 || right.Length == 0) continue;
            if (!Regex.IsMatch(left, @"^(?:Unsuspend|Suspend|Delete|Return|Trash|Gain|＜Draw|This Digimon|It|1 of your Digimon)\b", IC)) continue;
            if (Sentence(left, nested: true) is not { } a) continue;
            string r = Cap(right);
            r = Regex.Replace(r, @"^Unsuspends\b", "Unsuspend this Digimon", IC);
            if (Sentence(r, nested: true) is not { } b) continue;
            return $"{Conj38(a)} {b}";
        }
        return null;
    }

    private static string? TamerUnsuspendKo38(string s)
    {
        const string Col = @"(?:red|blue|yellow|green|black|purple|white)";
        var m = Regex.Match(s, $@"^Unsuspend (?:(?<dig>this Digimon|1 of your Digimon) (?<conj>or|and) )?(?:(?:1|one) of your (?:(?<col>{Col}) )?Tamers|(?<bare>Tamer)|(?:1|one) of your (?<nm>\[[^\]]+\]))$", IC);
        if (!m.Success) return null;
        bool or = m.Groups["conj"].Value.Equals("or", StringComparison.OrdinalIgnoreCase);
        if (m.Groups["bare"].Success)
            return or && m.Groups["dig"].Value.Equals("this Digimon", StringComparison.OrdinalIgnoreCase) ? "이 디지몬/테이머를 액티브로 한다" : null;
        string tamer = m.Groups["nm"].Success ? $"자신의 {NamesKo37(m.Groups["nm"].Value, false)} 1명"
            : $"{(m.Groups["col"].Success ? ColorKo37(m.Groups["col"].Value) + "인 " : "")}자신의 테이머 1명";
        if (!m.Groups["dig"].Success) return $"{tamer}{J(tamer, "을", "를")} 액티브로 한다";
        string dig = m.Groups["dig"].Value.StartsWith("this", StringComparison.OrdinalIgnoreCase) ? "이 디지몬" : "자신의 디지몬 1마리";
        return or ? $"{dig} 또는 {tamer}{J(tamer, "을", "를")} 액티브로 한다" : $"{dig}{J(dig, "과", "와")} {tamer}{J(tamer, "을", "를")} 액티브로 한다";
    }

    private static string? ThisMayUnsuspendKo38(string s)
    {
        var m = Regex.Match(s, @"^(?:(?<may>you may )unsuspend this (?<adj>[^,]*?)Digimon(?<f> with .+)?|this (?<adj>[^,]*?)Digimon(?<f> with .+?)? (?<may>may )?unsuspends?|this (?<nm>\[[^\]]+\]) (?<may>may )?unsuspends?)$", IC);
        if (!m.Success) return null;
        string verb = m.Groups["may"].Success ? "액티브로 할 수 있다" : "액티브로 한다";
        if (m.Groups["nm"].Success) return $"{NamesKo37(m.Groups["nm"].Value, false)}인 이 디지몬을 {verb}";
        if (m.Groups["f"].Value.Trim().Length == 0 && m.Groups["adj"].Value.Trim().Length == 0) return null;
        if (TargetKo($"all of your {m.Groups["adj"].Value}Digimon {m.Groups["f"].Value.Trim()}".Trim()) is not { } tk) return null;
        string pre = Regex.Replace(tk, @"\s*자신의 디지몬 전부$", "");
        return $"{pre} 이 디지몬을 {verb}";
    }

    private static string? DeleteChosenAndKo38(string s)
    {
        var m = Regex.Match(s, @"^Delete (?<w>that Digimon|(?:1|one) of your chosen Digimon|the chosen Digimon) and (?<t>(?:1|one) of your opponent's .+)$", IC);
        if (!m.Success || TargetKo(m.Groups["t"].Value) is not { } tk) return null;
        string own = m.Groups["w"].Value.StartsWith("that", StringComparison.OrdinalIgnoreCase) ? "그 디지몬" : "선택한 자신의 디지몬";
        return $"{tk}{J(tk, "과", "와")} {own}{J(own, "을", "를")} 소멸시킨다";   // 공식 BT12-108 "~ 상대의 디지몬 1마리와 선택한 자신의 디지몬을 소멸시킨다"
    }

    private static string? ChooseAndKo38(string s)
    {
        var m = Regex.Match(s, @"^Choose (?<t>(?:1|one) of (?:your|your opponent's) [^,]+?) and (?<b>(?:return|delete|suspend|place)\b.+)$", IC);
        if (!m.Success || TargetKo(m.Groups["t"].Value) is not { } tk || Sentence(Cap(m.Groups["b"].Value), nested: true) is not { } b) return null;
        return $"{tk}{J(tk, "을", "를")} 선택하고, {b}";
    }
}

public static partial class KoreanEffectRenderer
{
    /// <summary>공식 BT14-091 "진화원 매수가 선택한 디지몬 이상의 상대의 디지몬이 없다면" · EX4-071 "이 효과로 명칭에 「레이브몬」을 포함하는 자신의 디지몬이 소멸하고 있었다면"</summary>
    private static Cond? CondKo38(string cond)
    {
        Match m;
        cond = cond.Trim();
        if (Regex.IsMatch(cond, @"^this effect placed$", IC)) return new Cond("이 효과로 놓았을 때,", "이 효과로 놓은 동안,", "이 효과로 놓았다면,");
        if ((m = Regex.Match(cond, @"^your opponent has no Digimon (?<f>with .+)$", IC)).Success && TargetKo("all of your opponent's Digimon " + m.Groups["f"].Value, bare: true) is { } tk)
            return NotHave($"{tk}이");
        if ((m = Regex.Match(cond, @"^(?:one|1) of your (?<t>Digimon .+?) was deleted by this effect$", IC)).Success && TargetKo("all of your " + m.Groups["t"].Value, bare: true) is { } ok)
            return new Cond($"이 효과로 {ok}이 소멸하고 있었을 때,", $"이 효과로 {ok}이 소멸하고 있는 동안,", $"이 효과로 {ok}이 소멸하고 있었다면,");
        return null;
    }

    /// <summary>공식 BT7-016 "이 디지몬의 진화원의 특징으로 「하이브리드체」를 갖는 카드 1장마다"</summary>
    private static string? CounterKo38(string counter)
    {
        var m = Regex.Match(counter.Trim(), @"^digivolution cards? this Digimon has (?<f>with .+)$", IC);
        if (m.Success && PickKo("1 card " + m.Groups["f"].Value) is { } pk) return $"이 디지몬의 진화원의 {pk}마다";
        return null;
    }

    /// <summary>
    /// 공식 BT16-077 "턴 종료까지 자신의 디지몬 1마리는 《속공》을 얻고, 그 디지몬으로 플레이어에게 어택할 수 있다" · BT20-102 "~ 그 디지몬으로 레스트시키지 않고 어택할 수 있다" ·
    /// BT20-019 "~ 자신의 디지몬 전부는 《관통》을 얻고, 액티브 상태인 상대의 디지몬에게도 어택할 수 있다" · BT10-069 "테이머 1명을 소멸시키고"
    /// </summary>
    private static string? SentenceKo38b(string s)
    {
        Match m;
        if ((m = Regex.Match(s, @"^(?<t>.+?) (?:may gain|gains?) (?<k>＜[^＞]+＞(?:\s*(?:,|and)\s*＜[^＞]+＞)*) for the turn and (?:that Digimon |it )?(?<atk>attacks?|attack)(?<rest>(?: a player| your opponent's Digimon)?(?: without suspending)?)$", IC)).Success
            && Sentence($"{m.Groups["t"].Value} gains {m.Groups["k"].Value} for the turn", nested: true) is { } a)
        {
            string rest = m.Groups["rest"].Value.ToLowerInvariant();
            string how = rest.Contains("player") ? "플레이어에게 " : rest.Contains("digimon") ? "상대의 디지몬에게 " : "";
            if (rest.Contains("without suspending")) how += "레스트시키지 않고 ";
            bool must = m.Groups["atk"].Value.Equals("attacks", StringComparison.OrdinalIgnoreCase);   // 공식 EX7-013 "그 디지몬으로 어택한다"
            return $"{Conj38(a)} 그 디지몬으로 {how}{(must ? "어택한다" : "어택할 수 있다")}";
        }
        if (Regex.IsMatch(s, @"^Delete (?:1|one) Tamer$", IC)) return "테이머 1명을 소멸시킨다";
        if ((m = Regex.Match(s, @"^(?<pre>(?:While [^,]+, )?)(?<who>all of your (?:[^,]*? )?Digimon(?: with [^,]+?)?) gain (?<kws>＜[^＞]+＞(?:\s*(?:,|and)\s*＜[^＞]+＞)*) and can also attack (?:your opponent's )?unsuspended Digimon$", IC)).Success
            && Sentence($"{m.Groups["pre"].Value}{m.Groups["who"].Value} gain {m.Groups["kws"].Value}", nested: false) is { } g)
            return $"{Conj38(g)} 액티브 상태인 상대의 디지몬에게도 어택할 수 있다";
        return null;
    }
}
