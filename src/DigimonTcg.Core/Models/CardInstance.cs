using System;
using System.Collections.Generic;
using System.Linq;

namespace DigimonTcg.Core.Models;

public class CardInstance
{
    public string InstanceId { get; set; }
    public CardData TopCard { get; private set; }
    public Stack<CardData> EvolutionSources { get; private set; } = new Stack<CardData>();

    public bool IsSuspended { get; set; }          // Rest 상태 여부
    public int TurnPlayed { get; set; }            // 등장한 턴 (소환 멀미 체크용)
    public int TemporaryDpBonus { get; set; }      // 이번 턴 스킬/옵션 임시 DP 버프
    public int TemporarySecurityAttackBonus { get; set; } // 이번 턴 스킬/옵션 임시 시큐리티 어택 버프
    public int AttackDpBonus { get; set; }         // "이 어택 동안" DP (≪연계≫) — 어택이 끝나면 0
    public int AttackSecurityAttackBonus { get; set; } // "이 어택 동안" 시큐리티 어택 (≪연계≫)
    public int TamerDpBonus { get; set; }          // 필드 테이머 지속 DP 버프
    public int TamerSecurityAttackBonus { get; set; } // 필드 테이머 지속 시큐리티 어택 버프
    public int EffectDpBonus { get; set; }         // 카드 지속 효과(진화원 조건부 DP 등) — 엔진이 매번 다시 계산
    public int EffectSecurityAttackBonus { get; set; } // 카드 지속 효과 시큐리티 어택 — 엔진이 매번 다시 계산
    public bool HasUsedOncePerTurnEffect { get; set; }    // 턴당 1회 효과(메탈가루몬 재행동 등) 사용 여부
    public List<string> ActiveSkillBuffLabels { get; } = new(); // 이번 턴 적용된 스킬/옵션 버프 이름 목록

    /// <summary>지금이 이 디지몬 주인의 턴인지 — [자신의 턴] 진화원 DP 보너스는 주인의 턴에만 적용된다 (엔진이 설정)</summary>
    public bool IsOwnersTurn { get; set; } = true;

    // 기간 효과: 값은 GameState.TurnSequence(플레이어 · 상대 턴마다 1씩 증가) 기준 "이 턴 종료 시까지"
    public List<(int Dp, int Until)> TimedDpBuffs { get; } = new();             // DP ± (엔진이 EffectDpBonus에 더한다)
    public List<(int Count, int Until)> TimedSecurityAttackBuffs { get; } = new(); // ≪시큐리티 어택 ±N≫
    public List<(DigimonTcg.Core.Engine.Effects.Keyword Keyword, int Until)> TimedKeywords { get; } = new(); // 얻은 키워드

    /// <summary>효과로 지금 얻고 있는 키워드 (기간 효과 + 조건부 지속 효과) — 엔진이 지속 효과를 계산할 때마다 다시 채운다</summary>
    public HashSet<DigimonTcg.Core.Engine.Effects.Keyword> ActiveKeywords { get; } = new();

    public int CannotAttackUntil { get; set; }     // 어택할 수 없음 (소로 블루 · 로제몬 등)
    public int CannotBlockUntil { get; set; }      // 블록할 수 없음
    public int SkipUnsuspendUntil { get; set; }   // 다음 액티브 페이즈에 액티브가 되지 않음 ("doesn't unsuspend during ~ next unsuspend phase")
    public int CannotDigivolveUntil { get; set; } // 진화할 수 없음 ("1 of their Digimon can't digivolve until their turn ends", v0.29.0)
    public int CannotUnsuspendUntil { get; set; } // 액티브가 될 수 없음 — 액티브 페이즈 · 효과 모두 ("can't unsuspend until the end of their turn")
    public int CannotSuspendUntil { get; set; }   // 레스트가 될 수 없음 — 어택 · 블록 · 효과로 레스트 불가 ("can't suspend until their turn ends")
    public bool AttackTargetLocked { get; set; }  // 이 어택 동안 어택 대상이 바뀌지 않음 ("This Digimon's attack target can't be switched") — 어택 종료 시 해제
    public List<CardData> LinkedCards { get; } = new();   // 링크 카드 (디지몬 옆에 가로로 끼운 카드 — 떠나면 트래시)
    public int GrantedBlockerUntil { get; set; }   // ≪블로커≫를 얻음 (메탈그레이몬 ST5-09)
    public int GrantedRebootUntil { get; set; }    // ≪재기동≫을 얻음 (파워드라몬 ST5-12)
    public int UnblockableUntil { get; set; }      // 블록당하지 않음 (오메가몬 Alter-S BT3-112)
    /// <summary>v0.32.0 효과로 받은 효과 ("gains \"[On Deletion] Lose 1 memory.\"") — 효과 문장만 든 카드, 끝나는 턴, 준 쪽 (엔진이 맨 위 카드 효과처럼 발휘)</summary>
    public List<(CardData Effect, int Until, PlayerZones? Granter)> GrantedEffects { get; } = new();

