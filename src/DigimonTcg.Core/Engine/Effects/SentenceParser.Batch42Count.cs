using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.42.0 셈 — 원래 규칙(ParseCounter ~ ParseCounter39)이 읽지 못한 것만</summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _counterDepth42;

    private static int SuspendedDigimonAndTamers42(PlayerZones z) =>
        z.BattleArea.Count(d => d.IsSuspended && d.TamerProxyOf == null) + z.SuspendedTamers.Count;

    private static int TamerColors42(PlayerZones z) => z.Tamers.SelectMany(t => t.AllColors).Distinct().Count();

    /// <summary>
    /// "2 suspended Digimon and/or Tamers" (BT12-057 — 공식 "레스트 상태인 디지몬과 테이머 2마리마다" — 양쪽) · "2 of your opponent's suspended Digimon or Tamers" (BT13-060) ·
    /// "2 of your Tamers' colors" · "3 of their colors" (AD1-014 · BT21-061 · AD1-004 — 자신의 테이머의 색) · "color among your opponent's Digimon and Tamers" (BT18-079 — 공식 "상대의 디지몬과 테이머의 색 1색마다") ·
    /// "yellow and/or red Tamer you have in play" (BT12-043 · BT12-104) · "Digimon your opponent has with no digivolution cards" (BT7-024) · "Tamer (in play)" (BT17-076 · BT6-086 — 공식 "자신과 상대의 테이머 1명당") ·
    /// "of this Digimon's level 6 digivolution cards" (BT20-037) · "of this Digimon's color(s)" (BT8-084) · "A and each B" (BT7-108 · BT13-030 — 공식 "특징으로 「하이브리드체」를 갖는 자신의 디지몬과 자신의 테이머 1명마다") ·
    /// "other Digimon with ~ you have in play" (BT13-017) · "digivolution card of 1 of your [Mother D-Reaper]s" (BT19-078) · "of your link cards" (BT25-075) · "of your red Tamers with different names" (BT21-082) ·
    /// "Option card in you and your opponent's trashes" (BT7-015)
    /// </summary>
    private static Func<PlayerZones, PlayerZones, CardInstance?, int>? ParseCounter42(string counter)
    {
        if (_counterDepth42 > 1) return null;
        _counterDepth42++;
        try
        {
            string s = counter.Trim().TrimEnd('.');
            Match m;
            int Per(Match x) => x.Groups["n"].Success && int.TryParse(x.Groups["n"].Value, out int n) && n > 0 ? n : 1;
            // 레스트 상태인 디지몬과 테이머
            if ((m = Regex.Match(s, @"^(?:(?<n>\d+) )?(?:of (?<w>your opponent's|their|your) )?suspended Digimon (?:and/or|and|or) Tamers(?: (?<w2>your opponent has|you have))?(?: in play)?$", I)).Success)
            {
                int per = Per(m);
                string w = (m.Groups["w"].Value + m.Groups["w2"].Value).ToLowerInvariant();
                bool opp = w.Contains("opponent") || w.Contains("their"), mine = !opp && w.Contains("you");
                return (o, e, _) => (opp ? SuspendedDigimonAndTamers42(e) : mine ? SuspendedDigimonAndTamers42(o) : SuspendedDigimonAndTamers42(o) + SuspendedDigimonAndTamers42(e)) / per;
            }
            // 자신의 테이머의 색
            if ((m = Regex.Match(s, @"^(?:(?<n>\d+) )?of (?:your Tamers'|their) colou?rs$", I)).Success)
            { int per = Per(m); return (o, _, _) => TamerColors42(o) / per; }
            // 상대의 디지몬과 테이머의 색
            if (Regex.IsMatch(s, @"^colou?r (?:among|of) your opponent's Digimon and Tamers$", I))
                return (_, e, _) => e.BattleArea.SelectMany(d => d.Colors).Concat(e.Tamers.SelectMany(t => t.AllColors)).Distinct().Count();
            // "yellow and/or red Tamer you have in play"
            if ((m = Regex.Match(s, $@"^(?<a>{Colors})(?: and/or| or|,) (?<b>{Colors}) Tamer you have(?: in play)?$", I)).Success)
            {
                var a = Enum.Parse<CardColor>(m.Groups["a"].Value, true); var b = Enum.Parse<CardColor>(m.Groups["b"].Value, true);
                return (o, _, _) => o.Tamers.Count(t => t.AllColors.Contains(a) || t.AllColors.Contains(b));
            }
            if (Regex.IsMatch(s, @"^Digimon your opponent has with no digivolution cards$", I))
                return (_, e, _) => e.BattleArea.Count(d => d.EvolutionSources.Count == 0);
            // "Tamer" · "Tamer in play" — 양쪽 (공식 BT6-086 "자신과 상대의 테이머 1명당")
            if (Regex.IsMatch(s, @"^Tamer(?: in play)?$", I))
                return (o, e, _) => o.Tamers.Count + e.Tamers.Count;
            if ((m = Regex.Match(s, @"^of this Digimon's level (?<lv>\d+) digivolution cards$", I)).Success)
            { int lv = int.Parse(m.Groups["lv"].Value); return (_, _, self) => self?.SourcesCount(x => x.Level == lv) ?? 0; }
            if (Regex.IsMatch(s, @"^of this Digimon's colou?rs?$", I))
                return (_, _, self) => self?.Colors.Count ?? 0;
            if (Regex.IsMatch(s, @"^of your link cards$", I))
                return (o, _, _) => o.BattleArea.Sum(d => d.LinkedCards.Count);
            if ((m = Regex.Match(s, $@"^of your (?:(?<c>{Colors}) )?Tamers with different names$", I)).Success)
            {
                CardColor? c = m.Groups["c"].Success ? Enum.Parse<CardColor>(m.Groups["c"].Value, true) : null;
                return (o, _, _) => o.Tamers.Where(t => c == null || t.AllColors.Contains(c.Value)).Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            }
            if (Regex.IsMatch(s, @"^Option cards? in (?:you|your) and your opponent's trash(?:es)?$", I))
                return (o, e, _) => o.Trash.Count(x => x.Type == CardType.Option) + e.Trash.Count(x => x.Type == CardType.Option);
            // "digivolution card of 1 of your [Mother D-Reaper]s" · "of 1 of your [Mother D-Reaper]'s digivolution cards" — 가장 많은 것 (고르는 쪽이 유리하게)
            if ((m = Regex.Match(s, @"^(?:digivolution card of (?:1|one) of your (?<x>\[[^\]]+\])s?|of (?:1|one) of your (?<x>\[[^\]]+\])(?:'s|s') digivolution cards)$", I)).Success)
            {
                string x = m.Groups["x"].Value.Trim('[', ']');
                return (o, _, _) => o.BattleArea.Where(d => CardNames.Is(d.TopCard, x)).Select(d => d.EvolutionSources.Count).DefaultIfEmpty(0).Max();
            }
            // "other Digimon with the [Royal Knight] trait or [Sistermon] in their names you have in play" (BT13-017)
            if ((m = Regex.Match(s, @"^(?<other>other )?(?<x>(?:[^,]*? )?Digimon with .+?) you have(?: in play)?$", I)).Success
                && TargetSpec.Parse("all of your " + (m.Groups["other"].Success ? "other " : "") + m.Groups["x"].Value) is { ContextFilters.Count: 0, Opponent: false } spec)
            {
                bool other = m.Groups["other"].Success;
                return (o, _, self) => o.BattleArea.Count(d => (!other || !ReferenceEquals(d, self)) && spec.Filters.All(f => f(d)));
            }
            // "A and each B" — 두 셈의 합 (BT7-108 "Digimon with the [Hybrid] trait and each Tamer you have in play" · BT13-030 "of your Digimon with the [Royal Knight] trait and each of your blue Tamers in play")
            if ((m = Regex.Match(s, @"^(?<a>.+?) and each (?<b>.+)$", I)).Success)
            {
                string a = m.Groups["a"].Value, b = m.Groups["b"].Value;
                string tail = Regex.Match(b, @"(?: you have)? in play$", I).Value;
                if (!a.StartsWith("of ", StringComparison.OrdinalIgnoreCase) && tail.Length > 0 && !Regex.IsMatch(a, @"\byou have\b", I)) a += tail.Contains("you have") ? tail : " you have" + tail;
                var ca = ParseCounter(a) ?? ParseCounter(Regex.Replace(a, @" in play$", "", I));
                var cb = ParseCounter(b) ?? ParseCounter(Regex.Replace(b, @" in play$", "", I));
                if (ca != null && cb != null) return (o, e, self) => ca(o, e, self) + cb(o, e, self);
            }
            return null;
        }
        finally { _counterDepth42--; }
    }

    /// <summary>
    /// 셈이 들어간 동작 (원래 규칙이 읽지 못한 것만): "Trash all of your security cards" (BT26-083 — 파기한 매수를 기억 → "For each card this effect trashed, ~")
    /// </summary>
    private static (Action<EffectContext>? Act, string? Error)? CountAction42Impl(string s, CardData card, List<EffectTrigger> triggers, string label)
    {
        if (Regex.IsMatch(s, @"^Trash all (?:of )?your security cards$", I))
            return (c =>
            {
                int k = 0;
                while (c.Owner.SecurityStack.Count > 0)
                {
                    var x = c.Owner.SecurityStack[0];
                    c.Owner.SecurityStack.RemoveAt(0);
                    c.Owner.Trash.Add(x);
                    c.Engine.NotifySecurityRemoved(c.Owner, x, trashed: true);
                    k++;
                }
                c.PaidCount = k;
                if (k == 0) c.LastStepResolved = false;
                else Diag42.Hit(ref Diag42.SecurityTrashN);
                Emit(c, label, $"자신의 시큐리티 {k}장 파기");
            }, null);
        return null;
    }
}
