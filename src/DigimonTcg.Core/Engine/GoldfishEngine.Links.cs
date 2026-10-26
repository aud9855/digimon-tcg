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
/// 링크 (효과 엔진 7단계): 카드를 디지몬 옆에 가로로 끼워 링크 카드로 둔다 (CardInstance.LinkedCards).
/// - 링크할 수 있는 수는 1장 (≪링크 +1≫마다 +1). 넘치면 가장 먼저 링크한 카드를 트래시
/// - 링크하면 링크 카드의 【링크 시】 효과(OnLinked) → 그 디지몬의 "When this Digimon gets linked"(ThisDigimonLinked)
/// - 링크된 디지몬이 배틀 에어리어를 떠나면 링크 카드는 트래시
/// 링크 조건 · 코스트 · 〈링크 중〉 DP는 영어 데이터에 있으면 그것, 없으면 공식 한글 문구(data/kr — 실행 중에만 읽고 저장하지 않는다)
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>이 디지몬에 링크할 수 있는 카드 수</summary>
    internal static int MaxLinks(CardInstance d) => 1 + (CardScripts.HasKeyword(d, Keyword.LinkPlusOne) ? 1 : 0);

    /// <summary>card를 target에 링크한다 (코스트는 부른 쪽이 낸다). 넘치면 가장 먼저 링크한 카드를 트래시</summary>
    internal void LinkCard(PlayerZones owner, CardInstance target, CardData card)
    {
        if (!owner.BattleArea.Contains(target))
            return;
        while (target.LinkedCards.Count >= MaxLinks(target))
        {
            var old = target.LinkedCards[0];
            target.LinkedCards.RemoveAt(0);
            owner.Trash.Add(old);
            EffectLog(owner, $"[링크] [{target.TopCard.Name}]의 링크 카드 [{old.Name}]을(를) 트래시 (링크할 수 있는 수를 넘음)");
        }
        target.LinkedCards.Add(card);
        RecalculateFieldBuffs();
        EffectLog(owner, $"[링크] [{card.Name}]을(를) [{target.TopCard.Name}]에 링크");
        RunEffects(EffectTrigger.OnLinked, owner, target, card, includeInherited: false);
        if (owner.BattleArea.Contains(target))
            RunEffects(EffectTrigger.ThisDigimonLinked, owner, target, target.TopCard);
        if (owner.BattleArea.Contains(target))
            FireForSide(EffectTrigger.AllyDigimonLinked, owner, target, triggerCard: card);   // TriggerCard = 링크한 카드 (v0.31.0)
        RecalculateFieldBuffs();
    }

    /// <summary>배틀 에어리어를 떠나는 디지몬의 링크 카드를 트래시</summary>
    private static void TrashLinks(PlayerZones zones, CardInstance inst)
    {
        if (inst.LinkedCards.Count == 0) return;
        zones.Trash.AddRange(inst.LinkedCards);
        inst.LinkedCards.Clear();
    }

    /// <summary>card를 target에 링크할 수 있는지 (링크 조건 — 모르면 null)</summary>
    internal static bool? MeetsLinkRequirement(CardData card, CardInstance target)
    {
        if (CardScripts.For(card)?.LinkRequirement is { } req) return req(target);
        return KoreanLink(card) is { Requirement: { } koReq } ? koReq(target) : null;
    }

    /// <summary>패에서 링크할 때의 코스트 (모르면 null)</summary>
    internal static int? LinkCostOf(CardData card)
    {
        if (CardScripts.For(card) is { LinkRequirement: not null } s) return s.LinkCost;
        return KoreanLink(card)?.Cost;
    }

    /// <summary>패에서 링크할 때의 코스트 — 화면 표시용 (모르면 null)</summary>
    public int? GetLinkCost(CardData card) => LinkCostOf(card);

    /// <summary>〈링크 중〉 이 디지몬의 DP +N (공식 한글 문구가 있을 때만)</summary>
    internal static int LinkDp(CardData card) => KoreanLink(card)?.Dp ?? 0;

    /// <summary>패의 card를 링크할 수 있는 자신의 디지몬 (플레이어 메인 페이즈 — 화면이 쓴다)</summary>
    public IReadOnlyList<CardInstance> GetLinkTargets(CardData card)
    {
        if (!State.Player.Hand.Contains(card) || LinkCostOf(card) is null)
            return Array.Empty<CardInstance>();
        return State.Player.BattleArea.Where(d => MeetsLinkRequirement(card, d) == true).ToList();
    }

    /// <summary>플레이어: 패의 card를 링크 코스트를 내고 target에 링크한다 (메인 페이즈). 메모리가 0 미만이 되면 턴이 끝난다</summary>
    public ActionResult LinkFromHand(CardData card, CardInstance target)
    {
        if (State.IsGameOver || State.CurrentPhase != Phase.Main || State.PendingBlock != null || State.BlitzAttacker != null)
            return ActionResult.Fail("지금은 링크할 수 없습니다.");
        if (!State.Player.Hand.Contains(card) || !State.Player.BattleArea.Contains(target))
            return ActionResult.Fail("링크할 카드나 디지몬이 없습니다.");
        if (LinkCostOf(card) is not int cost)
            return ActionResult.Fail($"[{card.Name}]의 링크 조건을 알 수 없습니다 (공식 한글 데이터가 필요합니다).");
        if (MeetsLinkRequirement(card, target) != true)
            return ActionResult.Fail($"[{target.TopCard.Name}]에는 [{card.Name}]을(를) 링크할 수 없습니다 (링크 조건).");
        State.Player.Hand.Remove(card);
        State.MemoryGauge -= cost;
        State.TotalMemorySpent += cost;
        Log($"링크: [{card.Name}] → [{target.TopCard.Name}] (코스트 {cost})");
        LinkCard(State.Player, target, card);
        CheckTurnEndAndAutoReset();
        return ActionResult.Success($"[{card.Name}]을(를) [{target.TopCard.Name}]에 링크했습니다 (코스트 {cost}).");
    }

    // ---- 공식 한글 문구의 〈링크〉 정보 (실행 중에만 — 저장하지 않는다) ----

    private sealed record KoreanLinkInfo(Func<CardInstance, bool>? Requirement, int? Cost, int Dp);

    private static readonly ConcurrentDictionary<string, KoreanLinkInfo?> KoreanLinkCache = new(StringComparer.Ordinal);

    private static KoreanLinkInfo? KoreanLink(CardData card) => KoreanLinkCache.GetOrAdd(card.CardId, _ => ParseKoreanLink(card));

    /// <summary>"〈링크〉특징 「어플몬」 : 코스트 2 … 〈링크 중〉이 디지몬의 DP +3000" · "〈링크〉Lv.3 이상 : 코스트 2"</summary>
    private static KoreanLinkInfo? ParseKoreanLink(CardData card)
    {
        var ko = KoreanCardData.Get(card.CardId);
        if (ko == null) return null;
        string text = $"{ko.MainText} {ko.SourceText}";
        if (!text.Contains("〈링크〉", StringComparison.Ordinal)) return null;

        int dp = 0;
        var dpm = Regex.Match(text, @"〈링크 중〉[^〈【]*?DP\s*[를을]?\s*\+\s*(?<n>\d+)");
        if (dpm.Success) dp = int.Parse(dpm.Groups["n"].Value);

        var req = Regex.Match(text, @"〈링크〉\s*(?<req>[^:：(（]+?)\s*[:：]\s*코스트\s*(?<cost>\d+)");
        if (!req.Success) return new KoreanLinkInfo(null, null, dp);
        string r = req.Groups["req"].Value;
        var conds = new List<Func<CardInstance, bool>>();
        var traits = Regex.Match(r, @"특징\s*(?:으로\s*)?(?<x>(?:「[^」]+」\s*/?\s*)+)");
        if (traits.Success)
        {
            var xs = Regex.Matches(traits.Groups["x"].Value, @"「([^」]+)」").Select(m => m.Groups[1].Value).ToList();
            conds.Add(d => KoreanCardData.DisplayTraits(d.TopCard).Any(t => xs.Any(x => t.Contains(x, StringComparison.Ordinal))));
        }
        var names = Regex.Match(r, @"명칭에\s*(?<x>(?:「[^」]+」\s*/?\s*)+)");
        if (names.Success)
        {
            var xs = Regex.Matches(names.Groups["x"].Value, @"「([^」]+)」").Select(m => m.Groups[1].Value).ToList();
            conds.Add(d => xs.Any(x => KoreanCardData.DisplayName(d.TopCard).Contains(x, StringComparison.Ordinal)));
        }
        var lv = Regex.Match(r, @"Lv\.\s*(?<n>\d+)\s*(?<dir>이상|이하)?");
        if (lv.Success)
        {
            int n = int.Parse(lv.Groups["n"].Value); string dir = lv.Groups["dir"].Value;
            conds.Add(d => dir == "이상" ? d.TopCard.Level >= n : dir == "이하" ? d.TopCard.Level <= n : d.TopCard.Level == n);
        }
        Func<CardInstance, bool>? requirement = conds.Count > 0 ? d => conds.All(c => c(d)) : null;
        return new KoreanLinkInfo(requirement, requirement != null ? int.Parse(req.Groups["cost"].Value) : null, dp);
    }
}
