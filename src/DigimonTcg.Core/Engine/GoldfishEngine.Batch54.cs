using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.54.0 효과 처리 중에 유발한 효과는 그 효과가 끝난 뒤에 발휘한다 (28차 묶음).
///  - 종합 룰 15-8-3-2: 유발형 효과는 규칙 · 효과의 처리 중에 발휘할 수 없다
///  - 15-4-2-2 · 15-4-4: 조건을 만족하면 그때 유발하고, 처리 중인 효과가 끝난 뒤 발휘 대기에서 1개씩
///  - 15-4-4-3 · 15-4-4-4: 발휘 전에 새 카드가 되었거나(필드를 떠남) 효과를 잃었으면(진화로 맨 위가 아님) 발휘하지 않는다
///  - 15-8-3-8: 유발 조건과 같은 상태를 보는 참조는 유발했을 때의 상태 (레스트시킨 효과의 주인 · 버린 카드 · 효과로 빠졌는지)
/// 전에는 레스트 · 액티브 · 효과로 한 진화 · 패/덱 파기 · 시큐리티 증감 · 앞면 시큐리티 · 상대 패에 추가 · 어택 대상 변경의 유발이
/// 처리 중인 효과의 한가운데에서 바로 발휘되었다 (≪트레이닝≫의 레스트 → 상대 효과가 이 디지몬을 떠나게 함 → 덱 위 카드가 놓일 곳이 없음, v0.53.0).
/// 효과 밖(어택 선언의 레스트 · 블록 · 액티브 페이즈 · 패에서 진화 · 시큐리티 체크)은 전과 같이 바로.
/// 효과 하나(ResolveCore)마다 모으고, 그 효과로 등장한 카드의 【등장 시】(v0.47.0) 뒤에 일어난 순서대로 발휘한다.
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>끄면 v0.53.0까지처럼 바로 발휘 (시뮬레이션 비교용)</summary>
    public static bool DeferMidEffectTriggers54 { get; set; } = true;

    /// <summary>지금 처리 중인 효과에서 유발한 효과 (효과 밖이면 null)</summary>
    private List<Action>? _midEffectTriggers54;

    /// <summary>효과 처리 중이라 유발한 효과를 미루는 중인지</summary>
    internal bool Deferring54 => DeferMidEffectTriggers54 && _midEffectTriggers54 != null;

    internal List<Action>? BeginTriggerBatch54()
    {
        var outer = _midEffectTriggers54;
        _midEffectTriggers54 = DeferMidEffectTriggers54 ? new List<Action>() : null;
        return outer;
    }

    internal void EndTriggerBatch54(List<Action>? outer, bool run)
    {
        var mine = _midEffectTriggers54;
        _midEffectTriggers54 = outer;
        if (!run || mine == null) return;
        foreach (var fire in mine)
        {
            if (State.IsGameOver) return;
            fire();
        }
    }

    /// <summary>효과 처리 중이면 그 효과가 끝날 때까지 미루고, 아니면 바로</summary>
    private void Defer54(Action fire)
    {
        if (Deferring54) { _midEffectTriggers54!.Add(fire); Diag46.Hit("deferred54"); }
        else fire();
    }

    private void WithSuspendCause54(PlayerZones? cause, Action fire)
    {
        var outer = SuspendCause;
        SuspendCause = cause;
        try { fire(); }
        finally { SuspendCause = outer; }
    }

    /// <summary>레스트 · 액티브 유발: 미루면 그 사건 때 있던 카드만 (그 뒤에 온 카드는 반응하지 않는다 — v0.46.0과 같은 기록)</summary>
    private EventWitness46? DeferWitness54() => Deferring54 ? CaptureWitness46() : null;

    /// <summary>디지몬이 레스트했을 때의 유발 ("이 디지몬이 레스트 상태가 되었을 때" · 자신의/상대의 디지몬 · 디지몬으로 취급하는 테이머)</summary>
    private void FireSuspended54(PlayerZones owner, CardInstance inst, bool tamerToo)
    {
        var cause = SuspendingForAttack ? null : CurrentEffectOwner;   // v0.32.0 "When an effect suspends ~"
        var witness = DeferWitness54();
        bool deferred = Deferring54;
        Defer54(() =>
        {
            if (State.IsGameOver) return;
            WithSuspendCause54(cause, () =>
            {
                if (!deferred || owner.BattleArea.Contains(inst))
                    RunEffects(EffectTrigger.ThisDigimonSuspends, owner, inst, inst.TopCard);
                FireForSide(EffectTrigger.AllyDigimonSuspended, owner, inst, witness: witness);
                FireForSide(EffectTrigger.OpponentDigimonSuspended, EnemyOf(owner), inst, witness: witness);
                if (tamerToo && inst.TamerProxyOf is { } tamer)
                {
                    FireForSide(EffectTrigger.AllyTamerSuspended, owner, null, triggerCard: tamer, witness: witness);
                    FireForSide(EffectTrigger.OpponentTamerSuspended, EnemyOf(owner), null, triggerCard: tamer, witness: witness);
                }
            });
        });
    }

    /// <summary>테이머가 레스트했을 때의 유발</summary>
    private void FireTamerSuspended54(PlayerZones owner, CardData tamer)
    {
        var cause = SuspendingForAttack ? null : CurrentEffectOwner;
        var witness = DeferWitness54();
        Defer54(() =>
        {
            if (State.IsGameOver) return;
            WithSuspendCause54(cause, () =>
            {
                FireForSide(EffectTrigger.AllyTamerSuspended, owner, null, triggerCard: tamer, witness: witness);
                FireForSide(EffectTrigger.OpponentTamerSuspended, EnemyOf(owner), null, triggerCard: tamer, witness: witness);
            });
        });
    }

    /// <summary>효과로 액티브가 되었을 때의 유발 ("메인 페이즈에 액티브가 되었을 때"는 액티브가 된 때의 페이즈로)</summary>
    private void FireUnsuspendedByEffect54(PlayerZones owner, CardInstance inst, bool mainPhase)
    {
        var witness = DeferWitness54();
        Defer54(() =>
        {
            if (State.IsGameOver || !owner.BattleArea.Contains(inst)) return;
            if (IsTurnOf(owner) && mainPhase)
                RunEffects(EffectTrigger.ThisDigimonUnsuspendedInMain, owner, inst, inst.TopCard);
            if (owner.BattleArea.Contains(inst))
            {
                FireForSide(EffectTrigger.AllyDigimonUnsuspended, owner, inst, witness: witness);
                FireForSide(EffectTrigger.OpponentDigimonUnsuspended, EnemyOf(owner), inst, witness: witness);
            }
        });
    }

    /// <summary>
    /// 효과로 진화한 뒤의 【진화 시】 · "진화했을 때". 미루었다가 발휘하기 전에 그 디지몬이 떠났으면 둘 다 없고,
    /// 다시 진화해 그 카드가 맨 위가 아니면 그 카드의 【진화 시】는 잃었다 (15-4-4-4 — "진화했을 때"는 그대로)
    /// </summary>
    private void FireDigivolvedByEffect54(PlayerZones owner, CardInstance target, CardData evoCard, bool fromTrash)
    {
        bool deferred = Deferring54;
        if (deferred) NoteDigivolved51(owner);   // "If you've digivolved this turn" — 같은 효과의 뒤 처리도 본다
        var witness = deferred ? CaptureWitness46() : null;
        Defer54(() =>
        {
            if (State.IsGameOver || !owner.BattleArea.Contains(target)) return;
            if (fromTrash) _digivolvingFromTrash++;
            try
            {
                if (!deferred || ReferenceEquals(target.TopCard, evoCard))
                {
                    _whenDigivolvingByEffect++;
                    try { ApplyWhenDigivolvingEffects(owner, target, evoCard, witness); }
                    finally { _whenDigivolvingByEffect--; }
                }
                else if (witness != null) _digivolveWitness46[target] = witness;
                _digivolvingByEffect++;
                try { FireForSide(EffectTrigger.AllyDigimonDigivolved, owner, target); }
                finally { _digivolvingByEffect--; }
            }
            finally { if (fromTrash) _digivolvingFromTrash--; }
        });
    }
}

