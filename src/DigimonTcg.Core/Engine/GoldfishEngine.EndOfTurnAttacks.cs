using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Ai;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// 턴 종료 시 어택 (효과 엔진 7단계): ≪볼텍스≫ · ≪에그제큐트≫.
/// - [자신의 턴 종료 시] 효과(액티브로 되돌리는 효과 등)를 처리한 뒤, 액티브 상태인 ≪볼텍스≫ · ≪에그제큐트≫ 디지몬이 1번씩 어택할 수 있다
/// - ≪볼텍스≫: 상대의 디지몬(레스트 — 보통 어택 대상)에게만, 등장한 턴에도
/// - ≪에그제큐트≫: 시큐리티 · 상대의 디지몬(액티브도). 그 어택이 끝나면 이 디지몬을 소멸 (어택 종료 시 — EndAttack)
/// 플레이어는 어택할지 · 대상을 고르고(추천 = 배틀에서 이기는 대상이 있을 때), AI는 정책(IAiPolicy.ChooseEndOfTurnAttack)이 고른다.
/// </summary>
public partial class GoldfishEngine
{
    // 이번 턴 종료 단계에서 이미 턴 종료 시 어택을 한 디지몬
    private readonly HashSet<CardInstance> _endOfTurnAttacked = new(ReferenceEqualityComparer.Instance);

    // ≪에그제큐트≫로 어택 중인 디지몬 — 어택 종료 시 소멸
    private CardInstance? _executingAttacker;

    /// <summary>이 디지몬이 지금 ≪에그제큐트≫로 어택 중인지 ("If attacking by ＜Execute＞")</summary>
    internal bool IsExecuteAttack(CardInstance? d) => d != null && ReferenceEquals(_executingAttacker, d);

    private bool CanEndOfTurnAttack(PlayerZones owner, CardInstance d) =>
        owner.BattleArea.Contains(d) && !d.IsSuspended && !_endOfTurnAttacked.Contains(d) && !EffectRules.IsAttackLocked(State, d)
        && (CardScripts.HasKeyword(d, Keyword.Vortex) || CardScripts.HasKeyword(d, Keyword.Execute) || CanEngage(owner, d));

    // ---- ≪급습≫ (종합 룰 16-44, v0.29.0): 자신의 턴 종료 시, 이 디지몬으로 어택할 수 있다 (보통 어택 — 레스트하고, 등장한 턴에는 ≪속공≫이 있어야 한다) ----
    private bool CanEngage(PlayerZones owner, CardInstance d) =>
        CardScripts.HasKeyword(d, Keyword.Engage) && (d.TurnPlayed < State.CurrentTurn || CardScripts.HasKeyword(d, Keyword.Rush));

    private bool IsEngageOnly(CardInstance d) => !CardScripts.HasKeyword(d, Keyword.Vortex) && !CardScripts.HasKeyword(d, Keyword.Execute);

    // ---- ≪오버클럭≫ (종합 룰 16-34, v0.29.0): 자신의 턴 종료 시, 자신의 토큰 또는 지정된 다른 자신의 디지몬 1마리를 소멸시키는 것으로(임의),
    //      이 디지몬으로 레스트하지 않고 플레이어에게 어택한다(강제). 디지몬마다 턴 종료 시 1번 ----
    private readonly HashSet<CardInstance> _overclocked = new(ReferenceEqualityComparer.Instance);

    /// <summary>≪오버클럭≫으로 소멸시킬 수 있는 자신의 디지몬 (토큰 · 지정된 다른 디지몬) — 가치가 낮은 순</summary>
    internal List<CardInstance> OverclockFodder(PlayerZones owner, CardInstance attacker)
    {
        if (CardScripts.For(attacker.TopCard)?.OverclockSpecified is not { } spec)
            return new List<CardInstance>();
        return owner.BattleArea.Where(d => !ReferenceEquals(d, attacker) && (IsToken(d.TopCard) || spec(d.TopCard)))
            .OrderByDescending(d => IsToken(d.TopCard)).ThenBy(d => d.TopCard.Level).ThenBy(d => d.GetCurrentDP()).ToList();
    }

