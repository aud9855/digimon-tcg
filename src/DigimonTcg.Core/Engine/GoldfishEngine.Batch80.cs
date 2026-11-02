using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.80.0 미지원 문장 54차 묶음 — 엔진 쪽.
///  - BT10-077 효과 하나로 상대의 패가 늘어났을 때 (드로우 포함 — 종합 룰 4-14-1) 그 장수 (공식 Q&amp;A BT10-077 Q1 · EX7-013 Q1):
///    효과 하나를 처리한 뒤 쪽마다 1번 (EndSourceWatch — "효과로 패가 늘어났다"와 같은 기준). 이 사건은 BT10-077만 본다
///  - BT16-015 이 디지몬의 【소멸 시】 효과에 【어택 종료 시】를 추가 — 어택 종료 시에 【소멸 시】 효과(메인 · 진화원)도 유발 (공식 Q&amp;A BT16-015 Q1: 조건은 따로 본다)
///  - BT7-112 테이머에서 진화하는 비용: 패 · 트래시의 테이머 카드 · 특징 「하이브리드체」 카드 10장을 원하는 순서대로 덱 아래로 (공식 Q&amp;A BT7-112 Q1 — 진화를 선언한 뒤)
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>지금 알리는 "효과 하나로 상대의 패가 늘어났다"의 장수 (BT10-077 효과가 읽는다)</summary>
    internal int OppHandAddedCount80 { get; private set; }

    /// <summary>효과 하나를 처리한 뒤: zonesOfHand의 패가 count장 늘었으면 그 상대 쪽에 알린다</summary>
    private void FireOppHandAddedCount80(PlayerZones zonesOfHand, int count, CardData? firstAdded)
    {
        if (count <= 0 || State.IsGameOver) return;
        var outer = OppHandAddedCount80;
        OppHandAddedCount80 = count;
        try { FireForSide(EffectTrigger.OpponentHandAddedCount80, EnemyOf(zonesOfHand), null, triggerCard: firstAdded); }
        finally { OppHandAddedCount80 = outer; }
    }

    /// <summary>BT16-015: 어택 종료 시 — 이 디지몬의 【소멸 시】 효과(메인 · 진화원)도 발휘한다 (소멸하지 않았으므로 진화원은 지금의 진화원)</summary>
    private void RunAttachedOnDeletion80(PlayerZones owner, CardInstance attacker)
    {
        if (!owner.BattleArea.Contains(attacker) || State.IsGameOver || !CardScripts.HasKeyword(attacker, Keyword.AttachEndOfAttackToOnDeletion80)) return;
        Diag46.Hit("attachEndOfAttack80");
        EffectLog(owner, $"[{attacker.TopCard.Name}] 【어택 종료 시】 — 【소멸 시】 효과를 발휘합니다 (【어택 종료 시】가 추가된 효과).");
        RunEffects(EffectTrigger.OnDeletion, owner, attacker, attacker.TopCard);
        RecalculateFieldBuffs();
    }

    // ---- BT7-112 테이머에서 진화하는 비용 ----

    /// <summary>evoCard를 테이머에서 진화시키는 비용 (패 · 트래시의 카드를 덱 아래로)을 낼 수 있는지 — 비용이 없는 카드는 true</summary>
    internal bool CanPayTamerEvoPlace80(PlayerZones owner, CardData evoCard) =>
        CardScripts.For(evoCard)?.TamerEvoPlaceToDeck80 is not { } cost
        || owner.Hand.Count(c => !ReferenceEquals(c, evoCard) && cost.Match(c)) + owner.Trash.Count(cost.Match) >= cost.Count;

    /// <summary>비용을 낸다 (고른 순서대로 덱 아래로). 플레이어는 고르고, AI는 트래시 → 패 (등장 코스트가 낮은 것) 순서</summary>
    private void PayTamerEvoPlace80(PlayerZones owner, CardData evoCard)
    {
        if (CardScripts.For(evoCard)?.TamerEvoPlaceToDeck80 is not { } cost) return;
        var pool = owner.Trash.Where(cost.Match).Select(c => (Card: c, Zone: owner.Trash))
            .Concat(owner.Hand.Where(c => !ReferenceEquals(c, evoCard) && cost.Match(c)).OrderBy(c => c.PlayCost).Select(c => (Card: c, Zone: owner.Hand))).ToList();
        List<(CardData Card, List<CardData> Zone)> picked;
        if (ShouldAskPlayer(owner))
        {
            var idx = ChooseCardIndexes(owner, CardChoiceZone.Other, pool.Select(p => p.Card).ToList(), cost.Count, upTo: false,
                $"[{evoCard.Name}] 테이머에서 진화하는 비용 — 패 · 트래시에서 덱 아래로 되돌릴 카드 {cost.Count}장을 고르세요 (고른 순서대로 덱 아래로).");
            picked = idx.Select(i => pool[i]).ToList();
            if (picked.Count < cost.Count) picked = pool.Where(p => !picked.Contains(p)).Take(cost.Count - picked.Count).Concat(picked).ToList();
        }
        else
            picked = pool.Take(cost.Count).ToList();
        foreach (var (card, zone) in picked)
            if (zone.Remove(card)) owner.MainDeck.Add(card);
        Diag46.Hit("tamerEvoPlacePaid80");
        EffectLog(owner, $"[{evoCard.Name}] 패 · 트래시의 카드 {picked.Count}장을 덱 아래로 되돌렸습니다 (테이머에서 진화하는 비용).");
    }
}
