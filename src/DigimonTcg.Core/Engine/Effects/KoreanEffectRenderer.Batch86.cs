using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.86.0 트래시 목록 — [Trash] [Main] 효과를 쓸 수 없는 이유 (조건 · 비용 구절의 한글) ·
/// "play this card with the cost reduced by N" (BT26-079 — 같은 뜻의 "play cost" 표기로 옮긴다)
/// </summary>
public static partial class KoreanEffectRenderer
{
    /// <summary>발동 조건 구절 → "~일 때" (끝 쉼표 없이). 읽지 못하면 null</summary>
    internal static string? CondIfKo86(string cond) => CondKo(cond)?.If.TrimEnd(',', ' ');

    /// <summary>비용 구절 → "~하는 것으로" (끝 쉼표 없이). 읽지 못하면 null</summary>
    internal static string? CostKo86(string cost) => CostKo(cost)?.TrimEnd(',', ' ');

    /// <summary>
    /// "deleting 1 of your level 5 Digimon with [Myotismon] in its text" (EX10-054) — 원래 규칙은 "자신의 … 자신의 디지몬"으로 겹쳐 옮겼다.
    /// 같은 뜻의 "in their texts" 표기(EX10-011과 같은 규칙)로 옮긴다
    /// </summary>
    private static string? CostKo86Fix(string cost)
    {
        var m = Regex.Match(cost.Trim(), @"^deleting (?<t>\d+ of your [^,]*?Digimon with \[[^\]]+\] in (?:its text|their texts))$", RegexOptions.IgnoreCase);
        if (!m.Success || TargetKo(m.Groups["t"].Value) is not { } t) return null;
        return $"{t}{J(t, "을", "를")} 소멸시키는 것으로,";
    }

    private static string? RenderClause86(string rawClause)
    {
        string t = Regex.Replace(rawClause.Trim(), @"\s+", " ");
        // BT26-079 "[Trash] [Main] If your hand has 5 or fewer cards, play this card with the cost reduced by 4."
        if (Regex.IsMatch(t, @"\bplay this card(?: from your (?:hand|trash))? with the cost reduced by \d+", RegexOptions.IgnoreCase))
        {
            string rw = Regex.Replace(t, @"\bplay this card(?<z> from your (?:hand|trash))? with the cost reduced by (?<n>\d+)", "play this card${z} with the play cost reduced by ${n}", RegexOptions.IgnoreCase);
            if (rw != t) return RenderClause(rw);
        }
        return null;
    }
}
