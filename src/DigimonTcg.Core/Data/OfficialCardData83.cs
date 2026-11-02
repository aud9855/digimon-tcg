using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Data;

/// <summary>
/// v0.83.0 영어 API에 효과 문구가 통째로 없거나 잘린 카드를 공식 영어 문구로 채운다 (도감을 읽을 때 메모리에서만 — data/cards_cache.json 파일은 API 값 그대로 저장.
/// OfficialCardData81 · 82와 같은 방식). 공식 영어 카드 리스트(world.digimoncard.com)의 문구와 공식 일본어 카드 리스트(digimoncard.com)의 문구가 같은 뜻인 것만
/// (공식 한글 카드가 있으면 공식 한글도 같다 — scratchpad 감사 aud83: 공식 영어 효과 줄 · 시점 표시 · [ ] 이름/특징을 API와 대조).
///
/// 1. 칸 채우기: 카드 번호 + API 칸 전체가 정확히 이 값일 때만 (빈 칸 · "|applinkdp =" 같은 API 잡음 · 앞 줄만 남은 칸) 공식 문구로 바꾼다
///    - 키워드가 여럿인 줄 ("＜Reboot＞ ＜Blocker＞")은 API처럼 한 줄에 하나씩 · 다른 API 카드와 같은 설명문(괄호)을 붙인다 (뜻은 같다)
///    - 공식 "{Trash}" · "{Hand}" 표시는 API 표기 "[Trash]" · "[Hand]"
/// 2. 문구 고침: OfficialCardData81.TextFixes와 같은 규칙 (카드 번호 + API 문장이 그 칸에 정확히 1번 있을 때만)
/// 3. 3색 이상 카드의 세 번째 색부터 (CardData는 색 2개 칸만 있다 — API에는 3번째 색이 없다): 공식 영어 · 공식 일본어 카드 리스트의 색
/// </summary>
public static class OfficialCardData83
{
    private const string Blocker = "＜Blocker＞ (This Digimon can block in the blocker timing.)";
    private const string Reboot = "＜Reboot＞ (This Digimon also unsuspends in your opponent's unsuspend phase.)";
    private const string Piercing = "＜Piercing＞ (When this Digimon deletes your opponent's Digimon in battle while attacking, it checks security before the attack ends.)";
    private const string SecA1 = "＜Security A. +1＞ (This Digimon checks 1 additional security card.)";
    private const string Barrier = "＜Barrier＞ (When this Digimon would be deleted in battle, by trashing your top security card, it isn't deleted.)";
    private const string SuccessionReminder = "(This Digimon gains all effects other than ＜Succession＞ on its topmost specified digivolution card.)";
    private const string TamerSecurity = "Security Effect [Security] Play this card without paying the cost.";

