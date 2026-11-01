using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.55.0 같은 카드가 여러 장이면 효과도 장마다 (29차 묶음).
///  - 종합 룰 15-14-1-3: 같은 이름 · 같은 카드 번호라도 카드가 다르면 [턴에 1회]는 따로 센다 (카드마다 다른 효과)
///  - 진화원에 같은 카드가 2장이면 진화원 효과도 2개 — 지속 효과(DP +N 등)는 2번 더하고, 유발 효과는 장마다 발휘, [턴에 1회]도 장마다
///  - 같은 테이머 2장은 [턴에 1회] 기록을 나누지 않는다
/// 덱 목록은 같은 CardData 객체를 여러 번 담는다 (덱 빌더 · 프리셋 · 시뮬레이션). 게임을 시작할 때 두 번째부터는 복사해 카드마다 다른 객체로 만든다.
/// 첫 번째 것은 그대로라 덱 목록의 객체로 찾는 곳은 전과 같다. 같은 객체가 진화원에 2번 들어 있으면(직접 만든 시험 필드) 전처럼 1장으로 본다.
/// </summary>
public static class CardIdentity55
{
    /// <summary>끄면 v0.54.0까지처럼 같은 카드 번호는 1장 · 게임 시작 때 복사하지 않음 (시뮬레이션 비교용)</summary>
    public static bool PerCard { get; set; } = true;

    private static readonly ConditionalWeakTable<CardData, string> Serials = new();
    private static int _next;

    /// <summary>카드 객체마다 다른 이름 ([턴에 1회] 기록 이름에 붙인다)</summary>
    public static string Of(CardData card) => Serials.GetValue(card, _ => "@" + System.Threading.Interlocked.Increment(ref _next));

    /// <summary>진화원 효과를 발휘하는 카드: 카드(객체)마다 — 끄면 카드 번호마다 1장</summary>
    public static IEnumerable<CardData> Each(IEnumerable<CardData> cards) =>
        PerCard ? cards.Distinct(ReferenceEqualityComparer.Instance).Cast<CardData>() : cards.GroupBy(s => s.CardId).Select(g => g.First());

    /// <summary>그 카드(객체)가 아직 목록에 있는지 — 끄면 같은 카드 번호가 있는지</summary>
    public static bool StillIn(IEnumerable<CardData> cards, CardData card) =>
        PerCard ? cards.Any(c => ReferenceEquals(c, card)) : cards.Any(c => c.CardId == card.CardId);
}

public partial class GoldfishEngine
{
    /// <summary>
    /// 게임을 시작할 때 같은 CardData 객체가 두 번 이상 나오면 두 번째부터 복사한다 (양쪽 덱 · 패 · 디지타마 덱 — 서로 같은 객체도).
    /// 섞은 순서는 바꾸지 않는다
    /// </summary>
    private void MakeCardsDistinct55()
    {
        if (!CardIdentity55.PerCard) return;
        var seen = new HashSet<CardData>(ReferenceEqualityComparer.Instance);
        CardData Own(CardData c)
        {
            if (seen.Add(c)) return c;
            var copy = CardInstance.CopyCard(c);
            seen.Add(copy);
            return copy;
        }
        foreach (var z in new[] { State.Player, State.DummyOpponent })
        {
            if (z == null) continue;
            foreach (var list in new[] { z.Hand, z.MainDeck, z.SecurityStack, z.Trash, z.Tamers })
                for (int i = 0; i < list.Count; i++) list[i] = Own(list[i]);
            // v0.71.0 필드 · 육성 · 테이머 아래도 (Stage 2 수비대처럼 시작할 때 필드에 놓인 카드 — 앞/뒷면 · 기록이 카드마다 따로)
            for (int i = 0; i < z.UnderTamers.Count; i++) z.UnderTamers[i] = Own(z.UnderTamers[i]);
            foreach (var d in z.BattleArea.Append(z.BreedingArea))
            {
                if (d == null) continue;
                if (!seen.Add(d.TopCard)) Diag46.Hit("distinctField71");   // 맨 위 카드가 겹치면 기록만 (맨 위 카드는 바꾸지 않는다)
                foreach (var x in d.EvolutionSources) seen.Add(x);
            }
            if (z.DigitamaDeck.Count > 0)
            {
                var eggs = z.DigitamaDeck.ToArray();   // 위 → 아래
                z.DigitamaDeck.Clear();
                foreach (var egg in eggs.Reverse().Select(Own).ToList()) z.DigitamaDeck.Push(egg);
            }
        }
    }
}

