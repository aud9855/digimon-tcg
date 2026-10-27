using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.43.0 미지원 문장 17차 묶음의 엔진 처리 (SentenceParser.Batch43*):
///  - 링크 카드의 링크 효과 (종합 룰 2-3-11-4-6 「링크 효과」 — 링크되어 있는 동안 그 디지몬의 효과로 발휘 · EffectSource.Link43)
///  - 어플 합체 (종합 룰 8-4): 「어플 합체」를 가진 디지몬 카드로, 지정된 카드 2종의 조합으로 링크 상태인 디지몬의 링크 카드를 그 디지몬 위에 겹치고 진화
///  - ≪블래스트 조그레스≫ (종합 룰 16-31): 【카운터】 지정된 자신의 디지몬 1마리와 패의 지정된 카드 1장으로 패의 이 카드로 조그레스 진화 (조그레스 조건은 무시할 수 없다)
/// 어플 합체 조건 · 조그레스 조건은 영어 데이터에 없어서 공식 한글 문구를 실행 중에만 읽는다 (저장하지 않는다 — 링크 조건과 같다)
/// </summary>
public partial class GoldfishEngine
{
    // =====================================================================
    // 링크 효과 (링크되어 있는 동안)
    // =====================================================================

    /// <summary>self에 링크된 카드들의 링크 효과 중 trigger 시점 효과 (같은 카드 번호는 1번, 처리 직전에 아직 링크되어 있을 때만)</summary>
    private void RunLinkEffects43(EffectContext ctx, CardInstance self, EffectTrigger trigger)
    {
        foreach (var link in self.LinkedCards.GroupBy(c => c.CardId).Select(g => g.First()).ToList())
        {
            if (CardScripts.For(link) is not { } script) continue;
            foreach (var effect in script.Effects.Where(e => e.Trigger == trigger && e.Source == EffectSource.Link43).ToList())
            {
                if (!self.LinkedCards.Contains(link)) break;
                Diag43.Hit(ref Diag43.LinkEffect);
                Resolve(ctx, link, effect);
            }
        }
    }

    // =====================================================================
    // 어플 합체 (종합 룰 8-4)
    // =====================================================================

    internal sealed record AppFusionRequirement(IReadOnlyList<string> Names, int Cost, string Text);

    private static readonly ConcurrentDictionary<string, AppFusionRequirement?> AppFusionCache = new(StringComparer.Ordinal);

    /// <summary>카드의 어플 합체 조건 (공식 한글 "〔어플 합체〕「서치몬」＆「내비몬」＆「츠부몬」 : 코스트 0" — 없거나 읽지 못하면 null)</summary>
    internal static AppFusionRequirement? AppFusionOf(CardData card) =>
        card.Type != CardType.Digimon ? null : AppFusionCache.GetOrAdd(card.CardId, _ => ParseKoreanAppFusion(card));

    /// <summary>어플 합체 조건이 있는지 (화면 · AI)</summary>
    public static bool HasAppFusion(CardData card) => AppFusionOf(card) != null;

    /// <summary>어플 합체 코스트 (조건을 모르면 null)</summary>
    public static int? GetAppFusionCost(CardData card) => AppFusionOf(card)?.Cost;

    private static AppFusionRequirement? ParseKoreanAppFusion(CardData card)
    {
        var ko = KoreanCardData.Get(card.CardId);
        if (ko?.MainText is not { Length: > 0 } text) return null;
        var m = Regex.Match(text, @"〔어플\s*합체〕\s*(?<names>(?:「[^」]+」\s*[＆&]?\s*)+)\s*[:：]\s*코스트\s*(?<n>\d+)");
        if (!m.Success) return null;
        var names = Regex.Matches(m.Groups["names"].Value, @"「([^」]+)」").Select(x => x.Groups[1].Value.Trim()).Where(x => x.Length > 0).Distinct().ToList();
        if (names.Count < 2) return null;
        return new AppFusionRequirement(names, int.Parse(m.Groups["n"].Value), string.Join("＆", names.Select(n => $"「{n}」")));
    }

    /// <summary>카드의 어플 합체 조건에서 쓰는 이름 (공식 한글 이름 — 없으면 같은 영어 이름의 한글 이름)</summary>
    private static string FusionName43(CardData c) => KoreanCardData.DisplayName(c);

