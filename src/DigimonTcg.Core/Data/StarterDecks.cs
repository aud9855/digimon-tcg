using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Data;

public record RecommendedDeckPreset(
    string Id,
    string Name,
    string ArchetypeBadge,
    string Description,
    CardColor PrimaryColor,
    string BossCardId,
    Func<(List<CardData> MainDeck, List<CardData> DigitamaDeck)> DeckFactory);

public static class StarterDecks
{
    public static List<CardData> GetGaiaRedSampleCards() =>
        GetAllCatalogCards().Where(c => c.CardId.StartsWith("ST1-") || c.Color == CardColor.Red).ToList();

    public static List<CardData> GetCocytusBlueSampleCards() =>
        GetAllCatalogCards().Where(c => c.CardId.StartsWith("ST2-") || c.Color == CardColor.Blue).ToList();

    /// <summary>
    /// 전체 7색상 통합 공식 카드 도감 (ST-1 ~ ST-6 스타터 전종 + BT-1 ~ BT-5 핵심 메타/궁극체/초궁극체)
    /// </summary>
    // 내장 한글 카드(= 대전 엔진이 효과를 구현한 카드) 번호 목록
    private static readonly Lazy<HashSet<string>> BuiltInCardIds =
        new(() => GetAllCatalogCards().Select(c => c.CardId).ToHashSet(StringComparer.Ordinal));

    public static bool IsBuiltInCard(string cardId) => BuiltInCardIds.Value.Contains(cardId);

    // 내장 카드 중 효과 문장의 전부 또는 일부가 대전에서 아직 동작하지 않는 카드 (도감 · 상세에 "일부 미구현" 표시)
    // - 일부만 동작: BT5-086 (≪진격≫은 동작, 상대 효과에서 지키는 [서로의 턴] 효과는 아직 문장만)
    // (ST4-13 · ST4-14 · ST5-14 · ST6-14 · BT2-021 · BT2-032는 효과 엔진 5단계에서 구현)
    private static readonly HashSet<string> TextOnlyEffectCardIds = new(StringComparer.Ordinal) { "BT5-086" };

    public static bool IsTextOnlyEffectCard(string cardId) => TextOnlyEffectCardIds.Contains(cardId);

    // 내장 한글 카드의 공식 영어 이름 (DigimonCard.io 공개 API의 카드 번호별 name — 2026-10 기준).
    // 효과 문장(영어)의 이름 조건 "[Greymon]" · "with [X] in its name" 등은 Name(한글)과 이 이름을 함께 본다 (Models.CardNames).
    private static readonly Dictionary<string, string> BuiltInEnglishNames = new(StringComparer.Ordinal)
    {
        ["BT1-001"] = "Yokomon", ["BT1-003"] = "Upamon", ["BT1-009"] = "Monodramon", ["BT1-010"] = "Agumon",
        ["BT1-015"] = "Greymon", ["BT1-020"] = "Groundramon", ["BT1-021"] = "MetalGreymon", ["BT1-025"] = "WarGreymon",
        ["BT1-029"] = "Gabumon", ["BT1-034"] = "Ikkakumon", ["BT1-040"] = "WereGarurumon", ["BT1-048"] = "Patamon",
        ["BT1-060"] = "MagnaAngemon", ["BT1-067"] = "Palmon", ["BT1-074"] = "Togemon", ["BT1-079"] = "Lillymon",
        ["BT1-084"] = "Omnimon", ["BT2-009"] = "Guilmon", ["BT2-020"] = "Gallantmon", ["BT2-021"] = "Veemon",
        ["BT2-032"] = "UlforceVeedramon", ["BT2-039"] = "Magnadramon", ["BT2-052"] = "Hagurumon", ["BT2-059"] = "Kurisarimon",
        ["BT2-065"] = "WarGreymon", ["BT2-068"] = "Impmon", ["BT2-074"] = "Devimon", ["BT2-080"] = "Piedmon",
        ["BT3-031"] = "Imperialdramon Dragon Mode", ["BT3-112"] = "Omnimon Alter-S", ["BT5-086"] = "Omnimon", ["ST1-01"] = "Koromon",
        ["ST1-02"] = "Biyomon", ["ST1-03"] = "Agumon", ["ST1-04"] = "Dracomon", ["ST1-05"] = "Birdramon",
        ["ST1-06"] = "Coredramon", ["ST1-07"] = "Greymon", ["ST1-08"] = "Garudamon", ["ST1-09"] = "MetalGreymon",
        ["ST1-10"] = "Phoenixmon", ["ST1-11"] = "WarGreymon", ["ST1-12"] = "Tai Kamiya", ["ST1-13"] = "Shadow Wing",
        ["ST1-14"] = "Starlight Explosion", ["ST1-15"] = "Giga Destroyer", ["ST1-16"] = "Gaia Force", ["ST2-01"] = "Tsunomon",
        ["ST2-02"] = "Gomamon", ["ST2-03"] = "Gabumon", ["ST2-04"] = "Bearmon", ["ST2-05"] = "Ikkakumon",
        ["ST2-06"] = "Garurumon", ["ST2-07"] = "Grizzlymon", ["ST2-08"] = "WereGarurumon", ["ST2-09"] = "Zudomon",
        ["ST2-10"] = "Plesiomon", ["ST2-11"] = "MetalGarurumon", ["ST2-12"] = "Matt Ishida", ["ST2-13"] = "Hammer Spark",
        ["ST2-14"] = "Sorrow Blue", ["ST2-15"] = "Kaiser Nail", ["ST2-16"] = "Cocytus Breath", ["ST3-01"] = "Tokomon",
        ["ST3-02"] = "Salamon", ["ST3-03"] = "Tapirmon", ["ST3-04"] = "Patamon", ["ST3-05"] = "Angemon",
        ["ST3-06"] = "Gatomon", ["ST3-07"] = "Unimon", ["ST3-08"] = "MagnaAngemon", ["ST3-09"] = "Angewomon",
        ["ST3-10"] = "Magnadramon", ["ST3-11"] = "Seraphimon", ["ST3-12"] = "T.K. Takaishi", ["ST3-13"] = "Heaven's Gate",
        ["ST3-16"] = "Seven Heavens", ["ST4-01"] = "Motimon", ["ST4-02"] = "Floramon", ["ST4-03"] = "Tentomon",
        ["ST4-04"] = "Palmon", ["ST4-05"] = "Kunemon", ["ST4-06"] = "Togemon", ["ST4-07"] = "Kuwagamon",
        ["ST4-08"] = "Kabuterimon", ["ST4-09"] = "Okuwamon", ["ST4-10"] = "Lillymon", ["ST4-11"] = "MegaKabuterimon",
        ["ST4-12"] = "Rosemon", ["ST4-13"] = "HerculesKabuterimon", ["ST4-14"] = "Izzy Izumi", ["ST4-16"] = "Electro Shocker",
        ["ST5-01"] = "Kapurimon", ["ST5-02"] = "Jazamon", ["ST5-03"] = "Agumon", ["ST5-04"] = "ToyAgumon",
        ["ST5-05"] = "Commandramon", ["ST5-06"] = "Greymon", ["ST5-07"] = "Jazardmon", ["ST5-08"] = "DarkTyrannomon",
        ["ST5-09"] = "MetalGreymon", ["ST5-11"] = "Megadramon", ["ST5-12"] = "Machinedramon", ["ST5-14"] = "Tai Kamiya",
        ["ST5-15"] = "Laser Eye", ["ST5-16"] = "Dark Side Attack", ["ST6-01"] = "Pagumon", ["ST6-02"] = "DemiDevimon",
        ["ST6-03"] = "Gabumon", ["ST6-04"] = "Dracmon", ["ST6-05"] = "Elecmon", ["ST6-06"] = "Garurumon",
        ["ST6-07"] = "Youkomon", ["ST6-08"] = "Devimon", ["ST6-09"] = "Kyukimon", ["ST6-10"] = "SkullSatamon",
        ["ST6-11"] = "WereGarurumon", ["ST6-14"] = "Matt Ishida", ["ST6-15"] = "Death Claw", ["ST6-16"] = "Nail Bone",
    };

    /// <summary>내장 카드의 공식 영어 이름 (내장 카드가 아니면 null)</summary>
    public static string? EnglishNameOf(string cardId) => BuiltInEnglishNames.TryGetValue(cardId, out var n) ? n : null;