    /// <summary>칸 채우기: (카드 번호, 진화원 칸인지, API 칸 전체(정확히 같을 때만 — 여럿이면 그중 하나), 공식 문구)</summary>
    internal static readonly (string Id, bool Inherited, string[] Api, string Official)[] FieldFills =
    {
        // ---- 효과 칸이 비어 있는 디지몬 (공식 영어 = 공식 일본어)
        ("BT24-028", false, new[] { "" },
            "[On Play] [When Digivolving] By placing 1 level 5 or lower blue [TS] trait Digimon card from your hand as this Digimon's bottom digivolution card, until your opponent's turn ends, this Digimon can't be deleted in battle and gains ＜Blocker＞.\n" +
            "[Your Turn] When this Digimon unsuspends, it may digivolve into [Neptunemon] in the hand without paying the cost."),
        ("BT24-028", true, new[] { "" },
            "[When Attacking] [Once Per Turn] You may play 1 level 4 or lower blue Digimon card with the [TS] trait from this Digimon's digivolution cards without paying the cost."),
        ("BT24-061", false, new[] { "" },
            "[On Play] [When Digivolving] Return 1 of your opponent's play cost 3 or lower Digimon or Tamers to the top of the deck."),
        ("BT24-061", true, new[] { "" },
            "[When Attacking] [Once Per Turn] ＜De-Digivolve 1＞ 1 of your opponent's Digimon. (Trash the top card. You can't trash past level 3 cards.)"),
        ("BT24-068", false, new[] { "" },
            "[On Play] Reveal the top 3 cards of your deck. Add 1 card with the [Evil] or [Fallen Angel] trait and 1 card with the [Seven Great Demon Lords] trait among them to the hand. Return the rest to the bottom of the deck. Then, trash 1 card in your hand."),
        ("BT24-068", true, new[] { "" },
            "[When Attacking] [Once Per Turn] Trash the top card of both players' decks."),
        ("BT24-078", false, new[] { "" },
            "[Trash] [Your Turn] When one of your [Creepymon] attacks, if your opponent has 10 or more cards in their trash, by digivolving it into this card without paying the cost, trash your opponent's top security card.\n" +
            "[When Digivolving] Delete all of your opponent's lowest level Digimon. Then, you may play up to 4 play cost's total worth of [Evil] or [Fallen Angel] trait cards from your trash without paying the cost. For every 10 cards in your opponent's trash, add 4 to this effect's play cost maximum."),
        ("BT26-001", true, new[] { "", "|applinkdp =" },
            "[Your Turn] [Once Per Turn] When your effects add to decks, this Digimon may digivolve into a Digimon card with [Chronomon] in its text in the hand with the cost reduced by 1."),
        ("BT26-012", false, new[] { "" },
            "[Main] [Once Per Turn] You may play or use 1 [TB] trait card from your hand with the cost reduced by 2."),
        ("BT26-012", true, new[] { "", "|applinkdp =" },
            "[When Attacking] [Once Per Turn] 1 of your opponent's Digimon gets -2000 DP for the turn."),
        ("BT26-014", false, new[] { "" },
            "[On Play] [When Digivolving] Delete 1 of your opponent's Digimon with 7000 DP or less.\n" +
            "[On Deletion] You may return 1 [Shambala] trait card from your trash to the hand. Then, you may play 1 [TB] trait Digimon card with 6000 DP or less from your hand without paying the cost."),
        ("BT26-014", true, new[] { "", "|applinkdp =" },
            "[On Deletion] You may play 1 [TB] trait Digimon card with 6000 DP or less from your hand without paying the cost."),
        ("BT26-024", false, new[] { "" },
            "[Your Turn] When any of your other Digimon with the [Vegetation], [Fairy] or [WG] trait are played, this Digimon may digivolve into a Digimon card with the [Vegetation], [Fairy] or [WG] trait in the hand without paying the cost."),
        ("BT26-024", true, new[] { "", "|applinkdp =" }, Barrier),
        ("BT26-027", false, new[] { "" },
            "[Start of Opponent's Main Phase] [On Play] By suspending 1 of your Digimon with the [Vegetation], [Fairy] or [WG] trait, give 1 of your opponent's Digimon ＜Security A. -2＞ until their turn ends."),
        ("BT26-027", true, new[] { "", "|applinkdp =" }, Barrier),
        ("BT26-054", false, new[] { "" },
            "[On Play] [When Digivolving] You may play 1 [CS] trait Tamer card from your hand without paying the cost. This effect can't play cards with the same name as any of your Tamers.\n" +
            "[All Turns] [Once Per Turn] When effects place [CS] trait Digimon cards in this Digimon's digivolution cards, this Digimon may digivolve into a [CS] trait Digimon card in the hand without paying the cost."),
        ("BT26-054", true, new[] { "", "|applinkdp =" },
            "[Opponent's Turn] [Once Per Turn] When one of your opponent's Digimon attacks, you may change the attack target to this Digimon."),
        ("BT26-058", false, new[] { "" },
            Reboot + "\n" + Blocker + "\n" +
            "[When Digivolving] [When Attacking] [Once Per Turn] Your opponent's Digimon effects don't affect 1 of your [CS] trait Digimon until their turn ends.\n" +
            "[All Turns] When any of your [CS] trait Digimon would leave the battle area, by placing this Digimon's top stacked card as its bottom digivolution card, they don't leave."),
        ("BT26-060", false, new[] { "" },
            SecA1 + "\n" + Reboot + "\n" + Blocker + "\n" +
            "＜Succession (Lv.6 w/[Chronomon] in name)＞ " + SuccessionReminder + "\n" +
            "[On Play] [When Digivolving] Return the top 5 stacked cards of 3 of your opponent's Digimon to the top of the deck.\n" +
            "[All Turns] [Once Per Turn] When your effects add to decks, you may delete 1 of your opponent's Digimon."),
        ("BT26-103", false, new[] { "" },
            Piercing + "\n" + Reboot + "\n" + Blocker + "\n" +
            "＜Succession ([Jupitermon])＞ " + SuccessionReminder + "\n" +
            "[When Digivolving] [Counter] [Once Per Turn] Trash your top security card and ＜Recovery +2＞.\n" +
            "[All Turns] [Once Per Turn] When security stacks are removed from, 1 of your opponent's Digimon gets -15000 DP until their turn ends."),
        // BT26-080 (듀얼): 디지몬 쪽 칸에 옵션의 색 조건 줄만 있다 · 옵션 쪽은 사용 조건 줄만 있다
        ("BT26-080", false, new[] { "Option Card Colour Requirements Purple" },
            SecA1 + "\n" +
            "＜Succession ([Bacchusmon])＞ " + SuccessionReminder + "\n" +
            "[When Digivolving] By suspending 1 Digimon, this Digimon may attack without suspending.\n" +
            "[When Attacking] [Once Per Turn] Delete 1 of your opponent's Digimon with the same orientation as this Digimon."),
        ("BT26-080", true, new[] { "Use Requirement: TS trait" },
            "Use Requirement: TS trait\n" +
            "[Main] You may unsuspend 1 Digimon. Then, delete all of your opponent's unsuspended Digimon with the lowest DP."),

        // ---- 효과 칸에 [시큐리티]만 있는 테이머 (공식 영어 = 공식 일본어) — 진화원 칸의 [시큐리티]는 그대로
        ("BT25-088", false, new[] { TamerSecurity },
            "[Start of Your Turn] If you have 2 or less memory, set it to 3.\n" +
            "[All Turns] When your security stack is removed from, by suspending this Tamer, you may place the top 2 cards of your deck face down under this Tamer.\n" +
            "[Your Turn] [Once Per Turn] When any of your [Glowing Dawn] trait cards would be played, by trashing the bottom face-down card from under any of your Tamers, reduce the cost by 1."),
        ("BT26-087", false, new[] { TamerSecurity },
            "[Start of Your Main Phase] By returning 1 [TS] trait Digimon card from your trash to the bottom of the deck, gain 1 memory. After, you may return 1 [Giant Slayer] from your trash to the hand.\n" +
            "[On Play] By trashing 1 [TS] trait card from your hand, ＜Draw 2＞."),
        ("BT26-096", false, new[] { TamerSecurity },
            "[Start of Your Turn] If you have 2 or less memory, set it to 3.\n" +
            "[Main] By returning this Tamer to the bottom of the deck, you may play 1 Digimon card with [Chronomon] in its text or 1 Tamer card with the [TS] trait from your hand or trash with the cost reduced by 2."),
        ("BT26-104", false, new[] { TamerSecurity },
            "[Start of Your Main Phase] Gain 1 memory.\n" +
            "[On Play] By trashing 1 [Shambala] trait card from your hand, ＜Draw 2＞.\n" +
            "[End of Your Turn] If you have a Digimon with the [Tentei Hachibushu] trait, by suspending this Tamer, you may use 1 Option card with the [Shambala] trait from your hand without paying the cost."),
        // BT26-089: 두 칸 모두 API 잡음
        ("BT26-089", false, new[] { "|inheriteff =" },
            "[Start of Your Main Phase] By placing 1 [BEATBREAK] trait card from your hand face down under this Tamer, ＜Draw 1＞ and gain 1 memory.\n" +
            "[All Turns] When your security stack is removed from, by suspending this Tamer, place the top card of your deck face down under this Tamer. After, if removed from by effects, give 1 of your opponent's Digimon ＜Security A. -1＞ until their turn ends."),
        ("BT26-089", true, new[] { "|applinkdp =" }, TamerSecurity),

        // ---- [시큐리티] 효과가 빠진 카드 (공식 영어 = 공식 일본어 · BT6-096은 공식 한글도)
        ("EX11-071", true, new[] { "" }, TamerSecurity),
        ("P-234", true, new[] { "" }, TamerSecurity),
        ("BT6-096", true, new[] { "" }, "Security Effect [Security] Add this card to its owner's hand."),

        // ---- 진화원 효과가 빠진 카드 (공식 영어 = 공식 일본어)
        ("EX11-032", true, new[] { "" },
            "[Your Turn] [Once Per Turn] When this Digimon wins a battle, this [Vortex Warriors] trait Digimon may unsuspend."),
        // P-191 ACE: 진화원 칸에 ≪오버플로우≫만 있다 (v0.81.0 BT19-011 등과 같은 경우)
        ("P-191", true, new[] { "Ace Overflow ＜-4＞ (As this card moves from the field or under a card to an area other than those, lose 4 memory.)" },
            "Ace Overflow ＜-4＞ (As this card moves from the field or under a card to an area other than those, lose 4 memory.)\n" +
            "[End of Your Turn] [Once Per Turn] 1 of your Digimon may attack."),
    };