    /// <summary>이번 턴에 사용한 [턴에 1회] 효과 (카드 번호 기준) — 액티브 페이즈에 비운다</summary>
    public HashSet<string> UsedOncePerTurn { get; } = new();

    /// <summary>엔진이 계산한 ≪블로커≫ 보유 여부 (카드 본래 블로커 + 진화원 · 효과로 얻은 블로커)</summary>
    public bool HasGrantedBlocker { get; set; }
    public bool HasBlocker => TopCard.IsBlocker || HasGrantedBlocker;

    /// <summary>v0.36.0 디지몬으로도 취급하는 테이머 (이 디지몬이 그 테이머를 나타낸다 — 테이머 카드는 테이머 존에 그대로). 보통 디지몬이면 null</summary>
    public CardData? TamerProxyOf { get; set; }

    /// <summary>v0.37.0 지속 효과로 더해진 색 ("This Digimon is also treated as yellow" · "~ treated as also having the color of its digivolution cards") — 엔진이 지속 효과를 계산할 때마다 다시 채운다</summary>
    public HashSet<CardColor> ExtraColors { get; } = new();

    /// <summary>v0.42.0 지금 필드에서의 지속 효과 조건 판단용 (엔진이 지속 효과를 계산할 때 채운다 — 조건이 붙은 진화 조건 무시 진화)</summary>
    public Func<DigimonTcg.Core.Engine.Effects.StaticContext>? LiveContext42 { get; set; }
    internal PlayerZones? LiveOwner42 { get; set; }

    /// <summary>v0.37.0 이 디지몬의 지금 색 (맨 위 카드의 색 + 효과로 더해진 색)</summary>
    public IReadOnlyList<CardColor> Colors => ExtraColors.Count == 0 ? TopCard.AllColors : TopCard.AllColors.Concat(ExtraColors.Where(c => !TopCard.AllColors.Contains(c))).ToList();

    public CardInstance(CardData baseCard, int currentTurn)
    {
        InstanceId = Guid.NewGuid().ToString();
        TopCard = baseCard;
        TurnPlayed = currentTurn;
    }

    // =====================================================================
    // v0.35.0 뒷면 진화원 (종합 룰 4-7-4 · 4-7-9 · 4-7-10 · 16-41 ≪트레이닝≫)
    //  - 아래에 겹쳐진 카드는 지정이 없으면 앞면 (4-7-4). "face down"으로 놓인 카드만 뒷면
    //  - 뒷면 카드는 카드의 정보를 갖지 않는 카드로 취급하며 정보를 참조할 수 없다 (4-7-9) → 진화원 효과 · DP · 이름 · 특징 · 색 · Lv 조건에서 빠진다.
    //    장 수("진화원 N장 이상" · "진화원이 없는")에는 들어간다 (카드이므로)
    //  - 주인은 자신의 뒷면 카드를 볼 수 있다 (4-7-10) — 화면은 상대 것만 카드 뒷면으로 가린다
    //  - 뒷면으로 놓을 때 엔진이 카드를 복사해 (같은 CardData 객체가 여러 장을 나타낼 수 있다) 그 복사본을 참조로 구분한다
    // =====================================================================

