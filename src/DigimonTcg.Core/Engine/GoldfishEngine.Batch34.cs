using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.34.0 미지원 문장 8차 묶음의 엔진 처리:
/// - "End the attack" — 어택 중인 디지몬의 어택을 배틀 · 시큐리티 체크 없이 끝낸다 (어택 종료 시 효과는 그대로)
/// - 제한 · 보호 키워드 — "can't attack players" · "can't be deleted in battle" · "isn't affected by your opponent's (Digimon's) effects" ·
///   "can't be returned to the hand or deck by your opponent's effects" · "can't have its DP reduced" · "can't activate [When Digivolving] effects"
/// - 상대가 고르는 동작 — "Your opponent may trash 1 card in their hand. If they don't, ~" 등
/// - 등장할 때 카드를 이 카드 아래에 놓는 비용 ("by placing up to 3 ~ under it, reduce the play cost by 4 for each one")
/// </summary>
public partial class GoldfishEngine
{
    // =====================================================================
    // 어택 종료
    // =====================================================================

    // "End the attack"을 받은 어택 중인 디지몬 (어택 선언마다 비운다)
    private CardInstance? _attackEndRequested;

    /// <summary>"End the attack" — 지금 어택 중인 디지몬이 있으면 그 어택을 끝낸다 (반환: 끝냈는지)</summary>
    internal bool EndCurrentAttack()
    {
        if (CurrentAttacker is not { } attacker || !OwnerOf(attacker).BattleArea.Contains(attacker))
            return false;
        _attackEndRequested = attacker;
        return true;
    }

    /// <summary>attacker의 어택이 효과로 끝났는지 (확인하면 지운다)</summary>
    private bool ConsumeAttackEnd(CardInstance attacker)
    {
        if (!ReferenceEquals(_attackEndRequested, attacker))
            return false;
        _attackEndRequested = null;
        return true;
    }

    // =====================================================================
    // 지금 처리 중인 효과가 적힌 카드 ("상대의 디지몬의 효과" · "옵션 카드의 효과"를 받지 않는다)
    // =====================================================================

    internal CardData? CurrentEffectCard { get; set; }

    /// <summary>효과 주인 effectOwner의 지금 효과를 target이 받지 않는지 (v0.34.0 키워드)</summary>
    private bool UnaffectedByKeyword34(PlayerZones effectOwner, CardInstance target)
    {
        var owner = OwnerOf(target);
        if (ReferenceEquals(owner, effectOwner) || !owner.BattleArea.Contains(target))
            return false;
        if (CardScripts.HasKeyword(target, Keyword.UnaffectedByOpponentEffects))
            return true;
        var card = CurrentEffectCard;
        if (card == null)
            return false;
        return (card.Type == CardType.Digimon && !card.IsDual && CardScripts.HasKeyword(target, Keyword.UnaffectedByOpponentDigimonEffects))
            || ((card.Type == CardType.Option || card.IsDual) && CardScripts.HasKeyword(target, Keyword.UnaffectedByOpponentOptionEffects));
    }

    /// <summary>지금 처리 중인 것이 zones에게 상대의 효과인지</summary>
    private bool IsOpponentEffectFor(PlayerZones zones) =>
        _deletingByOpponentEffect || (CurrentEffectOwner != null && !ReferenceEquals(CurrentEffectOwner, zones));

    // =====================================================================
    // 보호
    // =====================================================================

    /// <summary>소멸을 키워드로 막는지 (배틀 · 효과 — DP 0 · 비용 소멸은 막지 않는다)</summary>
    private bool DeletionBlocked34(PlayerZones zones, CardInstance inst, DeletionCause cause)
    {
        if (cause == DeletionCause.Battle && CardScripts.HasKeyword(inst, Keyword.CannotBeDeletedInBattle))
        {
            EffectLog(zones, $"[{inst.TopCard.Name}]은(는) 효과로 배틀에서 소멸하지 않습니다.");
            return true;
        }
        if (cause == DeletionCause.Effect
            && (CardScripts.HasKeyword(inst, Keyword.CannotBeDeletedByEffects)
                || (CardScripts.HasKeyword(inst, Keyword.CannotBeDeletedByOpponentEffects) && IsOpponentEffectFor(zones))))
        {
            EffectLog(zones, $"[{inst.TopCard.Name}]은(는) 효과로 소멸하지 않습니다.");
            return true;
        }
        return false;
    }

