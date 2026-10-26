using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// 앞면 시큐리티 (효과 엔진 7단계): 시큐리티에 앞면으로 놓인 카드 (PlayerZones.FaceUpSecurity).
/// 같은 CardData 객체가 여러 장을 나타낼 수 있으므로, 앞면으로 놓거나 뒤집을 때 그 카드를 복사해 위치마다 앞/뒷면이 정확하도록 한다.
/// - 앞면으로 놓이면 "When cards are placed face up in your security stack" · 상대 쪽 "When face-up cards are added to your opponent's security stack"
/// - 앞면 카드를 체크하면 "When this Digimon / your Digimon checks a face-up security card" · [시큐리티] "If this card was face-up"
/// </summary>
public partial class GoldfishEngine
{
    private static readonly MethodInfo CloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>카드 복사 (CardData는 83 소유 파일이라 여기서 얕은 복사)</summary>
    private static CardData CopyCard(CardData card) => (CardData)CloneMethod.Invoke(card, null)!;

    /// <summary>이 시큐리티 카드가 앞면인지</summary>
    public static bool IsFaceUp(PlayerZones zones, CardData card) => zones.FaceUpSecurity.Contains(card) && zones.SecurityStack.Contains(card);

    /// <summary>앞면 시큐리티 카드 (위 → 아래)</summary>
    internal static List<CardData> FaceUpCards(PlayerZones zones) => zones.SecurityStack.Where(zones.FaceUpSecurity.Contains).ToList();

    /// <summary>시큐리티를 떠난 카드의 앞면 표시를 지운다 (다시 뒷면으로 들어와도 앞면으로 남지 않도록)</summary>
    internal static void PruneFaceUp(PlayerZones zones)
    {
        if (zones.FaceUpSecurity.Count > 0)
            zones.FaceUpSecurity.RemoveWhere(c => !zones.SecurityStack.Contains(c));
    }

    /// <summary>카드를 시큐리티 맨 위/맨 아래에 앞면으로 놓는다 (+ 앞면으로 놓였을 때 효과)</summary>
    internal void PlaceSecurityFaceUp(PlayerZones owner, CardData card, bool top)
    {
        PruneFaceUp(owner);
        var faceUp = CopyCard(card);
        if (top) owner.SecurityStack.Insert(0, faceUp);
        else owner.SecurityStack.Add(faceUp);
        owner.FaceUpSecurity.Add(faceUp);
        NotifySecurityAdded(owner, faceUp);
        EffectLog(owner, $"[앞면 시큐리티] [{card.Name}]을(를) 시큐리티 맨 {(top ? "위" : "아래")}에 앞면으로");
        FireForSide(EffectTrigger.AllyFaceUpSecurityPlaced, owner, null, triggerCard: faceUp);
        FireForSide(EffectTrigger.OpponentFaceUpSecurityPlaced, EnemyOf(owner), null, triggerCard: faceUp);
    }

    /// <summary>위에서 첫 번째 뒷면 시큐리티 카드를 앞면으로 뒤집는다. 반환: 뒤집은 카드</summary>
    internal CardData? FlipTopFaceDownUp(PlayerZones zones)
    {
        PruneFaceUp(zones);
        int i = zones.SecurityStack.FindIndex(c => !zones.FaceUpSecurity.Contains(c));
        if (i < 0) return null;
        var faceUp = CopyCard(zones.SecurityStack[i]);
        zones.SecurityStack[i] = faceUp;
        zones.FaceUpSecurity.Add(faceUp);
        return faceUp;
    }

    /// <summary>위에서 첫 번째 앞면 시큐리티 카드를 뒷면으로 뒤집는다. 반환: 뒤집었는지</summary>
    internal static bool FlipTopFaceUpDown(PlayerZones zones)
    {
        PruneFaceUp(zones);
        var card = zones.SecurityStack.FirstOrDefault(zones.FaceUpSecurity.Contains);
        return card != null && zones.FaceUpSecurity.Remove(card);
    }

    /// <summary>체크한 시큐리티 카드가 앞면이었는지 (체크 직후 [시큐리티] "If this card was face-up"에 쓴다)</summary>
    internal bool LastCheckedWasFaceUp { get; private set; }

    /// <summary>시큐리티 체크로 맨 위 카드를 뺀 직후: 앞면이었으면 "checks a face-up security card" 효과 (attacker = 체크한 디지몬)</summary>
    private void OnSecurityCardChecked(PlayerZones securityOwner, CardData card, CardInstance attacker)
    {
        LastCheckedWasFaceUp = securityOwner.FaceUpSecurity.Remove(card);
        var attackerOwner = EnemyOf(securityOwner);
        // v0.33.0 "When this Digimon checks your opponent's security stack" — 체크할 때마다
        if (attackerOwner.BattleArea.Contains(attacker))
            FireForSide(EffectTrigger.AllyCheckedSecurity, attackerOwner, attacker, triggerCard: card);
        if (!LastCheckedWasFaceUp) return;
        if (attackerOwner.BattleArea.Contains(attacker))
            FireForSide(EffectTrigger.AllyCheckedFaceUpSecurity, attackerOwner, attacker, triggerCard: card);
    }
}