    public static List<CardData> GetAllCatalogCards()
    {
        var cards = new List<CardData>
        {
            // 공식 한글판 카드 이미지(이름 · 효과 문장) + DigimonCard.io 실제 수치(코스트 · DP · 진화 조건 · 등급) 기준으로 생성
            new CardData
            {
                CardId = "ST1-01", Name = "코로몬", Type = CardType.Digitama, Color = CardColor.Red, Level = 2, Rarity = "U",
                DP = 0,
                EffectText = "",
                InheritedEffectText = "[자신의 턴] 이 디지몬이 진화원을 4장 이상 가지는 동안, 이 디지몬의 DP를 +1000 한다."
            },
            new CardData
            {
                CardId = "BT1-001", Name = "어니몬", Type = CardType.Digitama, Color = CardColor.Red, Level = 2, Rarity = "R",
                DP = 0,
                EffectText = "",
                InheritedEffectText = "[어택 시] 상대의 디지몬에 어택했을 때, 이 턴 동안, 이 디지몬의 DP를 +1000 한다."
            },
            new CardData
            {
                CardId = "ST1-02", Name = "피요몬", Type = CardType.Digimon, Color = CardColor.Red, Level = 3, Rarity = "C",
                PlayCost = 2, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Red, DP = 3000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST1-03", Name = "아구몬", Type = CardType.Digimon, Color = CardColor.Red, Level = 3, Rarity = "U",
                PlayCost = 3, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Red, DP = 2000,
                InheritedDpBonus = 1000,
                EffectText = "",
                InheritedEffectText = "[자신의 턴] 이 디지몬의 DP를 +1000 한다."
            },
            new CardData
            {
                CardId = "ST1-04", Name = "드라코몬", Type = CardType.Digimon, Color = CardColor.Red, Level = 3, Rarity = "C",
                PlayCost = 3, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Red, DP = 4000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT1-009", Name = "모노드라몬", Type = CardType.Digimon, Color = CardColor.Red, Level = 3, Rarity = "C",
                PlayCost = 2, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Red, DP = 3000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT1-010", Name = "아구몬", Type = CardType.Digimon, Color = CardColor.Red, Level = 3, Rarity = "R",
                PlayCost = 3, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Red, DP = 2000,
                EffectText = "[등장 시] 자신의 덱 위에서 5장 오픈한다. 그 중 테이머 카드 1장을 패에 추가한다. 남은 카드는 원하는 순서대로 덱 아래로 되돌린다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT2-009", Name = "길몬", Type = CardType.Digimon, Color = CardColor.Red, Level = 3, Rarity = "C",
                PlayCost = 3, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Red, DP = 3000,
                EffectText = "",
                InheritedEffectText = "[자신의 턴] 상대의 트래시가 5장 이상일 동안, 이 디지몬의 DP를 +1000 한다."
            },
            new CardData
            {
                CardId = "ST1-05", Name = "버드라몬", Type = CardType.Digimon, Color = CardColor.Red, Level = 4, Rarity = "C",
                PlayCost = 4, EvoCost = 2, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Red, DP = 5000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST1-06", Name = "코어드라몬", Type = CardType.Digimon, Color = CardColor.Red, Level = 4, Rarity = "C",
                PlayCost = 5, EvoCost = 2, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Red, DP = 6000,
                IsBlocker = true,
                EffectText = "≪블로커≫ (상대의 디지몬이 어택했을 때, 이 디지몬이 액티브 상태라면 레스트시키는 것으로 어택의 대상을 이 디지몬으로 한다)\n[어택 시] 메모리를 -2 한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST1-07", Name = "그레이몬", Type = CardType.Digimon, Color = CardColor.Red, Level = 4, Rarity = "U",
                PlayCost = 5, EvoCost = 2, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Red, DP = 4000,
                InheritedSecurityAttackBonus = 1,
                EffectText = "",
                InheritedEffectText = "≪시큐리티 어택 +1≫ (이 디지몬이 체크하는 시큐리티의 매수 +1)"
            },
            new CardData
            {
                CardId = "BT1-015", Name = "그레이몬", Type = CardType.Digimon, Color = CardColor.Red, Level = 4, Rarity = "U",
                PlayCost = 4, EvoCost = 2, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Red, DP = 4000,
                InheritedDpBonus = 2000,
                EffectText = "",
                InheritedEffectText = "[자신의 턴] 이 디지몬의 DP를 +2000 한다."
            },
            new CardData
            {
                CardId = "ST1-08", Name = "가루다몬", Type = CardType.Digimon, Color = CardColor.Red, Level = 5, Rarity = "U",
                PlayCost = 6, EvoCost = 3, EvoRequirementLevel = 4, EvoRequirementColor = CardColor.Red, DP = 7000,
                EffectText = "[진화 시] 이 턴 동안, 자신의 디지몬 1마리의 DP를 +3000 한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST1-09", Name = "메탈그레이몬", Type = CardType.Digimon, Color = CardColor.Red, Level = 5, Rarity = "R",
                PlayCost = 7, EvoCost = 3, EvoRequirementLevel = 4, EvoRequirementColor = CardColor.Red, DP = 7000,
                EffectText = "",
                InheritedEffectText = "[자신의 턴] 이 디지몬이 블록당했을 때, 메모리를 +3 한다."
            },
            new CardData
            {
                CardId = "BT1-020", Name = "그라운드라몬", Type = CardType.Digimon, Color = CardColor.Red, Level = 5, Rarity = "U",
                PlayCost = 5, EvoCost = 2, EvoRequirementLevel = 4, EvoRequirementColor = CardColor.Red, DP = 6000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT1-021", Name = "메탈그레이몬", Type = CardType.Digimon, Color = CardColor.Red, Level = 5, Rarity = "U",
                PlayCost = 6, EvoCost = 3, EvoRequirementLevel = 4, EvoRequirementColor = CardColor.Red, DP = 7000,
                EffectText = "[어택 시] 메모리를 +3 한다. 이 턴 종료 시, 메모리를 -3 한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST1-10", Name = "페닉스몬", Type = CardType.Digimon, Color = CardColor.Red, Level = 6, Rarity = "R",
                PlayCost = 10, EvoCost = 2, EvoRequirementLevel = 5, EvoRequirementColor = CardColor.Red, DP = 12000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST1-11", Name = "워그레이몬", Type = CardType.Digimon, Color = CardColor.Red, Level = 6, Rarity = "SR",
                PlayCost = 12, EvoCost = 4, EvoRequirementLevel = 5, EvoRequirementColor = CardColor.Red, DP = 12000,
                EffectText = "[자신의 턴] 이 디지몬이 가지는 진화원 2장마다, 이 디지몬은 ≪시큐리티 어택 +1≫ (이 디지몬이 체크하는 시큐리티의 매수 +1)을 얻는다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT1-025", Name = "워그레이몬", Type = CardType.Digimon, Color = CardColor.Red, Level = 6, Rarity = "SR",
                PlayCost = 12, EvoCost = 3, EvoRequirementLevel = 5, EvoRequirementColor = CardColor.Red, DP = 11000,
                EffectText = "[진화 시] 이 턴 동안, 이 디지몬은 ≪시큐리티 어택 +1≫ (이 디지몬이 체크하는 시큐리티의 매수 +1)을 얻는다.\n[자신의 턴] 이 디지몬이 체크한 옵션 카드의 [시큐리티] 효과는 발휘하지 않는다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT2-020", Name = "듀크몬", Type = CardType.Digimon, Color = CardColor.Red, Level = 6, Rarity = "SR",
                PlayCost = 12, EvoCost = 4, EvoRequirementLevel = 5, EvoRequirementColor = CardColor.Red, DP = 11000,
                EffectText = "[진화 시] 레드인 자신의 테이머가 있을 때, DP 6000 이하인 상대의 디지몬 1마리를 소멸시킨다.\n[어택 시] 상대의 트래시 10장마다, 상대의 시큐리티를 1장 파기한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST1-12", Name = "신태일", Type = CardType.Tamer, Color = CardColor.Red, Level = 0, Rarity = "R",
                PlayCost = 2,
                EffectText = "[자신의 턴] 자신의 디지몬 전부의 DP를 +1000 한다.\n[시큐리티] 이 카드를 코스트를 지불하지 않고 등장시킨다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST1-13", Name = "섀도 윙", Type = CardType.Option, Color = CardColor.Red, Level = 0, Rarity = "C",
                PlayCost = 1,
                EffectText = "[메인] 이 턴 동안, 자신의 디지몬 1마리의 DP를 +3000 한다.\n[시큐리티] 다음 자신의 턴 종료 시까지 자신의 디지몬 전부는, ≪시큐리티 어택 +1≫ (이 디지몬이 체크하는 시큐리티의 매수 +1)을 얻는다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST1-14", Name = "스타라이트 익스플로전", Type = CardType.Option, Color = CardColor.Red, Level = 0, Rarity = "C",
                PlayCost = 2,
                EffectText = "[메인] 다음 상대의 턴 종료 시까지, 자신의 시큐리티 디지몬 전부의 DP를 +7000 한다.\n[시큐리티] 이 턴 동안, 자신의 시큐리티 디지몬 전부의 DP를 +7000 한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST1-15", Name = "기가 디스트로이어", Type = CardType.Option, Color = CardColor.Red, Level = 0, Rarity = "C",
                PlayCost = 6,
                EffectText = "[메인] DP 4000 이하인 상대의 디지몬 2마리까지를 소멸시킨다.\n[시큐리티] 이 카드의 [메인] 효과를 발휘한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST1-16", Name = "가이아 포스", Type = CardType.Option, Color = CardColor.Red, Level = 0, Rarity = "U",
                PlayCost = 8,
                EffectText = "[메인] 상대의 디지몬 1마리를 소멸시킨다.\n[시큐리티] 이 카드의 [메인] 효과를 발휘한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST2-01", Name = "뿔몬", Type = CardType.Digitama, Color = CardColor.Blue, Level = 2, Rarity = "U",
                DP = 0,
                EffectText = "",
                InheritedEffectText = "[자신의 턴] 진화원을 갖지 않은 상대의 디지몬과 배틀하고 있는 동안, 이 디지몬의 DP를 +1000 한다."
            },
            new CardData
            {
                CardId = "BT1-003", Name = "우파몬", Type = CardType.Digitama, Color = CardColor.Blue, Level = 2, Rarity = "R",
                DP = 0,
                EffectText = "",
                InheritedEffectText = "[어택 시] [턴에 1회] 진화원을 갖지 않은 상대의 디지몬이 있을 때, ≪1 드로우≫ (자신의 덱에서 카드를 1장 드로우한다)."
            },
            new CardData
            {
                CardId = "ST2-02", Name = "쉬라몬", Type = CardType.Digimon, Color = CardColor.Blue, Level = 3, Rarity = "C",
                PlayCost = 2, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Blue, DP = 3000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST2-03", Name = "파피몬", Type = CardType.Digimon, Color = CardColor.Blue, Level = 3, Rarity = "U",
                PlayCost = 3, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Blue, DP = 2000,
                EffectText = "",
                InheritedEffectText = "[어택 시] Lv.5 이하인 상대의 디지몬 1마리의 진화원을, 아래에서 1장 파기한다."
            },
            new CardData
            {
                CardId = "ST2-04", Name = "베어몬", Type = CardType.Digimon, Color = CardColor.Blue, Level = 3, Rarity = "C",
                PlayCost = 3, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Blue, DP = 4000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT1-029", Name = "파피몬", Type = CardType.Digimon, Color = CardColor.Blue, Level = 3, Rarity = "R",
                PlayCost = 3, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Blue, DP = 1000,
                EffectText = "[등장 시] ≪1 드로우≫ (자신의 덱에서 카드를 1장 드로우한다)",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT2-021", Name = "브이몬", Type = CardType.Digimon, Color = CardColor.Blue, Level = 3, Rarity = "C",
                PlayCost = 3, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Blue, DP = 2000,
                EffectText = "",
                InheritedEffectText = "[자신의 턴] [턴에 1회] 이 디지몬이 메인 페이즈에서 액티브 상태가 되었을 때, ≪1 드로우≫ (자신의 덱에서 카드를 1장 드로우한다)."
            },
            new CardData
            {
                CardId = "ST2-05", Name = "원뿔몬", Type = CardType.Digimon, Color = CardColor.Blue, Level = 4, Rarity = "C",
                PlayCost = 4, EvoCost = 2, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Blue, DP = 5000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST2-06", Name = "가루몬", Type = CardType.Digimon, Color = CardColor.Blue, Level = 4, Rarity = "U",
                PlayCost = 5, EvoCost = 2, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Blue, DP = 4000,
                EffectText = "",
                InheritedEffectText = "[어택 시] 상대의 디지몬 1마리의 진화원을, 아래에서 1장 파기한다."
            },
            new CardData
            {
                CardId = "ST2-07", Name = "그리즈몬", Type = CardType.Digimon, Color = CardColor.Blue, Level = 4, Rarity = "C",
                PlayCost = 5, EvoCost = 2, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Blue, DP = 6000,
                IsBlocker = true,
                EffectText = "≪블로커≫ (상대의 디지몬이 어택했을 때, 이 디지몬이 액티브 상태라면 레스트시키는 것으로 어택의 대상을 이 디지몬으로 한다)\n[어택 시] 메모리를 -2 한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT1-034", Name = "원뿔몬", Type = CardType.Digimon, Color = CardColor.Blue, Level = 4, Rarity = "R",
                PlayCost = 5, EvoCost = 2, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Blue, DP = 5000,
                EffectText = "",
                InheritedEffectText = "[자신의 턴] 이 디지몬은, 진화원을 갖지 않은 상대의 디지몬에게는 블록당하지 않는다."
            },
            new CardData
            {
                CardId = "ST2-08", Name = "워가루몬", Type = CardType.Digimon, Color = CardColor.Blue, Level = 5, Rarity = "R",
                PlayCost = 7, EvoCost = 3, EvoRequirementLevel = 4, EvoRequirementColor = CardColor.Blue, DP = 7000,
                EffectText = "",
                InheritedEffectText = "[자신의 턴] 진화원을 갖지 않은 상대의 디지몬이 있는 동안, 이 디지몬은 ≪시큐리티 어택 +1≫ (이 디지몬이 체크하는 시큐리티의 매수 +1)을 얻는다."
            },
            new CardData
            {
                CardId = "ST2-09", Name = "쥬드몬", Type = CardType.Digimon, Color = CardColor.Blue, Level = 5, Rarity = "U",
                PlayCost = 6, EvoCost = 3, EvoRequirementLevel = 4, EvoRequirementColor = CardColor.Blue, DP = 7000,
                EffectText = "[진화 시] 상대의 디지몬 1마리의 진화원을, 아래에서 2장 파기한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT1-040", Name = "워가루몬", Type = CardType.Digimon, Color = CardColor.Blue, Level = 5, Rarity = "U",
                PlayCost = 6, EvoCost = 3, EvoRequirementLevel = 4, EvoRequirementColor = CardColor.Blue, DP = 7000,
                EffectText = "[어택 시] 메모리를 +3 한다. 이 턴 종료 시, 메모리를 -3 한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST2-10", Name = "플레시오몬", Type = CardType.Digimon, Color = CardColor.Blue, Level = 6, Rarity = "R",
                PlayCost = 10, EvoCost = 2, EvoRequirementLevel = 5, EvoRequirementColor = CardColor.Blue, DP = 12000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST2-11", Name = "메탈가루몬", Type = CardType.Digimon, Color = CardColor.Blue, Level = 6, Rarity = "SR",
                PlayCost = 12, EvoCost = 4, EvoRequirementLevel = 5, EvoRequirementColor = CardColor.Blue, DP = 11000,
                EffectText = "[어택 시] [턴에 1회] 이 디지몬을 액티브로 한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT2-032", Name = "알포스브이드라몬", Type = CardType.Digimon, Color = CardColor.Blue, Level = 6, Rarity = "SR",
                PlayCost = 13, EvoCost = 3, EvoRequirementLevel = 5, EvoRequirementColor = CardColor.Blue, DP = 11000,
                EffectText = "[자신의 턴] 블루인 자신의 테이머가 레스트했을 때, 이 디지몬을 액티브로 한다.\n[자신의 턴] [턴에 1회] 이 디지몬이 메인 페이즈에서 액티브 상태가 되었을 때, 메모리를 +1 한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT3-031", Name = "황제드라몬: 드래곤 모드", Type = CardType.Digimon, Color = CardColor.Blue, Level = 6, Rarity = "SR",
                PlayCost = 13, EvoCost = 5, EvoRequirementLevel = 5, EvoRequirementColor = CardColor.Blue, DP = 12000,
                EffectText = "자신의 「파일드라몬」 또는 「다이노비몬」이 패의 이 카드로 진화할 때, 지불하는 진화 코스트를 -2 한다.\n≪재밍≫ (이 디지몬은 시큐리티 디지몬과의 배틀에서는 소멸하지 않는다)\n[진화 시] ≪재밍≫을 가진 자신의 디지몬 전부를 액티브로 한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST2-12", Name = "매튜", Type = CardType.Tamer, Color = CardColor.Blue, Level = 0, Rarity = "R",
                PlayCost = 2,
                EffectText = "[자신의 턴 개시 시] 진화원을 갖지 않은 상대의 디지몬이 있을 때, 메모리를 +1 한다.\n[시큐리티] 이 카드를 코스트를 지불하지 않고 등장시킨다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST2-13", Name = "해머 스파크", Type = CardType.Option, Color = CardColor.Blue, Level = 0, Rarity = "C",
                PlayCost = 0,
                EffectText = "[메인] 메모리를 +1 한다.\n[시큐리티] 메모리를 +2 한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST2-14", Name = "소로 블루", Type = CardType.Option, Color = CardColor.Blue, Level = 0, Rarity = "C",
                PlayCost = 2,
                EffectText = "[메인] 진화원을 갖지 않은 상대의 디지몬 1마리를 선택한다. 그 디지몬은 다음 상대의 턴 종료 시까지 어택과 블록을 할 수 없다.\n[시큐리티] 진화원을 갖지 않은 상대의 디지몬 1마리를 선택한다. 그 디지몬은 다음 자신의 턴 종료 시까지 어택과 블록을 할 수 없다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST2-15", Name = "카이저 네일", Type = CardType.Option, Color = CardColor.Blue, Level = 0, Rarity = "C",
                PlayCost = 4,
                EffectText = "[메인] 자신의 디지몬 1마리의 진화원인 디지몬 카드 1장을 선택하고, 그 카드를 코스트를 지불하지 않고 다른 디지몬으로서 등장시킨다.\n[시큐리티] 이 카드의 [메인] 효과를 발휘한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST2-16", Name = "코큐토스 브레스", Type = CardType.Option, Color = CardColor.Blue, Level = 0, Rarity = "U",
                PlayCost = 7,
                EffectText = "[메인] 상대의 디지몬 1마리를 패로 되돌린다. 그 디지몬이 가지는 진화원은 파기한다.\n[시큐리티] 이 카드의 [메인] 효과를 발휘한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST3-01", Name = "토코몬", Type = CardType.Digitama, Color = CardColor.Yellow, Level = 2, Rarity = "U",
                DP = 0,
                EffectText = "",
                InheritedEffectText = "[자신의 턴] [턴에 1회] 상대의 디지몬이 DP가 0이 되어 소멸했을 때, 이 턴 동안, 이 디지몬의 DP를 +1000 한다."
            },
            new CardData
            {
                CardId = "ST3-02", Name = "플롯트몬", Type = CardType.Digimon, Color = CardColor.Yellow, Level = 3, Rarity = "C",
                PlayCost = 2, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Yellow, DP = 3000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST3-03", Name = "테이파몬", Type = CardType.Digimon, Color = CardColor.Yellow, Level = 3, Rarity = "C",
                PlayCost = 3, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Yellow, DP = 4000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST3-04", Name = "파닥몬", Type = CardType.Digimon, Color = CardColor.Yellow, Level = 3, Rarity = "U",
                PlayCost = 3, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Yellow, DP = 1000,
                EffectText = "",
                InheritedEffectText = "[자신의 턴] [턴에 1회] 상대의 디지몬이 DP가 0이 되어 소멸했을 때, 메모리를 +1 한다."
            },
            new CardData
            {
                CardId = "BT1-048", Name = "파닥몬", Type = CardType.Digimon, Color = CardColor.Yellow, Level = 3, Rarity = "R",
                PlayCost = 3, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Yellow, DP = 2000,
                EffectText = "[등장 시] 자신의 덱 위에서 4장 오픈한다. 그 중 옐로인 테이머 카드 전부를 패에 추가한다. 남은 카드는 원하는 순서대로 덱 아래로 되돌린다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST3-05", Name = "엔젤몬", Type = CardType.Digimon, Color = CardColor.Yellow, Level = 4, Rarity = "U",
                PlayCost = 5, EvoCost = 2, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Yellow, DP = 4000,
                EffectText = "",
                InheritedEffectText = "[어택 시] 자신의 시큐리티가 4장 이상 있을 때, 메모리를 +1 한다."
            },
            new CardData
            {
                CardId = "ST3-06", Name = "가트몬", Type = CardType.Digimon, Color = CardColor.Yellow, Level = 4, Rarity = "C",
                PlayCost = 4, EvoCost = 2, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Yellow, DP = 5000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST3-07", Name = "유니몬", Type = CardType.Digimon, Color = CardColor.Yellow, Level = 4, Rarity = "C",
                PlayCost = 5, EvoCost = 2, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Yellow, DP = 6000,
                IsBlocker = true,
                EffectText = "≪블로커≫ (상대의 디지몬이 어택했을 때, 이 디지몬이 액티브 상태라면 레스트시키는 것으로 어택의 대상을 이 디지몬으로 한다)\n[어택 시] 메모리를 -2 한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST3-08", Name = "홀리엔젤몬", Type = CardType.Digimon, Color = CardColor.Yellow, Level = 5, Rarity = "R",
                PlayCost = 7, EvoCost = 3, EvoRequirementLevel = 4, EvoRequirementColor = CardColor.Yellow, DP = 7000,
                EffectText = "",
                InheritedEffectText = "[어택 시] 이 턴 동안, 상대의 디지몬 1마리의 DP를 -1000 한다."
            },
            new CardData
            {
                CardId = "ST3-09", Name = "엔젤우몬", Type = CardType.Digimon, Color = CardColor.Yellow, Level = 5, Rarity = "U",
                PlayCost = 6, EvoCost = 3, EvoRequirementLevel = 4, EvoRequirementColor = CardColor.Yellow, DP = 7000,
                EffectText = "[진화 시] 자신의 시큐리티가 3장 이하일 때, ≪리커버리 +1 (덱)≫ (자신의 덱 위에서 카드를 1장 시큐리티 위에 놓는다).",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT1-060", Name = "홀리엔젤몬", Type = CardType.Digimon, Color = CardColor.Yellow, Level = 5, Rarity = "SR",
                PlayCost = 7, EvoCost = 3, EvoRequirementLevel = 4, EvoRequirementColor = CardColor.Yellow, DP = 6000,
                EffectText = "[등장 시] ≪리커버리 +1 (덱)≫ (자신의 덱 위에서 카드를 1장 시큐리티 위에 놓는다)",
                InheritedEffectText = "[자신의 턴] 자신의 시큐리티 3장마다, 이 디지몬의 DP를 +1000 한다."
            },
            new CardData
            {
                CardId = "ST3-10", Name = "홀리드라몬", Type = CardType.Digimon, Color = CardColor.Yellow, Level = 6, Rarity = "R",
                PlayCost = 10, EvoCost = 2, EvoRequirementLevel = 5, EvoRequirementColor = CardColor.Yellow, DP = 12000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST3-11", Name = "세라피몬", Type = CardType.Digimon, Color = CardColor.Yellow, Level = 6, Rarity = "SR",
                PlayCost = 12, EvoCost = 4, EvoRequirementLevel = 5, EvoRequirementColor = CardColor.Yellow, DP = 10000,
                EffectText = "[어택 시] 이 턴 동안, 상대의 디지몬 1마리의 DP를 -4000 한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT2-039", Name = "홀리드라몬", Type = CardType.Digimon, Color = CardColor.Yellow, Level = 6, Rarity = "U",
                PlayCost = 11, EvoCost = 3, EvoRequirementLevel = 5, EvoRequirementColor = CardColor.Yellow, DP = 10000,
                EffectText = "[등장 시] 자신의 시큐리티가 3장 이하일 때, ≪리커버리 +2 (덱)≫ (자신의 덱 위에서 카드를 2장 시큐리티 위에 놓는다).\n[어택 시] 자신의 패에서, 옐로인 Lv.3인 디지몬 카드 1장을 코스트를 지불하지 않고 등장할 수 있다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST3-12", Name = "리키", Type = CardType.Tamer, Color = CardColor.Yellow, Level = 0, Rarity = "R",
                PlayCost = 2,
                EffectText = "[상대의 턴] 자신의 시큐리티 디지몬 전부의 DP를 +2000 한다.\n[시큐리티] 이 카드를 코스트를 지불하지 않고 등장시킨다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST3-13", Name = "천국의 문", Type = CardType.Option, Color = CardColor.Yellow, Level = 0, Rarity = "C",
                PlayCost = 1,
                EffectText = "[메인] 이 턴 동안, 자신의 디지몬 1마리의 DP를 +3000 한다.\n[시큐리티] 이 턴 동안, 자신의 디지몬과 시큐리티 디지몬 전부의 DP를 +5000 한다. 그 후, 이 카드를 패에 추가한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST3-16", Name = "세븐 헤븐즈", Type = CardType.Option, Color = CardColor.Yellow, Level = 0, Rarity = "U",
                PlayCost = 7,
                EffectText = "[메인] 이 턴 동안, 상대의 디지몬 1마리의 DP를 -10000 한다.\n[시큐리티] 이 카드의 [메인] 효과를 발휘한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST4-01", Name = "모티몬", Type = CardType.Digitama, Color = CardColor.Green, Level = 2, Rarity = "U",
                DP = 0,
                EffectText = "",
                InheritedEffectText = "[자신의 턴] 이 디지몬이 Lv.6 이상일 동안, 이 디지몬의 DP를 +1000 한다."
            },
            new CardData
            {
                CardId = "ST4-02", Name = "플로라몬", Type = CardType.Digimon, Color = CardColor.Green, Level = 3, Rarity = "C",
                PlayCost = 2, EvoCost = 1, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Green, DP = 4000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST4-03", Name = "텐타몬", Type = CardType.Digimon, Color = CardColor.Green, Level = 3, Rarity = "U",
                PlayCost = 3, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Green, DP = 2000,
                EffectText = "[등장 시] 자신의 덱 위에서부터 1장 오픈한다. 그 카드가 그린인 디지몬 카드라면 패에 추가한다. 남은 카드는 덱 아래로 되돌린다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST4-04", Name = "팔몬", Type = CardType.Digimon, Color = CardColor.Green, Level = 3, Rarity = "C",
                PlayCost = 3, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Green, DP = 2000,
                EffectText = "",
                InheritedEffectText = "[어택 시] 상대의 디지몬에게 어택했을 때, 이 턴 동안, 이 디지몬의 DP를 +2000 한다."
            },
            new CardData
            {
                CardId = "BT1-067", Name = "팔몬", Type = CardType.Digimon, Color = CardColor.Green, Level = 3, Rarity = "U",
                PlayCost = 3, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Green, DP = 1000,
                EffectText = "[등장 시] 자신의 덱 위에서 3장 오픈한다. 그 중 Lv.4인 디지몬 카드 1장을 패에 추가한다. 남은 카드는 원하는 순서대로 덱 아래로 되돌린다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST4-05", Name = "쿠네몬", Type = CardType.Digimon, Color = CardColor.Green, Level = 3, Rarity = "C",
                PlayCost = 4, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Green, DP = 5000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST4-06", Name = "니드몬", Type = CardType.Digimon, Color = CardColor.Green, Level = 4, Rarity = "C",
                PlayCost = 4, EvoCost = 2, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Green, DP = 4000,
                EffectText = "",
                InheritedEffectText = "[어택 시] 상대의 디지몬에게 어택했을 때, 이 턴 동안, 이 디지몬의 DP를 +2000 한다."
            },
            new CardData
            {
                CardId = "ST4-07", Name = "쿠가몬", Type = CardType.Digimon, Color = CardColor.Green, Level = 4, Rarity = "C",
                PlayCost = 5, EvoCost = 2, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Green, DP = 6000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST4-08", Name = "캅테리몬", Type = CardType.Digimon, Color = CardColor.Green, Level = 4, Rarity = "U",
                PlayCost = 6, EvoCost = 1, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Green, DP = 5000,
                IsBlocker = true,
                EffectText = "≪블로커≫ (상대의 디지몬이 어택했을 때, 이 디지몬이 액티브 상태라면 레스트시키는 것으로 어택의 대상을 이 디지몬으로 한다)\n[어택 시] 메모리를 -2 한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST4-09", Name = "왕쿠가몬", Type = CardType.Digimon, Color = CardColor.Green, Level = 5, Rarity = "C",
                PlayCost = 6, EvoCost = 2, EvoRequirementLevel = 4, EvoRequirementColor = CardColor.Green, DP = 7000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT1-074", Name = "니드몬", Type = CardType.Digimon, Color = CardColor.Green, Level = 4, Rarity = "R",
                PlayCost = 6, EvoCost = 2, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Green, DP = 5000,
                EffectText = "[진화 시] 자신의 덱 위에서 3장 오픈한다. 그 중 Lv.5 이상인 디지몬 카드 1장을 패에 추가한다. 남은 카드는 원하는 순서대로 덱 아래로 되돌린다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT1-079", Name = "릴리몬", Type = CardType.Digimon, Color = CardColor.Green, Level = 5, Rarity = "R",
                PlayCost = 7, EvoCost = 2, EvoRequirementLevel = 4, EvoRequirementColor = CardColor.Green, DP = 6000,
                EffectText = "",
                InheritedEffectText = "[어택 시] ≪블로커≫를 갖지 않은 상대의 디지몬 1마리를 레스트시킨다."
            },
            new CardData
            {
                CardId = "ST4-10", Name = "릴리몬", Type = CardType.Digimon, Color = CardColor.Green, Level = 5, Rarity = "U",
                PlayCost = 6, EvoCost = 3, EvoRequirementLevel = 4, EvoRequirementColor = CardColor.Green, DP = 7000,
                EffectText = "[진화 시] 자신의 덱 위에서부터 5장 오픈한다. 그 중 Lv.6 이상의 디지몬 카드 1장을 패에 추가한다. 남은 카드는 원하는 순서대로 덱 아래로 되돌린다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST4-11", Name = "아트라캅테리몬", Type = CardType.Digimon, Color = CardColor.Green, Level = 5, Rarity = "R",
                PlayCost = 7, EvoCost = 3, EvoRequirementLevel = 4, EvoRequirementColor = CardColor.Green, DP = 7000,
                EffectText = "",
                InheritedEffectText = "[자신의 턴] [턴에 1회] 이 디지몬이 배틀에서 상대 디지몬만을 소멸시켰을 때, 상대의 시큐리티를 위에서부터 1장 파기한다."
            },
            new CardData
            {
                CardId = "ST4-12", Name = "로제몬", Type = CardType.Digimon, Color = CardColor.Green, Level = 6, Rarity = "R",
                PlayCost = 10, EvoCost = 3, EvoRequirementLevel = 5, EvoRequirementColor = CardColor.Green, DP = 11000,
                EffectText = "[진화 시] 다음 상대의 턴 종료 시까지, 상대의 디지몬 1마리는 어택과 블록을 할 수 없다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST4-13", Name = "헤라클레스캅테리몬", Type = CardType.Digimon, Color = CardColor.Green, Level = 6, Rarity = "SR",
                PlayCost = 12, EvoCost = 4, EvoRequirementLevel = 5, EvoRequirementColor = CardColor.Green, DP = 12000,
                EffectText = "≪관통≫ (이 디지몬이 어택한 배틀에서 상대의 디지몬만을 소멸시켰을 때, 이 디지몬은 시큐리티를 체크한다)\n[메인] ≪디지버스트 2≫ (이 디지몬의 진화원을 2장 골라 파기하는 것으로 이하의 효과를 발휘한다)\n・상대의 디지몬 1마리를 레스트시킨다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST4-14", Name = "장한솔", Type = CardType.Tamer, Color = CardColor.Green, Level = 0, Rarity = "R",
                PlayCost = 2,
                EffectText = "[자신의 턴] 상대의 디지몬이 레스트했을 때, 이 테이머를 레스트시키는 것으로 메모리를 +1 한다.\n[시큐리티] 이 카드를 코스트를 지불하지 않고 등장시킨다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST4-16", Name = "전기 충격", Type = CardType.Option, Color = CardColor.Green, Level = 0, Rarity = "U",
                PlayCost = 5,
                EffectText = "[메인] 레스트 상태의 상대 디지몬 1마리를 패로 되돌린다. 그 디지몬이 가진 진화원은 파기한다.\n[시큐리티] 이 카드의 [메인] 효과를 발휘한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST5-01", Name = "카프리몬", Type = CardType.Digitama, Color = CardColor.Black, Level = 2, Rarity = "U",
                DP = 0,
                EffectText = "",
                InheritedEffectText = "[자신의 턴] 이 디지몬이 ≪블로커≫를 가지는 동안, 이 디지몬의 DP를 +1000 한다."
            },
            new CardData
            {
                CardId = "ST5-02", Name = "쟈자몬", Type = CardType.Digimon, Color = CardColor.Black, Level = 3, Rarity = "C",
                PlayCost = 2, EvoCost = 1, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Black, DP = 4000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST5-03", Name = "아구몬", Type = CardType.Digimon, Color = CardColor.Black, Level = 3, Rarity = "U",
                PlayCost = 3, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Black, DP = 1000,
                IsBlocker = true,
                EffectText = "≪블로커≫ (상대의 디지몬이 어택했을 때, 이 디지몬이 액티브 상태라면 레스트시키는 것으로 어택의 대상을 이 디지몬으로 한다)",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST5-04", Name = "토이아구몬", Type = CardType.Digimon, Color = CardColor.Black, Level = 3, Rarity = "C",
                PlayCost = 3, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Black, DP = 2000,
                EffectText = "",
                InheritedEffectText = "[상대의 턴 종료 시] 이 턴에 상대의 디지몬이 한 번도 어택하지 않았다면, ≪1 드로우≫ (자신의 덱에서 카드를 1장 드로우한다)."
            },
            new CardData
            {
                CardId = "BT2-052", Name = "톱니몬", Type = CardType.Digimon, Color = CardColor.Black, Level = 3, Rarity = "C",
                PlayCost = 2, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Black, DP = 3000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST5-05", Name = "코만드라몬", Type = CardType.Digimon, Color = CardColor.Black, Level = 3, Rarity = "C",
                PlayCost = 4, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Black, DP = 5000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST5-06", Name = "그레이몬", Type = CardType.Digimon, Color = CardColor.Black, Level = 4, Rarity = "C",
                PlayCost = 4, EvoCost = 2, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Black, DP = 4000,
                EffectText = "",
                InheritedEffectText = "[상대의 턴 종료 시] 이 턴에 상대의 디지몬이 한 번도 어택하지 않았다면, ≪1 드로우≫ (자신의 덱에서 카드를 1장 드로우한다)."
            },
            new CardData
            {
                CardId = "ST5-07", Name = "쟈자드몬", Type = CardType.Digimon, Color = CardColor.Black, Level = 4, Rarity = "C",
                PlayCost = 5, EvoCost = 2, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Black, DP = 6000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST5-08", Name = "다크티라노몬", Type = CardType.Digimon, Color = CardColor.Black, Level = 4, Rarity = "U",
                PlayCost = 6, EvoCost = 1, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Black, DP = 5000,
                IsBlocker = true,
                EffectText = "≪블로커≫ (상대의 디지몬이 어택했을 때, 이 디지몬이 액티브 상태라면 레스트시키는 것으로 어택의 대상을 이 디지몬으로 한다)\n[어택 시] 메모리를 -2 한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST5-09", Name = "메탈그레이몬", Type = CardType.Digimon, Color = CardColor.Black, Level = 5, Rarity = "U",
                PlayCost = 6, EvoCost = 3, EvoRequirementLevel = 4, EvoRequirementColor = CardColor.Black, DP = 7000,
                EffectText = "[진화 시] 다음 상대의 턴 종료 시까지, 자신의 디지몬 1마리는 ≪블로커≫ (상대의 디지몬이 어택했을 때, 이 디지몬이 액티브 상태라면 레스트시키는 것으로 어택의 대상을 이 디지몬으로 한다)를 얻는다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT2-059", Name = "크리사리몬", Type = CardType.Digimon, Color = CardColor.Black, Level = 4, Rarity = "U",
                PlayCost = 5, EvoCost = 2, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Black, DP = 4000,
                EffectText = "",
                InheritedEffectText = "[자신의 턴] 이 디지몬과 같은 명칭의 다른 자신의 디지몬이 등장했을 때, 메모리를 +1 한다."
            },
            new CardData
            {
                CardId = "ST5-11", Name = "메가드라몬", Type = CardType.Digimon, Color = CardColor.Black, Level = 5, Rarity = "R",
                PlayCost = 7, EvoCost = 3, EvoRequirementLevel = 4, EvoRequirementColor = CardColor.Black, DP = 7000,
                EffectText = "",
                InheritedEffectText = "≪블로커≫ (상대의 디지몬이 어택했을 때, 이 디지몬이 액티브 상태라면 레스트시키는 것으로 어택의 대상을 이 디지몬으로 한다)"
            },
            new CardData
            {
                CardId = "ST5-12", Name = "파워드라몬", Type = CardType.Digimon, Color = CardColor.Black, Level = 6, Rarity = "R",
                PlayCost = 10, EvoCost = 3, EvoRequirementLevel = 5, EvoRequirementColor = CardColor.Black, DP = 11000,
                EffectText = "[진화 시] 다음 상대의 턴 종료 시까지, 자신의 디지몬 2마리까지는 ≪재기동≫ (상대의 액티브 페이즈에, 이 디지몬을 액티브로 한다)을 얻는다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT2-065", Name = "워그레이몬", Type = CardType.Digimon, Color = CardColor.Black, Level = 6, Rarity = "SR",
                PlayCost = 12, EvoCost = 3, EvoRequirementLevel = 5, EvoRequirementColor = CardColor.Black, DP = 11000,
                IsBlocker = true,
                EffectText = "≪블로커≫ (상대의 디지몬이 어택했을 때, 이 디지몬이 액티브 상태라면 레스트시키는 것으로 어택의 대상을 이 디지몬으로 한다)\n≪재기동≫ (상대의 액티브 페이즈에, 이 디지몬을 액티브로 한다)",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST5-14", Name = "신태일", Type = CardType.Tamer, Color = CardColor.Black, Level = 0, Rarity = "R",
                PlayCost = 2,
                EffectText = "[상대의 턴] 자신의 디지몬이 ≪블로커≫를 사용하여 레스트했을 때, 이 테이머를 레스트시키는 것으로, 자신의 디지몬 1마리를 액티브로 한다.\n[시큐리티] 이 카드를 코스트를 지불하지 않고 등장시킨다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST5-15", Name = "레이저 아이", Type = CardType.Option, Color = CardColor.Black, Level = 0, Rarity = "C",
                PlayCost = 4,
                EffectText = "[메인] 상대의 디지몬 2마리까지를 ≪퇴화 1≫ (상대 디지몬 위에 포개진 카드를 위에서부터 1장까지 파기한다. 진화원이 없어지거나, Lv.3의 디지몬이 되었다면 그 이상 파기할 수 없다)한다.\n[시큐리티] 이 카드의 [메인] 효과를 발휘한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST5-16", Name = "제노사이드 어택", Type = CardType.Option, Color = CardColor.Black, Level = 0, Rarity = "U",
                PlayCost = 5,
                EffectText = "[메인] 등장 코스트 7 이하의 상대 디지몬 1마리를 소멸시킨다.\n[시큐리티] 이 카드의 [메인] 효과를 발휘한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST6-01", Name = "퍼그몬", Type = CardType.Digitama, Color = CardColor.Purple, Level = 2, Rarity = "U",
                DP = 0,
                EffectText = "",
                InheritedEffectText = "[소멸 시] 자신의 덱 위에서부터 2장 파기한다."
            },
            new CardData
            {
                CardId = "ST6-02", Name = "피코데블몬", Type = CardType.Digimon, Color = CardColor.Purple, Level = 3, Rarity = "C",
                PlayCost = 2, EvoCost = 1, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Purple, DP = 4000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST6-03", Name = "파피몬", Type = CardType.Digimon, Color = CardColor.Purple, Level = 3, Rarity = "C",
                PlayCost = 3, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Purple, DP = 2000,
                EffectText = "",
                InheritedEffectText = "[어택 시] ≪1 드로우≫ (자신의 덱에서 카드를 1장 드로우한다). 그 후, 자신의 패를 1장 파기한다."
            },
            new CardData
            {
                CardId = "ST6-04", Name = "드라큐몬", Type = CardType.Digimon, Color = CardColor.Purple, Level = 3, Rarity = "U",
                PlayCost = 3, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Purple, DP = 2000,
                EffectText = "[등장 시] 자신의 트래시에서, 퍼플인 사용 코스트 1 또는 7의 옵션 카드 1장을 패로 되돌릴 수 있다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT2-068", Name = "임프몬", Type = CardType.Digimon, Color = CardColor.Purple, Level = 3, Rarity = "C",
                PlayCost = 3, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Purple, DP = 1000,
                EffectText = "[소멸 시] 자신의 덱 위에서 3장 파기한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST6-05", Name = "에렉몬", Type = CardType.Digimon, Color = CardColor.Purple, Level = 3, Rarity = "C",
                PlayCost = 4, EvoCost = 0, EvoRequirementLevel = 2, EvoRequirementColor = CardColor.Purple, DP = 5000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST6-06", Name = "가루몬", Type = CardType.Digimon, Color = CardColor.Purple, Level = 4, Rarity = "C",
                PlayCost = 4, EvoCost = 2, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Purple, DP = 4000,
                EffectText = "",
                InheritedEffectText = "[어택 시] ≪1 드로우≫ (자신의 덱에서 카드를 1장 드로우한다). 그 후, 자신의 패를 1장 파기한다."
            },
            new CardData
            {
                CardId = "ST6-07", Name = "요우코몬", Type = CardType.Digimon, Color = CardColor.Purple, Level = 4, Rarity = "C",
                PlayCost = 5, EvoCost = 2, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Purple, DP = 6000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST6-08", Name = "데블몬", Type = CardType.Digimon, Color = CardColor.Purple, Level = 4, Rarity = "U",
                PlayCost = 6, EvoCost = 1, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Purple, DP = 5000,
                IsBlocker = true,
                EffectText = "≪블로커≫ (상대의 디지몬이 어택했을 때, 이 디지몬이 액티브 상태라면 레스트시키는 것으로 어택의 대상을 이 디지몬으로 한다)\n[어택 시] 메모리를 -2 한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST6-09", Name = "큐우키몬", Type = CardType.Digimon, Color = CardColor.Purple, Level = 5, Rarity = "C",
                PlayCost = 6, EvoCost = 3, EvoRequirementLevel = 4, EvoRequirementColor = CardColor.Purple, DP = 9000,
                EffectText = "",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT2-074", Name = "데블몬", Type = CardType.Digimon, Color = CardColor.Purple, Level = 4, Rarity = "U",
                PlayCost = 6, EvoCost = 2, EvoRequirementLevel = 3, EvoRequirementColor = CardColor.Purple, DP = 4000,
                EffectText = "≪길동무≫ (배틀에서 이 디지몬만이 소멸했을 때, 배틀한 상대의 디지몬을 소멸시킨다)",
                InheritedEffectText = "≪길동무≫ (배틀에서 이 디지몬만이 소멸했을 때, 배틀한 상대의 디지몬을 소멸시킨다)"
            },
            new CardData
            {
                CardId = "ST6-10", Name = "스컬사탄몬", Type = CardType.Digimon, Color = CardColor.Purple, Level = 5, Rarity = "U",
                PlayCost = 6, EvoCost = 3, EvoRequirementLevel = 4, EvoRequirementColor = CardColor.Purple, DP = 7000,
                EffectText = "[진화 시] 자신의 트래시에서, 퍼플인 디지몬 카드 1장을 패로 되돌릴 수 있다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST6-11", Name = "워가루몬", Type = CardType.Digimon, Color = CardColor.Purple, Level = 5, Rarity = "R",
                PlayCost = 7, EvoCost = 3, EvoRequirementLevel = 4, EvoRequirementColor = CardColor.Purple, DP = 7000,
                EffectText = "",
                InheritedEffectText = "[자신의 턴] 자신의 트래시가 5장 이상일 동안, 이 디지몬의 DP를 +2000 한다."
            },
            new CardData
            {
                CardId = "BT2-080", Name = "피에몬", Type = CardType.Digimon, Color = CardColor.Purple, Level = 6, Rarity = "SR",
                PlayCost = 12, EvoCost = 3, EvoRequirementLevel = 5, EvoRequirementColor = CardColor.Purple, DP = 10000,
                EffectText = "≪길동무≫ (배틀에서 이 디지몬만이 소멸했을 때, 배틀한 상대의 디지몬을 소멸시킨다)\n[등장 시] 자신의 트래시에서, 퍼플의 Lv.4 이하인 디지몬 카드 2장까지를 코스트를 지불하지 않고 등장할 수 있다. 이 효과로 등장한 디지몬의 [등장 시] 효과는 발휘하지 않는다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST6-14", Name = "매튜", Type = CardType.Tamer, Color = CardColor.Purple, Level = 0, Rarity = "R",
                PlayCost = 2,
                EffectText = "[자신의 턴] 자신의 디지몬이 소멸했을 때, 이 테이머를 레스트시키는 것으로, 메모리를 +1 한다.\n[시큐리티] 이 카드를 코스트를 지불하지 않고 등장시킨다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST6-15", Name = "데스 클로", Type = CardType.Option, Color = CardColor.Purple, Level = 0, Rarity = "C",
                PlayCost = 1,
                EffectText = "[메인] 자신의 디지몬 1마리를 소멸시키는 것으로, Lv.4 이하의 상대 디지몬 1마리를 소멸시킨다.\n[시큐리티] Lv.4 이하의 상대 디지몬 1마리를 소멸시킨다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "ST6-16", Name = "어둠의 저주", Type = CardType.Option, Color = CardColor.Purple, Level = 0, Rarity = "U",
                PlayCost = 7,
                EffectText = "[메인] 자신의 트래시에서, 퍼플인 Lv.3의 디지몬 카드 1장과 퍼플인 Lv.4의 디지몬 카드 1장을 코스트를 지불하지 않고 등장시킬 수 있다. 이 효과로 등장한 디지몬의 [등장 시] 효과는 발휘하지 않는다.\n[시큐리티] 자신의 트래시에서, 퍼플인 Lv.4 이하의 디지몬 카드 1장을 코스트를 지불하지 않고 등장시킬 수 있다. 이 효과로 등장한 디지몬의 [등장 시] 효과는 발휘하지 않는다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT1-084", Name = "오메가몬", Type = CardType.Digimon, Color = CardColor.White, Level = 7, Rarity = "SR",
                PlayCost = 15, EvoCost = 6, EvoRequirementLevel = 6, EvoRequirementColor = CardColor.Red, DP = 15000,
                EffectText = "[진화 시] 상대의 디지몬 1마리를 선택한다. 그 디지몬과 같은 명칭인 상대의 디지몬 전부를 소멸시킨다.\n[어택 시] 이 디지몬의 진화원인 Lv.6의 디지몬 카드 1장을 패로 되돌리는 것으로, 이 디지몬을 액티브로 한다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT3-112", Name = "오메가몬 Alter-S", Type = CardType.Digimon, Color = CardColor.White, Level = 7, Rarity = "SEC",
                PlayCost = 15, EvoCost = 6, EvoRequirementLevel = 6, EvoRequirementColor = CardColor.Red, DP = 15000,
                EffectText = "[진화 시] 상대의 디지몬 전부를 ≪퇴화 1≫ (상대 디지몬 위에 포개진 카드를 위에서부터 1장 파기한다. 진화원이 없어지거나, Lv.3의 디지몬이 되었다면 그 이상 파기할 수 없다) 한다. 그 후, DP 5000 이하인 상대의 디지몬 전부를 소멸시킨다.\n[어택 시] 이 디지몬의 진화원인 Lv.6의 디지몬 카드 1장을 패로 되돌리는 것으로, 이 턴 동안 이 디지몬은 상대 디지몬에게는 블록당하지 않는다.",
                InheritedEffectText = ""
            },
            new CardData
            {
                CardId = "BT5-086", Name = "오메가몬", Type = CardType.Digimon, Color = CardColor.White, Level = 7, Rarity = "SR",
                PlayCost = 15, EvoCost = 4, EvoRequirementLevel = 6, EvoRequirementColor = CardColor.Red, DP = 14000,
                EffectText = "[진화 시] ≪진격≫ (메모리가 상대측 1 이상일 때, 이 디지몬은 어택할 수 있다)\n[진화 시] 이 디지몬을 액티브로 한다.\n[서로의 턴] 이 디지몬이 상대의 효과로 소멸할 때, 또는 상대의 효과로 패 및 덱으로 돌아갈 때, 이 디지몬의 진화원에 있는 Lv.6의 디지몬 카드 1장을 파기하는 것으로 이 디지몬은 소멸하지 않으며, 패 및 덱으로 돌아가지 않는다.",
                InheritedEffectText = ""
            },

        };

        foreach (var card in cards)
        {
            if (string.IsNullOrWhiteSpace(card.ImageUrl))
            {
                card.ImageUrl = $"https://images.digimoncard.io/images/cards/{card.CardId}.jpg";
            }
            card.EnglishName = EnglishNameOf(card.CardId);
        }

        return cards;
    }

