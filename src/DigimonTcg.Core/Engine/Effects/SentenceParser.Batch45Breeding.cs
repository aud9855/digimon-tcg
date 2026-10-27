using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.45.0 [육성] 효과 (원래 규칙이 읽지 못한 절만 — ExtraClause45):
///  - [육성] [메인] 기동 효과: "[Breeding] [Main] [Once Per Turn] You may play 1 Digimon card with [Negamon] in its text from your hand with the play cost reduced by 2.
///    For each [Negamon] in your trash or your Digimon's digivolution cards, further reduce it by 1. Then, place this Digimon as the played Digimon's bottom digivolution card." (EX9-005 —
///    공식 "{육성}【메인】[턴 1회]자신의 패에서 「네가몬」이 기술되어 있는 디지몬 카드 1장을 지불하는 코스트 -2 하여 등장시킬 수 있다. 자신의 트래시와 디지몬의 진화원에 있는 「네가몬」 합계 1장마다
///    이 효과의 코스트 경감 수치 -1. 그 후, 이 디지몬을 등장한 디지몬의 진화원 아래에 놓는다") — 엔진: GoldfishEngine.Batch45 (GetMainAbilities · UseBreedingMain45)
///  - "[Breeding] [All Turns] This Digimon can't digivolve and effects can't delete or trash it." (EX9-005 — 디지타마 카드는 원래 효과로 소멸 · 파기되는 일이 없다: 진화 금지만 등록)
///  - "[Breeding] [All Turns] All of your [Mother Eater]s in the battle area are treated as having 16000 DP." (BT22-007 — 공식 "배틀 에어리어의 자신의 「마더 이터」 전부는 DP 16000으로 취급한다")
/// </summary>
internal static partial class SentenceParser
{
    private static bool BreedingClause45(string c0, bool inherited, CardData card, CardScriptDefinition? def)
    {
        if (inherited || card.Type is not (CardType.Digimon or CardType.Digitama)) return false;
        Match m;
        if ((m = Regex.Match(c0, @"^\[Breeding\]\s*\[Main\]\s*(?<once>\[Once Per Turn\]\s*)?You may play 1 (?<p>Digimon card with \[[^\]]+\] in its text) from your hand with the play cost reduced by (?<n>\d+)\. For each (?<counter>[^,]+), further reduce it by (?<k>\d+)\. Then, place this Digimon as the played Digimon's bottom digivolution card\.?$", I)).Success
            && RevealPick45(m.Groups["p"].Value) is { } pick && ParseCounter(m.Groups["counter"].Value) is { } counter)
        {
            int n = int.Parse(m.Groups["n"].Value), k = int.Parse(m.Groups["k"].Value);
            var match = pick.Match;
            if (def != null)
                def.Script41.Effects.Add(new CardEffect(EffectTrigger.ActivateMain, EffectSource.Breeding42, c =>
                {
                    var b = c.Self;
                    if (b == null || !ReferenceEquals(c.Owner.BreedingArea, b)) return;
                    int reduce = n + k * Math.Min(counter(c.Owner, c.Enemy, b), 50);
                    var pool = c.Owner.Hand.Where(x => x.Type == CardType.Digimon && match(x)).Distinct()
                        .Where(x => Math.Max(0, x.PlayCost - reduce) <= Math.Max(0, c.OwnerMemory) + 10).OrderByDescending(x => x.PlayCost).ToList();
                    if (pool.Count == 0 || c.Owner.BattleArea.Count >= 12) { c.LastStepResolved = false; return; }
                    var ks = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Hand, pool, 1, true, $"[{c.Card.Name}] 등장시킬 카드를 고르세요 (지불하는 코스트 -{reduce}).");
                    if (ks.Count == 0) { c.LastStepResolved = false; return; }
                    var x = pool[ks[0]];
                    c.StepsRan = true;
                    if (!c.Engine.TryPlayByEffect(c.Owner, x, PlayOrigin.Hand, () => c.Owner.Hand.Remove(x), payCost: Math.Max(0, x.PlayCost - reduce))) { c.LastStepResolved = false; return; }
                    c.AnyStepResolved = true;
                    Diag45.Hit("breedingMainPlay");
                    var played = c.Owner.BattleArea.LastOrDefault(d => ReferenceEquals(d.TopCard, x));
                    if (played != null && ReferenceEquals(c.Owner.BreedingArea, b) && c.Engine.PlaceBreedingUnder45(c.Owner, played))
                        c.Log($"[{c.Card.Name}] 이 디지몬을 [{x.Name}]의 진화원 아래에 놓았습니다.");
                }) { OncePerTurn = m.Groups["once"].Success });
            return true;
        }
        if (card.Type == CardType.Digitama && Regex.IsMatch(c0, @"^\[Breeding\]\s*\[All Turns\]\s*This Digimon can't digivolve and effects can't delete or trash it\.?$", I))
        {
            def?.Keyword(Keyword.CannotDigivolve);
            return true;
        }
        if ((m = Regex.Match(c0, @"^\[Breeding\]\s*\[All Turns\]\s*All of your (?<x>\[[^\]]+\])s? in the battle area are treated as having (?<dp>\d+) DP\.?$", I)).Success)
        {
            var names = TargetSpec.Names(m.Groups["x"].Value);
            int dp = int.Parse(m.Groups["dp"].Value);
            if (def != null)
                def.Script41.Statics.Add(new StaticEffect(StaticStat.AllyDigimonDpSet45, EffectSource.Breeding42, _ => dp)
                {
                    Subject = d => names.Any(nm => d.TopCard.Name.Equals(nm, StringComparison.OrdinalIgnoreCase)),
                });
            return true;
        }
        return false;
    }
}
