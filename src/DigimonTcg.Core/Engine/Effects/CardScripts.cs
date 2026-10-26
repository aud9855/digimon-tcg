using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// 카드 번호 → 효과 정의. 엔진은 카드 번호로 분기하지 않고 여기서 효과를 찾아 처리한다.
/// 지금은 사람이 쓴 스크립트(BuiltInCardScripts)만 있고, 이후 키워드 사전 · 문장 파서가 같은 형식으로 채운다
/// (docs/card_effect_engine_plan.md).
/// </summary>
public static class CardScripts
{
    private static readonly Lazy<Dictionary<string, CardScript>> Registry = new(() =>
    {
        var builder = new CardScriptBuilder();
        BuiltInCardScripts.Register(builder);
        return builder.Scripts;
    });

    // API 카드(내장 카드가 아닌 카드)의 자동 스크립트 — 키워드 사전이 효과 문장에서 만든다
    private static readonly ConcurrentDictionary<string, CardScript?> Generated = new(StringComparer.Ordinal);

    /// <summary>사람이 쓴 스크립트 (내장 카드)</summary>
    public static CardScript? Get(string cardId) =>
        Registry.Value.TryGetValue(cardId, out var script) ? script : null;

    /// <summary>카드의 효과 정의: 사람이 쓴 스크립트 → (내장 카드가 아니면) 키워드 사전이 만든 스크립트</summary>
    public static CardScript? For(CardData card)
    {
        if (Registry.Value.TryGetValue(card.CardId, out var script))
            return script;
        if (StarterDecks.IsBuiltInCard(card.CardId))
            return null;
        return Generated.GetOrAdd(card.CardId, _ => KeywordDictionary.GenerateScript(card));
    }

    // 듀얼 카드의 옵션 쪽 스크립트 (카드 번호 → 스크립트)
    private static readonly ConcurrentDictionary<string, CardScript?> OptionSides = new(StringComparer.Ordinal);

    /// <summary>듀얼 카드의 옵션 쪽 효과 (옵션 카드로 읽은 것). 듀얼 카드가 아니면 null</summary>
    public static CardScript? OptionSideFor(CardData card) =>
        card.IsDual ? OptionSides.GetOrAdd(card.CardId, _ => KeywordDictionary.GenerateOptionSideScript(card)) : null;

    /// <summary>패에서 "사용"할 때 보는 스크립트 — 옵션은 그 카드, 듀얼 카드는 옵션 쪽</summary>
    public static CardScript? ForUse(CardData card) => card.IsDual ? OptionSideFor(card) : For(card);

    /// <summary>패에서 옵션으로 사용하는 카드인지 (옵션 카드 · 옵션 쪽 [메인] 효과를 읽은 듀얼 카드)</summary>
    public static bool IsUsableAsOption(CardData card) =>
        card.Type == CardType.Option || card.IsDual && OptionSideFor(card)?.Effects.Any(e => e.Trigger == EffectTrigger.Main) == true;

    public static IReadOnlyCollection<CardScript> All => Registry.Value.Values;

    /// <summary>디지몬이 키워드를 가졌는지 (맨 위 카드의 키워드 + 진화원 효과로 얻는 키워드)</summary>
    public static bool HasKeyword(CardInstance digimon, Keyword keyword) =>
        digimon.ActiveKeywords.Contains(keyword)
        || (For(digimon.TopCard)?.Keywords.Contains(keyword) ?? false)
        || digimon.EvolutionSources.Any(s => For(s)?.InheritedKeywords.Contains(keyword) ?? false)
        || GainedSources(digimon).Any(s => For(s)?.Keywords.Contains(keyword) ?? false);   // v0.31.0 진화원 카드의 효과 전부를 얻는다

    /// <summary>
    /// v0.31.0 이 디지몬이 효과를 얻는 진화원 카드 (맨 위 카드의 메인 효과 · 진화원 효과의 "gains all effects of ~ in this Digimon's digivolution cards").
    /// 같은 카드 번호는 1장, 맨 위 카드 자신은 아니다 (공식 Q&A BT16-062 · BT16-014: 진화할 때 얻은 [진화 시]도 발휘)
    /// </summary>
    public static IEnumerable<CardData> GainedSources(CardInstance digimon, IReadOnlyList<CardData>? sources = null)
    {
        IEnumerable<CardData> stack = sources ?? (IEnumerable<CardData>)digimon.EvolutionSources;   // [소멸 시]: 이미 트래시로 간 진화원
        if (!stack.Any()) return Enumerable.Empty<CardData>();
        var whichList = new List<Func<CardData, bool>>();
        if (For(digimon.TopCard) is { GainedSourceEffects.Count: > 0 } top)
            whichList.AddRange(top.GainedSourceEffects.Where(g => g.Source == EffectSource.Main).Select(g => g.Which));
        foreach (var src in stack)
            if (For(src) is { GainedSourceEffects.Count: > 0 } ss)
                whichList.AddRange(ss.GainedSourceEffects.Where(g => g.Source == EffectSource.Inherited).Select(g => g.Which));
        if (whichList.Count == 0) return Enumerable.Empty<CardData>();
        return stack.Where(s => !ReferenceEquals(s, digimon.TopCard) && whichList.Any(w => w(s)))
            .GroupBy(s => s.CardId).Select(g => g.First()).ToList();
    }

