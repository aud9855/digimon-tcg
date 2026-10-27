using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine.Ai;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// 1인 솔리테어 (Stage 1), 블로커 돌파 챌린지 (Stage 2), 실전 AI 대전 (Stage 3) 통합 룰 엔진
/// </summary>
public partial class GoldfishEngine
{
    public GameState State { get; }
    private readonly Random _rng = new();

    /// <summary>
    /// true면 AI의 어택을 막을지 플레이어가 고른다 (State.PendingBlock에서 멈추고 ResolvePendingBlock으로 이어 감 — UI가 켠다).
    /// false면 (테스트 · CLI) 가장 DP가 높은 블로커로 자동 블록한다.
    /// </summary>
    public bool PlayerChoosesBlocks { get; set; }

    /// <summary>진행 중인 AI 턴 (플레이어의 블록 선택을 기다리는 동안 남은 어택 · 메모리를 보관)</summary>
    private sealed class AiTurnContext
    {
        public int Memory;
        public int EndOfTurnMemoryLoss;
        public bool IsOpeningTurn;
        public readonly Queue<CardInstance> Attackers = new();
        public CardInstance? AttackTarget;    // 지금 어택의 대상 (≪돌진≫으로 바꾼 내 디지몬, 없으면 시큐리티)
        public CardInstance? BlitzAttacker;   // ≪진격≫으로 메모리가 상대 쪽이어도 어택할 수 있는 AI 디지몬
        public int AttacksDeclared;           // 이번 턴에 정책에서 받은 어택 수 (무한 반복 방지)
        public bool EndPhase;                 // 턴 종료 단계 ([자신의 턴 종료 시] 효과 뒤 ≪볼텍스≫ · ≪에그제큐트≫ 어택)
        public readonly Dictionary<CardInstance, CardInstance?> QueuedTargets = new(ReferenceEqualityComparer.Instance);   // 효과로 큐에 넣은 어택자 → 대상 (null = 시큐리티)
        public readonly HashSet<CardInstance> NoSuspendAttackers = new(ReferenceEqualityComparer.Instance);                // "may attack without suspending"
    }

    private AiTurnContext? _aiTurn;

    /// <summary>AI 턴의 결정 정책 (진화 · 옵션 · 어택 · 등장 · [메인] 기동) — 결정만 돌려주고 처리는 엔진이 한다</summary>
    public IAiPolicy AiPolicy { get; set; } = new SmartAiPolicy();

    /// <summary>AI가 한 턴에 선언할 수 있는 어택 수 상한 (액티브로 돌아오는 효과의 무한 반복 방지)</summary>
    private const int MaxAiAttacksPerTurn = 20;

    public GoldfishEngine(GameState state)
    {
        State = state;
    }

    public void Log(string message)
    {
        // 화면에 나가는 문장: 영어 카드 이름 → 공식 한글 이름 (엔진 판정은 영어 이름 그대로)
        // AI 턴 동안 엔진의 단계 값은 플레이어 기준 TurnEnd — 머리말은 "상대 턴"으로 (AI 로그는 🤖로 시작).
        // 턴 시작 줄은 단계를 바꾸기 전에 남기므로 "턴 시작"으로
        string phase = _aiTurn != null || message.StartsWith("🤖", StringComparison.Ordinal) ? "상대 턴"
            : message.StartsWith("=== ", StringComparison.Ordinal) && message.Contains("턴 시작", StringComparison.Ordinal) ? "턴 시작"
            : PhaseLabel(State.CurrentPhase);
        string entry = $"[T{State.CurrentTurn} | {phase} | M:{State.MemoryGauge:+#;-#;0}] {KoreanNameLocalizer.Localize(message)}";
        State.ActionLogs.Add(entry);
    }

    /// <summary>로그 머리말의 단계 이름 (공식 한글판 표기)</summary>
    private static string PhaseLabel(Phase phase) => phase switch
    {
        Phase.Active => "액티브",
        Phase.Draw => "드로우",
        Phase.Breeding => "육성",
        Phase.Main => "메인",
        Phase.TurnEnd => "턴 종료",
        Phase.Setup => "준비",
        _ => phase.ToString(),
    };

    /// <summary>
    /// 덱 및 선택한 스테이지/상대방 덱으로 새 게임 초기화
    /// </summary>
    public void StartNewGame(
        IReadOnlyList<CardData> mainDeck,
        IReadOnlyList<CardData> digitamaDeck,
        int? seed = null,
        PracticeStage? stage = null,
        IReadOnlyList<CardData>? opponentMainDeck = null,
        IReadOnlyList<CardData>? opponentDigitamaDeck = null,
        string? opponentDeckName = null)
    {
        var rng = seed.HasValue ? new Random(seed.Value) : _rng;
        if (stage.HasValue)
        {
            State.CurrentStage = stage.Value;
        }

        State.CurrentTurn = 1;
        State.CurrentPhase = Phase.Active;
        State.PlayerGoesFirst = true;
        // 공식 룰: 메모리 게이지는 0에서 시작하므로 실전 대련의 선공은 첫 턴을 메모리 0으로 시작한다 (연습 모드는 매 턴 3)
        State.MemoryGauge = State.CurrentStage == PracticeStage.Stage3_AiBattle ? 0 : 3;
        State.CanMulligan = true;
        State.BreedingActionTaken = false;
        State.IsGameOver = false;
        State.IsVictory = false;
        State.GameOverReason = string.Empty;
        State.TotalMemorySpent = 0;
        State.TotalEvolutionsCount = 0;
        State.Lv3StartedChains = 0;
        State.Lv6CompletedChains = 0;
        State.SecurityCheckHistory.Clear();
        State.PlayerSecurityCheckHistory.Clear();
        State.LastRevealedSecurityCard = null;
        State.LastSecurityBattleResult = string.Empty;
        State.LastSecurityBattleWon = true;
        State.ActionLogs.Clear();
        State.OpponentTurnEvents.Clear();
        State.PendingBlock = null;
        State.BlitzAttacker = null;
        _aiTurn = null;
        _scheduledDeletions.Clear();
        _scheduledReturns39.Clear();
        _scheduledActs39.Clear();
        _afterSecurityBattle39.Clear();
        _tamerEntries.Clear();
        State.TurnSequence = 0;
        State.PendingEndOfTurnMemoryLoss = 0;
        State.PlayerSecurityDpBuffs.Clear();
        State.OpponentSecurityDpBuffs.Clear();
        State.PlayerAllSecurityAttackUntil = 0;
        State.OpponentAllSecurityAttackUntil = 0;
        State.OpponentAttackedThisTurn = false;

        State.TargetTurns = State.CurrentStage switch
        {
            PracticeStage.Stage1_Goldfish => 5,
            PracticeStage.Stage2_BlockerDefense => 6,
            PracticeStage.Stage3_AiBattle => 8,
            _ => 5
        };

        State.Player = new PlayerZones();
        State.DummyOpponent = new PlayerZones();

        // 플레이어 디지타마 덱 셔플 후 적재
        foreach (var d in digitamaDeck.OrderBy(_ => rng.Next()))
        {
            State.Player.DigitamaDeck.Push(d);
        }

        // 플레이어 메인 덱 셔플
        var shuffledMain = mainDeck.OrderBy(_ => rng.Next()).ToList();
        State.Player.MainDeck.AddRange(shuffledMain);

        // 공식 룰 5-2-1-4: 초기 손패 5장 드로우 (시큐리티는 멀리건이 끝난 뒤 BeginGame에서 놓는다)
        DealInitialHand(State.Player);

        // 스테이지별 상대방 세팅 (기초 덱 또는 사용자 지정 덱)
        SetupOpponentForStage(rng, opponentMainDeck, opponentDigitamaDeck, opponentDeckName);

        string stageTitle = State.CurrentStage switch
        {
            PracticeStage.Stage1_Goldfish => "Stage 1: 기본 타임어택 (골드피시)",
            PracticeStage.Stage2_BlockerDefense => "Stage 2: 블로커 & 수비진 돌파 챌린지",
            PracticeStage.Stage3_AiBattle => $"Stage 3: 실전 AI 대전 (상대 덱: {State.OpponentDeckName})",
            _ => "1인 연습 모드"
        };

        Log($"=== [{stageTitle}] 새 게임을 준비합니다! 초기 손패 5장을 받았습니다. ===");

        // 공식 룰 5-2: 선공 결정 → 손패 5장 → (선공부터) 멀리건 1회 → 시큐리티 5장 → 게임 시작.
        // 여기서는 손패까지만 나눠 주고 '게임 준비' 단계에서 멈춘다 → DecideFirstPlayer / ExecuteMulligan / BeginGame
        _setupRng = rng;
        _firstPlayerDecided = false;
        State.CurrentPhase = Phase.Setup;
    }

    private Random _setupRng = new();
    private bool _firstPlayerDecided;

    /// <summary>게임 준비 단계에서 선공 / 후공이 정해졌는지 (UI가 코인 토스 뒤 멀리건 화면을 띄울 때 사용)</summary>
    public bool IsFirstPlayerDecided => _firstPlayerDecided;

    private static void DealInitialHand(PlayerZones zones)
    {
        for (int i = 0; i < 5 && zones.MainDeck.Count > 0; i++)
        {
            zones.Hand.Add(zones.MainDeck[0]);
            zones.MainDeck.RemoveAt(0);
        }
    }

    /// <summary>손패를 모두 덱에 되돌려 섞고 5장을 다시 뽑는다 (공식 룰 5-2-1-5)</summary>
    private void RedrawInitialHand(PlayerZones zones)
    {
        zones.MainDeck.AddRange(zones.Hand);
        zones.Hand.Clear();
        var shuffled = zones.MainDeck.OrderBy(_ => _setupRng.Next()).ToList();
        zones.MainDeck.Clear();
        zones.MainDeck.AddRange(shuffled);
        DealInitialHand(zones);
    }

    /// <summary>
    /// 선공 / 후공 결정 (게임 준비 단계에서 1회). 실전 AI 대전에서 AI가 선공이면 AI가 먼저 멀리건 여부를 정한다.
    /// </summary>
    public ActionResult DecideFirstPlayer(bool playerGoesFirst)
    {
        if (State.CurrentPhase != Phase.Setup)
            return ActionResult.Fail("선공 / 후공은 게임 시작 전에만 정할 수 있습니다.");
        if (_firstPlayerDecided)
            return ActionResult.Fail("선공 / 후공은 이미 정해졌습니다.");
        if (!playerGoesFirst && State.CurrentStage != PracticeStage.Stage3_AiBattle)
            return ActionResult.Fail("상대 선공은 실전 AI 대전에서만 사용할 수 있습니다.");

        _firstPlayerDecided = true;
        State.PlayerGoesFirst = playerGoesFirst;
        Log(playerGoesFirst
            ? "🎲 선공 결정: 플레이어 선공 / 상대 후공"
            : "🎲 선공 결정: 상대 AI 선공 / 플레이어 후공");

        // 공식 룰 5-2-1-4: 멀리건 선언은 선공부터
        if (!playerGoesFirst)
            DecideAiMulligan();
        return ActionResult.Success(playerGoesFirst ? "내가 선공입니다." : "상대 AI가 선공입니다.");
    }

    /// <summary>AI의 멀리건: 처음에 낼 수 있는 Lv.3 디지몬이 손패에 없으면 다시 뽑는다</summary>
    private void DecideAiMulligan()
    {
        if (State.CurrentStage != PracticeStage.Stage3_AiBattle)
            return;
        var hand = State.DummyOpponent.Hand;
        bool hasStarter = hand.Any(c => c.Type == CardType.Digimon && c.Level <= 3);
        if (hasStarter)
        {
            Log("🤖 상대 AI는 초기 손패를 그대로 사용합니다.");
            return;
        }
        RedrawInitialHand(State.DummyOpponent);
        Log("🤖 상대 AI가 멀리건을 선언하고 초기 손패 5장을 다시 뽑았습니다.");
    }

    /// <summary>
    /// 멀리건까지 끝난 뒤 게임을 시작한다: 양쪽 시큐리티 5장을 덱 위에서 놓고 (공식 룰 5-2-1-6) 선공의 1턴을 시작한다.
    /// playerGoesFirst를 넘기면 선공 결정도 함께 한다 (아직 정하지 않았을 때).
    /// </summary>
    public ActionResult BeginGame(bool? playerGoesFirst = null)
    {
        if (State.CurrentPhase != Phase.Setup)
            return ActionResult.Fail("게임이 이미 시작되었습니다.");
        if (!_firstPlayerDecided)
        {
            var decided = DecideFirstPlayer(playerGoesFirst ?? true);
            if (!decided.CanExecute)
                return decided;
        }

        State.CanMulligan = false;
        // 공식 룰 5-2-1-4: 후공은 선공 다음에 멀리건을 정한다
        if (State.PlayerGoesFirst)
            DecideAiMulligan();

        PlaceSecurity(State.Player);
        if (State.CurrentStage == PracticeStage.Stage3_AiBattle)
            PlaceSecurity(State.DummyOpponent);   // 연습 모드 더미는 준비 단계에서 무작위 시큐리티를 이미 받았다
        Log("🛡️ 양쪽 모두 덱 위에서 시큐리티 5장을 놓았습니다. 게임 시작!");

        if (State.PlayerGoesFirst)
        {
            State.CurrentTurn = 1;
            Log("=== 1턴 시작! (선공) ===");
            ExecuteActivePhase();
            ExecuteDrawPhase();
            State.CurrentPhase = Phase.Breeding;
            Log("육성 페이즈 시작: [부화], [배틀 이동] 또는 [패스]를 선택하세요.");
            return ActionResult.Success("게임 시작! 선공 1턴 — 메모리 0 · 드로우 없이 육성 페이즈부터 시작합니다.");
        }

        // AI 선공: AI의 1턴은 0번째 턴으로 진행하고, 이어지는 내 턴을 1턴으로 센다
        // (같은 번호를 쓰면 AI가 첫 턴에 낸 디지몬이 다음 AI 턴에 '이번 턴에 등장'으로 보여 공격하지 못한다 — 공식 룰 7-1-2-1)
        State.CurrentTurn = 0;
        State.CurrentPhase = Phase.TurnEnd;
        BeginAiTurn(0, isOpeningTurn: true);   // 선공 첫 턴 = 메모리 0 (공격하지 않으므로 블록 선택으로 멈추지 않는다)
        if (State.IsGameOver)
            return ActionResult.Success(State.GameOverReason);
        return ActionResult.Success($"상대 AI 선공 턴이 끝났습니다. 후공 1턴 시작 (메모리 +{State.MemoryGauge}, 1장 드로우)");
    }

