using System.Collections.Generic;
using System;
using System.Linq;
using DigimonTcg.Core.Engine.Ai;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.32.0 효과로 준 효과 ("1 of your opponent's Digimon gains "[On Deletion] Lose 1 memory." until ~" · "give 1 of their Digimon "[Start of Your Main Phase] This Digimon attacks."") —
/// 그 디지몬(CardInstance.GrantedEffects)의 맨 위 카드 효과처럼 그 디지몬의 주인이 발휘한다. 공식 Q&A EX10-034: 효과를 받지 않는 디지몬에게도 줄 수 있지만,
/// 유발할 때 그 디지몬이 효과를 받지 않으면 유발하지 않는다. 진화해도 남는다 (같은 디지몬).
/// · "This Digimon attacks." (강제 어택): 어택할 수 있으면 반드시 어택한다 — 대상은 어택하는 쪽이 고른다. 공식 Q&A EX5-048: 같은 시점에 여럿이어도 어택은 1번
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>준 효과 중 지금 유효한 것 (기간 안 · 받는 디지몬이 준 쪽의 효과를 받을 수 있을 때)</summary>
    internal IEnumerable<CardData> ActiveGrantedEffects(CardInstance self) =>
        self.GrantedEffects.Where(g => EffectRules.IsActive(g.Until, State) && (g.Granter == null || !IsUnaffectedBy(g.Granter, self)))
            .Select(g => g.Effect).ToList();

    /// <summary>"This Digimon attacks." — 어택할 수 있으면 반드시 어택한다 (플레이어는 대상을 고르고, AI는 정책이 고른다)</summary>
    internal bool ForcedAttack(PlayerZones owner, CardInstance attacker, string label)
    {
        if (!CanEffectAttack(owner, attacker, withoutSuspending: false))
            return false;
        var targets = EnemyOf(owner).BattleArea.Where(EffectRules.CanBeAttackTarget).ToList();
        if (!ReferenceEquals(owner, State.Player))
        {
            if (_aiTurn is not { } ctx || ctx.Attackers.Contains(attacker)) return false;   // 같은 시점의 두 번째 강제 어택은 어택하지 않는다 (Q&A EX5-048)
            var choice = AiPolicy.ChooseEffectAttack(this, attacker, targets, canAttackSecurity: true);
            ctx.QueuedTargets[attacker] = choice is { Target: { } chosen } && targets.Contains(chosen) ? chosen : null;
            ctx.Attackers.Enqueue(attacker);
            Log($"🤖 [{label}] AI [{attacker.TopCard.Name}]이(가) 효과로 어택합니다!");
            return true;
        }
        var best = BestWinningTarget(attacker, targets);
        CardInstance? defender = null;
        if (targets.Count > 0)
            defender = ChooseTargets(owner, AiEvaluator.Rank(targets, best), 1,
                $"[{label}] [{attacker.TopCard.Name}]은(는) 반드시 어택합니다. 어택 대상을 고르세요. (고르지 않으면 시큐리티)",
                optional: true, preselected: best).FirstOrDefault();
        Log($"[{label}] [{attacker.TopCard.Name}] 효과로 어택!");
        if (defender != null) PlayerAttackOnDigimonCore(attacker, defender);
        else PlayerSecurityAttackCore(attacker);
        return true;
    }
}

/// <summary>v0.32.0 효과로 등장한 카드가 있던 곳 ("When you play a Digimon from your trash" · "~ is played from digivolution cards")</summary>
internal enum PlayOrigin { Unknown, Hand, Trash, Sources, UnderTamers }

public partial class GoldfishEngine
{
    /// <summary>지금 등장하는 카드가 있던 곳 — 효과로 등장시키는 중이면 그 효과가 알려 준 곳, 아니면 패 (코스트를 내는 보통 등장)</summary>
    internal PlayOrigin CurrentPlayOrigin => PlayingByEffect ? _playOrigin : PlayOrigin.Hand;
    private PlayOrigin _playOrigin;

    /// <summary>있던 곳을 알리고 코스트 없이 등장시킨다</summary>
    internal void PlayWithoutCostFrom(PlayerZones owner, CardData card, PlayOrigin origin, bool triggerOnPlay = true, bool suspended = false)
    {
        var outer = _playOrigin;
        _playOrigin = origin;
        try { PlayWithoutCost(owner, card, triggerOnPlay, suspended); }
        finally { _playOrigin = outer; }
    }

    /// <summary>
    /// v0.32.0 지금 레스트한 디지몬 · 테이머를 레스트시킨 효과의 주인 (효과 · 효과의 비용이 아니면 null — 어택 · 블록은 아니다).
    /// "When an effect suspends ~" · "When one of your effects suspends a Tamer" (사건 처리 중 RunEffects가 CurrentEffectOwner를 바꾸므로 레스트할 때 적어 둔다)
    /// </summary>
    internal PlayerZones? SuspendCause { get; private set; }

    private T WithSuspendCause<T>(Func<T> fire)
    {
        var outer = SuspendCause;
        SuspendCause = SuspendingForAttack ? null : CurrentEffectOwner;
        try { return fire(); }
        finally { SuspendCause = outer; }
    }
}