    /// <summary>문구 고침 (카드 번호, 진화원 칸인지, API 문장, 공식 문장) — OfficialCardData81.Apply가 함께 처리</summary>
    internal static readonly (string Id, bool Inherited, string Api, string Official)[] TextFixes =
    {
        // EX11-032: 효과 칸에 첫 줄만 있다 — 【진화 시】 줄 (공식 영어 = 공식 일본어)
        ("EX11-032", false,
            "[Hand] [Main] If you have [Shoto Kazama], by placing 1 [Galemon] from your trash as any of your [Pteromon]'s bottom digivolution card, it digivolves into this card for a digivolution cost of 3, ignoring digivolution requirements.\r\n",
            "[Hand] [Main] If you have [Shoto Kazama], by placing 1 [Galemon] from your trash as any of your [Pteromon]'s bottom digivolution card, it digivolves into this card for a digivolution cost of 3, ignoring digivolution requirements.\n" +
            "[When Digivolving] You may suspend 1 Digimon. Then, you may play 1 3000 DP or lower green Digimon card with [Avian] or [Bird] in any of its traits from your hand without paying the cost. For each suspended Digimon, add 1000 to this effect's DP maximum."),
        // P-238: 【자신의 턴】 ≪딜레이≫ 효과가 빠졌다 (공식 영어 = 공식 일본어)
        ("P-238", false,
            "[Main] Delete 1 of your opponent’s level 6 or lower Digimon. Then, place this card in the battle area.",
            "[Main] Delete 1 of your opponent’s level 6 or lower Digimon. Then, place this card in the battle area.\n" +
            "[Your Turn] When one of your [CS] trait Digimon attacks, ＜Delay＞.\n" +
            "・Delete 1 of your opponent's level 6 or lower Digimon."),
        // BT25-085 (듀얼 옵션 쪽): 사용 조건 줄이 빠졌다 · "1 [Three Musketeers] trait card" (공식 영어 = 공식 일본어)
        ("BT25-085", true,
            "[Main] Delete 1 of your opponent's highest level Digimon. Then, you may place 1 card from your hand or trash as any of your Digimon's bottom digivolution card.",
            "＜Use Req. ([Three Musketeers] in text)＞ (Specified cards let you ignore color requirements.)\n" +
            "[Main] Delete 1 of your opponent's highest level Digimon. Then, you may place 1 [Three Musketeers] trait card from your hand or trash as any of your Digimon's bottom digivolution card."),
        // 특징 이름의 띄어쓰기 (공식 "[Glowing Dawn]")
        ("BT26-031", true, "Use Requirement: GlowingDawn trait", "Use Requirement: Glowing Dawn trait"),
        ("BT26-057", true, "Use Requirement: GlowingDawn trait", "Use Requirement: Glowing Dawn trait"),
        // BT26-033: 공식 영어 "[Iliad] or [TS] trait card" (공식 일본어도 두 가지)
        ("BT26-033", false,
            "you may play or use 1 [Iliad] card from your hand with the cost reduced by 5.",
            "you may play or use 1 [Iliad] or [TS] trait card from your hand with the cost reduced by 5."),
        // P-209: 공식 영어 "[Demon] or [Titan] trait" (공식 일본어 · 공식 한글도 두 특징)
        ("P-209", false,
            "you may play 1 level 4 or lower [Demon] card from your trash without paying the cost.",
            "you may play 1 level 4 or lower [Demon] or [Titan] trait card from your trash without paying the cost."),
        // EX4-072: 고르는 디지몬은 [Gallantmon] / [Sakuyamon] / [MegaGargomon] (공식 영어 · 공식 일본어 · 공식 한글 — API는 "level 6 Digimon")
        ("EX4-072", false,
            "[Main] Choose 1 of your level 6 Digimon. It may digivolve into a level 6 Digimon card in your hand with a different name that includes the name of the chosen Digimon, ignoring digivolution requirements and without paying the cost.",
            "[Main] Choose 1 of your [Gallantmon], [Sakuyamon] or [MegaGargomon]. From your hand, ignoring digivolution requirements and without paying the cost, it may digivolve into a level 6 Digimon card with a different name that includes the chosen Digimon's name."),
        // BT4-099 · BT5-001: 공식 영어 "(other than [DoruGreymon], [BurningGreymon] or [DexDoruGreymon])" — 공식 일본어 · 공식 한글의 그 카드 이름에는 "Greymon"에 해당하는 말이 들어 있지 않다
        //   (v0.81.0 BT5-092와 같은 경우 — 영어 이름으로 판정하므로 공식 영어처럼 뺀다)
        ("BT4-099", false,
            "if one of your Digimon has [Greymon] in its name or [Dramon] in its name,",
            "if one of your Digimon has [Greymon] or [Dramon] in its name (other than [DoruGreymon], [BurningGreymon] or [DexDoruGreymon]),"),
        ("BT5-001", false,
            "If this Digimon has [Greymon] or [Omnimon] in its name,",
            "If this Digimon has [Greymon] or [Omnimon] in its name (other than [DoruGreymon], [BurningGreymon] or [DexDoruGreymon]),"),
        // BT21-027: 디지크로스에서 [OmniShoutmon] / [ZeigGreymon] (공식 영어 · 공식 일본어 · 공식 한글)
        ("BT21-027", false,
            "This card is also treated as [Shoutmon] or [ZeigGreymon] for DigiXros.",
            "This card is also treated as [OmniShoutmon] or [ZeigGreymon] for DigiXros."),
    };

