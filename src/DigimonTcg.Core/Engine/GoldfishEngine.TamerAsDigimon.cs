using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.36.0 테이머를 디지몬으로도 취급 — 「최건우」(Marcus Damon) "is also treated as a 3000 DP Digimon" (BT12-092 · BT13-008 · BT13-018 · BT17-087 · BT21-044 · BT21-096 · AD1-021 · BT25-104).
/// 공식 Q&A (BT12-092 등): 「DP X000인 디지몬」으로도 취급 — 보통 디지몬과 같이 어택할 수 있다. 단, 등장한 턴의 테이머는 그 턴에 어택할 수 없다.
///
/// 모델: 테이머 카드는 테이머 존(PlayerZones.Tamers)에 그대로 있고 (테이머 효과 · "자신의 테이머" · 테이머 비용은 그대로),
/// 배틀 에어리어에 그 테이머를 나타내는 디지몬(<see cref="CardInstance.TamerProxyOf"/>)을 하나 둔다. 디지몬의 맨 위 카드는 테이머 카드의 복사본
/// (종류 디지몬 · 주어진 DP · 효과 문장 없음 — <see cref="CardData.IsTamerProxy"/>, 카드 효과는 테이머 쪽에서만 발휘한다).
///  - 레스트 · 액티브는 서로 따른다: 어택 · 블록 · 효과로 그 디지몬이 레스트하면 테이머도 레스트 ("자신의 테이머가 레스트했을 때" 유발), 테이머가 레스트하면 디지몬도
///  - 그 디지몬이 배틀 에어리어를 떠나면 (배틀 · 효과로 소멸, 패 · 덱 등) 테이머 카드가 그곳으로 간다 — 복사본을 원래 카드 정보로 되돌리고 테이머 존에서 뺀다.
///    트래시로 갔으면 "자신의 테이머가 소멸했을 때"도 유발한다 (BT21-044 진화원 효과)
///  - 테이머가 효과로 테이머 존을 떠나면 그 디지몬도 없어진다. 기간이 끝나면 디지몬 취급만 끝난다 (테이머 · 레스트 상태 그대로)
///  - 진화할 수 없다 (카드에 적힌 대로) · 얻은 키워드(≪블로커≫ · ≪속공≫ · ≪연계≫)는 그 디지몬이 얻는다
///  - 지속 효과 "[Your Turn] All of your [Marcus Damon]s are also treated as 12000 DP Digimon and gain ＜Rush＞" (BT25-104)는 조건이 맞는 동안만
/// 아직 하지 않은 것: 테이머 아래 카드의 진화원 효과 (테이머 아래 카드는 테이머마다 나뉘어 있지 않다)
/// </summary>
public partial class GoldfishEngine
{
    private sealed class TamerProxy
    {
        public PlayerZones Owner = null!;
        public CardData Tamer = null!;
        public CardInstance Inst = null!;
        public int Until;            // 효과 기간 (지속 효과만이면 0)
        public bool Static;          // 지속 효과로 유지 중
        public bool LastSuspended;
    }

    private readonly List<TamerProxy> _tamerProxies = new();
    private bool _syncingTamerProxies;

    /// <summary>테이머를 디지몬으로 취급한 횟수 (시뮬레이션 · 진단용)</summary>
    public int TamerAsDigimonTotal { get; private set; }

    /// <summary>지금 디지몬으로도 취급하는 테이머의 디지몬 (없으면 null)</summary>
    internal CardInstance? TamerProxyOf(PlayerZones owner, CardData tamer) =>
        _tamerProxies.FirstOrDefault(p => ReferenceEquals(p.Owner, owner) && ReferenceEquals(p.Tamer, tamer) && owner.BattleArea.Contains(p.Inst))?.Inst;