    /// <summary>"Your opponent can't gain memory other than by Tamer effects" — side가 효과로 메모리를 얻으려 할 때 상대 필드에 그 디지몬이 있으면 (테이머 효과는 된다)</summary>
    private bool MemoryGainBlocked34(PlayerZones side)
    {
        if (!ReferenceEquals(CurrentEffectOwner, side) || CurrentEffectCard is { Type: CardType.Tamer })
            return false;
        if (!EnemyOf(side).BattleArea.Any(d => CardScripts.HasKeyword(d, Keyword.OpponentMemoryTamerOnly))
            && !State.Player.BattleArea.Concat(State.DummyOpponent.BattleArea).Any(d => CardScripts.HasKeyword(d, Keyword.PlayersMemoryTamerOnly)))   // v0.35.0 "Players can't ~"
            return false;
        EffectLog(side, "상대의 효과로 테이머의 효과 이외로는 메모리를 얻을 수 없습니다.");
        return true;
    }

    /// <summary>패 · 덱으로 되돌리는 것을 키워드로 막는지 (상대의 효과)</summary>
    private bool ReturnBlocked34(PlayerZones zones, CardInstance inst)
    {
        if (!zones.BattleArea.Contains(inst) || CurrentEffectOwner == null)
            return false;
        if (CardScripts.HasKeyword(inst, Keyword.CannotBeReturnedByEffects)
            || (CardScripts.HasKeyword(inst, Keyword.CannotBeReturnedByOpponentEffects) && IsOpponentEffectFor(zones)))
        {
            EffectLog(zones, $"[{inst.TopCard.Name}]은(는) 효과로 패 · 덱으로 되돌아가지 않습니다.");
            return true;
        }
        return false;
    }

    /// <summary>DP 마이너스를 키워드로 막는지</summary>
    private bool DpReductionBlocked34(CardInstance target)
    {
        var zones = OwnerOf(target);
        if (CurrentEffectOwner == null && !_deletingByOpponentEffect)
            return false;
        if (CardScripts.HasKeyword(target, Keyword.DpCannotBeReduced)
            || (CardScripts.HasKeyword(target, Keyword.DpCannotBeReducedByOpponent) && IsOpponentEffectFor(zones)))
        {
            EffectLog(zones, $"[{target.TopCard.Name}]은(는) 효과로 DP가 마이너스되지 않습니다.");
            return true;
        }
        return false;
    }

    // =====================================================================
    // 상대가 고르는 동작
    // =====================================================================

    /// <summary>
    /// 상대(victim)가 자신의 패를 count장 파기한다 (match에 맞는 카드). optional = "Your opponent may trash ~" — 버릴지 상대가 정한다
    /// (플레이어면 묻고, AI는 버려도 손해가 적을 때 — 패가 5장 이상이거나 효과를 받는 쪽이 더 손해일 때는 버린다). 반환: 버린 매수
    /// </summary>
    internal int OpponentTrashesFromHand(PlayerZones victim, int count, Func<CardData, bool> match, bool optional, string label, bool random = false, PlayerZones? chooser = null)
    {
        var pool = victim.Hand.Where(match).ToList();
        if (pool.Count < (optional ? count : 1))
            return 0;
        if (optional && !AskYesNo(victim, null, $"[{label}] 자신의 패 {count}장을 버릴까요? (버리지 않으면 상대의 효과가 이어집니다)", recommended: victim.Hand.Count >= 5))
            return 0;
        List<CardData> picks;
        if (random)
        {
            var rng = new Random(State.TurnSequence * 7919 + victim.Hand.Count * 31 + victim.Trash.Count);
            picks = pool.OrderBy(_ => rng.Next()).Take(count).ToList();
        }
        else
        {
            var who = chooser ?? victim;
            // 고르는 쪽이 버리는 쪽이면 아까운 카드부터 남긴다, 상대가 고르면 좋은 카드부터
            var ranked = ReferenceEquals(who, victim)
                ? pool.OrderBy(c => c.Type == CardType.Option ? 1 : 0).ThenBy(c => c.Level).ThenBy(c => c.PlayCost).ToList()
                : pool.OrderByDescending(c => c.PlayCost).ThenByDescending(c => c.Level).ToList();
            picks = ChooseCards(who, CardChoiceZone.Hand, ranked, Math.Min(count, ranked.Count), upTo: false, $"[{label}] 파기할 패 {count}장을 고르세요.");
        }
        foreach (var c in picks)
        {
            victim.Hand.Remove(c);
            victim.Trash.Add(c);
        }
        NotifyHandTrashed(victim, picks);
        if (picks.Count > 0)
            EffectLog(victim, $"[{label}] 패 [{string.Join(", ", picks.Select(c => c.Name))}]을(를) 파기했습니다.");
        return picks.Count;
    }

