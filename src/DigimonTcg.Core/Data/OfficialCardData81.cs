using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Data;

/// <summary>
/// v0.81.0 영어 API 데이터의 수치 · 문구 오류를 공식 값으로 고친다 (도감을 읽을 때 메모리에서만 — data/cards_cache.json 파일은 API 값 그대로 저장).
///
/// 1. 수치 (등장 코스트 · DP): 공식 영어 카드 리스트(로컬 data/en/card_rules.json — tools/ensite/collect_en.ps1)의 값이 API와 다르고,
///    공식 한글 카드(로컬 data/kr/cards_ko.json)의 값도 공식 영어와 같을 때만 고친다. 공식 영어 카드 리스트에도 틀린 값이 있다
///    (P-058 DP · BT10-086 Lv. · 색 칸에 진화 코스트의 색을 같이 적은 카드 — 한글 카드 이미지와 대조) — 그래서 둘이 같아야 한다.
///    한글 데이터가 없는 카드(신규 세트)는 API 값이 비어 있을 때(0)만 공식 영어 값으로 채운다. 파일이 없으면 아무것도 고치지 않는다.
///    종류 · Lv. · 색은 공식 영어와 공식 한글이 함께 API와 다른 카드가 없어 고치지 않는다 (scratchpad 감사 aud81 — 4,354장).
/// 2. 문구: 카드 번호 + API 문장(정확히 같을 때만) → 공식 영어 문장 (공식 한글과 같은 뜻인 것만).
///    효과 시점이 공식(에라타)과 다른 문장 · ACE 카드의 진화원 효과 칸에 ≪오버플로우≫만 있고 진화원 효과가 빠진 카드.
/// </summary>
public static class OfficialCardData81
{
    /// <summary>고친 칸 하나</summary>
    public sealed record Change(string CardId, string Field, string Before, string After);

    /// <summary>
    /// 문구 고침: (카드 번호, 진화원 칸인지, API 문장, 공식 문장). API 문장이 그 칸에 정확히 1번 있을 때만 바꾼다.
    /// </summary>
    internal static readonly (string Id, bool Inherited, string Api, string Official)[] TextFixes =
    {
        // BT5-029 — 공식 영어 · 공식 한글: 메인 【자신의 턴】(API [When Digivolving]), 진화원 【서로의 턴】 "명칭에 「가루몬」 또는 「오메가몬」을 포함하는 동안" (API [Your Turn] · KendoGarurumon 제외)
        ("BT5-029", false,
            "[When Digivolving] While a Digimon card with [WereGarurumon] in its name is in this Digimon's digivolution cards, this Digimon gains ＜Jamming＞ (This Digimon can't be deleted in battles against Security Digimon.)",
            "[Your Turn] While a Digimon card with [WereGarurumon] in its name is in this Digimon's digivolution cards, this Digimon gains ＜Jamming＞ (This Digimon can't be deleted in battles against Security Digimon.)"),
        ("BT5-029", true,
            "[Your Turn] When this Digimon has [Omnimon] or [Garurumon] in its name (other than [KendoGarurumon]), it gets +1000 DP.",
            "[All Turns] While this Digimon has [Garurumon] or [Omnimon] in its name, it gets +1000 DP."),
        // BT5-092 — 공식 영어 · 공식 한글: 【메인】 (API [Your Turn]). 공식 영어는 「도르그레이몬」 · 「버닝그레이몬」 · 「데크스도르그레이몬」을 뺀다 (명칭 포함 — 공식 한글 이름에는 「그레이몬」이 들어 있지 않다)
        ("BT5-092", false,
            "[Your Turn] When one of your Digimon would digivolve into a Digimon card in your hand with [Greymon], [Garurumon] or [Omnimon] in its name, by suspending this Tamer, reduce the digivolution cost by 1.",
            "[Main] When one of your Digimon would digivolve into a Digimon card in your hand with [Greymon], [Garurumon] or [Omnimon] in its name (other than [DoruGreymon], [BurningGreymon] or [DexDoruGreymon]), by suspending this Tamer, reduce the digivolution cost by 1."),
        // BT6-066 — 공식 영어 · 공식 한글: 【상대의 턴】 (API [Your Turn])
        ("BT6-066", false,
            "[Your Turn] [Once Per Turn] When one of your other Digimon is deleted, ＜De-Digivolve 1＞ 1 of your opponent's Digimon. (Trash the top card. You can't trash past level 3 cards.)",
            "[Opponent's Turn] [Once Per Turn] When one of your other Digimon is deleted, ＜De-Digivolve 1＞ 1 of your opponent's Digimon. (Trash the top card. You can't trash past level 3 cards.)"),
        // BT9-088 — 공식 영어 · 공식 한글: 【서로의 턴】 (API [Your Turn])
        ("BT9-088", false,
            "[Your Turn] When one of your green or blue Digimon deletes an opponent's Digimon in battle and survives, you may suspend this Tamer to ＜Draw 1＞ (Draw 1 card from your deck.)",
            "[All Turns] When one of your green or blue Digimon deletes an opponent's Digimon in battle and survives, you may suspend this Tamer to ＜Draw 1＞ (Draw 1 card from your deck.)"),
        // ST22-03 — 공식 영어 · 공식 한글: 【이동 시】【진화 시】 (API [On Play])
        ("ST22-03", false,
            "[When Moving] [On Play] Reveal the top 3 cards of your deck.",
            "[When Moving] [When Digivolving] Reveal the top 3 cards of your deck."),
        // EX6-022 — 공식 영어 · 공식 한글: 진화원 효과 【자신의 턴】 (API [All Turns])
        ("EX6-022", true,
            "[All Turns] While this Digimon has the [Angel] or [Three Great Angels] trait, it gains ＜Alliance＞",
            "[Your Turn] While this Digimon has the [Angel] or [Three Great Angels] trait, it gains ＜Alliance＞"),
        // ACE 카드: API 진화원 칸에는 ≪오버플로우≫만 있다 — 공식 영어 · 공식 한글의 진화원 효과를 ≪오버플로우≫ 뒤에 붙인다
        ("BT19-011", true, Overflow(3), Overflow(3) + "\n[All Turns] Add 3000 to this Digimon's DP deletion effects' maximums."),
        ("BT19-037", true, Overflow(3), Overflow(3) + "\n[When Attacking] 1 of your opponent's Digimon gets -4000 DP for the turn."),
        ("BT19-050", true, Overflow(3), Overflow(3) + "\n[Your Turn] This Digimon gets +4000 DP."),
        ("EX9-013", true, Overflow(4), Overflow(4) + "\n＜Security A. +1＞ (This Digimon checks 1 additional security card.)"),
        ("EX9-020", true, Overflow(4), Overflow(4) + "\n[Your Turn] This Digimon's attack target can't change."),
        ("LM-026", true, Overflow(4), Overflow(4) + "\n[All Turns] Add 5000 to this Digimon's DP deletion effects' maximums."),
        ("LM-043", true, Overflow(4), Overflow(4) + "\n＜Collision＞ (During this Digimon's attack, give all of your opponent's Digimon ＜Blocker＞, and the opponent blocks if able.)"),
    };

