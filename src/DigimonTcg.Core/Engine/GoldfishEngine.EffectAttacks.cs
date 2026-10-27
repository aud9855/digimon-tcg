using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Ai;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>효과로 하는 어택이 고를 수 있는 대상: "may attack" = 둘 다, "may attack a player" = 시큐리티만, "may attack your opponent's Digimon" = 디지몬만</summary>
public enum EffectAttackTarget { Any, PlayerOnly, DigimonOnly }

/// <summary>
/// 효과로 하는 어택 ("This Digimon may attack" · "1 of your Digimon may attack your opponent's Digimon" · "~ may attack without suspending").
/// - 자기 턴에만, 어택할 수 있는 디지몬만 (등장한 턴 · 어택 불가 효과 확인. "without suspending"이면 레스트 상태여도 되고 레스트하지 않는다)
/// - 플레이어: 어택할지 · 대상을 고르고 곧바로 어택한다 (≪볼텍스≫ · ≪에그제큐트≫와 같은 어택 처리)
/// - AI: 정책(IAiPolicy.ChooseEffectAttack)이 어택할지 · 대상을 고르고, AI 어택 큐에 넣는다 (지금 처리 중인 효과 · 어택이 끝난 뒤 어택)
/// </summary>
public partial class GoldfishEngine
{
    // 다음 플레이어 어택의 맨 앞 레스트를 1번 건너뛴다 ("may attack without suspending")
    private bool _nextAttackWithoutSuspending;

    /// <summary>플레이어 어택 시작: "without suspending" 어택이 아니면 어택하는 디지몬을 레스트시킨다</summary>
    private void SuspendForAttack(CardInstance attacker)
    {
        bool skip = _nextAttackWithoutSuspending;
        _nextAttackWithoutSuspending = false;
        if (!skip) SuspendAsAttacker(attacker);
    }

    internal bool CanEffectAttack(PlayerZones owner, CardInstance d, bool withoutSuspending) =>
        !State.IsGameOver && owner.BattleArea.Contains(d) && IsTurnOf(owner)
        && (withoutSuspending || !d.IsSuspended)
        && (d.TurnPlayed < State.CurrentTurn || CardScripts.HasKeyword(d, Keyword.Rush))
        && !EffectRules.IsAttackLocked(State, d);

    /// <summary>
    /// 효과로 어택한다 (어택했거나 AI 어택 큐에 넣었으면 true).
    /// v0.39.0 forced = "that Digimon attacks" 같은 강제 처리 (종합 룰 15-9-1 · 공식 Q&A BT21-081 · BT20-102 "가능한 만큼 어택합니다") — 어택할 수 있으면 어택하지 않기를 고를 수 없다. 대상은 어택하는 쪽이 고른다
    /// </summary>
    internal bool EffectAttack(PlayerZones owner, CardInstance attacker, EffectAttackTarget allowed, bool withoutSuspending, string label, bool forced = false)
    {
        if (!CanEffectAttack(owner, attacker, withoutSuspending))
            return false;
        bool canSecurity = allowed != EffectAttackTarget.DigimonOnly && EffectRules.CanAttackPlayer(attacker);   // v0.34.0 "can't attack players"
        var targets = allowed == EffectAttackTarget.PlayerOnly
            ? new List<CardInstance>()
            : EnemyOf(owner).BattleArea.Where(t => EffectRules.CanBeAttackTargetFor(attacker, t)).ToList();
        if (!canSecurity && targets.Count == 0)
            return false;

        if (!ReferenceEquals(owner, State.Player))
        {
            if (_aiTurn is not { } ctx)
                return false;
            if (forced && ctx.Attackers.Contains(attacker))
                return false;   // 같은 시점의 두 번째 강제 어택은 어택하지 않는다 (Q&A EX5-048)
            var choice = AiPolicy.ChooseEffectAttack(this, attacker, targets, canSecurity);
            bool legal = choice is { } ch && (ch.Target == null ? canSecurity : targets.Contains(ch.Target));
            if (!legal && !forced)
                return false;
            ctx.QueuedTargets[attacker] = legal ? choice!.Value.Target : ForcedAiTarget39(attacker, targets, canSecurity);
            if (forced) Diag39.Hit(ref Diag39.ForcedAttack);
            if (withoutSuspending) ctx.NoSuspendAttackers.Add(attacker);
            ctx.Attackers.Enqueue(attacker);
            Log($"🤖 [{label}] AI [{attacker.TopCard.Name}]이(가) 효과로 어택합니다!");
            return true;
        }

        var best = BestWinningTarget(attacker, targets);
        CardInstance? defender = null;
        if (forced)
        {
            Diag39.Hit(ref Diag39.ForcedAttack);
            defender = ChooseForcedAttackTarget39(owner, attacker, targets, canSecurity, $"[{label}] [{attacker.TopCard.Name}]은(는) 반드시 어택합니다. 어택 대상을 고르세요.{(canSecurity ? " (고르지 않으면 시큐리티)" : "")}");
        }
        else if (!AskYesNo(owner, attacker, $"[{label}] [{attacker.TopCard.Name}]으로 어택할까요?", recommended: canSecurity || best != null))
            return false;
        else if (targets.Count > 0)
            defender = ChooseTargets(owner, AiEvaluator.Rank(targets, best), 1,
                $"[{label}] [{attacker.TopCard.Name}]의 어택 대상을 고르세요.{(canSecurity ? " (고르지 않으면 시큐리티)" : "")}",
                optional: canSecurity, preselected: canSecurity ? best : best ?? targets[0]).FirstOrDefault();
        if (!canSecurity && defender == null)
            return false;
        Log($"[{label}] [{attacker.TopCard.Name}] 효과로 어택!{(withoutSuspending ? " (레스트하지 않음)" : "")}");
        _nextAttackWithoutSuspending = withoutSuspending;
        if (defender != null) PlayerAttackOnDigimonCore(attacker, defender);
        else PlayerSecurityAttackCore(attacker);
        _nextAttackWithoutSuspending = false;
        return true;
    }
}
