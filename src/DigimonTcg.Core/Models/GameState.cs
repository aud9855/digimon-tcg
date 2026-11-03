using System.Collections.Generic;
using System.Linq;

namespace DigimonTcg.Core.Models;

public record SecurityCheckHistoryItem(
    CardData SecurityCard,
    string AttackerName,
    int AttackerDp,
    bool IsBattleWon,
    string OutcomeSummary = ""
)
{
    // 화면에 나가는 문장: 영어 카드 이름 → 공식 한글 이름 (KoreanNameLocalizer)
    public string AttackerName { get; init; } = Engine.Effects.KoreanNameLocalizer.Localize(AttackerName);
    public string OutcomeSummary { get; init; } = Engine.Effects.KoreanNameLocalizer.Localize(OutcomeSummary);
}

/// <summary>상대(AI) 턴에 일어난 행동 1건 — UI가 상대 턴을 한 장면씩 다시 보여 주는 데 사용한다.</summary>
public enum OpponentActionKind { TurnStart, Breeding, Evolve, Play, Tamer, Attack, Block, SecurityResult, DirectAttack, TurnEnd, Effect }

public record OpponentActionEvent(
    OpponentActionKind Kind,
    string Title,
    string Detail,
    CardData? Card = null,
    CardData? SubCard = null
)
{
    // 화면에 나가는 문장: 영어 카드 이름 → 공식 한글 이름
    public string Title { get; init; } = Engine.Effects.KoreanNameLocalizer.Localize(Title);
    public string Detail { get; init; } = Engine.Effects.KoreanNameLocalizer.Localize(Detail);
}

/// <summary>AI의 어택에 대해 플레이어가 블록할지(어느 블로커로 막을지) 고를 차례 — 공식 룰상 블록은 방어 측의 선택이다.</summary>
public record PendingBlockDecision(
    CardInstance Attacker,
    IReadOnlyList<CardInstance> Candidates,
    int AttackerDp,
    int SecurityChecks
);

public class PlayerZones
{
    public Stack<CardData> DigitamaDeck { get; set; } = new Stack<CardData>();
    public List<CardData> MainDeck { get; set; } = new List<CardData>();
    public List<CardData> Hand { get; set; } = new List<CardData>();
    public List<CardData> SecurityStack { get; set; } = new List<CardData>();
    public List<CardData> Trash { get; set; } = new List<CardData>();

    public CardInstance? BreedingArea { get; set; }
    public List<CardInstance> BattleArea { get; set; } = new List<CardInstance>();

    /// <summary>v0.43.0 이 쪽의 지금 메모리 (엔진이 채운다 — "for each memory your opponent has" 셈)</summary>
    public Func<int>? MemoryProbe43 { get; set; }
    public List<CardData> Tamers { get; set; } = new List<CardData>();

    // 자신의 테이머 아래에 놓인 카드 전부 (≪세이브≫ 등 — 놓인 순서, 뒤가 아래). v0.85.0 어느 테이머 아래인지는 UnderTamerHosts85
    public List<CardData> UnderTamers { get; set; } = new List<CardData>();

