using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.41.0 다른 카드 · 이 디지몬의 효과를 발휘한다 — "activate this Digimon's [On Play] effects" (EX5-059 · EX5-061) · "activate 1 of this Digimon's [On Play] effects" (BT23-101) ·
/// "activate 1 [On Play] effect on the placed card as an effect of this Digimon" (BT18-066 · EX9-073 · BT15-102) · "Activate 1 of that card's [When Digivolving] effects as an effect of this Digimon" (BT10-112) ·
/// "activate 1 of its [When Digivolving] effects" (BT10-110) · "activate 1 of that Digimon's [On Play] effects" (EX3-065) · "activate the [When Digivolving] effects of the Digimon this effect played" (BT15-041) ·
/// "Activate 1 of your [Rina Shinomiya]'s [On Play] effect" (BT11-029) · "activate 1 [On Play] or [When Digivolving] effect of 1 of your [Olympos XII] trait Digimon" (BT24-102) ·
/// "activate 1 of the [On Deletion] effects of 1 of your red Digimon with the [Vaccine] trait" (BT11-097) ·
/// "Activate 1 [When Digivolving] effect of 1 Digimon card with [Justimon] in its name in this Digimon's digivolution cards as if was this Digimon's effect" (EX8-054)
/// — 공식 BT18-066 "놓은 카드의 【등장 시】 효과 1개를 이 디지몬의 효과로서 발휘할 수 있다". 발휘하는 효과는 그 카드의 메인 효과 (조건 · [턴에 1회]는 그대로)
/// </summary>
internal static partial class SentenceParser
{
    private static readonly Dictionary<string, EffectTrigger> ActivateTriggers41 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["On Play"] = EffectTrigger.OnPlay, ["When Digivolving"] = EffectTrigger.WhenDigivolving, ["On Deletion"] = EffectTrigger.OnDeletion,
        ["When Attacking"] = EffectTrigger.WhenAttacking,
    };

    internal static readonly Regex ActivateRe41 = new(
        @"^(?<may>you may )?activate (?:(?<one>1 (?:of )?)|(?<all>the |all (?:of )?))?" +
        @"(?:(?<who>this Digimon's|its|that Digimon's|that card's|the placed card's|your (?<name>\[[^\]]+\])'s) (?<trig>\[[^\]]+\](?: or \[[^\]]+\])?)(?: effects?)?" +
        @"|(?<trig>\[[^\]]+\](?: or \[[^\]]+\])?) effects? (?:on|of) (?<what>that card|the placed card|the Digimon this effect played|1 of your [^,]+?Digimon(?: with [^,]+?)?|1 Digimon card with .+? in this Digimon's digivolution cards))" +
        @"(?: as (?:an effect of this Digimon|if (?:it )?was this Digimon's effect))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Action<EffectContext>? ActivateEffects41(string s, List<EffectTrigger> triggers)
    {
        var m = ActivateRe41.Match(s.Trim());
        if (!m.Success) return null;
        var trigs = Regex.Matches(m.Groups["trig"].Value, @"\[([^\]]+)\]").Select(x => ActivateTriggers41.TryGetValue(x.Groups[1].Value.Trim(), out var t) ? t : (EffectTrigger?)null).ToList();
        if (trigs.Count == 0 || trigs.Any(t => t == null)) return null;
        var trigSet = trigs.Select(t => t!.Value).ToHashSet();
        bool one = m.Groups["one"].Success;
        string who = m.Groups["who"].Value.ToLowerInvariant(), what = m.Groups["what"].Value;
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        TargetSpec? ownSpec = null;
        (int Count, Func<CardData, bool> Match)? srcPick = null;
        if (what.StartsWith("1 of your", StringComparison.OrdinalIgnoreCase) && (ownSpec = TargetSpec.Parse(what)) is not { Opponent: false, AnySide: false }) return null;
        if (Regex.Match(what, @"^(?<p>1 Digimon card with .+?) in this Digimon's digivolution cards$", I) is { Success: true } sm && (srcPick = ParseCardPick(sm.Groups["p"].Value)) == null) return null;
        string nameX = m.Groups["name"].Value.Trim('[', ']');
        return c =>
        {
            // (효과를 발휘하는 디지몬, 효과를 가진 카드) 후보
            var cands = new List<(CardInstance? Host, CardData Card)>();
            CardInstance? self = c.Self != null && c.Owner.BattleArea.Contains(c.Self) ? c.Self : null;
            CardInstance? that = c.LastTargets.FirstOrDefault(x => c.Owner.BattleArea.Contains(x)) ?? (c.Other != null && c.Owner.BattleArea.Contains(c.Other) ? c.Other : null);
            if (who == "this digimon's") { if (self != null) cands.Add((self, self.TopCard)); }
            else if (who is "its" or "that digimon's") { if (that != null) cands.Add((that, that.TopCard)); }
            else if (who is "that card's" or "the placed card's" || what.Equals("that card", StringComparison.OrdinalIgnoreCase) || what.Equals("the placed card", StringComparison.OrdinalIgnoreCase))
            { if (c.LastCard != null) cands.Add((self, c.LastCard)); }
            else if (nameX.Length > 0)
            {
                foreach (var t in c.Owner.Tamers.Distinct().Where(t => CardNames.Is(t, nameX))) cands.Add((null, t));
                foreach (var d in c.Owner.BattleArea.Where(d => CardNames.Is(d.TopCard, nameX))) cands.Add((d, d.TopCard));
            }
            else if (what.Equals("the Digimon this effect played", StringComparison.OrdinalIgnoreCase))
            { foreach (var d in c.PlayedThisEffect.Where(c.Owner.BattleArea.Contains)) cands.Add((d, d.TopCard)); }
            else if (ownSpec != null)
            { foreach (var d in c.Owner.BattleArea.Where(d => ownSpec.Filters.All(f => f(d)) && ownSpec.ContextFilters.All(f => f(c, d)))) cands.Add((d, d.TopCard)); }
            else if (srcPick != null && self != null)
            { foreach (var x in self.FaceUpSources.Where(srcPick.Value.Match).Distinct()) cands.Add((self, x)); }
            cands = cands.Where(x => GoldfishEngine.HasEffectsFor41(x.Card, trigSet)).ToList();
            if (cands.Count == 0) { c.LastStepResolved = false; Emit(c, label, "발휘할 효과가 없습니다"); return; }
            if (m.Groups["may"].Success && !c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 효과를 발휘할까요?", recommended: true)) { c.LastStepResolved = false; return; }
            int pickIdx = cands.Count == 1 ? 0 : c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 효과를 발휘할 카드를 고르세요.", cands.Select(x => x.Card.Name).ToList(), 0);
            var (host, card) = cands[Math.Clamp(pickIdx, 0, cands.Count - 1)];
            int ran = c.Engine.ActivateEffectsAs41(c.Owner, host, card, trigSet, one);
            if (ran > 0) Diag41.Hit(ref Diag41.ActivateEffect);
            Emit(c, label, ran > 0 ? $"[{card.Name}]의 효과를 발휘했습니다" : "발휘할 효과가 없습니다");
            if (ran == 0) c.LastStepResolved = false;
        };
    }
}
