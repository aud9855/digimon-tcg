using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.46.0 진화원 아래에 놓기 (원래 규칙이 읽지 못한 것만 — 동작 · 비용):
///  - "By placing up to 3 red and/or black level 5 cards with the [Cyborg] trait and different card numbers from your hand and/or trash under this Digimon as its bottom
///    digivolution cards, ＜De-Digivolve 1＞ 1 of your opponent's Digimon for each card placed by this effect" (EX3-013 — 공식 "카드 넘버가 서로 다른 ~ 3장까지")
///  - "You may place up to 5 ~ in this card's digivolution cards to gain 1 memory for each card placed" (EX1-073)
///  - "By placing up to 2 Digimon cards with ＜Save＞ in their texts from under your Tamers or your trash under 1 of your Digimon with ＜Save＞ in its text as its bottom
///    digivolution cards, that Digimon gets +1000 DP for each card placed by this effect" (P-096 — 공식 "자신의 테이머 아래 또는 트래시에서")
/// 놓은 장수 = c.PaidCount ("for each card placed by this effect"), 놓은 디지몬 = c.LastTargets ("that Digimon")
/// </summary>
internal static partial class SentenceParser
{
    private const string PlaceFrom46 = @"(?<from>your hand or (?:your )?trash|your trash or (?:your )?hand|under your Tamers or (?:from )?(?:your )?trash|your trash or (?:from )?under your Tamers|your hand|your trash|under your Tamers)";
    private const string PlaceUnder46 = @"(?:under (?<t>.+?) as (?:its|their) bottom digivolution cards?|as (?<t2>.+?)'s bottom digivolution cards?|in (?<self>this card's|this Digimon's|its) digivolution cards)";

    /// <summary>"red and/or black" · "hand and/or trash" · "and different card numbers" 표기를 맞춘다 (distinct = 카드 넘버가 서로 다른)</summary>
    private static string NormalizePlace46(string s, out bool distinct)
    {
        distinct = Regex.IsMatch(s, @"\b(?:and|with) different card numbers\b", I);
        s = Regex.Replace(s, @"\s*\b(?:and|with) different card numbers\b", "", I);
        s = Regex.Replace(s, @"\b(?<a>red|blue|yellow|green|black|purple|white) and/or (?<b>red|blue|yellow|green|black|purple|white)\b", "${a} or ${b}", I);
        s = Regex.Replace(s, @"\byour hand and/or (?:your )?trash\b", "your hand or trash", I);
        return s;
    }

    private sealed record PlaceSpec46(int Count, Func<CardData, bool> Match, bool Hand, bool Trash, bool Under, bool Distinct, TargetSpec? Target);

