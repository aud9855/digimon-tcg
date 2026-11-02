using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.73.0 미지원 문장 47차 묶음 (엔진):
///  - 배틀 에어리어의 디지몬을 링크 (종합 룰 6-5-1-4 「패/배틀 에어리어의 카드의 링크」 · 10-1-1): 맨 위 카드가 링크 카드가 된다.
///    링크 카드는 에어리어에 속하지 않으므로 (4-9-4) 그 디지몬은 배틀 에어리어를 떠나고, 아래에 겹쳐져 있던 카드는 동시에 파기 (4-7-8), 가진 링크 카드도 파기
///  - 공식 영어 카드 리스트의 〈링크〉 정보 (영어 API에 링크 조건 · 링크 DP가 없고 공식 한글 데이터도 없는 카드):
///    EX11-027 [Maquinamon] — [Link DP] +2000 · [Link Condition] ＜Link＞ [Maquinamon] in text: Cost 2 (영어 API는 링크 효과를 진화원 효과 칸에 둔다)
/// </summary>
public partial class GoldfishEngine
{
    private sealed record EnglishLinkInfo73(Func<CardInstance, bool> Requirement, int Cost, int Dp);

    /// <summary>카드 번호 → 공식 영어 〈링크〉 정보</summary>
    private static readonly Dictionary<string, EnglishLinkInfo73> EnglishLinks73 = new(StringComparer.Ordinal)
    {
        // "[Maquinamon] in text" — 이름 · 특징 · 효과 등 카드의 어디든 [Maquinamon]이 기술된 카드 (공식 Q&A EX11-027 Q5822)
        ["EX11-027"] = new(d => TargetSpec.TextHas(d, "Maquinamon"), 2, 2000),
        // v0.83.0 링크하는 옵션 (종합 룰 2-3-11-4 하단 텍스트의 「링크」 · 10-1-1 — 옵션 카드도 「링크」를 가지면 링크 카드): 공식 영어 [Link Condition] · [Link DP] (공식 일본어도 같다)
        //   BT25-100 "＜Link＞ [TS] trait: Cost 2" · DP +2000 / BT25-101 "＜Link＞ [Vulcanusmon]: Cost 3" · DP +4000
        ["BT25-100"] = new(d => TargetSpec.HasTrait(d, "TS"), 2, 2000),
        ["BT25-101"] = new(d => CardNames.Is(d, "Vulcanusmon"), 3, 4000),
    };

    /// <summary>공식 영어 〈링크〉 정보가 있는 카드 (SentenceParser.IsLinkCard43 — 하단 텍스트는 링크 효과)</summary>
    internal static bool HasEnglishLink73(CardData card) => EnglishLinks73.ContainsKey(card.CardId);

    private static EnglishLinkInfo73? EnglishLink73(CardData card) => EnglishLinks73.GetValueOrDefault(card.CardId);

    /// <summary>
    /// 자신의 배틀 에어리어의 디지몬 moving을 target에 링크한다 (효과 — 코스트 없이). "떠나려 할 때" 효과로 막혔거나 링크하지 못했으면 false
    /// </summary>
    internal bool LinkFieldDigimon73(PlayerZones zones, CardInstance moving, CardInstance target)
    {
        if (ReferenceEquals(moving, target) || !zones.BattleArea.Contains(moving) || !zones.BattleArea.Contains(target) || IsToken(moving.TopCard) || IsToken(target.TopCard))
            return false;
        if (TryPreventLeave(zones, moving, deletion: false, inBattle: false, canPrevent: true, LeaveDestination.Other))
            return false;
        if (!zones.BattleArea.Contains(target) || !zones.BattleArea.Remove(moving))   // "떠나려 할 때" 처리 중에 링크 받을 디지몬이 사라졌으면 링크하지 않는다
            return false;
        TrashLinks(zones, moving);
        var trashed = moving.GetEvolutionStackBottomUp().ToList();
        ApplyOverflow(zones, trashed);
        while (moving.EvolutionSources.Count > 0)
            zones.Trash.Add(moving.EvolutionSources.Pop());
        PurgeTokens(zones);
        LinkCard(zones, target, moving.TopCard);
        NotifyLeftBattleArea(zones, moving);
        RecalculateFieldBuffs();
        return true;
    }
}