    /// <summary>옵션 [메인] 효과를 쓸 때 고를 대상 종류</summary>
    public static TargetKind MainTargetOf(CardData card) => For(card)?.MainTarget ?? TargetKind.None;

    /// <summary>옵션 [메인] 효과의 대상이 될 수 있는지 (대상 조건이 없으면 true) — 화면이 놓을 수 있는 디지몬을 강조할 때도 쓴다</summary>
    public static bool IsValidMainTarget(CardData card, CardInstance target) =>
        For(card)?.MainTargetFilter is not { } filter || filter(target);

    /// <summary>
    /// 옵션을 놓은 디지몬이 대상 조건에 안 맞으면 안내 문구, 맞거나 조건이 없으면 null.
    /// targetSide = 대상 종류에 맞는 쪽의 배틀 에어리어 (상대 디지몬 대상이면 상대 쪽) — 다른 쪽 디지몬에 놓은 것은 여기서 따지지 않는다
    /// </summary>
    public static string? InvalidMainTarget(CardData card, CardInstance? target, IEnumerable<CardInstance> targetSide) =>
        target != null && For(card) is { MainTargetFilter: { } filter } script && targetSide.Contains(target) && !filter(target)
            ? $"[{card.Name}]은(는) {script.MainTargetRule ?? "조건에 맞는 디지몬"}에게만 쓸 수 있습니다. [{target.TopCard.Name}]은(는) 대상이 될 수 없습니다."
            : null;

    /// <summary>
    /// 실제로 지불할 등장 · 사용 코스트 (카드의 코스트 감소 효과 반영, 0 미만이 되지 않음).
    /// enemy = 상대의 필드 ("if your opponent has ~" · "for each of your opponent's Digimon"). 모르면 빈 필드로 본다
    /// </summary>
    public static int EffectivePlayCost(PlayerZones owner, CardData card, PlayerZones? enemy = null) =>
        Math.Max(0, card.PlayCost - (ForUse(card)?.PlayCostReduction?.Invoke(owner, enemy ?? NoZones) ?? 0));

    private static readonly PlayerZones NoZones = new();

    /// <summary>카드에 적힌 다른 진화 조건을 만족하는지 (예: 오메가몬은 Lv.6 레드 · 블루 디지몬에서 진화)</summary>
    public static bool MeetsAlternativeEvolution(CardData evoCard, CardInstance target) =>
        For(evoCard)?.AlternativeEvolution?.Invoke(target) ?? false;

    /// <summary>진화 조건을 무시하는 다른 진화로 target에서 evoCard로 진화할 수 있으면 그 코스트 (여럿이면 가장 싼 것), 없으면 null</summary>
    /// <summary>
    /// 패의 evoCard로 테이머 tamer를 진화시킬 수 있으면 그 진화 코스트 (v0.30.0): 카드의 "~ Tamers as if the Tamer is a level 3 red Digimon" 이 테이머에 맞고,
    /// 그 레벨 · 색의 디지몬에서 진화하는 진화 조건을 가질 때
    /// </summary>
    public static int? TamerEvolutionCost(CardData evoCard, CardData tamer) =>
        tamer.Type != CardType.Tamer || evoCard.Type != CardType.Digimon ? null
        : For(evoCard)?.TamerEvolutions.Where(t => t.Tamer(tamer) && evoCard.EvoRequirementLevel == t.Level && evoCard.EvoRequirementColor == t.Color)
            .Select(t => (int?)(t.Cost ?? evoCard.EvoCost)).Min();

    public static int? SpecialEvolutionCost(CardData evoCard, CardInstance target) =>
        For(evoCard)?.SpecialEvolutions.Where(s => s.From(target)).Select(s => (int?)s.Cost).Min();
}