    /// <summary>
    /// 6대 메타/스타터 추천 덱 프리셋 목록 반환
    /// </summary>
    public static List<RecommendedDeckPreset> GetRecommendedDeckPresets() => new()
    {
        new RecommendedDeckPreset(
            "RED_OMEGA",
            "🔥 [추천 1] 워그레이몬 & 오메가몬 초화력 비트",
            "Red Tier 1 · 시큐리티 연쇄 돌파",
            "신태일의 DP +1000과 그레이몬 진화원의 ≪시큐리티 어택 +1≫, 워그레이몬의 진화원 2장마다 시큐리티 어택 +1로 시큐리티를 빠르게 깎는 레드 비트 덱입니다.",
            CardColor.Red,
            "ST1-11",
            CreateSt1GaiaRedPreset),

        new RecommendedDeckPreset(
            "BLUE_OMEGA",
            "❄️ [추천 2] 메탈가루몬 & 오메가몬 연타 템포",
            "Blue Tier 1 · 액티브 재기상 2연타",
            "파피몬 · 가루몬 진화원으로 상대 진화원을 벗겨 내고, 매튜와 해머 스파크로 메모리를 챙겨 메탈가루몬의 [어택 시] 액티브로 연속 공격하는 블루 템포 덱입니다.",
            CardColor.Blue,
            "ST2-11",
            CreateSt2CocytusBluePreset),

        new RecommendedDeckPreset(
            "YELLOW_SERAPHI",
            "⚡ [추천 3] 세라피몬 & 홀리드라몬 천사 컨트롤",
            "Yellow Control · DP 마이너스 & 리커버리",
            "엔젤몬 · 홀리엔젤몬의 DP 마이너스로 상대 디지몬을 녹이고, 엔젤우몬 · 홀리드라몬의 ≪리커버리≫로 시큐리티를 다시 쌓는 옐로 컨트롤 덱입니다.",
            CardColor.Yellow,
            "ST3-11",
            CreateSt3HeavensYellowPreset),

        new RecommendedDeckPreset(
            "GREEN_HERCULES",
            "🌿 [추천 4] 헤라클레스캅테리몬 & 로제몬",
            "Green Tempo · 레스트 & 관통",
            "릴리몬 진화원으로 상대 디지몬을 레스트시키고, 로제몬의 어택 · 블록 봉인과 헤라클레스캅테리몬의 ≪관통≫으로 필드와 시큐리티를 함께 무너뜨리는 그린 덱입니다.",
            CardColor.Green,
            "ST4-13",
            CreateSt4GigaGreenPreset),

        new RecommendedDeckPreset(
            "BLACK_MACHINEDRA",
            "⚙️ [추천 5] 파워드라몬 & 워그레이몬 철벽 블로커",
            "Black Defense · ≪블로커≫ & ≪재기동≫",
            "다크티라노몬 · 메가드라몬의 ≪블로커≫로 버티고, 파워드라몬과 워그레이몬(흑)의 ≪재기동≫으로 상대 턴에도 다시 일어서는 블랙 수비 덱입니다.",
            CardColor.Black,
            "ST5-12",
            CreateSt5MachineBlackPreset),

        new RecommendedDeckPreset(
            "PURPLE_VENOM",
            "💜 [추천 6] 피에몬 & 데블몬 길동무",
            "Purple Recursion · 트래시 재활용 & ≪길동무≫",
            "데블몬의 ≪길동무≫로 상대를 길동무 삼고, 스컬사탄몬 · 어둠의 저주 · 피에몬으로 트래시의 디지몬을 다시 불러내는 퍼플 덱입니다.",
            CardColor.Purple,
            "BT2-080",
            CreateSt6VenomVioletPreset)
    };

