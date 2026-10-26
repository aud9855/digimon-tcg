using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.30.0 "이 효과로 등장한 디지몬" — "The Digimon this effect played can't digivolve and is deleted at the end of your opponent's turn" ·
/// "At turn end, delete the Digimon this effect played" (EX10 · EX11 · BT23 · P-216 등).
/// 효과 하나(절 하나)를 처리하는 동안 그 효과가 직접 등장시킨 디지몬을 모아 두고(<see cref="PlayCollector"/>),
/// 정해진 턴의 종료 시에 소멸시킨다 (그 효과의 주인이 소멸시키는 것 — 자신의 효과로 소멸).
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>지금 처리 중인 효과가 등장시킨 디지몬을 모으는 곳 (효과 처리 밖이면 null — 안쪽 효과는 자기 목록으로 바꾼다)</summary>
    internal List<CardInstance>? PlayCollector { get; set; }

    private readonly List<(CardInstance Digimon, PlayerZones EffectOwner, int TurnSequence)> _scheduledDeletions = new();

    /// <summary>turnSequence 번째 턴이 끝날 때 digimon을 소멸시킨다 (그때 배틀 에어리어에 있으면 — 진화해도 같은 디지몬, 종합 룰 3-4-5)</summary>
    internal void ScheduleDeletionAtTurnEnd(CardInstance digimon, PlayerZones effectOwner, int turnSequence) =>
        _scheduledDeletions.Add((digimon, effectOwner, turnSequence));

    /// <summary>턴 종료 시 (양쪽의 [턴 종료 시] 효과 다음): 이번 턴에 소멸하기로 한 디지몬을 소멸시킨다</summary>
    private void RunScheduledDeletions()
    {
        if (_scheduledDeletions.Count == 0)
            return;
        var due = _scheduledDeletions.Where(x => x.TurnSequence <= State.TurnSequence).ToList();
        _scheduledDeletions.RemoveAll(x => x.TurnSequence <= State.TurnSequence);
        foreach (var (digimon, effectOwner, _) in due)
        {
            if (State.IsGameOver) return;
            var zones = State.Player.BattleArea.Contains(digimon) ? State.Player : State.DummyOpponent.BattleArea.Contains(digimon) ? State.DummyOpponent : null;
            if (zones == null) continue;
            EffectLog(zones, $"[턴 종료 시] 효과로 등장한 [{digimon.TopCard.Name}]을(를) 소멸시킵니다.");
            var outer = CurrentEffectOwner;
            CurrentEffectOwner = effectOwner;   // 그 효과의 주인이 소멸시킨다 ("other than by your effects" 판단)
            _effectDepth++;
            try { DeleteFrom(zones, digimon, DeletionCause.Effect); }
            finally { _effectDepth--; CurrentEffectOwner = outer; }
            FlushPendingTriggers();
        }
        RecalculateFieldBuffs();
    }
}
