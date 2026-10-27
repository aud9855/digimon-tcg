using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.45.0 19차 묶음 — 조건 (원래 규칙이 읽지 못한 것만, Gate45):
///  - 앞 문장의 결과: "If you trash a purple card" (BT9-076) · "If this effect trashed a card with the [Seven Great Demon Lords] trait" (EX5-069 · P-212) ·
///    "If you trash 4 or more cards with this effect" (BT8-111) · "If you return 3 or more cards using this effect" (BT8-065) · "If you place 2 cards with this effect" (BT6-075)
///    — RunStep이 문장마다 기록한 트래시 · 덱 · 진화원 변화 (EffectContext.LastStep*45)
///  - "If there are 4 or more total [Negamon] in your trash and your Digimon's digivolution cards" (EX9-055)
///  - "If a level 3 Digimon card isn't in this Digimon's digivolution cards" (BT18-024 "If there isn't" — ClauseRules45가 앞 조건의 부정으로 바꾼다)
///  - 필드 조건: "your opponent has a Digimon with a digivolution card in play" (BT8-029) · "you have a [Mother D-Reaper] with 6 or more digivolution cards in play" (EX2-054)
/// </summary>
internal static partial class SentenceParser
{
    private static Func<EffectContext, bool>? ParseEventCondition45(string cond)
    {
        if (!Gate45) return null;
        string s = cond.Trim();
        Match m;
        if ((m = Regex.Match(s, @"^you trash an? (?<c>red|blue|yellow|green|black|purple|white) card$", I)).Success)
        {
            var color = Enum.Parse<CardColor>(m.Groups["c"].Value, true);
            return c => c.LastStepTrashed45.Any(x => x.AllColors.Contains(color)) && Diag45.HitTrue("condTrashedColor");
        }
        if ((m = Regex.Match(s, @"^this effect trashed a card with the (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) traits?$", I)).Success)
        {
            var xs = Regex.Matches(m.Groups["x"].Value, @"\[([^\]]+)\]").Select(g => g.Groups[1].Value).ToList();
            return c => c.EffectTrashStart45 >= 0 && c.Owner.Trash.Count > c.EffectTrashStart45
                        && c.Owner.Trash.Skip(c.EffectTrashStart45).Any(x => xs.Any(t => TargetSpec.HasTrait(x, t))) && Diag45.HitTrue("condEffectTrashedTrait");
        }
        if ((m = Regex.Match(s, @"^you (?<v>trash|return|place) (?<n>\d+)(?<more> or more)? cards (?:with|using|by) this effect$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            string v = m.Groups["v"].Value.ToLowerInvariant();
            Func<EffectContext, int> got = v switch
            {
                "trash" => c => c.LastStepTrashed45.Count,
                "return" => c => Math.Max(0, c.LastStepDeckDelta45),
                _ => c => Math.Max(0, c.LastStepSourcesDelta45),
            };
            return c => got(c) >= n && Diag45.HitTrue("condStepCount");
        }
        if ((m = Regex.Match(s, @"^there are (?<n>\d+) or more total (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) in your trash and your Digimon's digivolution cards$", I)).Success
            && ParseCounter($"{m.Groups["x"].Value} in your trash or your Digimon's digivolution cards") is { } counter)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return c => counter(c.Owner, c.Enemy, c.Self) >= n && Diag45.HitTrue("condTotalCount");
        }
        if ((m = Regex.Match(s, @"^(?<x>an? .+? card) isn't in this Digimon's digivolution cards$", I)).Success
            && ParseEventCondition($"{m.Groups["x"].Value} is in this Digimon's digivolution cards") is { } pos)
            return c => !pos(c) && Diag45.HitTrue("condNotInSources");
        return null;
    }

    private static Func<PlayerZones, PlayerZones, bool>? ParseSideCondition45(string cond)
    {
        if (!Gate45) return null;
        Match m;
        // "your opponent has a Digimon with a digivolution card in play" (BT8-029)
        if ((m = Regex.Match(cond.Trim(), @"^(?<who>you have|your opponent has) an? Digimon with (?:a |any )?digivolution cards?(?: in play)?$", I)).Success)
        {
            bool opp = m.Groups["who"].Value.StartsWith("your", StringComparison.OrdinalIgnoreCase);
            return (o, e) => (opp ? e : o).BattleArea.Any(d => d.TamerProxyOf == null && d.EvolutionSources.Count > 0);
        }
        // "you have a [Mother D-Reaper] with 6 or more digivolution cards in play" (EX2-054)
        if ((m = Regex.Match(cond.Trim(), @"^(?<who>you have|your opponent has) an? (?<x>\[[^\]]+\]) with (?<n>\d+) or more digivolution cards(?: in play)?$", I)).Success)
        {
            bool opp = m.Groups["who"].Value.StartsWith("your", StringComparison.OrdinalIgnoreCase);
            var names = TargetSpec.Names(m.Groups["x"].Value);
            int n = int.Parse(m.Groups["n"].Value);
            return (o, e) => (opp ? e : o).BattleArea.Any(d => names.Any(x => CardNames.Is(d.TopCard, x)) && d.EvolutionSources.Count >= n);
        }
        return null;
    }
}