/// <summary>카드 스크립트를 읽기 쉽게 등록하는 도구</summary>
public sealed class CardScriptBuilder
{
    internal Dictionary<string, CardScript> Scripts { get; } = new(StringComparer.Ordinal);

    public CardScriptDefinition Card(string cardId)
    {
        if (!Scripts.TryGetValue(cardId, out var script))
            Scripts[cardId] = script = new CardScript(cardId);
        return new CardScriptDefinition(script);
    }
}

public sealed class CardScriptDefinition
{
    private readonly CardScript _script;
    internal CardScriptDefinition(CardScript script) => _script = script;

    /// <summary>v0.31.0: 설정하면 On()이 이 출처로 등록한다 ([트래시] 시점 효과 = EffectSource.Trash) — 문장 하나를 읽는 동안만</summary>
    internal EffectSource? ZoneSource { get; set; }

    public CardScriptDefinition On(EffectTrigger trigger, Action<EffectContext> resolve, Func<EffectContext, bool>? when = null, bool oncePerTurn = false, bool twicePerTurn = false)
    {
        _script.Effects.Add(new CardEffect(trigger, ZoneSource ?? EffectSource.Main, resolve) { Condition = when, OncePerTurn = oncePerTurn, TwicePerTurn = twicePerTurn });
        return this;
    }

    /// <summary>진화원 효과</summary>
    public CardScriptDefinition Inherited(EffectTrigger trigger, Action<EffectContext> resolve, Func<EffectContext, bool>? when = null, bool oncePerTurn = false, bool twicePerTurn = false)
    {
        _script.Effects.Add(new CardEffect(trigger, EffectSource.Inherited, resolve) { Condition = when, OncePerTurn = oncePerTurn, TwicePerTurn = twicePerTurn });
        return this;
    }

    public CardScriptDefinition OnPlay(Action<EffectContext> resolve, Func<EffectContext, bool>? when = null) => On(EffectTrigger.OnPlay, resolve, when);
    public CardScriptDefinition WhenDigivolving(Action<EffectContext> resolve, Func<EffectContext, bool>? when = null) => On(EffectTrigger.WhenDigivolving, resolve, when);
    public CardScriptDefinition WhenAttacking(Action<EffectContext> resolve, Func<EffectContext, bool>? when = null, bool oncePerTurn = false) => On(EffectTrigger.WhenAttacking, resolve, when, oncePerTurn);
    public CardScriptDefinition OnDeletion(Action<EffectContext> resolve) => On(EffectTrigger.OnDeletion, resolve);
    public CardScriptDefinition Security(Action<EffectContext> resolve) => On(EffectTrigger.Security, resolve);

    /// <summary>옵션 [메인] 효과 (filter · rule = 대상 조건과 그 설명, 예: 레스트 상태인 상대 디지몬)</summary>
    public CardScriptDefinition Main(TargetKind target, Action<EffectContext> resolve, Func<CardInstance, bool>? filter = null, string? rule = null)
    {
        _script.MainTarget = target;
        _script.MainTargetFilter = filter;
        _script.MainTargetRule = rule;
        return On(EffectTrigger.Main, resolve);
    }

    public CardScriptDefinition Static(StaticStat stat, EffectSource source, Func<StaticContext, int> value, Keyword? keyword = null, Func<CardInstance, bool>? subject = null)
    {
        _script.Statics.Add(new StaticEffect(stat, source, value) { Keyword = keyword, Subject = subject });
        return this;
    }

    /// <summary>진화원 효과: [자신의 턴] 조건부 DP +N</summary>
    public CardScriptDefinition InheritedDpOnOwnTurn(int dp, Func<StaticContext, bool> when) =>
        Static(StaticStat.Dp, EffectSource.Inherited, c => c.OwnersTurn && when(c) ? dp : 0);

    public CardScriptDefinition Keyword(Keyword keyword)
    {
        _script.Keywords.Add(keyword);
        return this;
    }

    public CardScriptDefinition InheritedKeyword(Keyword keyword)
    {
        _script.InheritedKeywords.Add(keyword);
        return this;
    }

    public CardScriptDefinition EvolvesFrom(Func<CardInstance, bool> condition)
    {
        _script.AlternativeEvolution = condition;
        return this;
    }

    public CardScriptDefinition ReducePlayCost(Func<PlayerZones, int> reduction) => ReducePlayCost((z, _) => reduction(z));

    /// <summary>진화하기 직전의 진화 코스트 감소 ("When this Digimon would digivolve into ~, reduce the digivolution cost by N")</summary>
    public CardScriptDefinition EvoCostModifier(EvoCostModifier modifier)
    {
        _script.EvoCostModifiers.Add(modifier);
        return this;
    }

