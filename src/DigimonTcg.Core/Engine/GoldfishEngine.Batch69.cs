using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.69.0 미지원 문장 43차 묶음 (엔진 쪽):
///  - [시큐리티]【서로의 턴】 "When any of your ~ would leave the battle area other than by battle, by trashing this card, 1 of those Digimon doesn't leave" (ST22-10) —
///    시큐리티에 앞면으로 있는 동안 유발 (종합 룰 15-14-5-1). 42차의 앞면 시큐리티 시점 효과 (EffectSource.FaceUpSecurity68)와 같은 표 — "떠나려 할 때"는 TryPreventLeave가 따로 돈다
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>
    /// v0.69.0 앞면 시큐리티의 이 카드의 [시큐리티]【서로의 턴】 · 【자신의 턴】 · 【상대의 턴】 지속 효과 (EffectSource.FaceUpSecurity68 지속 효과 — 종합 룰 15-14-5-1: 시큐리티에 앞면으로 있는 동안).
    /// 앞면 카드는 장마다 (같은 카드 2장이면 2번 — 다른 카드)
    /// </summary>
    private IEnumerable<(StaticEffect Effect, StaticContext Context)> SecurityAuras69(PlayerZones side, PlayerZones enemy, StaticStat stat)
    {
        if (side.FaceUpSecurity.Count == 0) yield break;
        var ctx = new StaticContext { Engine = this, State = State, Owner = side, Enemy = enemy, OwnersTurn = IsTurnOf(side) };
        foreach (var card in FaceUpCards(side))
            if (CardScripts.For(card) is { } script)
                foreach (var e in script.Statics.Where(e => e.Source == EffectSource.FaceUpSecurity68 && e.Stat == stat))
                    yield return (e, ctx);
    }

    /// <summary>앞면 시큐리티의 이 카드의 "자신의 디지몬이 떠나려 할 때" — 하나라도 막았으면 true (앞면 카드는 장마다 다른 카드)</summary>
    private bool RunFaceUpSecurityWouldLeave69(PlayerZones owner, CardInstance inst, bool deletion)
    {
        if (owner.FaceUpSecurity.Count == 0 || State.IsGameOver) return false;
        var triggers = deletion ? new[] { EffectTrigger.AllyDigimonWouldLeave, EffectTrigger.AllyDigimonWouldBeDeleted } : new[] { EffectTrigger.AllyDigimonWouldLeave };
        var cards = FaceUpCards(owner).Where(c => CardScripts.For(c)?.Effects.Any(e => e.Source == EffectSource.FaceUpSecurity68 && triggers.Contains(e.Trigger)) == true).ToList();
        bool prevented = false;
        foreach (var card in cards)
        {
            if (prevented || LeaveEscaped46(inst) || State.IsGameOver || !owner.BattleArea.Contains(inst)) break;
            if (!IsFaceUp(owner, card)) continue;
            var ctx = new EffectContext(this, owner, null, inst, null);
            _effectDepth++;
            var outerEffectOwner = CurrentEffectOwner;
            CurrentEffectOwner = owner;
            try
            {
                foreach (var effect in CardScripts.For(card)!.Effects.Where(e => e.Source == EffectSource.FaceUpSecurity68 && triggers.Contains(e.Trigger)).ToList())
                {
                    if (!IsFaceUp(owner, card) || ctx.PreventLeave) break;
                    ctx.Trigger = effect.Trigger;
                    Diag46.Hit("faceUpSecWouldLeave69");
                    Resolve(ctx, card, effect);
                }
            }
            finally
            {
                _effectDepth--;
                CurrentEffectOwner = outerEffectOwner;
            }
            if (ctx.PreventLeave) prevented = true;
        }
        return prevented;
    }
}