    /// <summary>host(배틀 에어리어의 디지몬)가 card의 어플 합체 조건 2종의 조합으로 링크 상태이면 그 링크 카드 (가장 먼저 링크한 것부터), 아니면 null</summary>
    internal static CardData? AppFusionLink43(CardData card, CardInstance host)
    {
        if (AppFusionOf(card) is not { } req || IsToken(host.TopCard) || host.TamerProxyOf != null) return null;
        string hostName = FusionName43(host.TopCard);
        if (!req.Names.Contains(hostName)) return null;
        return host.LinkedCards.FirstOrDefault(l => FusionName43(l) is var ln && ln != hostName && req.Names.Contains(ln));
    }

    /// <summary>owner가 card로 어플 합체할 수 있는 디지몬 (링크 카드)</summary>
    internal List<(CardInstance Host, CardData Link)> AppFusionTargets43(PlayerZones owner, CardData card)
    {
        var list = new List<(CardInstance, CardData)>();
        if (AppFusionOf(card) == null) return list;
        foreach (var d in owner.BattleArea)
            if (AppFusionLink43(card, d) is { } link && !EffectRules.IsDigivolveLocked(State, d) && CardScripts.EvoAllowed(d, card))
                list.Add((d, link));
        return list;
    }

    /// <summary>패의 card로 어플 합체할 수 있는 내 디지몬 (메인 페이즈 — 화면이 쓴다)</summary>
    public IReadOnlyList<CardInstance> GetAppFusionTargets(CardData card) =>
        State.Player.Hand.Contains(card) && State.CurrentPhase == Phase.Main ? AppFusionTargets43(State.Player, card).Select(x => x.Host).ToList() : Array.Empty<CardInstance>();

    /// <summary>플레이어: 패의 card로 내 디지몬 host를 어플 합체 (메인 페이즈, 코스트 지불). 메모리가 0 미만이 되면 턴이 끝난다</summary>
    public ActionResult AppFuse(CardData card, CardInstance host)
    {
        if (State.IsGameOver || State.CurrentPhase != Phase.Main || State.PendingBlock != null || State.BlitzAttacker != null)
            return ActionResult.Fail("지금은 어플 합체할 수 없습니다.");
        if (!State.Player.Hand.Contains(card) || !State.Player.BattleArea.Contains(host))
            return ActionResult.Fail("어플 합체할 카드나 디지몬이 없습니다.");
        if (AppFusionOf(card) is not { } req)
            return ActionResult.Fail($"[{card.Name}]의 어플 합체 조건을 알 수 없습니다 (공식 한글 데이터가 필요합니다).");
        if (AppFusionLink43(card, host) is not { } link)
            return ActionResult.Fail($"[{host.TopCard.Name}]은(는) [{card.Name}]의 어플 합체 조건({req.Text} 중 2종의 조합으로 링크 상태)에 맞지 않습니다.");
        if (EffectRules.IsDigivolveLocked(State, host) || !CardScripts.EvoAllowed(host, card))
            return ActionResult.Fail($"[{host.TopCard.Name}]은(는) 진화할 수 없습니다 (카드 효과).");
        State.CanMulligan = false;
        var msg = AppFuseCore43(State.Player, card, host, link, req.Cost, State.Player.Hand, byEffect: false);
        CheckTurnEndAndAutoReset();
        return msg == null ? ActionResult.Fail("어플 합체하지 못했습니다.") : ActionResult.Success(msg);
    }

