using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.85.0 59차 묶음 — 한글 표시 (원래 규칙이 읽지 못한 절만, SentenceParser.Batch85New와 같은 절):
/// 같은 뜻의 다른 표기로 바꿀 수 있는 절은 바꾼 문장을 옮기고 (BT12-109 · BT11-087 · BT18-096), 나머지는 절마다 옮긴다
/// </summary>
public static partial class KoreanEffectRenderer
{
    private static string? RenderClause85(string rawClause)
    {
        string t = Regex.Replace(rawClause.Trim(), @"\s+", " ");
        var rw = SentenceParser.Rewrite85(t, new Models.CardData());
        if (rw != null && rw != t && !rw.Contains('⟦') && RenderClause(rw) is { } viaRewrite)
            return viaRewrite;
        Match m;
        string Tr(string x) => TraitKo(x);
        // BT19-090 "・By unsuspending 1 of your [A] and 1 of your [B], attack a player with 1 of your Digimon."
        if ((m = Regex.Match(t, @"^(?<dot>・)?By unsuspending (?:1|one) of your \[(?<a>[^\]]+)\] and (?:1|one) of your \[(?<b>[^\]]+)\], attack a player with (?:1|one) of your Digimon\.$")).Success)
        {
            string a = NameKo(m.Groups["a"].Value), b = NameKo(m.Groups["b"].Value);
            return $"{m.Groups["dot"].Value}자신의 「{a}」{J(a, "과", "와")} 「{b}」 1마리씩을 액티브로 하는 것으로, 자신의 디지몬 1마리로 플레이어에게 어택할 수 있다.";
        }
        if (t == SentenceParser.Bt10093Api85)
            return $"【자신의 턴】 [턴에 1회] 특징으로 「{Tr("Bagra Army")}」{J(Tr("Bagra Army"), "을", "를")} 가진 Lv.4 이상인 자신의 디지몬 카드 1장을 등장시킬 때, 자신의 테이머 아래의 퍼플인 디지몬 카드 3장까지를 그 디지몬 카드의 진화원에 놓는 것으로, 놓은 1장마다 지불하는 등장 코스트 -2.";
        // 옵션 사용 코스트 감소 (BT25-049 · BT25-090의 셋째 효과)
        string? UseOpt(string x)
        {
            var u = SentenceParser.UseOptionCostRe85.Match(x);
            if (!u.Success) return null;
            string lab = Regex.Replace(u.Groups["labels"].Value, @"\]\s*\[", "] [").Trim();
            string head = lab.Length == 0 ? "" : RenderClause(lab + " Gain 1 memory.") is { } h && h.EndsWith("메모리를 +1 한다.") ? h[..^"메모리를 +1 한다.".Length] : "";
            if (lab.Length > 0 && head.Length == 0) return null;
            string tr = Tr(u.Groups["x"].Value);
            string? cost = Regex.IsMatch(u.Groups["cost"].Value, @"^trashing the bottom face-down card (?:from )?under any of your Tamers$", RegexOptions.IgnoreCase)
                ? "자신의 테이머 아래의 뒷면 카드를 아래에서부터 1장 파기하는 것으로" : null;
            if (cost == null) return null;
            return $"{head}자신이 특징으로 「{tr}」{J(tr, "을", "를")} 가진 옵션 카드를 사용할 때, {cost}, 지불하는 코스트 -{u.Groups["n"].Value}.";
        }
        if (UseOpt(t) is { } uo) return uo;
        if (t == SentenceParser.Bt25090Api85)
        {
            var parts = Regex.Split(t, @"(?<=\.) (?=\[(?:All Turns|Your Turn)\])");
            if (parts.Length == 3 && RenderClause(parts[0]) is { } p0 && RenderClause(parts[1]) is { } p1 && UseOpt(parts[2]) is { } p2)
                return $"{p0} {p1} {p2}";
        }
        // EX10-056
        if ((m = Regex.Match(t, @"^\[All Turns\] \[Once Per Turn\] When any of your opponent's Digimon or Tamers digivolve or effects place cards under them, by trashing any (?<n>\d+) of this Digimon's digivolution cards, trash your opponent's top security card\.$")).Success)
            return $"【서로의 턴】 [턴에 1회] 상대의 디지몬/테이머가 진화했을 때 또는 효과로 상대의 디지몬/테이머 아래에 카드가 놓였을 때, 이 디지몬의 진화원을 선택하여 {m.Groups["n"].Value}장 파기하는 것으로, 상대의 시큐리티를 위에서부터 1장 파기한다.";
        // BT18-096
        if ((m = Regex.Match(t, @"^\[Main\] 1 of your Digimon or Tamers may digivolve into \[(?<x>[^\]]+)\] in the hand or trash without paying the cost\. Then, by placing up to (?<n>\d+) of your Tamers with different colors under 1 of your \[(?<y>[^\]]+)\] as its bottom digivolution cards, gain 1 memory for each placed card\.$")).Success)
        {
            string x = NameKo(m.Groups["x"].Value), y = NameKo(m.Groups["y"].Value);
            return $"【메인】 자신의 디지몬/테이머 1마리(명)를 패/트래시의 「{x}」{J(x, "으로", "로")} 코스트를 지불하지 않고 진화시킬 수 있다. 그 후, 색이 서로 다른 자신의 테이머 {m.Groups["n"].Value}명까지를 자신의 「{y}」 1마리의 진화원 맨 아래에 놓는 것으로, 놓은 1장마다 메모리 +1.";
        }
        // BT11-109
        if ((m = Regex.Match(t, @"^\[Main\] You may place up to (?<n>\d+) Digimon cards with the \[(?<x>[^\]]+)\] trait from your trash under 1 of your Digimon as its bottom digivolution cards or under 1 of your Tamers\. Then, if you have a Digimon or Tamer with the \[(?<y>[^\]]+)\] trait in play, place 1 of your opponent's Digimon under 1 of your opponent's other Digimon as its bottom digivolution card\.$")).Success)
            return $"【메인】 자신의 트래시에서 특징으로 「{Tr(m.Groups["x"].Value)}」{J(Tr(m.Groups["x"].Value), "을", "를")} 가진 디지몬 카드 {m.Groups["n"].Value}장까지를 자신의 디지몬 1마리의 진화원 맨 아래 또는 자신의 테이머 1명 아래에 놓을 수 있다. "
                + $"그 후, 특징으로 「{Tr(m.Groups["y"].Value)}」{J(Tr(m.Groups["y"].Value), "을", "를")} 가진 자신의 디지몬/테이머가 있다면, 상대의 디지몬 1마리를 다른 상대의 디지몬 1마리의 진화원 맨 아래에 놓는다.";
        // BT21-012
        if ((m = Regex.Match(t, @"^\[Main\] By suspending this Digimon, you may play 1 (?<c>red|blue|yellow|green|black|purple|white) Tamer card with inherited effects from your hand without paying the cost\. If you did, place this Digimon under the played Tamer\.$")).Success)
            return $"【메인】 이 디지몬을 레스트시키는 것으로, 자신의 패에서 진화원 효과를 가진 {ColorKo85(m.Groups["c"].Value)}인 테이머 카드 1장을 코스트를 지불하지 않고 등장시킬 수 있다. 그랬다면, 이 디지몬을 등장한 그 테이머 아래에 놓는다.";
        // BT21-013
        if ((m = Regex.Match(t, @"^\[When Digivolving\] You may place 1 \[(?<a>[^\]]+)\] or \[(?<b>[^\]]+)\] trait Digimon card from your hand or trash as this Digimon's bottom digivolution card or under any of your (?<c>red|blue|yellow|green|black|purple|white) Tamers with inherited effects\.$")).Success)
            return $"【진화 시】 자신의 패/트래시에서 특징으로 「{Tr(m.Groups["a"].Value)}」/「{Tr(m.Groups["b"].Value)}」{J(Tr(m.Groups["b"].Value), "을", "를")} 가진 디지몬 카드 1장을 이 디지몬의 진화원 맨 아래 또는 진화원 효과를 가진 {ColorKo85(m.Groups["c"].Value)}인 자신의 테이머 1명 아래에 놓을 수 있다.";
        // EX10-039
        if ((m = Regex.Match(t, @"^\[Start of Your Main Phase\] You may place 1 Digimon card with the \[(?<x>[^\]]+)\] trait from your hand or trash as any of your \[(?<y>[^\]]+)\] trait Digimon's bottom digivolution cards or under any of your \[(?<z>[^\]]+)\] trait Tamers\.$")).Success)
            return $"【자신의 메인 페이즈 개시 시】 자신의 패/트래시에서 특징으로 「{Tr(m.Groups["x"].Value)}」{J(Tr(m.Groups["x"].Value), "을", "를")} 가진 디지몬 카드 1장을 특징으로 「{Tr(m.Groups["y"].Value)}」{J(Tr(m.Groups["y"].Value), "을", "를")} 가진 자신의 디지몬 1마리의 진화원 맨 아래 또는 특징으로 「{Tr(m.Groups["z"].Value)}」{J(Tr(m.Groups["z"].Value), "을", "를")} 가진 자신의 테이머 1명 아래에 놓을 수 있다.";
        // BT21-092
        if ((m = Regex.Match(t, @"^\[Main\] Place all Digimon cards in 1 of your \[(?<x>[^\]]+)\] trait Digimon's digivolution cards under 1 of your Tamers\. Then, you may play 1 Digimon card with the \[(?<y>[^\]]+)\] trait from your hand with the play cost reduced by (?<n>\d+) for each card this effect placed\.$")).Success)
            return $"【메인】 특징으로 「{Tr(m.Groups["x"].Value)}」{J(Tr(m.Groups["x"].Value), "을", "를")} 가진 자신의 디지몬 1마리의 진화원에 있는 디지몬 카드 전부를 자신의 테이머 1명 아래에 놓는다. "
                + $"그 후, 자신의 패에서 특징으로 「{Tr(m.Groups["y"].Value)}」{J(Tr(m.Groups["y"].Value), "을", "를")} 가진 디지몬 카드 1장을 이 효과로 놓은 카드 1장마다 지불하는 등장 코스트 -{m.Groups["n"].Value} 하여 등장시킬 수 있다.";
        // BT27-098
        if ((m = Regex.Match(t, @"^\[End of All Turns\] By trashing (?<n>\d+) bottom face-down cards from under any of your Tamers, you may play 1 Digimon card with \[(?<a>[^\]]+)\] or \[(?<b>[^\]]+)\] in its name from your hand or trash without paying the cost\. After, place this Tamer as its bottom digivolution card\.$")).Success)
            return $"【서로의 턴 종료 시】 자신의 테이머 아래의 뒷면 카드를 아래에서부터 {m.Groups["n"].Value}장 파기하는 것으로, 자신의 패/트래시에서 명칭에 「{NameKo(m.Groups["a"].Value)}」/「{NameKo(m.Groups["b"].Value)}」{J(NameKo(m.Groups["b"].Value), "을", "를")} 포함하는 디지몬 카드 1장을 코스트를 지불하지 않고 등장시킬 수 있다. 그 후, 이 테이머를 그 디지몬의 진화원 맨 아래에 놓는다.";
        // AD1-006
        if ((m = Regex.Match(t, @"^\[All Turns\] When this Digimon would leave the battle area other than by DigiXros, from this Digimon's digivolution cards, you may place up to (?<n>\d+) \[(?<a>[^\]]+)\] or \[(?<b>[^\]]+)\] trait Digimon cards under 1 of your Tamers and play 1 such card without paying the cost\.$")).Success)
            return $"【서로의 턴】 이 디지몬이 디지크로스 이외로 배틀 에어리어를 떠날 때, 이 디지몬의 진화원에서 특징으로 「{Tr(m.Groups["a"].Value)}」/「{Tr(m.Groups["b"].Value)}」{J(Tr(m.Groups["b"].Value), "을", "를")} 가진 디지몬 카드 {m.Groups["n"].Value}장까지를 자신의 테이머 1명 아래에 놓고, 그 카드 1장을 코스트를 지불하지 않고 등장시킬 수 있다.";
        if (t == SentenceParser.Bt7085Api85 || t == SentenceParser.Bt7087Api85)
        {
            bool hand = t == SentenceParser.Bt7087Api85;
            string x = NameKo(hand ? "MagnaGarurumon" : "EmperorGreymon");
            return $"【메인】 [턴에 1회] 자신의 {(hand ? "패" : "트래시")}에서 특징으로 「{Tr("Hybrid")}」{J(Tr("Hybrid"), "을", "를")} 가진 카드 5장을 원하는 순서로 이 테이머 아래에 놓는 것으로, 이 테이머를 {(hand ? "블루" : "레드")}인 Lv.5 디지몬으로 취급하여 패의 「{x}」{J(x, "으로", "로")} 진화 코스트를 지불하여 진화할 수 있다.";
        }
        if (t == SentenceParser.Bt12081Api85)
        {
            string q = NameKo("Quartzmon");
            return $"【진화 시】 자신의 테이머 아래에서 《세이브》가 적힌 Lv.4 이하의 디지몬 카드 1장을 코스트를 지불하지 않고 등장시킬 수 있다. 이 디지몬의 진화원이 4장 이상 있다면, 대신 이 디지몬은 자신의 패 또는 테이머 아래의 「{q}」{J(q, "으로", "로")} 지불하는 진화 코스트 -3 하여 진화할 수 있다.";
        }
        if (t == SentenceParser.P249Api85)
            return $"【자신의 메인 페이즈 개시 시】 자신의 패/트래시에서 특징으로 「{Tr("Hybrid")}」{J(Tr("Hybrid"), "을", "를")} 가진 카드 1장을 이 디지몬의 진화원 맨 아래 또는 진화원 효과를 가진 자신의 테이머 아래에 놓는 것으로, 그 디지몬/테이머는 패의 특징으로 「{Tr("Hybrid")}」{J(Tr("Hybrid"), "을", "를")} 가진 디지몬 카드로 지불하는 코스트 -2 하여 진화할 수 있다.";
        return null;
    }

    private static string ColorKo85(string c) => c.ToLowerInvariant() switch
    {
        "red" => "레드", "blue" => "블루", "yellow" => "옐로", "green" => "그린", "black" => "블랙", "purple" => "퍼플", _ => "화이트",
    };
}
