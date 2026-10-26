using System;
using System.Collections.Generic;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>효과가 발동하는 시점 (카드 텍스트의 【 】/[ ] 표기)</summary>
public enum EffectTrigger
{
    OnPlay,                         // [등장 시]
    WhenDigivolving,                // [진화 시]
    WhenAttacking,                  // [어택 시]
    OnDeletion,                     // [소멸 시]
    Main,                           // 옵션 [메인]
    Security,                       // [시큐리티]
    AttackBlocked,                  // 이 디지몬의 어택이 블록당했을 때
    DeletedOpponentInBattle,        // 배틀에서 상대 디지몬만 소멸시켰을 때
    OpponentDigimonDeletedByZeroDp, // 상대 디지몬이 DP 0이 되어 소멸했을 때
    AllySameNamePlayed,             // 같은 명칭의 다른 자신의 디지몬이 등장했을 때
    OpponentTurnEndWithoutAttack,   // [상대의 턴 종료 시] 이 턴에 상대가 어택하지 않았다면
    StartOfYourTurn,                // [자신의 턴 개시 시]
    EndOfAttack,                    // [어택 종료 시]
    StartOfYourMainPhase,           // [자신의 메인 페이즈 개시 시]
    EndOfYourTurn,                  // [자신의 턴 종료 시]
    EndOfOpponentsTurn,             // [상대의 턴 종료 시]
    ThisDigimonSuspends,            // 이 디지몬이 레스트 상태가 되었을 때
    OpponentDigimonAttacks,         // 상대의 디지몬이 어택했을 때 (Other = 어택한 디지몬)
    OpponentDigimonDeleted,         // 상대의 디지몬이 소멸했을 때 (Other = 소멸한 디지몬)
    AllyDigimonDeleted,             // 다른 자신의 디지몬이 소멸했을 때
    AllyDigimonPlayed,              // 자신의 디지몬이 등장했을 때 (Other = 등장한 디지몬)
    AllyDigimonDigivolved,          // 자신의 디지몬이 진화했을 때 (Other = 진화한 디지몬)
    AllyDigimonAttacks,             // 자신의 디지몬이 어택했을 때 (Other = 어택한 디지몬)
    AllyDigimonSuspended,           // 자신의 디지몬이 레스트 상태가 되었을 때 (Other)
    OpponentDigimonSuspended,       // 상대의 디지몬이 레스트 상태가 되었을 때 (Other)
    WonBattle,                      // 이 디지몬이 배틀에서 이겼을 때 (상대만 소멸, 어택 · 블록 모두)
    WhenMoving,                     // [이동 시] 육성 에어리어에서 배틀 에어리어로 이동했을 때
    ActivateMain,                   // 테이머 · 디지몬의 [메인] 기동 효과 (플레이어가 메인 페이즈에 사용 — ≪디지버스트≫ 포함)
    AllyTamerSuspended,             // 자신의 테이머가 레스트 상태가 되었을 때 (EffectContext.TriggerCard = 그 테이머)
    ThisDigimonUnsuspendedInMain,   // 이 디지몬이 (자신의) 메인 페이즈에 액티브 상태가 되었을 때
    ThisDigimonUnsuspendedInUnsuspendPhase, // 이 디지몬이 (자신의) 액티브 페이즈에 액티브 상태가 되었을 때
    OpponentDigimonPlayed,          // 상대의 디지몬이 등장했을 때 (Other = 등장한 디지몬)
    AllyOptionUsed,                 // 자신이 옵션 카드를 사용했을 때 (TriggerCard = 그 옵션)
    OpponentOptionUsed,             // 상대가 옵션 카드를 사용했을 때 (TriggerCard = 그 옵션)
    DigiBurstTrashedThis,           // ≪디지버스트≫ 비용으로 이 카드(진화원)가 트래시로 갔을 때 (진화원 효과)
    AllyDigiBurst,                  // 자신의 디지몬이 ≪디지버스트≫를 발휘했을 때 (Other = 그 디지몬)
    OpponentTamerPlayed,            // 상대가 테이머를 등장시켰을 때 (TriggerCard = 그 테이머)
    OpponentTamerSuspended,         // 상대의 테이머가 레스트 상태가 되었을 때 (TriggerCard = 그 테이머)
    ThisDigimonLinked,              // 이 디지몬에 카드가 링크되었을 때 ("When this Digimon gets linked")
    OnLinked,                       // 이 카드가 링크되었을 때 (링크 카드의 【링크 시】 효과 — Self = 링크된 디지몬)
    AllyDigimonLinked,              // 자신의 디지몬에 카드가 링크되었을 때 (Other = 그 디지몬)
    ThisDigimonWouldLeave,          // 이 디지몬이 배틀 에어리어를 떠나려 할 때 (소멸 · 패 · 덱 · 시큐리티 — PreventLeave로 막을 수 있다)
    ThisDigimonWouldBeDeleted,      // 이 디지몬이 소멸하려 할 때
    AllyDigimonWouldLeave,          // 자신의 다른 디지몬이 배틀 에어리어를 떠나려 할 때 (Other = 그 디지몬)
    AllyDigimonWouldBeDeleted,      // 자신의 다른 디지몬이 소멸하려 할 때 (Other = 그 디지몬)
    AllyDigimonMovedFromBreeding,   // 자신의 디지몬이 육성 에어리어에서 배틀 에어리어로 이동했을 때 (Other = 그 디지몬 — 육성 페이즈 · 효과 모두)
    OpponentDigimonMovedFromBreeding, // 상대의 디지몬이 육성 에어리어에서 배틀 에어리어로 이동했을 때 (Other = 그 디지몬)
    AllyHatched,                    // 자신이 육성 에어리어에 부화했을 때 (효과로 육성 에어리어에 등장한 것은 부화가 아니다)
    AllyFaceUpSecurityPlaced,       // 자신의 시큐리티에 카드가 앞면으로 놓였을 때 (TriggerCard = 그 카드)
    OpponentFaceUpSecurityPlaced,   // 상대의 시큐리티에 카드가 앞면으로 놓였을 때 (TriggerCard)
    AllyCheckedFaceUpSecurity,      // 자신의 디지몬이 앞면 시큐리티 카드를 체크했을 때 (Other = 체크한 디지몬, TriggerCard = 그 카드)
    ThisDigimonWouldDnaDigivolve,   // 이 디지몬이 DNA 진화하려 할 때 (TriggerCard = DNA 진화할 카드)
    AllySecurityRemoved,            // 자신의 시큐리티에서 카드가 빠졌을 때 (체크 · 파기 · 패에 추가 등, TriggerCard = 빠진 카드)
    OpponentSecurityRemoved,        // 상대의 시큐리티에서 카드가 빠졌을 때 (TriggerCard)
    AllySecurityAdded,              // 자신의 시큐리티에 카드가 놓였을 때 (≪리커버리≫ 등, TriggerCard = 놓인 카드)
    AttackTargetSwitched,           // 어느 쪽이든 어택 대상이 바뀌었을 때 (효과 · ≪돌진≫, Other = 어택 중인 디지몬)
    AllyAttackTargetSwitched,       // 자신의 디지몬의 어택 대상이 바뀌었을 때 (Other = 어택 중인 자신의 디지몬)
    AllyHandTrashed,                // 자신의 패가 (효과 · 비용으로) 버려졌을 때 (TriggerCard = 버린 카드 중 1장)
    OpponentHandTrashed,            // 상대의 패가 버려졌을 때 (TriggerCard)
    AllyDeckTrashed,                // 자신의 덱에서 카드가 파기되었을 때 (덱 위 파기, TriggerCard = 파기한 카드 중 1장)
    OpponentHandAddedByEffect,      // 효과로 상대의 패에 카드가 들어갔을 때 (패로 되돌리기 등, TriggerCard)
    HandMain,                       // [패] [메인]: 패의 이 카드로 쓰는 기동 효과 (플레이어 · AI가 메인 페이즈에 고른다)
    TrashMain,                      // [트래시] [메인]: 트래시의 이 카드로 쓰는 기동 효과
    AllyTamerPlayed,                // 자신이 테이머를 등장시켰을 때 (TriggerCard = 그 테이머 — 패 · 효과 모두)
    AllyDigimonUnsuspended,         // 자신의 디지몬이 액티브 상태가 되었을 때 (Other = 그 디지몬 — 액티브 페이즈 · 효과 모두)
    OpponentDigimonUnsuspended,     // 상대의 디지몬이 액티브 상태가 되었을 때 (Other)
    AllyDeletedOpponentInBattle,    // 자신의 디지몬이 배틀에서 상대 디지몬을 소멸시켰을 때 (Other = 이긴 자신의 디지몬, 어택 · 블록 모두 — 함께 소멸해도)
    SourceTrashedThis,              // 이 카드가 효과로 진화원에서 파기되었을 때 (Self = 그 진화원을 가졌던 디지몬, 파기된 카드의 진화원 효과로 처리)
    AllySourcesTrashed,             // 자신의 디지몬의 진화원이 효과로 파기되었을 때 (Other = 그 디지몬, GoldfishEngine.CurrentSourceCards = 파기된 카드)
    OpponentSourcesTrashed,         // 상대 디지몬의 진화원이 효과로 파기되었을 때 (Other = 그 디지몬)
    AllySourcesAdded,               // 효과로 자신의 디지몬의 진화원에 카드가 놓였을 때 (Other = 그 디지몬, CurrentSourceCards = 놓인 카드 — 진화로 겹친 것은 아님)
}