    /// <summary>
    /// 어플 합체 처리 (검사 뒤, 종합 룰 8-4-3): 코스트 지불 → host의 링크 카드를 host 위에 겹친다 → 그 위에 card → 1장 드로우 → [진화 시].
    /// 진화에 영향을 미치는 효과(진화 코스트 감소 등)는 어플 합체에도 (8-4-2-3). 진화하기 전의 표시 형식(레스트)은 그대로. 반환: 결과 메시지 (하지 못했으면 null)
    /// </summary>
    internal string? AppFuseCore43(PlayerZones owner, CardData card, CardInstance host, CardData link, int cost, List<CardData> from, bool byEffect)
    {
        if (!owner.BattleArea.Contains(host) || !host.LinkedCards.Contains(link) || EffectRules.IsDigivolveLocked(State, host) || !CardScripts.EvoAllowed(host, card))
            return null;
        if (!from.Contains(card)) return null;
        string hostName = host.TopCard.Name;
        if (byEffect)
        {
            // 효과로 어플 합체 — 나머지(코스트 감소 · 드로우 · [진화 시] · "효과로 진화")는 효과로 진화와 같다
            host.LinkedCards.Remove(link);
            host.Evolve(link);
            EffectLog(owner, $"[어플 합체] [{hostName}]의 링크 카드 [{link.Name}]을(를) 위에 겹칩니다.");
            EvolveByEffect(owner, host, card, cost, from);
            Diag43.Hit(ref Diag43.AppFusionByEffect);
            return $"[어플 합체] [{hostName}] + [{link.Name}] → [{card.Name}]";
        }
        from.Remove(card);
        cost = ApplyEvoCostModifiers(owner, host, card, cost);
        host.LinkedCards.Remove(link);
        host.Evolve(link);
        host.Evolve(card);
        AddMemory(owner, -cost);
        if (ReferenceEquals(owner, State.Player)) { State.TotalMemorySpent += cost; State.TotalEvolutionsCount++; }
        string msg = $"[어플 합체] [{hostName}] + 링크 [{link.Name}] → [{card.Name}(Lv.{card.Level})] (코스트 {cost})";
        EffectLog(owner, msg);
        Diag43.Hit(ref Diag43.AppFusion);
        DrawFor(owner);
        RecalculateFieldBuffs();
        if (owner.BattleArea.Contains(host))
        {
            ApplyWhenDigivolvingEffects(owner, host, card);
            if (owner.BattleArea.Contains(host))
                FireForSide(EffectTrigger.AllyDigimonDigivolved, owner, host);
        }
        return msg;
    }

    /// <summary>AI: 메모리 안에서 어플 합체를 할 수 있으면 한다 (레벨이 높은 카드부터). 했으면 true</summary>
    private bool AiTryAppFusion43(AiTurnContext ctx)
    {
        var opp = State.DummyOpponent;
        foreach (var card in opp.Hand.Where(c => c.Type == CardType.Digimon && !c.IsDual && HasAppFusion(c)).Distinct().OrderByDescending(c => c.Level).ToList())
        {
            if (AppFusionOf(card) is not { } req || req.Cost > ctx.Memory) continue;
            var t = AppFusionTargets43(opp, card).OrderByDescending(x => x.Host.EvolutionSources.Count).FirstOrDefault();
            if (t.Host == null) continue;
            int before = ctx.Memory;
            string hostName = t.Host.TopCard.Name;
            int copiesBefore = opp.Hand.Count(c => ReferenceEquals(c, card));
            string? done = null;
            var logs = CaptureEffectLogs(() => done = AppFuseCore43(opp, card, t.Host, t.Link, req.Cost, opp.Hand, byEffect: false));
            if (done == null || opp.Hand.Count(c => ReferenceEquals(c, card)) == copiesBefore) return false;
            Diag43.Hit(ref Diag43.AiAppFusion);
            Log($"🤖 [AI 어플 합체] [{hostName}] + [{t.Link.Name}] -> [{card.Name}(Lv.{card.Level}, 코스트 {req.Cost})] (AI 메모리: +{before} → {ctx.Memory})");
            AddOpponentEvent(OpponentActionKind.Evolve, "어플 합체", $"{hostName} + {t.Link.Name} → {card.Name} (Lv.{card.Level}) · 메모리 {before} → {ctx.Memory}", card);
            AddEffectEvent(card, logs);
            return true;
        }
        return false;
    }

    // =====================================================================
    // ≪블래스트 조그레스≫ (종합 룰 16-31) — 【카운터】
    // =====================================================================

    /// <summary>
    /// 【카운터】 ≪블래스트 조그레스 (A + B)≫: 패의 이 카드 · 지정된 자신의 디지몬 1마리(A 또는 B) · 패의 지정된 카드 1장(다른 쪽)으로 조그레스 진화.
    /// 조그레스 조건(공식 한글)도 만족해야 한다 (16-31-5). 했으면 true (어택 1번에 【카운터】 1개)
    /// </summary>
    private bool TryBlastDna43(PlayerZones defender, CardInstance attacker)
    {
        foreach (var card in defender.Hand.Where(c => CardScripts.For(c)?.BlastDna43 != null).Distinct().OrderByDescending(c => c.Level).ThenByDescending(c => c.DP).ToList())
        {
            var options = BlastDnaOptions43(defender, card);
            if (options.Count == 0) continue;
            var fields = options.Select(o => o.Field).Distinct()
                .OrderByDescending(d => ReferenceEquals(d, _aiAttackTarget43)).ThenByDescending(d => d.IsSuspended).ThenByDescending(d => d.EvolutionSources.Count).ToList();
            var pick = AskDigimon(defender, $"【카운터】 ≪블래스트 조그레스≫ [{attacker.TopCard.Name}]의 어택: 패의 [{card.Name}](으)로 조그레스 진화할 디지몬을 고르세요 (패의 짝 카드와 함께 · 고르지 않으면 쓰지 않음)",
                fields, fields[0], optional: true);
            if (pick == null) continue;
            var partner = options.First(o => ReferenceEquals(o.Field, pick)).Partner;
            if (BlastDnaCore43(defender, card, pick, partner) != null) return true;
        }
        return false;
    }

