using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>v0.39.0 미지원 문장 13차 묶음의 엔진 쪽 (SentenceParser.Batch39.cs) — 시뮬레이션에서 새 동작이 실제로 돈 횟수</summary>
public static class Diag39
{
    public static int ScheduledReturn, ScheduledDelete, ForcedAttack, PlayFromSources, ReturnOrPlay, SecurityToHand, EndOfBattle, CapForEach, OppTrashCost, ThoseSourcesCost, NameExclude, LevelLeIt, SecurityTrash, SecuritySearch, SourcePlace;
    public static void Hit(ref int counter) => System.Threading.Interlocked.Increment(ref counter);
    public static void Reset() => ScheduledReturn = ScheduledDelete = ForcedAttack = PlayFromSources = ReturnOrPlay = SecurityToHand = EndOfBattle = CapForEach = OppTrashCost = ThoseSourcesCost = NameExclude = LevelLeIt = SecurityTrash = SecuritySearch = SourcePlace = 0;
}

public partial class GoldfishEngine
{
    // v0.39.0 [시큐리티] "At the end of the battle, ~" — 시큐리티 디지몬의 배틀이 끝난 뒤 (다음 시큐리티 체크 전 · 체크가 모두 끝난 뒤)
    private readonly List<System.Action> _afterSecurityBattle39 = new();

    internal void QueueAfterSecurityBattle39(System.Action run) => _afterSecurityBattle39.Add(run);

    // v0.39.0 "At the next end of your opponent's turn, return it to the hand" (BT16-084 — 이 효과로 등장한 그 디지몬) — 정한 턴의 종료 시 (소멸 예약 다음)
    private readonly List<(CardInstance Digimon, PlayerZones EffectOwner, int TurnSequence)> _scheduledReturns39 = new();

    internal void ScheduleReturnToHand39(CardInstance digimon, PlayerZones effectOwner, int turnSequence) => _scheduledReturns39.Add((digimon, effectOwner, turnSequence));

    // v0.39.0 "At the end of your opponent's next turn, play 1 [Ravemon] from your trash ~" (BT13-089) — 정한 턴의 종료 시에 처리할 동작
    private readonly List<(System.Action Run, PlayerZones EffectOwner, int TurnSequence)> _scheduledActs39 = new();

    internal void ScheduleAct39(System.Action run, PlayerZones effectOwner, int turnSequence) => _scheduledActs39.Add((run, effectOwner, turnSequence));

    private void RunScheduledActs39()
    {
        if (_scheduledActs39.Count == 0) return;
        var due = _scheduledActs39.Where(x => x.TurnSequence <= State.TurnSequence).ToList();
        _scheduledActs39.RemoveAll(x => x.TurnSequence <= State.TurnSequence);
        foreach (var (run, effectOwner, _) in due)
        {
            if (State.IsGameOver) return;
            var outer = CurrentEffectOwner;
            CurrentEffectOwner = effectOwner;
            _effectDepth++;
            try { run(); }
            finally { _effectDepth--; CurrentEffectOwner = outer; }
            Diag39.Hit(ref Diag39.ScheduledReturn);
            FlushPendingTriggers();
        }
        RecalculateFieldBuffs();
    }

    private void RunScheduledReturns39()
    {
        RunScheduledActs39();
        if (_scheduledReturns39.Count == 0) return;
        var due = _scheduledReturns39.Where(x => x.TurnSequence <= State.TurnSequence).ToList();
        _scheduledReturns39.RemoveAll(x => x.TurnSequence <= State.TurnSequence);
        foreach (var (digimon, effectOwner, _) in due)
        {
            if (State.IsGameOver) return;
            var zones = State.Player.BattleArea.Contains(digimon) ? State.Player : State.DummyOpponent.BattleArea.Contains(digimon) ? State.DummyOpponent : null;
            if (zones == null) continue;
            EffectLog(zones, $"[턴 종료 시] 효과로 등장한 [{digimon.TopCard.Name}]을(를) 패로 되돌립니다.");
            var outer = CurrentEffectOwner;
            CurrentEffectOwner = effectOwner;
            _effectDepth++;
            try { ReturnToHand(zones, digimon); }
            finally { _effectDepth--; CurrentEffectOwner = outer; }
            Diag39.Hit(ref Diag39.ScheduledReturn);
            FlushPendingTriggers();
        }
        RecalculateFieldBuffs();
    }

    private void FlushAfterSecurityBattle39()
    {
        int guard = 0;
        while (_afterSecurityBattle39.Count > 0 && guard++ < 20)
        {
            var list = _afterSecurityBattle39.ToList();
            _afterSecurityBattle39.Clear();
            foreach (var run in list)
            {
                if (State.IsGameOver) { _afterSecurityBattle39.Clear(); return; }
                run();
            }
            RecalculateFieldBuffs();
        }
        _afterSecurityBattle39.Clear();
    }

    /// <summary>
    /// v0.39.0 "[On Play] effects on Digimon played by this effect don't activate" — 그 디지몬의 【등장 시】만 발휘하지 않는다.
    /// 다른 카드의 "디지몬이 등장했을 때" 효과는 그대로 유발한다 (EX5-060: 상대에게 등장시킨 뒤 "효과로 상대의 디지몬이 등장했을 때"). 전에는 둘 다 유발하지 않았다
    /// </summary>
    private void FirePlayedTriggers39(PlayerZones owner, CardInstance instance)
    {
        foreach (var other in owner.BattleArea.Where(d => !ReferenceEquals(d, instance)).ToList())
            RunEffects(EffectTrigger.AllySameNamePlayed, owner, other, null, other: instance);
        FireForSide(EffectTrigger.AllyDigimonPlayed, owner, instance);
        FireForSide(EffectTrigger.OpponentDigimonPlayed, EnemyOf(owner), instance);
        RecalculateFieldBuffs();
    }

    /// <summary>강제 어택에서 AI 정책이 고르지 않았을 때의 대상: 이기는 대상 → 시큐리티 → DP가 가장 낮은 디지몬</summary>
    private static CardInstance? ForcedAiTarget39(CardInstance attacker, IReadOnlyList<CardInstance> targets, bool canSecurity) =>
        BestWinningTarget(attacker, targets) ?? (canSecurity || targets.Count == 0 ? null : targets.OrderBy(t => t.GetCurrentDP()).First());

    /// <summary>
    /// v0.39.0 강제 어택의 대상 (플레이어): 어택하지 않기는 고를 수 없고, 대상만 고른다. 시큐리티에 어택할 수 있으면 "고르지 않음" = 시큐리티.
    /// 선택 창이 없으면(테스트 · 시뮬레이션) 이기는 대상 → 시큐리티 → 첫 후보
    /// </summary>
    private CardInstance? ChooseForcedAttackTarget39(PlayerZones owner, CardInstance attacker, List<CardInstance> targets, bool canSecurity, string question)
    {
        if (targets.Count == 0) return null;
        var best = BestWinningTarget(attacker, targets);
        var ranked = AiEvaluator.Rank(targets, best);
        CardInstance? recommended = best ?? (canSecurity ? null : ranked[0]);
        if (PlayerChoices == null || !ReferenceEquals(owner, State.Player)) return recommended;
        if (!canSecurity && ranked.Count == 1) return ranked[0];
        var p = PlayerChoices.ChooseDigimon(State, KoreanNameLocalizer.Localize(question), ranked, recommended, optional: canSecurity);
        return p != null && ranked.Contains(p) ? p : canSecurity ? null : ranked[0];
    }
}
