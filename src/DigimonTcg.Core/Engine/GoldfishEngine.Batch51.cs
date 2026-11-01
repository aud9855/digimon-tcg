using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.51.0 미지원 문장 25차 묶음의 엔진 쪽:
///  - 이번 턴에 진화했는지 ("If you've digivolved this turn" BT1-007 — 배틀 에어리어 · 육성 에어리어 · 효과로 진화 모두)
/// LM-020 카드 종류 면역의 지속 효과 쪽은 GoldfishEngine.Effects.cs (CategoryShield51), 효과로 등장시킨 카드는 CardInstance.PlayedByEffectCard51
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>owner가 지금 진화했다 (이번 턴 기록)</summary>
    private void NoteDigivolved51(PlayerZones owner) => owner.DigivolvedTurnSeq51 = State.TurnSequence;

    /// <summary>owner가 이번 턴에 진화했는지</summary>
    internal bool DigivolvedThisTurn51(PlayerZones owner) => owner.DigivolvedTurnSeq51 == State.TurnSequence;
}
