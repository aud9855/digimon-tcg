using System.Collections.Generic;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.39.0 미지원 문장 13차 묶음의 생성 한글 — 원래 규칙(12차까지)이 읽지 못한 문장만 (SentenceParser.Batch39와 같은 읽기)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _fallbackDepth39;
    /// <summary>13차 동작 읽기(Core39)를 켠다 — 원래 규칙(12차까지)으로 문장 전체가 읽히지 않을 때만 다시 읽으면서 (앞의 다른 읽기를 가로채지 않게)</summary>
    [ThreadStatic] private static bool _core39;

    private static string? SentenceFallback39(string s, bool nested)
    {
        if (_fallbackDepth39 >= 3) return null;
        _fallbackDepth39++;
        try
        {
            s = s.Trim().TrimEnd('.').Trim();
            if (Regex.IsMatch(s, @"^Then,?\s", IC)) return null;
            foreach (var alt in SentenceParser.Rewrites39(s))
                if (alt != s && Sentence(alt, nested) is { } ko) return ko;
            // "1 of your Digimon may get +5000 DP for the turn and attack your opponent's Digimon" (BT24-051)
            if (SentenceParser.GetAndAttackRe39.Match(s) is { Success: true } ga && Sentence($"{ga.Groups["t"].Value} gets {ga.Groups["dp"].Value} for the turn", nested: true) is { } gaKo)
            {
                string rest = ga.Groups["rest"].Value.ToLowerInvariant();
                string how = (rest.Contains("player") ? "플레이어에게 " : rest.Contains("digimon") ? "상대의 디지몬에게 " : "") + (rest.Contains("without suspending") ? "레스트시키지 않고 " : "");
                return $"{Conj38(gaKo)} 그 디지몬으로 {how}{(ga.Groups["may"].Success ? "어택할 수 있다" : "어택한다")}";
            }
            if (MaxAdjKo39(s) is { } maxKo) return maxKo;
            if (AtTurnEndKo39(s) is { } ateKo) return ateKo;
            if (AtTurnEndActKo39(s) is { } ataKo) return ataKo;
            // [시큐리티] "At the end of the battle, ~" — 공식 P-066 "배틀 종료 시, ~"
            if (Regex.Match(s, @"^At the end of (?:the )?battle, (?<x>.+)$", IC) is { Success: true } eob && Sentence(Cap(eob.Groups["x"].Value), nested: true) is { } eobKo)
                return "배틀 종료 시, " + eobKo;
            // 강제 어택: "that Digimon attacks" → 공식 BT21-081 "그 디지몬으로 어택한다" · BT19-062 "이 디지몬으로 플레이어에게 어택한다"
            var atk = Regex.Match(s, @"^(?!When\b|If\b|While\b)(?<t>.+?) attacks(?<rest>(?: a player| your opponent's Digimon| an opponent's Digimon)?(?: without suspending)?)$", IC);
            if (atk.Success && !atk.Groups["t"].Value.Contains(" may ", StringComparison.OrdinalIgnoreCase)
                && SentenceCore($"{atk.Groups["t"].Value} attack{atk.Groups["rest"].Value}", nested) is { } atkKo) return atkKo;
            if (!_core39)
            {
                _core39 = true;
                try { return SentenceCore(s, nested) ?? SentenceFallback38(s, nested); }
                finally { _core39 = false; }
            }
            return null;
        }
        finally { _fallbackDepth39--; }
    }
}

