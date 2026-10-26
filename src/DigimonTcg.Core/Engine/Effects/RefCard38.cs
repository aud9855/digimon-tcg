using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.38.0 "놓은 카드 · 되돌린 카드 · 파기한 카드 · 소멸한 디지몬" — 비용("By ~ing, ~")을 내기 전과 낸 뒤의 필드를 비교해
/// 비용이 옮긴 카드 1장을 EffectContext.RefCard38에 남긴다 (공식 BT18-042 "놓은 카드와 같은 Lv.의" · BT11-076 "소멸한 디지몬의 Lv. 이하의").
/// 배틀 에어리어를 떠난 디지몬 → 맨 위 카드가 바뀐 디지몬 → 패 · 트래시 · 진화원 · 시큐리티 · 덱 · 테이머 아래에서 빠진 카드 순으로 본다.
/// </summary>
internal sealed class RefSnap38
{
    private readonly List<(CardInstance Inst, CardData Top, int Dp)> _battle = new();
    private readonly List<(object Key, List<CardData> Cards)> _lists = new();

    private RefSnap38() { }

    public static RefSnap38 Take(EffectContext c)
    {
        var s = new RefSnap38();
        foreach (var z in new[] { c.Owner, c.Enemy })
        {
            foreach (var d in z.BattleArea) s._battle.Add((d, d.TopCard, SafeDp(d)));
            if (z.BreedingArea is { } b) s._battle.Add((b, b.TopCard, SafeDp(b)));
        }
        foreach (var (key, cards) in Lists(c)) s._lists.Add((key, cards.ToList()));
        return s;
    }

    private static int SafeDp(CardInstance d) { try { return d.GetCurrentDP(); } catch { return d.TopCard.DP; } }

    private static IEnumerable<(object Key, IEnumerable<CardData> Cards)> Lists(EffectContext c)
    {
        foreach (var z in new[] { c.Owner, c.Enemy })
        {
            yield return (z.Hand, z.Hand);
            yield return (z.Trash, z.Trash);
            foreach (var d in z.BattleArea) yield return (d, d.EvolutionSources);
            if (z.BreedingArea is { } b) yield return (b, b.EvolutionSources);
            yield return (z.SecurityStack, z.SecurityStack);
            yield return (z.MainDeck, z.MainDeck);
            yield return (z.UnderTamers, z.UnderTamers);
            yield return (z.Tamers, z.Tamers);
        }
    }

    /// <summary>비용이 옮긴 카드 (없으면 null) — Dp는 떠난 디지몬의 마지막 DP (카드만 옮겼으면 카드의 DP)</summary>
    public (CardData Card, int Dp)? Moved(EffectContext c)
    {
        var now = new HashSet<CardInstance>(c.Owner.BattleArea.Concat(c.Enemy.BattleArea), ReferenceEqualityComparer.Instance);
        if (c.Owner.BreedingArea is { } ob) now.Add(ob);
        if (c.Enemy.BreedingArea is { } eb) now.Add(eb);
        foreach (var (inst, top, dp) in _battle)
            if (!now.Contains(inst)) return (top, dp);
        foreach (var (inst, top, dp) in _battle)
            if (!ReferenceEquals(inst.TopCard, top)) return (top, top.DP);
        var current = Lists(c).ToDictionary(x => x.Key, x => x.Cards, ReferenceEqualityComparer.Instance);
        foreach (var (key, before) in _lists)
        {
            if (!current.TryGetValue(key, out var after)) continue;   // 떠난 디지몬의 진화원은 위에서 이미 본다
            var left = new List<CardData>(after);
            foreach (var card in before)
            {
                int at = left.FindIndex(x => ReferenceEquals(x, card));
                if (at >= 0) { left.RemoveAt(at); continue; }
                return (card, card.DP);
            }
        }
        return null;
    }
}
