using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.57.0 효과로 하는 어택은 효과 처리 중에 어택 선언 → 【어택 시】는 효과가 끝난 뒤 → 발휘 대기가 없어지면 카운터 · 블록 · 배틀 (31차 묶음).
///  - 종합 룰 11-1-3 · 11-1-4: 어택은 어택 선언 → 카운터 → 블록 → 판정 → 어택 종료. 어택 선언에서 유발한 【어택 시】 등을 모두 해결해야 카운터 타이밍
///  - 15-8-3-2: 유발형 효과는 효과의 처리 중에는 발휘할 수 없다 — 효과로 한 어택 선언의 【어택 시】는 그 효과가 끝난 뒤
///  - 11-2-3 · 11-2-4: 어택 중에는 새로 어택 선언을 할 수 없다
///  - 공식 Q&A BT5-086 Q1: ≪진격≫을 먼저 발휘하면 어택 선언과 레스트는 그 시점 — 그 뒤 다른 【진화 시】로 액티브로 할 수 있다
///  - 공식 Q&A BT20-036 Q1: 효과로 진화하고 그 디지몬으로 어택하면 【진화 시】와 【어택 시】는 동시 유발
///  - 공식 Q&A BT20-090 Q1: 1장째 효과로 어택 → 카운터 타이밍 전에 2장째 효과를 발휘할 수 있지만 어택 중이라 2장째로는 어택할 수 없다
/// 전에는 플레이어 쪽은 효과 한가운데에서 어택을 끝까지(배틀까지) 처리했고, AI 쪽은 어택 큐에 넣어 지금 효과 · 어택이 모두 끝난 뒤에야
/// 어택 선언(레스트)부터 했다 (어택 뒤 메인 페이즈에 쓴 옵션의 어택은 턴 종료 시 뒤로 밀렸다).
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>끄면 v0.56.0까지처럼 (플레이어는 바로 끝까지, AI는 큐) — 시뮬레이션 비교용</summary>
    public static bool EffectAttackDeclare57 { get; set; } = true;

    private sealed class DeclaredAttack57
    {
        public PlayerZones Owner = null!;
        public CardInstance Attacker = null!;
        public CardInstance? Target;      // 어택 대상 디지몬 (null = 플레이어)
        public bool ToDigimon;
        public bool TriggersDone;         // 【어택 시】 · "어택했을 때"를 처리했는지
    }

    /// <summary>효과로 선언했고 아직 끝나지 않은 어택</summary>
    private DeclaredAttack57? _declared57;

    /// <summary>어택 중인지 (선언 ~ 어택 종료) — 이때는 새로 어택 선언을 할 수 없다 (11-2-4)</summary>
    internal bool AttackInProgress57 => _declared57 != null || CurrentAttacker != null;

    /// <summary>효과 어택을 거절해야 하는지 (어택 중) — 거절하면 로그를 남긴다</summary>
    private bool RefuseEffectAttackDuringAttack57(PlayerZones owner, CardInstance attacker)
    {
        if (!EffectAttackDeclare57 || !AttackInProgress57) return false;
        Diag46.Hit("effectAttackDuringAttack57");
        EffectLog(owner, $"어택 중에는 새로 어택할 수 없어 [{attacker.TopCard.Name}]은(는) 어택하지 않습니다.");
        return true;
    }

    /// <summary>플레이어의 효과 어택 선언: 레스트 · 대상은 지금, 【어택 시】는 효과가 끝난 뒤, 배틀은 발휘 대기가 없어진 뒤</summary>
    private void DeclarePlayerEffectAttack57(PlayerZones owner, CardInstance attacker, CardInstance? defender, bool withoutSuspending)
    {
        var decl = new DeclaredAttack57 { Owner = owner, Attacker = attacker, Target = defender, ToDigimon = defender != null };
        _declared57 = decl;
        Diag46.Hit("effectAttackDeclared57");
        _playerAttackRedirect = null;
        if (!withoutSuspending) SuspendAsAttacker(attacker);
        State.BlitzAttacker = null;
        CurrentAttacker = attacker;
        Defer54(() => PlayerAttackTriggers57(decl));
        if (!Deferring54) RunDeclaredEffectAttack57();
    }

    private void PlayerAttackTriggers57(DeclaredAttack57 decl)
    {
        if (decl.TriggersDone) return;
        decl.TriggersDone = true;
        if (State.IsGameOver) return;
        var attacker = decl.Attacker;
        CurrentAttacker = attacker;
        BeginPlayerAttack63(decl.Target);   // v0.63.0 (GoldfishEngine.Batch63)
        TriggerWhenAttackingEffects(attacker, decl.Target);
        FireForSide(EffectTrigger.OpponentDigimonAttacks, State.DummyOpponent, attacker, triggerCard: decl.Target?.TopCard);
        FireForSide(EffectTrigger.AllyDigimonAttacks, State.Player, attacker, triggerCard: decl.Target?.TopCard);
        var redirected = _playerAttackRedirect is { } r && State.DummyOpponent.BattleArea.Contains(r) ? r : null;   // 상대 효과로 어택 대상 변경
        _playerAttackRedirect = null;
        if (ConsumeToPlayer63() && decl.ToDigimon) { decl.ToDigimon = false; redirected = null; }   // v0.63.0 효과로 플레이어로
        if (decl.ToDigimon)
        {
            var t = redirected ?? decl.Target!;
            decl.Target = ApplyAttackKeywords(attacker, t) ?? t;
        }
        else decl.Target = ApplyAttackKeywords(attacker, redirected) ?? redirected;
    }

    /// <summary>
    /// 플레이어의 효과 어택을 이어서 처리한다 (카운터 · 블록 · 배틀 · 어택 종료) — 처리 중인 효과 · 발휘 대기가 없을 때만.
    /// 메인 페이즈의 행동 끝(CheckTurnEndAndAutoReset) · 턴 종료 시 효과 뒤 · 메인 페이즈 개시 시 효과 뒤에 부른다
    /// </summary>
    internal void RunDeclaredEffectAttack57()
    {
        while (_declared57 is { } decl && ReferenceEquals(decl.Owner, State.Player))
        {
            if (_effectDepth > 0 || Deferring54 || _flushingTriggers) return;
            if (!decl.TriggersDone) PlayerAttackTriggers57(decl);
            var attacker = decl.Attacker;
            if (!State.IsGameOver)
            {
                Diag46.Hit("effectAttackBattle57");
                if (ConsumeAttackEnd(attacker)) Log($"[{attacker.TopCard.Name}]의 어택이 효과로 종료되었습니다.");
                else if (decl.Target is { } t) Log(ResolvePlayerAttackOnDigimon(attacker, t));
                else ResolveSecurityAttack(attacker);
            }
            _declared57 = null;
            EndAttack(attacker);
            if (State.IsGameOver) { _declared57 = null; return; }
        }
    }

    /// <summary>AI의 효과 어택 선언: 레스트 · 대상은 지금, 【어택 시】는 효과가 끝난 뒤, 카운터 · 블록 · 배틀은 AI 어택 큐의 맨 앞 (RunAiTurn)</summary>
    private void DeclareAiEffectAttack57(AiTurnContext ctx, CardInstance attacker, CardInstance? target, bool withoutSuspending)
    {
        var decl = new DeclaredAttack57 { Owner = State.DummyOpponent, Attacker = attacker, Target = target, ToDigimon = target != null };
        _declared57 = decl;
        Diag46.Hit("effectAttackDeclared57");
        if (!withoutSuspending) SuspendAsAttacker(attacker);
        State.OpponentAttackedThisTurn = true;
        CurrentAttacker = attacker;
        ctx.AttackTarget = target;   // 내 효과로 대상이 바뀌면 여기가 바뀐다 (RedirectAttack)
        ctx.Declared57.Add(attacker);
        ctx.QueuedTargets[attacker] = target;
        var rest = ctx.Attackers.ToList();
        ctx.Attackers.Clear();
        ctx.Attackers.Enqueue(attacker);
        foreach (var a in rest) ctx.Attackers.Enqueue(a);
        Defer54(() => AiAttackTriggers57(ctx, decl));
    }

    private void AiAttackTriggers57(AiTurnContext ctx, DeclaredAttack57 decl)
    {
        if (decl.TriggersDone) return;
        decl.TriggersDone = true;
        if (State.IsGameOver) return;
        var a = decl.Attacker;
        CurrentAttacker = a;
        TriggerWhenAttackingEffects(a, ctx.AttackTarget);
        FireForSide(EffectTrigger.OpponentDigimonAttacks, State.Player, a, triggerCard: ctx.AttackTarget?.TopCard);
        FireForSide(EffectTrigger.AllyDigimonAttacks, State.DummyOpponent, a, triggerCard: ctx.AttackTarget?.TopCard);
        ctx.AttackTarget = ApplyAttackKeywords(a, ctx.AttackTarget) ?? ctx.AttackTarget;
        decl.Target = ctx.AttackTarget;
    }

    /// <summary>RunAiTurn: 효과로 선언한 AI 어택을 꺼냈다 — 【어택 시】가 아직이면 지금 (보통은 선언한 효과가 끝날 때 이미 처리)</summary>
    private void EnsureAiDeclaredTriggers57(AiTurnContext ctx, CardInstance attacker)
    {
        if (_declared57 is { } d && ReferenceEquals(d.Attacker, attacker)) AiAttackTriggers57(ctx, d);
    }

    /// <summary>AI 어택이 끝났다: 효과로 선언한 어택이면 지운다 (【어택 종료 시】 효과가 새로 어택할 수 있도록 EndAttack 전에)</summary>
    private void ClearDeclaredAiAttack57(CardInstance attacker)
    {
        if (_declared57 is { } d && ReferenceEquals(d.Attacker, attacker)) _declared57 = null;
    }

    /// <summary>AI 턴의 메인 페이즈: 효과로 선언한 어택이 있으면 지금 처리해야 한다</summary>
    private static bool HasDeclaredAiAttack57(AiTurnContext ctx) => EffectAttackDeclare57 && ctx.Declared57.Count > 0;
}

