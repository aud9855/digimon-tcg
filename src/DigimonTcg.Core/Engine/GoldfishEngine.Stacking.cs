using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.33.0 디지몬 · 테이머를 다른 디지몬의 진화원으로 놓기 · 디지몬을 시큐리티에 놓기 · 테이머가 떠나려 할 때 ·
/// 디지몬이 배틀 에어리어를 떠났을 때 · 패로 되돌아갔을 때.
///  - 디지몬을 진화원으로 놓으면 맨 위 카드만 놓이고, 그 디지몬의 진화원은 트래시 (종합 룰 4-7-7 · 4-7-8 — 디지크로스와 같다)
///  - 배틀 에어리어를 떠나는 것이므로 "떠나려 할 때" 효과가 발휘한다. 비용으로 놓는 것은 막을 수 없다 (코스트 소멸과 같은 규칙)
///  - 링크 카드는 트래시, 토큰은 사라진다. 아래에 놓이는 카드는 ≪오버플로우≫를 처리하지 않는다 (4-19-4)
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>
    /// moving(자신의 디지몬)을 target의 진화원 맨 아래(또는 맨 위)에 놓는다. 막혔거나 놓지 못했으면 false.
    /// canPrevent = "떠나려 할 때" 효과로 막을 수 있는지 (비용이면 false)
    /// </summary>
    internal bool PlaceDigimonUnder(PlayerZones zones, CardInstance moving, CardInstance target, bool canPrevent, bool top = false)
    {
        if (ReferenceEquals(moving, target) || !zones.BattleArea.Contains(moving) || !zones.BattleArea.Contains(target))
            return false;
        if (TryPreventLeave(zones, moving, deletion: false, inBattle: false, canPrevent, LeaveDestination.Other) && canPrevent)
            return false;
        if (!zones.BattleArea.Remove(moving))
            return false;
        TrashLinks(zones, moving);
        // 종합 룰 4-7-7 · 4-7-8: 아래에 놓이는 디지몬(맨 위 카드)은 에어리어를 떠나고, 그 아래에 겹쳐져 있던 카드는 동시에 파기된다
        // (공식 한글 BT7-058 "「데들리액스몬」 1마리의 진화원을 모두 파기하고, 이 디지몬의 진화원 아래에 놓는 것으로" — 디지크로스와 같다. 조그레스 진화만 진화원째)
        var trashed = moving.GetEvolutionStackBottomUp().ToList();
        ApplyOverflow(zones, trashed);   // 4-19-4: 아래에 놓이는 맨 위 카드는 ≪오버플로우≫를 처리하지 않는다 — 트래시로 가는 진화원만
        while (moving.EvolutionSources.Count > 0)
            zones.Trash.Add(moving.EvolutionSources.Pop());
        if (!IsToken(moving.TopCard))
        {
            if (top) target.EvolutionSources.Push(moving.TopCard);   // 진화원 맨 위 (맨 위 카드 바로 아래)
            else target.AddBottomSource(moving.TopCard);
        }
        PurgeTokens(zones);
        NotifyLeftBattleArea(zones, moving);
        RecalculateFieldBuffs();
        return true;
    }

    /// <summary>비용으로 자신의 디지몬을 덱 아래로 (맨 위 카드와 진화원 — "떠나려 할 때" 효과는 발휘하지만 막을 수 없다)</summary>
    internal void BottomDeckAsCost(PlayerZones zones, CardInstance inst)
    {
        TryPreventLeave(zones, inst, deletion: false, inBattle: false, canPrevent: false, LeaveDestination.Deck);
        if (!zones.BattleArea.Remove(inst))
            return;
        ApplyOverflow(zones, inst.GetEvolutionStackBottomUp().Append(inst.TopCard));
        TrashLinks(zones, inst);
        if (!IsToken(inst.TopCard)) zones.MainDeck.Add(inst.TopCard);
        foreach (var s in inst.GetEvolutionStackBottomUp().Reverse())
            zones.MainDeck.Add(s);
        inst.EvolutionSources.Clear();
        PurgeTokens(zones);
        NotifyLeftBattleArea(zones, inst);
    }

    /// <summary>테이머(카드)를 target의 진화원 맨 아래에 놓는다 (테이머가 떠나려 할 때를 알린다). 놓았으면 true</summary>
    internal bool PlaceTamerUnder(PlayerZones zones, CardData tamer, CardInstance target, bool canPrevent)
    {
        if (!zones.Tamers.Contains(tamer) || !zones.BattleArea.Contains(target))
            return false;
        if (TryPreventTamerLeave(zones, tamer, canPrevent, LeaveDestination.Other) && canPrevent)
            return false;
        if (!zones.Tamers.Remove(tamer))
            return false;
        zones.SuspendedTamers.Remove(tamer);
        target.AddBottomSource(tamer);
        ClearUnderTamersIfNoTamer(zones);
        RecalculateFieldBuffs();
        return true;
    }

    /// <summary>
    /// 디지몬을 주인의 시큐리티 위 · 아래에 놓는다 (맨 위 카드만 — 진화원은 트래시). faceUp = 앞면으로.
    /// 막혔거나 놓지 못했으면 false (토큰은 시큐리티에 가지 않고 사라진다)
    /// </summary>
    internal bool PlaceDigimonInSecurity(PlayerZones zones, CardInstance inst, bool top, bool faceUp, bool canPrevent)
    {
        if (TryPreventLeave(zones, inst, deletion: false, inBattle: false, canPrevent, LeaveDestination.Security) && canPrevent)
            return false;
        if (!zones.BattleArea.Remove(inst))
            return false;
        ApplyOverflow(zones, inst.GetEvolutionStackBottomUp().Append(inst.TopCard));
        TrashLinks(zones, inst);
        while (inst.EvolutionSources.Count > 0)
            zones.Trash.Add(inst.EvolutionSources.Pop());
        if (!IsToken(inst.TopCard))
        {
            if (faceUp) PlaceSecurityFaceUp(zones, inst.TopCard, top);
            else
            {
                if (top) zones.SecurityStack.Insert(0, inst.TopCard); else zones.SecurityStack.Add(inst.TopCard);
                NotifySecurityAdded(zones, inst.TopCard);
            }
        }
        PurgeTokens(zones);
        NotifyLeftBattleArea(zones, inst);
        return true;
    }

    /// <summary>
    /// 디지몬의 맨 위 카드(겹쳐진 카드 중 가장 위)를 시큐리티 위 · 아래에 놓는다 — 바로 아래 진화원이 맨 위 카드가 된다.
    /// 진화원이 없으면 하지 않는다 (false)
    /// </summary>
    internal bool PlaceTopCardInSecurity(PlayerZones owner, CardInstance inst, bool top)
    {
        if (inst.EvolutionSources.Count == 0 || inst.TrashTopCard() is not { } card)
            return false;
        if (top) owner.SecurityStack.Insert(0, card); else owner.SecurityStack.Add(card);
        NotifySecurityAdded(owner, card);
        RecalculateFieldBuffs();
        return true;
    }

    /// <summary>테이머를 패로 되돌린다 (떠나려 할 때 · "패로 되돌아갔을 때"). 되돌렸으면 true</summary>
    internal bool ReturnTamerToHand(PlayerZones owner, CardData tamer, bool canPrevent = true)
    {
        if (!owner.Tamers.Contains(tamer))
            return false;
        if (TryPreventTamerLeave(owner, tamer, canPrevent, LeaveDestination.Hand) && canPrevent)
            return false;
        if (!owner.Tamers.Remove(tamer))
            return false;
        owner.SuspendedTamers.Remove(tamer);
        owner.Hand.Add(tamer);
        ClearUnderTamersIfNoTamer(owner);
        RunOrDefer(() =>
        {
            if (State.IsGameOver) return;
            FireForSide(EffectTrigger.AllyTamerReturnedToHand, owner, null, triggerCard: tamer);
        });
        return true;
    }

    /// <summary>
    /// v0.33.0 테이머가 배틀 에어리어를 떠나려 할 때 ("When any of your Digimon or Tamers would leave the battle area"):
    /// 자신의 디지몬 · 테이머 · 놓인 옵션의 <see cref="EffectTrigger.AllyTamerWouldLeave"/> 효과를 발휘하고, 막았으면 true.
    /// CurrentLeave.Digimon은 그 테이머 카드만 든 가짜 디지몬 (IsTamer = true) — 조건이 맨 위 카드(테이머)를 본다
    /// </summary>
    internal bool TryPreventTamerLeave(PlayerZones zones, CardData tamer, bool canPrevent, LeaveDestination destination)
    {
        if (!zones.Tamers.Contains(tamer))
            return false;
        var phantom = new CardInstance(tamer, State.CurrentTurn);
        var outer = CurrentLeave;
        bool deletion = destination == LeaveDestination.Deleted;
        CurrentLeave = new LeaveInfo(phantom, deletion, false, ReferenceEquals(CurrentEffectOwner, zones), canPrevent, destination) { IsTamer = true };
        bool prevented = false;
        try
        {
            foreach (var d in zones.BattleArea.ToList())
            {
                if (prevented) break;
                prevented |= RunEffects(EffectTrigger.AllyTamerWouldLeave, zones, d, d.TopCard, other: phantom).PreventLeave;
            }
            foreach (var card in zones.Tamers.Concat(zones.OptionsInPlay.Select(p => p.Card)).Distinct().ToList())
            {
                if (prevented) break;
                prevented |= RunEffects(EffectTrigger.AllyTamerWouldLeave, zones, null, card, other: phantom, includeInherited: false).PreventLeave;
            }
        }
        finally
        {
            CurrentLeave = outer;
        }
        if (!canPrevent)
            return false;
        if (prevented)
            EffectLog(zones, $"[{tamer.Name}]은(는) 효과로 배틀 에어리어를 떠나지 않습니다.");
        return prevented && zones.Tamers.Contains(tamer);
    }

    /// <summary>
    /// v0.33.0 디지몬이 배틀 에어리어를 떠났을 때 (소멸 · 패 · 덱 · 시큐리티 · 진화원으로) — 양쪽에 알린다 (효과 처리 중이면 끝난 뒤에)
    /// </summary>
    internal void NotifyLeftBattleArea(PlayerZones zones, CardInstance inst)
    {
        RunOrDefer(() =>
        {
            if (State.IsGameOver) return;
            FireForSide(EffectTrigger.AllyDigimonLeft, zones, inst);
            FireForSide(EffectTrigger.OpponentDigimonLeft, EnemyOf(zones), inst);
        });
    }

    /// <summary>v0.33.0 디지몬이 패로 되돌아갔을 때 ("When another Digimon is returned to the hand") — 양쪽에</summary>
    private void NotifyReturnedToHand(PlayerZones zones, CardInstance inst)
    {
        RunOrDefer(() =>
        {
            if (State.IsGameOver) return;
            FireForSide(EffectTrigger.DigimonReturnedToHand, zones, inst);
            FireForSide(EffectTrigger.DigimonReturnedToHand, EnemyOf(zones), inst);
        });
    }

    /// <summary>
    /// v0.33.0 효과로 테이머를 진화시킨다 (하이브리드체 [패] [메인] "that Tamer digivolves into this card for digivolution cost of 3, ignoring its digivolution requirements" ·
    /// "Digivolve it into this card as if that card is a level 4 red Digimon") — 테이머에서 진화와 같은 처리 (공식 Q&A BT4-011) + [진화 시] · 진화했을 때.
    /// 진화했으면 그 디지몬, 아니면 null
    /// </summary>
    internal CardInstance? EvolveTamerByEffect(PlayerZones owner, CardData tamer, CardData evoCard, int cost, List<CardData>? from = null)
    {
        from ??= owner.Hand;
        if (!owner.Tamers.Contains(tamer) || !from.Contains(evoCard) || evoCard.Type != CardType.Digimon)
            return null;
        if (!ReferenceEquals(from, owner.Hand)) from.Remove(evoCard);   // 트래시의 카드로 (EvolveTamerCore는 패에서만 뺀다)
        var inst = EvolveTamerCore(owner, tamer, evoCard, cost, out int paid);
        if (ReferenceEquals(owner, State.Player)) { State.TotalMemorySpent += paid; State.TotalEvolutionsCount++; }
        EffectLog(owner, $"[효과로 진화] 테이머 [{tamer.Name}] → [{evoCard.Name}(Lv.{evoCard.Level})] (코스트 {paid})");
        if (owner.BattleArea.Contains(inst))
        {
            _whenDigivolvingByEffect++;
            try { ApplyWhenDigivolvingEffects(owner, inst, evoCard); }
            finally { _whenDigivolvingByEffect--; }
            _digivolvingByEffect++;
            try { FireForSide(EffectTrigger.AllyDigimonDigivolved, owner, inst); }
            finally { _digivolvingByEffect--; }
        }
        return inst;
    }

    /// <summary>v0.33.0 지금 알리는 "트래시에서 패 · 덱으로" 옮긴 카드</summary>
    internal IReadOnlyList<CardData> CurrentMovedCards { get; private set; } = new List<CardData>();

    /// <summary>v0.33.0 트래시의 카드가 효과로 패 / 덱으로 되돌아갔을 때 (효과 처리 중이면 끝난 뒤에)</summary>
    internal void NotifyTrashMoved(PlayerZones zones, IReadOnlyList<CardData> cards, bool toHand)
    {
        if (cards.Count == 0) return;
        var moved = cards.ToList();
        RunOrDefer(() =>
        {
            if (State.IsGameOver) return;
            var outer = CurrentMovedCards;
            CurrentMovedCards = moved;
            try { FireForSide(toHand ? EffectTrigger.AllyTrashToHand : EffectTrigger.AllyTrashToDeck, zones, null, triggerCard: moved[0]); }
            finally { CurrentMovedCards = outer; }
        });
    }
}