    private static PlaceSpec46? ParsePlaceSpec46(Match m, bool distinct)
    {
        var pick = ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value);
        if (pick == null) return null;
        TargetSpec? target = null;
        if (!m.Groups["self"].Success)
        {
            string t = m.Groups["t"].Success ? m.Groups["t"].Value : m.Groups["t2"].Value;
            target = TargetSpec.Parse(t);
            if (target == null || target.Opponent) return null;
        }
        string from = m.Groups["from"].Value.ToLowerInvariant();
        return new PlaceSpec46(pick.Value.Count, pick.Value.Match, from.Contains("hand"), from.Contains("trash"), from.Contains("under"), distinct, target);
    }

    private static List<PoolCard> PlacePool46(EffectContext c, PlaceSpec46 p)
    {
        var pool = new List<PoolCard>();
        Func<CardData, bool> ok = x => p.Match(x) && !ReferenceEquals(x, c.Card);
        if (p.Trash) pool.AddRange(PoolFrom(c.Owner.Trash, ok));
        if (p.Under) pool.AddRange(PoolFrom(c.Owner.UnderTamers, ok));
        if (p.Hand) pool.AddRange(PoolFrom(c.Owner.Hand, ok));
        return pool;
    }

    private static CardInstance? PlaceTarget46(EffectContext c, PlaceSpec46 p) =>
        p.Target == null ? (c.Self != null && c.Owner.BattleArea.Contains(c.Self) ? c.Self : null) : Pick(p.Target, c, false).FirstOrDefault();

    /// <summary>놓는다 — 놓은 장수 (c.PaidCount · c.LastTargets도 정한다)</summary>
    private static int DoPlace46(EffectContext c, PlaceSpec46 p)
    {
        var d = PlaceTarget46(c, p);
        c.PaidCount = 0;
        if (d == null) return 0;
        var zoneKind = p.Hand && p.Trash ? CardChoiceZone.HandOrTrash : p.Hand ? CardChoiceZone.Hand : p.Trash && !p.Under ? CardChoiceZone.Trash : CardChoiceZone.Other;
        var chosen = new List<PoolCard>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        while (chosen.Count < p.Count)
        {
            var pool = PlacePool46(c, p)
                .Where(x => !chosen.Any(y => ReferenceEquals(y.Card, x.Card) && ReferenceEquals(y.Zone, x.Zone)) && (!p.Distinct || !usedIds.Contains(x.Card.CardId)))
                .OrderBy(x => ReferenceEquals(x.Zone, c.Owner.Hand) ? 1 : 0).ThenByDescending(x => x.Card.Level).ToList();
            if (pool.Count == 0) break;
            var ks = c.Engine.ChooseCardIndexes(c.Owner, zoneKind, pool.Select(x => x.Card).ToList(), 1, upTo: true,
                $"[{c.Card.Name}] [{d.TopCard.Name}]의 진화원 아래에 놓을 카드를 고르세요 ({chosen.Count}/{p.Count}{(p.Distinct ? ", 카드 넘버가 서로 다르게" : "")} — 그만 놓으려면 고르지 않음).");
            if (ks.Count == 0) break;
            var pk = pool[Math.Clamp(ks[0], 0, pool.Count - 1)];
            chosen.Add(pk);
            usedIds.Add(pk.Card.CardId);
        }
        int placed = 0;
        var names = new List<string>();
        foreach (var x in chosen)
        {
            if (!TakeFromPool(c, x)) continue;
            d.AddBottomSource(x.Card);
            c.LastCard = x.Card;
            names.Add(x.Card.Name);
            placed++;
        }
        c.PaidCount = placed;
        c.LastTargets = new List<CardInstance> { d };
        if (placed > 0)
        {
            c.RecalculateFieldBuffs();
            Diag46.Hit("placeSources");
            c.Log($"[{c.Card.Name}] [{string.Join(", ", names)}]을(를) [{d.TopCard.Name}]의 진화원 아래에 놓았습니다.");
        }
        return placed;
    }

    /// <summary>동작 "(You may) place ~" (ActionExtra46에서)</summary>
    private static (Action<EffectContext>? Act, string? Error)? PlaceAction46(string s)
    {
        string n = NormalizePlace46(s, out bool distinct);
        var m = Regex.Match(n, $@"^(?<may>you may )?place (?<pick>.+?) from {PlaceFrom46} {PlaceUnder46}$", I);
        if (!m.Success || ParsePlaceSpec46(m, distinct) is not { } p) return null;
        return (c =>
        {
            if (DoPlace46(c, p) == 0) c.LastStepResolved = false;
        }, null);
    }

    /// <summary>비용 "placing ~" (ParseCost46에서)</summary>
    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? PlaceCost46(string cost)
    {
        string n = NormalizePlace46(cost.Trim(), out bool distinct);
        var m = Regex.Match(n, $@"^placing (?<pick>.+?) from {PlaceFrom46} {PlaceUnder46}$", I);
        if (!m.Success || ParsePlaceSpec46(m, distinct) is not { } p) return null;
        bool upTo = IsUpTo(m.Groups["pick"].Value);
        return (c =>
            {
                bool hasTarget = p.Target == null ? c.Self != null && c.Owner.BattleArea.Contains(c.Self) : c.Owner.BattleArea.Any(x => p.Target.Filters.All(f => f(x)));
                if (!hasTarget) return false;
                var pool = PlacePool46(c, p);
                int avail = p.Distinct ? pool.Select(x => x.Card.CardId).Distinct().Count() : pool.Count;
                return upTo ? avail >= 1 : avail >= p.Count;
            },
            c =>
            {
                int k = DoPlace46(c, p);
                if (k == 0 || (!upTo && k < p.Count)) c.CostFailed = true;
            },
            c => true,
            "카드를 진화원 아래에 놓아");
    }
}

internal static partial class SentenceParser
{
    /// <summary>
    /// 공식 한글의 이름 진화 조건 〔진화〕「도루가몬」 : 코스트 0 (BT17-065 · BT17-067 · BT17-073) — 영어 데이터에는 기본 진화 조건만 있어,
    /// "by digivolving it into this card" 비용에서만 이 조건도 본다 (일반 진화의 이름 조건은 아직 엔진에 없다 — 다음 묶음)
    /// </summary>
    internal static bool KoreanNameEvo46(CardData into, CardInstance from)
    {
        var ko = DigimonTcg.Core.Data.KoreanCardData.Get(into.CardId)?.MainText;
        if (string.IsNullOrEmpty(ko) || from.TopCard.Type != CardType.Digimon) return false;
        string fromKo = DigimonTcg.Core.Data.KoreanCardData.DisplayName(from.TopCard);
        foreach (Match m in Regex.Matches(ko, @"〔진화〕\s*「(?<n>[^」]+)」\s*[:：]\s*코스트\s*\d+"))
            if (m.Groups["n"].Value == fromKo) { Diag46.Hit("koreanNameEvo"); return true; }
        return false;
    }
}