    /// <summary>뒷면으로 놓인 진화원 (참조로 구분 — 앞면으로 다시 놓이면 지운다)</summary>
    public HashSet<CardData> FaceDownSources { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>이 카드가 이 디지몬의 뒷면 진화원(이었던 카드)인지 — 진화원에서 막 떠난 카드도 true (뒷면 진화원 파기 시점용)</summary>
    public bool IsFaceDown(CardData card) => FaceDownSources.Count > 0 && FaceDownSources.Contains(card);

    /// <summary>앞면 진화원 (위 → 아래) — 정보를 참조하는 곳(진화원 효과 · 이름 · 특징 · 색 · Lv)은 이것을 본다</summary>
    public IEnumerable<CardData> FaceUpSources => FaceDownSources.Count == 0 ? EvolutionSources : EvolutionSources.Where(s => !FaceDownSources.Contains(s));

    /// <summary>뒷면 진화원 장 수</summary>
    public int FaceDownSourceCount => FaceDownSources.Count == 0 ? 0 : EvolutionSources.Count(FaceDownSources.Contains);

    /// <summary>정보가 없는 카드 (뒷면 카드를 조건에 비춰 볼 때 — 이름 · 특징 · 색 · 종류가 없고, 수치는 아주 낮은 것 · 높은 것 둘 다 맞아야 한다)</summary>
    private static readonly CardData BlankLow = new() { CardId = "FACE-DOWN", Name = "", Type = (CardType)(-1), Color = (CardColor)(-1), Level = 0, PlayCost = 0, EvoCost = 0, DP = 0 };
    private static readonly CardData BlankHigh = new() { CardId = "FACE-DOWN", Name = "", Type = (CardType)(-1), Color = (CardColor)(-1), Level = 99, PlayCost = 99, EvoCost = 99, DP = 999999 };

    /// <summary>뒷면 카드가 이 조건에 맞는지 — 정보를 보지 않는 조건("1 card" · "any 1 digivolution card")만 맞는다 (4-7-9)</summary>
    public static bool FaceDownMatches(Func<CardData, bool> match)
    {
        try { return match(BlankLow) && match(BlankHigh); }
        catch { return false; }
    }

    /// <summary>진화원 중 조건에 맞는 카드 (뒷면 카드는 정보 없이 비춰 본다) — 위 → 아래</summary>
    public IEnumerable<CardData> SourcesWhere(Func<CardData, bool> match)
    {
        if (FaceDownSources.Count == 0) return EvolutionSources.Where(match);
        bool fd = EvolutionSources.Any(FaceDownSources.Contains) && FaceDownMatches(match);
        return EvolutionSources.Where(s => FaceDownSources.Contains(s) ? fd : match(s));
    }

    public bool SourcesAny(Func<CardData, bool> match) => SourcesWhere(match).Any();
    public int SourcesCount(Func<CardData, bool> match) => SourcesWhere(match).Count();

    /// <summary>카드를 진화원 맨 아래에 뒷면으로 놓는다 (복사본을 놓고 반환)</summary>
    public CardData AddBottomSourceFaceDown(CardData card)
    {
        var copy = CopyCard(card);
        AddBottomSource(copy);
        FaceDownSources.Add(copy);
        return copy;
    }

    /// <summary>진화원 맨 위(맨 위 카드 바로 아래)에 앞면으로 놓는다</summary>
    public void AddTopSource(CardData card)
    {
        FaceDownSources.Remove(card);
        EvolutionSources.Push(card);
    }

    /// <summary>맨 위 카드가 뒷면 카드가 되었는지 (≪퇴화≫ 등으로 드러남 — 종합 룰 17-1-3-2-4: 에어리어의 뒷면 카드는 룰 처리로 파기)</summary>
    public bool TopIsFaceDown => FaceDownSources.Count > 0 && FaceDownSources.Contains(TopCard);

    private static readonly System.Reflection.MethodInfo CloneMethod = typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
    internal static CardData CopyCard(CardData card) => (CardData)CloneMethod.Invoke(card, null)!;

    public void Evolve(CardData newCard)
    {
        FaceDownSources.Remove(newCard);
        EvolutionSources.Push(TopCard);
        TopCard = newCard;
    }

    /// <summary>≪퇴화≫: 맨 위 카드를 버리고 바로 아래 진화원을 맨 위로 올린다 (버린 카드 반환)</summary>
    public CardData? DeDigivolveOnce()
    {
        if (EvolutionSources.Count == 0 || TopCard.Level <= 3)
            return null;
        var removed = TopCard;
        TopCard = EvolutionSources.Pop();
        return removed;
    }

    /// <summary>≪아머 퍼지≫: 맨 위 카드를 버리고 바로 아래 진화원을 맨 위로 올린다 (레벨 제한 없음, 진화원이 없으면 null)</summary>
    public CardData? TrashTopCard()
    {
        if (EvolutionSources.Count == 0)
            return null;
        var removed = TopCard;
        TopCard = EvolutionSources.Pop();
        return removed;
    }

    /// <summary>
    /// 이 디지몬에 겹쳐져 있는 카드를 위에서부터 1장 진화원 맨 아래에 놓는다 (공식 Q&amp;A EX5-007: 맨 위 카드를 진화원 맨 밑에 넣는다 —
    /// 바로 아래 진화원이 맨 위 카드가 된다). 진화원이 없으면 false
    /// </summary>
    public bool MoveTopToBottom()
    {
        if (EvolutionSources.Count == 0)
            return false;
        var oldTop = TopCard;
        TopCard = EvolutionSources.Pop();
        AddBottomSource(oldTop);
        return true;
    }

    /// <summary>진화원을 아래(가장 먼저 깔린 카드)에서부터 최대 count장 제거해 반환한다</summary>
    public List<CardData> RemoveBottomSources(int count)
    {
        var bottomUp = EvolutionSources.Reverse().ToList();
        var removed = bottomUp.Take(count).ToList();
        var rest = bottomUp.Skip(removed.Count).ToList();
        EvolutionSources = new Stack<CardData>(rest);
        return removed;
    }

    /// <summary>진화원 맨 아래에 카드를 넣는다</summary>
    public void AddBottomSource(CardData card)
    {
        FaceDownSources.Remove(card);
        var bottomUp = EvolutionSources.Reverse().ToList();
        bottomUp.Insert(0, card);
        EvolutionSources = new Stack<CardData>(bottomUp);
    }

    /// <summary>진화원에서 조건에 맞는 카드 1장을 꺼낸다 (순서 유지)</summary>
    public CardData? TakeSource(Func<CardData, bool> predicate)
    {
        var bottomUp = EvolutionSources.Reverse().ToList();
        var found = bottomUp.FirstOrDefault(predicate);
        if (found == null)
            return null;
        bottomUp.Remove(found);
        EvolutionSources = new Stack<CardData>(bottomUp);
        return found;
    }

    public bool HasSource(string cardId) => FaceUpSources.Any(s => s.CardId == cardId);

    /// <summary>
    /// v0.45.0 소멸하기 직전의 이 디지몬 (조건 판단 전용 복사본 — 진화원 · DP · 색 · 키워드 · 링크를 그대로 가진다). 소멸하지 않았으면 null.
    /// 종합 룰 15-8-3-9-4: 영역을 떠나 유발한 효과(【소멸 시】 · "소멸했을 때")의 처리 조건은 유발한 시점의 상황을 참조한다
    /// </summary>
    internal CardInstance? DeletionView45 { get; set; }

    /// <summary>v0.45.0 지속 효과로 액티브가 되지 않는다 (액티브 페이즈 · 효과 모두 — BT12-057). 엔진이 지속 효과를 계산할 때마다 다시 채운다</summary>
    public bool StaticUnsuspendLock45 { get; set; }

    /// <summary>v0.45.0 효과로 바뀐 등장 코스트 (기간 — "reduce the play costs of all of your opponent's Digimon by 1 until the end of the turn" BT16-060)</summary>
    public List<(int Delta, int Until)> TimedPlayCostMods45 { get; } = new();

    /// <summary>v0.45.0 지금 기간 안의 등장 코스트 변경 합 (엔진이 지속 효과를 계산할 때마다 다시 채운다)</summary>
    public int PlayCostDelta45 { get; set; }

    /// <summary>v0.45.0 이 Lv.의 카드로 진화할 수 없다 (0 = 없음 — "The Digimon played by this effect can't digivolve to level 7" EX3-069)</summary>
    public int CannotEvoToLevel45 { get; set; }

    /// <summary>v0.45.0 지속 효과의 DP 하한 (0 = 없음 — "can't have less than 1000 DP" EX11-070). 엔진이 지속 효과를 계산할 때마다 다시 채운다</summary>
    public int DpFloor45 { get; set; }

    /// <summary>v0.45.0 이 디지몬의 지금 등장 코스트 (맨 위 카드 + 효과로 바뀐 값, 0 미만은 0)</summary>
    public int PlayCost45 => PlayCostDelta45 == 0 ? TopCard.PlayCost : Math.Max(0, TopCard.PlayCost + PlayCostDelta45);

    /// <summary>v0.45.0 지금 모습의 복사본 (조건 판단 전용 — 효과가 옮기는 카드는 원래 디지몬의 것을 쓴다)</summary>
    internal CardInstance SnapshotView45()
    {
        var v = new CardInstance(TopCard, TurnPlayed)
        {
            InstanceId = InstanceId,
            IsSuspended = IsSuspended,
            TemporaryDpBonus = TemporaryDpBonus,
            TemporarySecurityAttackBonus = TemporarySecurityAttackBonus,
            AttackDpBonus = AttackDpBonus,
            AttackSecurityAttackBonus = AttackSecurityAttackBonus,
            TamerDpBonus = TamerDpBonus,
            TamerSecurityAttackBonus = TamerSecurityAttackBonus,
            EffectDpBonus = EffectDpBonus,
            EffectSecurityAttackBonus = EffectSecurityAttackBonus,
            IsOwnersTurn = IsOwnersTurn,
            HasGrantedBlocker = HasGrantedBlocker,
            TamerProxyOf = TamerProxyOf,
            PlayCostDelta45 = PlayCostDelta45,
            DpFloor45 = DpFloor45,
        };
        v.EvolutionSources = new Stack<CardData>(EvolutionSources.Reverse());
        foreach (var f in FaceDownSources) v.FaceDownSources.Add(f);
        foreach (var k in ActiveKeywords) v.ActiveKeywords.Add(k);
        foreach (var c in ExtraColors) v.ExtraColors.Add(c);
        v.LinkedCards.AddRange(LinkedCards);
        v.TimedDpBuffs.AddRange(TimedDpBuffs);
        v.TimedKeywords.AddRange(TimedKeywords);
        v.GrantedEffects.AddRange(GrantedEffects);
        return v;
    }

    public int GetInheritedDpBonus() =>
        IsOwnersTurn ? FaceUpSources.Sum(s => s.InheritedDpBonus) : 0;

    public int GetTotalBonusDP() =>
        GetInheritedDpBonus() + TamerDpBonus + TemporaryDpBonus + EffectDpBonus + AttackDpBonus;

    public int GetCurrentDP() =>
        Math.Max(DpFloor45, TopCard.DP + GetTotalBonusDP());   // v0.45.0 DP 하한 (0이면 원래대로)

    public int GetInheritedSecurityAttackBonus() =>
        FaceUpSources.Sum(s => s.InheritedSecurityAttackBonus);

    public int GetTotalBonusSecurityAttack() =>
        TopCard.SecurityAttackBonus + GetInheritedSecurityAttackBonus() + TamerSecurityAttackBonus + TemporarySecurityAttackBonus + EffectSecurityAttackBonus + AttackSecurityAttackBonus;

    public int GetSecurityAttackCount() =>
        Math.Max(0, 1 + GetTotalBonusSecurityAttack());

    /// <summary>
    /// 진화원 리스트 (아래에서 위로 쌓인 순서 반환 - UI 스택 표시용)
    /// </summary>
    public IReadOnlyList<CardData> GetEvolutionStackBottomUp()
    {
        return EvolutionSources.Reverse().ToList();
    }

    /// <summary>앞면 진화원 (아래 → 위)</summary>
    public IReadOnlyList<CardData> GetFaceUpStackBottomUp() => FaceUpSources.Reverse().ToList();
}