    /// <summary>
    /// 현재 짜고 있는 덱(또는 선택한 색상)을 분석하여 부족한 카드를 황금 비율(50장 메인 + 4장 디지타마)에 맞게 자동 추천 완성!
    /// </summary>
    public static (List<CardData> MainDeck, List<CardData> DigitamaDeck, string SummaryMessage) AutoCompleteRecommendedDeck(
        IReadOnlyList<CardData> currentMain,
        IReadOnlyList<CardData> currentDigitama,
        IReadOnlyList<CardData>? customCatalog,
        CardColor? preferredColor = null)
    {
        return AutoCompleteRecommendedDeck(currentMain, currentDigitama, preferredColor, customCatalog);
    }

    public static (List<CardData> MainDeck, List<CardData> DigitamaDeck, string SummaryMessage) AutoCompleteRecommendedDeck(
        IReadOnlyList<CardData> currentMain,
        IReadOnlyList<CardData> currentDigitama,
        CardColor? preferredColor = null,
        IReadOnlyList<CardData>? customCatalog = null)
    {
        var catalog = customCatalog ?? GetAllCatalogCards();

        // 1) 주력 색상 판별 (현재 덱에 가장 많은 색상 -> 필터 선택 색상 -> 기본 Red)
        CardColor targetColor = preferredColor
            ?? currentMain.Where(c => c.Color != CardColor.White)
                          .GroupBy(c => c.Color)
                          .OrderByDescending(g => g.Count())
                          .Select(g => g.Key)
                          .FirstOrDefault(CardColor.Red);

        if (targetColor == CardColor.White)
            targetColor = CardColor.Red;

        var main = currentMain.Take(50).ToList();
        var digitama = currentDigitama.Take(5).ToList();

        int CountCopies(IEnumerable<CardData> list, string id) => list.Count(c => c.CardId == id);

        // 2) 디지타마 4장 채우기
        var colorEggs = catalog.Where(c => c.Type == CardType.Digitama && c.Color == targetColor).ToList();
        if (colorEggs.Count == 0)
            colorEggs = catalog.Where(c => c.Type == CardType.Digitama).ToList();

        foreach (var egg in colorEggs)
        {
            while (digitama.Count < 4 && CountCopies(digitama, egg.CardId) < 4)
            {
                digitama.Add(egg);
            }
        }

        // 3) 황금 비율 목표치: Lv.3(14장), Lv.4(12장), Lv.5(10장), Lv.6~7(6장), 테이머/옵션(8장) = 50장
        void FillPool(Func<CardData, bool> predicate, int targetCategoryCount)
        {
            var candidates = catalog
                .Where(predicate)
                .OrderByDescending(c => c.SecurityAttackBonus + c.InheritedSecurityAttackBonus)
                .ThenByDescending(c => c.InheritedDpBonus)
                .ThenBy(c => c.EvoCost)
                .ToList();

            foreach (var cand in candidates)
            {
                while (main.Count < 50 &&
                       main.Count(predicate) < targetCategoryCount &&
                       CountCopies(main, cand.CardId) < 4)
                {
                    main.Add(cand);
                }
            }
        }

        FillPool(c => c.Type == CardType.Digimon && c.Color == targetColor && c.Level == 3, 14);
        FillPool(c => c.Type == CardType.Digimon && c.Color == targetColor && c.Level == 4, 12);
        FillPool(c => c.Type == CardType.Digimon && c.Color == targetColor && c.Level == 5, 10);
        FillPool(c => c.Type == CardType.Digimon && c.Color == targetColor && c.Level == 6, 5);

        // Lv.7 초궁극체 1장 조커 투입 (Lv.6 5장 + Lv.7 1장 = 총 6장 황금비율!)
        var ultraId = targetColor is CardColor.Black or CardColor.Purple ? "BT5-086" : "BT1-084";
        var ultraCard = catalog.FirstOrDefault(c => c.CardId == ultraId);
        if (ultraCard != null && targetColor is CardColor.Red or CardColor.Blue or CardColor.Black or CardColor.Purple)
        {
            while (main.Count < 50 && CountCopies(main, ultraCard.CardId) < 1 && main.Count(c => c.Level >= 6) < 6)
            {
                main.Add(ultraCard);
            }
        }
        else
        {
            FillPool(c => c.Type == CardType.Digimon && c.Color == targetColor && c.Level == 6, 6);
        }

        // 테이머 (2장) & 옵션 (6장) = 8장 채우기
        FillPool(c => c.Type == CardType.Tamer && c.Color == targetColor, 2);
        FillPool(c => c.Type == CardType.Option && c.Color == targetColor, 6);

        // 아직 50장이 안 되었다면(해당 색상 카드 종류가 부족) 황금 비율 상한을 넘지 않는 선에서 채운다:
        // 1차는 같은 색상, 2차는 다른 색상 카드로 부족한 레벨대를 채움
        var categoryCaps = new (Func<CardData, bool> Match, int Max)[]
        {
            (c => c.Type is CardType.Tamer or CardType.Option, 8),
            (c => c.Type == CardType.Digimon && c.Level == 5, 10),
            (c => c.Type == CardType.Digimon && c.Level == 4, 12),
            (c => c.Type == CardType.Digimon && c.Level == 3, 14),
            (c => c.Type == CardType.Digimon && c.Level >= 6, 6),
        };
        foreach (bool sameColorOnly in new[] { true, false })
        {
            foreach (var (match, max) in categoryCaps)
            {
                foreach (var cand in catalog.Where(c => match(c) && c.Color != CardColor.White && (!sameColorOnly || c.Color == targetColor)))
                {
                    while (main.Count < 50 && main.Count(match) < max && CountCopies(main, cand.CardId) < 4)
                        main.Add(cand);
                }
            }
        }

        // 그래도 50장이 안 되었다면 해당 색상의 남은 카드로 50장 완성
        var fallbackPool = catalog.Where(c => c.Type != CardType.Digitama && c.Color == targetColor && c.Level <= 5).ToList();
        foreach (var cand in fallbackPool)
        {
            while (main.Count < 50 && CountCopies(main, cand.CardId) < 4)
            {
                main.Add(cand);
            }
        }

        string msg = $"🤖 [{targetColor}] 속성 황금 비율(Lv.3 14장 / Lv.4 12장 / Lv.5 10장 / Lv.6~7 6장 / 테이머·옵션 8장)에 맞춰 메인 덱 {main.Count}장 + 디지타마 {digitama.Count}장을 자동 완성했습니다!";
        return (main, digitama, msg);
    }