    /// <summary>3색 이상 카드의 세 번째 색부터 (공식 영어 카드 리스트 · 공식 일본어 카드 리스트의 색 — 둘이 같다). 색 순서는 공식 카드 리스트 순서</summary>
    internal static readonly Dictionary<string, CardColor[]> ExtraColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BT16-102"] = new[] { CardColor.Black }, ["BT17-077"] = new[] { CardColor.Green }, ["EX7-037"] = new[] { CardColor.Black }, ["EX7-074"] = new[] { CardColor.Purple },
        ["BT18-018"] = new[] { CardColor.Blue }, ["BT18-042"] = new[] { CardColor.Black }, ["BT18-095"] = new[] { CardColor.Blue }, ["BT18-097"] = new[] { CardColor.Purple },
        ["BT19-101"] = new[] { CardColor.Black }, ["EX8-029"] = new[] { CardColor.Yellow }, ["EX8-064"] = new[] { CardColor.Yellow }, ["EX8-073"] = new[] { CardColor.Yellow },
        ["BT19-014"] = new[] { CardColor.Black }, ["BT19-091"] = new[] { CardColor.Green }, ["BT20-045"] = new[] { CardColor.Blue }, ["BT20-060"] = new[] { CardColor.Red },
        ["BT20-102"] = new[] { CardColor.Red }, ["BT21-052"] = new[] { CardColor.Blue }, ["EX9-021"] = new[] { CardColor.Red }, ["EX9-045"] = new[] { CardColor.Blue },
        ["BT22-015"] = new[] { CardColor.Blue }, ["EX10-011"] = new[] { CardColor.Blue }, ["BT23-047"] = new[] { CardColor.Blue }, ["AD1-006"] = new[] { CardColor.Blue },
        ["AD1-020"] = new[] { CardColor.Green }, ["AD1-023"] = new[] { CardColor.Purple }, ["AD1-025"] = new[] { CardColor.Blue }, ["BT25-084"] = new[] { CardColor.Green },
        ["EX12-037"] = new[] { CardColor.Red }, ["EX12-048"] = new[] { CardColor.Blue }, ["EX12-060"] = new[] { CardColor.Red }, ["EX12-070"] = new[] { CardColor.Blue },
        ["EX12-071"] = new[] { CardColor.Red }, ["EX12-076"] = new[] { CardColor.Red }, ["EX12-077"] = new[] { CardColor.Purple }, ["BT26-081"] = new[] { CardColor.Black },
        ["BT26-103"] = new[] { CardColor.Black }, ["EX13-016"] = new[] { CardColor.Blue }, ["EX13-045"] = new[] { CardColor.Blue }, ["EX13-076"] = new[] { CardColor.Green },
        ["P-185"] = new[] { CardColor.Blue },
    };

    /// <summary>
    /// 이름 고침 (카드 번호, API 이름, 공식 이름): 영어 API 이름에 콜론이 빠진 모드 이름 — 공식 일본어 「～：～」 · 공식 한글 "~: ~"는 모두 콜론이 있다
    /// (EX1-022 · EX2-073 · EX2-074는 공식 영어 카드 리스트도 콜론). 같은 명칭의 다른 카드 · 효과 문구의 "[Imperialdramon: Dragon Mode]"와 같은 명칭이 된다 (BT12-031 등)
    /// </summary>
    internal static readonly (string Id, string Api, string Official)[] NameFixes =
    {
        ("BT3-111", "Imperialdramon Dragon Mode", "Imperialdramon: Dragon Mode"), ("ST9-06", "Imperialdramon Dragon Mode", "Imperialdramon: Dragon Mode"),
        ("EX1-022", "Imperialdramon Dragon Mode", "Imperialdramon: Dragon Mode"), ("BT8-032", "Imperialdramon Fighter Mode", "Imperialdramon: Fighter Mode"),
        ("BT8-112", "Imperialdramon Paladin Mode", "Imperialdramon: Paladin Mode"), ("EX2-038", "Justimon Blitz Arm", "Justimon: Blitz Arm"),
        ("EX2-073", "Gallantmon Crimson Mode", "Gallantmon: Crimson Mode"), ("EX2-074", "Beelzemon Blast Mode", "Beelzemon: Blast Mode"),
    };

    /// <summary>칸 채우기 · 3색 · 이름 (문구 고침은 OfficialCardData81.Apply). 고친 칸을 changes에 더한다</summary>
    internal static void Apply(CardData card, List<OfficialCardData81.Change> changes)
    {
        foreach (var f in NameFixes)
        {
            if (!string.Equals(f.Id, card.CardId, StringComparison.OrdinalIgnoreCase) || card.Name != f.Api) continue;
            card.Name = f.Official;
            changes.Add(new OfficialCardData81.Change(card.CardId, "이름", f.Api, f.Official));
        }
        foreach (var f in FieldFills)
        {
            if (!string.Equals(f.Id, card.CardId, StringComparison.OrdinalIgnoreCase)) continue;
            string text = f.Inherited ? card.InheritedEffectText : card.EffectText;
            string norm = Norm(text);
            if (!f.Api.Any(a => Norm(a) == norm)) continue;
            if (f.Inherited) card.InheritedEffectText = f.Official; else card.EffectText = f.Official;
            changes.Add(new OfficialCardData81.Change(card.CardId, f.Inherited ? "진화원 효과" : "효과", text ?? string.Empty, f.Official));
        }
        if (ExtraColors.TryGetValue(card.CardId, out var extra) && card.ExtraColors83.Count == 0)
        {
            var add = extra.Where(c => !card.AllColors.Contains(c)).ToList();
            if (add.Count > 0 && card.SecondaryColor != null)
            {
                card.ExtraColors83 = add;
                changes.Add(new OfficialCardData81.Change(card.CardId, "세 번째 색", string.Join("/", card.AllColors.Take(2)), string.Join("/", card.AllColors)));
            }
        }
    }

    private static string Norm(string? s) => (s ?? string.Empty).Replace(' ', ' ').Replace("\r\n", "\n").Trim();
}