    /// <summary>
    /// 테이머 tamer를 until(TurnSequence)까지 DP dp의 디지몬으로도 취급한다 (이미 취급 중이면 DP · 기간 · 키워드를 더한다). 반환: 그 디지몬
    /// </summary>
    internal CardInstance? TreatTamerAsDigimon(PlayerZones owner, CardData tamer, int dp, int until, bool cannotDigivolve, IReadOnlyList<Keyword> grants, bool isStatic = false)
    {
        if (!owner.Tamers.Contains(tamer))
            return null;
        var p = _tamerProxies.FirstOrDefault(x => ReferenceEquals(x.Owner, owner) && ReferenceEquals(x.Tamer, tamer) && owner.BattleArea.Contains(x.Inst));
        if (p == null)
        {
            var copy = CardInstance.CopyCard(tamer);
            copy.IsTamerProxy = true;
            copy.Type = CardType.Digimon;
            copy.DP = dp;
            copy.EffectText = string.Empty;
            copy.InheritedEffectText = string.Empty;
            copy.IsBlocker = false;
            copy.InheritedDpBonus = 0;
            copy.SecurityAttackBonus = 0;
            copy.InheritedSecurityAttackBonus = 0;
            bool suspended = !owner.HasActiveTamer(tamer);
            var inst = new CardInstance(copy, TamerEnteredThisTurn(owner, tamer) ? State.CurrentTurn : State.CurrentTurn - 1)   // Q&A: 등장한 턴의 테이머는 어택할 수 없다
            {
                TamerProxyOf = tamer, IsSuspended = suspended, IsOwnersTurn = IsTurnOf(owner),
            };
            owner.BattleArea.Add(inst);
            p = new TamerProxy { Owner = owner, Tamer = tamer, Inst = inst, LastSuspended = suspended };
            _tamerProxies.Add(p);
            TamerAsDigimonTotal++;
            EffectLog(owner, $"테이머 [{tamer.Name}]을(를) DP {dp}의 디지몬으로도 취급합니다.");
        }
        else
            p.Inst.TopCard.DP = isStatic ? Math.Max(p.Inst.TopCard.DP, dp) : dp;   // 효과는 나중 것 · 지속 효과는 큰 쪽
        if (isStatic) p.Static = true;
        else p.Until = Math.Max(p.Until, until);
        int keyUntil = isStatic ? State.TurnSequence : until;
        if (cannotDigivolve) p.Inst.CannotDigivolveUntil = Math.Max(p.Inst.CannotDigivolveUntil, isStatic ? int.MaxValue / 2 : until);
        foreach (var k in grants)
            p.Inst.TimedKeywords.Add((k, keyUntil));
        RecalculateFieldBuffs();
        return p.Inst;
    }

    /// <summary>그 디지몬이 레스트했다 (어택 · 블록 · 효과) — 테이머도 레스트 (Suspend에서 부른다). 반환: 테이머가 레스트했는지</summary>
    private bool SuspendProxyTamer(PlayerZones owner, CardInstance inst)
    {
        var p = _tamerProxies.FirstOrDefault(x => ReferenceEquals(x.Inst, inst));
        if (p == null) return false;
        p.LastSuspended = true;
        if (!owner.Tamers.Contains(p.Tamer) || !owner.HasActiveTamer(p.Tamer)) return false;
        owner.SuspendedTamers.Add(p.Tamer);
        return true;
    }

    /// <summary>그 디지몬이 액티브가 되었다 (효과) — 테이머도 액티브</summary>
    private void UnsuspendProxyTamer(PlayerZones owner, CardInstance inst)
    {
        var p = _tamerProxies.FirstOrDefault(x => ReferenceEquals(x.Inst, inst));
        if (p == null) return;
        p.LastSuspended = false;
        if (owner.Tamers.Contains(p.Tamer) && !owner.HasActiveTamer(p.Tamer) && !TamerUnsuspendLocked(owner, p.Tamer))
        {
            owner.SuspendedTamers.Remove(p.Tamer);
            RunOrDefer(() => NotifyTamersUnsuspended(owner, new[] { p.Tamer }));
        }
    }

