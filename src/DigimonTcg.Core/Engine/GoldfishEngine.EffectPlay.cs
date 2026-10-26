using System;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.36.0 "효과로 등장" 한 곳 — 효과로 카드를 등장시키는 모든 길은 <see cref="TryPlayByEffect"/>를 지난다.
/// 등장시킬 수 없으면(플레이어 제한) 카드를 원래 있던 곳에서 꺼내지 않는다 — 카드는 그 자리에 남는다
/// (공개한 카드처럼 이미 어느 곳에도 없는 카드는 호출한 쪽이 "남은 카드"와 같이 처리한다).
/// · "Players can't play Digimon by effects" (BT14-009 · BT9-033 · BT9-047, 공식 "서로는 효과로 디지몬을 등장시킬 수 없다" — Q&A BT9-033: 옵션 카드의 효과도)
/// · "Your opponent can't play Digimon (or Tamers) by effects until ~" (BT8-097 · BT20-020) · "their effects can't play Digimon or Tamers from the trash" (BT23-014)
/// · "Players can't reduce play costs" (BT8-071 · EX7-015 · ST12-03 · ST13-08, Q&A: 등장 코스트를 지불할 때 마이너스할 수 없다 — 양쪽 플레이어)
/// · "Your opponent can't use Option cards ~" (EX1-072 · BT8-057, Q&A EX1-072: 체크된 옵션의 【시큐리티】 효과는 사용이 아니다)
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>owner가 지금 card를 효과로 등장시킬 수 없는 이유 (등장시킬 수 있으면 null). 효과로 등장하는 길이 모두 여기서 판단한다</summary>
    internal string? EffectPlayBlock(PlayerZones owner, CardData card, PlayOrigin origin)
    {
        bool digimon = card.Type == CardType.Digimon, tamer = card.Type == CardType.Tamer;
        if (!digimon && !tamer)
            return null;
        if (digimon && State.Player.BattleArea.Concat(State.DummyOpponent.BattleArea).Any(d => CardScripts.HasKeyword(d, Keyword.PlayersCannotPlayDigimonByEffects)))
            return "서로는 효과로 디지몬을 등장시킬 수 없습니다";
        if (digimon && EffectRules.IsActive(owner.CannotPlayDigimonByEffectsUntil, State))
            return "상대의 효과로 효과로 디지몬을 등장시킬 수 없습니다";
        if (tamer && EffectRules.IsActive(owner.CannotPlayTamersByEffectsUntil, State))
            return "상대의 효과로 효과로 테이머를 등장시킬 수 없습니다";
        if (origin == PlayOrigin.Trash && EffectRules.IsActive(owner.CannotPlayFromTrashByEffectsUntil, State))
            return "상대의 효과로 트래시에서 효과로 등장시킬 수 없습니다";
        if (EffectRules.PlayDpBlocked(State, owner, card))   // v0.37.0 "can't play Digimon with N DP or less" (Q&A EX3-012: 효과로도)
            return $"상대의 효과로 DP {card.DP} 이하의 디지몬을 등장시킬 수 없습니다";
        return null;
    }

    /// <summary>효과로 등장시키려다 막힌 횟수 (시뮬레이션 · 진단용)</summary>
    public int EffectPlayRefusedTotal { get; private set; }

    /// <summary>owner가 지금 card를 효과로 등장시킬 수 있는지 (후보를 고를 때)</summary>
    internal bool CanPlayByEffect(PlayerZones owner, CardData card, PlayOrigin origin) => EffectPlayBlock(owner, card, origin) == null;

    /// <summary>
    /// 효과로 등장 — 한 곳. 등장시킬 수 없으면 take를 부르지 않고(카드는 원래 있던 곳에 그대로) false.
    /// take: 카드를 있던 곳에서 꺼낸다 (null = 이미 어느 곳에도 없는 카드 — 공개한 카드 · 토큰). 꺼내지 못하면 false.
    /// payCost: 코스트를 내고 등장 ("with the play cost reduced by N" · "for a play cost of N"), null이면 코스트 없이.
    /// toBreeding: 비어 있는 육성 에어리어에 (【등장 시】 없음)
    /// </summary>
    internal bool TryPlayByEffect(PlayerZones owner, CardData card, PlayOrigin origin, Func<bool>? take,
        bool triggerOnPlay = true, bool suspended = false, int? payCost = null, bool toBreeding = false)
    {
        if (EffectPlayBlock(owner, card, origin) is { } why)
        {
            EffectLog(owner, $"[{card.Name}] {why}.");
            EffectPlayRefusedTotal++;
            return false;
        }
        if (toBreeding && (owner.BreedingArea != null || card.Type != CardType.Digimon))
            return false;
        if (take != null && !take())
            return false;
        if (toBreeding)
            return PlayToBreeding(owner, card);
        if (payCost is int cost)
        {
            AddMemory(owner, -cost);
            if (ReferenceEquals(owner, State.Player)) State.TotalMemorySpent += cost;
            EffectLog(owner, $"[효과] [{card.Name}] 등장 코스트 {cost} 지불");
        }
        PlayWithoutCostFrom(owner, card, origin, triggerOnPlay, suspended);
        return true;
    }

    /// <summary>"Players can't reduce play costs" — 지금 등장 코스트를 마이너스할 수 없는지 (양쪽 플레이어, 디지몬 · 테이머를 등장시킬 때)</summary>
    internal static bool PlayCostReductionBlocked(PlayerZones owner, PlayerZones? enemy, CardData card) =>
        card.Type is CardType.Digimon or CardType.Tamer && !card.IsDual
        && owner.BattleArea.Concat(enemy?.BattleArea ?? Enumerable.Empty<CardInstance>()).Any(d => CardScripts.HasKeyword(d, Keyword.PlayersCannotReducePlayCosts));

    internal bool PlayCostReductionBlocked(PlayerZones owner, CardData card) => PlayCostReductionBlocked(owner, EnemyOf(owner), card);

    /// <summary>효과로 등장시킬 때 줄인 코스트 (마이너스할 수 없으면 원래 코스트)</summary>
    internal int ReducedPlayCost(PlayerZones owner, CardData card, int reduced, int full) =>
        PlayCostReductionBlocked(owner, card) ? Math.Max(full, reduced) : reduced;
}

public static partial class EffectRules
{
    /// <summary>
    /// v0.36.0 user가 지금 옵션 카드를 사용할 수 없는지 — "Your opponent can't use Option cards until ~" (EX1-072) ·
    /// "[Opponent's Turn] While all of your Digimon are suspended, your opponent can't use Option cards" (BT8-057, 상대 필드의 지속 효과)
    /// </summary>
    public static bool OptionUseBlocked(GameState state, PlayerZones user)
    {
        if (IsActive(user.CannotUseOptionsUntil, state))
            return true;
        var enemy = ReferenceEquals(user, state.Player) ? state.DummyOpponent : state.Player;
        return enemy.BattleArea.Any(d => CardScripts.HasKeyword(d, Keyword.OpponentCannotUseOptions));
    }
}
