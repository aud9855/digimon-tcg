using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.38.0 비용의 생성 한글 — 공식 표기: EX7-043 "자신의 패/트래시에서 특징으로 「3총사」를 가진 카드 3장을 덱 위로 되돌리는 것으로" ·
/// EX6-054 "자신의 트래시 또는 이 디지몬의 진화원에서 「루체몬」 1장을 덱 아래로 되돌리는 것으로" · BT21-045 "옐로/레드인 자신의 테이머 1명을 레스트시키는 것으로" ·
/// EX4-058 "진화원에 특징으로 「조」/「새」를 포함하는 카드가 있는 이 디지몬을 소멸시키는 것으로" · BT13-107 "자신의 「두프트몬: 레오파드 모드」에 겹쳐져 있는 카드를 위에서부터 1장 패로 되돌리는 것으로"
/// </summary>
public static partial class KoreanEffectRenderer
{
    private static string? CostKo38(string cost)
    {
        Match m;
        cost = cost.Trim().TrimEnd('.');
        string alt = cost;
        alt = Regex.Replace(alt, @"^(?<v>deleting \d+ of your Digimon with the \[[^\]]+\](?: or \[[^\]]+\])* trait) Digimon$", "${v}", IC);
        alt = Regex.Replace(alt, @"^trashing (?<n>\d+) of your Digimon (?<f>with .+?) from your hand$", "trashing ${n} Digimon card ${f} from your hand", IC);
        alt = Regex.Replace(alt, @"\bwith a digivolution card and ", "with 1 or more digivolution cards and ", IC);
        alt = Regex.Replace(alt, @"^Deleting\b", "deleting");
        if (alt != cost && CostKoCore(alt) is { } same) return same;

        // P-048 "자신의 트래시에서, 디지타마 카드 이외의 카드 3장을 원하는 순서대로 덱 아래로 되돌리는 것으로"
        if ((m = Regex.Match(cost, @"^returning (?<n>\d+) non[- ]Digi-Egg cards from your trash to the bottom of (?:the|your) deck(?<ord> in any order)?$", IC)).Success)
            return $"자신의 트래시에서 디지타마 카드 이외의 카드 {m.Groups["n"].Value}장을 {(m.Groups["ord"].Success ? "원하는 순서대로 " : "")}덱 아래로 되돌리는 것으로,";

        if ((m = Regex.Match(cost, @"^(?<v>trashing|returning) (?<pick>.+?) (?:from|in) (?<zones>(?:(?:your |the |any of your |in |from )*(?:hand|trash|this Digimon's digivolution cards|Digimon's digivolution cards|digivolution cards|Digimon|link cards)(?:,? (?:or|and/or) )?)+?)(?: to (?:the |your |its owner's )?(?<dest>hand|top of (?:the|your) deck|bottom of (?:the|your) deck)(?: in any order)?)?$", IC)).Success)
        {
            var zs = Regex.Split(m.Groups["zones"].Value, @",? (?:or|and/or) ", IC)
                .Select(z => Regex.Replace(z.Trim(), @"^(?:(?:your|the|any of your|in|from) )+", "", IC).ToLowerInvariant())
                .Select(z => z switch { "hand" => "패", "trash" => "트래시", "this digimon's digivolution cards" => "이 디지몬의 진화원", "digimon's digivolution cards" or "digivolution cards" or "digimon" => "디지몬의 진화원", "link cards" => "링크 카드", _ => null })
                .ToList();
            if (zs.Any(z => z == null)) return null;
            string pick = m.Groups["pick"].Value.Trim();
            pick = Regex.Replace(pick, @"^any (?<n>\d+) digivolution cards\b", "${n} cards", IC);
            pick = Regex.Replace(pick, $@"^(?<n>(?:up to )?\d+) (?<x>{TargetSpec.NameList}) text cards?$", "${n} cards with ${x} in their texts", IC);
            if (PickKo(pick) is not { } pk) return null;
            string where = "자신의 " + string.Join(zs.Contains("이 디지몬의 진화원") ? " 또는 " : "/", zs);
            bool trash = m.Groups["v"].Value.Equals("trashing", StringComparison.OrdinalIgnoreCase);
            string d = m.Groups["dest"].Value.ToLowerInvariant();
            string verb = trash ? "파기하는" : d.StartsWith("hand") ? "패로 되돌리는" : d.StartsWith("top") ? "덱 위로 되돌리는" : "덱 아래로 되돌리는";
            return $"{where}에서 {pk}{J(pk, "을", "를")} {verb} 것으로,";
        }
        if ((m = Regex.Match(alt, @"^deleting (?<n>\d+) of your (?<t>.+)$", IC)).Success
            && TargetKo($"{m.Groups["n"].Value} of your {Regex.Replace(m.Groups["t"].Value, @"\btheir (names?|texts?)\b", "its $1", IC)}") is { } tk)
            return $"{tk}{J(tk, "을", "를")} 소멸시키는 것으로,";
        if (Regex.IsMatch(cost, @"^deleting (?:1|one) (?:of your )?Tokens?$", IC)) return "자신의 토큰 1마리를 소멸시키는 것으로,";
        if ((m = Regex.Match(cost, @"^deleting this Digimon that has a digivolution card (?<pick>with .+)$", IC)).Success && PickKo("1 card " + m.Groups["pick"].Value) is { } sk)
            return $"진화원에 {Regex.Replace(sk, @"\s*1장$", "")}{J(Regex.Replace(sk, @"\s*1장$", ""), "이", "가")} 있는 이 디지몬을 소멸시키는 것으로,";
        if ((m = Regex.Match(cost, @"^suspending (?:1|one) of your (?<col>(?:red|blue|yellow|green|black|purple|white)(?: or (?:red|blue|yellow|green|black|purple|white))*) Tamers$", IC)).Success)
            return $"{string.Join("/", Regex.Matches(m.Groups["col"].Value, "red|blue|yellow|green|black|purple|white", IC).Select(x => ColorKo37(x.Value)))}인 자신의 테이머 1명을 레스트시키는 것으로,";
        if ((m = Regex.Match(cost, $@"^returning the top card of (?:1|one) of your (?<x>{TargetSpec.NameList}) to (?:the |your |its owner's )?hand$", IC)).Success)
            return $"자신의 {NamesKo37(m.Groups["x"].Value, false)}에 겹쳐져 있는 카드를 위에서부터 1장 패로 되돌리는 것으로,";
        if (PlaceSourcesKo38(cost) is { } ps) return $"{ps.What} {ps.Where} 놓는 것으로,";
        if (Regex.IsMatch(cost, @"^trashing (?:1|one) card from your hand with the same level as the deleted Digimon$", IC))
            return "자신의 패에서 소멸한 디지몬과 같은 Lv.의 카드 1장을 파기하는 것으로,";
        return null;
    }
}

