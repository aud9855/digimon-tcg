using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>v0.70.0 미지원 문장 44차 묶음 — 엔진</summary>
public partial class GoldfishEngine
{
    /// <summary>
    /// EX14-012 듀얼 카드의 【시큐리티】 "이 카드를 사용" — 체크한 디지몬 카드를 옵션 쪽으로 사용했다 (종합 룰 4-6-2 · 9-1-5: 사용 뒤 트래시).
    /// 사용한 카드는 시큐리티 디지몬이 아니므로 배틀하지 않는다 (공격한 디지몬은 남는다)
    /// </summary>
    internal CardData? SecurityUsedAsOption70 { get; set; }

    /// <summary>체크한 디지몬 카드가 【시큐리티】 효과로 옵션으로 사용되었으면 true (배틀 없음 — 부른 쪽이 다음 체크로)</summary>
    private bool SecurityUsedNoBattle70(PlayerZones owner, CardData securityCard, CardInstance attacker, int attackerDp)
    {
        if (!ReferenceEquals(SecurityUsedAsOption70, securityCard)) { SecurityUsedAsOption70 = null; return false; }
        SecurityUsedAsOption70 = null;
        string msg = $"✨ [시큐리티 {securityCard.Name}] 옵션으로 사용되어 배틀하지 않습니다. [{attacker.TopCard.Name}]은(는) 생존합니다. (남은 시큐리티: {owner.SecurityStack.Count}장)";
        var item = new SecurityCheckHistoryItem(securityCard, attacker.TopCard.Name, attackerDp, IsBattleWon: !ReferenceEquals(owner, State.Player), OutcomeSummary: "✨ [시큐리티] 옵션으로 사용 (배틀 없음)");
        if (ReferenceEquals(owner, State.Player)) State.PlayerSecurityCheckHistory.Add(item);
        else
        {
            State.LastSecurityBattleResult = $"시큐리티 [{securityCard.Name}] 옵션으로 사용 (내 디지몬 생존)";
            State.LastSecurityBattleWon = true;
            State.SecurityCheckHistory.Add(item);
        }
        Log(msg);
        return true;
    }

    /// <summary>
    /// [상대의 메인 페이즈 개시 시] (EX3-024) — 턴 플레이어의 [자신의 메인 페이즈 개시 시] 효과 다음에, 상대(비턴 플레이어)의 디지몬 (메인 · 진화원).
    /// 종합 룰 15-4: 동시에 유발한 효과는 턴 플레이어 쪽부터
    /// </summary>
    private void RunOpponentMainPhaseStart70(PlayerZones turnPlayer)
    {
        var other = EnemyOf(turnPlayer);
        foreach (var d in other.BattleArea.ToList())
        {
            if (State.IsGameOver) return;
            if (other.BattleArea.Contains(d)) RunEffects(EffectTrigger.StartOfOpponentsMainPhase70, other, d, d.TopCard);
        }
    }
}