    /// <summary>공식 룰 5-2-1-6: 덱 위에서 5장을 한 장씩 뒷면으로 시큐리티에 놓는다</summary>
    private static void PlaceSecurity(PlayerZones zones)
    {
        for (int i = 0; i < 5 && zones.MainDeck.Count > 0; i++)
        {
            zones.SecurityStack.Add(zones.MainDeck[0]);
            zones.MainDeck.RemoveAt(0);
        }
    }

    private void SetupOpponentForStage(
        Random rng,
        IReadOnlyList<CardData>? customOpponentMain = null,
        IReadOnlyList<CardData>? customOpponentDigitama = null,
        string? customOpponentDeckName = null)
    {
        var catalog = StarterDecks.GetAllCatalogCards().ToDictionary(c => c.CardId);

        if (State.CurrentStage == PracticeStage.Stage3_AiBattle)
        {
            List<CardData> aiMain;
            List<CardData> aiDigitama;

            if (customOpponentMain != null && customOpponentMain.Count >= 10)
            {
                aiMain = customOpponentMain.ToList();
                aiDigitama = customOpponentDigitama != null && customOpponentDigitama.Count > 0
                    ? customOpponentDigitama.ToList()
                    : StarterDecks.CreateSt1GaiaRedPreset().DigitamaDeck;
                State.OpponentDeckName = !string.IsNullOrWhiteSpace(customOpponentDeckName)
                    ? customOpponentDeckName
                    : "🛠️ 내가 만든 커스텀 덱";
            }
            else
            {
                // 자동 배정: 플레이어 주 색상이 Red면 AI는 ST-2 Blue, 아니면 ST-1 Red 사용
                bool playerIsRed = State.Player.Hand.Concat(State.Player.MainDeck).Count(c => c.Color == CardColor.Red) >= 20;
                (aiMain, aiDigitama) = playerIsRed
                    ? StarterDecks.CreateSt2CocytusBluePreset()
                    : StarterDecks.CreateSt1GaiaRedPreset();
                State.OpponentDeckName = playerIsRed
                    ? "❄️ [기초 2] 코퀴토스 블루 (자동 배정)"
                    : "🔥 [기초 1] 가이아 레드 (자동 배정)";
            }

            foreach (var d in aiDigitama.OrderBy(_ => rng.Next()))
                State.DummyOpponent.DigitamaDeck.Push(d);

            State.DummyOpponent.MainDeck.AddRange(aiMain.OrderBy(_ => rng.Next()));

            // 공식 룰 5-2-1-4: 초기 손패 5장 (시큐리티는 멀리건이 끝난 뒤 BeginGame에서 덱 위 5장으로 놓는다)
            DealInitialHand(State.DummyOpponent);

            // 공식 룰: AI도 빈 필드에서 시작한다 (디지타마 부화 → 육성 → 배틀 이동 / 손패에서 등장)
            return;
        }

        State.OpponentDeckName = State.CurrentStage == PracticeStage.Stage2_BlockerDefense
            ? "🛡️ 블로커 돌파 훈련 수비대"
            : "🐣 5턴 타임어택 더미";

        // Stage 1 & Stage 2 공통: 무작위 Lv.3 ~ Lv.6 디지몬 5장으로 시큐리티 구성
        var dummyDigimonPool = catalog.Values
            .Where(c => c.Type == CardType.Digimon && c.Level <= 6)
            .ToList();

        for (int i = 0; i < 5; i++)
        {
            var randomDigimon = dummyDigimonPool[rng.Next(dummyDigimonPool.Count)];
            State.DummyOpponent.SecurityStack.Add(randomDigimon);
        }

        if (State.CurrentStage == PracticeStage.Stage2_BlockerDefense)
        {
            // Stage 2: 상대 필드에 액티브 ≪블로커≫ 코어드라몬(DP 6000) + 레스트 상태 가루몬(진화원 보유, DP 5000) 배치!
            var blocker = new CardInstance(catalog["ST1-06"], 0) { IsSuspended = false };
            var restedTarget = new CardInstance(catalog["ST2-03"], 0);
            restedTarget.Evolve(catalog["ST2-06"]);
            restedTarget.IsSuspended = true;

            State.DummyOpponent.BattleArea.Add(blocker);
            State.DummyOpponent.BattleArea.Add(restedTarget);
        }
    }

    /// <summary>
    /// 멀리건 (공식 룰 5-2-1-4 / 5-2-1-5): 게임 시작 전, 초기 손패 5장을 덱에 되돌려 섞고 5장을 다시 뽑는다 — 게임당 1회.
    /// 선공이 먼저 정하므로, AI 선공이면 선공 결정(DecideFirstPlayer) 뒤에만 할 수 있다.
    /// </summary>
    public ActionResult ExecuteMulligan()
    {
        if (State.CurrentPhase != Phase.Setup || !State.CanMulligan)
            return ActionResult.Fail("멀리건(손패 다시 뽑기)은 게임 시작 전에 1회만 할 수 있습니다.");

        RedrawInitialHand(State.Player);
        State.CanMulligan = false;
        Log("🔄 멀리건 실행: 손패 5장을 덱에 섞고 새로운 5장을 뽑았습니다.");
        return ActionResult.Success("🔄 멀리건 완료! 새로운 손패 5장을 뽑았습니다.");
    }

    /// <summary>
    /// 1. 액티브 페이즈: 레스트 상태인 모든 카드를 액티브 상태로 기상
    /// </summary>
    public void ExecuteActivePhase()
    {
        State.CurrentPhase = Phase.Active;
        State.TurnSequence++;
        var wokeTamers = WakeTamers(State.Player);   // v0.31.0 "When this Tamer becomes unsuspended" · v0.35.0 "can't unsuspend"인 테이머는 그대로
        _usedMainAbilities.Clear();
        int awakened = 0;
        var woke = new List<CardInstance>();
        foreach (var instance in AllInstances(State.Player))
        {
            if (instance.IsSuspended && !EffectRules.IsActive(instance.SkipUnsuspendUntil, State) && !EffectRules.IsUnsuspendLocked(State, instance) && !UnsuspendLock45(State.Player, instance, null, phase: true))   // "doesn't unsuspend during the next unsuspend phase" · v0.45.0 지속 효과
            {
                instance.IsSuspended = false;
                awakened++;
                woke.Add(instance);
            }
            ResetTurnScopedState(instance);
        }
        PruneExpiredBuffs();
        SetTurnOwner(playerTurn: true);
        Log($"액티브 페이즈 완료 (기상한 디지몬: {awakened}체)");
        FireUnsuspendPhase(State.Player, woke);
        NotifyTamersUnsuspended(State.Player, wokeTamers);
        TriggerStartOfTurn(State.Player);
    }

    /// <summary>
    /// 2. 드로우 페이즈: 메인 덱에서 1장 드로우 (선공 첫 턴은 스킵)
    /// </summary>
    public CardData? ExecuteDrawPhase()
    {
        State.CurrentPhase = Phase.Draw;
        if (State.CurrentTurn == 1 && State.PlayerGoesFirst)
        {
            Log("선공 1턴째 드로우 페이즈는 규칙에 따라 스킵합니다.");
            return null;
        }

        if (State.Player.MainDeck.Count == 0)
        {
            State.IsGameOver = true;
            State.IsVictory = false;
            State.GameOverReason = "드로우 페이즈에 덱이 0장이 되어 패배했습니다.";
            Log(State.GameOverReason);
            return null;
        }

        var drawn = DrawCard();
        if (drawn != null)
        {
            Log($"드로우 페이즈: [{drawn.Name}] 1장을 드로우했습니다.");
        }
        return drawn;
    }

    public CardData? DrawCard()
    {
        if (State.Player.MainDeck.Count == 0)
            return null;

        var card = State.Player.MainDeck[0];
        State.Player.MainDeck.RemoveAt(0);
        State.Player.Hand.Add(card);
        return card;
    }

    /// <summary>
    /// 3. 육성 페이즈 - 부화
    /// </summary>
    public ActionResult HatchDigitama()
    {
        var validation = ActionValidator.ValidateHatch(State);
        if (!validation.CanExecute)
            return validation;

        State.CanMulligan = false;
        var digitama = State.Player.DigitamaDeck.Peek();
        State.BreedingActionTaken = true;
        Log($"육성 에어리어 부화: [{digitama.Name}(Lv.{digitama.Level})]");
        Hatch(State.Player);

        State.CurrentPhase = Phase.Main;
        TriggerStartOfMainPhase(State.Player);
        Log("메인 페이즈로 전환되었습니다.");
        return ActionResult.Success($"{digitama.Name}(Lv.{digitama.Level})을 육성 에어리어에 부화하고 메인 페이즈로 이동했습니다.");
    }

    /// <summary>
    /// 3. 육성 페이즈 - 배틀 에어리어로 이동
    /// </summary>
    public ActionResult MoveFromBreedingArea()
    {
        var validation = ActionValidator.ValidateMoveFromBreeding(State);
        if (!validation.CanExecute)
            return validation;

        State.CanMulligan = false;
        var moving = State.Player.BreedingArea!;
        State.BreedingActionTaken = true;
        Log($"육성 에어리어 이동: [{moving.TopCard.Name}(Lv.{moving.TopCard.Level})] -> 배틀 에어리어");
        MoveBreedingToBattle(State.Player);

        State.CurrentPhase = Phase.Main;
        TriggerStartOfMainPhase(State.Player);
        Log("메인 페이즈로 전환되었습니다.");
        return ActionResult.Success($"{moving.TopCard.Name}(Lv.{moving.TopCard.Level})을 배틀 에어리어로 이동하고 메인 페이즈로 전환했습니다.");
    }

    /// <summary>
    /// 3. 육성 페이즈 - 패스 (메인 페이즈로 전환)
    /// </summary>
    public ActionResult PassBreedingPhase()
    {
        if (State.IsGameOver)
            return ActionResult.Fail("이미 종료된 게임입니다.");

        if (State.CurrentPhase != Phase.Breeding)
            return ActionResult.Fail("현재 육성 페이즈가 아닙니다.");

        State.CanMulligan = false;
        State.BreedingActionTaken = true;
        State.CurrentPhase = Phase.Main;
        TriggerStartOfMainPhase(State.Player);
        Log("육성 페이즈를 패스하고 메인 페이즈로 진입합니다.");
        return ActionResult.Success("육성 페이즈를 패스하고 메인 페이즈로 진입했습니다.");
    }

    /// <summary>
    /// 4. 메인 페이즈 - 카드 등장 / 옵션 사용
    /// </summary>
    public ActionResult PlayCard(CardData card, CardInstance? target = null)
    {
        var validation = ActionValidator.ValidatePlay(State, card);
        if (!validation.CanExecute)
            return validation;
        // 옵션을 대상 조건에 안 맞는 디지몬에 놓았다 (예: 전기 충격을 액티브 디지몬에) — 메모리를 쓰기 전에 막는다.
        // 그냥 쓰면 엔진이 조건에 맞는 다른 디지몬을 말없이 고르거나 효과 없이 끝난다
        if (card.Type == CardType.Option
            && CardScripts.InvalidMainTarget(card, target, CardScripts.MainTargetOf(card) == TargetKind.OpponentDigimon ? State.DummyOpponent.BattleArea : State.Player.BattleArea) is { } invalidTarget)
            return ActionResult.Fail(invalidTarget);

        State.CanMulligan = false;
        State.Player.Hand.Remove(card);
        var xros = card.Type == CardType.Digimon && !card.IsDual ? TakeXrosMaterials(State.Player, card) : new List<CardData>();   // 디지크로스 재료 (1장마다 코스트 -N)
        int cost = Math.Max(0, CardScripts.EffectivePlayCost(State.Player, card, State.DummyOpponent) - (PlayCostReductionBlocked(State.Player, card) ? 0 : XrosReduction(card, xros.Count)));   // 카드의 코스트 감소 효과 반영 (v0.36.0 마이너스할 수 없으면 디지크로스도)
        cost = ApplyPlayCostOptions(State.Player, card, cost);   // "by ~ing, reduce the play cost" — 비용을 낼지 묻는다
        State.MemoryGauge -= cost;
        State.TotalMemorySpent += cost;
        if (card.Type != CardType.Digimon || card.IsDual) DropPendingUnderPlayed(State.Player);

        string effectSummary = string.Empty;

        if (card.Type == CardType.Digimon && !card.IsDual)
        {
            var instance = new CardInstance(card, State.CurrentTurn);
            PlaceXrosMaterials(instance, xros);
            PlacePendingUnderPlayed(instance);   // v0.34.0 "by placing ~ under it, reduce the play cost"
            State.Player.BattleArea.Add(instance);
            RecalculateFieldBuffs();
            if (card.Level == 3)
            {
                State.Lv3StartedChains++;
            }
            Log($"디지몬 등장: [{card.Name}(Lv.{card.Level})] (코스트 {cost} 지불)");

            string onPlay = WithXros(instance, xros.Count, () => ApplyOnPlayEffects(State.Player, instance));
            if (onPlay.Length > 0)
                effectSummary = $" [{onPlay}]";
        }
        else if (card.Type == CardType.Tamer)
        {
            AddTamer(State.Player, card);
            RecalculateFieldBuffs();
            effectSummary = string.IsNullOrWhiteSpace(card.EffectText)
                ? string.Empty
                : $" [🎖️테이머 효과: {card.EffectText.Split('\n')[0]}]";
            Log($"테이머 등장: [{card.Name}] (코스트 {cost} 지불){effectSummary}");
            RunEffects(EffectTrigger.OnPlay, State.Player, null, card, includeInherited: false);
            FireForSide(EffectTrigger.AllyTamerPlayed, State.Player, null, triggerCard: card);
            FireForSide(EffectTrigger.OpponentTamerPlayed, State.DummyOpponent, null, triggerCard: card);
            RecalculateFieldBuffs();
        }
        else if (card.Type == CardType.Option || card.IsDual)   // 듀얼 카드 = 옵션 쪽으로 사용 (종합 룰 4-6-2)
        {
            effectSummary = " " + ApplyOptionEffect(State.Player, card, target);
            Log($"{(card.IsDual ? "듀얼 카드 옵션 쪽 사용" : "옵션 사용")}: [{card.Name}] (코스트 {cost} 지불 후 {(LastOptionArtsDigivolved ? "〈아츠 진화〉로 진화" : State.Player.OptionsInPlay.Any(p => ReferenceEquals(p.Card, card)) ? "배틀 에어리어에 놓음" : "트래시 이동")})");
        }

        CheckTurnEndAndAutoReset();
        return ActionResult.Success(validation.GuideMessage + effectSummary);
    }

