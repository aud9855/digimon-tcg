using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.35.0 뒷면 진화원 · 테이머 아래 뒷면 카드 (Ver.N · DM 계열, ≪트레이닝≫, DATA SQUAD · Glowing Dawn 테이머).
/// 종합 룰 (Ver.4.3):
///  - 4-7-4 아래에 겹쳐진 카드는 지정이 없는 한 앞면으로 놓는다 — "face down"이라고 적힌 것만 뒷면
///  - 4-7-5 뒷면 카드는 아래에 겹쳐져 있음을 알 수 있도록 겹친다 (화면: 카드 뒷면 띠)
///  - 4-7-9 뒷면으로 아래에 겹쳐진 카드는 카드의 정보를 갖지 않는 카드로 취급하며 정보를 참조할 수 없다
///    (예: ≪트레이닝≫으로 놓인 뒷면 진화원의 카드명이나 색 등을 다른 효과로 참조할 수 없다) → 진화원 효과 · DP · 조건에서 빠진다
///  - 4-7-10 뒷면 카드는 비공개 정보지만 주인은 자신의 뒷면 카드를 볼 수 있다 (화면: 상대 것만 가린다)
///  - 16-41 ≪트레이닝≫ 덱 위 1장을 이 디지몬의 진화원 밑에 뒷면으로 놓는다
///  - 17-1-3-2-4 에어리어에 놓인 뒷면 카드는 룰 처리로 파기 (≪퇴화≫ 등으로 뒷면 진화원이 맨 위가 되면 그 디지몬은 파기 — 4-7-8 아래 카드도 동시에)
/// 진화원이 트래시 · 패 · 덱으로 가면 공개 · 비공개 영역의 규칙대로 놓인다 (트래시는 앞면 — 3-6-3).
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>v0.35.0 테이머가 "can't suspend" 상태인지 (효과 · 비용 모두 레스트할 수 없다)</summary>
    internal bool TamerSuspendLocked(PlayerZones owner, CardData tamer) =>
        owner.TamerCannotSuspendUntil.TryGetValue(tamer, out int until) && EffectRules.IsActive(until, State);

    /// <summary>v0.35.0 테이머가 "can't unsuspend" 상태인지 (액티브 페이즈에도 액티브가 되지 않는다)</summary>
    internal bool TamerUnsuspendLocked(PlayerZones owner, CardData tamer) =>
        (owner.TamerCannotUnsuspendUntil.TryGetValue(tamer, out int until) && EffectRules.IsActive(until, State))
        || EnemyOf(owner).BattleArea.Any(d => Effects.CardScripts.HasKeyword(d, Effects.Keyword.OpponentTamersCannotUnsuspend))   // v0.36.0 "None of your opponent's Tamers can unsuspend" (BT17-048)
        || UnsuspendLock45(owner, null, tamer, phase: false) && Diag45.HitTrue("unsuspendLockTamer");   // v0.45.0 지속 효과 (BT12-057)

    /// <summary>액티브 페이즈: 레스트 상태인 테이머 중 "can't unsuspend"인 것은 레스트 그대로 둔다. 반환: 액티브가 된 테이머</summary>
    private List<CardData> WakeTamers(PlayerZones owner)
    {
        var keep = owner.SuspendedTamers.Where(t => TamerUnsuspendLocked(owner, t) || UnsuspendLock45(owner, null, t, phase: true)).ToList();   // v0.45.0 "in the unsuspend phase" (EX10-023 · BT6-092)
        var woke = owner.SuspendedTamers.Where(t => !keep.Contains(t)).ToList();
        owner.SuspendedTamers.Clear();
        owner.SuspendedTamers.AddRange(keep);
        return woke;
    }

    /// <summary>뒷면으로 놓은 장 수 (시뮬레이션 · 진단용)</summary>
    public int FaceDownPlacedTotal { get; private set; }

    /// <summary>카드(원래 있던 곳에서 이미 뺀)를 inst의 진화원 맨 아래에 뒷면으로 놓는다. 놓인 복사본 반환</summary>
    internal CardData PlaceFaceDownSource(PlayerZones owner, CardInstance inst, CardData card)
    {
        var copy = inst.AddBottomSourceFaceDown(card);
        FaceDownPlacedTotal++;
        EffectLog(owner, $"[뒷면 진화원] [{inst.TopCard.Name}]의 진화원 맨 아래에 카드 1장을 뒷면으로 놓았습니다 (뒷면 {inst.FaceDownSourceCount}장)");
        return copy;
    }

    /// <summary>카드(원래 있던 곳에서 이미 뺀)를 테이머 아래에 뒷면으로 놓는다 (맨 아래). 놓인 복사본 반환</summary>
    internal CardData PlaceFaceDownUnderTamer(PlayerZones owner, CardData card)
    {
        var copy = owner.AddUnderTamerFaceDown(card);
        FaceDownPlacedTotal++;
        EffectLog(owner, $"[테이머 아래 뒷면] 카드 1장을 테이머 아래에 뒷면으로 놓았습니다 (뒷면 {owner.FaceDownUnderTamerCount}장)");
        return copy;
    }

    /// <summary>inst의 진화원 중 아래에서부터 뒷면 카드 n장을 파기한다 (트래시로). 파기한 카드 반환</summary>
    internal List<CardData> TrashBottomFaceDownSources(PlayerZones owner, CardInstance inst, int n)
    {
        if (StackProtected42(owner, inst)) return new List<CardData>();   // v0.42.0 (BT21-060)
        var picks = inst.GetEvolutionStackBottomUp().Where(inst.IsFaceDown).Take(n).ToList();
        foreach (var x in picks)
            if (inst.TakeSource(y => ReferenceEquals(y, x)) != null) owner.Trash.Add(x);
        if (picks.Count > 0)
            EffectLog(owner, $"[뒷면 진화원] [{inst.TopCard.Name}]의 뒷면 진화원 {picks.Count}장을 파기했습니다");
        return picks;
    }

    /// <summary>테이머 아래 뒷면 카드를 아래에서부터 n장 파기한다. 파기한 카드 반환</summary>
    internal List<CardData> TrashBottomFaceDownUnderTamers(PlayerZones owner, int n)
    {
        var trashed = new List<CardData>();
        for (int i = owner.UnderTamers.Count - 1; i >= 0 && trashed.Count < n; i--)
        {
            var x = owner.UnderTamers[i];
            if (!owner.IsFaceDownUnderTamer(x)) continue;
            owner.UnderTamers.RemoveAt(i);
            owner.FaceDownUnderTamers.Remove(x);
            owner.Trash.Add(x);
            trashed.Add(x);
        }
        if (trashed.Count > 0) NotifyUnderTamersTrashed(owner, trashed);   // v0.31.0 "When effects trash cards from under your Tamers"
        return trashed;
    }

    /// <summary>
    /// 룰 처리 (종합 룰 17-1-3-2-4): 맨 위 카드가 뒷면 카드가 된 디지몬(≪퇴화≫ · 맨 위 카드 파기 등)은 그 카드와 아래 카드를 함께 파기한다 (소멸이 아니다)
    /// </summary>
    internal void TrashExposedFaceDownTops()
    {
        if (!State.Player.BattleArea.Concat(State.DummyOpponent.BattleArea).Any(d => d.TopIsFaceDown) && State.Player.BreedingArea?.TopIsFaceDown != true && State.DummyOpponent.BreedingArea?.TopIsFaceDown != true)
            return;
        foreach (var zones in new[] { State.Player, State.DummyOpponent })
        {
            foreach (var inst in zones.BattleArea.Where(d => d.TopIsFaceDown).ToList())
            {
                zones.BattleArea.Remove(inst);
                TrashLinks(zones, inst);
                zones.Trash.Add(inst.TopCard);
                while (inst.EvolutionSources.Count > 0) zones.Trash.Add(inst.EvolutionSources.Pop());
                EffectLog(zones, "[룰 처리] 뒷면 카드가 맨 위가 되어 그 디지몬의 카드를 모두 파기했습니다 (종합 룰 17-1-3-2-4)");
                NotifyLeftBattleArea(zones, inst);
            }
            if (zones.BreedingArea is { TopIsFaceDown: true } breed)
            {
                zones.BreedingArea = null;
                zones.Trash.Add(breed.TopCard);
                while (breed.EvolutionSources.Count > 0) zones.Trash.Add(breed.EvolutionSources.Pop());
                EffectLog(zones, "[룰 처리] 육성 에어리어의 뒷면 카드가 맨 위가 되어 파기했습니다 (종합 룰 17-1-3-2-4)");
            }
        }
        RecalculateFieldBuffs();
    }
}
