using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.44.0 18차 묶음 — 지속 효과 (원래 규칙이 읽지 못한 것만, ParseStatic 끝):
///  - 상대의 디지몬 전부(조건)의 DP: "While [Rapidmon] or [X Antibody] is in this Digimon's digivolution cards, all of your opponent's suspended Digimon get -4000 DP"
///    (BT16-101 — 공식 "이 디지몬의 진화원에 「라피드몬」/「X항체」가 있는 동안, 레스트 상태인 상대의 디지몬 전부를 DP -4000" — StaticStat.EnemyDigimonDp44)
/// </summary>
internal static partial class SentenceParser
{
    private static bool ParseStatic44(string body, bool inherited, CardData card, Qualifier qualifier, Pending pending, Func<StaticContext, bool>? outer)
    {
        if (!Gate44) return false;   // 18차 규칙은 원래 규칙이 읽지 못한 절을 다시 읽을 때만
        var sentences = SplitSentences(body);
        if (sentences.Count != 1) return false;
        string s = sentences[0].Trim().TrimEnd('.');
        Func<StaticContext, bool> turn = qualifier switch
        {
            Qualifier.YourTurn => c => c.OwnersTurn,
            Qualifier.OpponentsTurn => c => !c.OwnersTurn,
            _ => outer ?? (_ => true),
        };
        Func<StaticContext, bool> cond = turn;
        Match m;
        // 두 조건: "While A, or B, this Digimon gains ＜Blocker＞" (BT7-062 · EX3-033 — 둘 중 하나) — 뒤 문장은 원래 규칙으로
        if ((m = Regex.Match(s, @"^While (?<a>[^,]+), (?<op>or|and) (?<b>[^,]+), (?<rest>(?:this Digimon|it|all of your) .+)$", I)).Success
            && ParseStaticCondition(m.Groups["a"].Value) is { } sa && ParseStaticCondition(m.Groups["b"].Value) is { } sb)
        {
            bool or = m.Groups["op"].Value.Equals("or", StringComparison.OrdinalIgnoreCase);
            Func<StaticContext, bool> both = or ? c => turn(c) && (sa(c) || sb(c)) : c => turn(c) && sa(c) && sb(c);
            var trial = new Pending();
            if (ParseStaticCore(Cap44(m.Groups["rest"].Value) + ".", inherited, card, Qualifier.None, trial, both) != null) return false;
            pending.Registrations.AddRange(trial.Registrations);
            return true;
        }
        // 상대의 디지몬 제한 (조건): "Your opponent's Digimon with no digivolution cards can't attack or block" (BT5-032) ·
        //  "While a card with the [Hybrid] trait is in this Digimon's digivolution cards, none of your opponent's level 3 Digimon can attack" (BT7-024)
        if ((m = Regex.Match(s, @"^(?:(?:While|If) (?<cond>.+?), )?(?:none of your opponent's (?<t>.+?) can|your opponent's (?<t>.+?) can't) (?<v>attack players|attack or block|attack|block|suspend)$", I)).Success
            && card.Type is CardType.Digimon or CardType.Tamer
            && TargetSpec.Parse("all of your opponent's " + m.Groups["t"].Value) is { Opponent: true, AnySide: false, IsThis: false, IsThat: false, ContextFilters.Count: 0 } es)
        {
            Func<StaticContext, bool> c2 = cond;
            if (m.Groups["cond"].Success)
            {
                if (ParseStaticCondition(m.Groups["cond"].Value) is not { } pc) return false;
                c2 = c => turn(c) && pc(c);
            }
            var kws = m.Groups["v"].Value.ToLowerInvariant() switch
            {
                "attack players" => new[] { Keyword.CannotAttackPlayers },
                "attack or block" => new[] { Keyword.CannotAttack, Keyword.CannotBlock },
                "attack" => new[] { Keyword.CannotAttack },
                "block" => new[] { Keyword.CannotBlock },
                _ => new[] { Keyword.CannotSuspend },
            };
            var fs = es.Filters.ToList();
            var src = card.Type == CardType.Tamer ? EffectSource.Main : inherited ? EffectSource.Inherited : EffectSource.Main;
            foreach (var kw in kws)
            {
                var k = kw;
                pending.Registrations.Add(d => d.Static(StaticStat.EnemyKeyword, src, c => c2(c) ? 1 : 0, k, subject: x => fs.All(f => f(x))));
            }
            return true;
        }
        if ((m = Regex.Match(s, @"^(?:While|If) (?<cond>.+?), (?<rest>all of your opponent's .+)$", I)).Success)
        {
            if (ParseStaticCondition(m.Groups["cond"].Value) is not { } parsed) return false;
            cond = c => turn(c) && parsed(c);
            s = m.Groups["rest"].Value;
        }
        var source = card.Type == CardType.Tamer ? EffectSource.Main : inherited ? EffectSource.Inherited : EffectSource.Main;
        // "all of your opponent's suspended Digimon get -4000 DP"
        if ((m = Regex.Match(s, @"^(?<who>all of your opponent's (?:[^,]*? )?Digimon(?: with [^,]+?)?) gets? (?<sign>[+-])(?<n>\d+) DP$", I)).Success
            && !Regex.IsMatch(m.Groups["who"].Value, @"Security Digimon", I)
            && TargetSpec.Parse(m.Groups["who"].Value) is { Opponent: true, AnySide: false, IsThis: false, IsThat: false, ContextFilters.Count: 0 } spec)
        {
            int dp = int.Parse(m.Groups["n"].Value) * (m.Groups["sign"].Value == "-" ? -1 : 1);
            var fs = spec.Filters.ToList();
            var c2 = cond;
            pending.Registrations.Add(d => d.Static(StaticStat.EnemyDigimonDp44, source, c =>
            {
                if (!c2(c)) return 0;
                Diag44.Hit("enemyDigimonDpStatic");
                return dp;
            }, subject: x => fs.All(f => f(x))));
            return true;
        }
        return false;
    }
}
