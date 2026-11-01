using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.53.0 미지원 문장 27차 묶음의 엔진 처리:
/// · "When this Digimon deletes another Digimon" (BT16-061) — 배틀로 · 이 디지몬의 효과로 (공식 Q&amp;A EX1-064: 【등장 시】 효과로 소멸시켜도 유발) — 처리 중인 효과의 디지몬을 기억한다
/// · EX5-053 "When your security is checked, if that card is ~, play it without battling" — 상대의 턴 · 디지몬마다 턴에 1회
/// · EX11-062 "your ＜Vortex＞ can also attack players" — 상대에게 액티브 디지몬이 없을 때
/// · 비용: 육성 에어리어의 디지몬 파기 (BT16-090) · 다른 디지몬/테이머를 시큐리티 위 또는 아래에 (P-187) · 육성 에어리어에 등장 코스트를 내고 등장 (BT16-090)
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>지금 처리 중인 효과의 디지몬 (안쪽 효과가 위) — "this Digimon deletes" (효과로)</summary>
    private readonly List<CardInstance?> _effectSelf53 = new();

    internal void PushEffectSelf53(CardInstance? self) => _effectSelf53.Add(self);

    internal void PopEffectSelf53()
    {
        if (_effectSelf53.Count > 0) _effectSelf53.RemoveAt(_effectSelf53.Count - 1);
    }

    /// <summary>효과로 소멸할 디지몬을 소멸시킨 디지몬 (처리 중인 효과의 디지몬 — 자신이 아니고 배틀 에어리어에 있을 때)</summary>
    private CardInstance? EffectDeleter53(CardInstance victim) =>
        _effectSelf53.Count > 0 && _effectSelf53[^1] is { } self && !ReferenceEquals(self, victim) && OwnerOfOrNull53(self) != null ? self : null;

    private PlayerZones? OwnerOfOrNull53(CardInstance d) =>
        State.Player.BattleArea.Contains(d) ? State.Player : State.DummyOpponent.BattleArea.Contains(d) ? State.DummyOpponent : null;

    /// <summary>"When this Digimon deletes another Digimon" — 소멸시킨 디지몬이 아직 배틀 에어리어에 있으면</summary>
    private void FireDeletedAnother53(CardInstance? killer, CardInstance victim)
    {
        if (killer == null || OwnerOfOrNull53(killer) is not { } zones) return;
        if (RunEffects(EffectTrigger.ThisDigimonDeletedAnother53, zones, killer, killer.TopCard, other: victim).AnyEffectRan) Diag46.Hit("deletedAnother53");
    }

    /// <summary>육성 에어리어의 디지몬을 파기한다 (비용 — 소멸이 아니다. 진화원과 함께 트래시, ≪오버플로우≫)</summary>
    internal bool TrashBreeding53(PlayerZones owner)
    {
        if (owner.BreedingArea is not { } b || b.TopCard.Type != CardType.Digimon) return false;
        owner.BreedingArea = null;
        var stack = b.GetEvolutionStackBottomUp();
        ApplyOverflow(owner, stack.Append(b.TopCard));
        owner.Trash.Add(b.TopCard);
        while (b.EvolutionSources.Count > 0) owner.Trash.Add(b.EvolutionSources.Pop());
        EffectLog(owner, $"육성 에어리어의 [{b.TopCard.Name}]을(를) 파기했습니다.");
        RecalculateFieldBuffs();
        return true;
    }

    /// <summary>비용으로 디지몬(맨 위 카드) 또는 테이머를 주인의 시큐리티 위 · 아래에 (막을 수 없다)</summary>
    internal bool PlaceAsSecurity53(PlayerZones zones, CardInstance? digimon, CardData? tamer, bool top)
    {
        if (digimon != null)
            return PlaceDigimonInSecurity(zones, digimon, top, faceUp: false, canPrevent: false);
        if (tamer == null || !zones.Tamers.Contains(tamer)) return false;
        TryPreventTamerLeave(zones, tamer, false, LeaveDestination.Security);
        if (!zones.Tamers.Remove(tamer)) return false;
        zones.SuspendedTamers.Remove(tamer);
        ApplyOverflow(zones, new[] { tamer });
        if (top) zones.SecurityStack.Insert(0, tamer); else zones.SecurityStack.Add(tamer);
        NotifySecurityAdded(zones, tamer);
        ClearUnderTamersIfNoTamer(zones);
        RecalculateFieldBuffs();
        return true;
    }

    /// <summary>패의 카드를 비어 있는 육성 에어리어에 등장 코스트 N을 내고 등장 (BT16-090)</summary>
    internal bool PlayToBreedingPaying53(PlayerZones owner, CardData card, int cost)
    {
        if (owner.BreedingArea != null || card.Type != CardType.Digimon || !owner.Hand.Contains(card)) return false;
        if (!TryPlayByEffect(owner, card, PlayOrigin.Hand, () => owner.Hand.Remove(card), toBreeding: true)) return false;
        AddMemory(owner, -cost);
        if (ReferenceEquals(owner, State.Player)) State.TotalMemorySpent += cost;
        EffectLog(owner, $"[효과] [{card.Name}] 등장 코스트 {cost} 지불 (육성 에어리어)");
        return true;
    }

    private const string SecurityCheckedPlayKey53 = "security-checked-play-53";

    /// <summary>
    /// EX5-053 "[Opponent's Turn] [Once Per Turn] When your security is checked, if that card is ~, play it without battling and without paying the cost" —
    /// 체크된 시큐리티 디지몬 카드(트래시에 놓인 직후)를 배틀하지 않고 등장. 등장시켰으면 true
    /// </summary>
    private bool TrySecurityCheckedPlay53(PlayerZones owner, CardData card)
    {
        if (IsTurnOf(owner) || card.Type != CardType.Digimon || !owner.Trash.Contains(card)) return false;
        var src = owner.BattleArea.FirstOrDefault(d => d.TamerProxyOf == null && CardScripts.For(d.TopCard)?.SecurityCheckedPlay53 is { } f && f(card) && !d.UsedOncePerTurn.Contains(SecurityCheckedPlayKey53));
        if (src == null) return false;
        src.UsedOncePerTurn.Add(SecurityCheckedPlayKey53);
        EffectLog(owner, $"[{src.TopCard.Name}] 시큐리티가 체크되었을 때 — [{card.Name}]을(를) 배틀하지 않고 코스트 없이 등장시킵니다");
        if (!TryPlayByEffect(owner, card, PlayOrigin.Security, () => owner.Trash.Remove(card))) return false;
        Diag46.Hit("securityCheckedPlay53");
        return true;
    }

    /// <summary>EX11-062 "[Your Turn] While your opponent has no unsuspended Digimon, your ＜Vortex＞ can also attack players"</summary>
    internal bool VortexCanAttackPlayers53(PlayerZones owner) =>
        IsTurnOf(owner) && !EnemyOf(owner).BattleArea.Any(d => d.TamerProxyOf == null && !d.IsSuspended)
        && owner.Tamers.Any(t => CardScripts.For(t)?.VortexPlayers53 == true);
}
