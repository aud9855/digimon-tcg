using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.37.0 미지원 문장 11차 묶음의 생성 한글 — 공식 한글 표기를 따른다:
/// "이 디지몬의 DP 소멸 효과의 상한 +2000" (BT17-008) · "자신이 발휘하는 DP 소멸 효과의 상한 +1000" (BT12-001) · "이 디지몬의 색은 옐로로도 취급한다" (BT3-014) ·
/// "이 디지몬은 「엑자몬」의 조그레스 진화에서 「슬레이어드라몬」・Lv.6으로도 취급한다" (BT20-025) · "이 디지몬이 체크한 옵션 카드의 【시큐리티】 효과는 발휘하지 않는다" (BT7-014) ·
/// "상대는 DP 5000 이하의 디지몬을 등장시킬 수 없다" (EX3-012) · "~ 등장시킬 수 없으며, 이동시킬 수 없다" (EX7-014) · "DP 6000 이하의 상대의 디지몬은 등장할 수 없으며, 이 디지몬을 DP +4000" (BT14-017)
/// </summary>
public static partial class KoreanEffectRenderer
{
    private static string ColorKo37(string en) => en.ToLowerInvariant() switch
    {
        "red" => "레드", "blue" => "블루", "yellow" => "옐로", "green" => "그린", "black" => "블랙", "purple" => "퍼플", "white" => "화이트", _ => en,
    };

    private static string? SentenceKo37(string s, bool may)
    {
        if (SecurityKo37(s, may) is { } secKo) return secKo;   // 5. 시큐리티 (원래 규칙이 읽지 못한 것만)
        s = s.Trim().TrimEnd('.');
        if (s.EndsWith(" ⟦diff-level⟧", StringComparison.Ordinal))   // 공식 P-220 "이 효과로는 같은 레벨의 카드는 등장시킬 수 없다"
            return Sentence((may ? "You may " : "") + s[..^" ⟦diff-level⟧".Length]) is { } playKo ? $"{playKo}. 이 효과로는 같은 레벨의 카드는 등장시킬 수 없다" : null;
        // 공식 BT5-112 "배틀을 진행하지 않고, 이 카드를 코스트를 지불하지 않고 등장시킨다"
        if (!may && Regex.IsMatch(s, @"^play this card without battling and without paying (?:the|its) (?:memory )?costs?$", IC))
            return "배틀을 진행하지 않고, 이 카드를 코스트를 지불하지 않고 등장시킨다";
        // 공식 BT14-102 "자신의 육성 에어리어에 부화시킬 수 있다"
        if (Regex.Match(s.Trim().TrimEnd('.'), @"^(?<may>you may )?hatch (?:a|1) Digi-Egg in your breeding area$", IC) is { Success: true } hatch)
            return $"자신의 육성 에어리어에 {(may || hatch.Groups["may"].Success ? "부화시킬 수 있다" : "부화시킨다")}";
        // "Trash the top or bottom digivolution card of 1 of your opponent's Digimon" (BT27-020 진화원) — "~의 진화원을 위 또는 아래에서부터 1장 파기한다"
        if (!may && Regex.Match(s.Trim().TrimEnd('.'), @"^Trash the top or bottom digivolution card of (?<t>.+)$", IC) is { Success: true } tb && TargetKo(tb.Groups["t"].Value) is { } tbk)
            return $"{tbk}의 진화원을 위 또는 아래에서부터 1장 파기한다";
        if (may) return null;
        s = s.Trim().TrimEnd('.');
        Match m;
        if ((m = Regex.Match(s, @"^add (?<n>\d+) to this Digimon's DP(?:-based)? deletion effects'? maximums?$", IC)).Success)
            return $"이 디지몬의 DP 소멸 효과의 상한 +{m.Groups["n"].Value}";
        if ((m = Regex.Match(s, @"^add (?<n>\d+) to the maximum DP (?:you (?:can|may)|that can be) choose(?:n)? with DP-based deletion effects$", IC)).Success)
            return $"자신이 발휘하는 DP 소멸 효과의 상한 +{m.Groups["n"].Value}";
        if ((m = Regex.Match(s, @"^(?:this Digimon|it) is also treated as (?<c1>red|blue|yellow|green|black|purple|white)(?:(?:,| and| or) (?<c2>red|blue|yellow|green|black|purple|white))?$", IC)).Success)
        {
            string c1 = ColorKo37(m.Groups["c1"].Value);
            string cs = m.Groups["c2"].Success ? $"{c1}/{ColorKo37(m.Groups["c2"].Value)}" : c1;
            return $"이 디지몬의 색은 {cs}{J(cs, "으로", "로")}도 취급한다";
        }
        if (Regex.IsMatch(s, @"^(?:this Digimon|it) is treated as also having the colou?rs? of its digivolution cards$", IC))   // 공식 BT8-084
            return "이 디지몬의 색은 이 디지몬의 진화원의 카드의 색으로도 취급한다";
        if ((m = Regex.Match(s, @"^this Digimon is also treated as Lv\.\s*(?<lv>\d+) \[(?<as>[^\]]+)\] for \[(?<for>[^\]]+)\]'s DNA digivolution$", IC)).Success)
            return $"이 디지몬은 「{NameKo(m.Groups["for"].Value)}」의 조그레스 진화에서 「{NameKo(m.Groups["as"].Value)}」・Lv.{m.Groups["lv"].Value}{J(m.Groups["lv"].Value, "으로", "로")}도 취급한다";
        if ((m = Regex.Match(s, @"^(?:this Digimon|it)(?: with (?:the )?(?<x>\[[^\]]+\](?: or \[[^\]]+\])*) traits?)? doesn't activate \[Security\] effects on option cards it checks$", IC)).Success)
        {
            string who = m.Groups["x"].Success ? $"특징으로 {NamesKo37(m.Groups["x"].Value, true)}{J(LastName37(m.Groups["x"].Value, true), "을", "를")} 가진 이 디지몬" : "이 디지몬";
            return $"{who}이 체크한 옵션 카드의 【시큐리티】 효과는 발휘하지 않는다";
        }
        if (Regex.IsMatch(s, @"^none of your opponent's Digimon can suspend$", IC)) return "상대의 디지몬 전부는 레스트할 수 없다";
        if ((m = Regex.Match(s, @"^none of your opponent's Digimon can activate \[(?<w>When Digivolving|On Play|When Attacking)\] effects$", IC)).Success)
            return $"상대의 디지몬 전부의 {(m.Groups["w"].Value.StartsWith("On", StringComparison.OrdinalIgnoreCase) ? "【등장 시】" : m.Groups["w"].Value.EndsWith("Attacking", StringComparison.OrdinalIgnoreCase) ? "【어택 시】" : "【진화 시】")} 효과는 발휘하지 않는다";
        if ((m = Regex.Match(s, @"^(?:they|your opponent) can't play Digimon with (?<n>\d+) DP or less,? and this Digimon gets (?<v>[+-]\d+) DP$", IC)).Success)
            return $"DP {m.Groups["n"].Value} 이하의 상대의 디지몬은 등장할 수 없으며, 이 디지몬을 DP {m.Groups["v"].Value}";
        if ((m = Regex.Match(s, @"^(?:they|your opponent) can't play Digimon with (?<n>\d+) DP or less$", IC)).Success)
            return $"DP {m.Groups["n"].Value} 이하의 상대의 디지몬은 등장할 수 없다";
        // 기간: "Your opponent can't play (or move) Digimon with 5000 DP or less until the end of their turn"
        var sd = SplitDuration(s);
        if (sd.Ko != null && (m = Regex.Match(sd.Core.Trim(), @"^(?:your opponent|they) can't play (?<move>or move )?Digimon with (?<n>\d+) DP or less$", IC)).Success)
            return $"{sd.Ko}, 상대는 DP {m.Groups["n"].Value} 이하의 디지몬을 등장시킬 수 없{(m.Groups["move"].Success ? "으며, 이동시킬 수 없" : "")}다";
        return null;
    }