/// <summary>효과가 적힌 곳 — 맨 위 카드(메인)인지, 진화원인지</summary>
public enum EffectSource { Main, Inherited }

/// <summary>규칙이 정해져 있어서 이름만으로 동작하는 키워드 효과</summary>
public enum Keyword
{
    Blocker,                 // ≪블로커≫
    Jamming,                 // ≪재밍≫: 시큐리티 디지몬과의 배틀로는 소멸하지 않는다
    Piercing,                // ≪관통≫: 배틀에서 상대 디지몬만 소멸시키면 시큐리티 체크
    Retaliation,             // ≪길동무≫: 배틀에서 소멸하면 상대 디지몬도 소멸
    Reboot,                  // ≪재기동≫: 상대의 액티브 페이즈에도 액티브
    UnblockableBySourceless, // 진화원을 갖지 않은 상대 디지몬에게는 블록당하지 않는다 (원뿔몬 BT1-034)
    LinkPlusOne,             // ≪링크 +1≫: 이 디지몬의 최대 링크 수 +1 (여러 번 얻으면 더한다 — 엔진은 키워드 1개 = +1)
    CannotAttack,            // 이 디지몬은 어택할 수 없다 (지속 효과 "[Your Turn] This Digimon can't attack")
    Unblockable,             // 이 디지몬은 블록당하지 않는다
    BlockOpponentEvoCostReduction, // 상대는 진화 코스트를 줄일 수 없다 ("Your opponent can't reduce digivolution costs")
    CannotBeAttacked,        // 이 디지몬은 어택당하지 않는다
    NegateSecurityOptions,   // 체크한 옵션 카드의 [시큐리티] 효과는 발휘하지 않는다 (워그레이몬 BT1-025)
    Rush,                    // ≪속공≫: 등장한 턴에도 어택할 수 있다
    Raid,                    // ≪돌진≫: 어택했을 때 어택 대상을 가장 DP가 높은 액티브 상태의 상대 디지몬 1마리로 바꿀 수 있다
    Alliance,                // ≪연계≫: 어택했을 때 다른 자신의 디지몬 1마리를 레스트시켜, 이 어택 동안 그 DP를 더하고 ≪시큐리티 어택 +1≫
    Barrier,                 // ≪방벽≫: 배틀에서 소멸할 때 자신의 시큐리티 위에서 1장 파기하는 것으로 소멸하지 않는다
    Evade,                   // ≪회피≫: 소멸할 때 레스트시키는 것으로 소멸하지 않는다
    ArmorPurge,              // ≪아머 퍼지≫: 소멸할 때 이 디지몬의 맨 위 카드를 파기하는 것으로 소멸하지 않는다
    Fortitude,               // ≪불굴≫: 진화원을 가진 이 디지몬이 소멸했을 때 이 카드를 코스트 없이 등장
    Collision,               // ≪충돌≫: 이 디지몬이 어택하는 동안 상대 디지몬 전부가 ≪블로커≫를 얻고 상대는 가능하면 블록한다
    Vortex,                  // ≪볼텍스≫: 자신의 턴 종료 시 이 디지몬으로 상대의 디지몬에게 어택할 수 있다 (등장한 턴에도)
    Execute,                 // ≪에그제큐트≫: 자신의 턴 종료 시 이 디지몬으로 어택할 수 있다 (액티브 디지몬도). 그 어택 종료 시 이 디지몬은 소멸
    Scapegoat,               // ≪스케이프고트≫: 자신의 효과 이외로 소멸할 때 다른 자신의 디지몬 1마리를 소멸시키는 것으로 소멸하지 않는다
    Iceclad,                 // ≪빙장≫: 시큐리티 디지몬 이외와의 배틀에서는 DP가 아니라 진화원 매수를 비교한다
    Progress,                // ≪프로그레스≫: 어택 중인 이 디지몬은 상대의 효과를 받지 않는다
    AttackTargetLocked,      // (지속 효과) 이 디지몬의 어택 대상은 바뀌지 않는다 ("[Your Turn] This Digimon's attack target can't change")
}

