namespace DigimonTcg.Core.Models;

public class CardData
{
    public string CardId { get; set; } = string.Empty;              // 예: "ST1-03"
    public string Name { get; set; } = string.Empty;                // 예: "아구몬"
    /// <summary>
    /// 공식 영어 카드 이름 (내장 한글 카드만 채움, 예: ST1-07 "Greymon"). API 카드는 Name이 이미 영어라 null.
    /// 효과 문장의 이름 조건("[Greymon]" · "with [X] in its name" 등)은 Name과 이 이름을 함께 본다 → Models.CardNames
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? EnglishName { get; set; }
    public CardType Type { get; set; }
    public CardColor Color { get; set; }
    public CardColor? SecondaryColor { get; set; }                  // 여러 색 카드의 두 번째 색 (없으면 null)

    /// <summary>카드의 모든 색 (여러 색 카드면 2개)</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<CardColor> AllColors =>
        SecondaryColor is CardColor second && second != Color ? new[] { Color, second } : new[] { Color };
    public int Level { get; set; }                                  // Lv.2 ~ Lv.7 ( 테이머/옵션은 0 )
    public int PlayCost { get; set; }                               // 등장 코스트
    public int EvoCost { get; set; }                                // 진화 코스트
    public int EvoRequirementLevel { get; set; }                    // 필요 진화 전 레벨
    public CardColor EvoRequirementColor { get; set; }
    public int DP { get; set; }                                     // 기본 전투력
    public string EffectText { get; set; } = string.Empty;          // 메인 효과 설명
    public string InheritedEffectText { get; set; } = string.Empty; // 진화원 효과 설명 (듀얼 카드는 옵션 쪽 — IsDual)

    /// <summary>
    /// 듀얼 카드 (API type "Dual", 종합 룰 4-6): 디지몬 쪽(EffectText) + 옵션 쪽(InheritedEffectText 칸에 들어 있다).
    /// 패에서는 디지몬 쪽으로 진화하거나 옵션 쪽으로 사용한다 (등장하지 않는다). PlayCost = 옵션 쪽 사용 코스트.
    /// 진화원이 되어도 옵션 쪽은 진화원 효과가 아니다
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsDual { get; set; }

    /// <summary>v0.36.0 디지몬으로도 취급하는 테이머를 배틀 에어리어에서 나타내는 복사본 (효과 문장 없음 — 효과는 테이머 쪽에서). 저장하지 않는다</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsTamerProxy { get; set; }

    /// <summary>진화원 효과 칸의 문장 (듀얼 카드의 옵션 쪽은 진화원 효과가 아니므로 빈 문자열)</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string SourceEffectText => IsDual ? string.Empty : InheritedEffectText;
    public List<string> Traits { get; set; } = new();               // 특징 (API의 유형 · 세대 · 속성, 예: "Royal Knight", "Ultimate", "Vaccine")

    // 인게임 자동 계산을 위한 보조 수치
    public bool IsBlocker { get; set; }                             // ≪블로커≫ 보유 여부
    public int InheritedDpBonus { get; set; }                       // 진화원 DP 버프 (예: +1000)
    public int SecurityAttackBonus { get; set; }                    // 기본 시큐리티 어택 추가 수치
    public int InheritedSecurityAttackBonus { get; set; }           // 진화원 시큐리티 어택 추가 수치
    public string ImageUrl { get; set; } = string.Empty;            // 카드 이미지 URL (API 연동용)
    public string Rarity { get; set; } = string.Empty;              // 카드 등급: C / U / R / SR / SEC / P (API 동기화로 채워짐)

    // 내장 한글 카드(카드별 효과 스크립트가 있는 120장)인지 여부 — 이미지 미리 받기 · 로비 그림 · 덱 자동 완성에 쓴다.
    // 카드 효과가 대전에서 얼마나 동작하는지(API 카드 포함)는 Data.EffectSupport.For(card)로 확인한다.
    // 카드 번호로 판별하므로 덱 프리셋 · 손패 · 필드에서 새로 만든 카드 객체도 같은 결과가 나온다.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEffectImplemented => DigimonTcg.Core.Data.StarterDecks.IsBuiltInCard(CardId);

    // ---- 화면 표시용 (data/kr/cards_ko.json 공식 한글 데이터가 있으면 그것, 없으면 원래 값) ----
    // 대전 엔진 · 문장 파서는 Name · EffectText · Traits(영어 API 기준)를 그대로 쓴다.
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName => DigimonTcg.Core.Data.KoreanCardData.DisplayName(this);

    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayEffectText => DigimonTcg.Core.Data.KoreanCardData.DisplayEffectText(this);

    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayInheritedEffectText => DigimonTcg.Core.Data.KoreanCardData.DisplayInheritedEffectText(this);

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> DisplayTraits => DigimonTcg.Core.Data.KoreanCardData.DisplayTraits(this);

    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplaySummary =>
        Type == CardType.Digitama
            ? $"[{CardId}] {DisplayName} (Lv.{Level} 디지타마)"
            : Type == CardType.Digimon
                ? $"[{CardId}] {DisplayName} (Lv.{Level} | DP {DP} | 등장 {PlayCost} / 진화 {EvoCost})"
                : $"[{CardId}] {DisplayName} ({Type} | 코스트 {PlayCost})";
}