    /// <summary>≪오버클럭≫ 어택을 할 수 있는지 (레스트하지 않으므로 레스트 상태여도 된다. 어택 조건은 효과 어택과 같게 — 등장한 턴은 ≪속공≫)</summary>
    private bool CanOverclock(PlayerZones owner, CardInstance d) =>
        owner.BattleArea.Contains(d) && !_overclocked.Contains(d) && CardScripts.For(d.TopCard)?.OverclockSpecified != null
        && !EffectRules.IsAttackLocked(State, d) && (d.TurnPlayed < State.CurrentTurn || CardScripts.HasKeyword(d, Keyword.Rush))
        && OverclockFodder(owner, d).Count > 0;

    /// <summary>≪오버클럭≫ 비용: 고른 디지몬을 소멸시킨다 (소멸을 막았으면 어택하지 않는다)</summary>
    private bool PayOverclock(PlayerZones owner, CardInstance attacker, CardInstance fodder)
    {
        _overclocked.Add(attacker);
        EffectLog(owner, $"[≪오버클럭≫] [{fodder.TopCard.Name}]을(를) 소멸시켜 [{attacker.TopCard.Name}]으로 레스트하지 않고 플레이어에게 어택합니다.");
        DeleteFrom(owner, fodder, DeletionCause.Cost);
        return !owner.BattleArea.Contains(fodder) && owner.BattleArea.Contains(attacker) && !State.IsGameOver;
    }

    /// <summary>플레이어 턴 종료: ≪오버클럭≫ 디지몬마다 소멸시킬 디지몬을 골라 어택할지 묻는다</summary>
    private void RunPlayerOverclocks()
    {
        var owner = State.Player;
        foreach (var attacker in owner.BattleArea.ToList())
        {
            if (State.IsGameOver) return;
            if (!CanOverclock(owner, attacker)) continue;
            var fodder = OverclockFodder(owner, attacker);
            bool recommended = IsToken(fodder[0].TopCard) || EnemyOf(owner).SecurityStack.Count <= 1;
            if (!AskYesNo(owner, attacker, $"≪오버클럭≫ [{attacker.TopCard.Name}]: 토큰 또는 지정된 디지몬 1마리를 소멸시켜, 레스트하지 않고 플레이어에게 어택할까요?", recommended))
                continue;
            var pick = ChooseTargets(owner, fodder, 1, "≪오버클럭≫ 소멸시킬 디지몬을 고르세요.", optional: true, preselected: null).FirstOrDefault();
            if (pick == null || !PayOverclock(owner, attacker, pick)) continue;
            _nextAttackWithoutSuspending = true;
            PlayerSecurityAttackCore(attacker);
            _nextAttackWithoutSuspending = false;
        }
    }

    /// <summary>AI 턴 종료: ≪오버클럭≫ (토큰이면 하고, 지정 디지몬이면 어택하는 디지몬보다 훨씬 약하고 상대 시큐리티가 적을 때) — 비용을 내고 어택할 디지몬</summary>
    private CardInstance? NextAiOverclock(AiTurnContext ctx)
    {
        var owner = State.DummyOpponent;
        foreach (var attacker in owner.BattleArea.ToList())
        {
            if (!CanOverclock(owner, attacker)) continue;
            var fodder = OverclockFodder(owner, attacker)[0];
            bool worth = IsToken(fodder.TopCard) || State.Player.SecurityStack.Count == 0
                         || AiBoard.Value(fodder) * 2 < AiBoard.Value(attacker) && State.Player.SecurityStack.Count <= 3;
            if (!worth) { _overclocked.Add(attacker); continue; }
            if (!PayOverclock(owner, attacker, fodder)) continue;
            ctx.NoSuspendAttackers.Add(attacker);
            return attacker;
        }
        return null;
    }

