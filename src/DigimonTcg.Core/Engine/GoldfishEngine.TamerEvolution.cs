using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.30.0 테이머에서 진화 (하이브리드체): "You may digivolve this card from your hand onto one of your red Tamers as if the Tamer is a level 3 red Digimon
/// (for a memory cost of 2)" (BT4 · BT6 · BT7 · BT12 · BT17, 28문장).
/// 공식 Q&A BT4-011 · BT7-011: 테이머를 디지몬으로도 취급하여 진화한다 — "디지몬이 진화할 때 · 진화했을 때" 효과가 유발되고, "진화할 수 없다"면 진화할 수 없다.
/// 종합 룰 3-4-5: 에어리어의 카드 위에 카드가 겹쳐지면 그 존재가 변화한 것 (새로 등장한 것이 아니다) — 테이머가 이번 턴에 나온 것이 아니면 어택할 수 있다.
/// 종합 룰 4-3-2: 디지몬 아래에 겹쳐진 카드는 진화원 — 테이머 아래의 카드는 그 디지몬의 진화원이 된다 (테이머 아래 카드는 테이머마다 나누어 두지 않으므로, 테이머가 1명일 때만).
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>테이머가 필드에 나온 턴 (TurnSequence) — 같은 카드 객체가 여러 장일 수 있어 장마다 적는다</summary>
    private readonly List<(PlayerZones Zones, CardData Tamer, int Sequence)> _tamerEntries = new();

    /// <summary>테이머를 필드에 놓는다 (등장 · 효과 · [시큐리티]) — 나온 턴을 적어 둔다</summary>
    internal void AddTamer(PlayerZones zones, CardData tamer)
    {
        zones.Tamers.Add(tamer);
        _tamerEntries.Add((zones, tamer, State.TurnSequence));
    }

    /// <summary>필드의 이 테이머(같은 카드 중 가장 먼저 나온 것)가 이번 턴에 나왔는지</summary>
    private bool TamerEnteredThisTurn(PlayerZones zones, CardData tamer)
    {
        int copies = zones.Tamers.Count(t => ReferenceEquals(t, tamer));
        int thisTurn = _tamerEntries.Count(e => ReferenceEquals(e.Zones, zones) && ReferenceEquals(e.Tamer, tamer) && e.Sequence == State.TurnSequence);
        return thisTurn >= copies;
    }

    /// <summary>플레이어: 패의 evoCard로 자신의 테이머 tamer를 진화시킬 수 있는지 (메모리는 일반 진화처럼 상대 쪽으로 넘어가도 된다)</summary>
    public ActionResult ValidateTamerEvolution(CardData tamer, CardData evoCard)
    {
        if (State.IsGameOver) return ActionResult.Fail("이미 종료된 게임입니다.");
        if (State.CurrentPhase != Phase.Main) return ActionResult.Fail("메인 페이즈에만 진화할 수 있습니다.");
        if (State.BlitzAttacker != null && State.MemoryGauge < 0) return ActionResult.Fail("지금은 ≪진격≫ 어택이나 턴 종료만 할 수 있습니다.");
        if (!State.Player.Tamers.Contains(tamer)) return ActionResult.Fail("필드에 없는 테이머입니다.");
        if (!State.Player.Hand.Contains(evoCard)) return ActionResult.Fail("패에 없는 카드입니다.");
        if (CardScripts.TamerEvolutionCost(evoCard, tamer) is not int cost)
            return ActionResult.Fail($"[{evoCard.Name}]은(는) [{tamer.Name}]을(를) 디지몬으로 취급하여 진화할 수 없습니다.");
        return ActionResult.Success($"테이머 [{tamer.Name}]을(를) Lv.{evoCard.EvoRequirementLevel} 디지몬으로 취급하여 진화 (진화 코스트 {cost}). 진화 성공 시 카드 1장을 추가로 드로우합니다.");
    }

    /// <summary>플레이어: 패의 evoCard로 자신의 테이머를 진화시킨다 (테이머를 Lv.3 디지몬으로 취급)</summary>
    public ActionResult EvolveTamer(CardData tamer, CardData evoCard)
    {
        var validation = ValidateTamerEvolution(tamer, evoCard);
        if (!validation.CanExecute)
            return validation;
        State.CanMulligan = false;
        int cost = CardScripts.TamerEvolutionCost(evoCard, tamer)!.Value;
        var inst = EvolveTamerCore(State.Player, tamer, evoCard, cost, out int paid);
        State.TotalMemorySpent += paid;
        State.TotalEvolutionsCount++;
        Log($"테이머에서 진화: [{tamer.Name}] → [{evoCard.Name}(Lv.{evoCard.Level})] (코스트 {paid} 지불)");
        if (State.Player.BattleArea.Contains(inst))
        {
            ApplyWhenDigivolvingEffects(State.Player, inst, evoCard);
            FireForSide(EffectTrigger.AllyDigimonDigivolved, State.Player, inst);
        }
        CheckTurnEndAndAutoReset();
        return ActionResult.Success(validation.GuideMessage);
    }

    /// <summary>테이머 → 디지몬 (진화원 맨 아래가 테이머) + 진화 · 메모리 · 드로우. [진화 시]는 부른 쪽이 발휘한다</summary>
    private CardInstance EvolveTamerCore(PlayerZones owner, CardData tamer, CardData evoCard, int cost, out int paid)
    {
        bool enteredThisTurn = TamerEnteredThisTurn(owner, tamer);
        bool suspended = owner.SuspendedTamers.Remove(tamer);
        owner.Tamers.Remove(tamer);
        int entry = _tamerEntries.FindIndex(e => ReferenceEquals(e.Zones, owner) && ReferenceEquals(e.Tamer, tamer) && (enteredThisTurn || e.Sequence != State.TurnSequence));
        if (entry >= 0) _tamerEntries.RemoveAt(entry);
        var inst = new CardInstance(tamer, enteredThisTurn ? State.CurrentTurn : State.CurrentTurn - 1) { IsSuspended = suspended, IsOwnersTurn = IsTurnOf(owner) };
        // 테이머 아래의 카드 → 진화원 (테이머가 이 1명뿐일 때만 — 누구 아래인지 나누어 두지 않는다)
        if (owner.Tamers.Count == 0)
        {
            foreach (var under in owner.UnderTamers) inst.AddBottomSource(under);
            owner.UnderTamers.Clear();
        }
        owner.BattleArea.Add(inst);
        owner.Hand.Remove(evoCard);
        paid = ApplyEvoCostModifiers(owner, inst, evoCard, cost);
        inst.Evolve(evoCard);
        AddMemory(owner, -paid);
        DrawFor(owner);
        RecalculateFieldBuffs();
        return inst;
    }

    /// <summary>
    /// AI: 어택 전에 메모리 안에서 테이머를 진화시킬 수 있으면 한다 (하이브리드체는 테이머에서 진화하라고 만든 카드 — 진화한 디지몬은 테이머가 이번 턴에 나온 것이 아니면 바로 어택한다)
    /// </summary>
    private bool AiTryTamerEvolution(AiTurnContext ctx)
    {
        var opp = State.DummyOpponent;
        foreach (var card in opp.Hand.Where(c => c.Type == CardType.Digimon && !c.IsDual).Distinct().OrderByDescending(c => c.Level).ToList())
            foreach (var tamer in opp.Tamers.Distinct().ToList())
            {
                if (CardScripts.TamerEvolutionCost(card, tamer) is not int cost || cost > ctx.Memory)
                    continue;
                int before = ctx.Memory;
                var inst = EvolveTamerCore(opp, tamer, card, cost, out int paid);   // AddMemory가 AI 턴 메모리(ctx)를 줄인다
                Log($"🤖 [AI 테이머에서 진화] [{tamer.Name}] -> [{card.Name}(Lv.{card.Level}, 코스트 {paid})] (AI 메모리: +{before} → {ctx.Memory})");
                AddOpponentEvent(OpponentActionKind.Evolve, "테이머에서 진화", $"{tamer.Name} → {card.Name} (Lv.{card.Level}) · 메모리 {before} → {ctx.Memory}", card);
                if (opp.BattleArea.Contains(inst))
                    AddEffectEvent(card, CaptureEffectLogs(() => { ApplyWhenDigivolvingEffects(opp, inst, card); FireForSide(EffectTrigger.AllyDigimonDigivolved, opp, inst); }));
                return true;
            }
        return false;
    }
}
