using System.Linq;
using System;
using System.Collections.Generic;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// 효과 하나를 처리할 때 넘겨받는 정보와 공용 동작.
/// Owner / Enemy는 효과 주인 기준이므로 같은 스크립트가 플레이어 · AI 어느 쪽에서도 그대로 동작한다.
/// </summary>
public sealed class EffectContext
{
    internal EffectContext(GoldfishEngine engine, PlayerZones owner, CardInstance? self, CardInstance? other, CardInstance? chosenTarget)
    {
        Engine = engine;
        Owner = owner;
        Enemy = engine.EnemyOf(owner);
        Self = self;
        Other = other;
        ChosenTarget = chosenTarget;
    }

    internal GoldfishEngine Engine { get; }
    public GameState State => Engine.State;

    /// <summary>효과 주인의 필드</summary>
    public PlayerZones Owner { get; }
    public PlayerZones Enemy { get; }
    public bool OwnerIsPlayer => ReferenceEquals(Owner, State.Player);

    /// <summary>지금 처리 중인 효과가 적힌 카드 (메인 효과면 맨 위 카드 / 옵션, 진화원 효과면 그 진화원)</summary>
    public CardData Card { get; internal set; } = null!;

    /// <summary>효과를 가진 디지몬 (옵션 · 테이머 효과면 null)</summary>
    public CardInstance? Self { get; }

    /// <summary>
    /// 시점별 상대 카드: [어택 시] = 어택 대상 디지몬(시큐리티 어택이면 null),
    /// [시큐리티] = 어택 중인 상대 디지몬, 같은 명칭 등장 = 등장한 디지몬
    /// </summary>
    public CardInstance? Other { get; }

    /// <summary>플레이어가 UI에서 고른 대상 (옵션 [메인])</summary>
    public CardInstance? ChosenTarget { get; }

    /// <summary>시점을 일으킨 카드 (예: 레스트한 테이머 — AllyTamerSuspended)</summary>
    public CardData? TriggerCard { get; internal set; }

    /// <summary>지금 처리 중인 시점 (v0.31.0 — "A or B" 사건 문장이 어느 쪽 사건인지 본다)</summary>
    public EffectTrigger Trigger { get; internal set; }

    /// <summary>GameState.TurnSequence — "다음 상대의 턴 종료 시까지" = TurnSequence + 1</summary>
    public int TurnSequence => State.TurnSequence;

    // ---- 처리 결과 (엔진이 로그 · 결과 메시지에 쓴다) ----

    /// <summary>[등장 시] 요약 / 옵션 [메인] 결과 메시지 / [시큐리티] 상세</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>[시큐리티] 결과 한 줄 요약 (시큐리티 체크 기록)</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>[시큐리티] 효과로 어택 중인 디지몬이 필드를 떠났는지</summary>
    public bool AttackerRemoved { get; set; }

    /// <summary>[시큐리티] 효과를 발휘한 이 카드를 트래시 대신 패에 넣는지</summary>
    public bool ReturnToHand { get; set; }

    /// <summary>시큐리티 디지몬: 배틀이 끝난 뒤 트래시 대신 코스트 없이 등장 ("At the end of the battle, play this card")</summary>
    public bool PlayAfterBattle { get; set; }

    /// <summary>앞 문장에서 고른 대상 ("that Digimon" · "it"이 가리키는 디지몬)</summary>
    public List<CardInstance> LastTargets { get; set; } = new();

    /// <summary>v0.34.0 앞의 비용 · 동작이 옮긴 매수 ("for each card trashed / placed / returned" · "reduce the play cost by 4 for each one")</summary>
    public int PaidCount { get; set; }

    /// <summary>v0.34.0 "Your opponent may delete ~"의 결과 ("If this effect didn't delete") — 읽으면 지운다</summary>
    internal bool? LastDidDelete34 { get; set; }

    /// <summary>v0.34.0 "Choose 1 of your Digimon"으로 고른 디지몬 ("the chosen Digimon")</summary>
    internal CardInstance? Chosen34 { get; set; }

    /// <summary>앞 문장이 옮긴 카드 ("If it's a yellow card, ~" — 시큐리티에서 패에 넣은 카드 등)</summary>
    public CardData? LastCard { get; set; }

    /// <summary>지금 처리 중인 효과(절)가 직접 등장시킨 디지몬 — "the Digimon this effect played" (v0.30.0)</summary>
    public List<CardInstance> PlayedThisEffect { get; internal set; } = new();

    /// <summary>비용을 내다가 이루지 못했다 ("by deleting 1 of your opponent's ~"의 소멸이 막힘) — 뒤의 효과를 처리하지 않는다 (v0.30.0)</summary>
    public bool CostFailed { get; set; }

    /// <summary>앞 문장이 실제로 처리되었는지 (조건 불충족 · "~할 수 있다" 거절 · 비용 미지불이면 false) — "If you do, ~"</summary>
    public bool LastStepResolved { get; set; } = true;

    /// <summary>옵션: "Then, place this card in your battle area" — 트래시 대신 배틀 에어리어에 놓는다 (≪딜레이≫ 옵션)</summary>
    public bool PlaceInBattleArea { get; set; }

    /// <summary>"~ would leave the battle area / be deleted" 효과가 떠나는 것 · 소멸을 막았는지 ("prevent it from leaving" · "it isn't deleted")</summary>
    public bool PreventLeave { get; set; }

    /// <summary>옵션: "link this card to ~" — 이 카드가 링크 카드가 되었으므로 사용 뒤 트래시로 보내지 않는다</summary>
    public bool LinkedThisCard { get; set; }

    /// <summary>옵션: "place this card face up as the top/bottom security card" — 사용 뒤 트래시 대신 시큐리티에 앞면으로 (true = 맨 위)</summary>
    public bool? ThisCardToSecurityTop { get; set; }

