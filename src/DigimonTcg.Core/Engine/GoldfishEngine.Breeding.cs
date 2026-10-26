using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// 육성 · 디지타마 (효과 엔진 7단계): 부화 · 육성 에어리어 → 배틀 에어리어 이동 · 효과로 육성 에어리어에 등장 · 디지타마 덱 아래로.
/// - 부화하면 "When you hatch in the breeding area"(AllyHatched)
/// - 이동하면 [이동 시](WhenMoving) → 양쪽의 "When ~ Digimon moves from the breeding area to the battle area"
/// - 효과로 육성 에어리어에 등장한 디지몬은 【등장 시】가 유발되지 않고 부화도 아니다 (공식 Q&amp;A BT23-084 Q1 · BT15-068 Q1)
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>디지타마 덱 위 1장을 비어 있는 육성 에어리어에 부화한다 (육성 페이즈 · 효과 공용). 반환: 부화한 카드</summary>
    internal CardData? Hatch(PlayerZones owner)
    {
        if (owner.BreedingArea != null || owner.DigitamaDeck.Count == 0)
            return null;
        var egg = owner.DigitamaDeck.Pop();
        owner.BreedingArea = new CardInstance(egg, State.CurrentTurn) { IsOwnersTurn = IsTurnOf(owner) };
        FireForSide(EffectTrigger.AllyHatched, owner, owner.BreedingArea, triggerCard: egg);
        return egg;
    }

    /// <summary>육성 에어리어의 디지몬을 배틀 에어리어로 옮긴다 (육성 페이즈 · 효과 공용 — 소환 멀미 없음). 반환: 옮긴 디지몬</summary>
    internal CardInstance? MoveBreedingToBattle(PlayerZones owner)
    {
        if (owner.BreedingArea is not { } moving || moving.TopCard.Type != CardType.Digimon)
            return null;
        if (EffectRules.MoveDpBlocked(State, owner, moving))   // v0.37.0 "can't play or move Digimon with N DP or less" (EX7-014)
        {
            EffectLog(owner, $"[{moving.TopCard.Name}] 상대의 효과로 배틀 에어리어로 이동할 수 없습니다.");
            return null;
        }
        owner.BreedingArea = null;
        moving.TurnPlayed = Math.Min(moving.TurnPlayed, State.CurrentTurn - 1);
        owner.BattleArea.Add(moving);
        RecalculateFieldBuffs();
        RunEffects(EffectTrigger.WhenMoving, owner, moving, moving.TopCard);
        if (owner.BattleArea.Contains(moving))
        {
            FireForSide(EffectTrigger.AllyDigimonMovedFromBreeding, owner, moving);
            FireForSide(EffectTrigger.OpponentDigimonMovedFromBreeding, EnemyOf(owner), moving);
        }
        return moving;
    }

    /// <summary>
    /// 배틀 에어리어의 디지몬을 비어 있는 육성 에어리어로 옮긴다 ("This Digimon may move to the empty space in your breeding area").
    /// 에어리어를 떠나는 것이 아니므로 ≪오버플로우≫ · 떠날 때 효과는 처리하지 않는다 (공식 Q&amp;A — 육성 에어리어 이동 시 ≪오버플로우≫ 미처리)
    /// </summary>
    internal bool MoveBattleToBreeding(PlayerZones owner, CardInstance digimon)
    {
        if (owner.BreedingArea != null || !owner.BattleArea.Remove(digimon))
            return false;
        owner.BreedingArea = digimon;
        if (ReferenceEquals(CurrentAttacker, digimon)) CurrentAttacker = null;
        RecalculateFieldBuffs();
        return true;
    }

    /// <summary>카드를 비어 있는 육성 에어리어에 효과로 등장시킨다 (【등장 시】 없음 · 부화 아님). 반환: 놓았는지</summary>
    internal bool PlayToBreeding(PlayerZones owner, CardData card)
    {
        if (owner.BreedingArea != null || card.Type != CardType.Digimon)
            return false;
        owner.BreedingArea = new CardInstance(card, State.CurrentTurn) { IsOwnersTurn = IsTurnOf(owner) };
        EffectLog(owner, $"[효과] [{card.Name}(Lv.{card.Level})] 육성 에어리어에 코스트 없이 등장");
        RecalculateFieldBuffs();
        return true;
    }

    /// <summary>카드를 디지타마 덱 아래에 놓는다 (DigitamaDeck은 Stack — 맨 아래 = 마지막에 꺼내는 쪽)</summary>
    internal static void AddToDigitamaDeckBottom(PlayerZones owner, IEnumerable<CardData> cards)
    {
        var order = owner.DigitamaDeck.ToList();   // 위 → 아래
        order.AddRange(cards);
        owner.DigitamaDeck = new Stack<CardData>(Enumerable.Reverse(order));
    }
}
