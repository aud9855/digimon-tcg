using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.48.0 카드의 보통 진화 조건 (진화 코스트 칸 — "Red Blue 3 from Lv.4" · 두 번째 칸 "Red 1 from Lv.5").
/// 영어 API 데이터에는 칸이 하나(레벨 · 색 1개)뿐이라, 공식 영어 카드 리스트(로컬 data/en/card_rules.json)가 있으면 그 칸 전부를 쓴다:
///  - 종합 룰 2-3-5-1 진화 조건 = 그 카드로 진화할 수 있는 카드의 정보와 진화 코스트 · 8-1-2-1 조건이 여럿이면 어느 것으로 진화할지 고른다 (엔진은 가장 싼 것)
///  - 여러 색이 적힌 칸은 그 색 중 하나를 가진 카드에서 (종합 룰 4-24-1 "AA/BB" = AA 또는 BB · 공식 오메가몬 "Lv.6 레드 · 블루에서")
///  - 진화하는 쪽이 다색이면 가진 색 전부로 본다 (종합 룰 2-4-3-1 다색의 카드는 가지고 있는 모든 색의 카드로 취급)
///  - 멀티컬러 아이콘 칸(all)은 색을 보지 않는다. Lv.이 아닌 칸 (Tamer · 앱몬 등급 Stnd./Sup./Ult.)은 여기서 쓰지 않는다 (각각의 처리가 따로 있다)
/// 파일이 없거나 그 카드가 없으면 API의 칸 하나 (EvoRequirementLevel · EvoRequirementColor · EvoCost).
/// </summary>
public static class EvoRequirements48
{
    /// <summary>진화 조건 칸 하나: Level에서, Colors 중 하나를 가진 카드에서 (비면 아무 색), Cost</summary>
    public sealed record Req(int Level, IReadOnlyList<CardColor> Colors, int Cost);

    private static readonly ConcurrentDictionary<string, IReadOnlyList<Req>> Cache = new(StringComparer.Ordinal);

    /// <summary>evoCard의 보통 진화 조건 칸 전부</summary>
    public static IReadOnlyList<Req> For(CardData evoCard) =>
        Cache.GetOrAdd($"{evoCard.CardId}|{evoCard.Type}|{evoCard.EvoRequirementLevel}|{evoCard.EvoRequirementColor}|{evoCard.EvoCost}", _ => Build(evoCard));

    /// <summary>테스트 · 데이터 다시 읽기 뒤</summary>
    public static void ClearCache() => Cache.Clear();

    private static IReadOnlyList<Req> Build(CardData c)
    {
        var api = new[] { new Req(c.EvoRequirementLevel, new[] { c.EvoRequirementColor }, c.EvoCost) };
        if (c.Type != CardType.Digimon || EnglishCardRules.Get(c.CardId) is not { } en) return api;
        var list = new List<Req>();
        foreach (var e in en.Evo)
        {
            if (e.Level <= 0) continue;
            bool any = e.Colors.Any(x => x.Equals("all", StringComparison.OrdinalIgnoreCase));
            var cols = any ? Array.Empty<CardColor>()
                : e.Colors.Select(EnglishCardRules.ColorOf).Where(x => x != null).Select(x => x!.Value).Distinct().ToArray();
            if (!any && cols.Length == 0) cols = new[] { c.EvoRequirementColor };   // 색이 빠진 칸 — API의 색
            list.Add(new Req(e.Level, cols, e.Cost));
        }
        return list;   // 공식 카드 리스트에 Lv. 칸이 없으면 보통 진화 조건이 없는 카드 (DigiXros · 〔진화〕 조건만 등)
    }

    /// <summary>칸 하나의 색 조건을 target이 만족하는지 (다색은 가진 색 전부 · 효과로 얻은 색 포함 — 카드에 적힌 다른 진화 조건도)</summary>
    private static bool ColorOk(Req r, CardInstance target, CardData evoCard) =>
        r.Colors.Count == 0 || target.Colors.Any(r.Colors.Contains) || CardScripts.MeetsAlternativeEvolution(evoCard, target);

    /// <summary>target(맨 위 카드)에서 evoCard로 보통 진화 조건으로 진화할 수 있으면 그 코스트 (여럿이면 가장 싼 것)</summary>
    public static int? NormalCost(CardInstance target, CardData evoCard)
    {
        int? best = null;
        foreach (var r in For(evoCard))
            if (target.TopCard.Level == r.Level && ColorOk(r, target, evoCard) && (best == null || r.Cost < best)) best = r.Cost;
        return best;
    }

    /// <summary>"Lv.을 무시하고" 진화 — 색 조건만 (공식 BT13-098 · BT12-089): 칸 하나라도 색이 맞으면</summary>
    public static bool ColorOnly(CardInstance target, CardData evoCard) =>
        For(evoCard).Any(r => ColorOk(r, target, evoCard));

    /// <summary>안내 문구용 첫 칸의 레벨 (칸이 없으면 API 레벨)</summary>
    public static int FirstLevel(CardData evoCard) => For(evoCard).FirstOrDefault()?.Level ?? evoCard.EvoRequirementLevel;

    /// <summary>레벨 칸 중 하나가 target 레벨인지</summary>
    public static bool LevelMatches(CardInstance target, CardData evoCard) => For(evoCard).Any(r => r.Level == target.TopCard.Level);
}