    private static string Overflow(int n) =>
        $"Ace Overflow ＜-{n}＞ (As this card moves from the field or under a card to an area other than those, lose {n} memory.)";

    /// <summary>카드 1장을 공식 값으로 고친다 (내장 카드 · 듀얼 카드는 그대로). 고친 칸 목록을 돌려준다</summary>
    public static List<Change> Apply(CardData card)
    {
        var changes = new List<Change>();
        if (StarterDecks.IsBuiltInCard(card.CardId) || card.IsTamerProxy)
            return changes;

        foreach (var f in TextFixes)
        {
            if (!string.Equals(f.Id, card.CardId, StringComparison.OrdinalIgnoreCase)) continue;
            string text = f.Inherited ? card.InheritedEffectText : card.EffectText;
            string norm = (text ?? string.Empty).Replace(' ', ' ');   // API 문장에는 줄바꿈 없는 공백(U+00A0)이 섞여 있다 — 같은 길이로 맞춰 찾는다
            int at = norm.IndexOf(f.Api, StringComparison.Ordinal);
            if (at < 0 || norm.IndexOf(f.Api, at + 1, StringComparison.Ordinal) >= 0) continue;
            string raw = text ?? string.Empty;
            string fixedText = raw[..at] + f.Official + raw[(at + f.Api.Length)..];
            if (f.Inherited) card.InheritedEffectText = fixedText; else card.EffectText = fixedText;
            changes.Add(new Change(card.CardId, f.Inherited ? "진화원 효과" : "효과", f.Api, f.Official));
        }

        if (card.IsDual)
            return changes;
        var en = EnglishCardRules.Get(card.CardId);
        if (en == null)
            return changes;
        var ko = KoreanCardData.Get(card.CardId);
        if (en.PlayCost is int cost && cost != card.PlayCost && Confirmed(ko?.PlayCost, ko != null, cost, card.PlayCost))
        {
            changes.Add(new Change(card.CardId, "등장 코스트", card.PlayCost.ToString(), cost.ToString()));
            card.PlayCost = cost;
        }
        if (card.Type != CardType.Digitama && en.DP is int dp && dp != card.DP && Confirmed(ko?.DP, ko != null, dp, card.DP))
        {
            changes.Add(new Change(card.CardId, "DP", card.DP.ToString(), dp.ToString()));
            card.DP = dp;
        }
        return changes;
    }

    /// <summary>공식 한글 값이 공식 영어와 같으면 확인 — 한글 카드가 없으면 API 값이 비어 있을 때(0)만</summary>
    private static bool Confirmed(string? koValue, bool hasKo, int official, int api)
    {
        if (!hasKo) return api == 0;
        var m = Regex.Match(koValue ?? string.Empty, @"\d+");
        return m.Success && int.TryParse(m.Value, out int k) && k == official;
    }
}
