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
    ThisTrashedFromSecurity,        // 효과가 이 카드를 시큐리티에서 (직접) 파기했을 때 — 카드 자신의 효과 (공식 Q&A BT13-098: 오픈 · 체크 뒤 파기는 아님, v0.29.0)
    ThisTrashedFromDeck,            // 이 카드가 덱에서 (직접) 파기되었을 때 — 카드 자신의 효과 (공식 Q&A P-077: 오픈 · 확인 중인 카드는 아님)
    ThisTrashedFromHand,            // 이 카드가 패에서 파기되었을 때 — 카드 자신의 효과 (Q&A BT10-080)
    AllyHandAddedByEffect,          // 효과로 자신의 패에 카드가 들어갔을 때 (드로우 포함 — 종합 룰 4-14-1, TriggerCard = 들어간 카드 중 1장)
    ThisTrashedFromBattleArea,      // 배틀 에어리어에 놓인 이 옵션 카드가 파기되었을 때 — 카드 자신의 효과 (v0.30.0, BT19 디바이스 · P-159 · P-161)
    AllyOptionPlaced,               // 자신의 옵션 카드가 배틀 에어리어에 놓였을 때 (TriggerCard = 그 옵션, v0.30.0 — BT23-003 · BT23-053)
    OptionTrashedFromBattleArea,    // 배틀 에어리어의 옵션 카드가 파기되었을 때 (어느 쪽이든, TriggerCard = 그 옵션 — 공식 Q&A BT23-059: 「배틀 에어리어에 놓는다」로 놓인 옵션)
    OpponentDigimonDigivolved,      // 상대의 디지몬이 진화했을 때 (Other = 진화한 상대 디지몬, v0.31.0)
    AllyTamerDeleted,               // 자신의 테이머가 소멸했을 때 (TriggerCard = 그 테이머, v0.31.0)
    OpponentTamerDeleted,           // 상대의 테이머가 소멸했을 때 (TriggerCard)
    AllyTamerUnsuspended,           // 자신의 테이머가 액티브 상태가 되었을 때 (TriggerCard = 그 테이머 — 액티브 페이즈 · 효과 모두)
    UnderTamersTrashed,             // 효과로 자신의 테이머 아래 카드가 파기되었을 때 (TriggerCard = 파기된 카드 중 1장)
    AllyLinkCardsTrashed,           // 효과로 자신의 디지몬의 링크 카드가 파기되었을 때 (Other = 그 디지몬)
    OpponentDeckTrashed,            // 상대의 덱에서 카드가 파기되었을 때 (TriggerCard)
    Counter,                        // 【카운터】 (종합 룰 11-3): 상대의 디지몬이 어택했을 때 어택받는 쪽이 어택 1번에 1개 발휘할 수 있다 (Other = 어택한 디지몬)
    // v0.33.0
    AllyTamerWouldLeave,            // 자신의 테이머가 배틀 에어리어를 떠나려 할 때 (Other = 그 테이머만 든 가짜 디지몬 — GoldfishEngine.CurrentLeave.IsTamer, PreventLeave로 막는다)
    AllyTamerReturnedToHand,        // 자신의 테이머가 패로 되돌아갔을 때 (TriggerCard = 그 테이머)
    AllyDigimonLeft,                // 자신의 디지몬이 배틀 에어리어를 떠났을 때 (소멸 · 패 · 덱 · 시큐리티 · 진화원으로, Other = 그 디지몬)
    OpponentDigimonLeft,            // 상대의 디지몬이 배틀 에어리어를 떠났을 때 (Other)
    DigimonReturnedToHand,          // 어느 쪽이든 디지몬이 패로 되돌아갔을 때 (Other = 그 디지몬)
    AllyCheckedSecurity,            // 자신의 디지몬이 시큐리티를 1장 체크했을 때 (Other = 체크한 디지몬, TriggerCard = 그 카드)
    OpponentSecurityAdded,          // 상대의 시큐리티에 카드가 놓였을 때 (TriggerCard)
    OpponentSourcesAdded,           // 효과로 상대의 디지몬의 진화원에 카드가 놓였을 때 (Other = 그 디지몬)
    AllyTrashToHand,                // 자신의 트래시에서 카드가 효과로 패에 되돌아갔을 때 (GoldfishEngine.CurrentMovedCards = 그 카드)
    AllyTrashToDeck,                // 자신의 트래시에서 카드가 덱으로 되돌아갔을 때 (CurrentMovedCards)
    // v0.40.0
    ThisDigimonWouldDigivolve,      // 이 디지몬이 진화하려 할 때 (코스트를 내기 전, TriggerCard = 진화할 카드)
    AllyTopPlacedUnder,             // 효과로 자신의 디지몬의 맨 위 카드가 진화원에 놓였을 때 (Other = 그 디지몬 — 공식 Q&A EX5-001 · EX5-065, 진화는 아님)
    ThisSourcesToDeck,              // 이 디지몬의 진화원이 덱으로 되돌아갔을 때 (CurrentSourceCards = 그 카드)
    AllyUnderTamerPlaced,           // 자신의 테이머 아래에 카드가 놓였을 때 (TriggerCard = 놓인 카드 중 1장, CurrentSourceCards = 놓인 카드)
    // v0.43.0
    AllyTopStackedTrashed43,        // 자신의 디지몬에 겹쳐져 있는 카드가 위에서부터 파기되었을 때 (Other = 그 디지몬 — 맨 위 카드 파기 · ≪퇴화≫ · ≪아머 퍼지≫, BT21-094)
}