    /// <summary>턴 종료 시 어택의 대상 후보: ≪볼텍스≫ = 어택할 수 있는(레스트) 상대 디지몬, ≪에그제큐트≫ = 액티브도</summary>
    private List<CardInstance> EndOfTurnTargets(PlayerZones owner, bool execute) =>
        EnemyOf(owner).BattleArea.Where(t => execute ? !CardScripts.HasKeyword(t, Keyword.CannotBeAttacked) : EffectRules.CanBeAttackTarget(t)).ToList();

    /// <summary>배틀에서 이기는(상대만 소멸하는) 대상 중 가장 좋은 것 (없으면 null)</summary>
    private static CardInstance? BestWinningTarget(CardInstance attacker, IEnumerable<CardInstance> targets) =>
        targets.Where(t => attacker.GetCurrentDP() > t.GetCurrentDP())
            .OrderByDescending(t => t.TopCard.Level).ThenByDescending(t => t.GetCurrentDP()).FirstOrDefault();

    /// <summary>플레이어 턴 종료: ≪볼텍스≫ · ≪에그제큐트≫ 디지몬이 차례로 어택할지 고른다</summary>
    private void RunPlayerEndOfTurnAttacks()
    {
        _endOfTurnAttacked.Clear();
        _overclocked.Clear();
        var owner = State.Player;
        foreach (var attacker in owner.BattleArea.ToList())
        {
            if (State.IsGameOver) return;
            if (!CanEndOfTurnAttack(owner, attacker)) continue;
            if (IsEngageOnly(attacker))
            {
                RunPlayerEngage(owner, attacker);
                continue;
            }
            bool execute = CardScripts.HasKeyword(attacker, Keyword.Execute) && !CardScripts.HasKeyword(attacker, Keyword.Vortex)
                           || (CardScripts.HasKeyword(attacker, Keyword.Execute) && EndOfTurnTargets(owner, execute: false).Count == 0);
            var targets = EndOfTurnTargets(owner, execute);
            if (!execute && targets.Count == 0) continue;   // ≪볼텍스≫는 상대 디지몬에게만
            var best = BestWinningTarget(attacker, targets);
            string kw = execute ? "≪에그제큐트≫" : "≪볼텍스≫";
            if (!AskYesNo(owner, attacker, $"{kw} [{attacker.TopCard.Name}]으로 턴 종료 시 어택할까요?{(execute ? " (어택이 끝나면 이 디지몬은 소멸합니다)" : "")}", recommended: best != null))
                continue;
            CardInstance? defender = null;
            if (targets.Count > 0)
            {
                var ranked = AiEvaluator.Rank(targets, best);
                defender = ChooseTargets(owner, ranked, 1, $"{kw} [{attacker.TopCard.Name}]의 어택 대상을 고르세요.{(execute ? " (고르지 않으면 시큐리티)" : "")}",
                    optional: execute, preselected: best).FirstOrDefault();
            }
            if (!execute && defender == null) continue;
            _endOfTurnAttacked.Add(attacker);
            if (execute) _executingAttacker = attacker;
            Log($"[{kw}] [{attacker.TopCard.Name}] 턴 종료 시 어택!");
            if (defender != null) PlayerAttackOnDigimonCore(attacker, defender);
            else PlayerSecurityAttackCore(attacker);
        }
        RunPlayerOverclocks();
    }

