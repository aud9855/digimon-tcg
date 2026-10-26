using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

public record ActionResult
{
    public bool CanExecute { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string GuideMessage { get; init; } = string.Empty;

    public static ActionResult Success(string guide) =>
        new ActionResult { CanExecute = true, Reason = string.Empty, GuideMessage = KoreanNameLocalizer.Localize(guide) };   // 영어 카드 이름 → 공식 한글 이름

    public static ActionResult Fail(string reason) =>
        new ActionResult { CanExecute = false, Reason = KoreanNameLocalizer.Localize(reason), GuideMessage = string.Empty };
}

public static class ActionValidator
{
    // 1. 등장 유효성 검사
    public static ActionResult ValidatePlay(GameState state, CardData card)
    {
        if (state.IsGameOver)
            return ActionResult.Fail("이미 종료된 게임입니다.");

        if (state.CurrentPhase != Phase.Main)
            return ActionResult.Fail("메인 페이즈에만 카드를 등장시킬 수 있습니다.");

        if (state.BlitzAttacker != null && state.MemoryGauge < 0)
            return ActionResult.Fail("지금은 ≪진격≫ 어택이나 턴 종료만 할 수 있습니다.");

        if (card.Type == CardType.Digitama)
            return ActionResult.Fail("디지타마 카드는 패에서 등장시킬 수 없으며 육성 페이즈에 부화해야 합니다.");

        // 공식 룰: 옵션 카드는 자신의 배틀 에어리어 · 육성 에어리어의 디지몬 또는 테이머 중
        // 그 옵션의 색(여러 색이면 모든 색)을 가진 카드가 있어야 [메인]으로 사용할 수 있다. ([시큐리티] 발휘에는 조건 없음)
        // 듀얼 카드(종합 룰 4-6-2)는 패에서 등장하지 않는다 — 디지몬 쪽으로 진화하거나 옵션 쪽으로 사용한다. 이 경로 = 옵션 쪽 사용
        if (card.Type == CardType.Option || card.IsDual)
        {
            if (card.IsDual && CardScripts.OptionSideFor(card)?.Effects.Any(e => e.Trigger == EffectTrigger.Main) != true)
                return ActionResult.Fail($"[{card.Name}]은(는) 듀얼 카드입니다 — 디지몬 쪽으로 진화하거나 옵션 쪽으로 사용합니다 (이 카드의 옵션 쪽 효과는 아직 지원하지 않습니다).");
            var missing = MissingOptionColors(state.Player, card);
            if (missing.Count > 0)
                return ActionResult.Fail($"[{card.Name}]을(를) {(card.IsDual ? "옵션 쪽으로 " : "")}사용하려면 내 필드에 {string.Join(" · ", missing.Select(ColorName))} 디지몬 또는 테이머가 있어야 합니다{(card.IsDual ? " (또는 옵션 쪽의 사용 조건)" : "")}.");
        }

        // 공식 룰: 10을 넘는 코스트도 지불할 수 있다 (메모리는 상대 쪽 10에서 멈춘다)
        int cost = CardScripts.EffectivePlayCost(state.Player, card, state.DummyOpponent);   // 카드의 코스트 감소 효과 반영
        int projectedMemory = Math.Max(state.MemoryGauge - cost, -10);

        string guide = card.IsDual ? $"[{card.Name}] 옵션 쪽 사용 코스트 {cost}를 사용합니다." : $"[{card.Name}] 등장 코스트 {cost}를 사용합니다.";

        if (projectedMemory < 0)
            guide += $" (주의: 메모리가 {projectedMemory}가 되어 카드 처리 후 턴이 종료됩니다)";
        else
            guide += $" (남은 메모리: {projectedMemory})";

        return ActionResult.Success(guide);
    }

    /// <summary>옵션 카드의 색 중 자신의 필드(배틀 · 육성 에어리어의 디지몬, 테이머)에 없는 색</summary>
    public static List<CardColor> MissingOptionColors(PlayerZones zones, CardData option)
    {
        // 카드에 적힌 사용 조건 ("While you have a Tamer, you can ignore this card's color requirements")
        if (CardScripts.ForUse(option)?.IgnoreColorRequirement is { } ignore && ignore(zones))
            return new List<CardColor>();

        var fieldColors = new HashSet<CardColor>(zones.BattleArea.Select(d => d.TopCard.Color));
        if (zones.BreedingArea != null)
            fieldColors.Add(zones.BreedingArea.TopCard.Color);
        foreach (var t in zones.Tamers)
            fieldColors.Add(t.Color);
        // 여러 색 카드는 두 번째 색도 그 카드의 색으로 친다
        foreach (var c in zones.BattleArea.Select(d => d.TopCard).Concat(zones.Tamers).Append(zones.BreedingArea?.TopCard))
        {
            if (c?.SecondaryColor is CardColor second)
                fieldColors.Add(second);
        }

        return option.AllColors.Where(c => !fieldColors.Contains(c)).ToList();
    }

