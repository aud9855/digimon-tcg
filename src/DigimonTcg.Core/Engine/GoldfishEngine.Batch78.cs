using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>v0.78.0 미지원 문장 52차 묶음 — 엔진 쪽</summary>
public partial class GoldfishEngine
{
    // ---- EX13-076 "Compare the number of digivolution cards instead of DP in this battle" ----

    // 지금 처리 중인 배틀이 DP 대신 진화원 매수를 비교하는지 (EffectBattleBySourceCount78 동안만)
    private bool _compareSourcesBattle78;

    /// <summary>이 배틀은 DP 대신 진화원 매수로 비교한다 (≪빙장≫과 같은 비교 — ResolveDigimonBattle)</summary>
    internal string EffectBattleBySourceCount78(CardInstance mine, CardInstance theirs)
    {
        bool outer = _compareSourcesBattle78;
        _compareSourcesBattle78 = true;
        try { return EffectBattle(mine, theirs); }
        finally { _compareSourcesBattle78 = outer; }
    }

    /// <summary>
    /// EX13-076 "return all digivolution cards of 1 of their Digimon to the bottom of the deck" — d의 진화원 전부(뒷면 포함)를 주인의 덱 아래로.
    /// 디지타마 카드는 디지타마 덱 아래 (덱으로 돌아가는 카드는 그 카드의 덱으로). ≪오버플로우≫는 옮겨지는 진화원에 (4-19-4). 겹쳐진 카드 보호 (BT21-060)면 0. 반환: 옮긴 매수
    /// </summary>
    internal int ReturnAllSourcesToDeckBottom78(PlayerZones owner, CardInstance d)
    {
        if (!owner.BattleArea.Contains(d) || d.EvolutionSources.Count == 0 || StackProtected42(owner, d)) return 0;
        var moved = d.GetEvolutionStackBottomUp().Reverse().ToList();   // 위 → 아래
        ApplyOverflow(owner, d.GetFaceUpStackBottomUp());
        d.EvolutionSources.Clear();
        d.FaceDownSources.Clear();
        var eggs = moved.Where(x => x.Type == CardType.Digitama).ToList();
        foreach (var x in moved.Where(x => x.Type != CardType.Digitama)) owner.MainDeck.Add(x);
        if (eggs.Count > 0) AddToDigitamaDeckBottom(owner, eggs);
        RecalculateFieldBuffs();
        return moved.Count;
    }

    // ---- BT12-112 · BT21-030 효과로 등장할 때도 "When you would play this card, by placing 1 [Shoutmon] ~" (종합 룰 7-2-2-13 효과로 등장시킬 때도 디지크로스할 수 있다) ----

    // 효과 등장에서 이 카드 아래에 놓을 「샤우트몬」 (PlayWithoutCostCore가 디지크로스 재료 다음 = 맨 아래에 놓는다)
    private List<CardData>? _pendingUnderByEffect78;

    /// <summary>
    /// 코스트를 내는 효과 등장 (BT21-021 · BT19-099 · P-224 · BT21-092 등)에서 XrosFieldCost77을 가진 카드: 등장할 때 효과를 먼저 해결하고 (7-2-3-1)
    /// 디지크로스를 선언한다 (7-2-3-2 — 패 · 배틀 에어리어, 그 효과로 트래시도). 반환: 줄인 뒤의 코스트 · 디지크로스 재료
    /// </summary>
    private (int Cost, List<CardData> Xros) PayXrosFieldCostOnEffectPlay78(PlayerZones owner, CardData card, int cost)
    {
        var xros = TakeXrosMaterials(owner, card);
        int field = TakeXrosFieldCostReduce77();
        if (PendingUnderPlayed.Count > 0)
        {
            _pendingUnderByEffect78 = PendingUnderPlayed.ToList();
            PendingUnderPlayed.Clear();
        }
        if (xros.Count > 0 || field > 0) Diag46.Hit("xrosOnEffectPlay78");
        int reduced = PlayCostReductionBlocked(owner, card) ? cost : System.Math.Max(0, cost - XrosReduction(card, xros.Count) - field);
        return (reduced, xros);
    }

    /// <summary>놓지 못한 「샤우트몬」 (등장하지 않았으면 트래시)</summary>
    private void DropPendingUnderByEffect78(PlayerZones owner)
    {
        if (_pendingUnderByEffect78 is { } xs) owner.Trash.AddRange(xs);
        _pendingUnderByEffect78 = null;
    }
}
