using System;
using System.Collections.Generic;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// 등장할 때 고르는 코스트 감소 ("When this card would be played, by deleting 1 of your Digimon, reduce the play cost by 4.").
/// 카드가 패를 떠난 뒤 · 필드에 놓이기 전에, 낼 수 있는 비용마다 낼지 묻고(플레이어 — AI는 추천대로) 내면 코스트를 줄인다.
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
        foreach (var option in CardScripts.ForUse(card)?.PlayCostOptions ?? new List<PlayCostOption>())
        {
            if (cost <= 0)
                break;
            var ctx = new EffectContext(this, owner, null, null, null) { Card = card };
            if (option.CanPay(ctx) && option.Recommend(ctx))
                cost = Math.Max(0, cost - option.Reduce);
        }
        return cost;
    }

    private int ApplyPlayCostOptions(PlayerZones owner, CardData card, int cost)
    {
        var options = CardScripts.ForUse(card)?.PlayCostOptions;
        if (options == null || options.Count == 0)
            return cost;
        foreach (var option in options)
        {
            if (cost <= 0)
                break;
            var ctx = new EffectContext(this, owner, null, null, null) { Card = card };
            if (!option.CanPay(ctx)
                || !AskYesNo(owner, null, $"[{card.Name}] {option.Describe} {(CardScripts.IsUsableAsOption(card) ? "사용" : "등장")} 코스트를 {option.Reduce} 줄일까요?", option.Recommend(ctx)))
                continue;
            option.Pay(ctx);
            int reduced = Math.Min(option.Reduce, cost);
            cost -= reduced;
            EffectLog(owner, $"[{card.Name}] 비용을 내고 등장 코스트 -{reduced} (코스트 {cost})");
        }
        return cost;
    }
}
