using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.47.0 미지원 문장 21차 묶음: 원래 규칙(1~20차)이 읽지 못한 절만 21차 규칙으로 다시 읽는다.
///  - 영어 데이터에서 문장이 잘린 카드 (카드 번호 · 칸 · 원문이 정확히 같을 때만) — 공식 영어 카드 리스트(world.digimoncard.com)의 문구 구조로 고쳐 읽는다
///  - 디지타마의 진화원 칸에 메인 칸 "Inherited Effect ~"의 깨진 복사본이 들어 있는 카드 — 메인 칸 문장이 진화원 효과를 등록하므로 등록하지 않는다
/// 렌더러도 같은 표 (KoreanEffectRenderer.Batch47.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _clauseDepth47;

    /// <summary>카드 번호 → (깨진 원문, 고친 문장 — null이면 메인 칸 "Inherited Effect ~"의 깨진 복사본이라 등록하지 않는다)</summary>
    internal static readonly (string CardId, string Broken, string? Fixed)[] CardTextFixes47 =
    {
        // 듀얼 카드 옵션 쪽 — 이름 · 키워드가 빠졌다
        ("EX13-065", "[Main] You may play 1 play cost 4 or lower card with from your hand or trash without paying the cost. Then, to 1 of your opponent's Digimon, give -3000 DP for the turn for each of your Digimon.",
            "[Main] You may play 1 play cost 4 or lower card with [Sistermon] in its name from your hand or trash without paying the cost. Then, to 1 of your opponent's Digimon, give -3000 DP for the turn for each of your Digimon."),
        ("EX13-066", "[Main] You may play 1 play cost 4 or lower card with from your hand or trash without paying the cost. Then, to 1 of your opponent's Digimon, for each of your Digimon.",
            "[Main] You may play 1 play cost 4 or lower card with [Sistermon] in its name from your hand or trash without paying the cost. Then, to 1 of your opponent's Digimon, ＜De-Digivolve 1＞ for each of your Digimon."),
        ("BT26-033", "[Main] Delete all of your opponent's Digimon with the lowest DP. Then,",
            "[Main] Delete all of your opponent's Digimon with the lowest DP. Then, ＜Recovery +1 (Deck)＞ (Place the top card of your deck on top of your security stack.)"),
        ("BT26-056", "[Main] Trash 1 card in your hand. Then,",
            "[Main] Trash 1 card in your hand. Then, ＜De-Digivolve 3＞ 1 of your opponent's Digimon."),
        ("BT26-057", "[Main] Then, until their turn ends, 1 of their Digimon gets ''\" This Digimon attacks.\"''",
            "[Main] ＜De-Digivolve 1＞ 1 of your opponent's Digimon. Then, give 1 of their Digimon \"[Start of Your Main Phase] This Digimon attacks.\" until their turn ends."),
        ("BT25-057", "[Main] 1 of your Digimon gains , and +5000 DP until your opponent's turn ends. Then, it may attack.",
            "[Main] 1 of your Digimon gains ＜Rush＞, ＜Security A. +1＞ and +5000 DP until your opponent's turn ends. Then, it may attack."),
        // 디지타마 진화원 칸의 깨진 복사본 (메인 칸 "Inherited Effect ~"가 온전한 문장)
        ("P-245", "[Once Per Turn] By suspending 1 of your Digimon with , if your hand has 7 or fewer cards, Draw 1", null),
        ("P-246", "[Once Per Turn] When any of your other Digimon with are deleted, this Digimon may digivolve into a Digimon card with in the hand with the cost reduced by 2.", null),
        ("EX13-002", "[Once Per Turn] When any of your Tamers are played, this Digimon with may unsuspend.", null),
        ("EX12-004", "This Digimon with the [TB] trait gains", null),
        ("EX13-006", "[Once Per Turn] By paying 1 cost, 1 of your Digimon with the [X Antibody|Chronicle] trait may unsuspend.", null),
        ("BT26-005", "By trashing the bottom face-down card from under any of your Tamers, you may play 1 play cost 5 or lower [Avian] trait or [DATA SQUAD] trait trait card from your trash without paying the cost.", null),
        ("EX12-001", "This [VB] trait Digimon and any of your other Digimon may DNA digivolve into a [VB] trait Digimon card in the hand. Then, that DNA digivolved Digimon may attack.", null),
    };

    /// <summary>깨진 복사본의 생성 한글은 메인 칸의 온전한 문장으로 (공식 영어 카드 리스트와 같은 문장 — 렌더러만 쓴다)</summary>
    internal static readonly Dictionary<string, string> DuplicateKoFrom47 = new(StringComparer.Ordinal)
    {
        ["[Once Per Turn] By suspending 1 of your Digimon with , if your hand has 7 or fewer cards, Draw 1"] =
            "[End of All Turns] [Once Per Turn] By suspending 1 of your black Digimon with ＜Blocker＞, if your hand has 7 or fewer cards, ＜Draw 1＞ (Draw 1 card from your deck.)",
        ["[Once Per Turn] When any of your other Digimon with are deleted, this Digimon may digivolve into a Digimon card with in the hand with the cost reduced by 2."] =
            "[Your Turn] [Once Per Turn] When any of your other Digimon with [Sukamon] or [Mamemon] in their names are deleted, this Digimon may digivolve into a Digimon card with [Sukamon], [Etemon] or [Mamemon] in its name in the hand with the cost reduced by 2.",
        ["[Once Per Turn] When any of your Tamers are played, this Digimon with may unsuspend."] =
            "[Your Turn] [Once Per Turn] When any of your blue Tamers are played, this Digimon with [Veedramon] in its name may unsuspend.",
        ["This Digimon with the [TB] trait gains"] =
            "[Your Turn] This Digimon with the [TB] trait gains ＜Execute＞ (At the end of your turn, this Digimon may attack. At the end of that attack, delete this Digimon. Your opponent's unsuspended Digimon can also be attacked with this effect.)",
    };

    private static (bool Hit, string? Fixed) CardTextFix47(string clause, bool inherited, CardData card)
    {
        string t = clause.Trim();
        foreach (var f in CardTextFixes47)
            if (f.CardId == card.CardId && f.Broken == t)
            {
                if (f.Fixed != null ? !(inherited || card.Type == CardType.Option) : !inherited) return (false, null);   // 듀얼 카드 옵션 쪽은 옵션 카드로 읽는다 (KeywordDictionary.OptionSideCard)
                if (f.Fixed == null && !(card.EffectText ?? "").Contains("Inherited Effect", StringComparison.OrdinalIgnoreCase)) return (false, null);
                return (true, f.Fixed);
            }
        return (false, null);
    }

    /// <summary>
    /// [육성]【자신의 메인 페이즈 개시 시】 디지타마 덱 (엔진: GoldfishEngine.Batch47):
    ///  - "Reveal the top card of your Digi-Egg deck, then place that card and all of your [X] trait Digimon as this Digimon's bottom digivolution cards." (BT13-007)
    ///  - "Look at your Digi-Egg deck's top card. Among them, you may place [X]s as this Digimon's top digivolution cards. Then, if this Digimon has N or more digivolution cards,
    ///    you may play M [X]s from its digivolution cards without paying the costs." (BT22-007)
    /// </summary>
    private static bool BreedingStartOfMain47(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        if (inherited || card.Type is not (CardType.Digimon or CardType.Digitama)) return false;
        var m = System.Text.RegularExpressions.Regex.Match(rawClause.Trim(), @"^\[Breeding\]\s*\[Start of Your Main Phase\]\s*(?<b>.+?)\.?$", I);
        if (!m.Success) return false;
        string b = m.Groups["b"].Value.Trim();
        System.Text.RegularExpressions.Match x;
        if ((x = System.Text.RegularExpressions.Regex.Match(b, @"^Reveal the top card of your Digi-Egg deck, then place that card and all of your \[(?<t>[^\]]+)\] trait Digimon as this Digimon's bottom digivolution cards$", I)).Success)
        {
            string t = x.Groups["t"].Value;
            def?.Script41.Effects.Add(new CardEffect(EffectTrigger.StartOfYourMainPhase, EffectSource.Breeding42, c => c.Engine.BreedingRevealAndPlaceUnder47(c, d => TargetSpec.HasTrait(d, t))));
            return true;
        }
        if ((x = System.Text.RegularExpressions.Regex.Match(b, @"^Look at your Digi-Egg deck's top card\. Among them, you may place \[(?<n>[^\]]+)\]s? as this Digimon's top digivolution cards\. Then, if this Digimon has (?<k>\d+) or more digivolution cards, you may play (?<p>\d+) \[\k<n>\]s? from its digivolution cards without paying the costs?$", I)).Success)
        {
            string n = x.Groups["n"].Value; int k = int.Parse(x.Groups["k"].Value), p = int.Parse(x.Groups["p"].Value);
            def?.Script41.Effects.Add(new CardEffect(EffectTrigger.StartOfYourMainPhase, EffectSource.Breeding42, c => c.Engine.BreedingLookPlaceThenPlay47(c, d => CardNames.Is(d, n), k, p)));
            return true;
        }
        return false;
    }

    private static string? TryParse47(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse46(rawClause, inherited, card, def);
        if (err == null || _clauseDepth47 > 0) return err;
        _clauseDepth47++;
        try
        {
            if (BreedingStartOfMain47(rawClause, inherited, card, def)) return null;
            var (hit, fix) = CardTextFix47(rawClause, inherited, card);
            if (!hit) return err;
            if (fix == null) return null;   // 깨진 복사본 — 메인 칸 문장이 등록한다
            if (TryParse46(fix, inherited, card, null) is { } e2) return e2;
            if (def != null) TryParse46(fix, inherited, card, def);
            return null;
        }
        finally { _clauseDepth47--; }
    }
}
