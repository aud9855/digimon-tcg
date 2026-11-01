using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.74.0 미지원 문장 48차 묶음 (엔진):
///  - 메인 페이즈의 행동 「배틀 에어리어의 카드의 링크」 (종합 룰 6-5-1-4 · 10-1-1 — "패나 배틀 에어리어에서 배틀 에어리어의 디지몬에게 코스트를 지불하고 링크할 수 있습니다").
///    링크 조건을 만족하는 자신의 다른 디지몬을 고르고 (10-1-3-1), 링크 코스트를 지불하고 (10-1-3-2), 가로로 끼운다 (10-1-3-3).
///    링크 카드는 에어리어에 속하지 않으므로 (4-9-4) 그 디지몬은 배틀 에어리어를 떠나고, 겹쳐져 있던 카드 · 가진 링크 카드는 파기 (4-7-8 — LinkFieldDigimon73과 같다)
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>플레이어 메인 페이즈: 배틀 에어리어의 moving(맨 위 카드)을 링크할 수 있는 자신의 다른 디지몬 (화면이 쓴다)</summary>
    public IReadOnlyList<CardInstance> GetFieldLinkTargets(CardInstance moving)
    {
        if (State.IsGameOver || State.CurrentPhase != Phase.Main || State.PendingBlock != null || State.BlitzAttacker != null
            || !State.Player.BattleArea.Contains(moving) || IsToken(moving.TopCard) || LinkCostOf(moving.TopCard) is null)
            return Array.Empty<CardInstance>();
        return State.Player.BattleArea.Where(d => !ReferenceEquals(d, moving) && !IsToken(d.TopCard) && MeetsLinkRequirement(moving.TopCard, d) == true).ToList();
    }

    /// <summary>배틀 에어리어의 moving을 target에 링크할 때 낼 코스트 (화면 표시용 — 링크할 수 없으면 null)</summary>
    public int? GetFieldLinkCost(CardInstance moving, CardInstance target) =>
        LinkCostOf(moving.TopCard) is int cost && GetFieldLinkTargets(moving).Any(t => ReferenceEquals(t, target))
            ? ReduceLinkCost41(State.Player, target, moving.TopCard, cost, apply: false)
            : null;

    /// <summary>
    /// 플레이어: 배틀 에어리어의 moving을 링크 코스트를 내고 target에 링크한다 (메인 페이즈 — 종합 룰 6-5-1-4).
    /// "떠나려 할 때" 효과로 막혔으면 링크되지 않는다 (코스트는 이미 냈다 — 10-1-3-2 다음이 10-1-3-3). 메모리가 0 미만이 되면 턴이 끝난다
    /// </summary>
    public ActionResult LinkFromField(CardInstance moving, CardInstance target)
    {
        if (State.IsGameOver || State.CurrentPhase != Phase.Main || State.PendingBlock != null || State.BlitzAttacker != null)
            return ActionResult.Fail("지금은 링크할 수 없습니다.");
        if (!State.Player.BattleArea.Contains(moving) || !State.Player.BattleArea.Contains(target) || ReferenceEquals(moving, target))
            return ActionResult.Fail("링크할 디지몬이 없습니다.");
        var card = moving.TopCard;
        if (LinkCostOf(card) is not int cost)
            return ActionResult.Fail($"[{card.Name}]은(는) 링크 조건이 없습니다.");
        if (!GetFieldLinkTargets(moving).Any(t => ReferenceEquals(t, target)))
            return ActionResult.Fail($"[{target.TopCard.Name}]에는 [{card.Name}]을(를) 링크할 수 없습니다 (링크 조건).");
        cost = ReduceLinkCost41(State.Player, target, card, cost, apply: true);   // v0.41.0 링크 코스트 감소 (BT25-004 · BT25-045)
        State.MemoryGauge -= cost;
        State.TotalMemorySpent += cost;
        RecalculateFieldBuffs();
        Log($"링크 (배틀 에어리어): [{card.Name}] → [{target.TopCard.Name}] (코스트 {cost})");
        if (!LinkFieldDigimon73(State.Player, moving, target))
        {
            CheckTurnEndAndAutoReset();
            return ActionResult.Fail($"[{card.Name}]이(가) 배틀 에어리어를 떠나지 않아 링크하지 못했습니다 (코스트 {cost}는 냈습니다).");
        }
        CheckTurnEndAndAutoReset();
        return ActionResult.Success($"[{card.Name}]을(를) [{target.TopCard.Name}]에 링크했습니다 (코스트 {cost} · 진화원은 트래시).");
    }

    // =====================================================================
    // BT5-109 "At the end of the turn, return the Digimon that digivolved with this effect to the bottom of its owner's deck"
    //  — 공식 한글 BT5-109와 같은 뜻 (이 효과로 진화한 디지몬을 이 턴 종료 시 덱 아래로, 그 진화원은 파기)
    // =====================================================================

    /// <summary>턴 종료 시 되돌릴 "다음 진화" 감소 (효과 → 건 턴)</summary>
    private readonly Dictionary<EvoCostModifier, int> _evoModReturn74 = new(ReferenceEqualityComparer.Instance);

    /// <summary>덱 아래로 되돌릴 때 진화원은 트래시로 (BottomDeck)</summary>
    private readonly HashSet<CardInstance> _trashSourcesOnBottom74 = new(ReferenceEqualityComparer.Instance);

    /// <summary>card가 이 턴에 건 "다음 진화" 감소로 진화한 디지몬을 턴 종료 시 덱 아래로 (건 감소가 없으면 false)</summary>
    internal bool MarkTurnEvoReturn74(PlayerZones owner, CardData card)
    {
        var mods = _turnEvoMods41.Where(x => ReferenceEquals(x.Owner, owner) && ReferenceEquals(x.Card, card) && x.Turn == State.TurnSequence).Select(x => x.Mod).ToList();
        foreach (var m in mods) _evoModReturn74[m] = State.TurnSequence;
        return mods.Count > 0;
    }

    /// <summary>"다음 진화" 감소를 썼다 — 되돌리기로 한 감소면 그 디지몬을 턴 종료 시 덱 아래로</summary>
    private void TurnEvoUsed74(PlayerZones owner, EvoCostModifier mod, CardInstance target)
    {
        if (!_evoModReturn74.Remove(mod, out int turn) || turn != State.TurnSequence) return;
        _trashSourcesOnBottom74.Add(target);
        ScheduleReturnAtTurnEnd42(target, owner, State.TurnSequence, toHand: false);
        Diag46.Hit("evoReturnBottom74");
    }
}