    /// <summary>문장 파서의 문장을 하나라도 처리하려 했는지 · 그중 하나라도 실제로 처리했는지 — [메인] 기동 효과가 아무것도 하지 않았으면 사용으로 치지 않는다</summary>
    public bool StepsRan { get; set; }

    /// <summary>효과의 발동 조건("If ~" · 비용 지불 등)을 넘어 효과 본문을 하나라도 처리했는지 — 아니면 [메인] 기동 효과를 쓴 것으로 치지 않는다</summary>
    public bool AnyEffectRan { get; set; }
    public bool AnyStepResolved { get; set; }

    /// <summary>[시큐리티] 결과를 한 번에 정한다</summary>
    public void SecurityResult(string detail, string summary, bool attackerRemoved = false, bool returnToHand = false)
    {
        Message = detail;
        Summary = summary;
        AttackerRemoved = attackerRemoved;
        ReturnToHand = returnToHand;
    }

    // ---- 공용 동작 ----

    public void Log(string message) => Engine.EffectLog(Owner, message);

    /// <summary>효과 주인 기준 메모리 (AI 턴에는 AI 메모리)</summary>
    public int OwnerMemory => Engine.MemoryOf(Owner);

    public void AddMemory(int amount) => Engine.AddMemory(Owner, amount);

    /// <summary>[이 턴 종료 시] 메모리 -N 예약</summary>
    public void LoseMemoryAtEndOfTurn(int amount) => Engine.AddEndOfTurnMemoryLoss(Owner, amount);

    public CardData? Draw() => Engine.DrawFor(Owner);

    /// <summary>덱 위에서 count장을 오픈해 조건에 맞는 카드를 최대 maxTake장 패에 넣고, 나머지는 덱 아래로</summary>
    public List<CardData> RevealToHand(int count, Func<CardData, bool> match, int maxTake) =>
        Engine.RevealAndTake(Owner, count, match, maxTake);

    /// <summary>≪리커버리 +N (덱)≫</summary>
    public int Recovery(int count) => Engine.Recovery(Owner, count);

    /// <summary>자신의 덱 위에서 N장 파기</summary>
    public void Mill(int count, string reason) => Engine.Mill(Owner, count, reason);

    /// <summary>자신의 트래시에서 조건에 맞는 디지몬을 코스트 없이 등장 ([등장 시] 효과는 발휘하지 않음)</summary>
    public List<CardData> PlayFromTrash(Func<CardData, bool> match, int maxCount) =>
        Engine.PlayFromTrash(Owner, match, maxCount);

    /// <summary>카드를 코스트 없이 등장시킨다 ([등장 시] 효과는 발휘하지 않음)</summary>
    public CardInstance PlayWithoutCost(CardData card)
    {
        var inst = new CardInstance(card, State.CurrentTurn);
        Owner.BattleArea.Add(inst);
        return inst;
    }

    /// <summary>상대 디지몬을 효과로 소멸시킨다 (≪회피≫로 막으면 false)</summary>
    public bool DeleteEnemy(CardInstance target) => Engine.DeleteByOpponentEffect(Enemy, target);
    /// <summary>자신의 디지몬을 소멸시킨다 (코스트 — ≪회피≫ 등으로 막을 수 없음)</summary>
    public void DeleteOwn(CardInstance target) => Engine.DeleteFrom(Owner, target, GoldfishEngine.DeletionCause.Cost);

    /// <summary>상대 디지몬을 패로 되돌린다 (진화원은 트래시)</summary>
    public void BounceEnemy(CardInstance target) => Engine.ReturnToHand(Enemy, target);

    /// <summary>DP -N (이 턴 동안) — DP가 0이 되면 소멸. 반환: 소멸 여부</summary>
    public bool DpMinus(CardInstance target, int amount, string source) => Engine.ApplyDpMinus(target, amount, source);

    /// <summary>상대 디지몬 ≪퇴화 N≫ — 버린 매수</summary>
    public int DeDigivolveEnemy(CardInstance target, int count) => Engine.DeDigivolve(Enemy, target, count);

    /// <summary>
    /// 상대 디지몬 1마리를 대상으로 고른다: preferred(UI가 고른 대상 · 어택 상대)가 조건에 맞으면 그것,
    /// 아니면 플레이어의 효과는 플레이어에게 묻고(추천 = AI 평가), AI의 효과는 AI 평가가 가장 높은 것.
    /// </summary>
    public CardInstance? PickEnemy(Func<CardInstance, bool>? match = null, CardInstance? preferred = null) =>
        Pick(Enemy, match, preferred, "상대 디지몬");

    public CardInstance? PickOwn(Func<CardInstance, bool>? match = null, CardInstance? preferred = null) =>
        Pick(Owner, match, preferred, "자신의 디지몬");

    private CardInstance? Pick(PlayerZones zone, Func<CardInstance, bool>? match, CardInstance? preferred, string what)
    {
        var ranked = AiEvaluator.Rank(zone.BattleArea.Where(d => match == null || match(d)), preferred);
        return Engine.ChooseTargets(Owner, ranked, 1, $"[{Card?.Name}] 효과의 대상({what} 1마리)을 고르세요.", optional: false, preselected: preferred).FirstOrDefault();
    }

    /// <summary>상대 디지몬 1마리의 진화원을 아래에서 1장 파기 (가장 레벨이 높은 대상)</summary>
    public void TrashBottomSourceOfEnemy(Func<CardInstance, bool> match, string source) =>
        Engine.TrashBottomSourceOfEnemy(Enemy, match, source);

    public void RecalculateFieldBuffs() => Engine.RecalculateFieldBuffs();
}