    private static string ColorName(CardColor color) => color switch
    {
        CardColor.Red => "레드",
        CardColor.Blue => "블루",
        CardColor.Yellow => "옐로",
        CardColor.Green => "그린",
        CardColor.Black => "블랙",
        CardColor.Purple => "퍼플",
        CardColor.White => "화이트",
        _ => color.ToString()
    };

    private static bool MeetsNormalEvolution(CardInstance target, CardData evoCard) =>
        target.TopCard.Level == evoCard.EvoRequirementLevel
        && (target.TopCard.Color == evoCard.EvoRequirementColor || CardScripts.MeetsAlternativeEvolution(evoCard, target));

    /// <summary>target에서 evoCard로 진화하는 코스트 — 보통 진화와 카드 효과의 진화 중 싼 쪽 (진화할 수 없으면 카드의 진화 코스트)</summary>
    public static int EvolutionCost(CardInstance target, CardData evoCard)
    {
        int? special = CardScripts.SpecialEvolutionCost(evoCard, target);
        if (special == null) return evoCard.EvoCost;
        return MeetsNormalEvolution(target, evoCard) ? Math.Min(evoCard.EvoCost, special.Value) : special.Value;
    }

    // 2. 진화 유효성 검사 (명세서 기본 시그니처 유지)
    public static ActionResult ValidateEvolution(CardInstance? target, CardData evoCard)
    {
        if (target == null)
            return ActionResult.Fail("진화 대상 디지몬이 선택되지 않았습니다.");

        if (evoCard.Type != CardType.Digimon)
            return ActionResult.Fail("디지몬 카드만 진화에 사용할 수 있습니다.");

        if (EffectRules.IsDigivolveLocked(null, target))
            return ActionResult.Fail($"[{target.TopCard.Name}]은(는) 진화할 수 없습니다 (카드 효과).");

        // 진화 조건을 무시하는 다른 진화 ("Your [Shoutmon] can digivolve into this card ~ for a memory cost of 4")
        if (!MeetsNormalEvolution(target, evoCard) && CardScripts.SpecialEvolutionCost(evoCard, target) is int special)
            return ActionResult.Success($"진화 코스트 {special} 지불 (카드 효과의 진화). 진화 성공 시 카드 1장을 추가로 드로우합니다.");

        if (target.TopCard.Level != evoCard.EvoRequirementLevel)
            return ActionResult.Fail($"Lv.{evoCard.EvoRequirementLevel} 디지몬 위에만 진화할 수 있습니다. (현재 대상: Lv.{target.TopCard.Level})");

        bool colorMatches = target.TopCard.Color == evoCard.EvoRequirementColor
            || CardScripts.MeetsAlternativeEvolution(evoCard, target);   // 카드에 적힌 다른 진화 조건 (예: 오메가몬 = Lv.6 레드 · 블루에서)

        if (!colorMatches)
            return ActionResult.Fail($"색상이 일치하지 않습니다. ({evoCard.EvoRequirementColor} 필요)");

        string guide = $"진화 코스트 {evoCard.EvoCost} 지불. 진화 성공 시 카드 1장을 추가로 드로우합니다.";
        return ActionResult.Success(guide);
    }