    /// <summary>"Your opponent may trash their top security card" — 상대가 정한다 (AI는 시큐리티가 4장 이상일 때만)</summary>
    internal bool OpponentMayTrashTopSecurity(PlayerZones victim, string label)
    {
        if (victim.SecurityStack.Count == 0 || !AskYesNo(victim, null, $"[{label}] 자신의 시큐리티 위 1장을 파기할까요? (파기하지 않으면 상대의 효과가 이어집니다)", recommended: victim.SecurityStack.Count >= 4))
            return false;
        var x = victim.SecurityStack[0];
        victim.SecurityStack.RemoveAt(0);
        victim.Trash.Add(x);
        NotifySecurityRemoved(victim, x, trashed: true);
        EffectLog(victim, $"[{label}] 시큐리티 위 1장을 파기했습니다.");
        return true;
    }

    /// <summary>"Your opponent may delete 1 of their Digimon or Tamers" — 상대가 정하고 고른다 (AI는 Lv.3 이하 디지몬 · 테이머가 있을 때만)</summary>
    internal bool OpponentMayDeleteOwnPiece(PlayerZones victim, string label)
    {
        var digimon = victim.BattleArea.OrderBy(d => d.TopCard.Level).ThenBy(d => d.GetCurrentDP()).ToList();
        var tamers = victim.Tamers.Distinct().ToList();
        if (digimon.Count == 0 && tamers.Count == 0)
            return false;
        bool cheap = digimon.Any(d => d.TopCard.Level <= 3 || IsToken(d.TopCard)) || tamers.Count > 0;
        if (!AskYesNo(victim, null, $"[{label}] 자신의 디지몬 또는 테이머 1장을 소멸시킬까요? (소멸시키지 않으면 상대의 효과가 이어집니다)", recommended: cheap))
            return false;
        // 디지몬 먼저 (Lv.이 낮은 것), 테이머는 디지몬이 없거나 플레이어가 고르지 않을 때
        if (digimon.Count > 0)
        {
            var pick = AskDigimon(victim, $"[{label}] 소멸시킬 자신의 디지몬을 고르세요. (고르지 않으면 테이머)", digimon, digimon[0], optional: tamers.Count > 0);
            if (pick != null)
                return DeleteFrom(victim, pick, DeletionCause.Effect);
        }
        if (tamers.Count == 0)
            return false;
        var t = tamers.OrderBy(x => x.PlayCost).First();
        DeleteTamer(victim, t);
        return !victim.Tamers.Contains(t);
    }

    /// <summary>"Your opponent adds the top card of their security stack to the hand"</summary>
    internal CardData? AddTopSecurityToHand(PlayerZones zones)
    {
        if (zones.SecurityStack.Count == 0)
            return null;
        var x = zones.SecurityStack[0];
        zones.SecurityStack.RemoveAt(0);
        zones.Hand.Add(x);
        NotifySecurityRemoved(zones, x, trashed: false);
        return x;
    }

    // =====================================================================
    // 등장할 때 이 카드 아래에 놓는 비용 ("by placing up to 3 ~ under it, reduce the play cost by 4 for each one")
    // =====================================================================

    /// <summary>등장 코스트 비용으로 이 카드 아래에 놓을 카드 — 등장한 디지몬의 진화원 아래에 놓는다 (디지크로스 재료와 같은 때)</summary>
    internal List<CardData> PendingUnderPlayed { get; } = new();

    /// <summary>비용으로 자신의 디지몬을 배틀 에어리어에서 떼어 맨 위 카드를 돌려준다 (진화원은 파기 — 종합 룰 4-7-7). 토큰이면 null</summary>
    internal CardData? DetachDigimonAsCost(PlayerZones zones, CardInstance moving)
    {
        if (!zones.BattleArea.Contains(moving))
            return null;
        TryPreventLeave(zones, moving, deletion: false, inBattle: false, canPrevent: false, LeaveDestination.Other);
        if (!zones.BattleArea.Remove(moving))
            return null;
        TrashLinks(zones, moving);
        var trashed = moving.GetEvolutionStackBottomUp().ToList();
        ApplyOverflow(zones, trashed);
        while (moving.EvolutionSources.Count > 0)
            zones.Trash.Add(moving.EvolutionSources.Pop());
        PurgeTokens(zones);
        NotifyLeftBattleArea(zones, moving);
        RecalculateFieldBuffs();
        return IsToken(moving.TopCard) ? null : moving.TopCard;
    }