    /// <summary>v0.31.0 진화원 카드의 효과 전부를 얻는다 ("This Digimon gains all effects of ~ in this Digimon's digivolution cards")</summary>
    public CardScriptDefinition GainSourceEffects(EffectSource source, Func<CardData, bool> which)
    {
        _script.GainedSourceEffects.Add((source, which));
        return this;
    }

    /// <summary>v0.31.0 다른 카드의 등장 코스트 감소 (필드 · 육성의 이 카드)</summary>
    public CardScriptDefinition FieldPlayCostModifier(FieldPlayCostModifier modifier)
    {
        _script.FieldPlayCostModifiers.Add(modifier);
        return this;
    }

    /// <summary>등장할 때 비용을 내면 코스트 -N ("When this card would be played, by ~ing, reduce the play cost by N")</summary>
    public CardScriptDefinition OptionalPlayCostReduction(PlayCostOption option)
    {
        _script.PlayCostOptions.Add(option);
        return this;
    }

    /// <summary>코스트 감소 (자신의 필드, 상대의 필드 — 상대를 모르면 빈 필드)</summary>
    public CardScriptDefinition ReducePlayCost(Func<PlayerZones, PlayerZones, int> reduction)
    {
        var previous = _script.PlayCostReduction;
        _script.PlayCostReduction = previous == null ? reduction : (z, e) => previous(z, e) + reduction(z, e);
        return this;
    }

    public CardScriptDefinition SpecialEvolution(Func<CardInstance, bool> from, int cost)
    {
        _script.SpecialEvolutions.Add((from, cost));
        return this;
    }

    /// <summary>테이머를 Lv.N 색 디지몬으로 취급해 패의 이 카드로 진화 (v0.30.0, 공식 Q&A BT4-011 · BT12-012)</summary>
    public CardScriptDefinition TamerEvolution(Func<CardData, bool> tamer, int level, CardColor color, int? cost)
    {
        _script.TamerEvolutions.Add((tamer, level, color, cost));
        return this;
    }

    /// <summary>≪오버클럭 (지정)≫ — 자신의 턴 종료 시, 토큰 또는 지정된 다른 자신의 디지몬 1마리를 소멸시켜 레스트하지 않고 플레이어에게 어택</summary>
    public CardScriptDefinition Overclock(Func<CardData, bool> specified)
    {
        _script.OverclockSpecified = specified;
        return this;
    }

    public CardScriptDefinition Overflow(int memory)
    {
        _script.OverflowMemory = memory;
        return this;
    }

    public CardScriptDefinition Link(Func<CardInstance, bool> requirement, int cost)
    {
        _script.LinkRequirement = requirement;
        _script.LinkCost = cost;
        return this;
    }

    /// <summary>디지크로스에서만 이 이름으로도 취급 (영어 이름)</summary>
    public CardScriptDefinition XrosAlias(string englishName)
    {
        _script.XrosAliases.Add(englishName);
        return this;
    }

    /// <summary>이 조건이면 트래시의 카드도 이 카드의 디지크로스 재료</summary>
    public CardScriptDefinition XrosFromTrash(Func<PlayerZones, bool> condition)
    {
        _script.XrosFromTrash = condition;
        return this;
    }

    /// <summary>테이머: 이 조건의 디지몬을 디지크로스할 때 이 테이머를 레스트시켜 테이머 아래 카드도 재료로</summary>
    public CardScriptDefinition XrosUnderTamers(Func<CardData, bool> forDigimon)
    {
        _script.XrosUnderTamersFor = forDigimon;
        return this;
    }

    public CardScriptDefinition AllowBreedingActivation()
    {
        _script.ActivateInBreeding = true;
        return this;
    }

    public CardScriptDefinition MaterialSave(int count)
    {
        _script.MaterialSave = count;
        return this;
    }

    public CardScriptDefinition BlastDigivolve()
    {
        _script.BlastDigivolve = true;
        return this;
    }

    public CardScriptDefinition Digisorption(int reduction)
    {
        _script.DigisorptionReduction = reduction;
        return this;
    }

    public CardScriptDefinition IgnoreColorRequirementWhen(Func<PlayerZones, bool> condition)
    {
        _script.IgnoreColorRequirement = condition;
        return this;
    }

    public CardScriptDefinition StartOfTurnMemory(int amount, Func<PlayerZones, bool> enemyCondition, string reason)
    {
        _script.StartOfTurnMemory = new StartOfTurnMemoryEffect(amount, enemyCondition, reason);
        return this;
    }
}
