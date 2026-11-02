using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.83.0 덱 위로 되돌리는 효과 (SentenceParser.Batch83):
///  - "Return ~ to the top of the deck" (BT24-061): 덱 아래로 되돌리는 처리(BottomDeck · MoveTamerToDeckBottom34)를 그대로 쓰고 놓는 곳만 덱 위 — DeckTop83 &gt; 0인 동안.
///    디지몬은 맨 위 카드가 덱 맨 위, 그 아래로 진화원 (위에서부터 차례대로)
///  - "Return the top N stacked cards of ~ to the top of the deck" (BT26-060): 겹쳐진 카드를 위에서부터 N장 (맨 위 카드부터 — 먼저 뗀 카드가 덱 맨 위).
///    모두 되돌리면 그 디지몬은 배틀 에어리어를 떠난다 (BT21-030 "Trash the top N stacked cards"와 같은 셈 — 떠나려 할 때 막을 수 있다)
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>0보다 크면 BottomDeck · MoveTamerToDeckBottom34가 덱 위에 놓는다 (덱 위로 되돌리는 효과를 처리하는 동안)</summary>
    internal int DeckTop83 { get; set; }

    /// <summary>덱 위로 되돌리는 중인지 읽고, 떠나기 전 처리(다른 효과가 끼어들 수 있다) 동안은 끈다</summary>
    private bool TakeDeckTop83(out int saved)
    {
        saved = DeckTop83;
        DeckTop83 = 0;
        return saved > 0;
    }

    /// <summary>덱으로 가는 카드들 (위 → 아래 순서)을 덱 위 또는 아래에 놓는다</summary>
    private static void PutToDeck83(PlayerZones zones, IReadOnlyList<CardData> topFirst, bool top)
    {
        if (top) zones.MainDeck.InsertRange(0, topFirst);
        else zones.MainDeck.AddRange(topFirst);
    }

    /// <summary>겹쳐진 카드를 위에서부터 n장 주인의 덱 위로 (반환: 되돌린 장수)</summary>
    internal int ReturnTopStackedToDeckTop83(PlayerZones zones, CardInstance inst, int n)
    {
        if (!zones.BattleArea.Contains(inst) || n <= 0 || StackProtected42(zones, inst)) return 0;
        var moved = new List<CardData>();
        while (moved.Count < n && inst.EvolutionSources.Count > 0 && inst.TrashTopCard() is { } t)
        {
            ApplyOverflow(zones, new[] { t });
            if (!IsToken(t)) moved.Add(t);
        }
        bool left = false;
        if (moved.Count < n)
        {
            // 마지막 카드 — 디지몬 자체가 덱으로 ("떠나려 할 때" 효과로 막을 수 있다)
            if (!TryPreventLeave(zones, inst, deletion: false, inBattle: false, canPrevent: true, LeaveDestination.Deck) && zones.BattleArea.Remove(inst))
            {
                TrashLinks(zones, inst);
                ApplyOverflow(zones, new[] { inst.TopCard });
                if (!IsToken(inst.TopCard)) moved.Add(inst.TopCard);
                left = true;
            }
        }
        zones.MainDeck.InsertRange(0, moved);
        if (left) { PurgeTokens(zones); NotifyLeftBattleArea(zones, inst); }
        RecalculateFieldBuffs();
        TrashExposedFaceDownTops();
        return moved.Count;
    }
}