    /// <summary>≪급습≫: 보통 어택처럼 시큐리티 또는 레스트 상태인 상대 디지몬에게 (어택할지 · 대상을 고른다)</summary>
    private void RunPlayerEngage(PlayerZones owner, CardInstance attacker)
    {
        var targets = EndOfTurnTargets(owner, execute: false);
        var best = BestWinningTarget(attacker, targets);
        if (!AskYesNo(owner, attacker, $"≪급습≫ [{attacker.TopCard.Name}]으로 턴 종료 시 어택할까요?", recommended: true))
            return;
        CardInstance? defender = null;
        if (targets.Count > 0)
            defender = ChooseTargets(owner, AiEvaluator.Rank(targets, best), 1, $"≪급습≫ [{attacker.TopCard.Name}]의 어택 대상을 고르세요. (고르지 않으면 시큐리티)",
                optional: true, preselected: best).FirstOrDefault();
        _endOfTurnAttacked.Add(attacker);
        Log($"[≪급습≫] [{attacker.TopCard.Name}] 턴 종료 시 어택!");
        if (defender != null) PlayerAttackOnDigimonCore(attacker, defender);
        else PlayerSecurityAttackCore(attacker);
    }

    /// <summary>AI 턴 종료: 다음 턴 종료 시 어택 (정책이 고른 것 — Target null = 시큐리티). 없으면 null</summary>
    private (CardInstance Attacker, CardInstance? Target, bool Execute, string Label)? NextAiEndOfTurnAttack(AiTurnContext ctx)
    {
        var owner = State.DummyOpponent;
        foreach (var attacker in owner.BattleArea.ToList())
        {
            if (!CanEndOfTurnAttack(owner, attacker)) continue;
            if (IsEngageOnly(attacker))
            {
                // ≪급습≫: 보통 어택과 같은 점수로 (시큐리티 · 레스트 상태인 상대 디지몬)
                var engageTargets = EndOfTurnTargets(owner, execute: false);
                _endOfTurnAttacked.Add(attacker);
                if (AiPolicy.ChooseEffectAttack(this, attacker, engageTargets, canAttackSecurity: true) is not { } ec) continue;
                if (ec.Target != null && !engageTargets.Contains(ec.Target)) continue;
                return (attacker, ec.Target, false, "≪급습≫");
            }
            // 정책(IAiPolicy.ChooseEndOfTurnAttack)에 묻는다 — ≪볼텍스≫로 먼저, 고르지 않으면 ≪에그제큐트≫로
            EndOfTurnAttack? choice = null;
            bool execute = false;
            if (CardScripts.HasKeyword(attacker, Keyword.Vortex) && EndOfTurnTargets(owner, execute: false) is { Count: > 0 } vortexTargets)
                choice = AiPolicy.ChooseEndOfTurnAttack(this, attacker, vortexTargets, canAttackSecurity: false);
            if (choice is { Target: null }) choice = null;   // ≪볼텍스≫는 상대 디지몬에게만
            if (choice == null && CardScripts.HasKeyword(attacker, Keyword.Execute))
            {
                choice = AiPolicy.ChooseEndOfTurnAttack(this, attacker, EndOfTurnTargets(owner, execute: true), canAttackSecurity: true);
                execute = choice != null;
            }
            if (choice is not { } c) continue;
            var targets = EndOfTurnTargets(owner, execute);
            if (c.Target != null && !targets.Contains(c.Target)) continue;   // 후보에 없는 대상은 무시
            _endOfTurnAttacked.Add(attacker);
            if (execute) _executingAttacker = attacker;
            return (attacker, c.Target, execute, execute ? "≪에그제큐트≫" : "≪볼텍스≫");
        }
        if (NextAiOverclock(ctx) is { } oc)
            return (oc, null, false, "≪오버클럭≫");
        return null;
    }

    /// <summary>어택 종료 시: ≪에그제큐트≫로 어택한 디지몬을 소멸</summary>
    private void FinishExecuteAttack(CardInstance attacker)
    {
        if (!ReferenceEquals(_executingAttacker, attacker)) return;
        _executingAttacker = null;
        var owner = OwnerOf(attacker);
        if (!owner.BattleArea.Contains(attacker) || State.IsGameOver) return;
        EffectLog(owner, $"[≪에그제큐트≫] 어택이 끝나 [{attacker.TopCard.Name}]을(를) 소멸시킵니다.");
        DeleteFrom(owner, attacker, DeletionCause.Effect);
    }
}
