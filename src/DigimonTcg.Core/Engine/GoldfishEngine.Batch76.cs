using System.Linq;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>v0.76.0 미지원 문장 50차 묶음 — 엔진 쪽</summary>
public partial class GoldfishEngine
{
    /// <summary>
    /// 효과로 마지막에 등장한 테이머 카드 — BT13-020 "treat the Tamer played with this effect as ~" (바로 앞 문장이 등장시킨 테이머. 앞 문장이 처리되었을 때만 본다)
    /// </summary>
    internal CardData? LastTamerPlayedByEffect76 { get; private set; }

    /// <summary>
    /// BT13-112 "trash 1 of your Digimon in the breeding area" — 육성 에어리어의 카드를 진화원째 트래시 (효과 — 소멸이 아니다, ≪오버플로우≫).
    /// 에어리어에 놓인 디지타마 카드도 「디지몬」으로 취급하므로 (종합 룰 4-3-1) 디지타마 카드뿐이어도 파기한다
    /// </summary>
    internal bool TrashBreedingAny76(PlayerZones owner)
    {
        if (owner.BreedingArea is not { } b) return false;
        owner.BreedingArea = null;
        var stack = b.GetEvolutionStackBottomUp();
        ApplyOverflow(owner, stack.Append(b.TopCard));
        owner.Trash.Add(b.TopCard);
        while (b.EvolutionSources.Count > 0) owner.Trash.Add(b.EvolutionSources.Pop());
        EffectLog(owner, $"육성 에어리어의 [{b.TopCard.Name}]을(를) 파기했습니다.");
        RecalculateFieldBuffs();
        return true;
    }
}