    /// <summary>
    /// 4. 메인 페이즈 - 디지몬 진화 (진화 보너스 1드로우 및 진화 시 효과 포함)
    /// </summary>
    public ActionResult EvolveDigimon(CardInstance target, CardData evoCard)
    {
        var validation = ActionValidator.ValidateEvolution(State, target, evoCard);
        if (!validation.CanExecute)
            return validation;

        State.CanMulligan = false;
        string prevName = target.TopCard.Name;
        int prevLevel = target.TopCard.Level;

        int baseCost = ActionValidator.EvolutionCost(target, evoCard);
        NoteSpecialEvo42(target, evoCard, baseCost);   // v0.42.0 집계
        int evoCost = baseCost - TryDigisorption(State.Player, target, evoCard, baseCost, State.MemoryGauge);
        State.Player.Hand.Remove(evoCard);
        evoCost = ApplyEvoCostModifiers(State.Player, target, evoCard, evoCost);   // "When this Digimon would digivolve, reduce the digivolution cost by N"
        target.Evolve(evoCard);
        State.MemoryGauge -= evoCost;
        State.TotalMemorySpent += evoCost;
        RecalculateFieldBuffs();   // v0.37.0 메모리 조건 지속 효과
        State.TotalEvolutionsCount++;
        RecalculateFieldBuffs();

        if (prevLevel == 2 && evoCard.Level == 3)
        {
            State.Lv3StartedChains++;
        }
        else if (prevLevel == 5 && evoCard.Level >= 6)
        {
            State.Lv6CompletedChains++;
        }

        // [진화 시] 효과 — 육성 에어리어의 디지몬은 효과를 발휘하지 않는다
        if (State.Player.BattleArea.Contains(target))
            ApplyWhenDigivolvingEffects(State.Player, target, evoCard);
        else if (ReferenceEquals(State.Player.BreedingArea, target))
            RunBreedingEffects42(EffectTrigger.WhenDigivolving, State.Player, null, evoCard);   // v0.42.0 [육성]【진화 시】 (EX10-013)
        if (State.Player.BattleArea.Contains(target))
            FireForSide(EffectTrigger.AllyDigimonDigivolved, State.Player, target);

        var drawn = DrawCard();
        string drawText = drawn != null ? $"진화 보너스 드로우: [{drawn.Name}]" : "덱이 비어 진화 보너스 드로우 없음";
        Log($"디지몬 진화: [{prevName}(Lv.{prevLevel})] -> [{evoCard.Name}(Lv.{evoCard.Level})] (코스트 {evoCost}, {drawText})");

        CheckTurnEndAndAutoReset();
        return validation;
    }

    /// <summary>
    /// 4. 메인 페이즈 - 상대 레스트 상태 디지몬 공격 (필드 제압). 상대 ≪블로커≫가 대신 막을 수 있다.
    /// </summary>
    public ActionResult AttackOpponentDigimon(CardInstance attacker, CardInstance defender)
    {
        var validation = ActionValidator.ValidateAttack(State, attacker, defender);
        if (!validation.CanExecute)
            return validation;

        State.CanMulligan = false;
        string resultMsg = PlayerAttackOnDigimonCore(attacker, defender);
        EndTurnIfMemoryNegative();
        return ActionResult.Success(resultMsg);
    }

    /// <summary>내 디지몬이 상대 디지몬을 어택한다 (검사 없이 — ≪볼텍스≫ · ≪에그제큐트≫도 쓴다). 반환: 결과 메시지</summary>
    private string PlayerAttackOnDigimonCore(CardInstance attacker, CardInstance defender)
    {
        SuspendForAttack(attacker);   // "may attack without suspending"이면 레스트하지 않는다
        State.BlitzAttacker = null;
        _playerAttackRedirect = null;
        TriggerWhenAttackingEffects(attacker, defender);
        FireForSide(EffectTrigger.OpponentDigimonAttacks, State.DummyOpponent, attacker, triggerCard: defender.TopCard);   // TriggerCard = 어택 대상 (플레이어면 null)
        FireForSide(EffectTrigger.AllyDigimonAttacks, State.Player, attacker, triggerCard: defender.TopCard);
        if (_playerAttackRedirect is { } redirected && State.DummyOpponent.BattleArea.Contains(redirected)) defender = redirected;   // 상대 효과로 어택 대상 변경
        _playerAttackRedirect = null;
        defender = ApplyAttackKeywords(attacker, defender) ?? defender;

        // v0.34.0 "End the attack" — 배틀 없이 어택 종료
        string resultMsg = ConsumeAttackEnd(attacker) ? $"[{attacker.TopCard.Name}]의 어택이 효과로 종료되었습니다." : ResolvePlayerAttackOnDigimon(attacker, defender);
        Log(resultMsg);
        EndAttack(attacker);
        return resultMsg;
    }

    /// <summary>내 디지몬이 상대 디지몬을 어택한 뒤의 처리: 상대 ≪블로커≫의 블록 → 배틀 → ≪관통≫. 반환: 결과 메시지</summary>
    private string ResolvePlayerAttackOnDigimon(CardInstance attacker, CardInstance defender)
    {
        string resultMsg;
        if (!State.Player.BattleArea.Contains(attacker))
        {
            resultMsg = $"[{attacker.TopCard.Name}]이(가) 필드를 떠나 어택이 종료되었습니다.";
        }
        else if (!State.DummyOpponent.BattleArea.Contains(defender))
        {
            resultMsg = $"상대 [{defender.TopCard.Name}]이(가) [어택 시] 효과로 이미 필드를 떠나 배틀이 일어나지 않았습니다.";
        }
        else
        {
            _aiAttackTarget43 = defender;
            CounterTiming(State.DummyOpponent, attacker);   // 【카운터】 (≪블래스트 진화≫)
            _aiAttackTarget43 = null;
            if (!State.DummyOpponent.BattleArea.Contains(defender))   // v0.43.0 ≪블래스트 조그레스≫로 어택 대상이 새 디지몬이 되었다 (종합 룰 8-2-2-1-4)
                return $"상대 [{defender.TopCard.Name}]이(가) 【카운터】로 새 디지몬이 되어 어택 대상이 없어졌습니다 (배틀 없음).";
            var blocker = EffectRules.ChooseOpponentBlocker(State, attacker, defender);
            var battleTarget = blocker ?? defender;
            string prefix = string.Empty;
            if (blocker != null)
            {
                SuspendForBlock(blocker);
                OnAttackBlocked(attacker);
                prefix = $"🛡️ 상대 ≪블로커≫ [{blocker.TopCard.Name}]가 블록! ";
            }

            RecalculateFieldBuffs();
            var (atkDeleted, defDeleted, myDp, defDp) = ResolveDigimonBattle(attacker, battleTarget);
            resultMsg = prefix + DescribeBattle(attacker, myDp, battleTarget, defDp, atkDeleted, defDeleted);
            State.LastSecurityBattleWon = defDeleted && !atkDeleted;

            // ≪관통≫: 배틀에서 상대 디지몬만 소멸시켰을 때 시큐리티를 체크한다
            if (defDeleted && !atkDeleted && HasPiercing(attacker))
                resultMsg += " / ≪관통≫ " + RunSecurityChecks(attacker);
        }
        return resultMsg;
    }

    /// <summary>
    /// 4. 메인 페이즈 - 상대 시큐리티 공격 및 다이렉트 어택 판정 (상대 액티브 블로커 가로채기 포함)
    /// </summary>
    public ActionResult AttackSecurityOrDirect(CardInstance attacker)
    {
        var validation = ActionValidator.ValidateAttack(State, attacker, null);
        if (!validation.CanExecute)
            return validation;

        State.CanMulligan = false;
        var result = PlayerSecurityAttackCore(attacker);
        EndTurnIfMemoryNegative();
        return result;
    }

    /// <summary>내 디지몬이 시큐리티(플레이어)를 어택한다 (검사 없이 — ≪에그제큐트≫도 쓴다)</summary>
    private ActionResult PlayerSecurityAttackCore(CardInstance attacker)
    {
        SuspendForAttack(attacker);   // "may attack without suspending"이면 레스트하지 않는다
        State.BlitzAttacker = null;
        _playerAttackRedirect = null;
        TriggerWhenAttackingEffects(attacker, null);
        FireForSide(EffectTrigger.OpponentDigimonAttacks, State.DummyOpponent, attacker);
        FireForSide(EffectTrigger.AllyDigimonAttacks, State.Player, attacker);
        var redirected = _playerAttackRedirect is { } r && State.DummyOpponent.BattleArea.Contains(r) ? r : null;   // 상대 효과로 어택 대상 변경
        _playerAttackRedirect = null;

        ActionResult result;
        var raid = ApplyAttackKeywords(attacker, redirected) ?? redirected;
        if (ConsumeAttackEnd(attacker))   // v0.34.0 "End the attack" — 시큐리티 체크 없이 어택 종료
        {
            string endMsg = $"[{attacker.TopCard.Name}]의 어택이 효과로 종료되었습니다.";
            Log(endMsg);
            result = ActionResult.Success(endMsg);
        }
        else if (raid is { } raidTarget)
        {
            string msg = ResolvePlayerAttackOnDigimon(attacker, raidTarget);
            Log(msg);
            result = ActionResult.Success(msg);
        }
        else
        {
            result = ResolveSecurityAttack(attacker);
        }
        EndAttack(attacker);
        return result;
    }

    private ActionResult ResolveSecurityAttack(CardInstance attacker)
    {
        var opp = State.DummyOpponent;
        if (!State.Player.BattleArea.Contains(attacker))
            return ActionResult.Success($"[{attacker.TopCard.Name}]이(가) 필드를 떠나 어택이 종료되었습니다.");

        CounterTiming(opp, attacker);   // 【카운터】 (≪블래스트 진화≫)
        // 1) 상대가 ≪블로커≫로 막기로 하면 시큐리티 어택을 가로채 블록 (실전 AI는 막을지 스스로 판단)
        var blocker = EffectRules.ChooseOpponentBlocker(State, attacker, null);
        if (blocker != null)
        {
            SuspendForBlock(blocker);
            OnAttackBlocked(attacker);
            RecalculateFieldBuffs();
            var (atkDeleted, blkDeleted, myDp, blkDp) = ResolveDigimonBattle(attacker, blocker);
            string blkMsg = $"🛡️ 상대 ≪블로커≫ [{blocker.TopCard.Name}(DP {blkDp})]가 공격을 가로챘습니다! " +
                            DescribeBattle(attacker, myDp, blocker, blkDp, atkDeleted, blkDeleted);
            State.LastSecurityBattleWon = blkDeleted && !atkDeleted;

            if (blkDeleted && !atkDeleted && HasPiercing(attacker))
                blkMsg += " / ≪관통≫ " + RunSecurityChecks(attacker);

            State.LastSecurityBattleResult = blkMsg;
            Log(blkMsg);
            return ActionResult.Success(blkMsg);
        }

        // 공식 룰 11-5-1-2-1: 시큐리티 체크를 1장도 할 수 없는 디지몬의 어택으로는 이기지 못한다 (≪시큐리티 어택 -N≫ 등)
        if (attacker.GetSecurityAttackCount() < 1)
        {
            string noCheck = $"[{attacker.TopCard.Name}]은(는) 시큐리티 체크를 할 수 없어 어택이 아무 일 없이 끝났습니다.";
            Log(noCheck);
            return ActionResult.Success(noCheck);
        }

        // 2) 시큐리티가 0장인 상태에서 블로커도 없으면 -> 다이렉트 어택 승리!
        if (opp.SecurityStack.Count == 0)
        {
            State.IsGameOver = true;
            State.IsVictory = true;
            State.GameOverReason = $"🎉 [{attacker.TopCard.Name}]의 다이렉트 어택 성공! {State.CurrentTurn}턴 만에 승리했습니다!";

            // 목표 턴 평가는 연습(타임어택) 모드에서만 — 실전 AI 대전에는 턴 제한이 없다
            if (State.CurrentStage != PracticeStage.Stage3_AiBattle)
            {
                State.GameOverReason += State.CurrentTurn <= State.TargetTurns
                    ? $" (목표 턴({State.TargetTurns}턴 이내) 달성 성공!)"
                    : $" (목표 턴({State.TargetTurns}턴) 초과 클리어)";
            }
            Log(State.GameOverReason);
            return ActionResult.Success(State.GameOverReason);
        }

        // 3) 공식 룰에 따른 시큐리티 체크
        return ActionResult.Success(RunSecurityChecks(attacker));
    }