    // 2-1. 진화 유효성 검사 (GameState 포함 확장 오버로드)
    public static ActionResult ValidateEvolution(GameState state, CardInstance? target, CardData evoCard)
    {
        if (state.IsGameOver)
            return ActionResult.Fail("이미 종료된 게임입니다.");

        if (state.CurrentPhase != Phase.Main)
            return ActionResult.Fail("메인 페이즈에만 진화할 수 있습니다.");

        if (state.BlitzAttacker != null && state.MemoryGauge < 0)
            return ActionResult.Fail("지금은 ≪진격≫ 어택이나 턴 종료만 할 수 있습니다.");

        if (target != null && !state.Player.BattleArea.Contains(target) && !ReferenceEquals(state.Player.BreedingArea, target))
            return ActionResult.Fail("필드에 없는 디지몬은 진화시킬 수 없습니다.");

        var baseCheck = ValidateEvolution(target, evoCard);
        if (!baseCheck.CanExecute)
            return baseCheck;
        if (target != null && EffectRules.IsDigivolveLocked(state, target))
            return ActionResult.Fail($"[{target.TopCard.Name}]은(는) 이번 턴에 진화할 수 없습니다 (카드 효과).");

        int projectedMemory = Math.Max(state.MemoryGauge - EvolutionCost(target!, evoCard), -10);

        string guide = baseCheck.GuideMessage;
        if (projectedMemory < 0)
            guide += $" (주의: 메모리가 {projectedMemory}가 되어 진화 및 1드로우 후 턴이 종료됩니다)";

        return ActionResult.Success(guide);
    }

    // 3. 공격 유효성 검사
    public static ActionResult ValidateAttack(GameState state, CardInstance? attacker, CardInstance? defenderTarget = null)
    {
        if (state.IsGameOver)
            return ActionResult.Fail("이미 종료된 게임입니다.");

        if (state.CurrentPhase != Phase.Main)
            return ActionResult.Fail("메인 페이즈에만 공격할 수 있습니다.");

        if (attacker == null)
            return ActionResult.Fail("공격할 디지몬을 선택해주세요.");

        if (state.Player.BreedingArea == attacker)
            return ActionResult.Fail("육성 에어리어의 디지몬은 공격할 수 없습니다. 배틀 에어리어로 이동 후 공격하세요.");

        if (attacker.IsSuspended)
            return ActionResult.Fail("이미 레스트(Rest) 상태인 디지몬은 공격할 수 없습니다.");

        if (attacker.TurnPlayed == state.CurrentTurn && !CardScripts.HasKeyword(attacker, Keyword.Rush))
            return ActionResult.Fail("이번 턴에 등장한 디지몬은 소환 멀미로 인해 공격할 수 없습니다. (진화한 경우에도 등장 턴에는 공격 불가)");

        if (EffectRules.IsAttackLocked(state, attacker))
            return ActionResult.Fail("이 디지몬은 카드 효과로 지금은 어택할 수 없습니다.");

        if (state.BlitzAttacker != null && !ReferenceEquals(state.BlitzAttacker, attacker))
            return ActionResult.Fail($"≪진격≫으로는 [{state.BlitzAttacker.TopCard.Name}]만 어택할 수 있습니다.");

        if (defenderTarget != null && !defenderTarget.IsSuspended)
            return ActionResult.Fail("상대의 스탠드(Active) 상태 디지몬은 공격할 수 없습니다.");

        if (defenderTarget != null && CardScripts.HasKeyword(defenderTarget, Keyword.CannotBeAttacked))
            return ActionResult.Fail("이 디지몬은 카드 효과로 어택당하지 않습니다.");

        string guide;
        if (defenderTarget == null)
        {
            var activeBlocker = EffectRules.FindBlocker(state, state.DummyOpponent.BattleArea, attacker);
            if (activeBlocker != null)
            {
                int myDp = attacker.GetCurrentDP();
                int blkDp = activeBlocker.GetCurrentDP();
                string blkOutcome = myDp > blkDp
                    ? $"✅ 블로커 격파 가능! (내 DP {myDp} > 블로커 DP {blkDp})"
                    : $"⚠️ 주의: 블로커에게 막혀 내 디지몬이 소멸합니다! (내 DP {myDp} <= 블로커 DP {blkDp})";

                guide = state.CurrentStage == PracticeStage.Stage3_AiBattle
                    ? $"🛡️ 상대 필드에 블록할 수 있는 ≪블로커≫ [{activeBlocker.TopCard.Name}(DP {blkDp})]가 있습니다. 상대가 블록하면 — {blkOutcome}"
                    : $"🛡️ 상대 필드에 액티브 ≪블로커≫ [{activeBlocker.TopCard.Name}(DP {blkDp})]가 있어 시큐리티 공격을 가로채 방어합니다! — {blkOutcome}";
            }
            else if (state.DummyOpponent.SecurityStack.Count > 0)
            {
                var nextSec = state.DummyOpponent.SecurityStack[0];
                int myDp = attacker.GetCurrentDP();
                int checks = attacker.GetSecurityAttackCount();
                int cleared = state.SecurityCheckHistory.Count;

                if (state.CurrentStage == PracticeStage.Stage3_AiBattle)
                {
                    // 공식 룰: 상대 시큐리티는 뒷면(비공개)이므로 체크해서 오픈하기 전에는 카드 정보를 알려 주지 않는다
                    guide = $"시큐리티 #{cleared + 1} 공격! (내 DP {myDp:N0} · 체크 매수: {checks}장 · 상대 남은 시큐리티 {state.DummyOpponent.SecurityStack.Count}장)";
                }
                else
                {
                    string outcome = myDp > nextSec.DP
                        ? $"✅ 격파 가능! (내 DP {myDp} > 시큐리티 DP {nextSec.DP})"
                        : $"⚠️ 주의: 내 디지몬 소멸 위험! (내 DP {myDp} <= 시큐리티 DP {nextSec.DP})";

                    guide = $"시큐리티 #{cleared + 1} [{nextSec.Name}(Lv.{nextSec.Level}, DP {nextSec.DP})] 공격! (체크 매수: {checks}장) — {outcome}";
                }
            }
            else
            {
                guide = "🏆 상대 시큐리티 5장이 모두 파괴되었습니다! 플레이어에게 다이렉트 어택을 가해 게임에서 승리합니다!";
            }
        }
        else
        {
            int myDp = attacker.GetCurrentDP();
            int defDp = defenderTarget.GetCurrentDP();
            string outcome = myDp > defDp
                ? $"✅ 상대 디지몬 격파! (내 DP {myDp} > 상대 DP {defDp})"
                : myDp == defDp
                    ? $"💥 동귀어진(양쪽 모두 소멸)! (내 DP {myDp} == 상대 DP {defDp})"
                    : $"💀 내 디지몬 패배/소멸! (내 DP {myDp} < 상대 DP {defDp})";

            guide = $"상대 레스트 디지몬 [{defenderTarget.TopCard.Name}(DP {defDp})]을 공격합니다! — {outcome}";
        }

        return ActionResult.Success(guide);
    }

