using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Data;

/// <summary>
/// v0.82.0 영어 API 데이터 오류를 공식 값으로 (도감을 읽을 때 메모리에서만 — data/cards_cache.json 파일은 API 값 그대로 저장. OfficialCardData81과 같은 방식).
/// 공식 영어 카드 리스트 · 공식 일본어 카드 리스트(digimoncard.com) · 공식 한글 카드 중 둘 이상이 같고 API만 다른 칸만 고친다 (scratchpad 감사 aud82 — 일본어 7,407장).
/// 공식 영어 카드 리스트에도 틀린 칸이 있다 (EX3-051 · EX3-052 속성 · P-132 유형 · EX10-016 링크 효과 · 색 칸에 진화 코스트의 색) — 그런 칸은 API 그대로.
///
/// 1. 수치 · 색 (한글 데이터가 없는 카드 — 공식 영어 = 공식 일본어): 카드 번호 + API 값이 그대로일 때만
/// 2. 문구: 카드 번호 + API 문장이 정확히 같을 때만 (OfficialCardData81.Apply가 함께 처리)
///    - 링크 카드의 링크 효과 시점 (API에는 시점이 빠져 있다) · 빠진 링크 효과 · 효과 시점 / [턴에 1회] 오류 · LM-025 ACE 진화원 효과
/// 3. 특징: 〈룰〉 특징 (공식 "(Rule) Trait: Has [Angel] Type." — API 특징에 없다) · 속성 / 유형 오류 · 빠진 그룹 특징 ·
///    형태 (Form — API 특징에 형태가 빠진 카드가 많다: 로컬 공식 영어 카드 리스트 data/en/card_rules.json의 Form, 파일이 없으면 고치지 않는다)
/// </summary>
public static class OfficialCardData82
{
    /// <summary>문구 고침: (카드 번호, 진화원 칸인지, API 문장, 공식 문장) — OfficialCardData81.TextFixes와 같은 규칙</summary>
    internal static readonly (string Id, bool Inherited, string Api, string Official)[] TextFixes =
    {
        // ---- 링크 카드의 링크 효과 (영어 API는 진화원 효과 칸에 둔다) — 시점이 빠진 문장에 공식 시점 (공식 영어 · 공식 일본어, 한글이 있는 카드는 공식 한글도 같다)
        //      【링크 시】: 전에도 라벨 없는 어플몬 문장을 【링크 시】로 읽었다 (SentenceParser.Batch41) — 이제 문구에 적혀 있다
        ("BT21-018", true, "This Digimon may attack.", "[When Linking] This Digimon may attack."),
        ("BT21-023", true, "Delete 1 of your opponent's Digimon with as much or less DP as this Digimon.", "[When Linking] Delete 1 of your opponent's Digimon with as much or less DP as this Digimon."),
        ("BT21-043", true, "1 of your opponent's Digimon gets -2000 DP until their turn ends.", "[When Linking] 1 of your opponent's Digimon gets -2000 DP until their turn ends."),
        ("BT21-053", true, "Until your opponent's turn ends, 1 of their Digimon can't attack players.", "[When Linking] Until your opponent's turn ends, 1 of their Digimon can't attack players."),
        ("BT21-054", true, "Delete 1 of your opponent's Digimon with a play cost of 3 or less.", "[When Linking] Delete 1 of your opponent's Digimon with a play cost of 3 or less."),
        ("BT21-070", true, "You may return 1 Digimon card with the [Appmon] trait from your trash to the hand.", "[When Linking] You may return 1 Digimon card with the [Appmon] trait from your trash to the hand."),
        ("BT21-071", true, "Draw 2 and trash 2 cards in your hand.", "[When Linking] Draw 2 and trash 2 cards in your hand."),
        ("BT21-074", true, "Delete 1 of your opponent's level 4 or lower Digimon.", "[When Linking] Delete 1 of your opponent's level 4 or lower Digimon."),
        ("BT22-009", true, "Delete 1 of your opponent's Digimon with 4000 DP or less.", "[When Linking] Delete 1 of your opponent's Digimon with 4000 DP or less."),
        ("BT22-016", true, "Trash any 1 digivolution card of 1 of your opponent's Digimon.", "[When Linking] Trash any 1 digivolution card of 1 of your opponent's Digimon."),
        ("BT22-035", true, "To 1 of your opponent's Digimon, give -4000 DP for the turn for each of your Digimon with the [Appmon] trait.", "[When Linking] To 1 of your opponent's Digimon, give -4000 DP for the turn for each of your Digimon with the [Appmon] trait."),
        ("BT22-050", true, "Suspend 1 of your opponent's Digimon.", "[When Linking] Suspend 1 of your opponent's Digimon."),
        ("BT23-016", true, "Draw 1", "[When Linking] Draw 1"),
        ("BT23-021", true, "This Digimon can't be deleted in battle until your opponent's turn ends.", "[When Linking] This Digimon can't be deleted in battle until your opponent's turn ends."),
        ("BT23-028", true, "Until your opponent's turn ends, 1 of their Digimon can't activate [When Digivolving] effects.", "[When Linking] Until your opponent's turn ends, 1 of their Digimon can't activate [When Digivolving] effects."),
        ("BT23-039", true, "You may suspend 1 of your opponent's Digimon.", "[When Linking] You may suspend 1 of your opponent's Digimon."),
        ("BT24-032", true, "1 of your opponent's Digimon gets -2000 DP for the turn.", "[When Linking] 1 of your opponent's Digimon gets -2000 DP for the turn."),
        ("BT24-038", true, "1 of your opponent's Digimon gets -7000 DP for the turn.", "[When Linking] 1 of your opponent's Digimon gets -7000 DP for the turn."),
        ("BT24-056", true, "Delete 1 of your opponent's play cost 5 or lower Digimon.", "[When Linking] Delete 1 of your opponent's play cost 5 or lower Digimon."),
        ("BT24-077", true, "Delete 1 of your opponent's Digimon with the lowest DP.", "[When Linking] Delete 1 of your opponent's Digimon with the lowest DP."),
        ("BT25-007", true, "Delete 1 of your opponent's Digimon with 3000 DP or less.", "[When Linking] Delete 1 of your opponent's Digimon with 3000 DP or less."),
        ("BT25-036", true, "By trashing 1 [Appmon] trait card from your hand, Draw 2", "[When Linking] By trashing 1 [Appmon] trait card from your hand, Draw 2"),
        ("BT25-045", true, "Suspend 1 of your opponent's Digimon.", "[When Linking] Suspend 1 of your opponent's Digimon."),
        ("BT25-052", true, "Suspend 1 of your opponent's Digimon or Tamers.", "[When Linking] Suspend 1 of your opponent's Digimon or Tamers."),
        ("BT25-056", true, "Return 1 of your opponent's suspended Digimon to the bottom of the deck.", "[When Linking] Return 1 of your opponent's suspended Digimon to the bottom of the deck."),
        ("BT25-061", true, "1 of your opponent's Digimon can't unsuspend until their turn ends.", "[When Linking] 1 of your opponent's Digimon can't unsuspend until their turn ends."),
        ("BT25-070", true, "1 of your opponent's Digimon or Tamers can't unsuspend until their turn ends.", "[When Linking] 1 of your opponent's Digimon or Tamers can't unsuspend until their turn ends."),
        ("BT25-072", true, "2 of your opponent's Digimon or Tamers can't unsuspend until their turn ends.", "[When Linking] 2 of your opponent's Digimon or Tamers can't unsuspend until their turn ends."),
        ("BT26-019", true, "1 of your opponent's Digimon or Tamers can't suspend until their turn ends.", "[When Linking] 1 of your opponent's Digimon or Tamers can't suspend until their turn ends."),
        ("BT26-028", true, "Until your opponent's turn ends, 1 of their Digimon can't activate [When Digivolving] effects and gets -3000 DP.", "[When Linking] Until your opponent's turn ends, 1 of their Digimon can't activate [When Digivolving] effects and gets -3000 DP."),
        ("BT26-037", true, "This Digimon may battle 1 of your opponent's Digimon.", "[When Linking] This Digimon may battle 1 of your opponent's Digimon."),
        ("BT26-063", true, "Delete 1 of your opponent's Digimon with the lowest level.", "[When Linking] Delete 1 of your opponent's Digimon with the lowest level."),
        ("BT26-084", true, "You may link 1 non-white level 4 or lower [System|Seven Code] trait card from your trash to this Digimon without paying the cost.", "[When Linking] You may link 1 non-white level 4 or lower [System|Seven Code] trait card from your trash to this Digimon without paying the cost."),
        ("P-190", true, "Draw 1", "[When Linking] Draw 1"),
        ("ST22-12", true, "Return 1 of your opponent's 5000 DP or lower Digimon to the bottom of the deck.", "[When Linking] Return 1 of your opponent's 5000 DP or lower Digimon to the bottom of the deck."),
        // 링크 효과 【자신의 턴】 (공식 영어 · 공식 일본어 · 공식 한글)
        ("BT21-041", true, "All of your opponent's Security Digimon get -3000 DP.", "[Your Turn] All of your opponent's Security Digimon get -3000 DP."),
        // 링크 효과 【소멸 시】 (공식 영어 · 공식 일본어)
        ("BT24-036", true, "1 of your opponent's Digimon gets -5000 DP for the turn.", "[On Deletion] 1 of your opponent's Digimon gets -5000 DP for the turn."),
        ("BT24-071", true, "You may play 1 level 3 [Appmon] trait Digimon card from your trash without paying the cost.", "[On Deletion] You may play 1 level 3 [Appmon] trait Digimon card from your trash without paying the cost."),
        // 키워드가 빠진 문장
        ("BT23-052", true, "This Digimon gains and until your opponent's turn ends.", "[When Linking] This Digimon gains ＜Reboot＞ (Unsuspend this Digimon during your opponent's unsuspend phase.) and ＜Blocker＞ (This Digimon can block in the blocker timing.) until your opponent's turn ends."),
        // 빠진 링크 효과 — API 진화원 칸에는 링크 조건만 있다 (공식 영어 · 공식 일본어)
        ("BT21-059", true, "(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)", "(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)\n[When Linking] ＜De-Digivolve 1＞ 1 of your opponent's Digimon. (Trash the top card. You can't trash past level 3 cards.)"),
        ("BT22-055", true, "(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)", "(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)\n＜Blocker＞ (This Digimon can block in the blocker timing.)"),
        ("BT22-058", true, "(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)", "(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)\n[When Linking] ＜De-Digivolve 1＞ 1 of your opponent's Digimon. (Trash the top card. You can't trash past level 3 cards.)"),
        ("BT22-075", true, "(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)", "(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)\n＜Scapegoat＞ (When this Digimon would be deleted other than by your effects, by deleting 1 of your other Digimon, prevent that deletion.)"),
        ("BT24-053", true, "(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)", "(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)\n＜Blocker＞ (This Digimon can block in the blocker timing.)"),
        ("BT24-057", true, "(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)", "(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)\n[On Deletion] ＜De-Digivolve 1＞ 1 of your opponent's Digimon. (Trash the top card. You can't trash past level 3 cards.)"),
        ("BT24-067", true, "(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)", "(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)\n＜Retaliation＞ (When only this Digimon is deleted in battle, delete the Digimon it battled.)"),
        ("BT26-051", true, "(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)", "(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)\n[When Linking] ＜De-Digivolve 2＞ 1 of your opponent's Digimon. (Trash up to 2 cards from the top. You can't trash past level 3 cards.)"),
        ("ST22-09", true, "(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)", "(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)\n＜Jamming＞ (This Digimon can't be deleted in battles against Security Digimon.)"),
        ("ST22-11", true, "(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)", "(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)\n＜Blocker＞ (This Digimon can block in the blocker timing.)"),
        // 빠진 링크 키워드
        ("BT25-100", true, "＜Piercing＞", "＜Collision＞ (During this Digimon's attack, all of your opponent's Digimon gain ＜Blocker＞, and must block if possible.)\n＜Piercing＞"),
        ("BT26-010", true, "＜Piercing＞", "＜Progress＞ (While attacking, your opponent's effects don't affect this Digimon.)\n＜Piercing＞"),
        ("BT25-101", true, "＜Security A. +1＞", "＜Security A. +1＞\n＜Reboot＞ (Unsuspend this Digimon during your opponent's unsuspend phase.)"),        // ---- 효과 시점 · [턴에 1회]
        // BT19-048 [턴에 1회] 없음 (공식 영어 · 공식 한글 · 공식 일본어)
        ("BT19-048", false, "[All Turns] [Once Per Turn] When any of your other Digimon with the [Royal Base] trait would leave the battle area by effects, by placing this Digimon as the face-up bottom security card, they don't leave.", "[All Turns] When any of your other Digimon with the [Royal Base] trait would leave the battle area by effects, by placing this Digimon as the face-up bottom security card, they don't leave."),
        // EX6-023 ~ EX6-026 메인 효과 [턴에 1회] 없음 (진화원 효과에만 — 공식 영어 · 공식 한글 · 공식 일본어)
        ("EX6-023", false, "[On Play] [When Attacking] [Once Per Turn] 1 Digimon may gain ＜Security A. -1＞ (This Digimon checks 1 additional security card.) until the end of your opponent's turn. Then, if DigiXrosing, delete 1 of your opponent's Digimon with 6000 DP or less.", "[On Play] [When Attacking] 1 Digimon may gain ＜Security A. -1＞ (This Digimon checks 1 additional security card.) until the end of your opponent's turn. Then, if DigiXrosing, delete 1 of your opponent's Digimon with 6000 DP or less."),
        ("EX6-024", false, "[On Play] [When Attacking] [Once Per Turn] 1 Digimon may gain ＜Security A. -1＞ (This Digimon checks 1 additional security card.) until the end of your opponent's turn. Then, if DigiXrosing, 1 of your opponent's Digimon or Tamers can't suspend until the end of their turn.", "[On Play] [When Attacking] 1 Digimon may gain ＜Security A. -1＞ (This Digimon checks 1 additional security card.) until the end of your opponent's turn. Then, if DigiXrosing, 1 of your opponent's Digimon or Tamers can't suspend until the end of their turn."),
        ("EX6-025", false, "[On Play] [When Attacking] [Once Per Turn] 1 Digimon may gain ＜Security A. -1＞ (This Digimon checks 1 additional security card.) until the end of your opponent's turn. Then, if DigiXrosing, reveal the top 4 cards of your deck. Add 1 of each [Gokuumon], [Sagomon], [Cho-Hakkaimon] and [Shakamon] among them to the hand. Return the rest to the bottom of the deck.", "[On Play] [When Attacking] 1 Digimon may gain ＜Security A. -1＞ (This Digimon checks 1 additional security card.) until the end of your opponent's turn. Then, if DigiXrosing, reveal the top 4 cards of your deck. Add 1 of each [Gokuumon], [Sagomon], [Cho-Hakkaimon] and [Shakamon] among them to the hand. Return the rest to the bottom of the deck."),
        ("EX6-026", false, "[On Play] [When Attacking] [Once Per Turn] 1 Digimon may gain ＜Security A. -1＞ (This Digimon checks 1 additional security card.) until the end of your opponent's turn. Then, if DigiXrosing, this Digimon gets +3000 DP and ＜Blocker＞ (This Digimon can block in the blocker timing.) until the end of your opponent's turn.", "[On Play] [When Attacking] 1 Digimon may gain ＜Security A. -1＞ (This Digimon checks 1 additional security card.) until the end of your opponent's turn. Then, if DigiXrosing, this Digimon gets +3000 DP and ＜Blocker＞ (This Digimon can block in the blocker timing.) until the end of your opponent's turn."),
        // [턴에 1회]가 빠짐 (공식 영어 · 공식 한글 · 공식 일본어)
        ("EX7-010", false, "[When Digivolving] [When Attacking] You may trash any 1 Option card from 1 Digimon's digivolution cards.", "[When Digivolving] [When Attacking] [Once Per Turn] You may trash any 1 Option card from 1 Digimon's digivolution cards."),
        ("EX8-059", true, "[When Attacking] ＜Draw 1＞ (Draw 1 card from your deck.) and trash 1 card in your hand.", "[When Attacking] [Once Per Turn] ＜Draw 1＞ (Draw 1 card from your deck.) and trash 1 card in your hand."),
        // (공식 영어 · 공식 일본어)
        ("BT26-009", true, "[When Attacking] ＜Draw 1＞ (Draw 1 card from your deck.) Then, if your hand has 6 or more cards, return 1 card in your hand to the bottom of the deck.", "[When Attacking] [Once Per Turn] ＜Draw 1＞ (Draw 1 card from your deck.) Then, if your hand has 6 or more cards, return 1 card in your hand to the bottom of the deck."),
        // 【이동 시】 (API [On Play] — 공식 영어 · 공식 일본어)
        ("EX11-029", false, "[On Play] [When Digivolving] You may link 1 [Maquinamon] from your hand or this Digimon's digivolution cards to 1 of your Digimon without paying the cost.", "[When Moving] [When Digivolving] You may link 1 [Maquinamon] from your hand or this Digimon's digivolution cards to 1 of your Digimon without paying the cost."),
        // 【자신의 턴】 (API [All Turns] — 공식 영어 · 공식 일본어)
        ("P-244", false, "[All Turns] When effects place [Vemmon] as any of your Digimon's digivolution cards, ＜Delay＞ (By trashing this card after the placing turn, activate the effect below.)", "[Your Turn] When effects place [Vemmon] as any of your Digimon's digivolution cards, ＜Delay＞ (By trashing this card after the placing turn, activate the effect below.)"),
        ("ST23-13", false, "[All Turns] When effects trash cards from under this Tamer, by suspending this Tamer, 1 of your [Glowing Dawn] trait Digimon gets +3000 DP until your opponent's turn ends.", "[Your Turn] When effects trash cards from under this Tamer, by suspending this Tamer, 1 of your [Glowing Dawn] trait Digimon gets +3000 DP until your opponent's turn ends."),
        // 공식 영어 · 공식 일본어 · 공식 한글: {시큐리티}【자신의 턴】 (API [All Turns])
        ("BT21-095", false, "[Security] [All Turns] All of your [WG] trait Digimon gain ＜Vortex＞ (At the end of your turn, this Digimon may attack an opponent's Digimon. With this effect, it can attack the turn it was played.).", "[Security] [Your Turn] All of your [WG] trait Digimon gain ＜Vortex＞ (At the end of your turn, this Digimon may attack an opponent's Digimon. With this effect, it can attack the turn it was played.)."),
        // 진화원 효과가 다른 효과 (API ≪시큐리티 어택 +1≫ — 공식 영어 · 공식 일본어는 [Your Turn] 어택 대상 변경 불가)
        ("BT25-026", true, "＜Security A. +1＞ (This Digimon checks 1 additional security card.)", "[Your Turn] This Digimon's attack target can't change."),
        // 진화원 칸에 메인 효과를 "Card Effect(s)"로 한 번 더 적은 카드 — 공식 영어 · 공식 일본어 · 공식 한글에는 진화원 효과가 없다
        ("BT6-084", true, "Card Effect(s) [All Turns] All of your Digimon with [Huckmon] in their name or [Royal Knight] trait get +2000 DP.\r\n[On Play] Gain 1 memory.", ""),
        ("BT7-083", true, "Card Effect(s) [On Play] You may place 1 [Sistermon Noir] from your hand or trash at the bottom of this Digimon's digivolution cards to delete 1 of your opponent's Digimon with a play cost of 5 or less.\r\n[On Deletion] Return 1 card with [Jesmon], [Huckmon] or [Sistermon] in its name other than [Sistermon Noir (Awakened)] from your trash to your hand.", ""),
        ("ST12-13", true, "Card Effect(s) [On Play] Reveal the top 3 cards of your deck. Add 1 card with [Huckmon] in its name or [Royal Knight] trait among them to your hand. Trash the rest.\r\n[All Turns] All of your Digimon with [Huckmon] in their names or [Royal Knight] trait gain ＜Reboot＞ (Unsuspend this Digimon during your opponent's unsuspend phase.).", ""),
        ("BT23-077", true, "Card Effect(s) ＜Blocker＞ (This Digimon can block in the blocker timing.)\r\n[On Play] Delete 1 of your opponent's Digimon with a play cost of 4 or less.\r\n[All Turns] When this Digimon suspends, ＜De-Digivolve 1＞ 1 of your opponent's Digimon. (Trash the top card. You can't trash past level 3 cards.)", ""),
        // 공식 영어 · 공식 일본어: ≪연계≫와 ≪관통≫ (API ≪충돌≫)
        ("EX13-064", false, "[Your Turn] All of your Digimon with [Knightmon] in their texts gain ＜Collision＞ (During this Digimon's attack, all of your opponent's Digimon gain ＜Blocker＞, and must block if possible.) and ＜Piercing＞ (When this Digimon deletes your opponent's Digimon in battle while attacking, it checks security before the attack ends.)", "[Your Turn] All of your Digimon with [Knightmon] in their texts gain ＜Alliance＞ (When this Digimon attacks, by suspending 1 of your other Digimon, add the suspended Digimon's DP to this Digimon and it gains ＜Security A. +1＞ for the attack.) and ＜Piercing＞ (When this Digimon deletes your opponent's Digimon in battle while attacking, it checks security before the attack ends.)"),
        // 공식 영어 · 공식 일본어: 메인 효과에 ≪시큐리티 어택 +1≫이 없다 (API에만 있는 줄)
        ("EX12-016", false, "＜Security A. +1＞ (This Digimon checks 1 additional security card.)\r\n", ""),
        // ---- LM-025 ACE 진화원 효과 (API 진화원 칸에는 ≪오버플로우≫만) — 공식 영어 · 공식 일본어 【어택 시】[턴에 1회]. 공식 한글 【메인】은 한국 사이트 오류 (KoreanCardData.TimingFix82)
        ("LM-025", true, Overflow3, Overflow3 + "\n[When Attacking] [Once Per Turn] ＜De-Digivolve 1＞ 1 of your opponent's Digimon. (Trash the top card. You can't trash past level 3 cards.)"),
    };