    private static string NamesKo37(string names, bool trait) =>
        string.Join("/", Regex.Matches(names, @"\[([^\]]+)\]").Select(x => $"「{(trait ? TraitKo(x.Groups[1].Value) : NameKo(x.Groups[1].Value))}」").Distinct());

    private static string LastName37(string names, bool trait) =>
        Regex.Matches(names, @"\[([^\]]+)\]").Select(x => trait ? TraitKo(x.Groups[1].Value) : NameKo(x.Groups[1].Value)).LastOrDefault() ?? "";
}

public static partial class KoreanEffectRenderer
{
    /// <summary>v0.37.0 조건: "this Digimon has 10000 DP or more" (공식 BT17-079 "DP 10000 이상의 이 디지몬은")</summary>
    private static Cond? CondKo37(string cond)
    {
        var m = Regex.Match(cond.Trim(), @"^(?:this Digimon|it) has (?<n>\d+) DP or (?<d>more|less)$", IC);
        if (m.Success) return Be($"이 디지몬의 DP가 {m.Groups["n"].Value} {(m.Groups["d"].Value.Equals("more", StringComparison.OrdinalIgnoreCase) ? "이상" : "이하")}");
        return null;
    }
}

public static partial class KoreanEffectRenderer
{
    /// <summary>
    /// v0.37.0 "place this card as the bottom digivolution card of 1 of your ~" — 공식 EX7-070 "그 후, 이 카드를 특징으로 「3총사」를 가진 자신의 디지몬의 진화원 아래에 놓는다" ·
    /// BT9-029 "이 카드를 그 디지몬 1마리의 진화원 아래에 놓는다" (다른 규칙이 "this card"를 이 디지몬으로 읽기 전에)
    /// </summary>
    private static string? PlaceThisCardKo37(string s, bool may)
    {
        var m = Regex.Match(s.Trim().TrimEnd('.'), @"^(?:Then, )?place this card(?: from your hand)? (?:as the bottom digivolution card of (?<t>.+)|as (?<t>.+?)'s bottom digivolution card|under (?<t>.+?) as its bottom digivolution card)$", IC);
        if (!m.Success) return null;
        string t0 = Regex.Replace(m.Groups["t"].Value, @"^any of your ", "1 of your ", IC);
        string t = Regex.Replace(t0, @"^(?:(?:1|one|any) of )?your (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) trait Digimon$", "1 of your Digimon with the ${x} trait", IC);
        if (TargetKo(t) is not { } tk) return null;
        return $"이 카드를 {tk}의 진화원 아래에 {(may ? "놓을 수 있다" : "놓는다")}";
    }
}