    /// <summary>
    /// 상대 시큐리티를 체크 매수만큼 위에서부터 오픈한다 (디지몬 배틀 / 테이머 공짜 등장 / 옵션 [시큐리티] 효과)
    /// </summary>
    private string RunSecurityChecks(CardInstance attacker)
    {
        var opp = State.DummyOpponent;
        RecalculateFieldBuffs();
        int checksToPerform = attacker.GetSecurityAttackCount();
        int attackerDp = attacker.GetCurrentDP();
        var battleSummaries = new List<string>();

        Log($"[{attacker.TopCard.Name}(DP {attackerDp})] 시큐리티 체크! (체크 예정 매수: {checksToPerform}장)");

        for (int i = 0; i < checksToPerform; i++)
        {
            FlushAfterSecurityBattle39();   // v0.39.0 앞 시큐리티 디지몬의 "배틀 종료 시"
            if (!State.Player.BattleArea.Contains(attacker))
            {
                battleSummaries.Add($"공격 디지몬 [{attacker.TopCard.Name}]이(가) 필드에서 벗어나 남은 시큐리티 체크가 중단되었습니다.");
                break;
            }

            if (opp.SecurityStack.Count == 0)
            {
                battleSummaries.Add("남은 시큐리티가 0장이 되어 추가 체크를 종료합니다.");
                break;
            }

            RecalculateFieldBuffs();
            attackerDp = attacker.GetCurrentDP();
            var securityCard = opp.SecurityStack[0];
            opp.SecurityStack.RemoveAt(0);
            OnSecurityCardChecked(opp, securityCard, attacker);   // 앞면 카드 체크 효과
            _securityCheckRemoval = true;
            try { NotifySecurityRemoved(opp, securityCard); }   // 앞면 체크 처리 뒤에 "시큐리티가 빠졌을 때"
            finally { _securityCheckRemoval = false; }
            State.LastRevealedSecurityCard = securityCard;

            // [공식 룰 A] [시큐리티] 효과가 있는 테이머 → 배틀 없이 코스트 없이 상대 필드에 등장 (효과가 없으면 아래에서 트래시)
            if (securityCard.Type == CardType.Tamer && EffectRules.HasSecurityEffect(securityCard) && CanPlayByEffect(opp, securityCard, PlayOrigin.Security))   // v0.36.0 효과로 등장
            {
                AddTamer(opp, securityCard);
                RecalculateFieldBuffs();
                string msg = $"✨ [시큐리티 테이머 발동!] 오픈된 [{securityCard.Name}]이(가) 코스트 지불 없이 상대 필드에 등장! 내 [{attacker.TopCard.Name}]은(는) 생존합니다. (남은 시큐리티: {opp.SecurityStack.Count}장)";
                State.LastSecurityBattleResult = $"시큐리티 테이머 [{securityCard.Name}] 공짜 등장! (내 디지몬 생존)";
                State.LastSecurityBattleWon = true;
                State.SecurityCheckHistory.Add(new SecurityCheckHistoryItem(
                    securityCard, attacker.TopCard.Name, attackerDp, IsBattleWon: true, OutcomeSummary: "🎖️ [시큐리티] 테이머 공짜 등장"));
                battleSummaries.Add(msg);
                Log(msg);
                continue;
            }

            // [공식 룰 B] 옵션 → 배틀 없이 [시큐리티] 효과 발휘 후 트래시
            if (securityCard.Type == CardType.Option)
            {
                opp.Trash.Add(securityCard);
                SecurityOptionOutcome outcome;
                if (CardScripts.HasKeyword(attacker, Keyword.NegateSecurityOptions)) // 워그레이몬(BT1): 체크한 옵션 카드의 [시큐리티] 효과는 발휘하지 않는다
                {
                    outcome = new SecurityOptionOutcome(
                        $"🔥 [{attacker.TopCard.Name}] 체크한 옵션 [{securityCard.Name}]의 [시큐리티] 효과를 무효로 했습니다!",
                        $"[시큐리티 {securityCard.Name}] 무효 ({attacker.TopCard.Name})", false, false);
                }
                else
                {
                    outcome = ResolveSecurityOption(securityCard, opp, attacker);
                    if (outcome.ReturnToHand && opp.Trash.Remove(securityCard))
                        opp.Hand.Add(securityCard);
                }

                State.LastSecurityBattleResult = outcome.Summary;
                State.LastSecurityBattleWon = !outcome.AttackerRemoved;
                State.SecurityCheckHistory.Add(new SecurityCheckHistoryItem(
                    securityCard, attacker.TopCard.Name, attackerDp, IsBattleWon: !outcome.AttackerRemoved, OutcomeSummary: outcome.Summary));
                battleSummaries.Add(outcome.Log);
                Log(outcome.Log);

                if (outcome.AttackerRemoved || !State.Player.BattleArea.Contains(attacker))
                    break;
                continue;
            }

            // [시큐리티] 효과가 없는 디지몬 외 카드 → 배틀 없이 트래시 (공식 룰 13-1-8-4)
            if (securityCard.Type != CardType.Digimon)
            {
                opp.Trash.Add(securityCard);
                string msg = $"[{securityCard.Name}]이(가) 오픈되었지만 [시큐리티] 효과가 없어 그대로 트래시됩니다. (남은 시큐리티: {opp.SecurityStack.Count}장)";
                State.LastSecurityBattleResult = $"[{securityCard.Name}] 효과 없음 → 트래시";
                State.LastSecurityBattleWon = true;
                State.SecurityCheckHistory.Add(new SecurityCheckHistoryItem(
                    securityCard, attacker.TopCard.Name, attackerDp, IsBattleWon: true, OutcomeSummary: "[시큐리티] 효과 없음 → 트래시"));
                battleSummaries.Add(msg);
                Log(msg);
                continue;
            }

            // [공식 룰 C] 디지몬 → 시큐리티 디지몬과 DP 배틀 후 트래시
            opp.Trash.Add(securityCard);
            var (secEffect, playAfterBattle) = ResolveSecurityDigimonEffect(securityCard, opp, attacker);
            if (secEffect.Length > 0)
            {
                battleSummaries.Add(secEffect);
                if (!State.Player.BattleArea.Contains(attacker))
                    break;
                RecalculateFieldBuffs();
                attackerDp = attacker.GetCurrentDP();
            }
            if (SecurityPlayWithoutBattle && PlaySecurityDigimonWithoutBattle(opp, securityCard))   // v0.37.0 "[Security] Play this card without battling ~"
            {
                string msg = $"✨ [시큐리티 디지몬 {securityCard.Name}] 배틀하지 않고 코스트 없이 상대 필드에 등장! 내 [{attacker.TopCard.Name}]은(는) 생존합니다. (남은 시큐리티: {opp.SecurityStack.Count}장)";
                State.LastSecurityBattleResult = $"시큐리티 디지몬 [{securityCard.Name}] 배틀 없이 등장 (내 디지몬 생존)";
                State.LastSecurityBattleWon = true;
                State.SecurityCheckHistory.Add(new SecurityCheckHistoryItem(
                    securityCard, attacker.TopCard.Name, attackerDp, IsBattleWon: true, OutcomeSummary: "✨ [시큐리티] 배틀 없이 등장"));
                battleSummaries.Add(msg);
                Log(msg);
                continue;
            }
            attackerDp += BattleDpBonus(attacker, opponentHasSources: false);
            int securityDp = securityCard.DP + SecurityDigimonDpBonus(opp, ownersTurn: false);
            if (playAfterBattle)
                PlaySecurityDigimonAfterBattle(opp, securityCard);

            if (attackerDp > securityDp)
            {
                string msg = $"✅ 배틀 승리! 공격자 [{attacker.TopCard.Name}(DP {attackerDp})] > 시큐리티 [{securityCard.Name}(DP {securityDp})] -> 내 디지몬 생존 & 시큐리티 파괴! (남은 시큐리티: {opp.SecurityStack.Count}장)";
                State.LastSecurityBattleResult = $"배틀 승리! ({attacker.TopCard.Name} DP {attackerDp} > {securityCard.Name} DP {securityDp})";
                State.LastSecurityBattleWon = true;
                State.SecurityCheckHistory.Add(new SecurityCheckHistoryItem(
                    securityCard, attacker.TopCard.Name, attackerDp, IsBattleWon: true, OutcomeSummary: $"✅ 배틀 승리 (DP {attackerDp} > {securityDp})"));
                battleSummaries.Add(msg);
                Log(msg);
            }
            else if (HasJamming(attacker))
            {
                string msg = $"🛡️ ≪재밍≫! [{attacker.TopCard.Name}(DP {attackerDp})]은(는) 시큐리티 디지몬 [{securityCard.Name}(DP {securityDp})]과의 배틀로는 소멸하지 않습니다. (남은 시큐리티: {opp.SecurityStack.Count}장)";
                State.LastSecurityBattleResult = $"≪재밍≫으로 생존 ({securityCard.Name} DP {securityDp})";
                State.LastSecurityBattleWon = true;
                State.SecurityCheckHistory.Add(new SecurityCheckHistoryItem(
                    securityCard, attacker.TopCard.Name, attackerDp, IsBattleWon: true, OutcomeSummary: "🛡️ ≪재밍≫ 생존"));
                battleSummaries.Add(msg);
                Log(msg);
            }
            else
            {
                string msg = $"💀 배틀 패배(내 디지몬 소멸)! 공격자 [{attacker.TopCard.Name}(DP {attackerDp})] <= 시큐리티 [{securityCard.Name}(DP {securityDp})] -> 내 [{attacker.TopCard.Name}] 소멸! (※ 디지몬 TCG 규칙상 오픈된 시큐리티 [{securityCard.Name}]도 함께 트래시로 이동합니다)";
                State.LastSecurityBattleResult = $"내 디지몬 소멸! ({attacker.TopCard.Name} DP {attackerDp} <= {securityCard.Name} DP {securityDp} — 시큐리티는 규칙상 함께 소모됨)";
                State.LastSecurityBattleWon = false;
                State.SecurityCheckHistory.Add(new SecurityCheckHistoryItem(
                    securityCard, attacker.TopCard.Name, attackerDp, IsBattleWon: false, OutcomeSummary: $"💀 내 디지몬 소멸 (DP {attackerDp} <= {securityDp})"));
                battleSummaries.Add(msg);
                Log(msg);
                if (DeleteDigimon(attacker))   // ≪방벽≫ 등으로 소멸을 막으면 남은 체크를 계속한다
                    break;
            }
        }
        FlushAfterSecurityBattle39();   // v0.39.0

        return string.Join(" / ", battleSummaries);
    }

    private static string DescribeBattle(CardInstance attacker, int atkDp, CardInstance defender, int defDp, bool atkDeleted, bool defDeleted)
    {
        string a = $"[{attacker.TopCard.Name}(DP {atkDp})]";
        string d = $"[{defender.TopCard.Name}(DP {defDp})]";
        if (defDeleted && !atkDeleted)
            return $"⚔️ 배틀 승리! {a} > {d} -> {d}을(를) 격파했습니다!";
        if (defDeleted && atkDeleted)
            return atkDp == defDp
                ? $"💥 동귀어진! {a} == {d} -> 양쪽 디지몬이 모두 소멸했습니다!"
                : $"💥 {a} vs {d} -> ≪길동무≫로 양쪽 디지몬이 모두 소멸했습니다!";
        return $"💀 배틀 패배! {a} < {d} -> {a}이(가) 소멸했습니다!";
    }

    /// <summary>어택 중 [어택 시] 효과 등으로 메모리가 0 미만이 되었다면, 어택이 끝난 뒤 턴이 넘어간다</summary>
    private void EndTurnIfMemoryNegative()
    {
        if (!State.IsGameOver && State.MemoryGauge < 0)
            CheckTurnEndAndAutoReset();
    }

    /// <summary>
    /// 수동으로 턴 종료 선언
    /// </summary>
    public ActionResult PassTurn()
    {
        if (State.IsGameOver)
            return ActionResult.Fail("이미 종료된 게임입니다.");
        if (State.PendingBlock != null)
            return ActionResult.Fail("상대의 어택을 블록할지 먼저 선택하세요.");
        if (State.CurrentPhase is Phase.Setup or Phase.TurnEnd)
            return ActionResult.Fail("지금은 내 턴이 아닙니다.");

        State.CanMulligan = false;
        if (State.BlitzAttacker != null && State.MemoryGauge < 0)
            State.BlitzAttacker = null;   // ≪진격≫ 어택을 하지 않고 끝냄 — 메모리는 이미 상대 쪽
        else
            State.MemoryGauge = -3;
        Log("플레이어가 턴 종료(Pass)를 선언했습니다.");
        CheckTurnEndAndAutoReset();
        return State.PendingBlock != null
            ? ActionResult.Success("턴을 종료했습니다. 상대가 어택해 왔습니다 — 블록할지 선택하세요!")
            : ActionResult.Success($"턴을 종료했습니다. 상대 턴 진행 후 {State.CurrentTurn}턴(메모리 +{State.MemoryGauge})이 시작됩니다.");
    }