/// <summary>효과가 적힌 곳 — 맨 위 카드(메인)인지, 진화원인지</summary>
public enum EffectSource { Main, Inherited, Trash, Breeding42, BreedingInherited42, Link43 }   // Link43 = v0.43.0 링크 카드의 링크 효과 (링크되어 있는 동안 — 그 디지몬의 효과로) · Trash = 트래시의 이 카드로 발휘하는 [트래시] 시점 효과 (v0.31.0) · v0.42.0 [육성] 효과 (육성 에어리어의 디지몬 — 맨 위 카드 · 진화원)

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
    CannotDigivolve,         // (지속 효과) 이 디지몬은 진화할 수 없다 ("[All Turns] This Digimon can't digivolve")
    Engage,                  // ≪급습≫ (종합 룰 16-44): 자신의 턴 종료 시, 이 디지몬으로 어택할 수 있다
    Ascension,               // ≪천승≫ (종합 룰 16-43): 이 디지몬이 소멸했을 때, 이 카드를 시큐리티 위에 놓을 수 있다
    AttackTargetLocked,      // (지속 효과) 이 디지몬의 어택 대상은 바뀌지 않는다 ("[Your Turn] This Digimon's attack target can't change")
    // v0.34.0 제한 · 보호 (기간 효과 "~ until ~" · 지속 효과 모두 키워드처럼 얻는다)
    CannotAttackPlayers,     // 플레이어에게 어택할 수 없다 ("can't attack players")
    CannotAttackDigimon,     // 디지몬에게 어택할 수 없다 ("can't attack your opponent's Digimon")
    CanAttackUnsuspended,    // 액티브 상태의 상대 디지몬에게도 어택할 수 있다 ("can also attack your opponent's unsuspended Digimon")
    CanAttackUnsuspendedSourceless, // 진화원을 갖지 않은 액티브 상태의 상대 디지몬에게도 어택할 수 있다
    CannotBeDeletedInBattle, // 배틀로 소멸하지 않는다 ("can't be deleted in battle")
    CannotBeDeletedByOpponentEffects, // 상대의 효과로 소멸하지 않는다
    CannotBeDeletedByEffects, // 효과로 소멸하지 않는다 (자신의 효과도)
    CannotBeReturnedByOpponentEffects, // 상대의 효과로 패 · 덱으로 되돌아가지 않는다
    UnaffectedByOpponentEffects,        // 상대의 효과를 받지 않는다
    UnaffectedByOpponentDigimonEffects, // 상대의 디지몬의 효과를 받지 않는다
    UnaffectedByOpponentOptionEffects,  // 상대의 옵션 카드의 효과를 받지 않는다
    DpCannotBeReduced,       // DP가 마이너스되지 않는다 (효과 전부)
    DpCannotBeReducedByOpponent, // 상대의 효과로 DP가 마이너스되지 않는다
    CannotActivateWhenDigivolving, // 【진화 시】 효과를 발휘할 수 없다
    CannotActivateWhenAttacking,   // 【어택 시】 효과를 발휘할 수 없다
    CannotBeReturnedByEffects,     // 효과로 패 · 덱으로 되돌아가지 않는다 (자신의 효과도)
    CannotBlock,                   // (지속 효과) 블록할 수 없다 ("[All Turns] This Digimon can't attack or block")
    OpponentMemoryTamerOnly,       // (지속 효과) 상대는 테이머의 효과 이외로 메모리를 얻을 수 없다
    PlayersMemoryTamerOnly,        // v0.35.0 (지속 효과) 플레이어는(양쪽 모두) 테이머의 효과 이외로 메모리를 얻을 수 없다
    // v0.36.0 플레이어 규칙 제한 (지속 효과 — 이 디지몬이 있는 동안)
    PlayersCannotPlayDigimonByEffects, // 서로는 효과로 디지몬을 등장시킬 수 없다 ("Players can't play Digimon by effects")
    PlayersCannotReducePlayCosts,      // 서로는 지불하는 등장 코스트를 마이너스할 수 없다 ("Players can't reduce play costs")
    OpponentCannotUseOptions,          // 상대는 옵션 카드를 사용할 수 없다 ("your opponent can't use Option cards")
    TamerAsDigimonAura,                // (지속 효과) 이 카드의 TamerAuras가 켜져 있다 ("All of your [Marcus Damon]s are also treated as ~ Digimon")
    UnaffectedByOpponentDeDigivolve,   // 상대의 ≪퇴화≫ 효과를 받지 않는다 ("their ＜De-Digivolve＞ effects don't affect this Digimon")
    OpponentTamersCannotUnsuspend,     // 상대의 테이머 전부는 액티브가 되지 않는다 ("None of your opponent's Tamers can unsuspend")
    // v0.37.0
    CannotSuspend,                     // 레스트할 수 없다 (지속 효과 — "none of your opponent's Digimon can suspend" EX8-026, 어택도 할 수 없다)
    CannotActivateOnPlay,              // 【등장 시】 효과를 발휘할 수 없다 ("none of your opponent's Digimon can activate [On Play] effects" EX8-029)
    // v0.38.0 얻는 ≪가드≫ · ≪디코이 (색)≫ — 카드에 적힌 것은 ParseLeaveKeyword가 효과로 등록한다
    Guard,                             // ≪가드≫: 다른 자신의 디지몬이 상대의 효과로 떠날 때, 이 디지몬을 소멸시키는 것으로 떠나지 않는다
    DecoyRed, DecoyBlue, DecoyYellow, DecoyGreen, DecoyBlack, DecoyPurple, DecoyWhite,   // ≪디코이 (색)≫: 그 색의 다른 자신의 디지몬이 상대의 효과로 소멸할 때
    // v0.42.0
    StackProtectedFromOpponent,        // 겹쳐진 카드(맨 위 카드 · 진화원)가 상대의 효과로 파기되지 않는다 (공식 BT21-060 "이 디지몬에 겹쳐져 있는 카드는 상대의 효과로 파기되지 않는다" — ≪퇴화≫도)
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
    // v0.37.0
    DeleteCapSelf,      // 이 디지몬의 DP 소멸 효과의 상한 +N ("add 2000 to this Digimon's DP-based deletion effects' maximums" BT17-008)
    DeleteCapAll,       // 자신이 발휘하는 DP 소멸 효과의 상한 +N ("Add 1000 to the maximum DP you can choose with DP-based deletion effects" BT12-001)
    ExtraColors,        // 이 디지몬의 색에 더한다 (값 = 색 비트 1<<CardColor — "This Digimon is also treated as yellow")
    EnemyKeyword,       // 상대의 디지몬 전부(Subject 조건)가 키워드를 얻는다 (StaticEffect.Keyword — "none of your opponent's Digimon can suspend")
    OpponentPlayDpLock, // 상대는 DP N 이하의 디지몬을 등장시킬 수 없다 (값 = N, 0이면 없음 — BT14-017)
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

    /// <summary>v0.34.0 "All of your other ~" — 효과를 가진 디지몬 자신은 받지 않는다</summary>
    public bool ExcludeSource { get; init; }

    /// <summary>v0.40.0 효과를 가진 디지몬(StaticContext.Digimon)을 보는 받는 디지몬 조건 — "All of your other Digimon with the same name as this Digimon" (P-045 · BT5-063 · EX1-051)</summary>
    public Func<StaticContext, CardInstance, bool>? SubjectOf { get; init; }

    /// <summary>v0.40.0 효과를 받는 디지몬인지 (Subject · ExcludeSource · SubjectOf) — ctx = 효과를 가진 쪽 기준</summary>
    public bool AppliesTo(StaticContext ctx, CardInstance d) =>
        (Subject == null || Subject(d)) && !(ExcludeSource && ReferenceEquals(d, ctx.Digimon)) && (SubjectOf == null || SubjectOf(ctx, d));
}