    /// <summary>
    /// 디지몬과 테이머를 맞춘다 (RecalculateFieldBuffs 처음):
    /// 떠난 디지몬 → 테이머 카드가 그곳으로 · 테이머가 없어진 디지몬 → 없앤다 · 지속 효과 · 레스트 상태 맞추기
    /// </summary>
    private void SyncTamerProxies()
    {
        if (_syncingTamerProxies || (_tamerProxies.Count == 0 && !AnyTamerAura()))
            return;
        _syncingTamerProxies = true;
        try
        {
            foreach (var p in _tamerProxies.ToList())
            {
                if (!p.Owner.BattleArea.Contains(p.Inst))
                {
                    ProxyLeft(p);
                    continue;
                }
                if (!p.Owner.Tamers.Contains(p.Tamer) || p.Owner.Tamers.Count(t => ReferenceEquals(t, p.Tamer)) < _tamerProxies.Count(x => ReferenceEquals(x.Owner, p.Owner) && ReferenceEquals(x.Tamer, p.Tamer)))
                {
                    DropProxy(p, $"테이머 [{p.Tamer.Name}]이(가) 없어져 디지몬 취급이 끝납니다.");
                    continue;
                }
                // 레스트 상태: 테이머 쪽이 바뀌었으면 디지몬도 (디지몬 쪽 변화는 Suspend · Unsuspend가 처리한다)
                bool tamerSuspended = !p.Owner.HasActiveTamer(p.Tamer);
                if (p.Inst.IsSuspended != tamerSuspended)
                {
                    if (p.Inst.IsSuspended != p.LastSuspended)
                    {
                        if (p.Inst.IsSuspended) p.Owner.SuspendedTamers.Add(p.Tamer);
                        else p.Owner.SuspendedTamers.Remove(p.Tamer);
                    }
                    else p.Inst.IsSuspended = tamerSuspended;
                }
                p.LastSuspended = p.Inst.IsSuspended;
            }
            ApplyTamerAuras();
        }
        finally { _syncingTamerProxies = false; }
    }

    /// <summary>그 디지몬이 배틀 에어리어를 떠났다 — 맨 위 카드(복사본)를 원래 테이머 카드 정보로 되돌리고 테이머 존에서 뺀다</summary>
    private void ProxyLeft(TamerProxy p)
    {
        _tamerProxies.Remove(p);
        var copy = p.Inst.TopCard;
        RestoreCard(copy, p.Tamer);
        if (RemoveOne(p.Owner.Tamers, p.Tamer))
        {
            if (p.Owner.SuspendedTamers.Count(t => ReferenceEquals(t, p.Tamer)) > p.Owner.Tamers.Count(t => ReferenceEquals(t, p.Tamer)))
                RemoveOne(p.Owner.SuspendedTamers, p.Tamer);
            ClearUnderTamersIfNoTamer(p.Owner);
            EffectLog(p.Owner, $"디지몬으로 취급하던 테이머 [{p.Tamer.Name}]이(가) 배틀 에어리어를 떠났습니다.");
            if (p.Owner.Trash.Any(x => ReferenceEquals(x, copy)))   // 소멸 — "자신의 테이머가 소멸했을 때"
            {
                var owner = p.Owner;
                RunOrDefer(() =>
                {
                    if (State.IsGameOver) return;
                    FireForSide(EffectTrigger.AllyTamerDeleted, owner, null, triggerCard: copy);
                    FireForSide(EffectTrigger.OpponentTamerDeleted, EnemyOf(owner), null, triggerCard: copy);
                });
            }
        }
    }

    /// <summary>디지몬 취급을 끝낸다 (기간 끝 · 테이머가 없어짐) — 배틀 에어리어에서 조용히 뺀다 (아래 카드 · 링크는 트래시)</summary>
    private void DropProxy(TamerProxy p, string log)
    {
        _tamerProxies.Remove(p);
        if (p.Owner.BattleArea.Remove(p.Inst))
        {
            while (p.Inst.EvolutionSources.Count > 0)
            {
                var s = p.Inst.EvolutionSources.Pop();
                if (!IsToken(s)) p.Owner.Trash.Add(s);
            }
            p.Owner.Trash.AddRange(p.Inst.LinkedCards.Where(c => !IsToken(c)));
            p.Inst.LinkedCards.Clear();
            if (ReferenceEquals(CurrentAttacker, p.Inst)) CurrentAttacker = null;
            EffectLog(p.Owner, log);
        }
    }

    /// <summary>턴 종료 시: 기간이 끝난 디지몬 취급을 끝낸다</summary>
    internal void ExpireTamerProxies()
    {
        SyncTamerProxies();
        foreach (var p in _tamerProxies.ToList())
            if (!p.Static && p.Until <= State.TurnSequence)   // 이 턴 종료 시까지인 것 (다음 턴에는 끝)
                DropProxy(p, $"테이머 [{p.Tamer.Name}]의 디지몬 취급이 끝났습니다.");
    }

