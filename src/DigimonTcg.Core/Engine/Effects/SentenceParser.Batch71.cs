using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.71.0 미지원 문장 45차 묶음 (원래 규칙(44차까지)이 읽지 못한 절만):
///  - 필드의 디지몬 + 트래시의 카드로 조그레스 진화 (BT18-015 · BT18-073 · EX11-059 — 공식 Q&amp;A BT18-015 Q1: 조그레스 조건은 만족해야 한다)
///  - BT16-091 "The Digimon this effect DNA digivolved may gain ＜Security A. +1＞ for the turn and attack a player" — 이 효과로 조그레스 진화한 디지몬만 (조그레스 진화하지 않았으면 얻지 않는다 — 공식 Q&amp;A BT16-091 Q1: 조그레스 진화하지 않을 수 있다)
/// </summary>
internal static partial class SentenceParser
{
    private static string? TryParse71(string rawClause, bool inherited, CardData card, CardScriptDefinition? def) =>
        TryParse70(rawClause, inherited, card, def);

    /// <summary>한글 표시용: BT16-091 "The Digimon this effect DNA digivolved may gain ~" → "That Digimon may gain ~" (KoreanEffectRenderer.Batch71가 "이 효과로 조그레스 진화한 디지몬"으로 바꾼다)</summary>
    internal static string ClauseRewrite71(string clause) =>
        Regex.Replace(clause, @"\bThe Digimon this effect DNA digivolved may gain\b", "That Digimon may gain", RegexOptions.IgnoreCase);

    // ---- 동작 (원래 규칙이 읽지 못한 것만) ----

    private static (Action<EffectContext>? Act, string? Error)? ParseAction71(string s, CardData card, List<EffectTrigger> triggers)
    {
        s = Regex.Replace(s.Trim().TrimEnd('.'), @"^Then,?\s+", "", RegexOptions.IgnoreCase);
        Match m;
        // BT16-091 "The Digimon this effect DNA digivolved may gain ＜Security A. +1＞ for the turn and attack a player" — 이 효과를 처리하는 동안 조그레스 진화한 디지몬
        if ((m = Regex.Match(s, @"^the Digimon this effect DNA digivolved (?<rest>may gain .+)$", I)).Success && GainAndAttack38("That Digimon " + m.Groups["rest"].Value, card, triggers) is { } gainAtk)
            return (c =>
            {
                if (c.Engine.DnaSerial71 == c.DnaSerialAtStart71 || c.Engine.LastDna71 is not { } r || !c.Owner.BattleArea.Contains(r)) { c.LastStepResolved = false; return; }
                c.LastTargets = new List<CardInstance> { r };
                RunStep(gainAtk, c);
            }, null);
        if ((m = Regex.Match(s, @"^(?:you may )?DNA digivolve 1 of your (?<f>.+?) in play and 1 (?<p>.+?) in (?:the|your) trash into (?<into>.+?) in (?:the|your) hand$", I)).Success
            || (m = Regex.Match(s, @"^1 of your (?<f>.+?) and 1 (?<p>.+?) in (?:the|your) trash may DNA digivolve into (?<into>.+?) in (?:the|your) hand$", I)).Success)
        {
            string f = m.Groups["f"].Value.Trim();
            var spec = TargetSpec.Parse("all of your " + f + (Regex.IsMatch(f, @"\bDigimon$", I) ? "" : " Digimon"));
            if (spec == null || spec.Opponent || spec.ContextFilters.Count > 0) return null;
            string p = m.Groups["p"].Value.Trim();
            if ((ParseCardPick("1 " + p) ?? ParseNamePick("1 " + p)) is not { Count: 1 } partner) return null;
            string into = m.Groups["into"].Value.Trim();
            if ((RevealPick45(into) ?? ParseCardPick("1 " + Regex.Replace(into, @"^an? ", "", I)) ?? ParseNamePick("1 " + into)) is not { } dna) return null;
            var fieldOk = spec.Filters.ToList();
            var partnerOk = partner.Match;
            var intoOk = dna.Match;
            return (c =>
            {
                var r = c.Engine.DnaWithZoneCard71(c.Owner, d => fieldOk.All(x => x(d)), x => x.Type == CardType.Digimon && partnerOk(x), intoOk, c.Card.Name);
                c.LastStepResolved = r != null;
                c.LastTargets = r != null ? new List<CardInstance> { r } : new List<CardInstance>();
            }, null);
        }
        return null;
    }
}
