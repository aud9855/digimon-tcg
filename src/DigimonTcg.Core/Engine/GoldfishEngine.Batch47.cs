using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.47.0 [육성]【자신의 메인 페이즈 개시 시】 디지타마 덱 (육성 에어리어의 디지몬 = c.Self — RunBreedingEffects42):
///  - BT13-007 "Reveal the top card of your Digi-Egg deck, then place that card and all of your [Royal Knight] trait Digimon as this Digimon's bottom digivolution cards."
///    (아래에 놓이는 디지몬은 맨 위 카드만 놓이고 그 진화원은 파기된다: 종합 룰 4-7-7 · 4-7-8 · "떠나려 할 때" 효과로 막을 수 있다)
///  - BT22-007 "Look at your Digi-Egg deck's top card. Among them, you may place [Mother Eater]s as this Digimon's top digivolution cards. Then, if this Digimon has 10 or more
///    digivolution cards, you may play 3 [Mother Eater]s from its digivolution cards without paying the costs." — 확인만 하므로 놓지 않은 카드는 디지타마 덱 위에 그대로
///  (공식 한글 문구로 구조를 확인함 — 한글 원문은 로컬 데이터에만 둔다)
/// </summary>
public partial class GoldfishEngine
{
    internal void BreedingRevealAndPlaceUnder47(EffectContext c, Func<CardData, bool> digimonMatch)
    {
        if (c.Self is not { } host || !ReferenceEquals(c.Owner.BreedingArea, host)) return;
        if (c.Owner.DigitamaDeck.Count > 0)
        {
            var egg = c.Owner.DigitamaDeck.Pop();
            host.AddBottomSource(egg);
            EffectLog(c.Owner, $"디지타마 덱 위의 [{egg.Name}]을(를) 공개하고 [{host.TopCard.Name}]의 진화원 아래에 놓았습니다.");
        }
        else EffectLog(c.Owner, "디지타마 덱이 비어 있어 공개할 카드가 없습니다.");
        int placed = 0;
        foreach (var d in c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && digimonMatch(d.TopCard)).ToList())
            if (PlaceDigimonUnder(c.Owner, d, host, canPrevent: true)) placed++;
        if (placed > 0) EffectLog(c.Owner, $"디지몬 {placed}마리를 [{host.TopCard.Name}]의 진화원 아래에 놓았습니다.");
        Diag46.Hit("breedingReveal47");
    }

    internal void BreedingLookPlaceThenPlay47(EffectContext c, Func<CardData, bool> match, int needSources, int playCount)
    {
        if (c.Self is not { } host || !ReferenceEquals(c.Owner.BreedingArea, host)) return;
        if (c.Owner.DigitamaDeck.Count > 0)
        {
            var top = c.Owner.DigitamaDeck.Peek();
            if (match(top) && AskYesNo(c.Owner, host, $"디지타마 덱 위의 [{top.Name}]을(를) [{host.TopCard.Name}]의 진화원 위에 놓을까요?", recommended: true))
            {
                c.Owner.DigitamaDeck.Pop();
                host.AddTopSource(top);
                EffectLog(c.Owner, $"디지타마 덱 위의 [{top.Name}]을(를) [{host.TopCard.Name}]의 진화원 위에 놓았습니다.");
            }
            else EffectLog(c.Owner, "디지타마 덱 위의 카드를 확인하고 그대로 둡니다.");
        }
        if (host.EvolutionSources.Count < needSources || !ReferenceEquals(c.Owner.BreedingArea, host)) return;
        var pool = host.FaceUpSources.Where(x => x.Type is CardType.Digimon or CardType.Digitama && match(x)).Take(playCount).ToList();
        if (pool.Count == 0 || !AskYesNo(c.Owner, host, $"[{host.TopCard.Name}]의 진화원에서 [{pool[0].Name}] {pool.Count}장을 코스트 없이 등장시킬까요?", recommended: true)) return;
        foreach (var x in pool)
        {
            if (State.IsGameOver) break;
            TryPlayByEffect(c.Owner, x, PlayOrigin.Sources, () => host.TakeSource(s => ReferenceEquals(s, x)) != null);
        }
        Diag46.Hit("breedingLookPlay47");
    }
}