public static partial class KoreanEffectRenderer
{
    /// <summary>
    /// v0.37.0 시큐리티 — 공식 BT10-101 "상대의 디지몬 1마리를 상대의 시큐리티 위에 뒷면으로 놓는다" · ST10-14 "~ 상대의 시큐리티의 위 또는 아래에 뒤집어서 놓는다" ·
    /// BT14-038 "이 카드를 시큐리티 아래에 놓는다" · BT7-098 "이 턴 동안 상대 디지몬 1마리와 상대의 시큐리티 디지몬 전부의 DP를 -3000 한다"
    /// </summary>
    private static string? SecurityKo37(string s, bool may)
    {
        s = s.Trim().TrimEnd('.');
        Match m;
        if ((m = Regex.Match(s, @"^(?<may>you may )?Place (?<t>(?:\d+|all) of (?:your|your opponent's|their) .+?)(?: face down)? (?:(?:on|at the) (?<pos>top or bottom|top|bottom) of (?<who>your|your opponent's|its owner's|their owners'?|their) security stacks?|as (?<who>the|their|your|its owner's) (?<pos>top or bottom|top|bottom) security cards?)(?: face down)?$", IC)).Success)
        {
            string tRaw = Regex.Replace(m.Groups["t"].Value, @"^(?<n>\d+) of their\b", "${n} of your opponent's", IC);
            if (TargetKo(tRaw) is not { } tk) return null;
            bool enemy = tRaw.Contains("opponent", StringComparison.OrdinalIgnoreCase);
            string pos = m.Groups["pos"].Value.ToLowerInvariant();
            string posKo = pos == "top" ? "위" : pos == "bottom" ? "아래" : "위 또는 아래";
            string who = m.Groups["who"].Value.ToLowerInvariant();
            string whoKo = who.StartsWith("its") ? "주인의" : enemy ? "상대의" : "자신의";
            bool mayPlace = may || m.Groups["may"].Success;
            return $"{tk}{J(tk, "을", "를")} {whoKo} 시큐리티 {posKo}에 뒷면으로 {(mayPlace ? "놓을 수 있다" : "놓는다")}";
        }
        if (!may && (m = Regex.Match(s, @"^Place this card (?:(?:on|at the) (?<pos>top|bottom) of your security stack|as (?:the|your) (?<pos>top|bottom) security card)(?: face down)?$", IC)).Success)
            return $"이 카드를 시큐리티 {(m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase) ? "위" : "아래")}에 놓는다";
        if (!may && (m = Regex.Match(s, @"^(?:(?<a>.+?) and )?all of (?:your opponent's|their) Security Digimon get (?<dp>[+-]\d+) DP (?<dur>for the turn|until the end of (?:their|your opponent's) turn|until (?:their|your opponent's) turn ends)$", IC)).Success)
        {
            string durKo = m.Groups["dur"].Value.StartsWith("for", StringComparison.OrdinalIgnoreCase) ? "이 턴 동안" : "상대의 턴 종료 시까지";
            string? aKo = null;
            if (m.Groups["a"].Success && TargetKo(m.Groups["a"].Value) is not { } ak) return null;
            else if (m.Groups["a"].Success) aKo = TargetKo(m.Groups["a"].Value);
            string who = aKo != null ? $"{aKo}{J(aKo, "과", "와")} 상대의 시큐리티 디지몬 전부" : "상대의 시큐리티 디지몬 전부";
            return $"{durKo}, {who}의 DP를 {m.Groups["dp"].Value} 한다";
        }
        return null;
    }
}