    // ---- 지속 효과 (BT25-104) ----

    private bool AnyTamerAura() =>
        State.Player.BattleArea.Concat(State.DummyOpponent.BattleArea).Any(d => d.ActiveKeywords.Contains(Keyword.TamerAsDigimonAura));

    /// <summary>지속 효과로 디지몬 취급: 효과가 켜진 디지몬의 카드 문장(KeywordDictionary가 등록한 TamerAura)대로</summary>
    private void ApplyTamerAuras()
    {
        foreach (var owner in new[] { State.Player, State.DummyOpponent })
        {
            var auras = owner.BattleArea.Where(d => d.ActiveKeywords.Contains(Keyword.TamerAsDigimonAura))
                .SelectMany(d => TamerAuras36(d)).ToList();
            foreach (var p in _tamerProxies.Where(x => x.Static && ReferenceEquals(x.Owner, owner)).ToList())
            {
                if (auras.Any(a => CardNames.Is(p.Tamer, a.Name))) continue;
                p.Static = false;
                if (!EffectRules.IsActive(p.Until, State)) DropProxy(p, $"테이머 [{p.Tamer.Name}]의 디지몬 취급이 끝났습니다.");
            }
            foreach (var a in auras)
                foreach (var t in owner.Tamers.Distinct().Where(t => CardNames.Is(t, a.Name)).ToList())
                {
                    var existing = _tamerProxies.FirstOrDefault(x => ReferenceEquals(x.Owner, owner) && ReferenceEquals(x.Tamer, t));
                    if (existing != null && existing.Static && existing.Inst.TimedKeywords.Any(k => k.Until >= State.TurnSequence && a.Grants.Contains(k.Keyword))) continue;
                    if (existing == null && owner.BattleArea.Count >= 12) continue;
                    TreatTamerAsDigimonNoRecalc(owner, t, a.Dp, a.Grants);
                }
        }
    }

    private void TreatTamerAsDigimonNoRecalc(PlayerZones owner, CardData tamer, int dp, IReadOnlyList<Keyword> grants)
    {
        bool outer = _syncingTamerProxies;
        _syncingTamerProxies = true;
        try { TreatTamerAsDigimon(owner, tamer, dp, 0, cannotDigivolve: false, grants, isStatic: true); }
        finally { _syncingTamerProxies = outer; }
    }

    /// <summary>디지몬 d의 카드들이 가진 "All of your [X]s are also treated as N DP Digimon" (맨 위 카드 = 메인, 진화원 = 진화원 효과)</summary>
    private static IEnumerable<(string Name, int Dp, IReadOnlyList<Keyword> Grants)> TamerAuras36(CardInstance d)
    {
        foreach (var a in CardScripts.For(d.TopCard)?.TamerAuras ?? new List<TamerAura>())
            if (a.Source == EffectSource.Main) yield return (a.Name, a.Dp, a.Grants);
        foreach (var s in d.FaceUpSources)
            foreach (var a in CardScripts.For(s)?.TamerAuras ?? new List<TamerAura>())
                if (a.Source == EffectSource.Inherited) yield return (a.Name, a.Dp, a.Grants);
    }

    // ---- 도구 ----

    private static bool RemoveOne(List<CardData> list, CardData card)
    {
        int i = list.FindIndex(x => ReferenceEquals(x, card));
        if (i < 0) return false;
        list.RemoveAt(i);
        return true;
    }

    private static readonly System.Reflection.PropertyInfo[] CardProps =
        typeof(CardData).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite).ToArray();

    /// <summary>복사본 to를 원래 카드 from의 정보로 되돌린다 (그 자리에서 — 이미 다른 곳에 놓였을 수 있다)</summary>
    private static void RestoreCard(CardData to, CardData from)
    {
        foreach (var prop in CardProps)
            prop.SetValue(to, prop.GetValue(from));
        to.Traits = from.Traits.ToList();
        to.IsTamerProxy = false;
    }
}

/// <summary>v0.36.0 지속 효과 "All of your [Marcus Damon]s are also treated as 12000 DP Digimon and gain ＜Rush＞" (BT25-104)</summary>
public sealed record TamerAura(EffectSource Source, string Name, int Dp, IReadOnlyList<Keyword> Grants);