    // 4. 육성 페이즈 - 부화 유효성 검사
    public static ActionResult ValidateHatch(GameState state)
    {
        if (state.IsGameOver)
            return ActionResult.Fail("이미 종료된 게임입니다.");

        if (state.CurrentPhase != Phase.Breeding)
            return ActionResult.Fail("육성 페이즈에만 디지타마를 부화할 수 있습니다.");

        if (state.BreedingActionTaken)
            return ActionResult.Fail("이번 육성 페이즈에 이미 액션을 수행했습니다. (부화/이동 중 턴당 1회만 가능)");

        if (state.Player.BreedingArea != null)
            return ActionResult.Fail("육성 에어리어에 이미 디지몬이 존재합니다.");

        if (state.Player.DigitamaDeck.Count == 0)
            return ActionResult.Fail("디지타마 덱에 남은 카드가 없습니다.");

        return ActionResult.Success("디지타마 덱에서 Lv.2 디지타마를 육성 에어리어(절대 안전지대)에 부화합니다.");
    }

    // 5. 육성 페이즈 - 이동 유효성 검사
    public static ActionResult ValidateMoveFromBreeding(GameState state)
    {
        if (state.IsGameOver)
            return ActionResult.Fail("이미 종료된 게임입니다.");

        if (state.CurrentPhase != Phase.Breeding)
            return ActionResult.Fail("육성 페이즈에만 배틀 에어리어로 이동할 수 있습니다.");

        if (state.BreedingActionTaken)
            return ActionResult.Fail("이번 육성 페이즈에 이미 액션을 수행했습니다. (부화/이동 중 턴당 1회만 가능)");

        if (state.Player.BreedingArea == null)
            return ActionResult.Fail("육성 에어리어에 이동할 디지몬이 없습니다.");

        if (state.Player.BreedingArea.TopCard.Level < 3)
            return ActionResult.Fail("Lv.3(성장기) 이상으로 진화한 디지몬만 배틀 에어리어로 이동할 수 있습니다.");

        return ActionResult.Success($"육성 에어리어의 {state.Player.BreedingArea.TopCard.Name}(Lv.{state.Player.BreedingArea.TopCard.Level})을 배틀 에어리어로 이동합니다. (이동한 디지몬은 소환 멀미 없이 바로 공격 가능)");
    }
}