    /// <summary>
    /// <summary>
    /// 5. 턴 종료 체크 및 스테이지별 상대방(골드피시 / 수비 웨이브 / AI 대전) 턴 진행
    /// </summary>
    public bool CheckTurnEndAndAutoReset()
    {
        if (State.IsGameOver || State.MemoryGauge >= 0)
            return false;

        // ≪진격≫: 메모리가 상대 쪽이어도 그 디지몬의 어택 1번이 끝날 때까지 턴이 이어진다
        if (State.BlitzAttacker is { } blitz)
        {
            if (State.Player.BattleArea.Contains(blitz) && CanAttackThisTurn(blitz))
            {
                Log($"[≪진격≫] [{blitz.TopCard.Name}]으로 어택할 수 있습니다. (어택하거나 턴 종료를 선택하면 턴이 끝납니다)");
                return false;
            }
            State.BlitzAttacker = null;
        }

        State.CurrentPhase = Phase.TurnEnd;
        Log($"메모리가 {State.MemoryGauge}(0 미만)이 되어 플레이어 턴이 종료됩니다.");
        TriggerEndOfTurn(State.Player);
        RunPlayerEndOfTurnAttacks();   // ≪볼텍스≫ · ≪에그제큐트≫ (턴 종료 시 어택)
        if (State.IsGameOver)
            return true;
        TriggerEndOfOpponentTurn(State.DummyOpponent);

        // [이 턴 종료 시] 메모리 -N 효과 (메탈그레이몬 BT1-021 등): 상대에게 그만큼 더 넘어간다
        if (State.PendingEndOfTurnMemoryLoss > 0)
        {
            State.MemoryGauge -= State.PendingEndOfTurnMemoryLoss;
            Log($"[이 턴 종료 시] 메모리 -{State.PendingEndOfTurnMemoryLoss} → {State.MemoryGauge}");
            State.PendingEndOfTurnMemoryLoss = 0;
        }
        int memoryPassedToOpponent = Math.Min(10, Math.Abs(State.MemoryGauge));
        Log($"상대에게 넘어간 메모리: +{memoryPassedToOpponent}");

        if (State.CurrentStage == PracticeStage.Stage3_AiBattle)
        {
            // AI 턴이 끝나면 FinishAiTurn이 내 다음 턴을 시작한다 (블록 선택을 기다리면 그 전에 멈춘다)
            BeginAiTurn(memoryPassedToOpponent, isOpeningTurn: false);
            return true;
        }

        if (State.CurrentStage == PracticeStage.Stage2_BlockerDefense)
        {
            ExecuteStage2OpponentWave();
        }

        // 연습 모드는 상대가 메모리를 쓰지 않으므로 매 턴 3으로 시작한다
        StartNextPlayerTurn(3);
        return true;
    }

    /// <summary>
    /// [공식 룰] 상대가 넘겨준 메모리(상대 쪽 마이너스 값의 절댓값)로 내 다음 턴을 시작한다.
    /// </summary>
    private void StartNextPlayerTurn(int handedOver)
    {
        State.CurrentTurn++;
        State.BreedingActionTaken = false;

        var (startMemory, tamerNote) = ApplyStartOfTurnTamerMemory(handedOver, State.Player.Tamers, State.DummyOpponent);
        State.MemoryGauge = startMemory;

        if (tamerNote != null)
        {
            Log($"🎖️ [내 테이머 효과] {tamerNote} → 시작 메모리 +{startMemory}");
            if (State.CurrentStage == PracticeStage.Stage3_AiBattle)
                AddOpponentEvent(OpponentActionKind.TurnEnd, "내 테이머 효과", $"넘겨받은 메모리 +{handedOver} · {tamerNote} → 시작 메모리 +{startMemory}");
        }

        Log(State.CurrentStage == PracticeStage.Stage3_AiBattle
            ? $"=== {State.CurrentTurn}턴 시작! (상대가 넘겨준 메모리 +{handedOver} → 내 시작 메모리: +{State.MemoryGauge}) ==="
            : $"=== {State.CurrentTurn}턴 시작! (내 시작 메모리: +{State.MemoryGauge}) ===");
        ExecuteActivePhase();
        ExecuteDrawPhase();
        if (!State.IsGameOver)
        {
            State.CurrentPhase = Phase.Breeding;
        }
    }

    private void ExecuteStage2OpponentWave()
    {
        foreach (var d in State.DummyOpponent.BattleArea)
        {
            d.IsSuspended = false;
            d.TemporaryDpBonus = 0;
            d.TemporarySecurityAttackBonus = 0;
        }

        if (State.DummyOpponent.BattleArea.Count < 2)
        {
            var catalog = StarterDecks.GetAllCatalogCards().ToDictionary(c => c.CardId);
            var blocker = catalog[State.CurrentTurn % 2 == 0 ? "ST2-07" : "ST1-06"]; // 그리즈몬 or 코어드라몬
            State.DummyOpponent.BattleArea.Add(new CardInstance(blocker, State.CurrentTurn));
            Log($"🛡️ [Stage 2 수비 증원] 상대 필드에 ≪블로커≫ [{blocker.Name}(DP {blocker.DP})]가 추가 배치되었습니다!");
        }
        else
        {
            Log("🛡️ [Stage 2 상대 턴] 상대 수비진의 모든 디지몬이 액티브 상태로 기상했습니다.");
        }
    }

    /// <summary>후공 플레이어의 1턴 시작 (AI 선공 턴은 0번째 턴 → 내 첫 턴이 1턴)</summary>
    private void StartFirstPlayerTurnAfterOpponent(int handedOver)
    {
        State.CurrentTurn = 1;
        State.MemoryGauge = handedOver;
        State.BreedingActionTaken = false;
        Log($"=== 1턴 시작! (후공 · 내 시작 메모리: +{State.MemoryGauge}) ===");
        ExecuteActivePhase();
        ExecuteDrawPhase();
        if (!State.IsGameOver)
        {
            State.CurrentPhase = Phase.Breeding;
        }
    }

    /// <summary>
    /// AI 턴 시작: 액티브 → 드로우 → 육성 → 어택 전 진화까지 진행하고 어택할 디지몬을 정한 뒤 RunAiTurn으로 넘긴다.
    /// </summary>
    private void BeginAiTurn(int aiStartingMemory, bool isOpeningTurn)
    {
        // [공식 룰] 플레이어가 넘겨준 메모리(예: -1이면 +1, -2이면 +2)로 AI 턴을 시작한다. 선공 첫 턴은 0.
        var (aiMemory, aiTamerNote) = ApplyStartOfTurnTamerMemory(aiStartingMemory, State.DummyOpponent.Tamers, State.Player);

        string memoryText = isOpeningTurn
            ? $"선공 첫 턴 · 메모리 {aiMemory}"
            : $"넘겨받은 메모리 +{aiStartingMemory}";
        if (aiTamerNote != null)
            memoryText += $" · 상대 테이머 효과: {aiTamerNote}";

        Log($"🤖 === [AI 상대 턴 시작 | {memoryText} → AI 시작 메모리: +{aiMemory}] ===");
        State.OpponentTurnEvents.Clear();
        AddOpponentEvent(OpponentActionKind.TurnStart, "상대 턴",
            isOpeningTurn ? $"{memoryText} (드로우 · 공격 없음)" : $"{memoryText} → 시작 메모리 +{aiMemory}");

        // AI 턴 진행 정보 — 카드 효과가 AI 메모리를 바꿀 수 있도록 AI 턴이 끝날 때까지 _aiTurn에 둔다
        var ctx = new AiTurnContext { Memory = aiMemory, IsOpeningTurn = isOpeningTurn };
        _aiTurn = ctx;

        // 1. AI 액티브 페이즈 (턴 순번 증가 → "다음 상대의 턴 종료 시까지" 같은 기간 효과의 기준)
        State.TurnSequence++;
        State.OpponentAttackedThisTurn = false;
        var aiWokeTamers = WakeTamers(State.DummyOpponent);   // v0.35.0 "can't unsuspend"인 테이머는 그대로
        _usedMainAbilities.Clear();
        var aiWoke = new List<CardInstance>();
        foreach (var d in AllInstances(State.DummyOpponent))
        {
            if (d.IsSuspended && !EffectRules.IsActive(d.SkipUnsuspendUntil, State) && !EffectRules.IsUnsuspendLocked(State, d) && !UnsuspendLock45(State.DummyOpponent, d, null, phase: true)) { d.IsSuspended = false; aiWoke.Add(d); }   // v0.45.0 지속 효과
            ResetTurnScopedState(d);
        }
        // 내 디지몬의 "이 턴 동안" 효과는 내 턴이 끝나면 사라진다
        foreach (var d in AllInstances(State.Player))
        {
            d.TemporaryDpBonus = 0;
            d.TemporarySecurityAttackBonus = 0;
            d.ActiveSkillBuffLabels.Clear();
        }
        // ≪재기동≫: 상대의 액티브 페이즈에 내 디지몬도 액티브가 된다
        foreach (var d in State.Player.BattleArea.Where(d => d.IsSuspended && HasReboot(d) && !EffectRules.IsActive(d.SkipUnsuspendUntil, State) && !EffectRules.IsUnsuspendLocked(State, d) && !UnsuspendLock45(State.Player, d, null, phase: true)))
        {
            d.IsSuspended = false;
            Log($"[≪재기동≫] 내 [{d.TopCard.Name}]이(가) 상대의 액티브 페이즈에 액티브 상태가 되었습니다.");
        }
        PruneExpiredBuffs();
        SetTurnOwner(playerTurn: false);
        AddEffectEvent(State.DummyOpponent.Tamers.FirstOrDefault() ?? State.DummyOpponent.BattleArea.FirstOrDefault()?.TopCard ?? new CardData { Name = "상대" },
            CaptureEffectLogs(() => { FireUnsuspendPhase(State.DummyOpponent, aiWoke); NotifyTamersUnsuspended(State.DummyOpponent, aiWokeTamers); TriggerStartOfTurn(State.DummyOpponent); }));

        // 2. AI 드로우 페이즈 (AI가 선공인 게임 첫 턴은 드로우 없음)
        //    공식 룰: 드로우 페이즈에 덱이 0장이라 드로우할 수 없으면 그 플레이어가 패배한다 (AI도 동일)
        if (!isOpeningTurn && State.DummyOpponent.MainDeck.Count == 0)
        {
            State.IsGameOver = true;
            State.IsVictory = true;
            State.GameOverReason = "🎉 상대 AI가 드로우 페이즈에 덱이 0장이 되어 승리했습니다!";
            Log(State.GameOverReason);
            AddOpponentEvent(OpponentActionKind.TurnEnd, "덱 아웃", "상대 덱 0장 — 드로우할 수 없어 패배");
            _aiTurn = null;
            return;
        }
        if (!isOpeningTurn)
        {
            var drawn = State.DummyOpponent.MainDeck[0];
            State.DummyOpponent.MainDeck.RemoveAt(0);
            State.DummyOpponent.Hand.Add(drawn);
        }

        // 3. AI 육성 페이즈 (부화 / 배틀 이동 중 하나): 비어 있으면 디지타마 부화, Lv.3 이상이 자라 있으면 배틀 에어리어로 이동
        var aiBreeding = State.DummyOpponent.BreedingArea;
        if (aiBreeding == null)
        {
            if (State.DummyOpponent.DigitamaDeck.Count > 0)
            {
                var egg = State.DummyOpponent.DigitamaDeck.Peek();
                Log($"🤖 [AI 육성] 디지타마 부화: [{egg.Name}(Lv.{egg.Level})]");
                AddOpponentEvent(OpponentActionKind.Breeding, "디지타마 부화", $"{egg.Name} (Lv.{egg.Level}) → 상대 육성 에어리어", egg);
                AddEffectEvent(egg, CaptureEffectLogs(() => Hatch(State.DummyOpponent)));
            }
        }
        else if (aiBreeding.TopCard.Type == CardType.Digimon && aiBreeding.TopCard.Level >= 3 && !EffectRules.MoveDpBlocked(State, State.DummyOpponent, aiBreeding))   // v0.37.0 EX7-014
        {
            // 육성 에어리어에서 이동한 디지몬은 등장한 것이 아니므로 이번 턴에 바로 공격 가능 (플레이어와 동일 — MoveBreedingToBattle)
            Log($"🤖 [AI 육성] 육성 에어리어 이동: [{aiBreeding.TopCard.Name}(Lv.{aiBreeding.TopCard.Level})] -> 배틀 에어리어");
            AddOpponentEvent(OpponentActionKind.Breeding, "배틀 에어리어로 이동", $"육성 에어리어의 {aiBreeding.TopCard.Name} (Lv.{aiBreeding.TopCard.Level}) 출격", aiBreeding.TopCard);
            AddEffectEvent(aiBreeding.TopCard, CaptureEffectLogs(() => MoveBreedingToBattle(State.DummyOpponent)));
        }

        AddEffectEvent(State.DummyOpponent.Tamers.FirstOrDefault() ?? State.DummyOpponent.BattleArea.FirstOrDefault()?.TopCard ?? new CardData { Name = "상대" },
            CaptureEffectLogs(() => TriggerStartOfMainPhase(State.DummyOpponent)));

        // 3-A. [어택 전 메인 페이즈] 정책이 고른 옵션 · 진화 (메모리를 상대 쪽으로 넘기지 않는 것 — 진화 직후 바로 어택 가능!)
        while (ctx.Memory >= 0 && !State.IsGameOver)
        {
            if (AiPolicy.ChooseOption(this, ctx.Memory, beforeAttacks: true) is { } option) { AiUseOption(ctx, option); continue; }
            if (AiPolicy.ChooseEvolution(this, ctx.Memory, beforeAttacks: true) is { } evo) { AiEvolve(ctx, evo.Base, evo.Card); continue; }
            if (AiTryTamerEvolution(ctx)) continue;   // v0.30.0 테이머에서 진화 (하이브리드체)
            if (AiPolicy.ChooseDnaEvolution(this, ctx.Memory, beforeAttacks: true) is { } dna && AiDnaEvolve(dna)) continue;
            if (AiTryLink41(ctx)) continue;   // v0.41.0 링크 (메모리를 넘기지 않는 것만)
            if (AiTryAppFusion43(ctx)) continue;   // v0.43.0 어플 합체 (GoldfishEngine.Batch43)
            break;
        }

        // 3-B. [AI 어택 선언] 공식 룰상 메모리가 0 이상일 때만 공격할 수 있습니다!
        //      어택은 RunAiTurn이 하나씩 정책에 묻는다 (어택 결과로 필드가 바뀌므로). 선공 첫 턴은 공격하지 않는다
        RunAiTurn();
    }

