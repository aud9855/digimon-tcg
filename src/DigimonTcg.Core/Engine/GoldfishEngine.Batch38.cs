using System.Linq;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>v0.38.0 미지원 문장 12차 묶음의 엔진 쪽 (SentenceParser.Batch38.cs)</summary>
public partial class GoldfishEngine
{
    /// <summary>효과로 테이머를 액티브로 ("Unsuspend 1 of your Tamers") — "can't unsuspend" 상태면 그대로</summary>
    internal bool UnsuspendTamer38(PlayerZones owner, CardData tamer)
    {
        if (!owner.Tamers.Contains(tamer) || !owner.SuspendedTamers.Contains(tamer) || TamerUnsuspendLocked(owner, tamer))
            return false;
        owner.SuspendedTamers.Remove(tamer);
        var proxy = owner.BattleArea.FirstOrDefault(d => ReferenceEquals(d.TamerProxyOf, tamer));   // v0.36.0 디지몬으로도 취급하는 테이머
        if (proxy != null) proxy.IsSuspended = false;
        RecalculateFieldBuffs();
        Diag38.Hit(ref Diag38.TamerUnsuspend);
        return true;
    }
}

public partial class GoldfishEngine
{
    private static readonly (Effects.Keyword Keyword, CardColor Color)[] DecoyColors38 =
    {
        (Effects.Keyword.DecoyRed, CardColor.Red), (Effects.Keyword.DecoyBlue, CardColor.Blue), (Effects.Keyword.DecoyYellow, CardColor.Yellow),
        (Effects.Keyword.DecoyGreen, CardColor.Green), (Effects.Keyword.DecoyBlack, CardColor.Black), (Effects.Keyword.DecoyPurple, CardColor.Purple),
        (Effects.Keyword.DecoyWhite, CardColor.White),
    };

    /// <summary>
    /// v0.38.0 효과로 얻은 ≪가드≫ (다른 자신의 디지몬이 상대의 효과로 떠날 때) · ≪디코이 (색)≫ (그 색의 다른 자신의 디지몬이 상대의 효과로 소멸할 때) —
    /// 이 디지몬(guard)을 소멸시키는 것으로 막는다. 카드에 적힌 키워드는 효과로 등록되어 있으므로 얻은 키워드(ActiveKeywords)만 본다
    /// </summary>
    private bool GrantedGuardOrDecoy38(PlayerZones zones, CardInstance guard, CardInstance leaving, bool deletion, bool inBattle)
    {
        if (inBattle || ReferenceEquals(guard, leaving) || !zones.BattleArea.Contains(guard) || CurrentLeave is not { } l || l.ByOwnEffect) return false;
        bool guardKw = guard.ActiveKeywords.Contains(Effects.Keyword.Guard);
        bool decoy = deletion && DecoyColors38.Any(x => guard.ActiveKeywords.Contains(x.Keyword) && leaving.Colors.Contains(x.Color));
        if (!guardKw && !decoy) return false;
        string kw = guardKw ? "≪가드≫" : "≪디코이≫";
        if (!AskYesNo(zones, guard, $"{kw} [{guard.TopCard.Name}]을(를) 소멸시켜 [{leaving.TopCard.Name}]을(를) 지킬까요?", recommended: leaving.TopCard.Level >= guard.TopCard.Level))
            return false;
        EffectLog(zones, $"[{kw}] [{guard.TopCard.Name}]을(를) 소멸시켜 [{leaving.TopCard.Name}]을(를) 지킵니다.");
        DeleteFrom(zones, guard, DeletionCause.Cost);
        Diag38.Hit(ref Diag38.GuardDecoy);
        return true;
    }
}

/// <summary>v0.38.0 시뮬레이션 진단 — 새 동작이 몇 번 일어났는지 (테스트 · 하네스가 읽는다)</summary>
public static class Diag38
{
    public static int GuardDecoy, RefCompare, TamerUnsuspend, MultiZoneCost, SplitStep, ChosenDelete, OtherCost, ThisUnsuspend, GainAttack, DnaWhen, Ascension, PlaceTopBottom, SecurityTopBottom, SecurityEotPlay;
    public static void Hit(ref int counter) => System.Threading.Interlocked.Increment(ref counter);
    public static void Reset() => GuardDecoy = RefCompare = TamerUnsuspend = MultiZoneCost = SplitStep = ChosenDelete = OtherCost = ThisUnsuspend = GainAttack = DnaWhen = Ascension = PlaceTopBottom = SecurityTopBottom = SecurityEotPlay = 0;
}

public partial class GoldfishEngine
{
    /// <summary>
    /// v0.38.0 "[Security] [End of Opponent's Turn] Play this card without paying the cost" — 종합 룰 15-14-5-1: {시큐리티} 효과는 그 카드가 시큐리티에 앞면으로 있는 동안 유발 · 발휘.
    /// 상대의 턴 종료 시, 자신의 시큐리티의 앞면인 그 카드를 코스트 없이 등장시킨다 (위에서부터)
    /// </summary>
    private void PlayFaceUpSecurityAtEndOfOpponentTurn38(PlayerZones owner)
    {
        foreach (var card in FaceUpCards(owner).Where(c => Effects.CardScripts.For(c)?.SecurityPlayAtEndOfOpponentTurn38 == true).ToList())
        {
            if (State.IsGameOver || !IsFaceUp(owner, card)) continue;
            if (card.Type == CardType.Digimon && owner.BattleArea.Count >= 12) continue;
            bool played = TryPlayByEffect(owner, card, PlayOrigin.Security, () =>
            {
                int at = owner.SecurityStack.IndexOf(card);
                if (at < 0) return false;
                owner.SecurityStack.RemoveAt(at);
                owner.FaceUpSecurity.Remove(card);
                NotifySecurityRemoved(owner, card);
                return true;
            });
            if (played)
            {
                EffectLog(owner, $"[{card.Name}] [시큐리티] 【상대의 턴 종료 시】 앞면인 이 카드를 코스트 없이 등장시켰습니다.");
                Diag38.Hit(ref Diag38.SecurityEotPlay);
            }
        }
    }
}
