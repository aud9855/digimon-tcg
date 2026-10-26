using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// 진화하기 직전의 진화 코스트 감소 ("When this Digimon would digivolve into a [X] trait Digimon card, reduce the digivolution cost by 2").
/// 진화할 카드가 패(또는 효과가 정한 곳)를 떠난 뒤 · 진화하기 전에, 걸리는 효과를 차례로 적용한다:
///  - 자신의 디지몬(맨 위 카드의 효과 · 진화원 효과) · 테이머의 효과, 진화할 카드 자신의 "into this card" 효과
///  - [턴에 1회]는 효과가 있는 곳마다 한 턴에 1번, 비용이 있으면 낼지 묻는다(AI는 추천대로)
///  - 상대 효과로 진화 코스트 감소가 막혀 있으면 적용하지 않는다
/// </summary>
public partial class GoldfishEngine
{
    // [턴에 1회] 진화 코스트 감소를 쓴 턴 (효과, 효과가 있는 곳)
    private readonly Dictionary<(EvoCostModifier, object), int> _evoCostModifierUsedTurn = new();

    /// <summary>
    /// 미리보기: target이 evoCard로 진화할 때 낼 진화 코스트 (부작용 없음 — 묻지 않고, 비용이 있는 감소 · ≪디지섭션≫은 넣지 않는다).
    /// 화면의 "진화 (코스트 N)" · AI 정책이 쓴다
    /// </summary>
    public int PreviewEvoCost(PlayerZones owner, CardInstance target, CardData evoCard)
    {
        int cost = ActionValidator.EvolutionCost(target, evoCard);
        if (cost <= 0 || !CanReduceEvoCost(owner))
            return cost;
        foreach (var (mod, _, _, ctx) in ApplicableEvoCostModifiers(owner, target, evoCard))
            if (mod.Cost == null)
                cost = Math.Max(0, cost - mod.ReduceFor(ctx, target));
        return cost;
    }

    /// <summary>지금 걸리는 진화 코스트 감소 (조건 · [턴에 1회] 확인까지 — 비용을 낼 수 있는지는 보지 않는다)</summary>
    private IEnumerable<(EvoCostModifier Mod, CardInstance? Holder, CardData Card, EffectContext Ctx)> ApplicableEvoCostModifiers(PlayerZones owner, CardInstance target, CardData evoCard)
    {
        var enemy = EnemyOf(owner);
        foreach (var (mod, holder, card) in EvoCostModifierSources(owner, evoCard).ToList())
        {
            var ctx = new EffectContext(this, owner, holder, target, null) { Card = card, TriggerCard = evoCard };
            bool subject = mod.Subject switch
            {
                EvoCostSubject.ThisDigimon => ReferenceEquals(holder, target),
                EvoCostSubject.IntoThisCard => ReferenceEquals(card, evoCard),
                _ => true,
            };
            if (!subject || (mod.SubjectFilter != null && !mod.SubjectFilter(ctx, target))
                || (mod.Into != null && !mod.Into(evoCard))
                || (mod.YourTurnOnly && !IsTurnOf(owner))
                || (mod.Condition != null && !mod.Condition(owner, enemy)))
                continue;
            if (mod.OncePerTurn && _evoCostModifierUsedTurn.TryGetValue((mod, (object?)holder ?? card), out int usedTurn) && usedTurn == State.TurnSequence)
                continue;
            yield return (mod, holder, card, ctx);
        }
    }

    private int ApplyEvoCostModifiers(PlayerZones owner, CardInstance target, CardData evoCard, int cost)
    {
        if (cost <= 0 || !CanReduceEvoCost(owner))
            return cost;
        foreach (var (mod, holder, card, ctx) in ApplicableEvoCostModifiers(owner, target, evoCard).ToList())
        {
            if (cost <= 0)
                break;
            var key = (mod, (object?)holder ?? card);
            if (mod.Cost is { } pay)
            {
                if (!pay.CanPay(ctx) || !AskYesNo(owner, holder, $"[{card.Name}] {pay.Describe} [{evoCard.Name}] 진화 코스트를 {mod.Reduce}{(pay.PerCount ? "씩" : "")} 줄일까요?", pay.Recommend(ctx)))
                    continue;
                ctx.PaidCount = 0;
                pay.Pay(ctx);
            }
            int reduced = Math.Min(mod.Cost is { PerCount: true } ? mod.Reduce * Math.Max(0, Math.Min(ctx.PaidCount, 20)) : mod.ReduceFor(ctx, target), cost);   // v0.36.0 낸 매수마다
            cost -= reduced;
            if (mod.OncePerTurn)
                _evoCostModifierUsedTurn[key] = State.TurnSequence;
            EffectLog(owner, $"[{card.Name}] [{evoCard.Name}]으로 진화 — 진화 코스트 -{reduced} (코스트 {cost})");
        }
        return cost;
    }

    /// <summary>진화 코스트 감소 효과가 있는 곳: (효과, 효과를 가진 디지몬 — 테이머 · 패의 카드면 null, 효과가 적힌 카드)</summary>
    private static IEnumerable<(EvoCostModifier Mod, CardInstance? Holder, CardData Card)> EvoCostModifierSources(PlayerZones owner, CardData evoCard)
    {
        foreach (var d in owner.BattleArea)
        {
            foreach (var mod in CardScripts.For(d.TopCard)?.EvoCostModifiers ?? Enumerable.Empty<EvoCostModifier>())
                if (mod.Source == EffectSource.Main && mod.Subject != EvoCostSubject.IntoThisCard)
                    yield return (mod, d, d.TopCard);
            foreach (var src in d.FaceUpSources)
                foreach (var mod in CardScripts.For(src)?.EvoCostModifiers ?? Enumerable.Empty<EvoCostModifier>())
                    if (mod.Source == EffectSource.Inherited)
                        yield return (mod, d, src);
        }
        foreach (var t in owner.Tamers.Distinct())
            foreach (var mod in CardScripts.For(t)?.EvoCostModifiers ?? Enumerable.Empty<EvoCostModifier>())
                if (mod.Source == EffectSource.Main && mod.Subject == EvoCostSubject.AnyOwnDigimon)
                    yield return (mod, null, t);
        foreach (var mod in CardScripts.For(evoCard)?.EvoCostModifiers ?? Enumerable.Empty<EvoCostModifier>())
            if (mod.Source == EffectSource.Main && mod.Subject == EvoCostSubject.IntoThisCard)
                yield return (mod, null, evoCard);
    }
}