    /// <summary>
    /// v0.85.0 테이머 아래 카드 → 그 카드가 놓인 테이머 (카드 객체 → 테이머 카드 객체). 종합 룰 4-4-2 · 4-7-8 · 16-20 (≪세이브≫ "자신의 테이머 1명의 밑에"):
    /// 테이머 아래 카드는 테이머마다 따로 겹쳐지고, 그 테이머가 에어리어를 떠나면 그 아래 카드만 함께 파기된다.
    /// 테이머마다의 순서 = UnderTamers 안의 순서 (뒤가 아래). 놓을 테이머를 정하지 않고 넣은 카드(테스트 준비)는 첫 테이머 아래로 본다
    /// </summary>
    public Dictionary<CardData, CardData> UnderTamerHosts85 { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>v0.85.0 이 테이머 아래 카드가 놓인 테이머 (그 테이머가 필드에 없으면 null — 떠난 테이머 아래에 남은 카드)</summary>
    public CardData? HostOfUnderTamer(CardData under)
    {
        if (UnderTamerHosts85.TryGetValue(under, out var host))
        {
            foreach (var t in Tamers) if (ReferenceEquals(t, host)) return host;
            return null;
        }
        return Tamers.Count > 0 ? Tamers[0] : null;
    }

    /// <summary>v0.85.0 테이머 tamer 아래의 카드 (위 → 아래)</summary>
    public List<CardData> UnderOf(CardData tamer) => UnderTamers.Where(x => ReferenceEquals(HostOfUnderTamer(x), tamer)).ToList();

    /// <summary>v0.85.0 테이머 tamer 아래의 카드 장 수</summary>
    public int UnderCountOf(CardData tamer) => UnderTamers.Count == 0 ? 0 : UnderTamers.Count(x => ReferenceEquals(HostOfUnderTamer(x), tamer));

    /// <summary>v0.85.0 테이머 tamer 아래의 앞면 카드 (정보를 참조하는 곳 — 종합 룰 4-7-9)</summary>
    public IEnumerable<CardData> FaceUpUnderOf(CardData tamer) => UnderTamers.Where(x => ReferenceEquals(HostOfUnderTamer(x), tamer) && !IsFaceDownUnderTamer(x));

    /// <summary>v0.85.0 테이머 tamer 아래의 뒷면 카드 장 수</summary>
    public int FaceDownUnderCountOf(CardData tamer) => FaceDownUnderTamers.Count == 0 ? 0 : UnderTamers.Count(x => FaceDownUnderTamers.Contains(x) && ReferenceEquals(HostOfUnderTamer(x), tamer));

    /// <summary>v0.85.0 카드를 테이머 tamer 아래 맨 아래에 놓는다 (종합 룰 4-4-2 — 원래 있던 곳에서 이미 뺀 카드)</summary>
    public void PlaceUnderTamer85(CardData tamer, CardData card)
    {
        UnderTamers.Add(card);
        UnderTamerHosts85[card] = tamer;
    }

    /// <summary>v0.85.0 테이머 아래 카드 1장을 뺀다 (어느 테이머 아래든 · 뒷면 표시도 지운다). 없었으면 false</summary>
    public bool RemoveUnderTamer85(CardData card)
    {
        int i = UnderTamers.FindIndex(x => ReferenceEquals(x, card));
        if (i < 0) return false;
        UnderTamers.RemoveAt(i);
        if (!UnderTamers.Any(x => ReferenceEquals(x, card)))
        {
            FaceDownUnderTamers.Remove(card);
            UnderTamerHosts85.Remove(card);
        }
        return true;
    }

    // v0.35.0 테이머 아래에 뒷면으로 놓인 카드 (참조로 구분 — 놓을 때 엔진이 카드를 복사한다). 종합 룰 4-7-4 · 4-7-9: 지정이 없으면 앞면, 뒷면 카드는 정보를 참조할 수 없다
    public HashSet<CardData> FaceDownUnderTamers { get; set; } = new HashSet<CardData>(ReferenceEqualityComparer.Instance);

    // v0.35.0 테이머의 "can't suspend / can't unsuspend until ~" (값 = GameState.TurnSequence 기준 끝나는 턴). 같은 카드 객체로 구분한다
    public Dictionary<CardData, int> TamerCannotSuspendUntil { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<CardData, int> TamerCannotUnsuspendUntil { get; } = new(ReferenceEqualityComparer.Instance);

    // v0.36.0 플레이어 제한 기간 (값 = 끝나는 GameState.TurnSequence): 효과로 디지몬 · 테이머를 등장시킬 수 없다 · 트래시에서 효과로 등장시킬 수 없다 · 옵션 카드를 사용할 수 없다
    public int CannotPlayDigimonByEffectsUntil { get; set; }
    public int CannotPlayTamersByEffectsUntil { get; set; }
    public int CannotPlayFromTrashByEffectsUntil { get; set; }
    public int CannotUseOptionsUntil { get; set; }

    /// <summary>v0.59.0 이 쪽의 디지몬 · 테이머는 【등장 시】 효과를 발휘할 수 없다 (기간 — BT20-037 "none of their Digimon or Tamers can activate [On Play] effects until the end of their turn")</summary>
    public int OnPlayBlockedUntil59 { get; set; }

    /// <summary>v0.60.0 이 쪽이 하는 체크에서는 상대의 【시큐리티】 효과가 발휘되지 않는다 (기간 — EX3-073 "none of your opponent's [Security] effects can activate for the turn")</summary>
    public int NoEnemySecurityUntil60 { get; set; }

    /// <summary>v0.51.0 이 쪽이 마지막으로 진화한 턴 (GameState.TurnSequence, 없으면 -1) — "If you've digivolved this turn" (BT1-007)</summary>
    public int DigivolvedTurnSeq51 { get; set; } = -1;

    /// <summary>v0.37.0 "can't play (or move) Digimon with N DP or less until ~" (EX3-012 · EX7-014): (DP 상한, 끝나는 TurnSequence, 육성 에어리어에서 이동도 막는지)</summary>
    public List<(int Dp, int Until, bool Move)> PlayDpLocks { get; } = new();

    /// <summary>v0.37.0 상대 필드의 지속 효과 "they can't play Digimon with N DP or less" (BT14-017) — 엔진이 지속 효과를 계산할 때 채운다 (0 = 없음)</summary>
    public int StaticPlayDpLock { get; set; }

    /// <summary>v0.64.0 이 쪽의 턴 종료 조건: 메모리가 상대 쪽의 N 이상 (기본 1 — 종합 룰 6-1-4-1, BT14-081 · BT17-069는 3) — 엔진이 지속 효과를 계산할 때 채운다</summary>
    public int TurnEndMemory64 { get; set; } = 1;

    /// <summary>v0.65.0 이 쪽의 효과로는 시큐리티에 카드를 추가할 수 없다 (기간 — BT9-103 "cards can't be added to security stacks by your opponent's effects", 값 = 끝나는 TurnSequence)</summary>
    public int NoSecurityAdd65 { get; set; }

    /// <summary>v0.65.0 효과로 이 쪽의 테이머에게 준 효과 문장 (EX10-058 "give 1 of their Digimon or Tamers "~"") — 그 테이머(카드 객체)가 필드에 있는 동안 기간 안에 그 테이머의 효과처럼 발휘</summary>
    public List<(CardData Tamer, CardData Effect, int Until, PlayerZones? Granter)> TamerGrants65 { get; } = new();

    /// <summary>v0.66.0 효과로 원래 색이 바뀐 이 쪽의 테이머 (BT18-078 — 테이머 카드 객체 · 색 · 끝나는 TurnSequence). 엔진이 기간이 끝나거나 테이머가 떠나면 지운다</summary>
    public List<(CardData Tamer, CardColor Color, int Until)> TamerColors66 { get; } = new();

    /// <summary>v0.66.0 이 쪽의 테이머 t의 지금 색 (효과로 원래 색이 바뀌었으면 그 색)</summary>
    public IReadOnlyList<CardColor> TamerColorsOf66(CardData t)
    {
        if (TamerColors66.Count > 0)
            foreach (var x in TamerColors66)
                if (ReferenceEquals(x.Tamer, t)) return new[] { x.Color };
        return t.AllColors;
    }

    /// <summary>테이머 아래 카드가 뒷면인지</summary>
    public bool IsFaceDownUnderTamer(CardData card) => FaceDownUnderTamers.Count > 0 && FaceDownUnderTamers.Contains(card);

    /// <summary>테이머 아래 뒷면 카드 장 수</summary>
    public int FaceDownUnderTamerCount => FaceDownUnderTamers.Count == 0 ? 0 : UnderTamers.Count(FaceDownUnderTamers.Contains);

    /// <summary>테이머 아래 앞면 카드 (정보를 참조하는 곳 — ≪세이브≫ · 디지크로스 · 등장)</summary>
    public IEnumerable<CardData> FaceUpUnderTamers => FaceDownUnderTamers.Count == 0 ? UnderTamers : UnderTamers.Where(c => !FaceDownUnderTamers.Contains(c));

    /// <summary>카드를 테이머 아래에 뒷면으로 놓는다 (복사본을 놓고 반환 — 맨 아래 = 목록의 끝). v0.85.0 테이머를 정하지 않으면 첫 테이머 아래로 본다</summary>
    public CardData AddUnderTamerFaceDown(CardData card) => AddUnderTamerFaceDown(null, card);

    /// <summary>v0.85.0 카드를 테이머 tamer 아래 맨 아래에 뒷면으로 놓는다 (복사본을 놓고 반환)</summary>
    public CardData AddUnderTamerFaceDown(CardData? tamer, CardData card)
    {
        var copy = CardInstance.CopyCard(card);
        UnderTamers.Add(copy);
        FaceDownUnderTamers.Add(copy);
        if (tamer != null) UnderTamerHosts85[copy] = tamer;
        return copy;
    }

    // 배틀 에어리어에 놓인 옵션 카드 (≪딜레이≫ — 놓은 다음 턴부터 파기해서 효과를 발휘한다)
    public List<PlacedOption> OptionsInPlay { get; set; } = new List<PlacedOption>();

    // 앞면으로 놓인 시큐리티 카드 (참조로 구분 — 앞면으로 놓을 때 엔진이 카드를 복사해 위치마다 구분된다)
    public HashSet<CardData> FaceUpSecurity { get; set; } = new HashSet<CardData>(ReferenceEqualityComparer.Instance);

    // 레스트 상태인 테이머 (같은 카드 객체가 여러 장일 수 있어 장 수로 센다) — 자신의 액티브 페이즈에 비운다
    public List<CardData> SuspendedTamers { get; set; } = new List<CardData>();

    /// <summary>이 테이머 카드 중 액티브 상태인 장이 있는지</summary>
    public bool HasActiveTamer(CardData tamer) =>
        Tamers.Count(t => ReferenceEquals(t, tamer)) > SuspendedTamers.Count(t => ReferenceEquals(t, tamer));
}

/// <summary>배틀 에어리어에 놓인 옵션 카드 1장 (PlacedTurn = 놓은 GameState.TurnSequence)</summary>
public sealed class PlacedOption
{
    public CardData Card { get; init; } = new();
    public int PlacedTurn { get; init; }
}

public class GameState
{
    public PracticeStage CurrentStage { get; set; } = PracticeStage.Stage1_Goldfish;
    public int CurrentTurn { get; set; } = 1;
    public Phase CurrentPhase { get; set; } = Phase.Active;

    // 메모리 위치: -10 (상대 우세) ~ +10 (플레이어 우세)
    // 공식 룰 1-4-2-2: 메모리는 양쪽 모두 10을 넘지 않는다 (10을 넘는 코스트도 지불할 수 있고, 마커는 10에서 멈춘다)
    private int _memoryGauge;
    public int MemoryGauge
    {
        get => _memoryGauge;
        set => _memoryGauge = Math.Clamp(value, -10, 10);
    }

    // 코인 토스 결과: 플레이어 선공 여부 (선공은 1턴째 드로우 없음)
    public bool PlayerGoesFirst { get; set; } = true;

    // 턴 순번: 플레이어 턴 · 상대 턴이 시작될 때마다 1씩 증가 ("다음 상대의 턴 종료 시까지" 같은 기간 효과 기준)
    public int TurnSequence { get; set; }

    // "이 턴 종료 시, 메모리를 -3 한다" (메탈그레이몬 BT1-021 등) — 턴 종료 시 상대에게 추가로 넘어가는 메모리
    public int PendingEndOfTurnMemoryLoss { get; set; }

    // 시큐리티 디지몬 DP 버프 (스타라이트 익스플로전 · 천국의 문 등): (DP, 유효한 마지막 TurnSequence)
    public List<(int Dp, int Until)> PlayerSecurityDpBuffs { get; set; } = new();
    public List<(int Dp, int Until)> OpponentSecurityDpBuffs { get; set; } = new();

    // 섀도 윙 [시큐리티]: "다음 자신의 턴 종료 시까지 자신의 디지몬 전부 ≪시큐리티 어택 +1≫" (유효한 마지막 TurnSequence)
    public int PlayerAllSecurityAttackUntil { get; set; }
    public int OpponentAllSecurityAttackUntil { get; set; }

    // 이번 상대 턴에 상대 디지몬이 어택했는지 (토이아구몬 ST5-04 등 [상대의 턴 종료 시] 효과용)
    public bool OpponentAttackedThisTurn { get; set; }

    // 1턴 시작 시 멀리건(손패 1회 다시 뽑기) 가능 여부
    public bool CanMulligan { get; set; } = true;

    // 육성 페이즈 액션 수행 여부 (턴당 1회: 부화 / 이동 / 패스 중 택 1)
    public bool BreedingActionTaken { get; set; }

    // 상대 시큐리티 체크 이력 및 내 시큐리티 피격 이력
    public List<SecurityCheckHistoryItem> SecurityCheckHistory { get; set; } = new List<SecurityCheckHistoryItem>();
    public List<SecurityCheckHistoryItem> PlayerSecurityCheckHistory { get; set; } = new List<SecurityCheckHistoryItem>();
    public CardData? LastRevealedSecurityCard { get; set; }
    private string _lastSecurityBattleResult = string.Empty;
    // 화면에 나가는 문장: 영어 카드 이름 → 공식 한글 이름
    public string LastSecurityBattleResult { get => _lastSecurityBattleResult; set => _lastSecurityBattleResult = Engine.Effects.KoreanNameLocalizer.Localize(value); }
    public bool LastSecurityBattleWon { get; set; } = true;

    // 게임 종료 및 타임어택 평가 지표 (HUD용)
    public int TargetTurns { get; set; } = 5;
    public bool IsGameOver { get; set; }
    public bool IsVictory { get; set; }
    public string GameOverReason { get; set; } = string.Empty;

    public int TotalMemorySpent { get; set; }
    public int TotalEvolutionsCount { get; set; }
    public int Lv3StartedChains { get; set; }
    public int Lv6CompletedChains { get; set; }
    public List<string> ActionLogs { get; set; } = new List<string>();

    // 직전 상대 턴의 행동 목록 (UI가 리플레이 후 비운다)
    public List<OpponentActionEvent> OpponentTurnEvents { get; set; } = new List<OpponentActionEvent>();

    // AI 턴이 플레이어의 블록 선택을 기다리며 멈춰 있는 상태 (null = 대기 없음)
    public PendingBlockDecision? PendingBlock { get; set; }

    // ≪진격≫으로 어택할 수 있게 된 내 디지몬 — 메모리가 상대 쪽이어도 턴이 끝나지 않고 이 디지몬의 어택 1번만 할 수 있다
    public CardInstance? BlitzAttacker { get; set; }

    public PlayerZones Player { get; set; } = new PlayerZones();
    public PlayerZones DummyOpponent { get; set; } = new PlayerZones();
    public string OpponentDeckName { get; set; } = "🤖 자동 카운터 덱";

    public double MemoryEfficiencyPerTurn =>
        CurrentTurn > 0 ? (double)TotalMemorySpent / CurrentTurn : 0.0;

    public double EvolutionChainSuccessRate =>
        Lv3StartedChains > 0 ? (double)Lv6CompletedChains / Lv3StartedChains * 100.0 : 0.0;
}