/// <summary>지속 효과 계산에 필요한 정보</summary>
public sealed class StaticContext
{
    /// <summary>v0.37.0 메모리 조건 ("While you have 0 or less memory") — 효과 주인 기준 메모리를 엔진에서 읽는다 (없으면 0)</summary>
    public GoldfishEngine? Engine { get; init; }

    /// <summary>효과 주인의 메모리 (주인 쪽이 +)</summary>
    public int OwnerMemory => Engine?.MemoryOf(Owner) ?? 0;

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

    /// <summary>
    /// v0.30.0 "You may digivolve this card from your hand onto one of your red Tamers as if the Tamer is a level 3 red Digimon (for a memory cost of 2)" —
    /// 패의 이 카드로 자신의 테이머를 Lv.N 색 디지몬으로 취급해 진화 (테이머 조건, 레벨, 색, 코스트 — null이면 이 카드의 진화 코스트)
    /// </summary>
    public List<(Func<CardData, bool> Tamer, int Level, CardColor Color, int? Cost)> TamerEvolutions { get; } = new();

    public StartOfTurnMemoryEffect? StartOfTurnMemory { get; set; }

    /// <summary>≪오버클럭≫의 지정 (토큰 외에 소멸시킬 수 있는 다른 자신의 디지몬 — 맨 위 카드 기준). 없으면 null</summary>
    public Func<CardData, bool>? OverclockSpecified { get; set; }

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

