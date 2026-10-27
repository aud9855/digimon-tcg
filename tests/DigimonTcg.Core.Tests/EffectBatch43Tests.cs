using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>
/// v0.43.0 미지원 문장 17차 묶음: 링크 카드의 링크 효과 (링크되어 있는 동안) · 영어 데이터가 깨진 링크 카드 · 어플 합체 (메인 페이즈 · 효과) · ≪블래스트 조그레스≫ ·
/// 겹쳐진 맨 위 카드 (비용 · 사건) · 트래시의 카드를 시큐리티 아래에 · "for each" 지속 효과 · 상대 메모리 셈 · 고른 디지몬의 진화원 셈 · 코스트를 내고 옵션 사용 · "대신"
/// (어플 합체 · 조그레스 조건은 공식 한글 문구에서 읽으므로 KoreanCardData를 바꾸는 테스트와 같은 컬렉션)
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch43Tests
{
    private sealed class Scripted : IChoiceProvider
    {
        public bool ConfirmAnswer = true;
        public int Confirms;
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) { Confirms++; return ConfirmAnswer; }
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) =>
            recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => recommended;
        public IReadOnlyList<int> ChooseCards(GameState state, string question, CardChoiceZone zone, IReadOnlyList<CardData> options, int min, int max, IReadOnlyList<int> recommended) => recommended;
    }

    private static CardData Card(string id, string main = "", CardType type = CardType.Option, int level = 0, int playCost = 3,
        CardColor color = CardColor.Blue, int dp = -1, string? name = null, string inherited = "", int evoCost = 1, CardColor? second = null, params string[] traits)
    {
        var card = new CardData
        {
            CardId = id, Name = name ?? id, Type = type, Color = color, SecondaryColor = second, Level = level, DP = dp >= 0 ? dp : level * 1000, PlayCost = playCost, EvoCost = evoCost,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main, InheritedEffectText = inherited, Traits = traits.ToList(),
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    private static CardData Digimon(string id, int level, string main = "", string inherited = "", int dp = -1, string? name = null, int playCost = 3,
        CardColor color = CardColor.Blue, int evoCost = 1, CardColor? second = null, params string[] traits) =>
        Card(id, main, CardType.Digimon, level, playCost, color, dp, name, inherited, evoCost, second, traits);

    private static CardData Tamer(string id, string main = "", CardColor color = CardColor.Blue, string? name = null, params string[] traits) =>
        Card(id, main, CardType.Tamer, 0, 2, color, 0, name, "", 1, null, traits);

    private static (GameState State, GoldfishEngine Engine, Scripted Choices) NewMainPhase()
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3 };
        state.MemoryGauge = 8;
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B43-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B43-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B43-SEC" + i, 3, dp: 1000, color: CardColor.White));
            state.DummyOpponent.SecurityStack.Add(Digimon("B43-OSEC" + i, 3, dp: 1000, color: CardColor.White));
        }
        var choices = new Scripted();
        return (state, new GoldfishEngine(state) { PlayerChoices = choices }, choices);
    }

    private static CardInstance Put(PlayerZones zones, CardData card, bool suspended = false, int turnPlayed = 1, params CardData[] sources)
    {
        var d = new CardInstance(card, turnPlayed) { IsSuspended = suspended };
        foreach (var s in sources) d.AddBottomSource(s);
        zones.BattleArea.Add(d);
        return d;
    }

    private static void AssertFull(CardData card) => Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(card).Level);

    private static void WithKoreanCards(IEnumerable<KoreanCard> extra, Action run)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dtcg_b43_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        var file = System.IO.Path.Combine(dir, "cards_ko.json");
        System.IO.File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(KoreanCardData.All.Concat(extra).ToList()));
        try
        {
            KoreanCardData.Reload(file);
            run();
        }
        finally
        {
            KoreanCardData.Reload();
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
    }

    // =====================================================================
    // 1. 링크 카드의 링크 효과 (링크되어 있는 동안 — 진화원에서는 발휘하지 않는다)
    // =====================================================================

    [Fact]
    public void LinkEffect_WhenAttacking_FiresWhileLinked_NotFromSources_EX10_014()
    {
        var (state, engine, _) = NewMainPhase();
        var link = Digimon("B43-WEATHER", 4, inherited: "[When Attacking] By trashing 1 of this Digimon's link cards, 1 of your opponent's Digimon gets -6000 DP for the turn.", traits: "Weather (App Name)");
        AssertFull(link);
        Assert.Contains(CardScripts.For(link)!.Effects, e => e.Trigger == EffectTrigger.WhenAttacking && e.Source == EffectSource.Link43);
        Assert.DoesNotContain(CardScripts.For(link)!.Effects, e => e.Source == EffectSource.Inherited);
        var host = Put(state.Player, Digimon("B43-HOST", 5));
        engine.LinkCard(state.Player, host, link);
        var victim = Put(state.DummyOpponent, Digimon("B43-V1", 4, dp: 5000), suspended: true);
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, host, host.TopCard);
        Assert.DoesNotContain(victim, state.DummyOpponent.BattleArea);   // DP 5000 - 6000 → 소멸
        Assert.Empty(host.LinkedCards);
        Assert.Contains(link, state.Player.Trash);

        // 진화원에 있을 때는 발휘하지 않는다 (진화원 효과가 아니다)
        var host2 = Put(state.Player, Digimon("B43-HOST2", 5), sources: link);
        var victim2 = Put(state.DummyOpponent, Digimon("B43-V2", 4, dp: 5000), suspended: true);
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, host2, host2.TopCard);
        Assert.Contains(victim2, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void LinkEffect_KeywordAndSecurityAttack_OnlyWhileLinked()
    {
        var (state, engine, _) = NewMainPhase();
        var pierce = Digimon("B43-NAVIL", 4, inherited: "＜Piercing＞", traits: "Navi (App Name)");
        var sa = Digimon("B43-OUJA", 4, inherited: "＜Security A. +1＞", traits: "Game (App Name)");
        Assert.Equal(0, sa.InheritedSecurityAttackBonus);   // 링크 카드의 하단은 진화원 효과가 아니다
        var host = Put(state.Player, Digimon("B43-HOSTK", 5));
        engine.LinkCard(state.Player, host, pierce);
        state.Player.Trash.Clear();
        Assert.True(CardScripts.HasKeyword(host, Keyword.Piercing));
        var host2 = Put(state.Player, Digimon("B43-HOSTK2", 5), sources: new[] { pierce, sa });
        engine.RecalculateFieldBuffs();
        Assert.False(CardScripts.HasKeyword(host2, Keyword.Piercing));
        Assert.Equal(1, host2.GetSecurityAttackCount());
        var host3 = Put(state.Player, Digimon("B43-HOSTK3", 5));
        engine.LinkCard(state.Player, host3, sa);
        Assert.Equal(2, host3.GetSecurityAttackCount());
    }

    [Fact]
    public void LinkEffect_WouldLeave_TrashLinkInstead_EX10_030()
    {
        var (state, engine, _) = NewMainPhase();
        var link = Digimon("B43-COMET", 4, inherited: "[All Turns] [Once Per Turn] When this Digimon would leave the battle area, by trashing 1 of its link cards, it doesn't leave.", traits: "Space (App Name)");
        AssertFull(link);
        var host = Put(state.Player, Digimon("B43-HOSTL", 5));
        engine.LinkCard(state.Player, host, link);
        Assert.True(engine.TryPreventLeave(state.Player, host, deletion: true, inBattle: true));
        Assert.Empty(host.LinkedCards);
        Assert.Contains(link, state.Player.Trash);
    }

    [Fact]
    public void BrokenLinkText_EX10_024_And_BT23_009_ReadAsOfficialText()
    {
        var (state, engine, _) = NewMainPhase();
        var kabe = Digimon("EX10-024", 3, "[Security] At the end of the battle, play this card without paying the cost.", "[When Attacking] By trashing 1 of this Digimon's link cards,", traits: "Wall (App Name)");
        AssertFull(kabe);
        var host = Put(state.Player, Digimon("B43-HOSTW", 5));
        engine.LinkCard(state.Player, host, kabe);
        var opp = Put(state.DummyOpponent, Digimon("B43-OPP5", 5), sources: Digimon("B43-OPP4", 4));
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, host, host.TopCard);
        Assert.Equal("B43-OPP4", opp.TopCard.CardId);   // ≪퇴화 1≫
        Assert.Empty(host.LinkedCards);
        Assert.Equal("【어택 시】 이 디지몬의 링크 카드를 1장 파기하는 것으로, 상대의 디지몬 1마리를 《퇴화 1》 (상대 디지몬 위에 포개진 카드를 위에서부터 1장 파기한다. 진화원이 없어지거나, Lv.3의 디지몬이 되었다면 그 이상 파기할 수 없다) 한다.",
            KoreanEffectRenderer.RenderClause(kabe.InheritedEffectText));

        var coach = Digimon("BT23-009", 4, "[Your Turn] [Once Per Turn] When this Digimon gets linked, 1 of your Digimon gets +4000 DP for the turn.", "[Once Per Turn] This Digimon may attack a player.", traits: "Sports (App Name)");
        AssertFull(coach);
        Assert.Contains(CardScripts.For(coach)!.Effects, e => e.Trigger == EffectTrigger.EndOfYourTurn && e.Source == EffectSource.Link43);
        Assert.Equal("【자신의 턴 종료 시】 [턴에 1회] 이 디지몬으로 플레이어에게 어택할 수 있다.", KoreanEffectRenderer.RenderClause(coach.InheritedEffectText));
    }

    // =====================================================================
    // 2. 어플 합체 (종합 룰 8-4)
    // =====================================================================

    private static IEnumerable<KoreanCard> FusionKo() => new[]
    {
        new KoreanCard { CardId = "B43-SEARCH", Name = "테스트서치몬" },
        new KoreanCard { CardId = "B43-NAVI", Name = "테스트내비몬" },
        new KoreanCard { CardId = "B43-SUPER", Name = "테스트슈퍼서치몬", MainText = "〔어플 합체〕「테스트서치몬」＆「테스트내비몬」＆「테스트츠부몬」 : 코스트 0 2종의 카드에 의해 링크 상태라면, 링크 카드를 위에 겹쳐 진화시킨다" },
        new KoreanCard { CardId = "B43-GLOBE", Name = "테스트글로브몬", MainText = "〔어플 합체〕「테스트슈퍼서치몬」＆「테스트내비몬」 : 코스트 2" },
    };

    [Fact]
    public void AppFusion_Player_StacksLinkCardThenFusionCard_Draws()
    {
        WithKoreanCards(FusionKo(), () =>
        {
            var (state, engine, _) = NewMainPhase();
            var search = Digimon("B43-SEARCH", 3, traits: "Search (App Name)");
            var navi = Digimon("B43-NAVI", 3, traits: "Navi (App Name)");
            var super = Digimon("B43-SUPER", 4, "[When Digivolving] Gain 1 memory.", traits: "Super Search (App Name)");
            var host = Put(state.Player, search, suspended: true);
            Assert.Empty(engine.GetAppFusionTargets(super));   // 패에 없음
            state.Player.Hand.Add(super);
            Assert.Empty(engine.GetAppFusionTargets(super));   // 링크 상태가 아님
            host.LinkedCards.Add(navi);
            Assert.Single(engine.GetAppFusionTargets(super));
            int hand = state.Player.Hand.Count;
            var r = engine.AppFuse(super, host);
            Assert.True(r.CanExecute, r.Reason);
            Assert.Same(super, host.TopCard);
            Assert.Equal(new[] { "B43-NAVI", "B43-SEARCH" }, host.EvolutionSources.Select(c => c.CardId).ToArray());   // 링크 카드를 위에 겹치고 그 위에 진화
            Assert.Empty(host.LinkedCards);
            Assert.True(host.IsSuspended);   // 진화하기 전의 표시 형식 그대로
            Assert.Equal(hand, state.Player.Hand.Count);   // 1장 내고 1장 드로우
            Assert.Equal(8 + 1, state.MemoryGauge);   // 코스트 0 + [진화 시]
        });
    }

    [Fact]
    public void AppFusion_NeedsTwoDifferentKinds_AndPaysCost()
    {
        WithKoreanCards(FusionKo(), () =>
        {
            var (state, engine, _) = NewMainPhase();
            var search = Digimon("B43-SEARCH", 3, traits: "Search (App Name)");
            var super = Digimon("B43-SUPER", 4, "[When Digivolving] Gain 1 memory.", traits: "Super Search (App Name)");
            var globe = Digimon("B43-GLOBE", 5, traits: "Globe (App Name)");
            var host = Put(state.Player, search);
            host.LinkedCards.Add(Digimon("B43-SEARCH", 3, traits: "Search (App Name)"));   // 같은 종류 2장은 아니다
            state.Player.Hand.Add(super);
            Assert.False(engine.AppFuse(super, host).CanExecute);
            var host2 = Put(state.Player, super);
            host2.LinkedCards.Add(Digimon("B43-NAVI", 3, traits: "Navi (App Name)"));
            state.Player.Hand.Add(globe);
            Assert.True(engine.AppFuse(globe, host2).CanExecute);
            Assert.Equal(8 - 2, state.MemoryGauge);
            Assert.Equal(2, GoldfishEngine.GetAppFusionCost(globe));
        });
    }

    [Fact]
    public void AppFusion_ByEffect_FromHandAndTrash()
    {
        WithKoreanCards(FusionKo(), () =>
        {
            var (state, engine, _) = NewMainPhase();
            var tamer = Tamer("B43-HARU", "[Main] 1 of your Digimon may app fuse into a Digimon card in the hand.");
            AssertFull(tamer);
            state.Player.Tamers.Add(tamer);
            var host = Put(state.Player, Digimon("B43-SEARCH", 3, traits: "Search (App Name)"));
            host.LinkedCards.Add(Digimon("B43-NAVI", 3, traits: "Navi (App Name)"));
            var super = Digimon("B43-SUPER", 4, "[When Digivolving] Gain 1 memory.", traits: "Super Search (App Name)");
            state.Player.Hand.Add(super);
            Assert.True(engine.ActivateMainAbility(null, tamer).CanExecute);
            Assert.Same(super, host.TopCard);
            Assert.Equal("B43-NAVI", host.EvolutionSources.Peek().CardId);

            var opt = Card("B43-REI", "[Main] 1 of your Digimon may app fuse into a Digimon card with the [Globe (App Name)] trait in the trash.", playCost: 0);
            AssertFull(opt);
            host.LinkedCards.Add(Digimon("B43-NAVI", 3, traits: "Navi (App Name)"));
            var globe = Digimon("B43-GLOBE", 5, traits: "Globe (App Name)");
            state.Player.Trash.Add(globe);
            state.Player.Hand.Add(opt);
            Assert.True(engine.PlayCard(opt).CanExecute);
            Assert.Same(globe, host.TopCard);
            Assert.DoesNotContain(globe, state.Player.Trash);
            Assert.Equal("자신의 디지몬 1마리를 패의 디지몬 카드로 어플 합체할 수 있다.", KoreanEffectRenderer.RenderClause("1 of your Digimon may app fuse into a Digimon card in the hand."));
        });
    }

    [Fact]
    public void AppFusion_Ai_FusesDuringItsTurn()
    {
        WithKoreanCards(FusionKo(), () =>
        {
            var (state, engine, _) = NewMainPhase();
            state.CurrentStage = PracticeStage.Stage3_AiBattle;
            var host = Put(state.DummyOpponent, Digimon("B43-SEARCH", 3, traits: "Search (App Name)"));
            host.LinkedCards.Add(Digimon("B43-NAVI", 3, traits: "Navi (App Name)"));
            var super = Digimon("B43-SUPER", 4, "[When Digivolving] Gain 1 memory.", traits: "Super Search (App Name)");
            super.EvoRequirementLevel = 9;   // 보통 진화는 할 수 없다
            state.DummyOpponent.Hand.Add(super);
            state.MemoryGauge = 0;
            engine.PassTurn();
            Assert.Same(super, host.TopCard);
            Assert.Equal("B43-NAVI", host.EvolutionSources.Peek().CardId);
        });
    }

    // =====================================================================
    // 3. ≪블래스트 조그레스≫ (종합 룰 16-31)
    // =====================================================================

    private static IEnumerable<KoreanCard> BlastKo() => new[]
    {
        new KoreanCard { CardId = "B43-WG", Name = "테스트워그레이몬" },
        new KoreanCard { CardId = "B43-MG", Name = "테스트메탈가루몬" },
        new KoreanCard { CardId = "B43-OMNI", Name = "테스트오메가몬", MainText = "〔조그레스〕명칭에 「그레이몬」을 포함하는 Lv.6+명칭에 「가루몬」을 포함하는 Lv.6 : 코스트 0" },
    };

    private const string OmniText = "[Hand] [Counter] ＜Blast DNA Digivolve ([WarGreymon] + [MetalGarurumon])＞ (1 of your specified Digimon and 1 specified card in hand may DNA digivolve into this card.)\r\n[When Digivolving] If DNA digivolving, gain 2 memory.";

    [Fact]
    public void BlastDna_Counter_NewActiveDigimon_HandCardBelow()
    {
        WithKoreanCards(BlastKo(), () =>
        {
            var (state, engine, _) = NewMainPhase();
            var omni = Digimon("B43-OMNI", 7, OmniText, color: CardColor.White, dp: 15000);
            AssertFull(omni);
            Assert.Equal(("WarGreymon", "MetalGarurumon"), CardScripts.For(omni)!.BlastDna43);
            var wg = Put(state.Player, Digimon("B43-WG", 6, name: "WarGreymon", color: CardColor.Red), suspended: true, sources: Digimon("B43-MGR", 5, name: "MetalGreymon", color: CardColor.Red));
            wg.LinkedCards.Add(Digimon("B43-LNK", 4));
            var mg = Digimon("B43-MG", 6, name: "MetalGarurumon");
            state.Player.Hand.Add(omni);
            state.Player.Hand.Add(mg);
            var attacker = Put(state.DummyOpponent, Digimon("B43-ATK", 6, dp: 12000));
            int hand = state.Player.Hand.Count;
            engine.CounterTiming(state.Player, attacker);
            Assert.DoesNotContain(wg, state.Player.BattleArea);   // 새로운 디지몬 (종합 룰 8-2-2-1)
            var nd = Assert.Single(state.Player.BattleArea);
            Assert.Same(omni, nd.TopCard);
            Assert.False(nd.IsSuspended);
            Assert.Equal(new[] { "B43-WG", "B43-MGR", "B43-MG" }, nd.EvolutionSources.Select(c => c.CardId).ToArray());   // 조건 왼쪽(그레이몬) 묶음이 위
            Assert.Empty(nd.LinkedCards);
            Assert.Contains(state.Player.Trash, c => c.CardId == "B43-LNK");   // 링크 카드는 파기
            Assert.Equal(hand - 2 + 1, state.Player.Hand.Count);
            Assert.Equal(8 + 2, state.MemoryGauge);   // [진화 시] "조그레스 진화하고 있었다면"
            Assert.Equal("[패]【카운터】《블래스트 조그레스《「워그레이몬」+「메탈가루몬」》》(지정된 자신의 디지몬 1마리와 패의 지정된 카드 1장으로, 이 카드로 조그레스 진화할 수 있다)",
                KoreanEffectRenderer.RenderClause(OmniText.Split("\r\n")[0]));
        });
    }

    [Fact]
    public void BlastDna_NeedsPartnerInHand_AndDnaRequirement()
    {
        WithKoreanCards(BlastKo(), () =>
        {
            var (state, engine, _) = NewMainPhase();
            var omni = Digimon("B43-OMNI", 7, OmniText, color: CardColor.White, dp: 15000);
            var wg = Put(state.Player, Digimon("B43-WG", 6, name: "WarGreymon", color: CardColor.Red));
            state.Player.Hand.Add(omni);
            var attacker = Put(state.DummyOpponent, Digimon("B43-ATK2", 6, dp: 12000));
            engine.CounterTiming(state.Player, attacker);
            Assert.Same(wg, Assert.Single(state.Player.BattleArea));   // 짝 카드가 없다
            state.Player.Hand.Add(Digimon("B43-MG5", 5, name: "MetalGarurumon"));   // 이름은 맞지만 Lv.5 — 조그레스 조건(Lv.6)에 맞지 않는다
            engine.CounterTiming(state.Player, attacker);
            Assert.Same(wg, Assert.Single(state.Player.BattleArea));
        });
    }

    [Fact]
    public void BlastDna_AiDefender_AttackTargetDisappears()
    {
        WithKoreanCards(BlastKo(), () =>
        {
            var (state, engine, _) = NewMainPhase();
            var omni = Digimon("B43-OMNI", 7, OmniText, color: CardColor.White, dp: 15000);
            var wg = Put(state.DummyOpponent, Digimon("B43-WG", 6, name: "WarGreymon", color: CardColor.Red, dp: 12000), suspended: true);
            state.DummyOpponent.Hand.Add(omni);
            state.DummyOpponent.Hand.Add(Digimon("B43-MG", 6, name: "MetalGarurumon"));
            var me = Put(state.Player, Digimon("B43-ME", 6, dp: 13000));
            var r = engine.AttackOpponentDigimon(me, wg);
            Assert.True(r.CanExecute, r.Reason);
            Assert.Contains(me, state.Player.BattleArea);   // 배틀 없음
            var nd = Assert.Single(state.DummyOpponent.BattleArea);
            Assert.Same(omni, nd.TopCard);
            Assert.False(nd.IsSuspended);
        });
    }

    // =====================================================================
    // 4. 겹쳐진 맨 위 카드 · 트래시의 카드를 시큐리티 아래에
    // =====================================================================

    [Fact]
    public void TopStackedCost_And_TopStackedEvent_BT21_085_BT21_094()
    {
        var (state, engine, _) = NewMainPhase();
        var tamer = Tamer("B43-DAISUKE", "[Main] By suspending this Tamer and trashing the top stacked card of 1 of your [Armor Form] trait Digimon, ＜Draw 1＞ (Draw 1 card from your deck.) and gain 1 memory.");
        AssertFull(tamer);
        var watcher = Digimon("B43-WATCH", 5, "[All Turns] When the top stacked card of any of your [Armor Form] trait Digimon is trashed, gain 1 memory.");
        AssertFull(watcher);
        Put(state.Player, watcher);
        state.Player.Tamers.Add(tamer);
        var armor = Put(state.Player, Digimon("B43-FLAMEDRAMON", 4, traits: "Armor Form"), sources: Digimon("B43-V", 3));
        int hand = state.Player.Hand.Count;
        Assert.True(engine.ActivateMainAbility(null, tamer).CanExecute);
        Assert.Equal("B43-V", armor.TopCard.CardId);
        Assert.Contains(state.Player.Trash, c => c.CardId == "B43-FLAMEDRAMON");
        Assert.Equal(hand + 1, state.Player.Hand.Count);
        Assert.Equal(8 + 1 + 1, state.MemoryGauge);   // 비용 효과 +1 · 사건 +1
        Assert.Contains("겹쳐져 있는 카드를 위에서부터 1장 파기하는 것으로", KoreanEffectRenderer.RenderClause(tamer.EffectText));
    }

    [Fact]
    public void TopStackedEvent_FiresOnDeDigivolve()
    {
        var (state, engine, _) = NewMainPhase();
        Put(state.Player, Digimon("B43-WATCH2", 5, "[All Turns] When the top stacked card of any of your [Armor Form] trait Digimon is trashed, gain 1 memory."));
        var armor = Put(state.Player, Digimon("B43-ARM5", 5, traits: "Armor Form"), sources: Digimon("B43-ARM4", 4));
        engine.DeDigivolve(state.Player, armor, 1);
        Assert.Equal("B43-ARM4", armor.TopCard.CardId);
        Assert.Equal(8 + 1, state.MemoryGauge);
    }

    [Fact]
    public void PlaceFromTrashAtSecurityBottom_BT14_038()
    {
        var (state, engine, _) = NewMainPhase();
        var opt = Card("B43-ETE", "[Main] Place 1 [Etemon] from your trash at the bottom of your security stack.", playCost: 0);
        AssertFull(opt);
        var etemon = Digimon("B43-ETEMON", 5, name: "Etemon");
        Put(state.Player, Digimon("B43-BLUE", 4));   // 옵션의 색 조건
        state.Player.Trash.Add(etemon);
        state.Player.Hand.Add(opt);
        int sec = state.Player.SecurityStack.Count;
        Assert.True(engine.PlayCard(opt).CanExecute);
        Assert.Equal(sec + 1, state.Player.SecurityStack.Count);
        Assert.Same(etemon, state.Player.SecurityStack[^1]);
        Assert.Equal("【메인】 자신의 트래시에서 「Etemon」 1장을 시큐리티 아래에 놓는다.".Replace("「Etemon」", KoreanEffectRenderer.RenderClause(opt.EffectText)!.Contains("「에테몬」") ? "「에테몬」" : "「Etemon」"),
            KoreanEffectRenderer.RenderClause(opt.EffectText));
    }

    // =====================================================================
    // 5. "for each" 지속 효과 · 셈
    // =====================================================================

    [Fact]
    public void ForEachStatic_OtherRoyalKnights_AllDigimonGetDp_BT13_017()
    {
        var (state, engine, _) = NewMainPhase();
        var rk = Digimon("B43-BT13017", 6, "[All Turns] For each other Digimon with the [Royal Knight] trait or [Sistermon] in their names you have in play, all of your Digimon get +1000 DP.", dp: 10000);
        AssertFull(rk);
        var self = Put(state.Player, rk);
        var a = Put(state.Player, Digimon("B43-RK1", 6, dp: 10000, traits: "Royal Knight"));
        var b = Put(state.Player, Digimon("B43-SIS", 4, name: "Sistermon Blanc", dp: 4000));
        var c = Put(state.Player, Digimon("B43-OTHER", 4, dp: 4000));
        engine.RecalculateFieldBuffs();
        Assert.Equal(12000, self.GetCurrentDP());   // 다른 2마리
        Assert.Equal(12000, a.GetCurrentDP());
        Assert.Equal(6000, c.GetCurrentDP());
    }

    [Fact]
    public void ForEachStatic_YourTurnPerTamer_BT17_076()
    {
        var (state, engine, _) = NewMainPhase();
        var eos = Digimon("B43-BT17076", 6, "[Your Turn] For each Tamer, all of your [Eosmon] get +1000 DP.", name: "Eosmon", dp: 10000);
        AssertFull(eos);
        var me = Put(state.Player, eos);
        state.Player.Tamers.Add(Tamer("B43-T1"));
        state.DummyOpponent.Tamers.Add(Tamer("B43-T2"));
        engine.RecalculateFieldBuffs();
        Assert.Equal(12000, me.GetCurrentDP());
    }

    [Fact]
    public void ForEachStatic_ColorsInSources_AD1_013()
    {
        var (state, engine, _) = NewMainPhase();
        var src = Digimon("B43-AD1013", 4, inherited: "[All Turns] For each color in this [Blue Flare] or [Xros Heart] trait Digimon's digivolution cards, it gets +1000 DP.", color: CardColor.Yellow);
        AssertFull(src);
        var host = Put(state.Player, Digimon("B43-BF", 5, dp: 7000, traits: "Blue Flare"), sources: new[] { src, Digimon("B43-R", 3, color: CardColor.Red), Digimon("B43-Y2", 3, color: CardColor.Yellow) });
        var plain = Put(state.Player, Digimon("B43-NOBF", 5, dp: 7000), sources: new[] { src, Digimon("B43-R2", 3, color: CardColor.Red) });
        engine.RecalculateFieldBuffs();
        Assert.Equal(7000 + 2000, host.GetCurrentDP());   // 옐로 · 레드
        Assert.Equal(7000, plain.GetCurrentDP());
    }

    [Fact]
    public void Counter_OpponentMemory_BT25_086()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B43-OPPMEM", 5, "[On Play] 1 of your Digimon gains +1000 DP for the turn for each memory your opponent has.", dp: 6000);
        AssertFull(card);
        var me = Put(state.Player, card);
        state.MemoryGauge = -3;   // 상대 쪽 3
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        engine.RecalculateFieldBuffs();
        Assert.Equal(9000, me.GetCurrentDP());
        Assert.Contains("상대의 메모리 1마다", KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void Counter_ThatDigimonsSources_BT7_104()
    {
        var (state, engine, _) = NewMainPhase();
        var opt = Card("B43-BT7104", "[Main] Choose 1 of your Digimon with the [X Antibody] trait. Then, ＜Draw 1＞ for each of that Digimon's digivolution cards.", playCost: 0);
        AssertFull(opt);
        Put(state.Player, Digimon("B43-XA", 5, traits: "X Antibody"), sources: new[] { Digimon("B43-S1", 4), Digimon("B43-S2", 3), Digimon("B43-S3", 2) });
        state.Player.Hand.Add(opt);
        int hand = state.Player.Hand.Count;
        Assert.True(engine.PlayCard(opt).CanExecute);
        Assert.Equal(hand - 1 + 3, state.Player.Hand.Count);
    }

    [Fact]
    public void UseOptionPayingCost_ReducedByOpponentMemory_BT26_090()
    {
        var (state, engine, _) = NewMainPhase();
        var tamer = Tamer("B43-BT26090", "[Main] By suspending this Tamer, you may use 1 Option card with the [TS] trait from your hand. For each point of memory your opponent has, reduce this effect's paid cost by 1.");
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        var ts = Card("B43-TSOPT", "[Main] Gain 2 memory.", playCost: 3, traits: "TS");
        state.Player.Hand.Add(ts);
        Assert.True(engine.ActivateMainAbility(null, tamer).CanExecute);
        Assert.DoesNotContain(ts, state.Player.Hand);
        Assert.Equal(8 - 3 + 2, state.MemoryGauge);   // 상대 쪽 메모리 0 → 코스트 3 그대로
    }

    // =====================================================================
    // 5-B. [육성] 효과 · 링크하는 옵션 · 다른 표기
    // =====================================================================

    [Fact]
    public void Breeding_OptionPlaced_GainMemory_BT13_007()
    {
        var (state, engine, _) = NewMainPhase();
        var egg = Card("B43-BT13007", "", CardType.Digitama, 2, inherited: "[Breeding] [Your Turn] [Once Per Turn] When an Option card with the [Royal Knight] trait is placed in the battle area, gain 1 memory.");
        AssertFull(egg);
        var b = new CardInstance(Digimon("B43-BR3", 3), 1);
        b.AddBottomSource(egg);
        state.Player.BreedingArea = b;
        engine.PlaceOption(state.Player, Card("B43-RKOPT", "", traits: "Royal Knight"));
        Assert.Equal(8 + 1, state.MemoryGauge);
        engine.PlaceOption(state.Player, Card("B43-RKOPT2", "", traits: "Royal Knight"));
        Assert.Equal(8 + 1, state.MemoryGauge);   // [턴 1회]
    }

    [Fact]
    public void Breeding_DigiEggToSources_DeleteAll_EX6_006()
    {
        var (state, engine, _) = NewMainPhase();
        var egg = Card("B43-EX6006", "[Breeding] [Start of Your Main Phase] Place the top card of your Digi-Egg deck as this Digimon's bottom digivolution card and delete all of your Digimon. If this effect deleted, place 1 card with the [Seven Great Demon Lords] trait from your trash as this Digimon's bottom digivolution card.", CardType.Digitama, 2);
        AssertFull(egg);
        state.Player.BreedingArea = new CardInstance(egg, 1);
        var top = Card("B43-EGGTOP", "", CardType.Digitama, 2);
        state.Player.DigitamaDeck.Push(top);
        Put(state.Player, Digimon("B43-D1", 3));
        Put(state.Player, Digimon("B43-D2", 4));
        var lord = Digimon("B43-LORD", 6, traits: "Seven Great Demon Lords");
        state.Player.Trash.Add(lord);
        engine.RunBreedingEffects42(EffectTrigger.StartOfYourMainPhase, state.Player, null, null);
        Assert.Empty(state.Player.BattleArea);
        Assert.Equal(new[] { "B43-EGGTOP", "B43-LORD" }, state.Player.BreedingArea!.EvolutionSources.Select(c => c.CardId).ToArray());
        Assert.Contains("자신의 디지타마 덱 위에서부터 1장을 이 디지몬의 진화원 아래에 놓고, 자신의 디지몬 전부를 소멸시킨다", KoreanEffectRenderer.RenderClause(egg.EffectText));
    }

    [Fact]
    public void Breeding_ReturnNegamonToEggDeck_MoveToBattle_EX9_057()
    {
        var (state, engine, _) = NewMainPhase();
        var abba = Digimon("B43-EX9057", 6, "[Breeding] [Opponent's Turn] When one of your opponent's Digimon attacks, by returning 4 [Negamon] from your trash or your Digimon's digivolution cards to the bottom of the Digi-Egg deck, this Digimon moves to the battle area.", color: CardColor.Purple);
        AssertFull(abba);
        var opp = state.DummyOpponent;   // 플레이어의 턴 = AI에게는 상대의 턴
        opp.BreedingArea = new CardInstance(abba, 1);
        var eggTop = Card("B43-OEGG", "", CardType.Digitama, 2);
        opp.DigitamaDeck.Push(eggTop);
        for (int i = 0; i < 3; i++) opp.Trash.Add(Digimon("B43-NEGA" + i, 3, name: "Negamon"));
        Put(opp, Digimon("B43-OHOST", 5), sources: Digimon("B43-NEGA9", 3, name: "Negamon"));
        var attacker = Put(state.Player, Digimon("B43-PATK", 5));
        engine.FireForSide(EffectTrigger.OpponentDigimonAttacks, opp, attacker);
        Assert.Null(opp.BreedingArea);
        Assert.Contains(opp.BattleArea, d => ReferenceEquals(d.TopCard, abba));
        Assert.Equal(5, opp.DigitamaDeck.Count);
        Assert.Same(eggTop, opp.DigitamaDeck.Peek());   // 덱 아래로
        Assert.DoesNotContain(opp.Trash, c => c.Name == "Negamon");
    }

    [Fact]
    public void LinkOption_PlugIn_LinkEffect_ST22_08()
    {
        var plug = Card("ST22-08", "[Main] 1 of your Digimon gets +2000 DP for the turn.", traits: "Plug-In", inherited: "[Once Per Turn] This Digimon may attack.");
        AssertFull(plug);
        Assert.Contains(CardScripts.For(plug)!.Effects, e => e.Trigger == EffectTrigger.EndOfYourTurn && e.Source == EffectSource.Link43);
        Assert.Equal("【자신의 턴 종료 시】 [턴에 1회] 이 디지몬으로 어택할 수 있다.", KoreanEffectRenderer.RenderClause(plug.InheritedEffectText));
    }

    [Fact]
    public void InsteadGainIt_ThreeTargets_BT10_099()
    {
        var text = "[Main] Until the end of your opponent's turn, 1 of your opponent's Digimon gains ＜Security A. -1＞ (This Digimon checks 1 fewer security card.). If you have a [Venusmon] in play, 3 of your opponent's Digimon gain it instead.";
        var opt = Card("B43-BT10099", text, playCost: 0);
        AssertFull(opt);
        var (state, engine, _) = NewMainPhase();
        Put(state.Player, Digimon("B43-VENUS", 6, name: "Venusmon"));
        var os = Enumerable.Range(0, 3).Select(i => Put(state.DummyOpponent, Digimon("B43-OS" + i, 5))).ToList();
        state.Player.Hand.Add(opt);
        Assert.True(engine.PlayCard(opt).CanExecute);
        Assert.All(os, d => Assert.Equal(0, d.GetSecurityAttackCount()));
    }

    [Fact]
    public void WordingVariants_BT9_041_BT8_110()
    {
        AssertFull(Digimon("B43-BT9041", 6, "[When Digivolving] You may play 1 red or yellow Tamer card from your hand without paying its memory cost. Then, if [RizeGreymon] or [X Antibody] is in this Digimon's digivolution cards, 1 of opponent's Digimon gets -2000 DP for each red or yellow Tamer you have in play for the turn."));
        var opt = Card("B43-BT8110", "[Main] Trash the top card of 1 of your Digimon with the [Armor Form] trait. Then, you may digivolve 1 of your Digimon into a Digimon card in your hand with the [Armor Form] trait for its digivolution cost. If you do, unsuspend that Digimon.", playCost: 0);
        AssertFull(opt);
        var (state, engine, _) = NewMainPhase();
        var armor = Put(state.Player, Digimon("B43-ARMR", 4, traits: "Armor Form"), suspended: true, sources: Digimon("B43-ARM3", 3));
        var into = Digimon("B43-INTO", 4, traits: "Armor Form", evoCost: 2);
        into.EvoRequirementLevel = 3;
        state.Player.Hand.Add(into);
        state.Player.Hand.Add(opt);
        Assert.True(engine.PlayCard(opt).CanExecute);
        Assert.Same(into, armor.TopCard);   // 맨 위 카드 파기 → Lv.3 → Lv.4로 진화
        Assert.False(armor.IsSuspended);
        Assert.Equal(8 - 2, state.MemoryGauge);
    }

    // =====================================================================
    // 6. "대신"
    // =====================================================================

    [Fact]
    public void InsteadByResult_BT13_111()
    {
        var text = "[On Play] Delete 1 of your opponent's Digimon with the 6000 DP or less. If no Digimon was deleted by this effect, delete 1 of your opponent's Digimon with the 13000 DP or more instead.";
        var card = Digimon("B43-BT13111", 6, text, dp: 9000);
        AssertFull(card);
        {
            var (state, engine, _) = NewMainPhase();
            var me = Put(state.Player, card);
            var small = Put(state.DummyOpponent, Digimon("B43-SM", 4, dp: 5000));
            var big = Put(state.DummyOpponent, Digimon("B43-BIG", 7, dp: 14000));
            engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
            Assert.DoesNotContain(small, state.DummyOpponent.BattleArea);
            Assert.Contains(big, state.DummyOpponent.BattleArea);
        }
        {
            var (state, engine, _) = NewMainPhase();
            var me = Put(state.Player, card);
            var mid = Put(state.DummyOpponent, Digimon("B43-MID", 5, dp: 8000));
            var big = Put(state.DummyOpponent, Digimon("B43-BIG2", 7, dp: 14000));
            engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
            Assert.Contains(mid, state.DummyOpponent.BattleArea);
            Assert.DoesNotContain(big, state.DummyOpponent.BattleArea);
        }
    }

    [Fact]
    public void InsteadCompoundCondition_BT8_096()
    {
        var text = "[Main] Delete 1 of your opponent's Digimon with 4000 DP or less. If you have a Digimon in play with 2 or more colors, or with 2 or more colors in its digivolution cards, delete 1 of your opponent's Digimon with 7000 DP or less instead.";
        var opt = Card("B43-BT8096", text, playCost: 0);
        AssertFull(opt);
        var (state, engine, _) = NewMainPhase();
        Put(state.Player, Digimon("B43-MONO", 4), sources: Digimon("B43-TWO", 3, second: CardColor.Red));
        var six = Put(state.DummyOpponent, Digimon("B43-SIX", 5, dp: 6000));
        state.Player.Hand.Add(opt);
        Assert.True(engine.PlayCard(opt).CanExecute);
        Assert.DoesNotContain(six, state.DummyOpponent.BattleArea);
        Assert.Contains("2색 이상인 자신의 디지몬 또는 진화원에 2색 이상인 카드를 가진 자신의 디지몬이", KoreanEffectRenderer.RenderClause(text));
    }
}