    private const string Overflow3 = "Ace Overflow ＜-3＞ (As this card moves from the field or under a card to an area other than those, lose 3 memory.)";

    /// <summary>수치 · 색: (카드 번호, 칸, API 값, 공식 값) — 공식 영어 = 공식 일본어 (한글 데이터 없음)</summary>
    internal static readonly (string Id, string Field, string Api, string Official)[] ValueFixes =
    {
        ("EX11-013", "DP", "2000", "1000"),
        ("EX11-033", "DP", "5000", "7000"),
        ("EX12-048", "등장 코스트", "5", "13"),
        ("EX12-048", "DP", "12000", "13000"),   // 색은 공식 3색 (옐로 · 레드 · 블루) — 카드 데이터가 2색까지라 그대로
        ("ST24-08", "DP", "1000", "2000"),
        ("BT26-091", "등장 코스트", "4", "3"),
        ("BT26-096", "등장 코스트", "3", "4"),
        ("EX13-033", "두 번째 색", "", "Red"),
    };

    /// <summary>〈룰〉 특징 (공식 영어 "(Rule) Trait: Has [X]" · 공식 일본어 〈ルール〉特徴 · 공식 한글 〈룰〉 특징) — API 특징에 없는 것</summary>
    internal static readonly IReadOnlyDictionary<string, string[]> RuleTraits = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["AD1-013"] = new[] { "Xros Heart" }, ["BT16-035"] = new[] { "Angel" }, ["BT16-036"] = new[] { "Boss", "D-Brigade" }, ["BT16-042"] = new[] { "Insectoid" },
        ["BT16-063"] = new[] { "Angel" }, ["BT16-102"] = new[] { "Free" }, ["BT17-025"] = new[] { "Dark Animal" }, ["BT17-077"] = new[] { "Free" },
        ["BT18-020"] = new[] { "Aquatic" }, ["BT18-022"] = new[] { "Ice-Snow" }, ["BT18-023"] = new[] { "Aquatic" }, ["BT18-025"] = new[] { "Ice-Snow" },
        ["BT18-026"] = new[] { "Ice-Snow" }, ["BT18-028"] = new[] { "Ice-Snow" }, ["BT18-038"] = new[] { "Angel" }, ["BT18-046"] = new[] { "Insectoid" },
        ["BT18-047"] = new[] { "Vegetation" }, ["BT18-052"] = new[] { "Insectoid" }, ["BT18-056"] = new[] { "Insectoid" }, ["BT18-071"] = new[] { "Three Great Angels" },
        ["BT18-072"] = new[] { "Insectoid" }, ["BT18-073"] = new[] { "Composite" }, ["BT18-102"] = new[] { "Hybrid" }, ["BT19-017"] = new[] { "Aquatic" },
        ["BT19-018"] = new[] { "Aquatic" }, ["BT19-019"] = new[] { "Aquatic" }, ["BT19-021"] = new[] { "Aquatic" }, ["BT19-024"] = new[] { "Aquatic" },
        ["BT19-027"] = new[] { "Aquatic" }, ["BT19-028"] = new[] { "Aquatic" }, ["BT19-048"] = new[] { "Insectoid" }, ["BT19-052"] = new[] { "Insectoid" },
        ["BT19-053"] = new[] { "Insectoid" }, ["BT19-065"] = new[] { "Composite" }, ["BT19-068"] = new[] { "Composite" }, ["BT20-028"] = new[] { "Aquatic" },
        ["BT20-062"] = new[] { "Ghost" }, ["BT20-073"] = new[] { "Ghost" }, ["BT20-075"] = new[] { "Dark Dragon" }, ["BT20-078"] = new[] { "Ghost" },
        ["BT20-101"] = new[] { "Bird Dragon" }, ["BT21-024"] = new[] { "Dragonkin" }, ["BT21-031"] = new[] { "Aquatic" }, ["BT21-039"] = new[] { "Avian" },
        ["BT21-051"] = new[] { "Vegetation" }, ["BT21-055"] = new[] { "Mineral" }, ["BT22-018"] = new[] { "Aquatic" }, ["BT22-021"] = new[] { "Aquatic" },
        ["BT22-024"] = new[] { "Aquatic" }, ["BT22-027"] = new[] { "Aquatic" }, ["BT22-062"] = new[] { "Dinosaur" }, ["BT23-032"] = new[] { "Angel" },
        ["BT23-042"] = new[] { "Insectoid" }, ["BT23-043"] = new[] { "Insectoid" }, ["BT23-045"] = new[] { "Insectoid" }, ["BT24-014"] = new[] { "Dragonkin" },
        ["BT24-019"] = new[] { "Aquatic" }, ["BT24-025"] = new[] { "Aquatic" }, ["BT24-026"] = new[] { "Demon" }, ["BT24-027"] = new[] { "Aquatic" },
        ["BT24-030"] = new[] { "Aquatic" }, ["BT24-059"] = new[] { "Aquatic" }, ["BT24-066"] = new[] { "Evil" }, ["BT24-073"] = new[] { "Fallen Angel" },
        ["BT24-074"] = new[] { "Aquatic" }, ["BT24-076"] = new[] { "Dark Dragon" }, ["BT24-081"] = new[] { "Demon" }, ["BT24-086"] = new[] { "DigiPolice" },
        ["BT25-019"] = new[] { "Dinosaur" }, ["BT25-025"] = new[] { "Cyborg" }, ["BT25-038"] = new[] { "Angel" }, ["BT25-042"] = new[] { "Angel" },
        ["BT25-053"] = new[] { "Vegetation" }, ["BT25-055"] = new[] { "Vegetation" }, ["BT25-059"] = new[] { "Vegetation" }, ["BT25-069"] = new[] { "Cyborg" },
        ["BT25-103"] = new[] { "Olympos XII" }, ["BT26-018"] = new[] { "Aquatic" }, ["BT26-029"] = new[] { "Angel" }, ["BT26-032"] = new[] { "Vegetation" },
        ["BT26-046"] = new[] { "Avian" }, ["BT26-056"] = new[] { "Dark Animal" }, ["BT26-073"] = new[] { "Wizard" }, ["BT26-082"] = new[] { "Birdkin" },
        ["EX6-013"] = new[] { "Aquatic" }, ["EX6-015"] = new[] { "Aquatic" }, ["EX6-021"] = new[] { "Angel" }, ["EX6-030"] = new[] { "Angel" },
        ["EX7-016"] = new[] { "Ice-Snow" }, ["EX7-017"] = new[] { "Ice-Snow" }, ["EX7-019"] = new[] { "Ice-Snow" }, ["EX7-020"] = new[] { "Ice-Snow" },
        ["EX7-021"] = new[] { "Ice-Snow" }, ["EX7-023"] = new[] { "Ice-Snow" }, ["EX7-033"] = new[] { "Dinosaur" }, ["EX7-035"] = new[] { "Dinosaur" },
        ["EX7-036"] = new[] { "Bird Dragon" }, ["EX7-039"] = new[] { "Machine Dragon" }, ["EX7-057"] = new[] { "Dark Dragon" }, ["EX8-019"] = new[] { "Ice-Snow" },
        ["EX8-026"] = new[] { "Aquatic" }, ["EX8-029"] = new[] { "Aquatic" }, ["EX8-043"] = new[] { "Dinosaur" }, ["EX8-047"] = new[] { "Mineral" },
        ["EX9-052"] = new[] { "Cyborg" }, ["EX10-009"] = new[] { "Fallen Angel" }, ["EX10-025"] = new[] { "Mineral" }, ["EX10-036"] = new[] { "Mineral" },
        ["EX10-049"] = new[] { "Fallen Angel" }, ["EX11-013"] = new[] { "Aquatic" }, ["EX11-014"] = new[] { "Ice-Snow" }, ["EX11-018"] = new[] { "Aquatic" },
        ["EX11-030"] = new[] { "Insectoid" }, ["EX11-031"] = new[] { "Insectoid" }, ["EX11-034"] = new[] { "Insectoid" }, ["EX11-035"] = new[] { "Bird Dragon" },
        ["EX11-038"] = new[] { "Mineral" }, ["EX11-050"] = new[] { "Dark Dragon" }, ["EX12-026"] = new[] { "Aquatic" }, ["EX12-031"] = new[] { "Aquatic" },
        ["EX12-036"] = new[] { "Aquatic" }, ["EX12-076"] = new[] { "Hybrid" }, ["EX13-025"] = new[] { "Witchelny" }, ["EX13-038"] = new[] { "Beast" },
        ["EX13-054"] = new[] { "Mutant" }, ["EX13-056"] = new[] { "Machine" }, ["EX13-066"] = new[] { "Data" }, ["EX13-076"] = new[] { "Free" },
        ["LM-040"] = new[] { "Sea Beast" }, ["LM-042"] = new[] { "Three Great Angels" }, ["LM-044"] = new[] { "Fallen Angel" }, ["P-164"] = new[] { "Aquatic" },
        ["P-170"] = new[] { "Three Musketeers" }, ["P-209"] = new[] { "Demon" }, ["P-213"] = new[] { "Dragonkin" }, ["P-215"] = new[] { "Mineral" },
        ["P-241"] = new[] { "Leviathan" }, ["ST18-06"] = new[] { "Vegetation" }, ["ST18-09"] = new[] { "Vegetation" }, ["ST18-12"] = new[] { "Bird Dragon" },    };

    /// <summary>특징 오류: (카드 번호, 뺄 특징, 넣을 특징) — 공식 영어 · 공식 일본어 (· 공식 한글)가 같고 API만 다른 것</summary>
    internal static readonly (string Id, string[] Remove, string[] Add)[] TraitFixes =
    {
        // 속성
        ("AD1-010", new[] { "Data" }, new[] { "Vaccine" }), ("BT12-045", new[] { "Vaccine" }, new[] { "Data" }), ("BT12-052", new[] { "Data" }, new[] { "Vaccine" }),
        ("BT14-054", new[] { "Vaccine" }, new[] { "Data" }), ("BT18-016", new[] { "Virus" }, new[] { "Data" }), ("BT22-011", new[] { "Data" }, new[] { "Virus" }),
        ("BT23-014", new[] { "Data" }, new[] { "Virus" }), ("BT23-058", new[] { "Data" }, new[] { "Vaccine" }), ("BT23-061", new[] { "Virus" }, new[] { "Data" }),
        ("BT25-044", new[] { "Vaccine" }, new[] { "Virus" }), ("BT6-054", new[] { "Virus" }, new[] { "Data" }), ("BT6-057", new[] { "Vaccine" }, new[] { "Virus" }),
        ("BT9-020", new[] { "Vaccine" }, new[] { "Data" }), ("BT9-072", new[] { "Data" }, new[] { "Vaccine" }), ("EX11-010", new[] { "Data" }, new[] { "Vaccine" }),
        ("EX12-034", new[] { "Data" }, new[] { "Vaccine" }), ("EX12-057", new[] { "Data" }, new[] { "Vaccine" }), ("EX13-060", new[] { "Data" }, new[] { "Vaccine" }),
        // 빠진 속성
        ("AD1-006", Array.Empty<string>(), new[] { "Data" }), ("AD1-013", Array.Empty<string>(), new[] { "Virus" }), ("BT24-020", Array.Empty<string>(), new[] { "Vaccine" }),
        ("BT24-022", Array.Empty<string>(), new[] { "Vaccine" }), ("BT24-072", Array.Empty<string>(), new[] { "Virus" }), ("BT24-075", Array.Empty<string>(), new[] { "Data" }),
        // 유형
        ("BT20-010", new[] { "Beast Dragon" }, new[] { "Beast" }), ("BT22-023", new[] { "Mythical Dragon" }, new[] { "Holy Dragon" }),
        ("EX13-051", new[] { "Mine" }, new[] { "Machine" }), ("EX6-010", new[] { "Weapon" }, new[] { "Holy Sword" }),
        ("BT25-050", Array.Empty<string>(), new[] { "Ancient Bird" }), ("EX13-001", Array.Empty<string>(), new[] { "Lesser" }), ("EX13-004", Array.Empty<string>(), new[] { "Flame" }),
        // 그룹 특징
        ("BT25-086", Array.Empty<string>(), new[] { "ADAMAS" }), ("BT26-021", Array.Empty<string>(), new[] { "Titan" }), ("BT26-060", Array.Empty<string>(), new[] { "Titan" }),
        ("BT26-104", Array.Empty<string>(), new[] { "SW", "TB", "TS" }), ("EX10-069", Array.Empty<string>(), new[] { "LIBERATOR" }), ("EX11-059", Array.Empty<string>(), new[] { "NSo" }),
        ("EX12-073", Array.Empty<string>(), new[] { "VB" }), ("P-234", Array.Empty<string>(), new[] { "Leviathan" }),
        // 공식에 없는 특징 (공식 영어 · 공식 일본어 · 공식 한글 모두 없다)
        ("BT19-061", new[] { "Twilight" }, Array.Empty<string>()), ("BT24-081", new[] { "Olympos XII" }, Array.Empty<string>()),
        ("BT17-063", new[] { "3" }, Array.Empty<string>()), ("BT17-066", new[] { "3" }, Array.Empty<string>()), ("BT17-071", new[] { "3" }, Array.Empty<string>()), ("BT17-072", new[] { "3" }, Array.Empty<string>()),
    };

    /// <summary>수치 · 색 · 특징 고침 (문구는 OfficialCardData81.Apply). 고친 칸을 changes에 더한다</summary>
    internal static void ApplyValuesAndTraits(CardData card, List<OfficialCardData81.Change> changes)
    {
        foreach (var f in ValueFixes)
        {
            if (!string.Equals(f.Id, card.CardId, StringComparison.OrdinalIgnoreCase)) continue;
            switch (f.Field)
            {
                case "등장 코스트" when card.PlayCost.ToString() == f.Api:
                    card.PlayCost = int.Parse(f.Official); changes.Add(new(card.CardId, f.Field, f.Api, f.Official)); break;
                case "DP" when card.DP.ToString() == f.Api:
                    card.DP = int.Parse(f.Official); changes.Add(new(card.CardId, f.Field, f.Api, f.Official)); break;
                case "두 번째 색" when card.SecondaryColor == null && Enum.TryParse<CardColor>(f.Official, out var col) && col != card.Color:
                    card.SecondaryColor = col; changes.Add(new(card.CardId, f.Field, f.Api, f.Official)); break;
            }
        }

        var traits = card.Traits.ToList();
        bool Has(string t) => traits.Any(x => x.Equals(t, StringComparison.OrdinalIgnoreCase) || x.Split('/').Any(w => w.Trim().Equals(t, StringComparison.OrdinalIgnoreCase)));
        int edits = 0;
        foreach (var f in TraitFixes)
        {
            if (!string.Equals(f.Id, card.CardId, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var r in f.Remove)
                edits += traits.RemoveAll(x => x.Equals(r, StringComparison.OrdinalIgnoreCase));
            foreach (var a in f.Add)
                if (!Has(a)) { traits.Add(a); edits++; }
        }
        if (RuleTraits.TryGetValue(card.CardId, out var rule))
            foreach (var a in rule)
                if (!Has(a)) { traits.Add(a); edits++; }
        // 형태 (공식 영어 카드 리스트의 Form — "Stnd./Appmon"처럼 여럿이면 각각). 영어 API는 형태를 특징 맨 앞에 둔다
        if (EnglishCardRules.Get(card.CardId)?.Form is { Length: > 0 } form)
        {
            int at = 0;
            foreach (var a in form.Split('/').Select(x => x.Trim()).Where(x => x.Length > 0 && x != "-"))
                if (!Has(a)) { traits.Insert(at++, a); edits++; }
        }
        if (edits == 0) return;
        changes.Add(new(card.CardId, "특징", string.Join("/", card.Traits), string.Join("/", traits)));
        card.Traits = traits;   // 새 목록 (API 값은 CardCatalogService가 옛 목록으로 저장한다)
    }
}
