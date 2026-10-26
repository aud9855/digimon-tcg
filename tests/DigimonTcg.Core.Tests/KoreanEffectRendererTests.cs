using System.IO;
using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>효과 확장 6단계 — 영어 효과 문장을 공식 한글판 표기로 만드는지 (공식 한글이 없는 카드의 화면 표시)</summary>
[Collection("KoreanCardData")]   // KoreanCardData.Reload(전역)을 쓰는 테스트끼리 차례로
public class KoreanEffectRendererTests
{
    [Theory]
    // 공식 한글판 문구와 같은 문장이 나와야 하는 것들 (BT1-039 · BT1-092 · BT1-036 · BT1-023 · BT1-012 · BT1-085)
    [InlineData("[When Attacking] [Twice Per Turn] You may trash 3 cards in your hand to unsuspend this Digimon.",
                "【어택 시】 [턴에 2회] 자신의 패를 3장 파기하는 것으로, 이 디지몬을 액티브로 한다.")]
    [InlineData("[Main] ＜Draw 2＞ (Draw 2 cards from your deck.) Then 1 of your Digimon gets +2000 DP for the turn.",
                "【메인】 《2 드로우》 (자신의 덱에서 카드를 2장 드로우한다). 그 후, 이 턴 동안, 자신의 디지몬 1마리의 DP를 +2000 한다.")]
    [InlineData("[On Play] Unsuspend 1 of your Digimon.", "【등장 시】 자신의 디지몬 1마리를 액티브로 한다.")]
    [InlineData("[On Play] Delete 1 of your opponent's Digimon with ＜Blocker＞.", "【등장 시】 《블로커》를 가진 상대의 디지몬 1마리를 소멸시킨다.")]
    [InlineData("[Your Turn] When this Digimon is blocked, it gets +2000 DP.", "【자신의 턴】 이 디지몬이 블록당했을 때, 이 디지몬의 DP를 +2000 한다.")]
    [InlineData("[Start of Your Turn] If you have 2 or less memory, set it to 3.", "【자신의 턴 개시 시】 메모리가 2 이하일 때, 메모리를 3으로 한다.")]
    // 대상 꾸밈말 · 기간 · "~할 수 있다" · 비용
    [InlineData("[On Play] Delete 1 of your opponent's Digimon with 4000 DP or less.", "【등장 시】 DP 4000 이하인 상대의 디지몬 1마리를 소멸시킨다.")]
    [InlineData("[Main] Return up to 2 of your opponent's level 4 or lower Digimon to their hand.", "【메인】 Lv.4 이하인 상대의 디지몬 2마리까지를 패로 되돌린다.")]
    [InlineData("[Main] Until the end of your opponent's next turn, 1 of your opponent's Digimon can't attack or block.",
                "【메인】 다음 상대의 턴 종료 시까지, 상대의 디지몬 1마리는 어택과 블록을 할 수 없다.")]
    [InlineData("[On Deletion] You may delete 1 of your Digimon to gain 2 memory.", "【소멸 시】 자신의 디지몬 1마리를 소멸시키는 것으로, 메모리를 +2 한다.")]
    // 키워드만 있는 절 (공식 설명문)
    [InlineData("＜Jamming＞ (This Digimon can't be deleted in battles against Security Digimon.)", "《재밍》 (이 디지몬은 시큐리티 디지몬과의 배틀에서는 소멸하지 않는다)")]
    [InlineData("＜Security A. +1＞", "《시큐리티 어택 +1》 (이 디지몬이 체크하는 시큐리티의 매수 +1)")]
    public void RenderClause_ShouldFollowOfficialKoreanWording(string english, string korean)
    {
        Assert.Equal(korean, KoreanEffectRenderer.RenderClause(english));
    }

    [Fact]
    public void Particles_ShouldFollowTheFinalConsonant()
    {
        // 《관통》을 (받침 있음) · 《블로커》를 (받침 없음) · 1장과 · 《드로우》 …
        Assert.Equal("이 디지몬은 《관통》 (이 디지몬이 어택한 배틀에서 상대의 디지몬만을 소멸시켰을 때, 이 디지몬은 시큐리티를 체크한다)을 얻는다.",
            KoreanEffectRenderer.RenderClause("This Digimon gains ＜Piercing＞."));
        Assert.Contains("1장과 ", KoreanEffectRenderer.RenderClause(
            "[On Play] Reveal the top 3 cards of your deck. Add 1 level 5 Digimon card and 1 level 6 Digimon card among them to your hand. Trash the rest."));
    }

