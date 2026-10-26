using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Ai;

/// <summary>
/// 정책을 분리하기 전 AI와 같은 결정 (승률 비교 기준):
/// - 진화: 필드(배틀 → 육성) 순서대로, 패에서 처음 찾은 낼 수 있는 진화
/// - 어택: 어택할 수 있는 디지몬 전부가 DP 높은 순으로 시큐리티를 어택 (혼자 남은 ≪블로커≫는 지킨다)
/// - 옵션: 쓰지 않음 · [메인] 기동: 효과 문장 점수가 0보다 크면
/// - 테이머: 필드에 2장 미만이면 패의 첫 테이머 · 디지몬: Lv.4 이하 중 코스트 큰 것 → 코스트 작은 것
/// </summary>
public sealed class LegacyAiPolicy : IAiPolicy
{
    public string Name => "기존 AI";
    public int MaxMemoryHandOver => 3;

    public AiEvolution? ChooseEvolution(GoldfishEngine engine, int memory, bool beforeAttacks)
    {
        var ai = engine.State.DummyOpponent;
        int limit = beforeAttacks ? memory : memory + MaxMemoryHandOver;
        foreach (var field in EvolutionBases(ai))
        {
            var card = EffectRules.IsDigivolveLocked(engine.State, field) ? null : ai.Hand.FirstOrDefault(c => ActionValidator.EvolutionCost(field, c) <= limit && ActionValidator.ValidateEvolution(field, c).CanExecute);
            if (card != null)
                return new AiEvolution(field, card);
        }
        return null;
    }

    public AiOptionUse? ChooseOption(GoldfishEngine engine, int memory, bool beforeAttacks) => null;

    public AiAttack? ChooseNextAttack(GoldfishEngine engine, int memory)
    {
        var ai = engine.State.DummyOpponent;
        var attacker = ai.BattleArea
            .Where(d => engine.CanAttackThisTurn(d) && (!d.HasBlocker || ai.BattleArea.Count >= 2))
            .OrderByDescending(d => d.GetCurrentDP())
            .FirstOrDefault();
        return attacker == null ? null : new AiAttack(attacker, null);
    }

    public bool ShouldUseMainAbility(GoldfishEngine engine, GoldfishEngine.MainAbility ability, int memory) =>
        AiEvaluator.EstimateSentenceValue(ability.Inherited ? ability.Card.InheritedEffectText : ability.Card.EffectText) > 0;

    public CardData? ChooseTamerToPlay(GoldfishEngine engine, int memory)
    {
        var ai = engine.State.DummyOpponent;
        return ai.Tamers.Count < 2 ? ai.Hand.FirstOrDefault(c => c.Type == CardType.Tamer) : null;
    }

    public CardData? ChooseDigimonToPlay(GoldfishEngine engine, int memory)
    {
        var ai = engine.State.DummyOpponent;
        return ai.Hand
            .Where(c => c.Type == CardType.Digimon && !c.IsDual && CardScripts.EffectivePlayCost(ai, c) <= memory + MaxMemoryHandOver)
            .OrderByDescending(c => c.Level <= 4 ? c.PlayCost : 0)
            .ThenBy(c => c.PlayCost)
            .FirstOrDefault();
    }

    /// <summary>정책을 분리하기 전 AI는 메인 페이즈에 스스로 DNA 진화하지 않았다</summary>
    public AiDnaEvolution? ChooseDnaEvolution(GoldfishEngine engine, int memory, bool beforeAttacks) => null;

    /// <summary>
    /// 정책을 분리하기 전 엔진 동작: 배틀에서 이기는(상대만 소멸하는) 대상 중 Lv → DP가 가장 높은 디지몬, 없으면 어택하지 않는다
    /// (시큐리티는 어택하지 않는다).
    /// </summary>
    public EndOfTurnAttack? ChooseEndOfTurnAttack(GoldfishEngine engine, CardInstance attacker, IReadOnlyList<CardInstance> targets, bool canAttackSecurity)
    {
        var best = targets
            .Where(t => attacker.GetCurrentDP() > t.GetCurrentDP())
            .OrderByDescending(t => t.TopCard.Level)
            .ThenByDescending(t => t.GetCurrentDP())
            .FirstOrDefault();
        return best == null ? null : new EndOfTurnAttack(best);
    }

    /// <summary>
    /// 정책을 분리하기 전 AI는 효과 어택이 없었다 — 턴 종료 시 어택과 같이 이기는 대상을 고르고,
    /// 없으면 보통 어택처럼 시큐리티 (시큐리티를 어택할 수 있을 때)
    /// </summary>
    public EndOfTurnAttack? ChooseEffectAttack(GoldfishEngine engine, CardInstance attacker, IReadOnlyList<CardInstance> targets, bool canAttackSecurity) =>
        ChooseEndOfTurnAttack(engine, attacker, targets, canAttackSecurity) ?? (canAttackSecurity ? new EndOfTurnAttack(null) : null);

    /// <summary>AI가 진화시킬 수 있는 디지몬: 배틀 에어리어 + 육성 에어리어 (육성 중인 Lv.2 → Lv.3 진화 포함)</summary>
    internal static List<CardInstance> EvolutionBases(PlayerZones ai)
    {
        var bases = ai.BattleArea.ToList();
        if (ai.BreedingArea != null)
            bases.Add(ai.BreedingArea);
        return bases;
    }
}
