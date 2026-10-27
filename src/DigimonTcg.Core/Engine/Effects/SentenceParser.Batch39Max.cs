using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.39.0 상한 늘리기 · 줄이기의 다른 표기 (원래 규칙이 읽지 못한 문장만): "For each your Digimon with the [Three Musketeers] trait with different names, add 3000 to this DP-Based deletion effects maximum" (EX7-066) ·
/// "For each of your opponent's security cards, remove 2000 from this DP deletion effect's maximum" (BT15-089) · "For each card in your hand, reduce 1 from this effect's play cost maximum" (EX7-062) ·
/// "~ reduce this effect's play cost maximum by 1" (EX6-059) · "For every 3 cards in your opponent's hand, remove 1 from this effect level maximum" (EX8-072)
/// </summary>
internal static partial class SentenceParser
{
    internal sealed record MaxAdj39(string Counter, string What, int Per);

    /// <summary>상한을 바꾸는 문장 → (셈, 무엇: level · play cost · DP, 한 번에 바뀌는 값 — 줄이면 음수)</summary>
    internal static MaxAdj39? ParseMaxAdj39(string raw)
    {
        string s = raw.Trim().TrimEnd('.');
        var m = Regex.Match(s, @"^For (?:each|every) (?<cnt>.+?), (?:(?<v>add|remove|reduce) (?<n>\d+) (?:to|from) (?<w>.+?)|reduce (?<w>.+?) by (?<n>\d+))$", I);
        if (!m.Success) return null;
        string w = m.Groups["w"].Value.Trim().ToLowerInvariant();
        string? what = Regex.IsMatch(w, @"^this (?:dp[- ]?(?:based )?(?:deletion|delete) effects?'?s?|dp deletion effect's) maximum$") ? "DP"
            : Regex.Match(w, @"^this effect(?:'s)? (?<x>dp|level|play cost) maximum$") is { Success: true } x ? x.Groups["x"].Value
            : Regex.Match(w, @"^this effect(?:'s)? maximum (?<x>dp|level|play cost)$") is { Success: true } y ? y.Groups["x"].Value
            : null;
        if (what == null) return null;
        string v = m.Groups["v"].Success ? m.Groups["v"].Value.ToLowerInvariant() : "reduce";
        int n = int.Parse(m.Groups["n"].Value);
        string cnt = Regex.Replace(m.Groups["cnt"].Value.Trim(), @"^your\b", "of your", I);
        return new MaxAdj39(cnt, what.ToLowerInvariant() == "dp" ? "DP" : what.ToLowerInvariant(), v == "add" ? n : -n);
    }

    /// <summary>앞 문장의 상한 숫자를 셈만큼 바꿔 다시 읽은 문장으로 처리 (MaxUpStep과 같은 방식 — 줄일 때는 0 아래로 내려가지 않는다)</summary>
    private static Step? MaxAdjStep39(Step prev, string prevRaw, MaxAdj39 adj, CardData card, List<EffectTrigger> triggers)
    {
        string pattern = adj.What == "level" ? @"\b(?:level |Lv\.\s*)(?<v>\d+) or (?:lower|less)\b"
            : adj.What == "play cost" ? @"\b(?:play cost (?:of )?|a play cost of )(?<v>\d+) or (?:lower|less)\b"
            : @"\b(?<v>\d+) DP or (?:lower|less)\b";
        var thresholds = Regex.Matches(prevRaw, pattern, I);
        if (thresholds.Count != 1) return null;
        var thr = thresholds[0].Groups["v"];
        int baseValue = int.Parse(thr.Value);
        if (ParseCounter(adj.Counter) is not { } counter) return null;
        string Adjusted(int k) => prevRaw[..thr.Index] + Math.Max(0, baseValue + adj.Per * k) + prevRaw[(thr.Index + thr.Length)..];
        if (ParseStep(Adjusted(1), card, triggers).Step == null) return null;
        var byK = new Dictionary<int, Step?>();
        return prev with
        {
            Condition = null, Optional = false,
            Act = c =>
            {
                int k = Math.Min(counter(c.Owner, c.Enemy, c.Self), 20);
                if (k <= 0) { RunStep(prev, c); return; }
                Diag39.Hit(ref Diag39.CapForEach);
                if (!byK.TryGetValue(k, out var s)) byK[k] = s = ParseStep(Adjusted(k), card, triggers).Step;
                if (s != null) RunStep(s, c);
            },
        };
    }

    /// <summary>
    /// v0.39.0 셈 (원래 규칙이 읽지 못한 것만): "your Digimon with the [Three Musketeers] trait with different names" (EX7-066 — 이름이 다른 디지몬 수) ·
    /// "of your opponent's security cards" (BT15-089) · "of their Digimon and Tamers" (BT23-014) · "other suspended Digimon" (EX8-074) · "of your [Diaboromon]" (P-114)
    /// </summary>
    private static Func<PlayerZones, PlayerZones, CardInstance?, int>? ParseCounter39(string counter)
    {
        string s = counter.Trim().TrimEnd('.');
        Match m;
        if ((m = Regex.Match(s, @"^(?:of )?your (?<t>Digimon .+?) with different names$", I)).Success && TargetSpec.Parse("all of your " + m.Groups["t"].Value) is { ContextFilters.Count: 0 } spec)
            return (own, _, _) => own.BattleArea.Where(d => spec.Filters.All(f => f(d))).Select(d => CardNames.MatchName(d.TopCard)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (Regex.IsMatch(s, @"^of (?:your opponent's|their) security cards$", I))
            return (_, enemy, _) => enemy.SecurityStack.Count;
        if ((m = Regex.Match(s, @"^of (?<w>your opponent's|their|your) Digimon and Tamers$", I)).Success)
        {
            bool mine = m.Groups["w"].Value.Equals("your", StringComparison.OrdinalIgnoreCase);
            return (own, enemy, _) => { var z = mine ? own : enemy; return z.BattleArea.Count(d => d.TamerProxyOf == null) + z.Tamers.Count; };
        }
        if (Regex.IsMatch(s, @"^other suspended Digimon$", I))
            return (own, enemy, self) => own.BattleArea.Concat(enemy.BattleArea).Count(d => d.IsSuspended && !ReferenceEquals(d, self));
        if ((m = Regex.Match(s, $@"^of your (?<x>{TargetSpec.NameList})s?$", I)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            return (own, _, _) => own.BattleArea.Count(d => xs.Any(x => CardNames.Is(d.TopCard, x)));
        }
        return null;
    }
}
