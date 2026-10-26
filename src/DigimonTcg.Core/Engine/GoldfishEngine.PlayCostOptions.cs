using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// 등장할 때 고르는 코스트 감소 ("When this card would be played, by deleting 1 of your Digimon, reduce the play cost by 4.").
/// 카드가 패를 떠난 뒤 · 필드에 놓이기 전에, 낼 수 있는 비용마다 낼지 묻고(플레이어 — AI는 추천대로) 내면 코스트를 줄인다.
/// v0.31.0: 필드 · 육성의 다른 카드가 줄이는 것 ("[Your Turn] When any Digimon cards with the [ADVENTURE] trait would be played from your hand,
/// by suspending this Tamer, reduce the play cost by 1") — 공식 Q&A ST20-12 · ST21-12: 여러 장이면 더한다
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>
    /// 미리보기: card를 패에서 등장 · 사용할 때 낼 코스트 (부작용 없음). EffectivePlayCost(상대 필드 포함)에서
    /// 지금 낼 수 있고(CanPay) 추천(Recommend)이 참인 선택형 감소를 뺀다 — AI가 실제로 낼 때와 같은 기준. 디지크로스 감소는 넣지 않는다.
    /// 미리보기 시점엔 card가 아직 패에 있으므로 "패의 카드를 버리는" 비용은 card 자신까지 센다
    /// </summary>
    public int PreviewPlayCostWithOptions(PlayerZones owner, CardData card)
    {
        int cost = CardScripts.EffectivePlayCost(owner, card, EnemyOf(owner));
        if (PlayCostReductionBlocked(owner, card))   // v0.36.0 "Players can't reduce play costs"
            return cost;
        foreach (var option in CardScripts.ForUse(card)?.PlayCostOptions ?? new List<PlayCostOption>())
        {
            if (cost <= 0)
                break;
            var ctx = new EffectContext(this, owner, null, null, null) { Card = card };
            if (option.CanPay(ctx) && option.Recommend(ctx))
                cost = Math.Max(0, cost - (option.PerCount ? option.Reduce * Math.Max(0, option.PreviewCount?.Invoke(ctx) ?? 1) : option.Reduce));
        }
        foreach (var (mod, ctx) in ApplicableFieldPlayCostModifiers(owner, card))
        {
            if (cost <= 0)
                break;
            if (mod.Cost is { } pay && !(pay.CanPay(ctx) && pay.Recommend(ctx)))
                continue;
            cost = Math.Max(0, cost - FieldReduce(mod, ctx));
        }
        return cost;
    }

    private int ApplyPlayCostOptions(PlayerZones owner, CardData card, int cost)
    {
        if (PlayCostReductionBlocked(owner, card))   // v0.36.0 "Players can't reduce play costs" — 줄이는 비용도 묻지 않는다
            return cost;
        var options = CardScripts.ForUse(card)?.PlayCostOptions ?? new List<PlayCostOption>();
        foreach (var option in options)
        {
            if (cost <= 0)
                break;
            var ctx = new EffectContext(this, owner, null, null, null) { Card = card };
            if (!option.CanPay(ctx)
                || !AskYesNo(owner, null, $"[{card.Name}] {option.Describe} {(CardScripts.IsUsableAsOption(card) ? "사용" : "등장")} 코스트를 {option.Reduce} 줄일까요?", option.Recommend(ctx)))
                continue;
            ctx.PaidCount = 0;
            option.Pay(ctx);
            int reduced = Math.Min(option.PerCount ? option.Reduce * Math.Max(0, ctx.PaidCount) : option.Reduce, cost);   // v0.34.0 "for each one"
            cost -= reduced;
            EffectLog(owner, $"[{card.Name}] 비용을 내고 등장 코스트 -{reduced} (코스트 {cost})");
        }
        return ApplyFieldPlayCostModifiers(owner, card, cost);
    }

    // [턴에 1회] 등장 코스트 감소를 쓴 턴 (효과, 효과가 있는 곳)
    private readonly Dictionary<(FieldPlayCostModifier, object), int> _fieldPlayCostUsedTurn = new();

    private static int FieldReduce(FieldPlayCostModifier mod, EffectContext ctx) =>
        mod.Amount != null ? Math.Max(0, mod.Amount(ctx))
        : mod.Times == null ? mod.Reduce : mod.Reduce * Math.Max(0, Math.Min(mod.Times(ctx), 20));

    /// <summary>패에서 등장하는 card에 지금 걸리는 필드 · 육성의 등장 코스트 감소 (조건 · [턴에 1회]까지 — 비용을 낼 수 있는지는 보지 않는다)</summary>
    private IEnumerable<(FieldPlayCostModifier Mod, EffectContext Ctx)> ApplicableFieldPlayCostModifiers(PlayerZones owner, CardData card)
    {
        if (card.Type == CardType.Option || card.IsDual)   // 옵션 · 듀얼 카드(옵션 쪽으로 사용)는 "등장"이 아니다
            yield break;
        foreach (var (mod, holder, source) in FieldPlayCostSources(owner).ToList())
        {
            if (!mod.Which(card) || (mod.YourTurnOnly && !IsTurnOf(owner)))
                continue;
            if (mod.OncePerTurn && _fieldPlayCostUsedTurn.TryGetValue((mod, (object?)holder ?? source), out int used) && used == State.TurnSequence)
                continue;
            yield return (mod, new EffectContext(this, owner, holder, null, null) { Card = source, TriggerCard = card });
        }
    }

    private int ApplyFieldPlayCostModifiers(PlayerZones owner, CardData card, int cost)
    {
        foreach (var (mod, ctx) in ApplicableFieldPlayCostModifiers(owner, card).ToList())
        {
            if (cost <= 0)
                break;
            int reduce = FieldReduce(mod, ctx);
            if (reduce <= 0)
                continue;
            if (mod.Cost is { } pay)
            {
                if (!pay.CanPay(ctx) || !AskYesNo(owner, ctx.Self, $"[{ctx.Card.Name}] {pay.Describe} [{card.Name}] 등장 코스트를 {reduce} 줄일까요?", pay.Recommend(ctx)))
                    continue;
                pay.Pay(ctx);
            }
            else if (mod.Optional && !AskYesNo(owner, ctx.Self, $"[{ctx.Card.Name}] [{card.Name}] 등장 코스트를 {reduce} 줄일까요?", recommended: true))
                continue;
            int reduced = Math.Min(reduce, cost);
            cost -= reduced;
            if (mod.OncePerTurn)
                _fieldPlayCostUsedTurn[(mod, (object?)ctx.Self ?? ctx.Card)] = State.TurnSequence;
            EffectLog(owner, $"[{ctx.Card.Name}] [{card.Name}] 등장 코스트 -{reduced} (코스트 {cost})");
        }
        return cost;
    }

    /// <summary>등장 코스트 감소 효과가 있는 곳: (효과, 효과를 가진 디지몬 — 테이머면 null, 효과가 적힌 카드)</summary>
    private static IEnumerable<(FieldPlayCostModifier Mod, CardInstance? Holder, CardData Card)> FieldPlayCostSources(PlayerZones owner)
    {
        IEnumerable<(FieldPlayCostModifier, CardInstance?, CardData)> Of(CardInstance d, bool breeding)
        {
            foreach (var mod in CardScripts.For(d.TopCard)?.FieldPlayCostModifiers ?? Enumerable.Empty<FieldPlayCostModifier>())
                if (mod.Source == EffectSource.Main && mod.Breeding == breeding)
                    yield return (mod, d, d.TopCard);
            foreach (var src in d.FaceUpSources)
                foreach (var mod in CardScripts.For(src)?.FieldPlayCostModifiers ?? Enumerable.Empty<FieldPlayCostModifier>())
                    if (mod.Source == EffectSource.Inherited && mod.Breeding == breeding)
                        yield return (mod, d, src);
        }
        foreach (var d in owner.BattleArea)
            foreach (var x in Of(d, breeding: false))
                yield return x;
        if (owner.BreedingArea is { } egg)
            foreach (var x in Of(egg, breeding: true))
                yield return x;
        foreach (var t in owner.Tamers.Distinct())
            foreach (var mod in CardScripts.For(t)?.FieldPlayCostModifiers ?? Enumerable.Empty<FieldPlayCostModifier>())
                if (mod.Source == EffectSource.Main && !mod.Breeding)
                    yield return (mod, null, t);
    }
}