/// <summary>지속 효과가 바꾸는 수치</summary>
public enum StaticStat
{
    Dp,                 // 이 디지몬의 DP (진화원 · 자기 자신)
    DpInBattle,         // 배틀하고 있는 동안만 더하는 DP (StaticContext.BattleOpponentHasSources로 상대 조건 판단)
    SecurityAttack,     // 이 디지몬의 ≪시큐리티 어택 +N≫
    AllyDigimonDp,      // 테이머: 자신의 디지몬 전부의 DP
    SecurityDigimonDp,  // 테이머: 자신의 시큐리티 디지몬의 DP
    Keyword,            // 이 디지몬이 키워드를 얻는다 (StaticEffect.Keyword, 값이 0이 아니면 얻은 상태 — 조건부 키워드)
    EnemySecurityDigimonDp, // 상대의 시큐리티 디지몬의 DP (예: "All of your opponent's Security Digimon get -1000 DP")
    AllyKeyword,        // 자신의 디지몬 전부(Subject 조건)가 키워드를 얻는다 (StaticEffect.Keyword) — "All of your level 4 or lower Digimon gain ＜Jamming＞"
    AllySecurityAttack, // 자신의 디지몬 전부(Subject 조건)의 ≪시큐리티 어택 ±N≫
    EnemySecurityAttack, // 상대의 디지몬 전부(Subject 조건)의 ≪시큐리티 어택 ±N≫
}