public partial class GoldfishEngine
{
    /// <summary>진화원 [메인] 기동 효과의 사용 기록 이름 — 진화원 카드(객체)마다 (끄면 카드 번호마다)</summary>
    private static object InheritedMainKey55(CardInstance d, CardData source) =>
        CardIdentity55.PerCard ? (d, (object)source) : (d, source.CardId);
}

public partial class GoldfishEngine
{
    /// <summary>코스트 감소의 [턴에 1회] 기록 주인: 진화원 효과면 (디지몬, 진화원 카드) — 같은 카드 2장은 따로 (끄면 디지몬)</summary>
    private static object OnceHolder55(EffectSource source, CardInstance? holder, CardData card) =>
        holder == null ? card : CardIdentity55.PerCard && source == EffectSource.Inherited ? (holder, card) : holder;
}

/// <summary>
/// v0.55.0 효과 처리 중에 유발한 효과는 그 효과가 끝난 뒤 (28차 묶음의 나머지 — 종합 룰 15-8-3-2 · 15-4-4):
/// 효과로 한 부화 · 육성 에어리어에서 이동 · 링크 · ≪디지버스트≫ 비용 · 효과로 사용한 옵션의 "사용했을 때".
/// 효과 밖(육성 페이즈의 부화 · 이동, 패에서 링크, 패에서 옵션 사용)은 전과 같이 바로
/// </summary>
public partial class GoldfishEngine
{
    private void FireHatched55(PlayerZones owner, CardInstance? hatched, CardData egg)
    {
        var witness = DeferWitness54();
        if (Deferring54) Diag46.Hit("hatchDeferred55");
        Defer54(() =>
        {
            if (State.IsGameOver) return;
            FireForSide(EffectTrigger.AllyHatched, owner, hatched, triggerCard: egg, witness: witness);
        });
    }

    private void FireMovedFromBreeding55(PlayerZones owner, CardInstance moving)
    {
        var witness = DeferWitness54();
        bool deferred = Deferring54;
        if (deferred) Diag46.Hit("moveDeferred55");
        Defer54(() =>
        {
            if (State.IsGameOver || !owner.BattleArea.Contains(moving)) return;   // 15-4-4-3 발휘 전에 떠났으면 [이동 시]도 없다
            RunEffects(EffectTrigger.WhenMoving, owner, moving, moving.TopCard);
            if (owner.BattleArea.Contains(moving))
            {
                FireForSide(EffectTrigger.AllyDigimonMovedFromBreeding, owner, moving, witness: witness);
                FireForSide(EffectTrigger.OpponentDigimonMovedFromBreeding, EnemyOf(owner), moving, witness: witness);
            }
        });
    }

    private void FireLinked55(PlayerZones owner, CardInstance target, CardData card)
    {
        var witness = DeferWitness54();
        bool deferred = Deferring54;
        if (deferred) Diag46.Hit("linkDeferred55");
        Defer54(() =>
        {
            if (State.IsGameOver || !owner.BattleArea.Contains(target)) return;
            if (!deferred || target.LinkedCards.Any(l => ReferenceEquals(l, card)))   // 링크 카드의 "링크했을 때"는 그 카드가 아직 링크되어 있을 때
                RunEffects(EffectTrigger.OnLinked, owner, target, card, includeInherited: false);
            if (owner.BattleArea.Contains(target))
                RunEffects(EffectTrigger.ThisDigimonLinked, owner, target, target.TopCard);
            if (owner.BattleArea.Contains(target))
                FireForSide(EffectTrigger.AllyDigimonLinked, owner, target, triggerCard: card, witness: witness);   // TriggerCard = 링크한 카드 (v0.31.0)
        });
    }

    private void FireDigiBurst55(PlayerZones owner, CardInstance self, List<CardData> removed)
    {
        var witness = DeferWitness54();
        bool deferred = Deferring54;
        if (deferred) Diag46.Hit("digiBurstDeferred55");
        Defer54(() =>
        {
            if (State.IsGameOver) return;
            // "When this card is trashed by ＜Digi-Burst＞" — 발휘 전에 트래시를 떠난 카드는 새 카드 (15-4-4-3)
            var still = deferred ? removed.Where(c => owner.Trash.Any(t => ReferenceEquals(t, c))).ToList() : removed;
            if (still.Count > 0)
                RunEffects(EffectTrigger.DigiBurstTrashedThis, owner, self, null, sources: still);
            if (!State.IsGameOver)
                FireForSide(EffectTrigger.AllyDigiBurst, owner, self, witness: witness);
        });
    }
}