    /// <summary>v0.38.0 "[Security] [End of Opponent's Turn] Play this card without paying the cost" — 시큐리티에 앞면으로 있는 동안 상대의 턴 종료 시 이 카드를 등장 (종합 룰 15-14-5-1)</summary>
    public bool SecurityPlayAtEndOfOpponentTurn38 { get; set; }

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

    /// <summary>v0.34.0 "This Digimon can only digivolve into white Digimon" — (출처, 자신의 턴에만, 진화할 수 있는 카드)</summary>
    public List<(EffectSource Source, bool YourTurnOnly, Func<CardData, bool> Allowed)> EvoOnlyInto { get; } = new();

    /// <summary>v0.36.0 테이머를 디지몬으로도 취급하는 지속 효과 (BT25-104)</summary>
    public List<TamerAura> TamerAuras { get; } = new();

    /// <summary>"When this Digimon would digivolve (into ~), reduce the digivolution cost by N" — 진화하기 직전에 진화 코스트를 바꾸는 효과</summary>
    public List<EvoCostModifier> EvoCostModifiers { get; } = new();

    /// <summary>v0.40.0 상대의 디지몬이 진화할 때 진화 코스트 +N ("[Opponent's Turn] When an opponent's Digimon with no digivolution cards would digivolve, increase the digivolution cost by 1" EX3-016 · EX3-019)</summary>
    public List<(EffectSource Source, Func<CardInstance, bool> Who, int Amount, bool OpponentsTurnOnly)> OpponentEvoCostIncreases40 { get; } = new();

