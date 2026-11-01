using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.66.0 미지원 문장 40차 묶음 (엔진 쪽):
///  - EX2-039 "if it wasn't trashed by [EX2-039 Impmon]'s effect" — 덱에서 파기된 카드를 파기한 효과가 적힌 카드 (ThisTrashedByCard66)
///  - BT18-078 바뀐 원래 색의 기간 — 디지몬 (CardInstance.BaseColor66) · 테이머 (PlayerZones.TamerColors66)
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>"When this card is trashed from your deck" 처리 중: 이 카드를 파기한 효과가 적힌 카드 (효과 밖 · 룰이면 null)</summary>
    internal CardData? ThisTrashedByCard66 { get; private set; }

    /// <summary>기간이 끝난 · 떠난 테이머의 바뀐 원래 색을 지운다 (지속 효과를 계산할 때마다)</summary>
    private void ExpireColors66()
    {
        foreach (var side in new[] { State.Player, State.DummyOpponent })
        {
            foreach (var d in side.BattleArea)
                if (d.BaseColor66 is { } bc && !EffectRules.IsActive(bc.Until, State))
                    d.BaseColor66 = null;
            if (side.TamerColors66.Count > 0)
                side.TamerColors66.RemoveAll(x => !EffectRules.IsActive(x.Until, State) || !side.Tamers.Any(t => ReferenceEquals(t, x.Tamer)));
        }
    }
}