/// <summary>
/// v0.57.0 "[Opponent's Turn] All of your opponent's Tamers with play costs of 2 or less can't suspend." (EX5-052) ·
/// 트래시에서 코스트를 내고 등장시키며 트래시의 카드로 디지크로스 (BT10-104 — "If you play a Digimon card with DigiXros requirements by this effect, you may DigiXros by placing any of the specified Digimon from your trash")
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>owner의 테이머가 상대 디지몬의 지속 효과로 레스트할 수 없는지 (그 효과는 [상대의 턴] = owner의 턴)</summary>
    private bool TamerSuspendLockStatic57(PlayerZones owner, CardData tamer)
    {
        if (!IsTurnOf(owner)) return false;
        foreach (var d in EnemyOf(owner).BattleArea)
            if (d.TamerProxyOf == null && CardScripts.For(d.TopCard)?.OpponentTamerSuspendLock57 is { } f && f(tamer))
            {
                Diag46.Hit("tamerSuspendLock57");
                return true;
            }
        return false;
    }

    /// <summary>지금 처리 중인 효과 등장이 트래시의 카드로 디지크로스할 수 있는지 (BT10-104)</summary>
    internal int EffectXrosFromTrash57 { get; set; }

    // 효과 등장에서 아래에 놓을 디지크로스 재료 (PlayWithoutCostCore가 새 디지몬에 놓는다)
    private List<CardData>? _pendingXros57;

    // 효과 등장으로 디지크로스한 디지몬 → (등장한 카드, 장수, 턴) — 그 등장의 【등장 시】 "if DigiXrosing"
    private readonly Dictionary<CardInstance, (CardData Card, int Count, int Turn)> _xrosByEffect57 = new(ReferenceEqualityComparer.Instance);

    private int XrosByEffect57(CardInstance digimon) =>
        _xrosByEffect57.TryGetValue(digimon, out var e) && e.Turn == State.TurnSequence && ReferenceEquals(digimon.TopCard, e.Card) ? e.Count : 0;

    /// <summary>트래시의 디지몬 카드로 디지크로스 재료를 고르고 트래시에서 떼어 낸다 (위 → 아래). 하지 않으면 빈 목록</summary>
    private List<CardData> TakeXrosFromTrash57(PlayerZones owner, CardData card, XrosRequirement req)
    {
        var taken = new List<CardData>();
        var pool = owner.Trash.Where(c => c.Type == CardType.Digimon && !ReferenceEquals(c, card)).ToList();
        if (!req.Slots.Any(s => pool.Any(s.Match))) return taken;
        if (!AskYesNo(owner, null, $"[{card.Name}]을(를) 트래시의 카드로 디지크로스할까요? (1장마다 코스트 -{req.Reduction})", recommended: true)) return taken;
        var used = new HashSet<CardData>(ReferenceEqualityComparer.Instance);
        foreach (var slot in req.Slots)
        {
            var cands = pool.Where(c => !used.Contains(c) && slot.Match(c)).ToList();
            if (cands.Count == 0) continue;
            int max = Math.Min(slot.Count, cands.Count);
            var idx = ChooseCardIndexes(owner, CardChoiceZone.Other, cands, max, upTo: true,
                $"[{card.Name}] 디지크로스 ({slot.Text}) — 트래시에서 이 카드 아래에 놓을 카드를 고르세요 (1장마다 코스트 -{req.Reduction}).");
            foreach (var c in idx.Select(i => cands[i]))
            {
                if (slot.Distinct && taken.Any(x => x.CardId == c.CardId)) continue;
                used.Add(c);
                taken.Add(c);
            }
        }
        foreach (var c in taken) owner.Trash.Remove(c);
        if (taken.Count > 0)
        {
            EffectLog(owner, $"[디지크로스] [{card.Name}] 아래에 트래시의 [{string.Join(", ", taken.Select(c => c.Name))}] — 코스트 -{req.Reduction * taken.Count}");
            Diag46.Hit("xrosFromTrashByEffect57");
        }
        return taken;
    }
}