    /// <summary>
    /// ST-1 가이아 레드 50장 메인 덱 + 4장 디지타마 프리셋 생성 (황금 비율 준수)
    /// </summary>
    public static (List<CardData> MainDeck, List<CardData> DigitamaDeck) CreateSt1GaiaRedPreset()
    {
        var catalog = GetAllCatalogCards().ToDictionary(c => c.CardId);
        var digitama = new List<CardData>();
        var main = new List<CardData>();

        AddCopies(digitama, catalog["ST1-01"], 4);

        // Lv.3 성장기: 14장
        AddCopies(main, catalog["ST1-03"], 4);  // 아구몬 x4
        AddCopies(main, catalog["ST1-02"], 4);  // 피요몬 x4
        AddCopies(main, catalog["ST1-04"], 4);  // 드라코몬 x4
        AddCopies(main, catalog["BT1-010"], 2); // 아구몬(BT1) x2

        // Lv.4 성숙기: 12장
        AddCopies(main, catalog["ST1-07"], 4); // 그레이몬 x4
        AddCopies(main, catalog["ST1-05"], 4); // 버드라몬 x4
        AddCopies(main, catalog["ST1-06"], 4); // 코어드라몬 x4

        // Lv.5 완전체: 10장
        AddCopies(main, catalog["ST1-09"], 4);  // 메탈그레이몬 x4
        AddCopies(main, catalog["ST1-08"], 4);  // 가루다몬 x4
        AddCopies(main, catalog["BT1-020"], 2); // 그라운드라몬 x2

        // Lv.6~7 궁극체/초궁극체: 6장 (황금비율 4~6장 적정)
        AddCopies(main, catalog["ST1-11"], 3);  // 워그레이몬 x3
        AddCopies(main, catalog["ST1-10"], 2);  // 페닉스몬 x2
        AddCopies(main, catalog["BT1-084"], 1); // 오메가몬(Lv.7) x1

        // 테이머/옵션: 8장 (황금비율 6~8장 적정)
        AddCopies(main, catalog["ST1-12"], 2); // 신태일 x2
        AddCopies(main, catalog["ST1-13"], 2); // 섀도우 윙 x2
        AddCopies(main, catalog["ST1-14"], 2); // 스타라이트 익스플로전 x2
        AddCopies(main, catalog["ST1-16"], 2); // 가이아 포스 x2

        return (main, digitama);
    }