public static partial class KoreanEffectRenderer
{
    /// <summary>"placing 3 Digimon cards with [Gammamon] in their texts from your hand or trash as this Digimon's top or bottom digivolution cards" → "자신의 패/트래시에서 ~ 3장을 이 디지몬의 진화원 위 또는 아래에 놓는"</summary>
    private static (string What, string Where)? PlaceSourcesKo38(string text)
    {
        var m = Regex.Match(text.Trim().TrimEnd('.'), @"^(?:placing|place) (?<pick>.+?) from your (?<z>hand or (?:your )?trash|hand|trash) (?:as (?<host>this Digimon's|(?:1|one) of your (?<hf>[^,]*?)Digimon's) (?<pos>top or bottom|top|bottom) digivolution cards?|under (?<host2>this Digimon|(?:1|one) of your (?<hf2>[^,]*?)Digimon) as (?:its|their) (?<pos2>top or bottom|top|bottom) digivolution cards?)$", IC);
        if (!m.Success) return null;
        string pos = (m.Groups["pos"].Success ? m.Groups["pos"].Value : m.Groups["pos2"].Value).ToLowerInvariant();
        string pickText = Regex.Replace(m.Groups["pick"].Value.Trim(), @"\bin its text\b", "in their texts", IC);
        if (PickKo(pickText) is not { } pk) return null;
        string z = m.Groups["z"].Value.ToLowerInvariant();
        string from = z.Contains("hand") && z.Contains("trash") ? "자신의 패/트래시에서" : z.Contains("hand") ? "자신의 패에서" : "자신의 트래시에서";
        bool thisHost = (m.Groups["host"].Success ? m.Groups["host"].Value : m.Groups["host2"].Value).StartsWith("this", StringComparison.OrdinalIgnoreCase);
        string hf = m.Groups["hf"].Success ? m.Groups["hf"].Value : m.Groups["hf2"].Value;
        string? host = thisHost ? "이 디지몬" : TargetKo($"1 of your {hf}Digimon");
        if (host == null) return null;
        string posKo = pos == "top" ? "위" : pos == "bottom" ? "아래" : "위 또는 아래";
        return ($"{from} {pk}{J(pk, "을", "를")}", $"{host}의 진화원 {posKo}에");
    }

    private static string? SentenceKo38c(string s, bool may)
    {
        Match m;
        if (PlaceSourcesKo38(s) is { } ps) return $"{ps.What} {ps.Where} {(may ? "놓을 수 있다" : "놓는다")}";
        // 공식 BT16-056 "상대의 시큐리티를 위 또는 아래에서부터 1장 파기한다"
        if ((m = Regex.Match(s, @"^Trash (?:the (?<pos>top or bottom|bottom) card of (?<who>your opponent's|their|your) security stack|(?<who>your opponent's|their|your) (?<pos>top or bottom|bottom) security card)$", IC)).Success)
        {
            string who = m.Groups["who"].Value.Equals("your", StringComparison.OrdinalIgnoreCase) ? "자신" : "상대";
            string pos = m.Groups["pos"].Value.StartsWith("top", StringComparison.OrdinalIgnoreCase) ? "위 또는 아래" : "아래";
            return $"{who}의 시큐리티를 {pos}에서부터 1장 파기한다";
        }
        // 공식 BT16-056 "특징으로 「백신종」을 가진 상대의 디지몬 1마리에 겹쳐져 있는 카드를 위에서부터 1장 시큐리티 위에 놓을 수 있다"
        if ((m = Regex.Match(s, @"^Place the top card of (?<t>(?:1|one) of your opponent's .+?) (?:on (?<pos>top|the bottom) of their security stack|as their (?<pos>top|bottom) security card)$", IC)).Success
            && TargetKo(m.Groups["t"].Value) is { } tk)
            return $"{tk}에 겹쳐져 있는 카드를 위에서부터 1장 상대의 시큐리티 {(m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase) ? "위" : "아래")}에 {(may ? "놓을 수 있다" : "놓는다")}";
        return null;
    }
}
