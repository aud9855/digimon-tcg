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
/// v0.53.0 미지원 문장 27차 묶음: 함께 소멸 · 합계 소멸 (BT16-070 · BT19-011 · P-094 · EX9-074) · AI가 고르는 레스트 / 액티브 대상 (BT24-047 등) ·
/// 시큐리티가 체크되었을 때 (EX5-053) · 이 디지몬이 다른 디지몬을 소멸시켰을 때 (BT16-061) · 효과로 레스트했을 때 · ≪볼텍스≫로 플레이어에게 (EX11-062) ·
/// 비용 (BT16-090 · BT20-086 · P-187 · BT25-060) · [Appmon] 특징 = "(App Name)" 특징
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch53Tests
{
    private static CardData Card(string id, string main = "", CardType type = CardType.Option, int level = 0, int playCost = 3,
        CardColor color = CardColor.Blue, int dp = -1, string? name = null, int evoCost = 1, string inherited = "", params string[] traits)
    {
        var card = new CardData
        {
            CardId = id, Name = name ?? id, Type = type, Color = color, Level = level, DP = dp >= 0 ? dp : level * 1000, PlayCost = playCost, EvoCost = evoCost,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main, InheritedEffectText = inherited, Traits = traits.ToList(),
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    private static CardData Digimon(string id, int level, string main = "", string? name = null, CardColor color = CardColor.Blue, int dp = -1, string inherited = "", int playCost = 3, params string[] traits) =>
        Card(id, main, CardType.Digimon, level, playCost, color, dp, name, 1, inherited, traits);

    private static CardData Tamer(string id, string main = "", string? name = null, CardColor color = CardColor.Blue, int playCost = 2) =>
        Card(id, main, CardType.Tamer, 0, playCost, color, 0, name, 0, "");

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B53-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B53-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B53-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B53-OSEC" + i, 3, color: CardColor.White, dp: 1000));
        }
        return (state, new GoldfishEngine(state));
    }

    private static CardInstance Put(PlayerZones zones, CardData card, params CardData[] sources)
    {
        var d = new CardInstance(card, 1);
        foreach (var s in sources) d.AddBottomSource(s);
        zones.BattleArea.Add(d);
        return d;
    }

    private static string Uid(string p) => p + Guid.NewGuid().ToString("N")[..8];

    private static void AllSupported(CardData card) =>
        Assert.All(EffectSupport.For(card).Clauses, c => Assert.True(c.Support == ClauseSupport.Supported, c.Text + " | " + c.Note));

    private static string Ko(string clause) => KoreanEffectRenderer.RenderClause(clause) ?? "<null>";

    private static string Logs(GameState s) => string.Join(" | ", s.ActionLogs);

    // =====================================================================
    // 1. 함께 소멸 · 합계 소멸
    // =====================================================================

    private const string Bt16070 = "[When Digivolving] [When Attacking] You may choose 1 of your Digimon. Delete it and 1 of your opponent's Digimon with as much or less DP as it.";

    [Fact]
    public void ChooseOwnAndDeleteBoth_PicksCheapPair_BT16_070()
    {
        var mon = Digimon(Uid("B53-BT16070-"), 4, Bt16070, dp: 6000);
        AllSupported(mon);
        Assert.Contains("선택한 디지몬의 DP 이하인 상대의 디지몬 1마리와 그 디지몬을 소멸시킨다", Ko(Bt16070));
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, mon);
        var small = Put(state.Player, Digimon("B53-SMALL3", 3, dp: 4000));
        var o4 = Put(state.DummyOpponent, Digimon("B53-OPP4", 4, dp: 4000));
        var o6 = Put(state.DummyOpponent, Digimon("B53-OPP6", 6, dp: 9000));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, mon);
        Assert.Contains(me, state.Player.BattleArea);   // 이 디지몬 자신을 고르지 않는다 (전 동작이면 가장 DP가 높은 자신을 골랐다)
        Assert.DoesNotContain(small, state.Player.BattleArea);
        Assert.DoesNotContain(o4, state.DummyOpponent.BattleArea);
        Assert.Contains(o6, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void ChooseOwnAndDeleteBoth_DeclinesWithoutGain_BT16_070()
    {
        var mon = Digimon(Uid("B53-BT16070B-"), 4, Bt16070, dp: 6000);
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, mon);
        var o = Put(state.DummyOpponent, Digimon("B53-OPP7", 6, dp: 9000));   // 고를 수 있는 상대가 없다
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, mon);
        Assert.Contains(me, state.Player.BattleArea);
        Assert.Contains(o, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void DeleteDpTotal_MaxGrowsPerOpponentDigimon_GainMemoryPerDeleted_BT19_011()
    {
        var mon = Digimon(Uid("B53-BT19011-"), 5, "[On Play] [When Digivolving] Delete any of your opponent's Digimon with DP adding up to 3000. For each of your opponent's Digimon, add 2000 to this DP-Based deletion effect's maximum. Then, for each Digimon deleted by this effect, gain 1 memory.", color: CardColor.Red);
        AllSupported(mon);
        Assert.Contains("DP 합계 3000까지", Ko(mon.EffectText));
        var (state, engine) = NewMainPhase(3);
        var me = Put(state.Player, mon);
        Put(state.DummyOpponent, Digimon("B53-O2", 3, dp: 2000));
        Put(state.DummyOpponent, Digimon("B53-O3", 4, dp: 3000));
        var big = Put(state.DummyOpponent, Digimon("B53-O5", 5, dp: 5000));
        // 상한 3000 + 2000 x 3 = 9000 → 5000 + 3000 (남은 1000으로는 2000을 고를 수 없다)
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, mon);
        Assert.Single(state.DummyOpponent.BattleArea);
        Assert.DoesNotContain(big, state.DummyOpponent.BattleArea);
        Assert.Equal(5, state.MemoryGauge);   // 소멸한 2마리마다 +1
    }

    [Fact]
    public void DeletePlayCostTotal_DigimonAndTamers_MaxGrowsPerVemmon_P_094()
    {
        var mon = Digimon(Uid("B53-P094-"), 5, "[On Play] [When Digivolving] Delete your opponent's Digimon and Tamers with a total play cost of 3. For every [Vemmon] in this Digimon's digivolution cards, increase the maximum play cost you can choose by this effect by 1.", color: CardColor.Black);
        AllSupported(mon);
        Assert.Contains("상대의 디지몬과 테이머를 선택하고", Ko(mon.EffectText));
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, mon, Digimon("B53-VEM", 2, name: "Vemmon"));
        var d3 = Put(state.DummyOpponent, Digimon("B53-OD3", 3, playCost: 3));
        var d5 = Put(state.DummyOpponent, Digimon("B53-OD5", 5, playCost: 5));
        var t1 = Tamer("B53-OT1", playCost: 1);
        state.DummyOpponent.Tamers.Add(t1);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, mon);   // 상한 3 + 1 = 4: 디지몬(3) + 테이머(1)
        Assert.DoesNotContain(d3, state.DummyOpponent.BattleArea);
        Assert.DoesNotContain(t1, state.DummyOpponent.Tamers);
        Assert.Contains(d5, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void SixColors_DeleteOneOfEachDifferentColor_EX9_074()
    {
        var mon = Digimon(Uid("B53-EX9074-"), 5, "[On Play] [When Digivolving] You may place 1 level 4 or lower [DM] trait Digimon card from your trash as this Digimon's top digivolution card. Then, delete 1 of your opponent's Digimon with the same color as any of this Digimon's digivolution cards. If this Digimon has 6 or more colors in its digivolution cards, instead delete 1 of each of your opponent's Digimon with different colors.", color: CardColor.Black);
        AllSupported(mon);
        Assert.Contains("색이 서로 다른 상대의 디지몬 1마리씩을 소멸시킨다", Ko(mon.EffectText));
        var (state, engine) = NewMainPhase();
        var cols = new[] { CardColor.Red, CardColor.Blue, CardColor.Yellow, CardColor.Green, CardColor.Black, CardColor.Purple };
        var me = Put(state.Player, mon, cols.Select((c, i) => Digimon("B53-SRC" + i, 4, color: c)).ToArray());
        Put(state.DummyOpponent, Digimon("B53-R1", 5, color: CardColor.Red));
        Put(state.DummyOpponent, Digimon("B53-R2", 6, color: CardColor.Red));
        Put(state.DummyOpponent, Digimon("B53-B1", 5, color: CardColor.Blue));
        Put(state.DummyOpponent, Digimon("B53-W1", 5, color: CardColor.White));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, mon);
        Assert.Single(state.DummyOpponent.BattleArea);   // 레드 1 · 블루 1 · 화이트 1 — 레드 하나는 남는다
        Assert.Equal(CardColor.Red, state.DummyOpponent.BattleArea[0].TopCard.Color);
    }

    // =====================================================================
    // 2. AI가 고르는 레스트 / 액티브 대상
    // =====================================================================

    [Fact]
    public void SuspendOneDigimon_PrefersUnsuspendedOpponent_BT24_047()
    {
        var mon = Digimon(Uid("B53-BT24047-"), 4, "[On Play] [When Digivolving] You may suspend 1 Digimon. If this effect suspended your Digimon, 1 of your Digimon with [Avian] or [Bird] in any of its traits or the [Vortex Warriors] trait unsuspends. If this effect unsuspended, that Digimon may attack.", color: CardColor.Green);
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, mon);
        var big = Put(state.DummyOpponent, Digimon("B53-BIG", 6, dp: 12000));
        big.IsSuspended = true;
        var small = Put(state.DummyOpponent, Digimon("B53-SMALL", 4, dp: 4000));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, mon);
        Assert.True(small.IsSuspended, Logs(state));   // 전에는 DP가 가장 높은(이미 레스트인) 디지몬을 골라 아무 일도 없었다
    }

    [Fact]
    public void UnsuspendOneOfYourDigimon_PrefersOneThatCanAttack()
    {
        var opt = Card(Uid("B53-UNSUS-"), "[Main] Unsuspend 1 of your Digimon. Then, that Digimon may attack.", CardType.Option, 0, 1);
        var (state, engine) = NewMainPhase();
        var fresh = Put(state.Player, Digimon("B53-FRESH", 6, dp: 12000));
        fresh.IsSuspended = true; fresh.TurnPlayed = state.CurrentTurn;   // 이번 턴에 등장 — 어택할 수 없다
        var old = Put(state.Player, Digimon("B53-OLD", 5, dp: 6000));
        old.IsSuspended = true; old.TurnPlayed = 0;
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt);
        engine.RunDeclaredEffectAttack57();   // v0.57.0 효과로 선언한 어택은 행동이 끝날 때 카운터 · 블록 · 배틀
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);   // 어택할 수 있는 디지몬을 액티브로 해 어택
        Assert.True(fresh.IsSuspended, Logs(state));
    }

    // =====================================================================
    // 3. 시점
    // =====================================================================

    [Fact]
    public void SecurityChecked_DevaPlaysWithoutBattle_OncePerTurn_EX5_053()
    {
        var mon = Digimon(Uid("B53-EX5053-"), 6, "[Opponent's Turn] [Once Per Turn] When your security is checked, if that card is a Digimon card with the [Deva] trait, play it without battling and without paying the cost.", color: CardColor.Green);
        AllSupported(mon);
        Assert.Contains("배틀을 진행하지 않고", Ko(mon.EffectText));
        var (state, engine) = NewMainPhase();
        Put(state.DummyOpponent, mon);
        var deva = Digimon(Uid("B53-DEVA-"), 5, dp: 20000, traits: "Deva");
        state.DummyOpponent.SecurityStack.Insert(0, deva);
        var atk = Put(state.Player, Digimon("B53-ATK", 6, dp: 12000));
        atk.TurnPlayed = 0;
        engine.AttackSecurityOrDirect(atk);
        Assert.Contains(state.DummyOpponent.BattleArea, d => ReferenceEquals(d.TopCard, deva));
        Assert.Contains(atk, state.Player.BattleArea);   // 배틀하지 않았다 (DP 20000이었으면 졌다)
    }

    [Fact]
    public void ThisDigimonDeletesAnother_ByBattleAndByItsEffect_NotOthers_BT16_061()
    {
        const string inh = "[All Turns] [Once Per Turn] When this Digimon deletes another Digimon, you may play 1 card with the [X Antibody] or [SoC] trait and a play cost of 5 or less from your trash without paying the cost.";
        var src = Digimon(Uid("B53-BT16061-"), 5, inherited: inh, color: CardColor.Purple);
        AllSupported(src);
        Assert.Contains("이 디지몬이 다른 디지몬을 소멸시켰을 때", Ko(inh));
        CardData Xa() => Digimon(Uid("B53-XA-"), 3, playCost: 3, traits: "X Antibody");

        // 배틀로
        var (s1, e1) = NewMainPhase();
        var a = Put(s1.Player, Digimon("B53-TOP6", 6, dp: 12000), src);
        a.TurnPlayed = 0;
        s1.Player.Trash.Add(Xa());
        var o = Put(s1.DummyOpponent, Digimon("B53-O4", 4));
        o.IsSuspended = true;
        e1.AttackOpponentDigimon(a, o);
        Assert.Equal(2, s1.Player.BattleArea.Count);

        // 이 디지몬의 효과로
        var (s2, e2) = NewMainPhase();
        var top = Digimon(Uid("B53-KILLER-"), 6, "[When Digivolving] Delete 1 of your opponent's Digimon with 5000 DP or less.");
        var k = Put(s2.Player, top, src);
        s2.Player.Trash.Add(Xa());
        Put(s2.DummyOpponent, Digimon("B53-O3", 3));
        e2.RunEffects(EffectTrigger.WhenDigivolving, s2.Player, k, top);
        Assert.Equal(2, s2.Player.BattleArea.Count);

        // 다른 카드의 효과로 소멸하면 아니다
        var (s3, e3) = NewMainPhase();
        Put(s3.Player, Digimon("B53-TOP6B", 6), src);
        s3.Player.Trash.Add(Xa());
        var o3 = Put(s3.DummyOpponent, Digimon("B53-O3B", 3));
        var opt = Card(Uid("B53-OPT-"), "[Main] Delete 1 of your opponent's Digimon.", CardType.Option, 0, 1);
        e3.RunEffects(EffectTrigger.Main, s3.Player, null, opt);
        Assert.DoesNotContain(o3, s3.DummyOpponent.BattleArea);
        Assert.Single(s3.Player.BattleArea);
    }

    private const string Ex11062 = "[All Turns] When any Digimon suspend, by suspending this Tamer, if effects suspended those Digimon, ＜Draw 1＞ (Draw 1 card from your deck.) After, 1 of your Digimon with [Avian] or [Bird] in any of its traits in any of its traits or the [Vortex Warriors] trait gets +3000 DP until your opponent's turn ends.";

    [Fact]
    public void SuspendedByEffect_DrawAndDp_NotByAttack_EX11_062()
    {
        var tamer = Tamer(Uid("B53-EX11062-"), Ex11062 + "\r\n[Your Turn] While your opponent has no unsuspended Digimon, your ＜Vortex＞ can also attack players.", color: CardColor.Green);
        AllSupported(tamer);
        Assert.Contains("디지몬이 효과로 레스트했을 때", Ko(Ex11062));
        var (state, engine) = NewMainPhase();
        state.Player.Tamers.Add(tamer);
        var bird = Put(state.Player, Digimon("B53-BIRD", 5, dp: 7000, traits: "Avian"));
        var o = Put(state.DummyOpponent, Digimon("B53-O5", 5));
        var opt = Card(Uid("B53-SUSOPT-"), "[Main] Suspend 1 of your opponent's Digimon.", CardType.Option, 0, 1);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt);
        engine.RecalculateFieldBuffs();
        Assert.True(o.IsSuspended);
        Assert.Single(state.Player.Hand);
        Assert.Equal(10000, bird.GetCurrentDP());

        var (s2, e2) = NewMainPhase();
        s2.Player.Tamers.Add(tamer);
        var atk = Put(s2.Player, Digimon("B53-ATK5", 5));
        atk.TurnPlayed = 0;
        e2.AttackSecurityOrDirect(atk);   // 어택 선언의 레스트는 룰 (공식 Q&A BT17-089)
        Assert.Empty(s2.Player.Hand);
        Assert.Empty(s2.Player.SuspendedTamers);
    }

    [Fact]
    public void VortexCanAttackPlayers_WhenOpponentHasNoUnsuspendedDigimon_EX11_062()
    {
        var tamer = Tamer(Uid("B53-EX11062V-"), "[Your Turn] While your opponent has no unsuspended Digimon, your ＜Vortex＞ can also attack players.", color: CardColor.Green);
        AllSupported(tamer);
        var vortex = Digimon(Uid("B53-VTX-"), 5, "＜Vortex＞ (At the end of your turn, this Digimon may attack an opponent's Digimon. With this effect, it can attack the turn it was played.)", dp: 7000);
        foreach (bool withTamer in new[] { false, true })
        {
            var (state, engine) = NewMainPhase();
            if (withTamer) state.Player.Tamers.Add(tamer);
            var v = Put(state.Player, vortex);
            v.TurnPlayed = 0;
            engine.PassTurn();
            Assert.Equal(withTamer ? 4 : 5, state.DummyOpponent.SecurityStack.Count);
        }
    }

    // =====================================================================
    // 4. 비용
    // =====================================================================

    [Fact]
    public void DeleteAndTrashBreeding_PlayToBreedingForCost3_BT16_090()
    {
        var tamer = Tamer(Uid("B53-BT16090-"), "[Main] [Once Per Turn] By deleting 1 of your [Ukkomon] and trashing 1 of your Digimon in the breeding area, you may play 1 [BigUkkomon] from your hand to an empty space in your breeding area for a play cost of 3.", color: CardColor.Purple);
        AllSupported(tamer);
        Assert.Contains("육성 에어리어에 등장 코스트 3", Ko(tamer.EffectText));
        var (state, engine) = NewMainPhase(5);
        state.Player.Tamers.Add(tamer);
        var ukko = Put(state.Player, Digimon("B53-UKKO", 3, name: "Ukkomon"));
        var bred = Digimon("B53-BRED", 3);
        state.Player.BreedingArea = new CardInstance(bred, 1);
        var big = Digimon("B53-BIGUKKO", 6, name: "BigUkkomon", playCost: 10);
        state.Player.Hand.Add(big);
        Assert.True(engine.ActivateMainAbility(null, tamer).CanExecute, Logs(state));
        Assert.DoesNotContain(ukko, state.Player.BattleArea);
        Assert.Contains(bred, state.Player.Trash);
        Assert.Same(big, state.Player.BreedingArea?.TopCard);
        Assert.Equal(2, state.MemoryGauge);
    }

    [Fact]
    public void PlaceCardUnderSuchTraitDigimon_FlipOpponentSecurity_BT20_086()
    {
        var tamer = Tamer(Uid("B53-BT20086-"), "[Start of Your Main Phase] By placing 1 black Digimon card with the [Cyborg] or [Machine] trait with a play cost of 4 or less from your hand or trash at the bottom of your Digimon with such trait, flip your opponent's top face down security card face up.", color: CardColor.Black);
        AllSupported(tamer);
        var (state, engine) = NewMainPhase();
        state.Player.Tamers.Add(tamer);
        var cy = Put(state.Player, Digimon("B53-CY5", 5, color: CardColor.Black, traits: "Cyborg"));
        state.Player.Trash.Add(Digimon("B53-MACH3", 3, color: CardColor.Black, playCost: 3, traits: "Machine"));
        engine.RunEffects(EffectTrigger.StartOfYourMainPhase, state.Player, null, tamer);
        Assert.Single(cy.EvolutionSources);
        Assert.Single(state.DummyOpponent.FaceUpSecurity);
    }

    [Fact]
    public void PlaceOtherDigimonAsSecurity_ThenTrashOpponentTop_P_187()
    {
        const string text = "[When Digivolving] ＜Recovery +1 (Deck)＞ (Place the top card of your deck on top of your security stack.) Then, if DNA digivolving, by placing 1 other Digimon or Tamer as the top or bottom security card, trash your opponent's top security card.";
        var mon = Digimon(Uid("B53-P187-"), 6, text, color: CardColor.Purple);
        AllSupported(mon);
        Assert.Contains("다른 디지몬/테이머 1마리(명)를 시큐리티 위 또는 아래에 놓는 것으로", Ko(text));
    }

    [Fact]
    public void LinkAsCost_ThenUnsuspend_AndLinkedTriggerIsSeparateOncePerTurn_BT25_060()
    {
        var mon = Digimon(Uid("B53-BT25060-"), 6, "＜Link +1＞ (Add 1 to this Digimon's maximum links.)\r\n[When Digivolving] [When Attacking] [Once Per Turn] By linking 1 [Appmon] trait Digimon card from your hand or this Digimon's digivolution cards to this Digimon without paying the cost, 1 of your Digimon may unsuspend.\r\n[All Turns] [Once Per Turn] When this Digimon gets linked or unsuspends, until your turn ends, this Digimon gains ＜Piercing＞ (When this Digimon attacks and deletes an opponent's Digimon in battle, it checks security before the attack ends.) and ＜Blocker＞ (This Digimon can block in the blocker timing.), and your opponent's Digimon effects don't affect it.", color: CardColor.Green, traits: "Reboot (App Name)");
        AllSupported(mon);
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, mon);
        var other = Put(state.Player, Digimon("B53-OTHER5", 5));
        other.IsSuspended = true;
        var app = Digimon(Uid("B53-APP-"), 3, traits: "Search (App Name)");   // 영어 데이터의 어플몬 — 특징 "Appmon"이 없다
        state.Player.Hand.Add(app);
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, mon);
        engine.RecalculateFieldBuffs();
        Assert.Contains(app, me.LinkedCards);
        Assert.False(other.IsSuspended, Logs(state));
        Assert.True(CardScripts.HasKeyword(me, Keyword.Piercing), Logs(state));   // 같은 카드의 다른 [턴에 1회] 효과
        Assert.True(CardScripts.HasKeyword(me, Keyword.Blocker));
    }

    [Fact]
    public void AppmonTrait_MatchesAppNameTraits()
    {
        Assert.True(TargetSpec.HasTrait(Digimon("B53-APPN", 3, traits: "Search (App Name)"), "Appmon"));
        Assert.False(TargetSpec.HasTrait(Digimon("B53-NOAPP", 3, traits: "Hero"), "Appmon"));
    }

    // =====================================================================
    // 5. 한글 · 캐시
    // =====================================================================

    [Fact]
    public void Korean_SameWithAndWithoutMemo_Batch53()
    {
        var clauses = new[]
        {
            Bt16070, Ex11062,
            "[Main] [Once Per Turn] By deleting 1 of your [Ukkomon] and trashing 1 of your Digimon in the breeding area, you may play 1 [BigUkkomon] from your hand to an empty space in your breeding area for a play cost of 3.",
            "[Opponent's Turn] [Once Per Turn] When your security is checked, if that card is a Digimon card with the [Deva] trait, play it without battling and without paying the cost.",
        };
        var cached = clauses.Select(Ko).ToList();
        Assert.DoesNotContain("<null>", cached);
        KoreanEffectRenderer.MemoEnabled52 = false;
        try { Assert.Equal(clauses.Select(Ko).ToList(), cached); }
        finally { KoreanEffectRenderer.MemoEnabled52 = true; }
    }
}
