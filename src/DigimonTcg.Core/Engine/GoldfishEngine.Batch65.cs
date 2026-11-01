using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.65.0 미지원 문장 39차 묶음 (엔진 쪽):
///  - 종합 룰 6-6-4 "턴 종료 시에 메모리가 0 이상이 된 경우, 턴 종료가 되지 않고, 그 페이즈 그대로입니다" — 턴 종료 시의 처리가 모두 끝난 때
///    (6-6-2 · 6-6-3) 메모리가 턴 종료 조건을 만족하지 않으면 턴 교대 없이 메인 페이즈가 이어진다 (공식 Q&amp;A BT9-111 Q1 "턴 교대는 하지 않고 자신의 턴이 계속됩니다").
///    BT14-081 · BT17-069의 턴 종료 조건(상대 쪽의 3 이상)이 있으면 그 조건으로 본다. 다시 조건을 만족하면 새 「턴 종료 시」가 된다
///  - AI 패스는 턴 종료 시 효과보다 먼저 메모리를 상대 쪽의 3으로 (6-5-1-7-1 — 플레이어의 패스와 같은 순서)
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>
    /// v0.65.0 BT9-103 "cards can't be added to security stacks by your opponent's effects" — effectOwner의 효과로는 (어느 쪽이든) 시큐리티에 카드를 추가할 수 없다.
    /// 막힌 카드는 원래 자리에 그대로 (사용 중인 옵션은 트래시 — 공식 Q&amp;A BT10-041 Q1). 시큐리티 안에서 순서를 바꾸는 것 · 대전 준비는 추가가 아니다
    /// </summary>
    internal bool SecurityAddBlocked65(PlayerZones? effectOwner)
    {
        if (effectOwner == null || effectOwner.NoSecurityAdd65 <= 0 || !EffectRules.IsActive(effectOwner.NoSecurityAdd65, State)) return false;
        Diag46.Hit("noSecurityAdd65");
        return true;
    }

    /// <summary>
    /// v0.65.0 EX10-058 테이머에게 준 효과 중 지금 유효한 것 — 기간 안 · 그 테이머(카드 객체)가 아직 필드에 있을 때.
    /// 떠난 테이머 · 끝난 기간의 것은 지운다 (떠났다 다시 나온 테이머는 새 카드 — 종합 룰 4-1-4 · 같은 객체라도 효과는 남지 않게 떠날 때 지운다)
    /// </summary>
    internal List<CardData> ActiveTamerGrants65(PlayerZones owner, CardData tamer)
    {
        owner.TamerGrants65.RemoveAll(g => !owner.Tamers.Any(t => ReferenceEquals(t, g.Tamer)) || !EffectRules.IsActive(g.Until, State));
        return owner.TamerGrants65.Where(g => ReferenceEquals(g.Tamer, tamer)).Select(g => g.Effect).ToList();
    }

    /// <summary>테이머가 필드를 떠났다 — 그 테이머에게 준 효과를 지운다 (EX10-058)</summary>
    internal void ForgetTamerGrants65(PlayerZones owner)
    {
        if (owner.TamerGrants65.Count > 0)
            owner.TamerGrants65.RemoveAll(g => !owner.Tamers.Any(t => ReferenceEquals(t, g.Tamer)));
    }

    /// <summary>한 턴에 6-6-4로 턴이 이어질 수 있는 횟수 상한 (같은 효과가 매번 메모리를 되돌리는 무한 반복 방지)</summary>
    private const int MaxTurnResumes65 = 10;

    private int _playerResumes65;
    private int _playerResumeSeq65 = -1;

    /// <summary>방금 턴 종료 처리에서 6-6-4로 플레이어의 턴이 이어졌는지 (PassTurn 안내 · 테스트)</summary>
    public bool TurnResumed65 { get; private set; }

    /// <summary>플레이어 턴: 턴 종료 시의 처리가 끝난 때 메모리가 턴 종료 조건을 만족하지 않으면 메인 페이즈로 돌아간다 (종합 룰 6-6-4)</summary>
    private bool ResumePlayerTurn65()
    {
        TurnResumed65 = false;
        if (State.IsGameOver || PlayerTurnEndMet64()) return false;
        if (_playerResumeSeq65 != State.CurrentTurn) { _playerResumeSeq65 = State.CurrentTurn; _playerResumes65 = 0; }
        if (++_playerResumes65 > MaxTurnResumes65)
        {
            Log($"[종합 룰 6-6-4] 이번 턴에 턴 종료가 {MaxTurnResumes65}번 미뤄져 더 미루지 않고 턴을 끝냅니다. (메모리를 상대 쪽의 3으로)");
            State.MemoryGauge = -3;
            return false;
        }
        Diag46.Hit("turnResume65");
        State.CurrentPhase = Phase.Main;
        State.BlitzAttacker = null;
        TurnResumed65 = true;
        Log($"[종합 룰 6-6-4] 턴 종료 시에 메모리가 내 쪽(+{State.MemoryGauge})이 되어 턴 종료가 되지 않고 메인 페이즈가 이어집니다.");
        return true;
    }

    /// <summary>AI 턴: 더 할 것이 없어 턴을 끝낼 때 메모리가 턴 종료 조건을 만족하지 않으면 패스 — 즉시 상대 쪽의 3 (종합 룰 6-5-1-7-1)</summary>
    private void AiPassIfTurnContinues65(AiTurnContext ctx)
    {
        ctx.Passed65 = false;
        if (TurnEndMet64(State.DummyOpponent, ctx.Memory)) return;
        ctx.Passed65 = true;
        ctx.Memory = -3;
        Log("🤖 [AI 패스] AI가 더 이상 행동하지 않고 패스(Pass)를 선언하여 메모리가 -3(내 메모리 +3)이 됩니다.");
    }

    /// <summary>AI 턴: 턴 종료 시의 처리가 끝난 때 메모리가 턴 종료 조건을 만족하지 않으면 메인 페이즈로 돌아간다 (종합 룰 6-6-4)</summary>
    private bool ResumeAiTurn65(AiTurnContext ctx)
    {
        if (State.IsGameOver || TurnEndMet64(State.DummyOpponent, ctx.Memory)) return false;
        if (++ctx.Resumes65 > MaxTurnResumes65)
        {
            Log($"🤖 [종합 룰 6-6-4] 이번 턴에 턴 종료가 {MaxTurnResumes65}번 미뤄져 더 미루지 않고 턴을 끝냅니다. (메모리를 상대 쪽의 3으로)");
            ctx.Memory = -3;
            ctx.Passed65 = true;
            return false;
        }
        Diag46.Hit("turnResume65");
        ctx.EndPhase = false;
        ctx.Passed65 = false;
        ctx.Attackers.Clear();
        ctx.AttackTarget = null;
        ctx.BlitzAttacker = null;
        Log($"🤖 [종합 룰 6-6-4] 턴 종료 시에 AI 메모리가 +{ctx.Memory}(AI 쪽)이 되어 턴 종료가 되지 않고 AI의 메인 페이즈가 이어집니다.");
        AddOpponentEvent(OpponentActionKind.TurnStart, "턴 계속 (종합 룰 6-6-4)", $"턴 종료 시에 메모리가 상대 쪽으로 돌아와 상대 턴이 이어집니다 (메모리 +{ctx.Memory})");
        return true;
    }
}
