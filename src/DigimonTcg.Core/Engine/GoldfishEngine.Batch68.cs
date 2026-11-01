using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.68.0 미지원 문장 42차 묶음 (엔진 쪽):
///  - 정보를 바꾸는 효과와 효과를 받지 않는 디지몬 (종합 룰 15-15-5-1 · 15-15-5-2): 바뀐 원래 명칭 · 색 · DP (Changed67 · BaseColor66)를 준 효과를 기록하고,
///    그 효과를 받지 않는 동안 (상대의 효과 · LM-020 카드 종류)에는 원래 명칭 · 색 · DP로 본다. 다시 받게 되면 그 시점부터 바뀐 것으로 (기간이 남아 있으면)
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>지금 처리 중인 효과가 d에게 주는 정보 변경의 출처 (상대의 효과인지 · 효과가 적힌 카드의 종류 — 이 디지몬 자신의 효과는 null)</summary>
    internal (bool Opp, CardType? Type) ChangeSource68(CardInstance d) => (IsOpponentEffectFor(OwnerOf(d)), EffectCategory61(d));
}

public partial class GoldfishEngine
{
    /// <summary>
    /// v0.68.0 앞면 시큐리티 · 패의 이 카드의 시점 효과 (EffectSource.FaceUpSecurity68 · Hand68):
    ///  - [시큐리티]【자신의 턴】 "When one of your ~ Digimon attacks, ~" (EX12-069 · EX12-074) — 시큐리티에 앞면으로 있는 동안 유발 · 발휘 (종합 룰 15-14-5-1). 앞면 카드는 장마다 다른 카드
    ///  - 듀얼 카드 옵션 쪽 "When one of your ~ Digimon attacks, ~ you may use this card ~" (LM-068) — 패의 이 카드 (같은 카드 여러 장은 1장만 — [트래시] 시점 효과와 같다)
    /// 처리하는 동안 그 카드가 그곳에 그대로 있어야 한다
    /// </summary>
    internal void RunZoneEffects68(EffectTrigger trigger, PlayerZones owner, CardInstance? other, CardData? triggerCard)
    {
        if (State.IsGameOver) return;
        if (owner.FaceUpSecurity.Count > 0)
            foreach (var card in FaceUpCards(owner).Where(c => CardScripts.For(c)?.Effects.Any(e => e.Source == EffectSource.FaceUpSecurity68 && e.Trigger == trigger) == true).ToList())
                RunZoneCard68(owner, card, CardScripts.For(card)!, EffectSource.FaceUpSecurity68, trigger, other, triggerCard, () => IsFaceUp(owner, card));
        if (owner.Hand.Count > 0)
            foreach (var card in owner.Hand.Where(c => ZoneScript68(c)?.Effects.Any(e => e.Source == EffectSource.Hand68 && e.Trigger == trigger) == true).GroupBy(c => c.CardId).Select(g => g.First()).ToList())
                RunZoneCard68(owner, card, ZoneScript68(card)!, EffectSource.Hand68, trigger, other, triggerCard, () => owner.Hand.Contains(card));
    }

    /// <summary>패의 카드의 시점 효과가 적힌 스크립트 (듀얼 카드는 옵션 쪽)</summary>
    private static CardScript? ZoneScript68(CardData card) => card.IsDual ? CardScripts.OptionSideFor(card) : CardScripts.For(card);

    private void RunZoneCard68(PlayerZones owner, CardData card, CardScript script, EffectSource source, EffectTrigger trigger, CardInstance? other, CardData? triggerCard, Func<bool> stillThere)
    {
        if (State.IsGameOver || !stillThere()) return;
        var ctx = new EffectContext(this, owner, null, other, null) { TriggerCard = triggerCard, Trigger = trigger };
        _effectDepth++;
        var outerEffectOwner = CurrentEffectOwner;
        CurrentEffectOwner = owner;
        try
        {
            foreach (var effect in script.Effects.Where(e => e.Source == source && e.Trigger == trigger).ToList())
            {
                if (!stillThere()) break;
                Diag46.Hit(source == EffectSource.Hand68 ? "handZoneEffect68" : "faceUpSecEffect68");
                Resolve(ctx, card, effect);
            }
        }
        finally
        {
            _effectDepth--;
            CurrentEffectOwner = outerEffectOwner;
        }
        if (_effectDepth == 0)
            FlushPendingTriggers();
    }
}