    [Fact]
    public void ItWithoutAnEarlierTarget_ShouldMeanThisDigimon()
    {
        Assert.Contains("이 디지몬의 DP를", KoreanEffectRenderer.RenderClause("[Your Turn] While this Digimon has ＜Reboot＞, it gets +1000 DP."));
        Assert.Contains("그 디지몬은 다음 액티브 페이즈에", KoreanEffectRenderer.RenderClause(
            "[On Play] Suspend 1 of your opponent's Digimon. It doesn't unsuspend during your opponent's next unsuspend phase."));
    }

    [Fact]
    public void LevelNOrM_ShouldStayOneCardPickInTheEngineAndTheRenderer()
    {
        // "1 level 4 or 5 Digimon card"를 "1 level 4" 또는 "5 Digimon cards"로 나누면 안 된다
        var card = new CardData
        {
            CardId = "KO-LV45", Name = "KO-LV45", Type = CardType.Digimon, Color = CardColor.Green, Level = 3, DP = 2000,
            EffectText = "[On Deletion] Reveal the top 3 cards of your deck. Add 1 level 4 or 5 Digimon card among them to your hand. Place the remaining cards at the bottom of your deck in any order.",
        };
        Assert.All(KeywordDictionary.Analyze(card), c => Assert.Equal(ClauseSupport.Supported, c.Support));
        Assert.Contains("Lv.4 또는 Lv.5인 디지몬 카드 1장", KoreanEffectRenderer.Render(card, inherited: false));
    }

    [Fact]
    public void UntranslatedClause_ShouldShowTheEnglishWithAMark()
    {
        var card = new CardData
        {
            CardId = "KO-MIXED", Name = "KO-MIXED", Type = CardType.Digimon, Color = CardColor.Red, Level = 5, DP = 7000,
            EffectText = "[On Play] Gain 1 memory.\n[Your Turn] Your turn end condition is the opponent having 3 or more memory.",
        };
        var lines = KoreanEffectRenderer.Render(card, inherited: false)!.Split('\n');
        Assert.Equal("【등장 시】 메모리를 +1 한다.", lines[0]);
        Assert.StartsWith(KoreanEffectRenderer.UntranslatedMark, lines[1]);
        Assert.Contains("turn end condition", lines[1]);
        Assert.Null(KoreanEffectRenderer.RenderClause("[Your Turn] Your turn end condition is the opponent having 3 or more memory."));
    }

    [Fact]
    public void DisplayText_ShouldUseOfficialThenBuiltInThenGeneratedThenEnglish()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dtcg_kogen_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            KoreanCardData.Reload(Path.Combine(dir, "missing.json"));   // 공식 한글 데이터가 없는 PC
            var api = new CardData
            {
                CardId = "KO-DISPLAY", Name = "KO-DISPLAY", Type = CardType.Tamer, Color = CardColor.Red,
                EffectText = "[Start of Your Turn] If you have 2 or less memory, set it to 3.",
                InheritedEffectText = "[Security] Play this card without paying the cost.",
            };
            Assert.Equal("【자신의 턴 개시 시】 메모리가 2 이하일 때, 메모리를 3으로 한다.\n【시큐리티】 이 카드를 코스트를 지불하지 않고 등장시킨다.", api.DisplayEffectText);
            Assert.Equal(string.Empty, api.DisplayInheritedEffectText);   // 테이머 · 옵션은 [시큐리티]를 메인 쪽에

            // 내장 카드는 이미 한글 — 생성으로 덮지 않는다
            var builtIn = StarterDecks.GetAllCatalogCards().First(c => c.CardId == "ST1-16");
            Assert.Equal(builtIn.EffectText, builtIn.DisplayEffectText);