    /// <summary>v0.41.0 이 디지몬에 카드를 링크할 때의 링크 코스트 감소 ("[Your Turn] [Once Per Turn] When a [Social], [Tool] or [Game] trait card would link to this Digimon, you may reduce the cost by 1" BT25-004 · BT25-045)</summary>
    public List<LinkCostModifier41> LinkCostModifiers41 { get; } = new();

    /// <summary>v0.41.0 ≪계승 (지정)≫ — 이 디지몬은 가장 위의 지정된 진화원 카드의 ≪계승≫ 이외의 효과를 모두 얻는다 (BT26-032)</summary>
    public List<Func<CardData, bool>> SuccessionSpecs41 { get; } = new();

    /// <summary>v0.42.0 조건이 붙은 진화 조건 무시 진화 — 진화할 카드 쪽 ("While ~, your [Impmon] can digivolve into this card ~ for a memory cost of 4" BT2-111) (조건, 진화하는 디지몬, 코스트)</summary>
    public List<(Func<StaticContext, bool>? Cond, Func<CardInstance, bool> From, int Cost)> SpecialEvolutions42 { get; } = new();

    /// <summary>v0.42.0 진화 조건 무시 진화 — 진화하는 디지몬 쪽 ("[Your Turn] If ~, this Digimon can digivolve into a [Gallantmon] in your hand for a memory cost of 4" ST7-03) (적힌 곳, [자신의 턴], 조건, 진화할 카드, 코스트)</summary>
    public List<(EffectSource Source, bool YourTurnOnly, Func<StaticContext, bool>? Cond, Func<CardData, bool> Into, int Cost)> HolderEvolutions42 { get; } = new();

    /// <summary>v0.43.0 링크 카드의 링크 효과 중 키워드 (링크되어 있는 동안 그 디지몬이 얻는다 — 종합 룰 2-3-11-4-6 「링크 효과」)</summary>
    public HashSet<Keyword> LinkKeywords43 { get; } = new();

    /// <summary>v0.43.0 링크 효과의 ≪시큐리티 어택 ±N≫ (링크되어 있는 동안)</summary>
    public int LinkSecurityAttack43 { get; set; }

    /// <summary>v0.43.0 [패]【카운터】≪블래스트 조그레스 (A + B)≫ — 지정된 이름 2개 (영어). 없으면 null (종합 룰 16-31)</summary>
    public (string A, string B)? BlastDna43 { get; set; }




    /// <summary>v0.31.0 "[Your Turn] When any Digimon cards with the [ADVENTURE] trait would be played from your hand, by suspending this Tamer, reduce the play cost by 1" — 다른 카드의 등장 코스트 감소 (필드 · 육성)</summary>
    public List<FieldPlayCostModifier> FieldPlayCostModifiers { get; } = new();

    /// <summary>
    /// v0.31.0 "[All Turns] This Digimon gains all effects of cards with [Gammamon] in their names in this Digimon's digivolution cards" —
    /// (적힌 곳, 진화원 카드 조건). 조건에 맞는 진화원 카드의 메인 효과(시점 효과 · 지속 효과 · 키워드 · [메인])를 이 디지몬이 얻는다
    /// </summary>
    public List<(EffectSource Source, Func<CardData, bool> Which)> GainedSourceEffects { get; } = new();