    /// <summary>
    /// 남은 AI 어택을 차례로 처리한다. 플레이어가 블록 여부를 골라야 하면 State.PendingBlock을 세우고 멈춘다
    /// (ResolvePendingBlock이 이어서 부른다). 어택이 모두 끝나면 FinishAiTurn으로 넘어간다.
    /// </summary>
    private void RunAiTurn()
    {
        var ctx = _aiTurn;
        if (ctx == null)
            return;

        while (true)
        {
            // 다음 어택은 정책에 묻는다 — 받은 대상(내 레스트 디지몬)이 어택 대상의 처음 값 (≪돌진≫ 등 키워드가 바꿀 수 있다)
            if (ctx.Attackers.Count == 0)
            {
                if (ctx.EndPhase)
                    break;   // 턴 종료 시 어택(≪볼텍스≫ · ≪에그제큐트≫)이 끝났다
                if (ctx.IsOpeningTurn || State.IsGameOver || ctx.AttacksDeclared >= MaxAiAttacksPerTurn
                    || AiPolicy.ChooseNextAttack(this, ctx.Memory) is not { } next)
                    break;
                if (ctx.Memory < 0)
                {
                    // 어택 도중 효과로 AI 메모리가 0 미만이 되면 남은 어택 없이 AI 턴의 메인 페이즈도 끝난다
                    Log($"⚡ AI 메모리가 {ctx.Memory}(0 미만)이 되어 AI의 추가 공격 및 메인 페이즈가 즉시 종료됩니다!");
                    break;
                }
                ctx.AttacksDeclared++;
                ctx.Attackers.Enqueue(next.Attacker);
                // 정책이 고른 대상이 어택할 수 없는 디지몬(액티브 · 어택당하지 않음)이면 시큐리티로
                ctx.AttackTarget = next.Target != null && State.Player.BattleArea.Contains(next.Target) && EffectRules.CanBeAttackTargetFor(next.Attacker, next.Target) ? next.Target : null;
            }

            // 공격 도중 내 시큐리티 해머 스파크 등으로 AI 메모리가 0 미만이 되면 즉시 AI 턴 종료! (≪진격≫ 어택은 예외)
            if (ctx.Memory < 0 && !ctx.EndPhase && !ReferenceEquals(ctx.Attackers.Peek(), ctx.BlitzAttacker) && !ctx.QueuedTargets.ContainsKey(ctx.Attackers.Peek()))
            {
                Log($"⚡ AI 메모리가 {ctx.Memory}(0 미만)이 되어 AI의 추가 공격 및 메인 페이즈가 즉시 종료됩니다!");
                ctx.Attackers.Clear();
                break;
            }

            var aiAttacker = ctx.Attackers.Dequeue();
            if (ReferenceEquals(aiAttacker, ctx.BlitzAttacker))
                ctx.BlitzAttacker = null;
            // 효과 어택 ("may attack"): 큐에 넣을 때 고른 대상 (그사이 사라졌거나 어택할 수 없게 됐으면 시큐리티)
            if (ctx.QueuedTargets.Remove(aiAttacker, out var queuedTarget))
                ctx.AttackTarget = queuedTarget != null && State.Player.BattleArea.Contains(queuedTarget) && EffectRules.CanBeAttackTargetFor(aiAttacker, queuedTarget) ? queuedTarget : null;
            bool noSuspend = ctx.NoSuspendAttackers.Remove(aiAttacker);
            if (!State.DummyOpponent.BattleArea.Contains(aiAttacker) || (aiAttacker.IsSuspended && !noSuspend) || EffectRules.IsAttackLocked(State, aiAttacker)
                || (ctx.AttackTarget == null && !EffectRules.CanAttackPlayer(aiAttacker)))   // v0.34.0 "can't attack players"
            {
                ctx.AttackTarget = null;
                continue;
            }

            if (!noSuspend) SuspendAsAttacker(aiAttacker);
            State.OpponentAttackedThisTurn = true;

            // AI 디지몬의 [어택 시] 효과 (플레이어와 같은 카드 스크립트) → ≪연계≫ · ≪돌진≫
            var attackEffects = CaptureEffectLogs(() =>
            {
                TriggerWhenAttackingEffects(aiAttacker, ctx.AttackTarget);
                FireForSide(EffectTrigger.OpponentDigimonAttacks, State.Player, aiAttacker, triggerCard: ctx.AttackTarget?.TopCard);
                FireForSide(EffectTrigger.AllyDigimonAttacks, State.DummyOpponent, aiAttacker, triggerCard: ctx.AttackTarget?.TopCard);
                ctx.AttackTarget = ApplyAttackKeywords(aiAttacker, ctx.AttackTarget) ?? ctx.AttackTarget;   // 상대(나) 효과로 바뀐 대상 유지
            });

            RecalculateFieldBuffs();
            int aiDp = aiAttacker.GetCurrentDP();
            int aiChecks = aiAttacker.GetSecurityAttackCount();
            AddOpponentEvent(OpponentActionKind.Attack, "공격 선언",
                ctx.AttackTarget is { } raided
                    ? $"{aiAttacker.TopCard.Name} (DP {aiDp:N0}) → 내 {raided.TopCard.Name} (DP {raided.GetCurrentDP():N0})"
                    : $"{aiAttacker.TopCard.Name} (DP {aiDp:N0}) → 내 시큐리티 {aiChecks}장", aiAttacker.TopCard, ctx.AttackTarget?.TopCard);
            AddEffectEvent(aiAttacker.TopCard, attackEffects);
            if (State.IsGameOver)
            {
                _aiTurn = null;
                return;
            }
            // v0.34.0 "End the attack" — [어택 시] · "어택했을 때" 효과가 어택을 끝냈다 (배틀 · 시큐리티 체크 없이)
            if (!State.DummyOpponent.BattleArea.Contains(aiAttacker) || ConsumeAttackEnd(aiAttacker))
            {
                EndAiAttack(ctx, aiAttacker);
                continue;
            }

            _aiAttackTarget43 = ctx.AttackTarget;
            var counterLogs = CaptureEffectLogs(() => CounterTiming(State.Player, aiAttacker));   // 【카운터】 (내 ≪블래스트 진화≫)
            _aiAttackTarget43 = null;
            if (counterLogs.Count > 0)
                AddReplayEvent("내 【카운터】", string.Join("\n", counterLogs));
            // [공식 룰] ≪블로커≫로 막을지는 방어하는 플레이어가 고른다 (어택 대상인 디지몬은 블록할 수 없다)
            var candidates = EffectRules.BlockCandidates(State, State.Player.BattleArea.Where(d => !ReferenceEquals(d, ctx.AttackTarget)), aiAttacker);
            if (candidates.Count > 0 && PlayerChoosesBlocks)
            {
                State.PendingBlock = new PendingBlockDecision(aiAttacker, candidates, aiDp, aiChecks);
                Log($"🛡️ AI [{aiAttacker.TopCard.Name}(DP {aiDp})]의 어택! 내 ≪블로커≫ {candidates.Count}체로 블록할 수 있습니다 — 블록 여부를 선택하세요.");
                return;
            }

            ResolveAiAttack(aiAttacker, candidates.FirstOrDefault());
            EndAiAttack(ctx, aiAttacker);
            if (State.IsGameOver)
            {
                _aiTurn = null;
                return;
            }
        }

        if (ctx.EndPhase) EndAiTurn(ctx);
        else FinishAiTurn(ctx);
    }

    private void EndAiAttack(AiTurnContext ctx, CardInstance aiAttacker)
    {
        ctx.AttackTarget = null;
        AddEffectEvent(aiAttacker.TopCard, CaptureEffectLogs(() => EndAttack(aiAttacker)));
    }

    /// <summary>
    /// AI의 어택을 막을지 플레이어가 고른 결과 (blocker = null이면 블록하지 않음). 남은 AI 턴을 이어서 진행한다.
    /// </summary>
    public ActionResult ResolvePendingBlock(CardInstance? blocker)
    {
        var pending = State.PendingBlock;
        if (pending == null || _aiTurn == null)
            return ActionResult.Fail("지금은 블록을 선택할 차례가 아닙니다.");
        if (blocker != null && (!pending.Candidates.Contains(blocker) || !EffectRules.CanBlockAgainst(State, blocker, pending.Attacker)))
            return ActionResult.Fail($"[{blocker.TopCard.Name}]은(는) 이 어택을 블록할 수 없습니다.");
        if (blocker == null && EffectRules.ForcesBlock(pending.Attacker) && pending.Candidates.Count > 0)
            return ActionResult.Fail($"≪충돌≫: [{pending.Attacker.TopCard.Name}]의 어택은 블록할 수 있으면 반드시 블록해야 합니다.");

        State.PendingBlock = null;
        string attackerName = pending.Attacker.TopCard.Name;
        Log(blocker != null
            ? $"🛡️ 블록 선언: 내 [{blocker.TopCard.Name}]으로 AI [{attackerName}]의 어택을 막습니다."
            : $"블록하지 않음: AI [{attackerName}]의 어택을 그대로 받습니다.");

        ResolveAiAttack(pending.Attacker, blocker);
        EndAiAttack(_aiTurn, pending.Attacker);
        if (State.IsGameOver)
        {
            _aiTurn = null;
            return ActionResult.Success(State.GameOverReason);
        }

        RunAiTurn();
        if (State.PendingBlock != null)
            return ActionResult.Success($"AI [{State.PendingBlock.Attacker.TopCard.Name}]이(가) 이어서 어택합니다 — 블록할지 선택하세요!");
        if (State.IsGameOver)
            return ActionResult.Success(State.GameOverReason);
        return ActionResult.Success($"상대 턴이 끝났습니다. {State.CurrentTurn}턴 시작 (메모리 +{State.MemoryGauge})");
    }

    /// <summary>
    /// 선언된 AI 어택 1회를 처리한다: 블록 배틀, 또는 시큐리티 체크 / 다이렉트 어택.
    /// </summary>
    private void ResolveAiAttack(CardInstance aiAttacker, CardInstance? playerBlocker)
    {
        var ctx = _aiTurn!;
        if (!State.DummyOpponent.BattleArea.Contains(aiAttacker))
            return;

        RecalculateFieldBuffs();
        int aiDp = aiAttacker.GetCurrentDP();
        int aiChecks = aiAttacker.GetSecurityAttackCount();

        // 내 디지몬을 어택 중 (정책이 고른 레스트 디지몬 · ≪돌진≫ — 블록하지 않았을 때)
        if (playerBlocker == null && ctx.AttackTarget is { } raidTarget)
        {
            string ai = aiAttacker.TopCard.Name, mine = raidTarget.TopCard.Name;
            if (!State.Player.BattleArea.Contains(raidTarget))
            {
                Log($"🤖 AI [{ai}]의 어택 대상 [{mine}]이(가) 필드를 떠나 어택이 끝났습니다.");
                return;
            }
            var (aiLost, mineLost, aiBattleDp, myDp) = ResolveDigimonBattle(aiAttacker, raidTarget);
            string result = mineLost && !aiLost ? $"내 [{mine}] 소멸" : aiLost && !mineLost ? $"AI [{ai}] 소멸" : aiLost ? "양쪽 소멸" : "양쪽 생존";
            Log($"⚔️ [디지몬 배틀] AI [{ai}(DP {aiBattleDp})] vs 내 [{mine}(DP {myDp})] → {result}");
            AddOpponentEvent(OpponentActionKind.Block, "디지몬 배틀", $"{ai} (DP {aiBattleDp:N0}) vs 내 {mine} (DP {myDp:N0}) → {result}", aiAttacker.TopCard, raidTarget.TopCard);
            if (mineLost && !aiLost && HasPiercing(aiAttacker) && aiAttacker.GetSecurityAttackCount() > 0)
            {
                Log($"🤖 [≪관통≫] AI [{ai}]이(가) 내 디지몬을 쓰러뜨리고 내 시큐리티를 체크합니다!");
                RunAiSecurityChecks(aiAttacker, aiAttacker.GetSecurityAttackCount());
            }
            return;
        }

        if (playerBlocker != null)
        {
            SuspendForBlock(playerBlocker);
            var blockedEffects = CaptureEffectLogs(() => OnAttackBlocked(aiAttacker));
            AddEffectEvent(aiAttacker.TopCard, blockedEffects);
            RecalculateFieldBuffs();
            var (aiDeleted, blkDeleted, aiBattleDp, myBlkDp) = ResolveDigimonBattle(aiAttacker, playerBlocker);
            aiDp = aiBattleDp;
            string ai = aiAttacker.TopCard.Name, blk = playerBlocker.TopCard.Name;
            if (aiDeleted && !blkDeleted)
            {
                Log($"🛡️ [내 블로커 방어 성공!] 내 [{blk}(DP {myBlkDp})]이(가) AI의 [{ai}(DP {aiDp})]을(를) 블록하여 격파했습니다!");
                AddOpponentEvent(OpponentActionKind.Block, "내 블로커 방어 성공!", $"내 {blk} (DP {myBlkDp:N0})가 막고 {ai} 격파", aiAttacker.TopCard, playerBlocker.TopCard);
            }
            else if (blkDeleted && !aiDeleted)
            {
                Log($"🛡️ [내 블로커 희생 방어] 내 [{blk}]이(가) AI의 [{ai}] 공격을 대신 막고 소멸했습니다!");
                AddOpponentEvent(OpponentActionKind.Block, "내 블로커가 대신 막음", $"내 {blk} 소멸 (시큐리티는 지켜짐)", aiAttacker.TopCard, playerBlocker.TopCard);
            }
            else
            {
                Log($"🛡️ [블록] 내 [{blk}]과(와) AI의 [{ai}]이(가) 함께 소멸했습니다!");
                AddOpponentEvent(OpponentActionKind.Block, "블록 — 양쪽 소멸", $"내 {blk}와 {ai} 모두 소멸 (시큐리티는 지켜짐)", aiAttacker.TopCard, playerBlocker.TopCard);
            }

            // ≪관통≫: 배틀에서 상대 디지몬만 소멸시켰을 때 시큐리티를 체크한다
            if (blkDeleted && !aiDeleted && HasPiercing(aiAttacker) && aiAttacker.GetSecurityAttackCount() > 0)
            {
                Log($"🤖 [≪관통≫] AI [{ai}]이(가) 블로커를 쓰러뜨리고 내 시큐리티를 체크합니다!");
                RunAiSecurityChecks(aiAttacker, aiAttacker.GetSecurityAttackCount());
            }
            return;
        }

        // 공식 룰 11-5-1-2-1: 시큐리티 체크를 할 수 없는 디지몬의 어택은 아무 일 없이 끝난다
        if (aiChecks < 1)
        {
            Log($"🤖 AI [{aiAttacker.TopCard.Name}]은(는) 시큐리티 체크를 할 수 없어 어택이 아무 일 없이 끝났습니다.");
            return;
        }

        // 플레이어 시큐리티가 0장이면 AI의 다이렉트 어택 -> 플레이어 패배!
        if (State.Player.SecurityStack.Count == 0)
        {
            State.IsGameOver = true;
            State.IsVictory = false;
            State.GameOverReason = $"💀 AI의 [{aiAttacker.TopCard.Name}]에게 다이렉트 어택을 허용하여 패배했습니다! (블로커를 세우거나 먼저 시큐리티를 돌파하세요)";
            Log(State.GameOverReason);
            AddOpponentEvent(OpponentActionKind.DirectAttack, "다이렉트 어택!", $"{aiAttacker.TopCard.Name}의 공격 — 내 시큐리티 0장", aiAttacker.TopCard);
            return;
        }

        RunAiSecurityChecks(aiAttacker, aiChecks);
    }

