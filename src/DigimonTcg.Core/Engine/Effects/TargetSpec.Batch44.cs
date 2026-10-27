using System;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.44.0 원래 규칙이 읽지 못한 대상만:
///  - "with as much or less DP as 1 of your [Dark Dragon] or [Evil Dragon] trait Digimon" (EX11-050 · ST22-08 · BT27-015 — 공식 ST22-08 "자신의 디지몬 1마리의 DP 이하의") — 자신의 그 디지몬 중 가장 높은 DP 이하
///  - "with as high or lower a level as 1 of your Digimon with [Seadramon] in its text" (P-214)
///  - "with a level as high or higher as the number of cards in your hand" (BT23-097 · LM-068 · EX6-071 — 공식 "자신의 패의 매수 이상의 Lv.인")
///  - "with the same color as any of this Digimon's digivolution cards" (EX9-074)
///  - "with as much or less DP as the Digimon this effect played" (BT16-015 — "the Digimon played by this effect"와 같다)
/// </summary>
public sealed partial class TargetSpec
{
    private const RegexOptions I44 = RegexOptions.IgnoreCase;

    private static TargetSpec? Parse44(string phrase)
    {
        if (!SentenceParser.Gate44) return null;   // 원래 규칙이 읽지 못한 절을 다시 읽을 때만 (SentenceParser.TryParse44)
        phrase = phrase.Trim().TrimEnd('.').Trim();
        Match m;
        if (Regex.IsMatch(phrase, @"\bthe Digimon this effect played\b", I44))
            return ParseCore(Regex.Replace(phrase, @"\bthe Digimon this effect played\b", "the Digimon played by this effect", I44)) ?? Parse39(Regex.Replace(phrase, @"\bthe Digimon this effect played\b", "the Digimon played by this effect", I44));
        // "1 of your non-white Digimon" (BT14-097 · P-146 — 공식 "화이트 이외의")
        if ((m = Regex.Match(phrase, @"^(?<pre>.*?\bof your(?: opponent's)? )non-(?<c>red|blue|yellow|green|black|purple|white) (?<rest>Digimon.*)$", I44)).Success
            && Parse(m.Groups["pre"].Value + m.Groups["rest"].Value) is { IsThis: false, IsThat: false } nc)
        {
            var col = Enum.Parse<CardColor>(m.Groups["c"].Value, true);
            nc.Filters.Add(d => !d.Colors.Contains(col));
            return nc;
        }
        // "1 of your Digimon with [Bird] in any of its traits or with the [Vortex Warriors] trait" (ST18-05 — 특징에 포함 · 또는)
        if ((m = Regex.Match(phrase, @"^(?<pre>.+?Digimon) with (?<a>.+?) or (?<b>with .+)$", I44)).Success
            && Parse(m.Groups["pre"].Value + " with " + m.Groups["a"].Value) is { IsThis: false, IsThat: false } ua
            && Parse(m.Groups["pre"].Value + " " + m.Groups["b"].Value) is { IsThis: false, IsThat: false } ub)
        {
            var fa = ua.Filters.ToList(); var fb = ub.Filters.ToList();
            var cfa = ua.ContextFilters.ToList(); var cfb = ub.ContextFilters.ToList();
            ua.Filters.Clear(); ua.ContextFilters.Clear();
            ua.ContextFilters.Add((c, d) => fa.All(f => f(d)) && cfa.All(f => f(c, d)) || fb.All(f => f(d)) && cfb.All(f => f(c, d)));
            return ua;
        }
        if ((m = Regex.Match(phrase, $@"^(?<pre>.+?Digimon) with (?<x>{NameList}) in (?:any|one) of (?:its|their) traits$", I44)).Success
            && Parse(m.Groups["pre"].Value) is { IsThis: false, IsThat: false } tr)
        {
            var xs = Names(m.Groups["x"].Value);
            tr.Filters.Add(d => d.TopCard.Traits.Any(t => xs.Any(x => t.Contains(x, StringComparison.OrdinalIgnoreCase))));
            return tr;
        }
        // "1 of their Digimon or Tamers without cards under it" (LM-005 · RB1-014 · BT16-069 — 공식 "아래에 카드가 없는 상대의 디지몬/테이머") — 테이머는 테이머 아래 카드가 없을 때
        if ((m = Regex.Match(phrase, @"^(?<head>.+?) without (?:any )?cards under (?:it|them)$", I44)).Success
            && Parse(m.Groups["head"].Value) is { IsThis: false, IsThat: false } hw)
        {
            hw.Filters.Add(d => d.EvolutionSources.Count == 0);
            bool opp = hw.Opponent;
            hw.ContextFilters.Add((c, d) => d.TopCard.Type != CardType.Tamer || d.TamerProxyOf != null || (opp ? c.Enemy : c.Owner).UnderTamers.Count == 0);
            return hw;
        }
        // "1 of your [Dark Dragon] or [Evil Dragon] Digimon" (BT24-080 — 이름 또는 특징) · "1 of your [Leviamon] or Digimon with [X Antibody] in their digivolution cards" (BT15-081)
        if ((m = Regex.Match(phrase, @"^(?<pre>(?:\d+|all) of your) (?<a>\[[^\]]+\](?: or \[[^\]]+\])+) Digimon$", I44)).Success)
        {
            var xs = Names(m.Groups["a"].Value);
            if (ParseCore(m.Groups["pre"].Value + " Digimon") is not { } baseSpec) return null;
            baseSpec.Filters.Add(d => xs.Any(x => NameHas(d.TopCard, x) || HasTrait(d.TopCard, x)));
            return baseSpec;
        }
        if ((m = Regex.Match(phrase, @"^(?<pre>(?:\d+|all) of your) (?<a>\[[^\]]+\]) or (?<b>Digimon with .+)$", I44)).Success
            && ParseCore(m.Groups["pre"].Value + " Digimon") is { } bs && ParseCore("all of your " + m.Groups["b"].Value) is { ContextFilters.Count: 0 } other)
        {
            var x = m.Groups["a"].Value.Trim('[', ']');
            var fo = other.Filters.ToList();
            bs.Filters.Add(d => NameHas(d.TopCard, x) || fo.All(f => f(d)));
            return bs;
        }
        // DP · Lv. 비교 — 자신의 디지몬 1마리
        if ((m = Regex.Match(phrase, @"^(?<head>.+?) with (?:(?<dp>as much or less DP as|DP less than or equal to) (?:1|one|any) of your (?<t>.+?)|as much DP as (?:1|one) of your (?<t2>.+?) or less|(?<lv>as high or lower a level as|a level less than or equal to) (?:1|one|any) of your (?<t>.+?))$", I44)).Success
            && ParseCore(m.Groups["head"].Value) is { IsThis: false, IsThat: false } head)
        {
            string t = m.Groups["t2"].Success ? m.Groups["t2"].Value : m.Groups["t"].Value;
            if (!Regex.IsMatch(t, @"\bDigimon\b", I44)) return null;
            if (ParseCore("all of your " + t) is not { Opponent: false, AnySide: false, IsThis: false, IsThat: false, ContextFilters.Count: 0 } own) return null;
            var fs = own.Filters.ToList();
            bool level = m.Groups["lv"].Success;
            head.ContextFilters.Add((c, d) =>
            {
                var mine = c.Owner.BattleArea.Where(x => !ReferenceEquals(x, d) && x.TamerProxyOf == null && fs.All(f => f(x))).ToList();
                if (mine.Count == 0) return false;
                return level ? d.TopCard.Level <= mine.Max(x => x.TopCard.Level) : d.GetCurrentDP() <= mine.Max(x => x.GetCurrentDP());
            });
            return head;
        }
        // Lv. ≥ 패의 매수
        if ((m = Regex.Match(phrase, @"^(?<head>.+?) with a level (?:as high or higher as|as high as|equal to or higher than|greater than or equal to) the number of cards in (?<w>your|their|your opponent's) hand(?: or higher)?$", I44)).Success
            && ParseCore(m.Groups["head"].Value) is { IsThis: false, IsThat: false } hh)
        {
            bool mineHand = m.Groups["w"].Value.Equals("your", StringComparison.OrdinalIgnoreCase);
            hh.ContextFilters.Add((c, d) => d.TopCard.Level >= (mineHand ? c.Owner.Hand.Count : c.Enemy.Hand.Count));
            return hh;
        }
        // 이 디지몬의 진화원 중 어느 카드와 같은 색
        if ((m = Regex.Match(phrase, @"^(?<head>.+?) with the same colou?r as any of this Digimon's digivolution cards$", I44)).Success
            && ParseCore(m.Groups["head"].Value) is { IsThis: false, IsThat: false } hc)
        {
            hc.ContextFilters.Add((c, d) => c.Self != null && c.Self.FaceUpSources.SelectMany(s => s.AllColors).Distinct().Any(col => d.Colors.Contains(col)));
            return hc;
        }
        return null;
    }
}