/// <summary>옵션 [메인] 효과를 쓸 때 UI가 고르게 할 대상</summary>
public enum TargetKind { None, OwnDigimon, OwnDigimonWithDigimonSource, OpponentDigimon }

/// <summary>
/// 카드 효과 한 절: 발동 시점 + 출처 + (조건) + 처리.
/// 같은 시점에 여러 효과가 발동하면 메인 효과 → 진화원 효과(아래에서 위 순서)로 처리한다.
/// </summary>
public sealed record CardEffect(EffectTrigger Trigger, EffectSource Source, Action<EffectContext> Resolve)
{
    /// <summary>발동 조건 (없으면 항상). [턴에 1회]는 조건을 만족했을 때만 사용 기록을 남긴다.</summary>
    public Func<EffectContext, bool>? Condition { get; init; }

    /// <summary>[턴에 1회] — 효과를 가진 디지몬마다 카드 번호 기준으로 기록 (CardInstance.UsedOncePerTurn)</summary>
    public bool OncePerTurn { get; init; }

    /// <summary>[턴에 2회]</summary>
    public bool TwicePerTurn { get; init; }
}

/// <summary>지속 효과 한 절 — 필드 상태가 바뀔 때마다 엔진이 다시 계산한다</summary>
public sealed record StaticEffect(StaticStat Stat, EffectSource Source, Func<StaticContext, int> Value)
{
    /// <summary>Stat == Keyword일 때 얻는 키워드</summary>
    public Keyword? Keyword { get; init; }