    /// <summary>AI 디지몬의 시큐리티 어택 매수만큼 내 시큐리티를 위에서부터 오픈한다 (디지몬 배틀 / 테이머 등장 / 옵션 [시큐리티] 효과)</summary>
    private void RunAiSecurityChecks(CardInstance aiAttacker, int aiChecks)
    {
        for (int c = 0; c < aiChecks; c++)
        {
            FlushAfterSecurityBattle39();   // v0.39.0 앞 시큐리티 디지몬의 "배틀 종료 시"
            if (!State.DummyOpponent.BattleArea.Contains(aiAttacker) || State.Player.SecurityStack.Count == 0)
                break;

            RecalculateFieldBuffs();
            int aiDp = aiAttacker.GetCurrentDP();
            var mySec = State.Player.SecurityStack[0];
            State.Player.SecurityStack.RemoveAt(0);
            OnSecurityCardChecked(State.Player, mySec, aiAttacker);   // 앞면 카드 체크 효과
            _securityCheckRemoval = true;
            try { NotifySecurityRemoved(State.Player, mySec); }
            finally { _securityCheckRemoval = false; }

            // 1) 내 시큐리티에서 [시큐리티] 효과가 있는 [테이머]가 나온 경우 -> 코스트 지불 없이 내 테이머 존에 무료 등장!
            if (mySec.Type == CardType.Tamer && EffectRules.HasSecurityEffect(mySec) && CanPlayByEffect(State.Player, mySec, PlayOrigin.Security))   // v0.36.0 효과로 등장
            {
                AddTamer(State.Player, mySec);
                RecalculateFieldBuffs();
                string tamerMsg = $"✨ [내 시큐리티 테이머 발동!] AI [{aiAttacker.TopCard.Name}]의 공격으로 오픈된 내 시큐리티 [{mySec.Name}]이(가) 코스트 없이 내 필드에 무료 등장했습니다! (내 남은 시큐리티: {State.Player.SecurityStack.Count}장)";
                State.PlayerSecurityCheckHistory.Add(new SecurityCheckHistoryItem(
                    mySec, aiAttacker.TopCard.Name, aiDp, IsBattleWon: true, OutcomeSummary: "🎖️ 내 테이머 무료 등장!"));
                Log(tamerMsg);
                AddOpponentEvent(OpponentActionKind.SecurityResult, "내 시큐리티 오픈: 테이머", $"{mySec.Name} 무료 등장 · 남은 시큐리티 {State.Player.SecurityStack.Count}장", aiAttacker.TopCard, mySec);
                continue;
            }

            // 2) 내 시큐리티에서 [옵션]이 나온 경우 -> 공식 룰: [시큐리티] 효과를 즉시 무료 발동 후 트래시행!
            if (mySec.Type == CardType.Option)
            {
                State.Player.Trash.Add(mySec);
                SecurityOptionOutcome outcome;
                if (CardScripts.HasKeyword(aiAttacker, Keyword.NegateSecurityOptions))
                {
                    outcome = new SecurityOptionOutcome(
                        $"🔥 AI [{aiAttacker.TopCard.Name}]이(가) 체크한 내 옵션 [{mySec.Name}]의 [시큐리티] 효과를 무효로 했습니다!",
                        $"[시큐리티 {mySec.Name}] 무효 ({aiAttacker.TopCard.Name})", false, false);
                }
                else
                {
                    outcome = ResolveSecurityOption(mySec, State.Player, aiAttacker);
                    if (outcome.ReturnToHand && State.Player.Trash.Remove(mySec))
                        State.Player.Hand.Add(mySec);
                }
                bool aiRemoved = outcome.AttackerRemoved || !State.DummyOpponent.BattleArea.Contains(aiAttacker);
                State.PlayerSecurityCheckHistory.Add(new SecurityCheckHistoryItem(
                    mySec, aiAttacker.TopCard.Name, aiDp, IsBattleWon: aiRemoved, OutcomeSummary: outcome.Summary));
                Log(outcome.Log);
                AddOpponentEvent(OpponentActionKind.SecurityResult, "내 시큐리티 오픈: 옵션 발동", $"{mySec.Name} — {outcome.Summary} · 남은 시큐리티 {State.Player.SecurityStack.Count}장", aiAttacker.TopCard, mySec);
                if (aiRemoved)
                    break;
                continue;
            }

            // [시큐리티] 효과가 없는 디지몬 외 카드 → 배틀 없이 트래시 (공식 룰 13-1-8-4)
            if (mySec.Type != CardType.Digimon)
            {
                State.Player.Trash.Add(mySec);
                State.PlayerSecurityCheckHistory.Add(new SecurityCheckHistoryItem(
                    mySec, aiAttacker.TopCard.Name, aiDp, IsBattleWon: false, OutcomeSummary: "[시큐리티] 효과 없음 → 트래시"));
                Log($"내 시큐리티 [{mySec.Name}]이(가) 오픈되었지만 [시큐리티] 효과가 없어 그대로 트래시됩니다. (내 남은 시큐리티: {State.Player.SecurityStack.Count}장)");
                AddOpponentEvent(OpponentActionKind.SecurityResult, "내 시큐리티 오픈", $"{mySec.Name} — [시큐리티] 효과 없음 · 남은 시큐리티 {State.Player.SecurityStack.Count}장", aiAttacker.TopCard, mySec);
                continue;
            }

            // 3) 내 시큐리티에서 [디지몬]이 나온 경우 -> 공식 룰: 시큐리티 DP 배틀 후 트래시행!
            State.Player.Trash.Add(mySec);
            (string Message, bool PlayAfterBattle) mySecResult = default;
            var mySecEffect = CaptureEffectLogs(() => mySecResult = ResolveSecurityDigimonEffect(mySec, State.Player, aiAttacker));
            AddEffectEvent(mySec, mySecEffect);
            if (!State.DummyOpponent.BattleArea.Contains(aiAttacker))
                break;
            RecalculateFieldBuffs();
            aiDp = aiAttacker.GetCurrentDP();
            if (SecurityPlayWithoutBattle && PlaySecurityDigimonWithoutBattle(State.Player, mySec))   // v0.37.0 "[Security] Play this card without battling ~"
            {
                State.PlayerSecurityCheckHistory.Add(new SecurityCheckHistoryItem(
                    mySec, aiAttacker.TopCard.Name, aiDp, IsBattleWon: false, OutcomeSummary: "✨ [시큐리티] 배틀 없이 등장"));
                Log($"✨ 내 시큐리티 디지몬 [{mySec.Name}]이(가) 배틀하지 않고 코스트 없이 등장했습니다. (내 남은 시큐리티: {State.Player.SecurityStack.Count}장)");
                AddOpponentEvent(OpponentActionKind.SecurityResult, "내 시큐리티 디지몬 등장", $"{mySec.Name} — 배틀 없이 등장 · 남은 시큐리티 {State.Player.SecurityStack.Count}장", aiAttacker.TopCard, mySec);
                continue;
            }
            aiDp += BattleDpBonus(aiAttacker, opponentHasSources: false);
            int mySecDp = mySec.DP + SecurityDigimonDpBonus(State.Player, ownersTurn: false);
            if (mySecResult.PlayAfterBattle)
                PlaySecurityDigimonAfterBattle(State.Player, mySec);
            if (mySecDp >= aiDp && !HasJamming(aiAttacker))
            {
                bool aiRemoved = DeleteOpponentDigimon(aiAttacker);
                string winMsg = $"🛡️💥 [내 시큐리티 역격파!] AI [{aiAttacker.TopCard.Name}(DP {aiDp})]가 내 시큐리티 디지몬 [{mySec.Name}(DP {mySecDp})]와 배틀하여 역으로 소멸했습니다! (내 남은 시큐리티: {State.Player.SecurityStack.Count}장)";
                State.PlayerSecurityCheckHistory.Add(new SecurityCheckHistoryItem(
                    mySec, aiAttacker.TopCard.Name, aiDp, IsBattleWon: true, OutcomeSummary: $"💥 AI [{aiAttacker.TopCard.Name}] 역격파!"));
                Log(winMsg);
                AddOpponentEvent(OpponentActionKind.SecurityResult, "내 시큐리티 역격파!", $"내 {mySec.Name} (DP {mySecDp:N0}) ≥ {aiAttacker.TopCard.Name} (DP {aiDp:N0}) · 남은 시큐리티 {State.Player.SecurityStack.Count}장", aiAttacker.TopCard, mySec);
                if (aiRemoved)   // ≪방벽≫ 등으로 소멸을 막으면 남은 체크를 계속한다
                    break;
                continue;
            }
            else
            {
                string why = mySecDp >= aiDp ? " (≪재밍≫으로 소멸하지 않음)" : string.Empty;
                string loseMsg = $"🤖⚔️ AI [{aiAttacker.TopCard.Name}(DP {aiDp})]의 공격으로 내 시큐리티 [{mySec.Name}(DP {mySecDp})]이(가) 파괴되었습니다!{why} (내 남은 시큐리티: {State.Player.SecurityStack.Count}장)";
                State.PlayerSecurityCheckHistory.Add(new SecurityCheckHistoryItem(
                    mySec, aiAttacker.TopCard.Name, aiDp, IsBattleWon: false, OutcomeSummary: $"⚔️ 시큐리티 돌파됨 (내 DP {mySecDp} vs AI {aiDp}){why}"));
                Log(loseMsg);
                AddOpponentEvent(OpponentActionKind.SecurityResult, "내 시큐리티 파괴", $"내 {mySec.Name} (DP {mySecDp:N0}) vs {aiAttacker.TopCard.Name} (DP {aiDp:N0}){why} · 남은 시큐리티 {State.Player.SecurityStack.Count}장", aiAttacker.TopCard, mySec);
            }
        }
        FlushAfterSecurityBattle39();   // v0.39.0
    }