    /// <summary>
    /// ST-2 코큐토스 블루 50장 메인 덱 + 4장 디지타마 프리셋 생성 (황금 비율 준수)
    /// </summary>
    public static (List<CardData> MainDeck, List<CardData> DigitamaDeck) CreateSt2CocytusBluePreset()
    {
        var catalog = GetAllCatalogCards().ToDictionary(c => c.CardId);
        var digitama = new List<CardData>();
        var main = new List<CardData>();

        AddCopies(digitama, catalog["ST2-01"], 4);

        // Lv.3 성장기: 14장
        AddCopies(main, catalog["ST2-03"], 4);  // 파피몬 x4
        AddCopies(main, catalog["ST2-02"], 4);  // 고마몬 x4
        AddCopies(main, catalog["ST2-04"], 4);  // 베어몬 x4
        AddCopies(main, catalog["BT1-029"], 2); // 파피몬(BT1) x2

        // Lv.4 성숙기: 12장
        AddCopies(main, catalog["ST2-06"], 4); // 가루몬 x4
        AddCopies(main, catalog["ST2-05"], 4); // 원뿔몬 x4
        AddCopies(main, catalog["ST2-07"], 4); // 그리즈몬 x4

        // Lv.5 완전체: 10장
        AddCopies(main, catalog["ST2-08"], 4);  // 워가루몬 x4
        AddCopies(main, catalog["ST2-09"], 4);  // 쥬드몬 x4
        AddCopies(main, catalog["BT1-040"], 2); // 워가루몬(BT1) x2

        // Lv.6~7 궁극체/초궁극체: 6장 (황금비율 4~6장 적정)
        AddCopies(main, catalog["ST2-11"], 3);  // 메탈가루몬 x3
        AddCopies(main, catalog["ST2-10"], 2);  // 플레시오몬 x2
        AddCopies(main, catalog["BT1-084"], 1); // 오메가몬(Lv.7) x1

        // 테이머/옵션: 8장 (황금비율 6~8장 적정)
        AddCopies(main, catalog["ST2-12"], 2); // 매튜 x2
        AddCopies(main, catalog["ST2-13"], 2); // 해머 스파크 x2
        AddCopies(main, catalog["ST2-14"], 2); // 소로우 블루 x2
        AddCopies(main, catalog["ST2-16"], 2); // 코큐토스 브레스 x2

        return (main, digitama);
    }

