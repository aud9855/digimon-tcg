using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.64.0 미지원 문장 38차 묶음 (엔진 쪽):
///  - 턴 종료 조건 (종합 룰 6-1-4-1 · 6-6-1): 원래는 메모리가 상대 쪽의 1 이상이 되면 턴 종료 시가 되지만, BT14-081 【자신의 턴】 · BT17-069 진화원이
///    발휘하고 있는 동안은 상대 쪽의 3 이상이 되지 않으면 턴 종료 시가 되지 않는다 — 상대 쪽의 1 · 2인 동안은 자신의 턴이 이어진다 (공식 Q&amp;A BT14-081 Q1).
///    효과가 없어지면(디지몬이 떠나거나 조건을 잃으면) 그 처리가 끝난 때 원래 조건으로 본다. 패스는 지금처럼 메모리를 상대 쪽의 3으로 (6-5-1-7-1)
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>side의 턴 종료 조건: 메모리가 상대 쪽의 N 이상 (기본 1 — 배틀 에어리어의 디지몬이 가진 지속 효과 중 가장 큰 N)</summary>
    internal int TurnEndMemory64(PlayerZones side)
    {
        int n = 1;
        foreach (var (aura, ctx) in DigimonAuras(side, EnemyOf(side), StaticStat.TurnEndMemory64))
            n = Math.Max(n, aura.Value(ctx));
        if (n > 1) Diag46.Hit("turnEnd64");
        return n;
    }

    /// <summary>side 기준 메모리(side 쪽이 +)가 턴 종료 조건을 만족하는지</summary>
    internal bool TurnEndMet64(PlayerZones side, int memory) => memory <= -TurnEndMemory64(side);

    /// <summary>플레이어 턴: 지금 메모리가 턴 종료 조건을 만족하는지</summary>
    private bool PlayerTurnEndMet64() => TurnEndMet64(State.Player, State.MemoryGauge);

    /// <summary>
    /// AI 턴: 정책과 행동 반복이 보는 메모리 — 턴이 끝나기 전까지 더 쓸 수 있는 만큼을 더한다 (턴 종료 조건이 상대 쪽의 3이면 +2).
    /// 0 미만 = 턴 종료 조건을 만족했다
    /// </summary>
    private int AiMem64(AiTurnContext ctx) => ctx.Memory + TurnEndMemory64(State.DummyOpponent) - 1;
}

public static partial class EffectRules
{
    /// <summary>v0.64.0 플레이어 턴의 턴 종료 조건을 만족했는지 (메모리가 상대 쪽의 N 이상 — 기본 1, BT14-081 · BT17-069는 3). 엔진이 지속 효과를 계산할 때 채운 값</summary>
    public static bool PlayerTurnEndMet64(GameState state) => state.MemoryGauge <= -Math.Max(1, state.Player.TurnEndMemory64);
}