    /// <summary>등장한 디지몬에 PendingUnderPlayed를 진화원 아래로 놓는다</summary>
    private void PlacePendingUnderPlayed(CardInstance instance)
    {
        if (PendingUnderPlayed.Count == 0)
            return;
        foreach (var x in PendingUnderPlayed)
            instance.AddBottomSource(x);
        PendingUnderPlayed.Clear();
    }

    // =====================================================================
    // 비용 · 배틀
    // =====================================================================

    /// <summary>비용으로 이 디지몬을 패로 (막을 수 없다 — "떠나려 할 때"는 발휘)</summary>
    internal void ReturnToHandAsCost34(PlayerZones zones, CardInstance inst)
    {
        TryPreventLeave(zones, inst, deletion: false, inBattle: false, canPrevent: false, LeaveDestination.Hand);
        if (!zones.BattleArea.Remove(inst))
            return;
        ApplyOverflow(zones, inst.GetFaceUpStackBottomUp().Append(inst.TopCard));
        TrashLinks(zones, inst);
        while (inst.EvolutionSources.Count > 0)
            zones.Trash.Add(inst.EvolutionSources.Pop());
        if (!IsToken(inst.TopCard)) zones.Hand.Add(inst.TopCard);
        PurgeTokens(zones);
        NotifyLeftBattleArea(zones, inst);
        if (!IsToken(inst.TopCard)) NotifyReturnedToHand(zones, inst);
        RecalculateFieldBuffs();
    }

    /// <summary>비용으로 테이머를 패 / 덱 아래로 (막을 수 없다)</summary>
    internal void MoveTamerAsCost34(PlayerZones owner, CardData tamer, bool toHand)
    {
        if (toHand)
        {
            ReturnTamerToHand(owner, tamer, canPrevent: false);
            return;
        }
        if (!owner.Tamers.Contains(tamer))
            return;
        TryPreventTamerLeave(owner, tamer, false, LeaveDestination.Deck);
        if (!owner.Tamers.Remove(tamer))
            return;
        owner.SuspendedTamers.Remove(tamer);
        owner.MainDeck.Add(tamer);
        ClearUnderTamersIfNoTamer(owner);
    }

    internal void ApplyOverflowPublic34(PlayerZones owner, IEnumerable<CardData> leaving) => ApplyOverflow(owner, leaving);

    /// <summary>효과로 테이머를 덱 아래로 (떠나려 할 때 — 막을 수 있다)</summary>
    internal void MoveTamerToDeckBottom34(PlayerZones owner, CardData tamer)
    {
        if (!owner.Tamers.Contains(tamer))
            return;
        if (TryPreventTamerLeave(owner, tamer, true, LeaveDestination.Deck))
            return;
        if (!owner.Tamers.Remove(tamer))
            return;
        owner.SuspendedTamers.Remove(tamer);
        owner.MainDeck.Add(tamer);
        ClearUnderTamersIfNoTamer(owner);
    }

    /// <summary>
    /// "This Digimon may battle 1 of your opponent's Digimon" — 어택이 아닌 배틀 (DP 비교 · 소멸 · ≪길동무≫ · "배틀에서 소멸시켰을 때"). 반환: 결과 메시지
    /// </summary>
    internal string EffectBattle(CardInstance mine, CardInstance theirs)
    {
        var (aDel, dDel, aDp, dDp) = ResolveDigimonBattle(mine, theirs);
        string msg = $"배틀: [{mine.TopCard.Name}(DP {aDp:N0})] vs [{theirs.TopCard.Name}(DP {dDp:N0})] — "
                     + (aDel && dDel ? "둘 다 소멸" : dDel ? $"[{theirs.TopCard.Name}] 소멸" : aDel ? $"[{mine.TopCard.Name}] 소멸" : "소멸 없음");
        Log(msg);
        RecalculateFieldBuffs();
        return msg;
    }

    /// <summary>등장하지 않았으면(옵션 등) 놓으려던 카드는 트래시로</summary>
    private void DropPendingUnderPlayed(PlayerZones owner)
    {
        if (PendingUnderPlayed.Count == 0)
            return;
        owner.Trash.AddRange(PendingUnderPlayed);
        PendingUnderPlayed.Clear();
    }
}
