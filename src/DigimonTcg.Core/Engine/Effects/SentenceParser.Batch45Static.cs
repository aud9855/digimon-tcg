using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.45.0 19차 묶음 — 지속 효과 (원래 규칙이 읽지 못한 것만, ParseStatic 끝):
///  - "All other Digimon and Tamers don't unsuspend" (BT12-057 — 공식 "이 디지몬 이외의 디지몬과 테이머 전부는 액티브가 되지 않는다")
///  - "Other than this Digimon, no Digimon or Tamers can unsuspend in the unsuspend phase" (EX10-023 — 공식 "액티브 페이즈에서는, 이 디지몬 이외의 디지몬과 테이머 전부는 액티브가 되지 않는다")
///  - "While you have an [Eosmon] in play, your opponent's Tamers don't unsuspend during their unsuspend phase" (BT6-092)
/// </summary>
internal static partial class SentenceParser
{
    private static bool ParseStatic45(string body, bool inherited, CardData card, Qualifier qualifier, Pending pending, Func<StaticContext, bool>? outer)
    {
        if (!Gate45) return false;   // 19차 규칙은 원래 규칙이 읽지 못한 절을 다시 읽을 때만
        var sentences = SplitSentences(body);
        if (sentences.Count != 1) return false;
        string s = sentences[0].Trim().TrimEnd('.');
        Func<StaticContext, bool> turn = qualifier switch
        {
            Qualifier.YourTurn => c => c.OwnersTurn,
            Qualifier.OpponentsTurn => c => !c.OwnersTurn,
            _ => outer ?? (_ => true),
        };
        var source = inherited ? EffectSource.Inherited : EffectSource.Main;   // 테이머 카드의 진화원 효과(EX11-070)도 진화원 효과
        Match m;
        // 효과를 가진 디지몬과 비교하는 상대 디지몬 제한: "None of your opponent's Digimon with as much or less DP as this Digimon can attack players" (BT18-046 —
        //  공식 "이 디지몬의 DP 이하의 상대의 디지몬 전부는 플레이어에게 어택할 수 없다") · "~ with as many or fewer digivolution cards as this Digimon can suspend" (EX7-023)
        if ((m = Regex.Match(s, @"^None of your opponent's Digimon with (?<c>as much or less DP|as many or fewer digivolution cards) as this Digimon can (?<v>attack players|suspend|attack|block)$", I)).Success
            && card.Type == CardType.Digimon)
        {
            bool byDp = m.Groups["c"].Value.Contains("DP", StringComparison.OrdinalIgnoreCase);
            var kw = m.Groups["v"].Value.ToLowerInvariant() switch
            {
                "attack players" => Keyword.CannotAttackPlayers,
                "attack" => Keyword.CannotAttack,
                "block" => Keyword.CannotBlock,
                _ => Keyword.CannotSuspend,
            };
            var src = inherited ? EffectSource.Inherited : EffectSource.Main;
            var t2 = turn;
            pending.Registrations.Add(d => d.Script41.Statics.Add(new StaticEffect(StaticStat.EnemyKeyword, src, c => t2(c) ? 1 : 0)
            {
                Keyword = kw,
                SubjectOf = (sc, x) => sc.Digimon is { } me && (byDp ? x.GetCurrentDP() <= me.GetCurrentDP() : x.EvolutionSources.Count <= me.EvolutionSources.Count) && Diag45.HitTrue("compareAura"),
            }));
            return true;
        }
        // DP 하한 + 겹쳐진 카드 보호 (조건이 붙은 주어): "This Digimon with [Maquinamon] in its text can't have less than 1000 DP, and your opponent's effects can't trash its stacked cards" (EX11-070 진화원 효과)
        if ((m = Regex.Match(s, @"^This Digimon with (?<x>\[[^\]]+\]) in its text can't have less than (?<n>\d+) DP, and your opponent's effects can't trash its stacked cards$", I)).Success
            && ParseStaticCondition($"this Digimon has {m.Groups["x"].Value} in its text") is { } hasX)
        {
            int n = int.Parse(m.Groups["n"].Value);
            var t2 = turn;
            var trial = new Pending();
            if (ParseStatic($"While this Digimon has {m.Groups["x"].Value} in its text, it can't have its stacked cards trashed by your opponent's effects.", inherited, card, qualifier, trial, outer) != null) return false;
            pending.Registrations.AddRange(trial.Registrations);
            pending.Registrations.Add(d => d.Static(StaticStat.DpFloor45, source, c => t2(c) && hasX(c) && Diag45.HitTrue("dpFloor") ? n : 0));
            return true;
        }
        Func<StaticContext, bool>? pre = null;
        if ((m = Regex.Match(s, @"^While (?<c>[^,]+), (?<rest>.+)$", I)).Success)
        {
            if (ParseStaticCondition(m.Groups["c"].Value) is not { } pc) return false;
            pre = pc;
            s = m.Groups["rest"].Value;
        }
        // 액티브가 되지 않는다
        int bits = 0;
        bool other = false, phaseOnly = false;
        if (Regex.IsMatch(s, @"^All other Digimon and Tamers don't unsuspend$", I)) { bits = 1 | 2 | 4 | 8; other = true; }
        else if (Regex.IsMatch(s, @"^Other than this Digimon, no Digimon or Tamers can unsuspend (?:in|during) the unsuspend phase$", I)) { bits = 1 | 2 | 4 | 8; other = true; phaseOnly = true; }
        else if (Regex.IsMatch(s, @"^your opponent's Tamers don't unsuspend (?:in|during) their unsuspend phase$", I)) { bits = 8; phaseOnly = true; }
        if (bits == 0) return false;
        if (other && card.Type != CardType.Digimon) return false;
        int v = bits | (phaseOnly ? 16 : 0);
        // 상대의 액티브 페이즈에서만 보는 효과는 [상대의 턴] 표시를 따로 보지 않는다 (액티브 페이즈 = 그 플레이어의 턴 — 턴 교대 처리 순서와 무관하게)
        Func<StaticContext, bool> cond = phaseOnly && bits == 8 && qualifier == Qualifier.OpponentsTurn ? (outer ?? (_ => true)) : turn;
        var p2 = pre;
        pending.Registrations.Add(d => d.Static(StaticStat.UnsuspendLock45, source, c => cond(c) && (p2 == null || p2(c)) ? v : 0, excludeSource: other));
        return true;
    }
}