            // 번역할 수 없는 문장뿐이면 영어 원문
            var english = new CardData { CardId = "KO-ENGLISH", Name = "KO-ENGLISH", Type = CardType.Digimon, EffectText = "[Your Turn] Your turn end condition is the opponent having 3 or more memory." };
            Assert.Equal(english.EffectText, english.DisplayEffectText);
        }
        finally
        {
            KoreanCardData.Reload();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // ---- 번역 점검 (2026-10-22): 7단계 키워드 · 뜻 오류 · 이름 바꾸기 ----

    [Fact]
    public void Phase7Keywords_ShouldRenderWithOfficialNotation()
    {
        Assert.StartsWith("《링크 +1》", KoreanEffectRenderer.RenderClause("＜Link +1＞ (Add 1 to this Digimon's maximum links.)"));
        Assert.StartsWith("《프래그먼트《3》》", KoreanEffectRenderer.RenderClause("＜Fragment (3)＞ (When this Digimon would be deleted, by trashing any 3 of its digivolution cards, it isn't deleted.)"));
        Assert.StartsWith("《디코드《블루 Lv.4》》", KoreanEffectRenderer.RenderClause("＜Decode (Blue Lv.4)＞ (When this Digimon would leave the battle area other than in battle, you may play 1 Blue Level 4 Digimon card from its digivolution cards without paying the cost.)"));
        Assert.StartsWith("《파티션《레드 Lv.4&옐로 Lv.4》》", KoreanEffectRenderer.RenderClause("＜Partition (Red Lv.4 & Yellow Lv.4)＞"));
        // API 원문의 짝 없는 괄호 "…cost.))"
        Assert.NotNull(KoreanEffectRenderer.RenderClause("＜Fragment (2)＞ (When this Digimon would be deleted, by trashing any 2 of its digivolution cards, it isn't deleted.))"));
    }

    [Fact]
    public void MeaningErrors_ShouldBeFixed()
    {
        // "for each ~ with ＜Security A.＞"를 키워드 목록으로 읽지 않는다 (BT12-044)
        var each = KoreanEffectRenderer.RenderClause("[Your Turn] This Digimon gains ＜Security A. +1＞ for each of your opponent's Digimon with ＜Security A.＞");
        Assert.NotNull(each);
        Assert.Contains("가진 상대의 디지몬 1마리마다", each);
        // 가운데의 may도 "~할 수 있다" (BT14-007)
        var may = KoreanEffectRenderer.RenderClause("[Start of Your Main Phase] This Digimon may digivolve into [Greymon] in your hand without paying the cost.");
        Assert.NotNull(may);
        Assert.Contains("진화시킬 수 있다", may);
    }

    [Fact]
    public void NameLocalizer_ShouldReplaceOnlyWholeEnglishCardNames()
    {
        if (KoreanCardData.Count == 0) return;   // 공식 한글 데이터가 없는 PC — 아무것도 바꾸지 않는다
        Assert.Equal("[메탈가루몬(Lv.6)] 드로우", KoreanNameLocalizer.Localize("[MetalGarurumon(Lv.6)] 드로우"));
        Assert.DoesNotContain("Snow아구몬", KoreanNameLocalizer.Localize("SnowAgumon"));   // 영문자에 붙은 이름은 바꾸지 않는다
        Assert.Equal("워가루몬", KoreanNameLocalizer.KoreanNameFor("WereGarurumon"));       // ACE 카드 이름이 아니라 가장 많이 쓰인 이름
        Assert.Equal("패스(Pass)를 선언", KoreanNameLocalizer.Localize("패스(Pass)를 선언"));
        // 공식 한글이 없는 카드도 같은 영어 이름의 공식 한글 이름으로 표시
        Assert.Equal("아구몬", KoreanCardData.DisplayName(new CardData { CardId = "TEST-NOOFF-1", Name = "Agumon", Type = CardType.Digimon }));
    }

    [Fact]
    public void Phase7Sentences_ShouldRender()
    {
        string? R(string en) => KoreanEffectRenderer.RenderClause(en);
        Assert.Equal("【서로의 턴】 [턴에 1회] 이 디지몬이 자신의 효과 이외로 배틀 에어리어를 벗어날 때, 이 디지몬의 링크 카드를 1장 파기하는 것으로, 그 디지몬은 배틀 에어리어를 벗어나지 않는다.",
            R("[All Turns] [Once Per Turn] When this Digimon would leave the battle area other than by your effects, by trashing 1 of its link cards, it doesn't leave."));
        Assert.Contains("액티브가 되지 않는다", R("[On Play] 1 of your opponent's Digimon or Tamers can't unsuspend until the end of their turn."));
        Assert.Contains("레스트할 수 없다", R("[On Play] 1 of your opponent's Digimon or Tamers can't suspend until the end of their turn."));
        Assert.Equal("【등장 시】 디지몬 1마리를 레스트시킬 수 있다.", R("[On Play] You may suspend 1 Digimon."));
        Assert.Contains("이 디지몬과 다른 자신의 디지몬으로, 패의 디지몬 카드로 조그레스 진화할 수 있다", R("[End of Your Turn] This Digimon and any of your other Digimon may DNA digivolve into a Digimon card in the hand."));
        Assert.Contains("이 디지몬에게 코스트를 지불하지 않고 링크할 수 있다", R("[On Play] You may link 1 level 4 or lower Digimon card from your hand to this Digimon without paying the cost."));
        Assert.Contains("이 디지몬이 링크했을 때", R("[Your Turn] When this Digimon gets linked, ＜Draw 1＞"));
        Assert.Contains("이 카드를 시큐리티 아래에 앞면으로 놓는다", R("[Main] Add your bottom security card to the hand. Then, place this card face up as the bottom security card."));
        // "이 효과로 등장한 카드의 【등장 시】 효과는 발휘하지 않는다"를 빼먹지 않는다 (BT2-038)
        Assert.Contains("【등장 시】 효과는 발휘하지 않는다", R("[When Digivolving] You may play 1 yellow Tamer card from your hand without paying the cost. [On Play] effects of Tamers played by this effect don't activate."));
    }

    [Fact]
    public void ScreenCheckSentences_ShouldRender()
    {
        string? R(string en) => KoreanEffectRenderer.RenderClause(en);
        // 앞에 오는 기간 (BT24-072)
        Assert.Contains("상대의 턴 종료 시까지, 특징으로", R("[On Play] By trashing 1 card in your hand, until your opponent's turn ends, 1 of your Digimon with the [Demon], [Shaman] or [Titan] trait gains ＜Blocker＞ and ＜Retaliation＞."));
        Assert.Contains("이거나 특징으로", R("[Your Turn] While this Digimon is [Titamon] or has the [Titan] trait, it gains ＜Security A. +1＞."));
        Assert.Equal("【어택 시】 [턴에 1회] 서로 《1 드로우》 (자신의 덱에서 카드를 1장 드로우한다).", R("[When Attacking] [Once Per Turn] Both players ＜Draw 1＞"));
        Assert.Equal("【자신의 턴】 이 디지몬의 어택의 대상은 변경되지 않는다.", R("[Your Turn] This Digimon's attack target can't change."));
        Assert.Equal("【자신의 턴 종료 시】 자신의 디지몬 1마리로 어택할 수 있다.", R("[End of Your Turn] 1 of your Digimon may attack."));
        Assert.Contains("진화원이 5장 이상인 이 디지몬으로 레스트시키지 않고 어택할 수 있다", R("[End of Your Turn] This Digimon with 5 or more digivolution cards may attack without suspending."));
        Assert.Contains("상대의 디지몬의 효과를 받지 않는다", R("[On Play] Until your opponent's turn ends, their Digimon's effects don't affect 1 of your Digimon."));
    }

    [Fact]
    public void NoOfficialCardTraits_ShouldUseTheTraitGlossary()
    {
        if (KoreanCardData.Count == 0) return;   // 공식 한글 데이터가 없는 PC
        var card = new CardData { CardId = "TEST-NOOFF-TRAIT", Name = "Testmon", Type = CardType.Digimon, Traits = new() { "Vaccine", "Shaman", "TS" } };
        Assert.Equal(new[] { "백신종", "신인형", "TS" }, KoreanCardData.DisplayTraits(card));
    }

    [Fact]
    public void ScreenCheckSentences2_ShouldRender()
    {
        string? R(string en) => KoreanEffectRenderer.RenderClause(en);
        Assert.StartsWith("《디코드》", R("＜Decode＞"));
        Assert.Contains("명칭에 「스카몬」을 포함하는 자신의 다른 디지몬 1마리를 소멸시키는 것으로, 그 디지몬은 소멸하지 않는다", R("[All Turns] When this Digimon would be deleted, by deleting 1 other Digimon with [Sukamon] in its name, prevent that deletion."));
        Assert.Contains("다음 상대의 액티브 페이즈에 액티브가 되지 않는다", R("[On Play] 1 of their Digimon or Tamers can't unsuspend in their next unsuspend phase."));
        Assert.Contains("옐로인 자신의 디지몬이 있다면 지불하는 등장 코스트를 -1 한다", R("When you would play this card from your hand, reduce its play cost by 1 if you have a yellow Digimon in play."));
        Assert.Contains("액티브 상태인 상대의 디지몬이 없는 동안", R("[All Turns] While your opponent has no unsuspended Digimon in play, this Digimon gets +1000 DP."));
        Assert.Contains("이 디지몬이 2색 이상인 동안", R("[All Turns] While this Digimon has 2 or more colors, it gets +1000 DP."));
        Assert.Contains("자신의 다른 디지몬이 등장하거나 진화했을 때, 그중 특징으로", R("[Your Turn] When your other Digimon are played or digivolve, if any of them have the [ADVENTURE] trait, 1 of your Digimon gains ＜Alliance＞ for the turn."));
        Assert.Contains("패 또는 트래시의", R("[When Attacking] This Digimon may digivolve into a Digimon card with [Leomon] in its name in the hand or trash."));
    }

    [Fact]
    public void AttackTargetSwitchSentences_ShouldRender()
    {
        string? R(string en) => KoreanEffectRenderer.RenderClause(en);
        Assert.StartsWith("【서로의 턴】 어택의 대상이 변경되었을 때,", R("[All Turns] When an attack target is switched, ＜Draw 1＞"));
        Assert.StartsWith("【자신의 턴】 이 디지몬의 어택의 대상이 변경되었을 때,", R("[Your Turn] When this Digimon's attack target is switched, it gets +3000 DP for the turn."));
        Assert.Contains("어택의 대상을 레스트 상태인 자신의 디지몬 1마리로 변경할 수 있다", R("[Opponent's Turn] When one of your opponent's Digimon attacks, you may switch the attack target to 1 of your suspended Digimon."));
    }

    [Fact]
    public void PlayCostReductionSentences_ShouldUseOfficialNotation()
    {
        string? R(string en) => KoreanEffectRenderer.RenderClause(en);
        // 비용을 내고 줄이기 (v0.16.0) — 공식: "패의 이 카드를 등장시킬 때, 자신의 「디아블로몬」 1마리를 소멸시키는 것으로, 지불하는 등장 코스트를 -12 한다."
        Assert.Equal("패의 이 카드를 등장시킬 때, 자신의 디지몬 1마리를 소멸시키는 것으로, 지불하는 등장 코스트를 -4 한다.", R("When this card would be played, by deleting 1 of your Digimon, reduce the play cost by 4."));
        Assert.Equal("패의 이 카드를 등장시킬 때, 자신의 「디아블로몬」 1마리를 소멸시키는 것으로, 지불하는 등장 코스트를 -12 한다.", R("When you would play this card from your hand, you may delete 1 of your [Diaboromon] to reduce this card's play cost by 12."));
        Assert.Contains("「사이보그형」/「Ver.1」을 가진 카드 1장을 파기하는 것으로, 지불하는 등장 코스트를 -2 한다.", R("When this card would be played, by trashing 1 [Cyborg] or [Ver.1] trait card from your hand, reduce the play cost by 2."));
        // 셈 · 조건
        Assert.Equal("패의 이 카드를 등장시킬 때, 상대의 디지몬 1마리마다 지불하는 등장 코스트를 -2 한다.", R("When this card would be played, reduce the play cost by 2 for each of your opponent's Digimon."));
        Assert.Equal("패의 이 카드를 등장시킬 때, 블루인 자신의 디지몬이 있다면 지불하는 등장 코스트를 -1 한다.", R("When this card would be played, reduce its play cost by 1 if you have a blue Digimon in play."));
    }

    [Fact]
    public void CostVariants_ShouldRender()
    {
        string? R(string en) => KoreanEffectRenderer.RenderClause(en);
        // v0.17.0에서 엔진이 새로 읽는 비용
        Assert.StartsWith("【메인】 디지몬 2마리를 레스트시키는 것으로,", R("[Main] By suspending 2 Digimon, gain 2 memory."));
        Assert.StartsWith("【메인】 옐로인 자신의 테이머 1장을 레스트시키는 것으로,", R("[Main] By suspending 1 of your yellow Tamers, gain 1 memory."));
        Assert.StartsWith("【자신의 턴】 이 디지몬을 액티브로 하는 것으로,", R("[Your Turn] By unsuspending this Digimon, gain 1 memory."));
        Assert.Equal("【등장 시】 【진화 시】 【어택 시】 [턴에 1회] 자신의 시큐리티의 위 또는 아래에서 1장 파기하는 것으로, 상대의 턴 종료 시까지, 이 디지몬은 상대의 디지몬의 효과를 받지 않는다.",
            R("[On Play] [When Digivolving] [When Attacking] [Once Per Turn] By trashing your top or bottom security card, your opponent's Digimon effects don't affect this Digimon until their turn ends."));
        Assert.StartsWith("【등장 시】 자신의 패에서 카드 1장을 이 디지몬의 진화원 가장 아래에 뒷면으로 놓는 것으로,", R("[On Play] By placing 1 card in your hand face down as this Digimon's bottom digivolution card, gain 1 memory."));
        Assert.StartsWith("【등장 시】 자신의 트래시에서 카드 1장을 이 디지몬의 진화원 가장 아래에 뒷면으로 놓는 것으로,", R("[On Play] By placing 1 card from your trash face down as this Digimon's bottom digivolution card, gain 1 memory."));
        Assert.StartsWith("【등장 시】 자신의 덱 위에서 카드 1장을 이 디지몬의 진화원 가장 아래에 뒷면으로 놓는 것으로,", R("[On Play] By placing the top card of your deck face down as this Digimon's bottom digivolution card, gain 1 memory."));
        Assert.StartsWith("【메인】 자신의 테이머 아래의 뒷면 카드를 아래에서부터 1장 파기하는 것으로,", R("[Main] By trashing the bottom face-down card from under any of your Tamers, gain 2 memory."));
        Assert.Contains("아래에서부터 1장 또는 자신의 시큐리티를 위에서부터 1장 파기하는 것으로,", R("[Main] By trashing the bottom face-down card from under any of your Tamers or your top security card, gain 2 memory."));
    }

    [Fact]
    public void DigivolutionCostReduction_ShouldUseOfficialNotation()
    {
        string? R(string en) => KoreanEffectRenderer.RenderClause(en);
        // 공식: "【자신의 턴】이 디지몬이 특징으로 「곤충형」을 가진 디지몬 카드로 진화할 때, 지불하는 진화 코스트를 -1 한다."
        Assert.Equal("【자신의 턴】 이 디지몬이 특징으로 「로얄 나이츠」를 가진 디지몬 카드로 진화할 때, 지불하는 진화 코스트를 -1 한다.",
            R("[Your Turn] When this Digimon would digivolve into a Digimon card with the [Royal Knight] trait, reduce the digivolution cost by 1."));
        Assert.Equal("【자신의 턴】 이 디지몬이 명칭에 「그레이몬」을 포함하는 카드로 진화할 때, 지불하는 진화 코스트를 -1 한다.",
            R("[Your Turn] When this Digimon would digivolve into a card with [Greymon] in its name, reduce the digivolution cost by 1."));
        Assert.Equal("【자신의 턴】 [턴에 1회] 이 디지몬이 진화할 때, 지불하는 진화 코스트를 -1 한다.",
            R("[Your Turn] [Once Per Turn] When this Digimon would digivolve, reduce the digivolution cost by 1."));
        // 비용 2개 — 공식: "이 테이머를 레스트시키고, 자신의 테이머 아래의 카드 1장을 그 디지몬의 진화원 아래에 놓는 것으로,"
        Assert.Equal("【자신의 턴】 자신의 디지몬이 《세이브》가 기술되어 있는 디지몬 카드로 진화할 때, 이 테이머를 레스트시키고, 자신의 테이머 아래의 카드 1장을 그 디지몬의 진화원 아래에 놓는 것으로, 지불하는 진화 코스트를 -1 한다.",
            R("[Your Turn] When one of your Digimon would digivolve into a Digimon card with ＜Save＞ in its text, by suspending this Tamer and placing 1 card from under one of your Tamers as the bottom digivolution card of that Digimon, reduce the digivolution cost by 1."));
        // 주어 · 셈
        Assert.Contains("패의 이 카드로 진화할 때, 그 디지몬의 뒷면의 진화원 1장마다 지불하는 진화 코스트를 -1 한다.",
            R("When any of your [Hybrid] trait Digimon would digivolve into this card, for each of their face-down digivolution cards, reduce the digivolution cost by 1."));
        // v0.31.0 공식 표기 (BT11-059): "그린/블랙인 자신의 테이머 1명마다"
        Assert.Equal("자신의 디지몬이 패의 이 카드로 진화할 때, 그린/블랙인 자신의 테이머 1명마다 지불하는 진화 코스트를 -1 한다.",
            R("When one of your Digimon would digivolve into this card, reduce the digivolution cost by 1 for each green or black Tamer you have in play."));
        // 트래시의 장수 조건 (공식: "자신의 트래시에 명칭에 「묘티스몬」을 포함하는 카드가 5장 이상 있을 때,") · 최다 시큐리티 비용
        Assert.Equal("【등장 시】 자신의 트래시에 명칭에 「묘티스몬」을 포함하는 카드가 5장 이상 있을 때, 액티브 상태인 상대의 디지몬 1마리를 소멸시킨다.",
            R("[On Play] If you have 5 or more cards with [Myotismon] in their names in your trash, delete 1 of your opponent's unsuspended Digimon."));
        Assert.Contains("자신의 트래시에 「루체몬」/", R("When this card would be played, if you have 4 or more cards with [Lucemon] or [Witchelny] in its text in your trash, reduce the play cost by 5."));
        Assert.StartsWith("【진화 시】 시큐리티가 가장 많은 플레이어 1명의 시큐리티를 위에서부터 1장 파기하는 것으로,",
            R("[When Digivolving] By trashing the top security card of 1 player with the most security cards, 1 of your opponent's Digimon or Tamers can't suspend until their turn ends."));
    }

    [Theory]
    // v0.19.0 시점 묶음 — 공식: "자신의 효과로 자신의 패를 파기했을 때," · "자신의 덱이 파기되었을 때," · "자신이 사용 코스트 2 이상의 옵션 카드를 사용했을 때," · "자신의 디지몬이 효과로 레스트했을 때,"
    [InlineData("When one of your effects trashes a card in your hand", "자신의 효과로 자신의 패를 파기했을 때,")]
    [InlineData("When you trash a card in your hand using one of your effects", "자신의 효과로 자신의 패를 파기했을 때,")]
    [InlineData("When your opponent's hand is trashed from", "상대의 패가 파기되었을 때,")]
    [InlineData("When a card is trashed from your deck", "자신의 덱이 파기되었을 때,")]
    [InlineData("When an effect adds cards to your opponent's hand", "효과로 상대의 패에 카드가 추가되었을 때,")]
    [InlineData("When you use an Option card with a cost of 2 or more", "자신이 사용 코스트 2 이상의 옵션 카드를 사용했을 때,")]
    [InlineData("When you use [Royal Knight] trait Option cards", "자신이 특징으로 「로얄 나이츠」를 가진 옵션 카드를 사용했을 때,")]
    [InlineData("When an effect plays one of your Digimon", "효과로 자신의 디지몬이 등장했을 때,")]
    [InlineData("When an effect plays an opponent's Digimon or Tamer", "효과로 상대의 디지몬 또는 테이머가 등장했을 때,")]
    [InlineData("When an effect suspends one of your Digimon", "자신의 디지몬이 효과로 레스트했을 때,")]
    [InlineData("When an effect suspends this Digimon", "이 디지몬이 효과로 레스트했을 때,")]
    [InlineData("When an effect suspends another Digimon or Tamer", "다른 디지몬 또는 테이머가 효과로 레스트했을 때,")]
    [InlineData("When any Digimon suspend", "디지몬이 레스트했을 때,")]
    public void TriggerVariants_ShouldUseOfficialNotation(string en, string ko) =>
        Assert.StartsWith($"【서로의 턴】 {ko} 《1 드로우》", KoreanEffectRenderer.RenderClause($"[All Turns] {en}, ＜Draw 1＞"));

    [Theory]
    // 옵션을 코스트 없이 사용 — 공식: "자신의 패에서 사용 코스트 5 이하의 옵션 카드 1장을 코스트를 지불하지 않고 사용할 수 있다"
    [InlineData("You may use 1 Option card with a cost of 5 or less in your hand without paying the cost.", "자신의 패에서 사용 코스트 5 이하의 옵션 카드 1장을 코스트를 지불하지 않고 사용할 수 있다.")]
    [InlineData("You may use 1 purple Option card with a cost of 6 or less from your hand without paying the cost.", "자신의 패에서 퍼플인 사용 코스트 6 이하의 옵션 카드 1장을 코스트를 지불하지 않고 사용할 수 있다.")]
    [InlineData("You may use 1 single-color Option card with the [CS] trait from your hand without paying the cost.", "자신의 패에서 특징으로 「CS」를 가진 1색의 옵션 카드 1장을 코스트를 지불하지 않고 사용할 수 있다.")]
    [InlineData("You may use 1 Option card with the [Three Musketeers] trait from your hand or under your Tamers without paying the cost.", "자신의 패 또는 테이머 아래에서 특징으로 「3총사」를 가진 옵션 카드 1장을 코스트를 지불하지 않고 사용할 수 있다.")]
    [InlineData("You may use 1 use cost 3 or lower Option card with [Greymon] in its text from your hand or trash without paying the cost.", "자신의 패 또는 트래시에서 「그레이몬」이 기술되어 있는 사용 코스트 3 이하의 옵션 카드 1장을 코스트를 지불하지 않고 사용할 수 있다.")]
    [InlineData("You may use an Option card with a memory cost of 7 in your hand without paying its memory cost.", "자신의 패에서 사용 코스트 7의 옵션 카드 1장을 코스트를 지불하지 않고 사용할 수 있다.")]
    public void UseOptionWithoutPaying_ShouldUseOfficialNotation(string en, string ko) =>
        Assert.Equal($"【진화 시】 {ko}", KoreanEffectRenderer.RenderClause($"[When Digivolving] {en}"));

    [Fact]
    public void HandMainLegendArms_ShouldUseOfficialNotation()
    {
        string? R(string en) => KoreanEffectRenderer.RenderClause(en);
        // 공식: "[패]【메인】1 코스트 지불하고, 이 카드를 특징으로 「Legend-Arms」를 가지거나 Lv.3의 자신의 디지몬의 진화원 아래에 놓는 것으로, 《1 드로우》."
        Assert.StartsWith("[패] 【메인】 1 코스트 지불하고, 이 카드를 특징으로 「Legend-Arms」를 가지거나 Lv.3의 자신의 디지몬의 진화원 아래에 놓는 것으로, 이 턴 동안, 그 디지몬의 DP를 +4000 한다.",
            R("[Hand] [Main] By paying 1 cost and placing this card as the bottom digivolution card of 1 of your Digimon that's level 3 or has the [Legend-Arms] trait, that Digimon gets +4000 DP for the turn."));
        // 공식: "그 디지몬의 DP 이하의 상대 디지몬 1마리를 소멸시킨다."
        Assert.EndsWith("그 디지몬의 DP 이하의 상대의 디지몬 1마리를 소멸시킨다.",
            R("[Hand] [Main] By paying 3 cost and placing this card as the bottom digivolution card of 1 of your Digimon that's level 6 or has the [Legend-Arms] trait, delete 1 of your opponent's Digimon with as much or less DP as that Digimon."));
        // v0.25.0 — 공식: "자신의 패의 「테슬라젤리몬」 1장을 자신의 「젤리몬」의 진화원 아래에 놓는 것으로, 그 디지몬을 이 카드로 진화 조건을 무시하고 진화 코스트 3으로 진화시킨다."
        Assert.EndsWith("자신의 트래시의 「슈슈몬」 1장을 자신의 「슈몬」의 진화원 아래에 놓는 것으로, 그 디지몬을 이 카드로 진화 조건을 무시하고 진화 코스트 3으로 진화시킨다.",
            R("[Hand] [Main] If you have [Arisa Kinosaki], by placing 1 [ShoeShoemon] from your trash as any of your [Shoemon]'s bottom digivolution card, it digivolves into this card for a digivolution cost of 3, ignoring digivolution requirements."));
        Assert.EndsWith("자신의 「아구몬」 1마리를 이 카드로 진화 조건을 무시하고 진화 코스트 6으로 진화시킨다.",
            R("[Hand] [Main] If you have [Nokia Shiramine], 1 of your [Agumon] digivolves into this card for a digivolution cost of 6, ignoring digivolution requirements."));
        // 공식: "[트래시]【메인】자신의 패가 4장 이하라면, 이 카드를 지불하는 등장 코스트 -4 하여 등장시킨다."
        Assert.Equal("[트래시] 【메인】 자신의 패가 4장 이하일 때, 이 카드를 지불하는 등장 코스트 -2 하여 등장시킨다.",
            R("[Trash] [Main] If you have 4 or fewer cards in your hand, play this card with the play cost reduced by 2."));
    }
}
