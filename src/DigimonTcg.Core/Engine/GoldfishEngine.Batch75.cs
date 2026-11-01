using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.75.0 미지원 문장 49차 묶음 (엔진):
///  - DP를 가진 디지타마 카드(EX2-007 「마더 디・리퍼」 DP 15000)의 육성 에어리어 → 배틀 에어리어 이동 (종합 룰 4-17-2 "이동은 DP를 가진 디지몬만").
///    육성 에어리어의 카드의 효과는 육성 에어리어를 지정 · 참조하지 않는 한 발휘하지 않으므로 (3-4-7-4) EX2-007의 【메인】 · 【자신의 턴】은 배틀 에어리어에서 쓴다
///  - AI의 배틀 에어리어 디지몬 링크 (종합 룰 6-5-1-4) — 링크로 얻는 것(링크 DP · 링크 효과 · ≪분리≫ 지정)이 배틀 에어리어에서 잃는 것(그 디지몬 · 진화원 · 어택)보다 클 때만.
///    같은 판단을 플레이어 화면의 추천(★)에도 쓴다
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>육성 에어리어에서 배틀 에어리어로 이동할 수 있는 맨 위 카드 — Lv.3 이상 디지몬, DP를 가진 디지타마 카드 (종합 룰 4-17-2)</summary>
    internal static bool CanMoveFromBreeding75(CardData top) =>
        top.Type == CardType.Digimon && top.Level >= 3 || top.Type == CardType.Digitama && top.DP > 0;

    // =====================================================================
    // 배틀 에어리어 디지몬 링크의 값어치 (AI · 화면 추천)
    // =====================================================================

    /// <summary>
    /// moving(배틀 에어리어)을 target에 cost를 내고 링크할 때의 값어치 (0보다 크면 할 만하다).
    /// 얻는 것: 링크 DP · 링크 효과(〈링크〉 하단 · 【링크 시】) · ≪분리≫ 지정 링크 카드 · target의 레벨.
    /// 잃는 것: moving의 DP · 레벨 · 진화원(트래시 — 4-7-8) · 링크 카드 · 아직 액티브면 어택/블록할 수 있는 몸 · 낼 코스트
    /// </summary>
    internal int FieldLinkScore75(PlayerZones owner, CardInstance moving, CardInstance target, int cost)
    {
        var card = moving.TopCard;
        int gain = LinkDp(card) / 1000 * 5 + target.TopCard.Level + (HasLinkEffect75(card) ? 6 : 0) + (Detach40Specified(target, card) ? 100 : 0);
        if (target.LinkedCards.Count >= MaxLinks(target))   // 넘치면 가장 먼저 링크한 카드를 트래시 (LinkCard)
        {
            var old = target.LinkedCards[0];
            gain -= LinkDp(old) / 1000 * 5 + (HasLinkEffect75(old) ? 6 : 0) + (Detach40Specified(target, old) ? 100 : 0);
        }
        int loss = moving.GetCurrentDP() / 1000 * 2 + card.Level * 2 + moving.EvolutionSources.Count * 6 + moving.LinkedCards.Count * 6
                   + (moving.IsSuspended ? 0 : 8) + (!moving.IsSuspended && CardScripts.HasKeyword(moving, Keyword.Blocker) ? 10 : 0);
        return gain - loss - cost * 3;
    }

    private static bool HasLinkEffect75(CardData card) =>
        CardScripts.For(card) is { } s && s.Effects.Any(e => e.Source == EffectSource.Link43 || e.Trigger == EffectTrigger.OnLinked);

    /// <summary>owner의 배틀 에어리어 디지몬 링크 후보 중 값어치가 가장 큰 것 (memory = 낼 수 있는 코스트 상한). 값어치가 0 이하면 null</summary>
    internal (CardInstance Moving, CardInstance Target, int Cost, int Score)? BestFieldLink75(PlayerZones owner, int memory, ISet<CardInstance>? skip = null)
    {
        (CardInstance, CardInstance, int, int)? best = null;
        foreach (var moving in owner.BattleArea.ToList())
        {
            if (IsToken(moving.TopCard) || LinkCostOf(moving.TopCard) is not int baseCost || skip?.Contains(moving) == true) continue;
            foreach (var target in owner.BattleArea)
            {
                if (ReferenceEquals(target, moving) || IsToken(target.TopCard) || MeetsLinkRequirement(moving.TopCard, target) != true) continue;
                int cost = ReduceLinkCost41(owner, target, moving.TopCard, baseCost, apply: false);
                if (cost > memory) continue;
                int score = FieldLinkScore75(owner, moving, target, cost);
                if (score > 0 && (best == null || score > best.Value.Item4)) best = (moving, target, cost, score);
            }
        }
        return best;
    }

    /// <summary>플레이어 화면: moving을 링크할 때 추천할 디지몬 (값어치가 0 이하면 null — 메모리를 상대 쪽으로 넘기지 않는 것만)</summary>
    public CardInstance? RecommendFieldLink75(CardInstance moving)
    {
        CardInstance? best = null;
        int bestScore = 0;
        foreach (var t in GetFieldLinkTargets(moving))
        {
            if (GetFieldLinkCost(moving, t) is not int cost || cost > State.MemoryGauge) continue;
            int score = FieldLinkScore75(State.Player, moving, t, cost);
            if (score > bestScore) { bestScore = score; best = t; }
        }
        return best;
    }

    /// <summary>AI가 이 턴에 링크하려다 막힌 디지몬 ("떠나려 할 때" 효과 — 같은 디지몬을 다시 고르지 않게)</summary>
    private readonly HashSet<CardInstance> _aiFieldLinkTried75 = new(ReferenceEqualityComparer.Instance);
    private int _aiFieldLinkTurn75 = -1;

    /// <summary>AI: 배틀 에어리어의 디지몬 링크 하나 (handScore = 패에서 링크하는 가장 좋은 후보의 값어치 — 그보다 좋을 때만). 했으면 true</summary>
    private bool AiTryFieldLink75(AiTurnContext ctx, int? handScore)
    {
        var owner = State.DummyOpponent;
        if (_aiFieldLinkTurn75 != State.TurnSequence) { _aiFieldLinkTried75.Clear(); _aiFieldLinkTurn75 = State.TurnSequence; }
        if (BestFieldLink75(owner, ctx.Memory, _aiFieldLinkTried75) is not { } pick || handScore is int hs && hs >= pick.Score) return false;
        var (moving, target, _, _) = pick;
        _aiFieldLinkTried75.Add(moving);
        var card = moving.TopCard;
        int cost = ReduceLinkCost41(owner, target, card, LinkCostOf(card) ?? 0, apply: true);
        int before = ctx.Memory;
        ctx.Memory -= cost;
        Diag46.Hit("aiFieldLink75");
        Log($"🤖 [AI 링크] 배틀 에어리어의 [{card.Name}] → [{target.TopCard.Name}] (코스트 {cost}, AI 메모리: {before} → {ctx.Memory})");
        AddOpponentEvent(OpponentActionKind.Tamer, "링크", $"{card.Name} (배틀 에어리어) → {target.TopCard.Name} · 메모리 {before} → {ctx.Memory}", card);
        AddEffectEvent(card, CaptureEffectLogs(() => LinkFieldDigimon73(owner, moving, target)));
        return true;
    }
}

public partial class GoldfishEngine
{
    /// <summary>ChooseAiLink41이 고른 패의 링크 후보의 값어치 (배틀 에어리어 링크와 비교)</summary>
    private int _aiHandLinkScore75 = int.MinValue;
}