/// <summary>v0.54.0 진화할 카드가 있던 곳 ("would digivolve into a Digimon card in your trash" BT9-077 · "~ from your hand" EX1-061)</summary>
public partial class GoldfishEngine
{
    internal enum EvoOrigin54 { Hand, Trash, Other }

    /// <summary>지금 코스트를 정하는 진화의 진화할 카드가 있던 곳 (효과가 정하지 않았으면 패 — 보통 진화)</summary>
    internal EvoOrigin54 CurrentEvoOrigin54 { get; private set; } = EvoOrigin54.Hand;

    private T WithEvoOrigin54<T>(EvoOrigin54 origin, Func<T> run)
    {
        var outer = CurrentEvoOrigin54;
        CurrentEvoOrigin54 = origin;
        try { return run(); }
        finally { CurrentEvoOrigin54 = outer; }
    }
}

/// <summary>v0.54.0 "발휘하지 않는다" 계열 (BT17-092 · BT5-085)</summary>
public partial class GoldfishEngine
{
    /// <summary>owner가 등장시킨 테이머의 【등장 시】를 상대의 지속 효과가 막는지 ("all of your opponent's Tamers [On Play] effects don't activate" BT17-092)</summary>
    internal bool TamerOnPlayBlocked54(PlayerZones owner, CardData tamer)
    {
        if (CardScripts.For(tamer)?.Effects.Any(e => e.Trigger == EffectTrigger.OnPlay && e.Source == EffectSource.Main) != true) return false;
        if (OnPlayPeriodBlocked59(owner)) { EffectLog(owner, $"[{tamer.Name}]의 【등장 시】 효과는 상대의 효과로 발휘하지 않습니다."); return true; }   // v0.59.0 BT20-037 기간 효과
        var enemy = EnemyOf(owner);
        bool Blocks(CardData c) => CardScripts.For(c)?.BlockOpponentTamerOnPlay54 is { } f && f(enemy, owner);
        if (!enemy.Tamers.Distinct().Any(Blocks) && !enemy.BattleArea.Any(d => d.TamerProxyOf == null && Blocks(d.TopCard))) return false;
        EffectLog(owner, $"[{tamer.Name}]의 【등장 시】 효과는 상대의 효과로 발휘하지 않습니다.");
        Diag46.Hit("tamerOnPlayBlocked54");
        return true;
    }

    /// <summary>"The [When Digivolving] effects on level 7 Digimon don't activate" (BT5-085 — 어느 쪽 디지몬이든)</summary>
    internal bool WhenDigivolvingBlocked54(CardInstance target)
    {
        foreach (var side in new[] { State.Player, State.DummyOpponent })
            foreach (var d in side.BattleArea)
                if (CardScripts.For(d.TopCard)?.BlockWhenDigivolvingLevel54 is int lv && lv > 0 && target.TopCard.Level == lv)
                {
                    Diag46.Hit("whenDigivolvingBlocked54");
                    return true;
                }
        return false;
    }
}