    /// <summary>지금 어택받는 내 디지몬 (AI 어택 대상 — 블래스트 조그레스 추천용)</summary>
    private CardInstance? _aiAttackTarget43;

    /// <summary>card로 블래스트 조그레스할 수 있는 (필드의 디지몬, 패의 짝 카드)</summary>
    internal List<(CardInstance Field, CardData Partner)> BlastDnaOptions43(PlayerZones owner, CardData card)
    {
        var list = new List<(CardInstance, CardData)>();
        if (CardScripts.For(card)?.BlastDna43 is not { } names || DnaOf(card) is not { } req || !owner.Hand.Contains(card)) return list;
        foreach (var d in owner.BattleArea.Where(d => !IsToken(d.TopCard) && d.TamerProxyOf == null && !EffectRules.IsDigivolveLocked(State, d)))
        {
            foreach (var (mine, other) in new[] { (names.A, names.B), (names.B, names.A) })
            {
                if (!DnaFacets(d, card).Any(f => CardNames.Is(f, mine))) continue;
                var partner = owner.Hand.FirstOrDefault(h => !ReferenceEquals(h, card) && h.Type == CardType.Digimon && CardNames.Is(h, other)
                                                            && (DnaLeft(req, card, d) && req.Right(h) || DnaRight(req, card, d) && req.Left(h)));
                if (partner != null) { list.Add((d, partner)); break; }
            }
        }
        return list;
    }

    /// <summary>
    /// 블래스트 조그레스 처리: 코스트 지불 → 새 디지몬 (종합 룰 8-2-2-1: 진화원이 된 카드의 상태를 인계하지 않는 새로운 1마리 — 액티브, 링크 카드 파기, 부여된 효과 · [턴 1회] 리셋,
    /// 어택 대상이었으면 어택 대상이 없어진다) → 1장 드로우 → [진화 시] ("조그레스 진화하고 있었다면"). 겹치는 순서: 조그레스 조건의 왼쪽이 위. 반환: 결과 메시지 (하지 못했으면 null)
    /// </summary>
    internal string? BlastDnaCore43(PlayerZones owner, CardData card, CardInstance field, CardData partner)
    {
        if (DnaOf(card) is not { } req || !owner.BattleArea.Contains(field) || !owner.Hand.Contains(card) || !owner.Hand.Contains(partner)) return null;
        bool fieldLeft = DnaLeft(req, card, field) && req.Right(partner);
        if (!fieldLeft && !(DnaRight(req, card, field) && req.Left(partner))) return null;
        RunEffects(EffectTrigger.ThisDigimonWouldDnaDigivolve, owner, field, field.TopCard, triggerCard: card);
        if (!owner.BattleArea.Contains(field) || EffectRules.IsDigivolveLocked(State, field) || !owner.Hand.Contains(card) || !owner.Hand.Contains(partner)) return null;
        owner.Hand.Remove(card);
        owner.Hand.Remove(partner);
        AddMemory(owner, -req.Cost);
        if (ReferenceEquals(owner, State.Player)) { State.TotalMemorySpent += req.Cost; State.TotalEvolutionsCount++; }
        TrashLinks(owner, field);   // 8-2-2-1-2 진화하기 전의 디지몬이 가진 링크 카드는 파기
        var fieldStack = new List<CardData> { field.TopCard };
        fieldStack.AddRange(field.EvolutionSources);   // 위 → 아래
        var order = fieldLeft ? fieldStack.Append(partner).ToList() : new[] { partner }.Concat(fieldStack).ToList();
        var nd = new CardInstance(card, field.TurnPlayed) { IsSuspended = false, IsOwnersTurn = field.IsOwnersTurn };
        foreach (var x in order)
        {
            nd.AddBottomSource(x);
            if (field.IsFaceDown(x)) nd.FaceDownSources.Add(x);
        }
        int idx = owner.BattleArea.IndexOf(field);
        owner.BattleArea[idx] = nd;
        if (ReferenceEquals(CurrentAttacker, field)) CurrentAttacker = null;
        string msg = $"【카운터】 ≪블래스트 조그레스≫ [{field.TopCard.Name}] + 패의 [{partner.Name}] → [{card.Name}] (코스트 {req.Cost}, 액티브)";
        EffectLog(owner, msg);
        Diag43.Hit(ref Diag43.BlastDna);
        DrawFor(owner);
        RecalculateFieldBuffs();
        if (owner.BattleArea.Contains(nd))
        {
            var outer = _dnaDigivolving;
            _dnaDigivolving = nd;
            try
            {
                ApplyWhenDigivolvingEffects(owner, nd, card);
                if (owner.BattleArea.Contains(nd))
                    FireForSide(EffectTrigger.AllyDigimonDigivolved, owner, nd);
            }
            finally { _dnaDigivolving = outer; }
        }
        RecalculateFieldBuffs();
        return msg;
    }
}

