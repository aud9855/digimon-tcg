using System;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.38.0 대상 조건 — 다른 카드와 비교: "with the same level as the placed card" (공식 BT18-042 "놓은 카드와 같은 Lv.의") ·
/// "with a level less than or equal to the deleted Digimon's level" (BT11-076 "소멸한 디지몬의 Lv. 이하의") ·
/// "with a play cost less than or equal to the Digimon you played" (BT10-106 "등장한 디지몬의 등장 코스트 이하의") ·
/// "with DP less than or equal to the Digimon played with this effect" (BT6-110 "이 효과로 등장한 디지몬의 DP 이하의") ·
/// "with a level as high or lower than the number of cards in your security stack" (EX6-028 "자신의 시큐리티 매수 이하의 Lv.의")
/// </summary>
public sealed partial class TargetSpec
{
    /// <summary>비교하는 카드를 가리키는 말 (렌더러도 쓴다)</summary>
    internal const string RefWords38 =
        @"(?<r>the (?:placed|returned|trashed|deleted|chosen) (?:card|Digimon)|the Digimon (?:you played|played (?:with|by) this effect)|the played Digimon|that (?:card|Digimon)|it\b)";

    internal const string LevelLe38 = @"(?:(?:with )?(?:a )?level (?:that is )?(?:less than or equal to|equal to or (?:less|lower) than|as high or lower than)|whose level is (?:less than or equal to|equal to or (?:less|lower) than)|with as high or lower a level as)";
    internal const string CostLe38 = @"(?:(?:with )?a play cost (?:less than or equal to|equal to or (?:less|lower) than)|whose play cost (?:is )?(?:less than or equal to|equal to or (?:less|lower) than)|with as high or lower a play cost as)";
    internal const string DpLe38 = @"(?:with )?(?:DP less than or equal to|as much or less DP as)";

    /// <summary>비교하는 카드 (Card, DP). 등장한 디지몬 = 이 효과가 등장시킨 디지몬 → 시점의 디지몬, 그 밖 = 비용이 옮긴 카드 → 선택한 디지몬 → 앞의 대상 → 시점의 디지몬</summary>
    internal static (CardData Card, int Dp)? Ref38(EffectContext c, string word)
    {
        string w = word.ToLowerInvariant();
        static (CardData, int)? Of(CardInstance? d) => d == null ? null : (d.TopCard, d.GetCurrentDP());
        if (w.Contains("played"))
            return Of(c.PlayedThisEffect.LastOrDefault()) ?? Of(c.Other);
        if (w.Contains("chosen") || w is "that digimon" or "it")
        {
            if (c.Chosen34 != null) return Of(c.Chosen34);
            if (c.RefCard38 != null) return (c.RefCard38, c.RefDp38);
            return Of(c.LastTargets.FirstOrDefault()) ?? Of(c.Other);
        }
        if (c.RefCard38 != null) return (c.RefCard38, c.RefDp38);
        return Of(c.Other);
    }

    /// <summary>v0.38.0 다른 카드와 비교하는 조건이 있는지 (시뮬레이션 진단)</summary>
    internal bool UsesRef38 { get; private set; }

    private static Match? ParseFilter38(string rest, TargetSpec spec)
    {
        var r38 = ParseFilter38Core(rest, spec);
        if (r38 != null) spec.UsesRef38 = true;
        return r38;
    }

    private static Match? ParseFilter38Core(string rest, TargetSpec spec)
    {
        Match m;
        string tail = @"(?:'s (?:level|play cost|DP))?";
        if ((m = Regex.Match(rest, $@"^(?:with |and )?(?:the same level as|a level equal to) {RefWords38}{tail}", RegexOptions.IgnoreCase)).Success)
        {
            string w = m.Groups["r"].Value;
            spec.ContextFilters.Add((c, d) => Ref38(c, w) is { } r && r.Card.Level > 0 && d.TopCard.Level == r.Card.Level);
            return m;
        }
        if ((m = Regex.Match(rest, $@"^(?:and )?{LevelLe38} {RefWords38}{tail}", RegexOptions.IgnoreCase)).Success)
        {
            string w = m.Groups["r"].Value;
            spec.ContextFilters.Add((c, d) => Ref38(c, w) is { } r && r.Card.Level > 0 && d.TopCard.Level <= r.Card.Level);
            return m;
        }
        if ((m = Regex.Match(rest, $@"^(?:and )?{CostLe38} {RefWords38}{tail}", RegexOptions.IgnoreCase)).Success)
        {
            string w = m.Groups["r"].Value;
            spec.ContextFilters.Add((c, d) => Ref38(c, w) is { } r && d.TopCard.PlayCost <= r.Card.PlayCost);
            return m;
        }
        if ((m = Regex.Match(rest, $@"^(?:and )?{DpLe38} {RefWords38}{tail}", RegexOptions.IgnoreCase)).Success)
        {
            string w = m.Groups["r"].Value;
            spec.ContextFilters.Add((c, d) => Ref38(c, w) is { } r && d.GetCurrentDP() <= r.Dp);
            return m;
        }
        // "with ＜Save＞ in its text" (BT12-098 — 공식 "《세이브》가 기술되어 있는")
        if ((m = Regex.Match(rest, @"^(?:with |and |or )?＜(?<k>[^＞]+?)\s*＞ in (?:its|their) texts?", RegexOptions.IgnoreCase)).Success)
        {
            string k = m.Groups["k"].Value.Trim();
            spec.Filters.Add(d => d.TopCard.EffectText.Contains("＜" + k, StringComparison.OrdinalIgnoreCase) || d.TopCard.InheritedEffectText.Contains("＜" + k, StringComparison.OrdinalIgnoreCase));
            return m;
        }
        // "with as many or more digivolution cards as that Digimon" (BT14-091 — 공식 "진화원 매수가 선택한 디지몬 이상의")
        if ((m = Regex.Match(rest, @"^(?:with |and )?(?:as many or more|equal or more) digivolution cards (?:as|than) (?:that Digimon|the chosen Digimon|it\b)", RegexOptions.IgnoreCase)).Success)
        {
            spec.ContextFilters.Add((c, d) => (c.Chosen34 ?? c.LastTargets.FirstOrDefault()) is { } r && !ReferenceEquals(r, d) && d.EvolutionSources.Count >= r.EvolutionSources.Count);
            return m;
        }
        // "whose level is less than or equal to the number of cards in your security stack" · "~ in this Digimon's digivolution cards" · "~ in your or their security stack"(큰 쪽)
        if ((m = Regex.Match(rest, $@"^(?:and )?{LevelLe38} the number of cards in (?<src>your security stack|your or (?:their|your opponent's) security stack|this Digimon's digivolution cards)", RegexOptions.IgnoreCase)).Success)
        {
            string src = m.Groups["src"].Value.ToLowerInvariant();
            Func<EffectContext, int> n = src.StartsWith("this") ? c => c.Self?.EvolutionSources.Count ?? 0
                : src.Contains(" or ") ? c => Math.Max(c.Owner.SecurityStack.Count, c.Enemy.SecurityStack.Count)
                : c => c.Owner.SecurityStack.Count;
            spec.ContextFilters.Add((c, d) => d.TopCard.Level <= n(c));
            return m;
        }
        return null;
    }
}