    public static (List<CardData> MainDeck, List<CardData> DigitamaDeck) CreateSt3HeavensYellowPreset()
    {
        var (main, digitama, _) = AutoCompleteRecommendedDeck(Array.Empty<CardData>(), Array.Empty<CardData>(), CardColor.Yellow);
        return (main, digitama);
    }

    public static (List<CardData> MainDeck, List<CardData> DigitamaDeck) CreateSt4GigaGreenPreset()
    {
        var (main, digitama, _) = AutoCompleteRecommendedDeck(Array.Empty<CardData>(), Array.Empty<CardData>(), CardColor.Green);
        return (main, digitama);
    }

    public static (List<CardData> MainDeck, List<CardData> DigitamaDeck) CreateSt5MachineBlackPreset()
    {
        var (main, digitama, _) = AutoCompleteRecommendedDeck(Array.Empty<CardData>(), Array.Empty<CardData>(), CardColor.Black);
        return (main, digitama);
    }

    public static (List<CardData> MainDeck, List<CardData> DigitamaDeck) CreateSt6VenomVioletPreset()
    {
        var (main, digitama, _) = AutoCompleteRecommendedDeck(Array.Empty<CardData>(), Array.Empty<CardData>(), CardColor.Purple);
        return (main, digitama);
    }

    private static void AddCopies(List<CardData> list, CardData card, int count)
    {
        for (int i = 0; i < count; i++)
        {
            list.Add(card);
        }
    }
}
