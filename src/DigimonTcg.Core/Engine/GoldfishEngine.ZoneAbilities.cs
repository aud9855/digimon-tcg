using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>[메인] 기동 효과를 쓰는 곳: 필드(디지몬 · 테이머 · 놓인 옵션) · 패의 이 카드([Hand] [Main]) · 트래시의 이 카드([Trash] [Main])</summary>
public enum MainAbilityZone { Field, Hand, Trash }

/// <summary>
/// 패 · 트래시의 카드로 쓰는 [메인] 기동 효과 ("[Hand] [Main] By paying 3 memory, place this card under ~" · "[Trash] [Main] ~").
/// 필드의 [메인] 기동 효과(GetMainAbilities · ActivateMainAbility)와 같은 방식 — 자기 메인 페이즈에, 같은 카드 1장마다 턴에 1번.
/// 같은 카드 여러 장은 같은 CardData 객체이므로 (곳, 카드)마다 쓴 횟수를 세어 장수만큼 쓸 수 있다.
/// 비용을 내지 않았거나 조건이 맞지 않아 아무 문장도 처리하지 않았으면 쓴 것으로 치지 않는다.
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>owner의 패에서 지금 쓸 수 있는 [Hand] [Main] 효과</summary>
    public IReadOnlyList<MainAbility> GetHandMainAbilities(PlayerZones owner) => GetZoneAbilities(owner, MainAbilityZone.Hand);

    /// <summary>owner의 트래시에서 지금 쓸 수 있는 [Trash] [Main] 효과</summary>
    public IReadOnlyList<MainAbility> GetTrashMainAbilities(PlayerZones owner) => GetZoneAbilities(owner, MainAbilityZone.Trash);

    /// <summary>플레이어가 패의 카드로 [Hand] [Main] 효과를 쓴다. 조건 · 비용이 맞지 않으면 Fail(이유)</summary>
    public ActionResult ActivateHandEffect(CardData card) => ActivateZoneEffect(card, MainAbilityZone.Hand);

    /// <summary>플레이어가 트래시의 카드로 [Trash] [Main] 효과를 쓴다. 조건 · 비용이 맞지 않으면 Fail(이유)</summary>
    public ActionResult ActivateTrashEffect(CardData card) => ActivateZoneEffect(card, MainAbilityZone.Trash);

    private static EffectTrigger ZoneTrigger(MainAbilityZone zone) => zone == MainAbilityZone.Hand ? EffectTrigger.HandMain : EffectTrigger.TrashMain;

    private static string ZoneName(MainAbilityZone zone) => zone == MainAbilityZone.Hand ? "패" : "트래시";

    private IReadOnlyList<MainAbility> GetZoneAbilities(PlayerZones owner, MainAbilityZone zone)
    {
        var cards = zone == MainAbilityZone.Hand ? owner.Hand : owner.Trash;
        var trigger = ZoneTrigger(zone);
        return cards.Distinct()
            .Where(c => CardScripts.For(c)?.Effects.Any(e => e.Trigger == trigger) == true
                        && _usedMainAbilities.GetValueOrDefault((zone, c)) < cards.Count(x => ReferenceEquals(x, c)))
            .Select(c => new MainAbility(null, c, zone))
            .ToList();
    }

    private ActionResult ActivateZoneEffect(CardData card, MainAbilityZone zone)
    {
        if (State.IsGameOver || State.CurrentPhase != Phase.Main || State.PendingBlock != null || State.BlitzAttacker != null || !IsTurnOf(State.Player))
            return ActionResult.Fail($"지금은 {ZoneName(zone)}의 [메인] 효과를 쓸 수 없습니다.");
        if (!GetZoneAbilities(State.Player, zone).Any(a => ReferenceEquals(a.Card, card)))
            return ActionResult.Fail($"[{card.Name}]의 {ZoneName(zone)} [메인] 효과는 지금 쓸 수 없습니다 (이번 턴에 이미 썼거나 {ZoneName(zone)}에 없음).");
        if (zone == MainAbilityZone.Trash && TrashMainStatus(State.Player, card) is { CanUse: false, Reason: { } why86 })   // v0.86.0 조건 · 비용을 미리 본다 (GoldfishEngine.TrashZone86)
            return ActionResult.Fail($"[{card.Name}]: {why86}");
        var ctx = UseZoneAbility(State.Player, card, zone);
        CheckTurnEndAndAutoReset();
        if (!ctx.AnyStepResolved)
            return ActionResult.Fail($"[{card.Name}]: 조건이 맞지 않거나 비용을 낼 수 없어 효과를 발휘하지 않았습니다.");
        return ActionResult.Success($"[{card.Name}]의 {ZoneName(zone)} [메인] 효과를 썼습니다.");
    }

    private EffectContext UseZoneAbility(PlayerZones owner, CardData card, MainAbilityZone zone)
    {
        EffectLog(owner, $"[{ZoneName(zone)}] [메인] {card.Name}의 효과 사용");
        var zoneCards = zone == MainAbilityZone.Hand ? owner.Hand : owner.Trash;
        int copiesBefore = zoneCards.Count(x => ReferenceEquals(x, card));
        var ctx = RunEffects(ZoneTrigger(zone), owner, null, card, includeInherited: false);
        // v0.86.0 쓴 카드가 그 효과로 패 · 트래시를 떠났으면 (등장 · 덱 아래 등) 남은 같은 카드는 아직 쓰지 않은 카드다 —
        // 같은 카드 여러 장은 같은 CardData 객체라 떠난 장수만큼 셈에서 뺀다 (전에는 1장을 써서 떠나면 남은 장도 쓴 것으로 셌다)
        bool usedCopyLeft = zoneCards.Count(x => ReferenceEquals(x, card)) < copiesBefore;
        if (ctx.AnyStepResolved && !usedCopyLeft)
            _usedMainAbilities[(zone, card)] = _usedMainAbilities.GetValueOrDefault((zone, card)) + 1;
        RecalculateFieldBuffs();
        return ctx;
    }
}