    /// <summary>AllyDigimonDp: 효과를 받는 디지몬 조건 (예: "All of your black Digimon") — null이면 전부</summary>
    public Func<CardInstance, bool>? Subject { get; init; }
}

/// <summary>지속 효과 계산에 필요한 정보</summary>
public sealed class StaticContext
{
    public required GameState State { get; init; }
    public required PlayerZones Owner { get; init; }
    public required PlayerZones Enemy { get; init; }

    /// <summary>효과를 가진 디지몬 (테이머 효과면 효과를 받는 디지몬)</summary>
    public CardInstance? Digimon { get; init; }

    /// <summary>지금이 효과 주인의 턴인지 ([자신의 턴] / [상대의 턴] 효과)</summary>
    public bool OwnersTurn { get; init; }

    /// <summary>DpInBattle 계산 때: 배틀 상대가 진화원을 가졌는지 (시큐리티 디지몬은 진화원이 없다)</summary>
    public bool BattleOpponentHasSources { get; init; }
}

/// <summary>[자신의 턴 개시 시] 메모리 보정 (테이머) — 상대 필드가 Condition을 만족하면 장마다 Amount. Reason은 로그 표시용</summary>
public sealed record StartOfTurnMemoryEffect(int Amount, Func<PlayerZones, bool> Condition, string Reason);

/// <summary>카드 한 장의 효과 정의 전부</summary>
public sealed class CardScript
{
    public CardScript(string cardId) => CardId = cardId;

    public string CardId { get; }
    public List<CardEffect> Effects { get; } = new();
    public List<StaticEffect> Statics { get; } = new();
    public HashSet<Keyword> Keywords { get; } = new();
    public HashSet<Keyword> InheritedKeywords { get; } = new();

    /// <summary>옵션 [메인] 효과의 대상 종류 (UI 대상 선택)</summary>
    public TargetKind MainTarget { get; set; }

    /// <summary>옵션 [메인] 효과의 대상 조건 (예: 전기 충격은 레스트 상태인 상대 디지몬만) — 없으면 MainTarget의 디지몬 아무나</summary>
    public Func<CardInstance, bool>? MainTargetFilter { get; set; }

    /// <summary>대상 조건의 설명 ("레스트 상태인 상대 디지몬") — 조건에 안 맞는 대상에 쓰려 할 때 안내</summary>
    public string? MainTargetRule { get; set; }

    /// <summary>카드에 적힌 다른 진화 조건 (예: 오메가몬 "Lv.6 레드 · 블루에서 진화") — 진화 대상 디지몬을 받아 진화할 수 있는지</summary>
    public Func<CardInstance, bool>? AlternativeEvolution { get; set; }

    /// <summary>"Your [Shoutmon] can digivolve into this card in your hand for a memory cost of 4, ignoring this card's digivolution requirements" — 진화 조건을 무시하는 다른 진화 (대상 조건, 코스트)</summary>
    public List<(Func<CardInstance, bool> From, int Cost)> SpecialEvolutions { get; } = new();

    public StartOfTurnMemoryEffect? StartOfTurnMemory { get; set; }

    /// <summary>ACE ≪오버플로우 -N≫ — 이 카드가 배틀 에어리어 · 진화원에서 다른 곳으로 가면 주인의 메모리 -N (0이면 없음)</summary>
    public int OverflowMemory { get; set; }

    /// <summary>≪디지섭션 -N≫ — 패에서 이 카드로 진화할 때 자신의 다른 디지몬 1마리를 레스트시켜 진화 코스트 -N (0이면 없음)</summary>
    public int DigisorptionReduction { get; set; }

    /// <summary>[패] [카운터] ≪블래스트 진화≫ — 상대 디지몬이 어택했을 때 패의 이 카드로 자신의 디지몬을 코스트 없이 진화시킬 수 있다</summary>
    public bool BlastDigivolve { get; set; }

