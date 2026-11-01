using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.67.0 미지원 문장 41차 묶음 (엔진 쪽):
///  - BT8-040 이 턴 동안 더해진 색 (CardInstance.TimedColors67 — ExtraColors에 더한다)
///  - BT11-043 · BT14-097 · EX13-031 원래 명칭 · 색 · DP를 「스카몬」 · 화이트 · 3000으로 (CardInstance.Changed67 + BaseColor66 — 종합 룰 15-12-2)
///  - 테이머 기록은 카드(객체)마다: 필드에 새로 놓인 테이머는 같은 카드가 전에 받은 색 · 효과를 넘기지 않는다 (종합 룰 3-4-4)
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>기간이 끝난 이 턴의 색 · 바뀐 원래 명칭 · DP를 지운다 (지속 효과를 계산할 때마다)</summary>
    private void ExpireChanges67()
    {
        foreach (var side in new[] { State.Player, State.DummyOpponent })
            foreach (var d in side.BattleArea)
            {
                if (d.TimedColors67.Count > 0) d.TimedColors67.RemoveAll(x => !EffectRules.IsActive(x.Until, State));
                if (d.Changed67 is { } ch && !EffectRules.IsActive(ch.Until, State)) d.Changed67 = null;
            }
    }

    /// <summary>테이머 카드(객체)가 필드에 새로 놓일 때: 그 카드가 필드에 없었으면 전에 받은 바뀐 색 · 받은 효과 기록을 지운다</summary>
    private static void ForgetTamerRecords67(PlayerZones zones, CardData tamer)
    {
        if (zones.Tamers.Any(t => ReferenceEquals(t, tamer))) return;   // 같은 객체가 이미 필드에 (직접 만든 시험 필드) — 그 장의 기록은 그대로
        if (zones.TamerColors66.Count > 0) zones.TamerColors66.RemoveAll(x => ReferenceEquals(x.Tamer, tamer));
        if (zones.TamerGrants65.Count > 0) zones.TamerGrants65.RemoveAll(x => ReferenceEquals(x.Tamer, tamer));
    }
}