public static partial class KoreanEffectRenderer
{
    /// <summary>v0.39.0 대상 꾸밈말: "without [Doomsday Clock] in its digivolution cards" → 공식 BT17-100 "진화원에 「종말의 시계」가 없는"</summary>
    private static (Match M, string Ko)? FilterKo39(string rest)
    {
        Match m;
        if ((m = Regex.Match(rest, $@"^without (?<x>{TargetSpec.NameList}) in (?:its|their) digivolution cards", IC)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return (m, $"진화원에 {x}{J(x, "이", "가")} 없는"); }
        if ((m = Regex.Match(rest, $@"^(?:,\s*)?without (?<x>{TargetSpec.NameList}) in (?:its|their) names?", IC)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return (m, $"{x} 이외의"); }
        return null;
    }

    /// <summary>v0.39.0 원래 규칙이 읽지 못한 대상만 (TargetSpec.Parse39와 같은 읽기)</summary>
    private static string? TargetKo39(string phrase, bool bare)
    {
        phrase = phrase.Trim().TrimEnd('.').Trim();
        if (!Regex.IsMatch(phrase, @"\bwithout \[|\bother than \[", IC)) return null;
        string norm = TargetSpec.NormalizeOtherThan39(phrase);
        if (norm != phrase && TargetKoCore(norm, bare) is { } n) return n;
        var m = Regex.Match(phrase, $@"^(?<pre>(?:\d+|all|up to \d+) of (?:your|your opponent's|their) (?:other )?)(?<x>{TargetSpec.NameList})s?(?<rest> (?:without|with) .+)$", IC);
        if (m.Success && TargetKoCore(m.Groups["pre"].Value + "Digimon" + m.Groups["rest"].Value, bare) is { } ko)
            return Regex.Replace(ko, @"(자신|상대)의 (다른 )?디지몬", $"$1의 $2{NamesKo(m.Groups["x"].Value)}");
        return null;
    }
}

public static partial class KoreanEffectRenderer
{
    /// <summary>v0.39.0 원래 규칙이 읽지 못한 동작만 (Core의 마지막)</summary>
    private static Phrase? Core39(string s)
    {
        if (PlayFromZonesKo39(s) is { } pz) return pz;
        if (SecurityKo39(s) is { } sk) return sk;
        if (UnderTamerEvoKo39(s) is { } ute) return ute;
        if (MoreKo39(s) is { } more) return more;
        if (DigimonOrTamerEvoKo39(s) is { } dte) return dte;
        return null;
    }

    /// <summary>
    /// 공식: EX3-022 "블루인 자신의 디지몬 1마리의 진화원에서 블루인 Lv.3의 디지몬 카드 1장을 코스트를 지불하지 않고 등장시킬 수 있다" ·
    /// BT17-025 "자신의 트래시 또는 디지몬의 진화원에서 ~" · BT18-017 "이 디지몬의 진화원에서 ~ 1장을 패로 되돌리거나, 코스트를 지불하지 않고 등장시킬 수 있다" ·
    /// BT20-094 "자신의 「황제드라몬: 파이터 모드」의 진화원에서 「황제드라몬: 드래곤 모드」 1장을" · BT7-027 "~ 별개의 디지몬으로서 등장시킬 수 있다"
    /// </summary>
    private static Phrase? PlayFromZonesKo39(string s)
    {
        var m = SentenceParser.PlayFromZonesRe39.Match(s);
        if (!m.Success || SentenceParser.SplitMid39(m.Groups["mid"].Value) is not { } mid) return null;
        var zones = mid.Zones;
        var parts = SentenceParser.SplitPicks39(mid.Pick);
        var zk = new List<string>();
        foreach (var (z, zm) in zones)
        {
            string ko = z.Kind switch
            {
                "hand" => "자신의 패",
                "trash" => "자신의 트래시",
                "under" => "자신의 테이머 아래",
                "self" => "이 디지몬의 진화원",
                "that" => "그 디지몬의 진화원",
                _ => zm.Groups["x"].Success ? $"자신의 {NamesKo(zm.Groups["x"].Value)}의 진화원"
                    : $"{(zm.Groups["c"].Value.Trim().Length > 0 ? ColorsKo(zm.Groups["c"].Value.Trim()) + "인 " : "")}자신의 디지몬{(z.AnyHost ? "" : " 1마리")}의 진화원",
            };
            if (zk.Count > 0 && ko.StartsWith("자신의 ") && zk[^1].StartsWith("자신의 ")) ko = ko["자신의 ".Length..];
            zk.Add(ko);
        }
        var pk = new List<string>();
        foreach (var part in parts)
        {
            string pt = SentenceParser.NormalizePick39(part);
            if ((PickKo(pt) ?? (Regex.IsMatch(pt, @"^\d+ \[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*$", IC) ? $"{NamesKo(Regex.Replace(pt, @"^\d+ ", ""))} {Regex.Match(pt, @"^\d+").Value}장" : null)) is not { } k) return null;
            pk.Add(k);
        }
        string picks = pk.Count == 1 ? pk[0] : string.Join(" ", pk.Select((k, i) => i < pk.Count - 1 ? k + J(k, "과", "와") : k));
        string v = m.Groups["v"].Value.ToLowerInvariant();
        string sus = mid.Suspended ? "레스트 상태로 " : "";
        string asOther = m.Groups["as"].Success ? (m.Groups["as"].Value.Contains("Tamer", StringComparison.OrdinalIgnoreCase) ? "테이머 또는 별개의 디지몬으로서 " : "별개의 디지몬으로서 ") : "";
        string cost = m.Groups["red"].Success ? $"지불하는 코스트 -{m.Groups["red"].Value} 하여 " : "코스트를 지불하지 않고 ";
        string head = $"{string.Join(" 또는 ", zk)}에서 {picks}{J(picks, "을", "를")} ";
        if (v.StartsWith("return"))
            return new(head + "패로 되돌리거나, " + cost, new Verb("등장시킨다", "등장시킬 수 있다", "등장시키는 것으로", "등장시키고"));
        if (v == "play or use")
            return new(head + cost + sus, new Verb("등장시키거나 사용한다", "등장시키거나 사용할 수 있다", "등장시키거나 사용하는 것으로", "등장시키거나 사용하고"));
        if (v == "use")
            return new(head + cost, Ha("사용"));
        return new(head + cost + sus + asOther, Siki("등장"));
    }
}

public static partial class KoreanEffectRenderer
{
    /// <summary>
    /// 공식: BT16-036 "서로의 시큐리티를 위에서부터 1장 파기한다" · BT21-098 "상대의 시큐리티가 1장이 되도록 위에서부터 파기한다" ·
    /// BT6-033 "자신의 시큐리티를 3장이 될 때까지 위에서부터 파기하는 것으로, 파기한 시큐리티 1장당 메모리를 +1 한다" · BT21-024 "상대는 본인의 패 1장을 시큐리티 아래에 놓는다"
    /// </summary>
    private static Phrase? SecurityKo39(string s)
    {
        Match m;
        if ((m = Regex.Match(s, @"^Trash the top (?:card|(?<n>\d+) cards) of both players' security stacks?$", IC)).Success)
            return new($"서로의 시큐리티를 위에서부터 {(m.Groups["n"].Success ? m.Groups["n"].Value : "1")}장 ", Ha("파기"));
        if ((m = Regex.Match(s, @"^Trash (?:the top cards of|cards from the top of) (?<w>your opponent's|their|your|both players') security stacks? (?:so that (?:it has|they have|there (?:is|are)) (?<n>\d+) cards? left|until there (?:is|are) (?<n>\d+) left)(?<mem>; gain 1 memory for each (?:security )?card trashed)?$", IC)).Success)
        {
            string w = m.Groups["w"].Value.ToLowerInvariant();
            string who = w == "your" ? "자신" : w == "both players'" ? "서로" : "상대";
            if (m.Groups["mem"].Success)
                return new($"{who}의 시큐리티를 {m.Groups["n"].Value}장이 될 때까지 위에서부터 파기하고, 파기한 시큐리티 1장당 메모리를 +1 ", Ha());
            return new($"{who}의 시큐리티가 {m.Groups["n"].Value}장이 되도록 위에서부터 ", Ha("파기"));
        }
        if ((m = Regex.Match(s, @"^(?:Your opponent places|They place|they place) (?<n>\d+) cards? from their hand (?:as the bottom security cards?|at the bottom of their security stack)$", IC)).Success)
            return new($"상대는 본인의 패 {m.Groups["n"].Value}장을 시큐리티 아래에 ", Notda);
        if ((m = Regex.Match(s, @"^Trash any (?<n>\d+) of (?:your opponent's|their) security cards$", IC)).Success)
            return new($"상대의 시큐리티 중 {m.Groups["n"].Value}장을 ", Ha("파기"));
        return null;
    }
}

public static partial class KoreanEffectRenderer
{
    /// <summary>v0.39.0 사건: "one of your [Galacticmon] attacks" (BT21-098 — 공식 "자신의 「라그나몬」이 어택했을 때,")</summary>
    /// <summary>
    /// v0.39.0 원래 규칙이 "자신의 ~"를 빠뜨린 공격 사건 ("디지몬이 어택했을 때,")만 고친다: "any of your Digimon attack" (BT18-092 — 공식 "자신의 디지몬이 어택했을 때,") ·
    /// "any of your Digimon with [Aqua] or [Sea Animal] in any of its traits attack" (BT19-082). "When a Digimon attacks"(양쪽)는 그대로
    /// </summary>
    private static string? FixEventKo39(string ev, string? core)
    {
        if (core != "디지몬이 어택했을 때,") return core;
        var m = Regex.Match(ev.Trim(), @"^(?:When )?(?:one|any) of your (?<spec>Digimon(?: with .+?)?) attacks?$", IC);
        if (!m.Success) return core;
        return TargetKo("all of your " + m.Groups["spec"].Value, bare: true) is { } t ? $"{t}{J(t, "이", "가")} 어택했을 때," : core;
    }

    private static string? EventKo39(string ev)
    {
        var m = Regex.Match(ev.Trim(), @"^(?:When )?(?:one|any|1) of your (?<x>\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*)s? attacks?$", IC);
        if (m.Success) { string x = NamesKo(m.Groups["x"].Value); return $"자신의 {x}{J(x, "이", "가")} 어택했을 때,"; }
        return null;
    }
}

public static partial class KoreanEffectRenderer
{
    /// <summary>
    /// v0.39.0 상한 바꾸기 — 공식 BT15-089 "상대의 시큐리티 1장마다 이 DP 소멸 효과의 상한 -2000" · EX8-074 "레스트 상태인 다른 디지몬 1마리마다 이 DP 소멸 효과의 상한 +3000"
    /// </summary>
    private static string? MaxAdjKo39(string s)
    {
        if (SentenceParser.ParseMaxAdj39(s) is not { } adj || CounterKo(adj.Counter) is not { } cnt) return null;
        bool dpDeletion = Regex.IsMatch(s, @"\bDP[- ]?(?:Based )?(?:deletion|delete) effect", IC);
        string cap = adj.What == "level" ? "이 효과의 Lv. 상한" : adj.What == "play cost" ? "이 효과의 등장 코스트 상한" : dpDeletion ? "이 DP 소멸 효과의 상한" : "이 효과의 DP 상한";
        return $"{cnt.TrimEnd(',')} {cap} {(adj.Per >= 0 ? "+" : "-")}{Math.Abs(adj.Per)}";
    }

    /// <summary>v0.39.0 셈 — 공식 EX7-066 "명칭이 서로 다른 특징으로 「3총사」를 가진 자신의 디지몬 1마리마다" · BT23-014 "상대의 디지몬과 테이머 1마리(명)마다"</summary>
    private static string? CounterKo39(string counter)
    {
        string s = counter.Trim().TrimEnd('.');
        Match m;
        if ((m = Regex.Match(s, @"^(?:of )?your (?<t>Digimon .+?) with different names$", IC)).Success && TargetKo("all of your " + m.Groups["t"].Value, bare: true) is { } t)
            return $"명칭이 서로 다른 {t} 1마리마다";
        if (Regex.IsMatch(s, @"^of (?:your opponent's|their) security cards$", IC)) return "상대의 시큐리티 1장마다";
        if ((m = Regex.Match(s, @"^of (?<w>your opponent's|their|your) Digimon and Tamers$", IC)).Success)
            return $"{(m.Groups["w"].Value.Equals("your", StringComparison.OrdinalIgnoreCase) ? "자신" : "상대")}의 디지몬과 테이머 1마리(명)마다";
        if (Regex.IsMatch(s, @"^other suspended Digimon$", IC)) return "레스트 상태인 다른 디지몬 1마리마다";
        if ((m = Regex.Match(s, $@"^of your (?<x>{TargetSpec.NameList})s?$", IC)).Success) return $"자신의 {NamesKo(m.Groups["x"].Value)} 1마리마다";
        return null;
    }
}

public static partial class KoreanEffectRenderer
{
    /// <summary>
    /// v0.39.0 비용 — 공식 BT18-019 "상대의 트래시에서 Lv.이 서로 다른 디지몬 카드 1장씩을 덱 위로 되돌리는 것으로," · "자신의 트래시에서 「키메라몬」과 「파워드라몬」 1장씩을 덱 아래로 되돌리는 것으로," ·
    /// BT20-097 "그 디지몬의 진화원에서 「도루몬」 1장을 패로 되돌리는 것으로,"
    /// </summary>
    private static string? CostKo39(string cost)
    {
        Match m;
        if ((m = SentenceParser.TrashReturnCostRe39.Match(cost.Trim())).Success)
        {
            string z = m.Groups["z"].Value.ToLowerInvariant();
            string where = z.Contains("trashes") || z.Contains(" or ") ? "서로의 트래시" : z.Contains("opponent") || z.StartsWith("their") ? "상대의 트래시" : "자신의 트래시";
            string pos = m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase) ? "위" : "아래";
            string pick = m.Groups["pick"].Value.Trim();
            var each = Regex.Match(pick, @"^1 of each (?<p>.+?) with different levels$", IC);
            if (each.Success && PickKo("1 " + each.Groups["p"].Value) is { } ek)
                return $"{where}에서 Lv.이 서로 다른 {Regex.Replace(ek, @"\s*1장$", "")} 1장씩을 덱 {pos}로 되돌리는 것으로,";
            var parts = Regex.Split(pick, @"\s+and\s+(?=1 )", IC);
            var ks = parts.Select(p => PickKo(p) ?? (Regex.IsMatch(p, @"^\d+ \[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*$", IC) ? $"{NamesKo(Regex.Replace(p, @"^\d+ ", ""))} {Regex.Match(p, @"^\d+").Value}장" : null)).ToList();
            if (ks.Any(k => k == null)) return null;
            string all = string.Join(" ", ks.Select((k, i) => i < ks.Count - 1 ? k + J(k!, "과", "와") : k));
            if (Regex.IsMatch(pick, @"^\d+ cards?$", IC)) all = $"카드 {Regex.Match(pick, @"^\d+").Value}장";
            return $"{where}에서 {all}{J(all, "을", "를")} 덱 {pos}로 되돌리는 것으로,";
        }
        if ((m = SentenceParser.UnderTamerToThatCostRe39.Match(cost.Trim())).Success)
            return $"자신의 테이머 아래의 카드 {m.Groups["n"].Value}장을 그 디지몬의 진화원 아래에 놓는 것으로,";
        if ((m = SentenceParser.ThoseSourcesCostRe39.Match(cost.Trim())).Success && (PickKo(m.Groups["pick"].Value) ?? (Regex.IsMatch(m.Groups["pick"].Value, @"^\d+ \[[^\]]+\]$") ? $"{NamesKo(Regex.Replace(m.Groups["pick"].Value, @"^\d+ ", ""))} {Regex.Match(m.Groups["pick"].Value, @"^\d+").Value}장" : null)) is { } pk)
            return $"그 디지몬의 진화원에서 {pk}{J(pk, "을", "를")} {(m.Groups["to"].Value.StartsWith("hand", StringComparison.OrdinalIgnoreCase) ? "패로" : "덱 아래로")} 되돌리는 것으로,";
        return null;
    }
}

public static partial class KoreanEffectRenderer
{
    /// <summary>v0.39.0 공식 BT19-008 "이 디지몬을 자신의 테이머 아래의 「오메가샤우트몬」으로 코스트를 지불하지 않고 진화시킬 수 있다" · BT19-025 "~ 테이머 아래의 특징으로 「블루 플레어」를 가진 디지몬 카드로 ~"</summary>
    private static Phrase? UnderTamerEvoKo39(string s)
    {
        var m = SentenceParser.UnderTamerEvoRe39.Match(s);
        if (!m.Success) return null;
        string tw = m.Groups["t"].Value;
        string? t = tw.Equals("this Digimon", StringComparison.OrdinalIgnoreCase) ? "이 디지몬" : Regex.IsMatch(tw, "^(?:it|that Digimon)$", IC) ? "그 디지몬" : TargetKo(tw);
        string pick = m.Groups["pick"].Value;
        string? pk = Regex.IsMatch(pick, @"^\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*$") ? NamesKo(pick) : PickKo("1 " + Regex.Replace(pick, @"^(?:an?|1) ", "", IC)) is { } p ? Regex.Replace(p, @"\s*1장$", "") : null;
        if (t == null || pk == null) return null;
        string how = m.Groups["free"].Value.StartsWith("without", StringComparison.OrdinalIgnoreCase) ? "코스트를 지불하지 않고 "
            : m.Groups["red"].Success ? $"지불하는 진화 코스트 -{m.Groups["red"].Value} 하여 " : m.Groups["cost"].Success ? $"진화 코스트 {m.Groups["cost"].Value}{J(m.Groups["cost"].Value, "으로", "로")} " : "";
        bool may = Regex.IsMatch(s, @" may digivolve", IC);
        return new($"{t}{J(t, "을", "를")} 자신의 테이머 아래의 {pk}{J(pk, "으로", "로")} {how}", new Verb(may ? "진화시킬 수 있다" : "진화시킨다", "진화시킬 수 있다", "진화시키는 것으로", "진화시키고"));
    }
}

public static partial class KoreanEffectRenderer
{
    /// <summary>
    /// v0.39.0 공식 EX5-060 "상대는 본인의 트래시에서 Lv.4 이하의 디지몬 카드 1장을 코스트를 지불하지 않고 레스트 상태로 등장시킨다" ·
    /// "자신의 트래시에서 등장한 디지몬의 Lv. 이하의 퍼플인 디지몬 카드 1장을 코스트를 지불하지 않고 등장시킬 수 있다" · BT19-082 "~ 그 디지몬의 진화원 아래에 놓을 수 있다"
    /// </summary>
    private static Phrase? MoreKo39(string s)
    {
        Match m;
        if ((m = SentenceParser.OppPlaysRe39.Match(s)).Success && PickKo(m.Groups["pick"].Value) is { } op)
        {
            string tail = m.Groups["noplay"].Success ? ". 이 효과로 등장한 디지몬의 【등장 시】 효과는 발휘하지 않는다" : "";
            return new($"상대는 본인의 {(m.Groups["z"].Value.Equals("trash", StringComparison.OrdinalIgnoreCase) ? "트래시" : "패")}에서 {op}{J(op, "을", "를")} 코스트를 지불하지 않고 {(m.Groups["sus"].Success ? "레스트 상태로 " : "")}",
                new Verb("등장시킨다" + tail, "등장시킬 수 있다" + tail, "등장시키는 것으로", "등장시키고"));
        }
        if ((m = SentenceParser.LevelLeItRe39.Match(s)).Success && PickKo(m.Groups["pick"].Value) is { } lp)
        {
            string z = m.Groups["z"].Value.ToLowerInvariant();
            string zk = z.Contains("hand") && z.Contains("trash") ? "자신의 패/트래시" : z.Contains("hand") ? "자신의 패" : "자신의 트래시";
            return new($"{zk}에서 그 디지몬의 Lv. 이하의 {lp}{J(lp, "을", "를")} 코스트를 지불하지 않고 ", Siki("등장"));
        }
        if ((m = SentenceParser.PlaceToThatRe39.Match(s)).Success && PickKo(m.Groups["pick"].Value) is { } pp)
        {
            string f = m.Groups["from"].Value.ToLowerInvariant();
            string zk = f.Contains("hand") && f.Contains("trash") ? "자신의 패/트래시" : f.Contains("hand") ? "자신의 패" : "자신의 트래시";
            return new($"{zk}에서 {pp}{J(pp, "을", "를")} 그 디지몬의 진화원 아래에 ", Notda);
        }
        return null;
    }
}

public static partial class KoreanEffectRenderer
{
    /// <summary>v0.39.0 공식 BT18-022 "자신의 디지몬/테이머 1마리(명)를 패의 특징으로 「하이브리드체」를 가진 블루/레드인 디지몬 카드로 지불하는 진화 코스트 -1 하여 진화시킬 수 있다"</summary>
    private static Phrase? DigimonOrTamerEvoKo39(string s)
    {
        var m = SentenceParser.DigimonOrTamerEvoRe39.Match(s);
        if (!m.Success) return null;
        string pick = m.Groups["pick"].Value;
        string? pk = Regex.IsMatch(pick, @"^\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*$") ? NamesKo(pick) : PickKo(Regex.Replace(pick, @"^(?:an?|1) ", "1 ", IC)) is { } p ? Regex.Replace(p, @"\s*1장$", "") : null;
        if (pk == null) return null;
        string f = m.Groups["from"].Value.ToLowerInvariant();
        string zk = f.Contains("hand") && f.Contains("trash") ? "패/트래시" : f.Contains("hand") ? "패" : "트래시";
        string how = m.Groups["free"].Success ? "코스트를 지불하지 않고 " : m.Groups["red"].Success ? $"지불하는 진화 코스트 -{m.Groups["red"].Value} 하여 " : "";
        bool may = Regex.IsMatch(s, @"\bmay digivolve\b", IC);
        return new($"자신의 디지몬/테이머 1마리(명)를 {zk}의 {pk}{J(pk, "으로", "로")} {how}", new Verb(may ? "진화시킬 수 있다" : "진화시킨다", "진화시킬 수 있다", "진화시키는 것으로", "진화시키고"));
    }
}

public static partial class KoreanEffectRenderer
{
    /// <summary>v0.39.0 공식 BT17-025 "다음 상대의 턴 종료 시, 그 디지몬을 패로 되돌린다" · P-165 "상대의 턴 종료 시, 그 토큰을 소멸시킨다"</summary>
    private static string? AtTurnEndKo39(string s)
    {
        var m = SentenceParser.AtTurnEndRe39.Match(s.Trim().TrimEnd('.'));
        if (!m.Success || m.Groups["v"].Value.Equals("delete", StringComparison.OrdinalIgnoreCase) == m.Groups["hand"].Success) return null;
        string w = m.Groups["w"].Value.ToLowerInvariant();
        string when = w is "your opponent's" or "their" ? (m.Groups["next"].Success ? "다음 상대의 턴 종료 시" : "상대의 턴 종료 시") : w == "your" ? "자신의 턴 종료 시" : "이 턴 종료 시";
        string t = m.Groups["t"].Value.Equals("that token", StringComparison.OrdinalIgnoreCase) ? "그 토큰" : "그 디지몬";
        return m.Groups["v"].Value.Equals("delete", StringComparison.OrdinalIgnoreCase) ? $"{when}, {t}{J(t, "을", "를")} 소멸시킨다" : $"{when}, {t}{J(t, "을", "를")} 패로 되돌린다";
    }
}

public static partial class KoreanEffectRenderer
{
    /// <summary>v0.39.0 공식 BT13-089 "다음 상대의 턴 종료 시, 자신의 트래시에서 「레이브몬」 1장을 코스트를 지불하지 않고 등장시킨다"</summary>
    private static string? AtTurnEndActKo39(string s)
    {
        var m = SentenceParser.AtTurnEndActRe39.Match(s.Trim().TrimEnd('.'));
        if (!m.Success || SentenceParser.AtTurnEndRe39.IsMatch(s.Trim().TrimEnd('.'))) return null;
        return Sentence(Cap(m.Groups["x"].Value), nested: true) is { } ko ? $"{(m.Groups["next"].Success ? "다음 " : "")}상대의 턴 종료 시, {ko}" : null;
    }
}