    /// <summary>v0.37.0 "This Digimon is also treated as Lv.6 [Slayerdramon] for [Examon]'s DNA digivolution" — (DNA 진화할 카드 이름, 취급 이름, 취급 Lv.) — 배틀 에어리어에 있을 때만 (Q&A BT20-025)</summary>
    public List<(string ForName, string AsName, int Level)> DnaAliases { get; } = new();
}

/// <summary>
/// v0.31.0 필드의 카드가 패에서 등장하는 다른 카드의 등장 코스트를 줄이는 효과. Source = 적힌 곳 (진화원 효과면 그 카드가 진화원에 있을 때),
/// Breeding = [육성] (육성 에어리어에 있을 때만, 아니면 배틀 에어리어 · 테이머). Which = 등장하는 카드 조건, Cost = 비용 (낼지 묻는다),
/// Optional = "you may reduce" (쓸지 묻는다). 공식 Q&A ST20-12 · ST21-12: 여러 장의 효과는 더한다
/// </summary>
public sealed record FieldPlayCostModifier(
    EffectSource Source,
    Func<CardData, bool> Which,
    int Reduce,
    PlayCostOption? Cost,
    bool Optional,
    bool OncePerTurn,
    bool YourTurnOnly,
    bool Breeding)
{
    /// <summary>"~ by 1 for each of this Digimon's digivolution cards" — Reduce × 셈 (효과 상황)</summary>
    public Func<EffectContext, int>? Times { get; init; }

    /// <summary>v0.32.0 줄이는 양을 직접 정한다 (Reduce · Times 대신) — "~ -4. 이 디지몬의 진화원 1장마다 이 코스트 경감 효과의 수치 -1" (BT13-007) · "~ 대신 -4" (EX6-006)</summary>
    public Func<EffectContext, int>? Amount { get; init; }

    /// <summary>v0.42.0 비용을 낸 뒤 줄이는 양 (Amount는 미리보기) — "reduce the play cost by the play cost of the deleted Digimon" (BT13-103)</summary>
    public Func<EffectContext, int>? PaidAmount42 { get; init; }
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
    bool YourTurnOnly)
{
    /// <summary>v0.31.0 "~ by 1 for each ~": Reduce × 셈 (효과 상황, 진화하는 디지몬). 없으면 Reduce 그대로</summary>
    public Func<EffectContext, CardInstance, int>? Times { get; init; }

    /// <summary>v0.40.0 정해진 코스트로 만드는 변화량 (원래 진화 코스트 - 정해진 코스트, 음수면 늘린다 — "the memory cost of the digivolution is equal to the number of cards in your security stack" BT7-040)</summary>
    public Func<EffectContext, CardInstance, int>? Delta40 { get; init; }

    /// <summary>지금 줄이는 양</summary>
    public int ReduceFor(EffectContext ctx, CardInstance target) => Delta40 != null ? Delta40(ctx, target) : Times == null ? Reduce : Reduce * Math.Max(0, Times(ctx, target));
}

/// <summary>등장할 때 고르는 코스트 감소: 비용(낼 수 있는지 · 내기 · 추천 · 설명)을 내면 Reduce만큼 줄인다. CostText = 원문 비용 구절 (예: "deleting 1 of your Digimon" — AI가 비용 종류를 가늠)</summary>
public sealed record PlayCostOption(int Reduce, Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe, string CostText = "")
{
    /// <summary>v0.34.0 "reduce the play cost by 4 for each one" — 낸 매수(EffectContext.PaidCount)만큼 곱한다. 미리보기는 PreviewCount</summary>
    public Func<EffectContext, int>? PreviewCount { get; init; }
    public bool PerCount { get; init; }
}

/// <summary>v0.41.0 링크 코스트 감소 하나: Source = 적힌 곳 (진화원 효과면 그 카드가 진화원에 있을 때), Which = 링크하는 카드 조건, Key = 같은 효과를 두 번 등록하지 않게 (영어 데이터의 중복 문장)</summary>
public sealed record LinkCostModifier41(EffectSource Source, Func<CardData, bool> Which, int Reduce, bool OncePerTurn, bool YourTurnOnly, bool Optional, string Key);
