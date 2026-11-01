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
/// v0.71.0 미지원 문장 45차 묶음: 겹쳐진 카드마다 앞/뒷면 (같은 CardData 객체) · 약칭 특징의 부분 일치 바로잡음 (종합 룰 2-3-2-3 · 2-3-2-4) ·
/// 「X」만 적힌 명칭은 명칭 일치 (종합 룰 2-3-1-2 · 공식 Q&amp;A) + 〈룰〉 명칭 · 필드의 디지몬 + 트래시의 카드로 조그레스 진화 (BT18-015 · BT18-073 · EX11-059) ·
/// BT16-091 "The Digimon this effect DNA digivolved may gain ~ and attack a player"
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch71Tests
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

    private static CardData Digimon(string id, int level, string main = "", string? name = null, CardColor color = CardColor.Blue, int dp = -1, int playCost = 3, int evoCost = 1, params string[] traits) =>
        Card(id, main, CardType.Digimon, level, playCost, color, dp, name, evoCost, "", traits);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8, int security = 5)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B71-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B71-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < security; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B71-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B71-OSEC" + i, 3, color: CardColor.White, dp: 1000));
        }
        return (state, new GoldfishEngine(state));
    }

    private static CardInstance Put(PlayerZones zones, CardData card, bool suspended = false, params CardData[] sources)
    {
        var d = new CardInstance(card, 1) { IsOwnersTurn = true, IsSuspended = suspended };
        foreach (var s in sources) d.AddBottomSource(s);
        zones.BattleArea.Add(d);
        return d;
    }

    private static string Uid(string p) => p + Guid.NewGuid().ToString("N")[..8];

    private static void AllSupported(CardData card) =>
        Assert.All(EffectSupport.For(card).Clauses, c => Assert.True(c.Support == ClauseSupport.Supported, c.Text + " | " + c.Note));

    private static string Ko(string clause) => KoreanEffectRenderer.RenderClause(clause) ?? "<null>";

    private static void WithKoreanCards(IEnumerable<KoreanCard> extra, Action run)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dtcg_b71_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        var file = System.IO.Path.Combine(dir, "cards_ko.json");
        System.IO.File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(KoreanCardData.All.Concat(extra).ToList()));
        try { KoreanCardData.Reload(file); run(); }
        finally { KoreanCardData.Reload(); try { System.IO.Directory.Delete(dir, true); } catch { } }
    }

    // =====================================================================
    // 1. 겹쳐진 카드마다 앞/뒷면 (같은 CardData 객체를 여러 장이 쓸 때)
    // =====================================================================

    [Fact]
    public void SameObjectStackedTwice_FaceStatePerPhysicalCard()
    {
        var src = Digimon(Uid("B71-SRC-"), 3);
        var host = new CardInstance(Digimon(Uid("B71-HOST-"), 5), 1);
        var down = host.AddBottomSourceFaceDown(src);
        Assert.Equal(1, host.FaceDownSourceCount);
        // 같은 객체를 앞면으로 하나 더 — 전에는 FaceDownSources.Remove가 이미 놓인 뒷면 카드까지 앞면으로 바꿨다
        var up1 = host.AddBottomSource(src);
        var up2 = host.AddBottomSource(src);
        Assert.Same(src, up1);
        Assert.NotSame(src, up2);   // 이미 겹쳐진 객체 → 복사본
        Assert.Equal(3, host.EvolutionSources.Count);
        Assert.Equal(1, host.FaceDownSourceCount);
        Assert.True(host.IsFaceDown(down));
        Assert.Equal(2, host.FaceUpSources.Count());
        host.AddTopSource(down);   // 뒷면 카드 객체를 앞면으로 맨 위에 → 복사본, 원래 뒷면 카드는 그대로
        Assert.Equal(1, host.FaceDownSourceCount);
        Assert.Equal(4, host.EvolutionSources.Count);
    }

    [Fact]
    public void Evolve_IntoObjectAlreadyInSources_KeepsThatSourceFace()
    {
        var src = Digimon(Uid("B71-EVSRC-"), 4);
        var host = new CardInstance(Digimon(Uid("B71-EVHOST-"), 3), 1);
        var down = host.AddBottomSourceFaceDown(src);
        host.Evolve(down);   // 같은 객체로 진화 → 맨 위는 복사본, 진화원의 뒷면 카드는 뒷면 그대로
        Assert.NotSame(down, host.TopCard);
        Assert.False(host.TopIsFaceDown);
        Assert.Equal(1, host.FaceDownSourceCount);
    }

    // =====================================================================
    // 2. 약칭 특징의 부분 일치 (종합 룰 2-3-2-3 · 2-3-2-4)
    // =====================================================================

    [Fact]
    public void AbbreviationTrait_NoLongerMatchesInsideOtherTraits()
    {
        var mega = Digimon(Uid("B71-MEGA-"), 6, traits: new[] { "Mega", "Flame" });
        var abadin = Digimon(Uid("B71-ABD-"), 4, traits: "Abadin Electronics");
        var squad = Card(Uid("B71-DS-"), "", CardType.Tamer, traits: "DATA SQUAD");
        var hudie = Digimon(Uid("B71-HUD-"), 4, traits: "Hudie/CS");
        var arch = Digimon(Uid("B71-ARCH-"), 5, traits: "Archangel");
        Assert.False(TargetSpec.HasTrait(mega, "ME"));
        Assert.True(TargetSpec.HasTrait(Digimon(Uid("B71-ME-"), 4, traits: "ME"), "ME"));
        Assert.False(TargetSpec.HasTrait(abadin, "CS"));
        Assert.True(TargetSpec.HasTrait(hudie, "CS"));        // 한 칸에 "/"로 적힌 특징은 나눠 본다 (2-3-2-2)
        Assert.True(TargetSpec.HasTrait(hudie, "Hudie"));
        Assert.False(TargetSpec.HasTrait(squad, "Data"));
        Assert.True(TargetSpec.HasTrait(Digimon(Uid("B71-DATA-"), 4, traits: "Data"), "Data"));
        Assert.True(TargetSpec.HasTrait(arch, "Angel"));      // 일반 특징은 전과 같다
        Assert.False(TargetSpec.HasTrait(Digimon(Uid("B71-SOC-"), 4, traits: "Social"), "SoC"));
    }

    [Fact]
    public void MeTraitPick_DoesNotOfferMegaDigimon()
    {
        var opt = Card(Uid("B71-METR-"), "[Main] Return 1 [ME] trait Digimon card from your trash to the hand.", color: CardColor.Blue, playCost: 0);
        AllSupported(opt);
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon(Uid("B71-BLUE-"), 3, color: CardColor.Blue));
        var mega = Digimon(Uid("B71-TMEGA-"), 6, traits: "Mega");
        state.Player.Trash.Add(mega);
        state.Player.Hand.Add(opt);
        var r = engine.PlayCard(opt);
        Assert.True(r.CanExecute, r.Reason);
        Assert.Contains(mega, state.Player.Trash);   // 전에는 「Mega」 안의 "ME"로 패에 넣었다
        Assert.DoesNotContain(mega, state.Player.Hand);
    }

    // =====================================================================
    // 3. 「X」만 적힌 명칭은 명칭 일치 (종합 룰 2-3-1-2 · 공식 Q&A) · 〈룰〉 명칭
    // =====================================================================

    [Fact]
    public void BareName_PlayPick_IsExactName()
    {
        var opt = Card(Uid("B71-AGU-"), "[Main] You may play 1 [Agumon] from your trash without paying the cost.", color: CardColor.Red, playCost: 0);
        AllSupported(opt);
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon(Uid("B71-RED-"), 3, color: CardColor.Red));
        var bond = Digimon(Uid("B71-BOND-"), 3, name: "Agumon - Bond of Bravery", color: CardColor.Red);
        state.Player.Trash.Add(bond);
        state.Player.Hand.Add(opt);
        var r1 = engine.PlayCard(opt);
        Assert.True(r1.CanExecute, r1.Reason);
        Assert.Contains(bond, state.Player.Trash);   // 전에는 명칭에 "Agumon"을 포함해 등장했다
        var agumon = Digimon(Uid("B71-AGU2-"), 3, name: "Agumon", color: CardColor.Red);
        var opt2 = Card(Uid("B71-AGUB-"), "[Main] You may play 1 [Agumon] from your trash without paying the cost.", color: CardColor.Red, playCost: 0);
        var (state2, engine2) = NewMainPhase();
        Put(state2.Player, Digimon(Uid("B71-RED2-"), 3, color: CardColor.Red));
        state2.Player.Trash.Add(agumon);
        state2.Player.Hand.Add(opt2);
        var r2 = engine2.PlayCard(opt2);
        Assert.True(r2.CanExecute, r2.Reason);
        Assert.True(state2.Player.BattleArea.Any(d => d.TopCard == agumon), string.Join(" | ", state2.ActionLogs));
    }

    [Fact]
    public void NameRuleAliases_AndJointTamers()
    {
        Assert.True(CardNames.Is(new CardData { CardId = "EX4-048", Name = "Gaiomon", Type = CardType.Digimon }, "Greymon"));
        Assert.True(CardNames.Is(new CardData { CardId = "EX5-070", Name = "X Antibody Proto Form", Type = CardType.Option }, "X Antibody"));
        Assert.True(CardNames.Is(new CardData { CardId = "B71-JOINT", Name = "Takuya Kanbara & Koji Minamoto", Type = CardType.Tamer }, "Koji Minamoto"));
        Assert.False(CardNames.Is(new CardData { CardId = "B71-GX", Name = "Garurumon (X Antibody)", Type = CardType.Digimon }, "Garurumon"));   // 공식 Q&A
    }

    [Fact]
    public void XAntibodyInSources_IsCardNameNotTrait()
    {
        var d = Digimon(Uid("B71-XA-"), 5, "[When Digivolving] If [X Antibody] is in this Digimon's digivolution cards, gain 1 memory.");
        AllSupported(d);
        foreach (var (src, expect) in new[]
        {
            (Digimon(Uid("B71-XTR-"), 4, name: "Gabumon (X Antibody)", traits: "X Antibody"), 3),   // 특징 · 명칭 포함은 아니다 (공식 Q&A)
            (Card(Uid("B71-XAN-"), "", CardType.Option, name: "X Antibody"), 4),
        })
        {
            var (state, engine) = NewMainPhase(memory: 3);
            var inst = Put(state.Player, d, false, src);
            engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, inst, d);
            Assert.Equal(expect, state.MemoryGauge);
        }
    }

    // =====================================================================
    // 4. 필드의 디지몬 + 트래시의 카드로 조그레스 진화 (BT18-015 · BT18-073 · EX11-059)
    // =====================================================================

    [Fact]
    public void DnaWithFieldAndTrashCard()
    {
        string id = Uid("B71-MIL-");
        WithKoreanCards(new[] { new KoreanCard { CardId = id, Name = "테스트밀레니엄몬", MainText = "〔조그레스〕퍼플 Lv.5+블랙 Lv.6 : 코스트 0" } }, () =>
        {
            var kimera = Digimon(Uid("B71-KIM-"), 5, "[On Deletion] You may DNA digivolve 1 of your [B71Machine] in play and 1 [B71Kimera] in the trash into [B71Millennium] in the hand.", name: "B71Kimera", color: CardColor.Purple);
            AllSupported(kimera);
            Assert.Equal("【소멸 시】 자신의 「B71Machine」 1마리와 트래시의 「B71Kimera」 1장으로 패의 「B71Millennium」으로 조그레스 진화할 수 있다.", Ko(kimera.EffectText).Replace("「B71Millennium」로", "「B71Millennium」으로"));
            var (state, engine) = NewMainPhase(memory: 3);
            var machine = Put(state.Player, Digimon(Uid("B71-MAC-"), 6, name: "B71Machine", color: CardColor.Black));
            var mil = Digimon(id, 7, name: "B71Millennium", color: CardColor.Purple);
            state.Player.Hand.Add(mil);
            state.Player.Trash.Add(kimera);   // 소멸해 트래시에 있는 이 카드
            engine.RunEffects(EffectTrigger.OnDeletion, state.Player, null, kimera);
            var r = state.Player.BattleArea.FirstOrDefault(x => x.TopCard == mil);
            Assert.NotNull(r);
            Assert.DoesNotContain(kimera, state.Player.Trash);
            Assert.Contains(kimera, r!.EvolutionSources);
            Assert.DoesNotContain(machine, state.Player.BattleArea);
        });
    }

    [Fact]
    public void DnaWithTrashCard_RequirementMustMatch()
    {
        string id = Uid("B71-NSO7-");
        WithKoreanCards(new[] { new KoreanCard { CardId = id, Name = "테스트NSo", MainText = "〔조그레스〕퍼플 Lv.5+옐로 Lv.5 : 코스트 0" } }, () =>
        {
            var tamer = Card(Uid("B71-REINA-"), "[All Turns] When any of your [NSo] trait Digimon are deleted, by suspending this Tamer, 1 of your [NSo] trait Digimon and 1 [NSo] trait Digimon card in the trash may DNA digivolve into a Digimon card with the [NSo] trait in the hand.", CardType.Tamer, color: CardColor.Yellow);
            AllSupported(tamer);
            Assert.Contains("트래시의 특징으로 「NSo」를 가진 디지몬 카드 1장으로 패의 특징으로 「NSo」를 가진 디지몬 카드로 조그레스 진화할 수 있다.", Ko(tamer.EffectText));
            var (state, engine) = NewMainPhase(memory: 3);
            engine.AddTamer(state.Player, tamer);
            Put(state.Player, Digimon(Uid("B71-P5-"), 5, color: CardColor.Purple, traits: "NSo"));
            var wrong = Digimon(Uid("B71-B5-"), 5, color: CardColor.Blue, traits: "NSo");   // 색이 조건에 맞지 않는다
            state.Player.Trash.Add(wrong);
            var into = Digimon(id, 6, color: CardColor.Yellow, traits: "NSo");
            state.Player.Hand.Add(into);
            var dead = Put(state.Player, Digimon(Uid("B71-DEAD-"), 4, traits: "NSo"));
            engine.FireForSide(EffectTrigger.AllyDigimonDeleted, state.Player, dead);
            Assert.Contains(into, state.Player.Hand);   // 조그레스 조건 불만족 — 하지 않는다 (공식 Q&A BT18-015 Q1)
            Assert.Contains(wrong, state.Player.Trash);
            var ok = Digimon(Uid("B71-Y5-"), 5, color: CardColor.Yellow, traits: "NSo");
            state.Player.Trash.Add(ok);
            state.Player.SuspendedTamers.Clear();
            engine.FireForSide(EffectTrigger.AllyDigimonDeleted, state.Player, dead);
            Assert.Contains(state.Player.BattleArea, d => d.TopCard == into && d.EvolutionSources.Contains(ok));
        });
    }

    // =====================================================================
    // 5. BT16-091 "The Digimon this effect DNA digivolved may gain ~ for the turn and attack a player"
    // =====================================================================

    [Fact]
    public void DnaDigivolvedMayGainAndAttack_OnlyTheDnaDigimon()
    {
        string id = Uid("B71-SILPH-");
        WithKoreanCards(new[] { new KoreanCard { CardId = id, Name = "테스트실피몬", MainText = "〔조그레스〕레드 Lv.4+옐로 Lv.4 : 코스트 0" } }, () =>
        {
            string text = "[Main] You may play 1 [B71Aquila] or [B71Gato] from your hand without paying the cost. Then, 2 of your Digimon may DNA digivolve into a Digimon card in your hand. The Digimon this effect DNA digivolved may gain ＜Security A. +1＞ (This Digimon checks 1 additional security card.) for the turn and attack a player.";
            var opt = Card(Uid("B71-BSD-"), text, color: CardColor.Red, playCost: 0);
            AllSupported(opt);
            Assert.Contains("이 효과로 조그레스 진화한 디지몬은 《시큐리티 어택 +1》", Ko(opt.EffectText));
            // 조그레스 진화하지 않으면 등장한 디지몬은 얻지 않는다 (공식 Q&A BT16-091 Q1)
            var (state, engine) = NewMainPhase();
            Put(state.Player, Digimon(Uid("B71-R0-"), 3, color: CardColor.Red));
            var gato = Digimon(Uid("B71-GATO-"), 4, name: "B71Gato", color: CardColor.Yellow);
            state.Player.Hand.Add(gato); state.Player.Hand.Add(opt);
            Assert.True(engine.PlayCard(opt).CanExecute);
            var g = state.Player.BattleArea.Single(d => d.TopCard == gato);
            Assert.Equal(0, g.TemporarySecurityAttackBonus);
            // 조그레스 진화하면 그 디지몬이 얻고 어택
            var (s2, e2) = NewMainPhase();
            Put(s2.Player, Digimon(Uid("B71-R4-"), 4, color: CardColor.Red));
            var opt2 = Card(Uid("B71-BSD2-"), text, color: CardColor.Red, playCost: 0);
            var gato2 = Digimon(Uid("B71-GATO2-"), 4, name: "B71Gato", color: CardColor.Yellow);
            var dna = Digimon(id, 5, color: CardColor.Red, dp: 7000);
            s2.Player.Hand.Add(gato2); s2.Player.Hand.Add(dna); s2.Player.Hand.Add(opt2);
            int sec = s2.DummyOpponent.SecurityStack.Count;
            Assert.True(e2.PlayCard(opt2).CanExecute);
            Assert.Contains(s2.Player.BattleArea, d => d.TopCard == dna);
            Assert.True(s2.DummyOpponent.SecurityStack.Count <= sec - 2 || s2.Player.BattleArea.Single(d => d.TopCard == dna).TemporarySecurityAttackBonus == 1, string.Join(" | ", s2.ActionLogs));
        });
    }
}