public partial class GoldfishEngine
{
    /// <summary>"for each memory your opponent has" 셈이 쓰는 메모리 (양쪽 PlayerZones에 채운다 — 판이 바뀌면 새 PlayerZones)</summary>
    private void EnsureMemoryProbes43()
    {
        var p = State.Player;
        var o = State.DummyOpponent;
        if (p.MemoryProbe43 == null) p.MemoryProbe43 = () => MemoryOf(p);
        if (o.MemoryProbe43 == null) o.MemoryProbe43 = () => MemoryOf(o);
    }

    /// <summary>
    /// 자신의 디지몬에 겹쳐져 있는 카드가 위에서부터 파기되었다 (맨 위 카드 파기 · ≪퇴화≫ · ≪아머 퍼지≫) — "When the top stacked card of any of your ~ Digimon is trashed" (BT21-094)
    /// </summary>
    internal void NotifyTopStackedTrashed43(PlayerZones zones, CardInstance inst, CardData? trashedTop)
    {
        if (State.IsGameOver || !zones.BattleArea.Contains(inst)) return;
        RunOrDefer(() =>
        {
            if (State.IsGameOver || !zones.BattleArea.Contains(inst)) return;
            Diag43.Hit(ref Diag43.TopStackedEvent);
            FireForSide(EffectTrigger.AllyTopStackedTrashed43, zones, inst, triggerCard: trashedTop);   // TriggerCard = 파기된 맨 위 카드 (파기되기 전의 디지몬 조건을 본다)
        });
    }

    /// <summary>효과로 옵션을 코스트를 내고 사용 (BT26-090 "you may use 1 Option card ~ from your hand")</summary>
    internal void UseOptionPaid43(PlayerZones owner, CardData option, int cost)
    {
        AddMemory(owner, -cost);
        if (ReferenceEquals(owner, State.Player)) State.TotalMemorySpent += cost;
        EffectLog(owner, $"[효과] 옵션 [{option.Name}]을(를) 코스트 {cost}를 내고 사용");
        ApplyOptionEffect(owner, option, null);
    }
}

/// <summary>v0.43.0 시뮬레이션 집계 — 새 동작이 실제로 일어난 횟수 (sim 하네스가 읽는다)</summary>
public static class Diag43
{
    public static int LinkEffect, AppFusion, AppFusionByEffect, AiAppFusion, BlastDna, TopStackedCost, TopStackedEvent, TrashToSecurityBottom, ForEachStatic, OppMemoryCount, Breeding, InsteadResult, InsteadCompound, TextFix, Misc;

    public static void Hit(ref int counter) => System.Threading.Interlocked.Increment(ref counter);

    public static void Reset() =>
        LinkEffect = AppFusion = AppFusionByEffect = AiAppFusion = BlastDna = TopStackedCost = TopStackedEvent = TrashToSecurityBottom = ForEachStatic = OppMemoryCount = Breeding = InsteadResult = InsteadCompound = TextFix = Misc = 0;

    public static string Report() =>
        $"linkEffect={LinkEffect} appFusion={AppFusion} appFusionByEffect={AppFusionByEffect} aiAppFusion={AiAppFusion} blastDna={BlastDna} topStackedCost={TopStackedCost} topStackedEvent={TopStackedEvent} " +
        $"trashToSecBottom={TrashToSecurityBottom} forEachStatic={ForEachStatic} oppMemoryCount={OppMemoryCount} breeding={Breeding} insteadResult={InsteadResult} insteadCompound={InsteadCompound} misc={Misc}";
}