    /// <summary>≪머티리얼 세이브 N≫ — 이 디지몬이 소멸할 때 진화원 N장을 자신의 테이머 아래에 놓을 수 있다 (0이면 없음)</summary>
    public int MaterialSave { get; set; }

    /// <summary>≪트레이닝≫ — 육성 에어리어에서도 [메인] 기동 효과를 쓸 수 있다</summary>
    public bool ActivateInBreeding { get; set; }

    /// <summary>링크 조건 ("Link Requirements [Link] [Appmon] trait: Cost 2") — 이 카드를 링크할 수 있는 디지몬. null = 영어 데이터에 없음</summary>
    public Func<CardInstance, bool>? LinkRequirement { get; set; }
    public int LinkCost { get; set; }

    /// <summary>디지크로스에서만 이 이름으로도 취급 ("This card is also treated as [Shoutmon] for a DigiXros") — 영어 이름</summary>
    public List<string> XrosAliases { get; } = new();

    /// <summary>"(While ~,) cards from your trash may also be placed for this card's DigiXros" — 이 조건(자신의 필드)이면 트래시도 디지크로스 재료</summary>
    public Func<PlayerZones, bool>? XrosFromTrash { get; set; }

    /// <summary>테이머: "When you would play 1 Digimon (조건) with DigiXros requirements, by suspending this Tamer, you may place cards from under your Tamers ~" — 그 디지몬 조건</summary>
    public Func<CardData, bool>? XrosUnderTamersFor { get; set; }

    /// <summary>옵션: 이 조건(자신의 필드)을 만족하면 색 조건을 무시하고 [메인]으로 쓸 수 있다</summary>
    public Func<PlayerZones, bool>? IgnoreColorRequirement { get; set; }

    /// <summary>"이 카드를 등장시킬 때 / 사용할 때 코스트 -N" — (자신의 필드, 상대의 필드)를 받아 줄일 코스트를 돌려준다</summary>
    public Func<PlayerZones, PlayerZones, int>? PlayCostReduction { get; set; }

    /// <summary>"When this card would be played, by deleting 1 of your Digimon, reduce the play cost by 4." — 등장할 때 비용을 낼지 묻는 코스트 감소</summary>
    public List<PlayCostOption> PlayCostOptions { get; } = new();

    /// <summary>"When this Digimon would digivolve (into ~), reduce the digivolution cost by N" — 진화하기 직전에 진화 코스트를 바꾸는 효과</summary>
    public List<EvoCostModifier> EvoCostModifiers { get; } = new();
}

/// <summary>진화 코스트 감소가 걸리는 진화: 이 디지몬이 진화할 때 · 자신의 디지몬이 진화할 때 · (패의) 이 카드로 진화할 때</summary>
public enum EvoCostSubject { ThisDigimon, AnyOwnDigimon, IntoThisCard }

/// <summary>
/// 진화 코스트 감소 효과 하나. Source = 이 효과가 적힌 곳 (진화원 효과면 그 카드가 진화원에 있을 때만).
/// Subject 조건(SubjectFilter: 효과 상황, 진화하는 디지몬) · 진화할 카드 조건(Into) · 필드 조건(Condition) · 비용(Cost — 낼지 묻는다)을 모두 만족하면 Reduce만큼 줄인다.
/// </summary>
public sealed record EvoCostModifier(
    EffectSource Source,
    EvoCostSubject Subject,
    Func<EffectContext, CardInstance, bool>? SubjectFilter,
    Func<CardData, bool>? Into,
    Func<PlayerZones, PlayerZones, bool>? Condition,
    PlayCostOption? Cost,
    int Reduce,
    bool OncePerTurn,
    bool YourTurnOnly);

/// <summary>등장할 때 고르는 코스트 감소: 비용(낼 수 있는지 · 내기 · 추천 · 설명)을 내면 Reduce만큼 줄인다. CostText = 원문 비용 구절 (예: "deleting 1 of your Digimon" — AI가 비용 종류를 가늠)</summary>
public sealed record PlayCostOption(int Reduce, Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe, string CostText = "");
