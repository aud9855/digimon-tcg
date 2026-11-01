using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>v0.77.0 미지원 문장 51차 묶음 — 엔진 쪽</summary>
public partial class GoldfishEngine
{
    /// <summary>
    /// 에어리어에 놓인 카드가 「디지몬」인지 — 디지몬 카드와 디지타마 카드 둘 다 (종합 룰 4-3-1 "에어리어에 놓인 디지타마 카드・디지몬 카드는 「디지몬」으로 취급".
    /// Q&amp;A BT16-090 Q1 "배틀 에어리어 및 육성 에어리어에 있는 디지타마 카드는 디지몬으로서 취급되므로, 파기할 수 있습니다")
    /// </summary>
    internal static bool IsDigimonInArea77(CardInstance d) => d.TopCard.Type is CardType.Digimon or CardType.Digitama;

    /// <summary>
    /// 디지몬(맨 위 카드)을 다른 디지몬의 진화원으로 놓을 때 그 아래에 겹쳐져 있던 카드를 트래시로 (종합 룰 4-7-7 · 4-7-8 · Q&amp;A BT11-088 · BT12-102 · BT12-027).
    /// 트래시로 가는 진화원만 ≪오버플로우≫를 처리한다 (4-19-4 — 아래에 놓이는 맨 위 카드는 처리하지 않는다. PlaceDigimonUnder와 같다)
    /// </summary>
    // ---- BT10-111 "When you would DigiXros this turn, this Digimon may replace 1 of the DigiXros requirements" ----

    // 디지크로스 조건 1장을 대신할 수 있는 디지몬 → 그 턴 (TurnSequence)
    private readonly Dictionary<CardInstance, int> _xrosSubstitute77 = new(ReferenceEqualityComparer.Instance);

    /// <summary>이 턴 동안 d가 디지크로스 조건 1장을 대신할 수 있다 (공식 Q&amp;A BT10-111 Q1 · 종합 룰 15-15-6-1)</summary>
    internal void AllowXrosSubstitute77(CardInstance d) => _xrosSubstitute77[d] = State.TurnSequence;

    /// <summary>d가 지금 디지크로스 조건 1장을 대신할 수 있는지 (그 턴 · 배틀 에어리어)</summary>
    private bool IsXrosSubstitute77(PlayerZones owner, CardInstance? d) =>
        d != null && _xrosSubstitute77.TryGetValue(d, out int turn) && turn == State.TurnSequence && owner.BattleArea.Contains(d);

    // ---- BT12-112 · BT21-030 등장할 때 배틀 에어리어의 「샤우트몬」을 놓아 코스트 -N · 트래시의 카드도 디지크로스 ----

    // 이번 등장에서 그 비용을 내 줄일 코스트 (TakeXrosMaterials가 정하고 등장 코스트 계산이 가져간다)
    private int _xrosFieldCostReduce77;

    /// <summary>방금 디지크로스 재료를 고를 때 낸 BT12-112 · BT21-030 비용의 코스트 감소 (가져가면 0)</summary>
    private int TakeXrosFieldCostReduce77()
    {
        int n = _xrosFieldCostReduce77;
        _xrosFieldCostReduce77 = 0;
        return n;
    }

    /// <summary>
    /// BT12-112 · BT21-030: 등장할 때 효과 (디지크로스 선언보다 먼저 — 종합 룰 7-2-2-2). 배틀 에어리어의 그 이름 디지몬 1마리(맨 위 카드)를 이 카드 아래에 놓으면
    /// (진화원 · 링크는 트래시 — 4-7-7 · 4-7-8, 공식 Q&amp;A BT12-112 Q1 배틀 에어리어의 것만) 코스트 -N, 트래시의 카드도 디지크로스 재료. 반환: 트래시를 열었는지
    /// </summary>
    private bool PayXrosFieldCost77(PlayerZones owner, CardData card, XrosRequirement req)
    {
        _xrosFieldCostReduce77 = 0;
        if (CardScripts.For(card)?.XrosFieldCost77 is not { } fc) return false;
        var pool = owner.BattleArea.Where(d => d.TamerProxyOf == null && !IsToken(d.TopCard) && fc.Names.Any(x => CardNames.Is(d, x))).ToList();
        if (pool.Count == 0) return false;
        // 추천: 플레이어는 예, AI는 트래시에 디지크로스 재료가 2장 이상일 때 (필드의 디지몬을 잃는다)
        int trashMats = owner.Trash.Count(c => c.Type == CardType.Digimon && req.Slots.Any(s => s.Match(c)));
        bool rec = ShouldAskPlayer(owner) || trashMats >= 2;
        string names = string.Join("/", fc.Names);
        if (!AskYesNo(owner, null, $"[{card.Name}] 자신의 [{names}] 1마리를 이 카드 아래에 놓아 등장 코스트 -{fc.Reduce}, 트래시의 카드도 디지크로스에 쓸까요? (그 디지몬의 진화원은 트래시)", rec))
            return false;
        var ranked = pool.OrderBy(d => d.EvolutionSources.Count).ThenBy(d => d.GetCurrentDP()).ToList();
        var pick = ranked.Count == 1 ? ranked[0]
            : ChooseTargets(owner, ranked, 1, $"[{card.Name}] 이 카드 아래에 놓을 [{names}]을(를) 고르세요.", optional: false, preselected: null).FirstOrDefault() ?? ranked[0];
        if (DetachDigimonAsCost(owner, pick) is not { } top) return false;
        PendingUnderPlayed.Add(top);
        _xrosFieldCostReduce77 = fc.Reduce;
        EffectLog(owner, $"[{card.Name}] 자신의 [{top.Name}]을(를) 이 카드 아래에 놓았습니다 — 등장 코스트 -{fc.Reduce}, 트래시의 카드도 디지크로스에 쓸 수 있습니다");
        Diag46.Hit("xrosFieldCostPaid77");
        return true;
    }

    internal void TrashSourcesOfPlaced77(PlayerZones owner, CardInstance moving)
    {
        var trashed = moving.GetEvolutionStackBottomUp().ToList();
        if (trashed.Count == 0) return;
        ApplyOverflow(owner, trashed);
        while (moving.EvolutionSources.Count > 0)
            owner.Trash.Add(moving.EvolutionSources.Pop());
    }
}