    /// <summary>
    /// AI 어택이 모두 끝난 뒤: 어택 후 메인 페이즈 → 턴 종료 처리 → 넘겨받은 메모리로 내 턴을 시작한다.
    /// </summary>
    private void FinishAiTurn(AiTurnContext ctx)
    {
        // 3-C. [어택 후 메인 페이즈] AI가 남은 메모리를 사용하여 진화/등장 (메모리가 마이너스(-)로 넘어가면 그 절대값이 플레이어의 다음 턴 메모리가 됨!)
        AiUseMainAbilities();   // 테이머 · 디지몬의 [메인] 기동 효과
        bool acted = true;
        while (ctx.Memory >= 0 && acted && !State.IsGameOver)
        {
            acted = false;

            // 1) 옵션 (정책)
            if (AiPolicy.ChooseOption(this, ctx.Memory, beforeAttacks: false) is { } option)
            {
                AiUseOption(ctx, option);
                acted = true;
                continue;
            }

            // 2) 진화 (코스트를 지불하여 AI 메모리가 음수로 내려갈 수 있음 — 상한은 정책의 MaxMemoryHandOver)
            if (AiPolicy.ChooseEvolution(this, ctx.Memory, beforeAttacks: false) is { } evo)
            {
                AiEvolve(ctx, evo.Base, evo.Card);
                acted = true;
            }
            // 2-B) DNA 진화 (정책)
            if (ctx.Memory >= 0 && AiPolicy.ChooseDnaEvolution(this, ctx.Memory, beforeAttacks: false) is { } dna && AiDnaEvolve(dna))
                acted = true;

            if (ctx.Memory < 0) break;

            // 2-C) 링크 (v0.41.0 — 메모리를 상대 쪽으로 넘기지 않는 것만, ≪분리≫의 지정 링크 카드 먼저)
            if (AiTryLink41(ctx)) { acted = true; continue; }
            if (AiTryAppFusion43(ctx)) { acted = true; continue; }   // v0.43.0

            // 3) 테이머 등장
            var tamerCandidate = AiPolicy.ChooseTamerToPlay(this, ctx.Memory);
            if (tamerCandidate != null && State.DummyOpponent.Hand.Contains(tamerCandidate))
            {
                State.DummyOpponent.Hand.Remove(tamerCandidate);
                int beforeMem = ctx.Memory;
                AddTamer(State.DummyOpponent, tamerCandidate);
                ctx.Memory -= CardScripts.EffectivePlayCost(State.DummyOpponent, tamerCandidate, State.Player);
                RecalculateFieldBuffs();
                Log($"🤖 [AI 테이머 등장] [{tamerCandidate.Name}(코스트 {tamerCandidate.PlayCost})] 배치! (AI 메모리: +{beforeMem} → {ctx.Memory})");
                AddOpponentEvent(OpponentActionKind.Tamer, "테이머 등장", $"{tamerCandidate.Name} · 메모리 {beforeMem} → {ctx.Memory}", tamerCandidate);
                AddEffectEvent(tamerCandidate, CaptureEffectLogs(() =>
                {
                    RunEffects(EffectTrigger.OnPlay, State.DummyOpponent, null, tamerCandidate, includeInherited: false);
                    FireForSide(EffectTrigger.AllyTamerPlayed, State.DummyOpponent, null, triggerCard: tamerCandidate);
                    FireForSide(EffectTrigger.OpponentTamerPlayed, State.Player, null, triggerCard: tamerCandidate);
                }));
                acted = true;
                if (ctx.Memory < 0) break;
            }

            // 4) 패에서 디지몬 등장 (메모리가 0 미만이 되어 플레이어에게 메모리가 넘어가도록 코스트 지불!)
            var playCandidate = AiPolicy.ChooseDigimonToPlay(this, ctx.Memory);
            if (playCandidate != null && State.DummyOpponent.Hand.Contains(playCandidate) && !EffectRules.PlayDpBlocked(State, State.DummyOpponent, playCandidate))   // v0.37.0
            {
                AiPlayDigimon(ctx, playCandidate);
                acted = true;
            }
        }

        if (State.IsGameOver)
        {
            _aiTurn = null;
            return;
        }

        // ≪진격≫: 메모리가 상대 쪽이 되었어도 그 디지몬으로 어택한다 → 어택이 끝나면 RunAiTurn이 다시 여기로 온다
        if (ctx.BlitzAttacker is { } blitz)
        {
            if (CanAttackThisTurn(blitz) && State.DummyOpponent.BattleArea.Contains(blitz))
            {
                Log($"🤖 [≪진격≫] AI [{blitz.TopCard.Name}]이(가) 메모리가 상대 쪽이지만 어택합니다!");
                ctx.Attackers.Enqueue(blitz);
                RunAiTurn();
                return;
            }
            ctx.BlitzAttacker = null;
        }

        EndAiTurn(ctx);
    }

    /// <summary>AI 턴 종료: [자신의 턴 종료 시] → ≪볼텍스≫ · ≪에그제큐트≫ 어택(RunAiTurn이 처리하고 다시 여기로) → [상대의 턴 종료 시] → 내 턴</summary>
    private void EndAiTurn(AiTurnContext ctx)
    {
        var endLabel = State.DummyOpponent.Tamers.FirstOrDefault() ?? State.DummyOpponent.BattleArea.FirstOrDefault()?.TopCard ?? new CardData { Name = "상대" };
        if (!ctx.EndPhase)
        {
            ctx.EndPhase = true;
            _endOfTurnAttacked.Clear();
            _overclocked.Clear();
            AddEffectEvent(endLabel, CaptureEffectLogs(() => TriggerEndOfTurn(State.DummyOpponent)));
        }
        if (!State.IsGameOver && ctx.Attackers.Count > 0)
        {
            RunAiTurn();   // [자신의 턴 종료 시] "~ may attack" 효과로 큐에 들어온 어택 → 끝나면 다시 여기로
            return;
        }
        if (!State.IsGameOver && NextAiEndOfTurnAttack(ctx) is { } eot)
        {
            Log($"🤖 [{eot.Label}] AI [{eot.Attacker.TopCard.Name}]이(가) 턴 종료 시 어택합니다!");
            ctx.AttackTarget = eot.Target;
            ctx.Attackers.Enqueue(eot.Attacker);
            RunAiTurn();
            return;
        }
        if (State.IsGameOver)
        {
            _aiTurn = null;
            return;
        }
        AddEffectEvent(endLabel, CaptureEffectLogs(() => TriggerEndOfOpponentTurn(State.Player)));
        int aiMemory = ctx.Memory;
        int aiEndOfTurnMemoryLoss = ctx.EndOfTurnMemoryLoss;
        if (aiMemory >= 0)
        {
            // AI가 더 이상 낼 카드가 없어 패스(Pass)를 선언한 경우 -> 공식 룰에 따라 상대(플레이어)에게 3메모리 제공
            aiMemory = -3;
            Log("🤖 [AI 턴 종료] AI가 더 이상 행동하지 않고 패스(Pass)를 선언하여 메모리가 -3(내 메모리 +3)으로 넘어옵니다.");
        }
        else
        {
            Log($"🤖 [AI 턴 종료] AI 메모리가 {aiMemory}(0 미만)이 되어 내 턴(시작 메모리 +{Math.Abs(aiMemory)})으로 넘어옵니다!");
        }

        // [이 턴 종료 시] 메모리 -3 (AI의 메탈그레이몬 BT1-021 등)
        if (aiEndOfTurnMemoryLoss > 0)
        {
            aiMemory -= aiEndOfTurnMemoryLoss;
            Log($"🤖 [이 턴 종료 시] AI 메모리 -{aiEndOfTurnMemoryLoss} → 내 시작 메모리 +{Math.Abs(aiMemory)}");
        }
        aiMemory = Math.Max(aiMemory, -10);

        // [상대의 턴 종료 시] 토이아구몬 · 그레이몬(흑) 진화원: 이 턴에 상대가 한 번도 어택하지 않았다면 ≪1 드로우≫
        if (!State.OpponentAttackedThisTurn)
            TriggerOpponentTurnEndWithoutAttack(State.Player);

        AddOpponentEvent(OpponentActionKind.TurnEnd, "상대 턴 종료", $"내 턴 시작 · 메모리 +{Math.Abs(aiMemory)}");

        _aiTurn = null;
        if (ctx.IsOpeningTurn)
            StartFirstPlayerTurnAfterOpponent(Math.Abs(aiMemory));
        else
            StartNextPlayerTurn(Math.Abs(aiMemory));
    }

    /// <summary>AI 진화: 코스트 지불 → 진화 보너스 드로우 → [진화 시] 효과 (배틀 에어리어의 디지몬만, 플레이어와 같은 카드 스크립트)</summary>
    private void AiEvolve(AiTurnContext ctx, CardInstance fieldDigimon, CardData evoCard)
    {
        var opp = State.DummyOpponent;
        int baseCost = ActionValidator.EvolutionCost(fieldDigimon, evoCard);
        NoteSpecialEvo42(fieldDigimon, evoCard, baseCost);   // v0.42.0 집계
        int evoCost = baseCost - TryDigisorption(opp, fieldDigimon, evoCard, baseCost, ctx.Memory);
        opp.Hand.Remove(evoCard);
        evoCost = ApplyEvoCostModifiers(opp, fieldDigimon, evoCard, evoCost);
        int beforeMem = ctx.Memory;
        string prev = fieldDigimon.TopCard.Name;
        fieldDigimon.Evolve(evoCard);
        ctx.Memory -= evoCost;
        DrawFor(opp);
        RecalculateFieldBuffs();
        Log($"🤖 [AI 진화] [{prev}] -> [{evoCard.Name}(Lv.{evoCard.Level}, 코스트 {evoCost})] (AI 메모리: +{beforeMem} → {ctx.Memory})");
        AddOpponentEvent(OpponentActionKind.Evolve, "진화", $"{prev} → {evoCard.Name} (Lv.{evoCard.Level}) · 메모리 {beforeMem} → {ctx.Memory}", evoCard);

        if (opp.BattleArea.Contains(fieldDigimon))
            AddEffectEvent(evoCard, CaptureEffectLogs(() => { ApplyWhenDigivolvingEffects(opp, fieldDigimon, evoCard); FireForSide(EffectTrigger.AllyDigimonDigivolved, opp, fieldDigimon); }));
        else if (ReferenceEquals(opp.BreedingArea, fieldDigimon))
            AddEffectEvent(evoCard, CaptureEffectLogs(() => RunBreedingEffects42(EffectTrigger.WhenDigivolving, opp, null, evoCard)));   // v0.42.0 [육성]【진화 시】 (EX10-013)
    }

    /// <summary>AI 등장: 코스트 지불 → [등장 시] 효과 (플레이어와 같은 카드 스크립트)</summary>
    private void AiPlayDigimon(AiTurnContext ctx, CardData card)
    {
        var opp = State.DummyOpponent;
        opp.Hand.Remove(card);
        int beforeMem = ctx.Memory;
        var xros = TakeXrosMaterials(opp, card);   // 디지크로스: AI는 패의 카드만
        // 코스트는 필드에 놓기 전에 정한다 (플레이어와 같다 — "by deleting 1 of your Digimon"이 이 카드를 고르지 않게)
        int cost = Math.Max(0, CardScripts.EffectivePlayCost(opp, card, State.Player) - (PlayCostReductionBlocked(opp, card) ? 0 : XrosReduction(card, xros.Count)));   // v0.36.0
        cost = ApplyPlayCostOptions(opp, card, cost);
        var summoned = new CardInstance(card, State.CurrentTurn) { IsOwnersTurn = true };
        PlaceXrosMaterials(summoned, xros);
        PlacePendingUnderPlayed(summoned);   // v0.34.0
        opp.BattleArea.Add(summoned);
        ctx.Memory -= cost;
        RecalculateFieldBuffs();
        Log($"🤖 [AI 등장] [{card.Name}(Lv.{card.Level}, 코스트 {cost})] 소환! (AI 메모리: +{beforeMem} → {ctx.Memory})");
        AddOpponentEvent(OpponentActionKind.Play, "등장", $"{card.Name} (Lv.{card.Level}) · 메모리 {beforeMem} → {ctx.Memory}", card);

        AddEffectEvent(card, CaptureEffectLogs(() => WithXros(summoned, xros.Count, () => ApplyOnPlayEffects(opp, summoned))));
    }

    /// <summary>run이 남긴 로그 (줄 머리 "[T.. | .. | M:..] " 제외)</summary>
    private List<string> CaptureEffectLogs(Action run)
    {
        int before = State.ActionLogs.Count;
        run();
        return State.ActionLogs.Skip(before)
            .Select(l => l.IndexOf("] ", StringComparison.Ordinal) is var i and >= 0 ? l[(i + 2)..] : l)
            .ToList();
    }

    /// <summary>AI 카드 효과를 상대 턴 리플레이에 한 장면으로 남긴다</summary>
    private void AddEffectEvent(CardData card, List<string> effectLogs)
    {
        if (effectLogs.Count > 0)
            AddOpponentEvent(OpponentActionKind.Effect, $"{card.Name} 효과", string.Join("\n", effectLogs), card);
    }

    /// <summary>
    /// AI 옵션 사용: 플레이어의 PlayCard 옵션 처리와 같게 — 패에서 빼고 코스트 지불 → [메인] 효과(target = 정책이 고른 대상, null이면 AI 평가 순) → 트래시.
    /// (턴을 넘기며 상대에게 넘겨줄 메모리 상한은 AiPolicy.MaxMemoryHandOver)
    /// </summary>
    private void AiUseOption(AiTurnContext ctx, AiOptionUse use)
    {
        var opp = State.DummyOpponent;
        if (EffectRules.OptionUseBlocked(State, opp) || !opp.Hand.Remove(use.Card))   // v0.36.0 옵션 사용 제한
            return;
        int beforeMem = ctx.Memory;
        ctx.Memory -= CardScripts.EffectivePlayCost(opp, use.Card, State.Player);
        RecalculateFieldBuffs();   // v0.37.0 메모리 조건 지속 효과
        Log($"🤖 [AI 옵션 사용] [{use.Card.Name}(코스트 {use.Card.PlayCost})] (AI 메모리: +{beforeMem} → {ctx.Memory})");
        AddOpponentEvent(OpponentActionKind.Play, "옵션 사용", $"{use.Card.Name} · 메모리 {beforeMem} → {ctx.Memory}", use.Card);
        var target = use.Target != null && (State.Player.BattleArea.Contains(use.Target) || opp.BattleArea.Contains(use.Target)) ? use.Target : null;
        AddEffectEvent(use.Card, CaptureEffectLogs(() => ApplyOptionEffect(opp, use.Card, target)));
    }

    /// <summary>AI가 진화시킬 수 있는 디지몬: 배틀 에어리어 + 육성 에어리어 (육성 중인 Lv.2 → Lv.3 진화 포함)</summary>
    private List<CardInstance> AiEvolutionBases()
    {
        var bases = State.DummyOpponent.BattleArea.ToList();
        if (State.DummyOpponent.BreedingArea != null)
            bases.Add(State.DummyOpponent.BreedingArea);
        return bases;
    }

    private void AddOpponentEvent(OpponentActionKind kind, string title, string detail, CardData? card = null, CardData? subCard = null) =>
        State.OpponentTurnEvents.Add(new OpponentActionEvent(kind, title, detail, card, subCard));
}
