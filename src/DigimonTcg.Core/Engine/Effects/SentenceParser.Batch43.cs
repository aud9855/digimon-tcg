using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.43.0 미지원 문장 17차 묶음: 링크 카드의 링크 효과 (링크되어 있는 동안) · 어플 합체 · ≪블래스트 조그레스≫ · 겹쳐진 맨 위 카드 · "for each" 지속 효과 · 상대 메모리 셈 ·
/// [육성] · "대신" 등. 원래 규칙(1~16차)이 읽지 못한 절 · 문장 · 동작만 17차 규칙으로 다시 읽는다 (엔진: GoldfishEngine.Batch43.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _clauseDepth43;
    [ThreadStatic] private static int _fallbackDepth43;

    // =====================================================================
    // 0. 링크 카드의 링크 효과 (종합 룰 2-3-11-4-6) — 영어 데이터는 링크 카드의 하단(링크 효과)을 진화원 효과 칸에 둔다
    // =====================================================================

    /// <summary>
    /// 링크 카드인지 (디지몬): 영어 하단에 "Link Requirements"가 있거나, 특징이 "~ (App Name)"인 Lv.3 이상 어플몬, 또는 공식 한글 문구에 〈링크〉가 있는 카드.
    /// 링크 카드의 하단 문장은 진화원 효과가 아니라 링크 효과다 — 링크되어 있는 동안 그 디지몬의 효과로 발휘하고, 진화원에 있을 때는 발휘하지 않는다
    /// </summary>
    internal static bool IsLinkCard43(CardData card) =>
        card.Type is CardType.Digimon or CardType.Option && !card.IsDual && (
            (card.InheritedEffectText ?? "").Contains("Link Requirements", StringComparison.OrdinalIgnoreCase)
            || card.Type == CardType.Digimon && card.Level >= 3 && card.Traits.Any(t => t.Contains("(App Name)", StringComparison.OrdinalIgnoreCase))
            || card.Type == CardType.Option && card.Traits.Any(t => t.Equals("Plug-In", StringComparison.OrdinalIgnoreCase))   // 링크하는 옵션 (ST22-08 「공격 플러그인 V」)
            || KoreanCardData.Get(card.CardId) is { } ko && $"{ko.MainText} {ko.SourceText}".Contains("〈링크〉", StringComparison.Ordinal));

    /// <summary>링크 카드의 진화원 효과로 등록된 것을 링크 효과(EffectSource.Link43)로 옮긴다 (【링크 시】는 메인 효과 OnLinked 그대로)</summary>
    internal static void RetagLinkEffects43(CardScript script, int linkSecurityAttack)
    {
        for (int i = 0; i < script.Effects.Count; i++)
            if (script.Effects[i].Source == EffectSource.Inherited)
                script.Effects[i] = script.Effects[i] with { Source = EffectSource.Link43 };
        for (int i = 0; i < script.Statics.Count; i++)
            if (script.Statics[i].Source == EffectSource.Inherited)
                script.Statics[i] = script.Statics[i] with { Source = EffectSource.Link43 };
        foreach (var k in script.InheritedKeywords) script.LinkKeywords43.Add(k);
        script.InheritedKeywords.Clear();
        script.LinkSecurityAttack43 = linkSecurityAttack;
    }

    // =====================================================================
    // 1. 절 — 원래 규칙이 읽지 못한 절만 (TryParse)
    // =====================================================================

    private static string? TryParse43(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string fixedClause = CardTextFix43(rawClause, inherited, card);
        if (!ReferenceEquals(fixedClause, rawClause))
        {
            if (TryParse43Core(fixedClause, inherited, card, null) is { } fixErr) return TryParse43Core(rawClause, inherited, card, def) == null ? null : fixErr;
            TryParse43Core(fixedClause, inherited, card, def);
            return null;
        }
        return TryParse43Core(rawClause, inherited, card, def);
    }

    private static string? TryParse43Core(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse42(rawClause, inherited, card, def);
        if (err == null || _clauseDepth43 > 0) return err;
        _clauseDepth43++;
        try
        {
            if (BlastDnaClause43(rawClause, inherited, card, def)) return null;
            if (ExtraClause43(rawClause, inherited, card, def)) return null;
            foreach (var alt in ClauseRewrites43(rawClause, card))
            {
                if (alt == rawClause) continue;
                if (TryParse42(alt, inherited, card, null) != null) continue;
                if (def != null) TryParse42(alt, inherited, card, def);
                return null;
            }
            return err;
        }
        finally { _clauseDepth43--; }
    }

    /// <summary>
    /// 영어 데이터가 깨진 링크 카드의 하단 문장 — 공식 카드 문구에 맞춘 영어 (카드 번호 · 칸 · 원문이 정확히 같을 때만):
    ///  - BT23-009 · ST22-08: 【자신의 턴 종료 시】가 빠졌다 (공식 "【자신의 턴 종료 시】[턴 1회]이 디지몬으로 (플레이어에게) 어택할 수 있다")
    ///  - EX10-024: 비용 뒤 문장이 빠졌다 (공식 "【어택 시】이 디지몬의 링크 카드 1장을 파기하는 것으로, 상대의 디지몬 1마리를 《퇴화 1》")
    ///  - BT23-052: 키워드 이름이 빠졌다 (공식 "【링크 시】상대의 턴 종료까지, 이 디지몬은 《재기동》과 《블로커》를 얻는다")
    ///  - BT21-041: 【자신의 턴】이 빠졌다 (공식 "【자신의 턴】상대의 시큐리티 디지몬 전부를 DP -3000")
    /// </summary>
    internal static string CardTextFix43(string clause, bool inherited, CardData card)
    {
        if (!inherited) return clause;
        string t = clause.Trim();
        string? fix = card.CardId switch
        {
            "BT23-009" when t == "[Once Per Turn] This Digimon may attack a player." => "[End of Your Turn] [Once Per Turn] This Digimon may attack a player.",
            "ST22-08" when t == "[Once Per Turn] This Digimon may attack." => "[End of Your Turn] [Once Per Turn] This Digimon may attack.",
            "EX10-024" when t == "[When Attacking] By trashing 1 of this Digimon's link cards," => "[When Attacking] By trashing 1 of this Digimon's link cards, ＜De-Digivolve 1＞ 1 of your opponent's Digimon.",
            "BT23-052" when t == "This Digimon gains and until your opponent's turn ends." => "This Digimon gains ＜Reboot＞ and ＜Blocker＞ until your opponent's turn ends.",
            "BT21-041" when t == "All of your opponent's Security Digimon get -3000 DP." => "[Your Turn] All of your opponent's Security Digimon get -3000 DP.",
            _ => null,
        };
        return fix ?? clause;
    }

    /// <summary>
    /// [패]【카운터】≪블래스트 조그레스 (A + B)≫ (종합 룰 16-31 — 공식 "지정된 자신의 디지몬 1마리와 패의 지정된 카드 1장으로, 이 카드로 조그레스 진화할 수 있다"):
    /// 엔진의 카운터 타이밍 (GoldfishEngine.Batch43 TryBlastDna43). 조그레스 조건 · 코스트는 그 카드의 것 (공식 한글)
    /// </summary>
    private static bool BlastDnaClause43(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        if (inherited || card.Type != CardType.Digimon) return false;
        var m = Regex.Match(rawClause.Trim(), @"^\[Hand\]\s*\[Counter\]\s*＜Blast DNA Digivolve\s*\(\s*\[(?<a>[^\]]+)\]\s*\+\s*\[(?<b>[^\]]+)\]\s*\)＞\s*(?:\([^)]*\))?\.?$", I);
        if (!m.Success) return false;
        if (def != null) def.Script41.BlastDna43 = (m.Groups["a"].Value.Trim(), m.Groups["b"].Value.Trim());
        return true;
    }

    /// <summary>절 전체의 같은 뜻 다른 표기 (렌더러도 쓴다) — 원래 규칙으로 읽혀야 쓴다</summary>
    internal static IEnumerable<string> ClauseRewrites43(string rawClause, CardData card)
    {
        string c = rawClause.Trim();
        var lead = Regex.Match(c, @"^(?<labels>(?:Inherited Effect\s*|Security Effect\s*)?(?:\[[^\]]+\]\s*)*)(?<body>.*)$", RegexOptions.Singleline);
        string labels = lead.Groups["labels"].Value, body = lead.Groups["body"].Value.Trim();
        var sentences = Regex.Split(body, @"(?<=\.)\s+(?=[A-Z＜])");
        bool changed = false;
        var outs = new List<string>();
        foreach (var s in sentences)
        {
            string t = s.Trim();
            string end = t.EndsWith(".") ? "." : "";
            string core = t.TrimEnd('.');
            string? alt = Rewrites43(core).LastOrDefault();
            if (alt != null && alt != core) { changed = true; outs.Add(alt + end); }
            else outs.Add(t);
        }
        if (changed) yield return labels + string.Join(" ", outs);
    }

    // =====================================================================
    // 2. 문장 — 원래 규칙이 읽지 못한 문장만 (ParseStep 끝)
    // =====================================================================

    private static (Step? Step, string? Error)? StepFallback43(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth43 >= 3) return null;
        _fallbackDepth43++;
        try
        {
            if (StepExtra43(sentence, card, triggers) is { } ex) return (ex, null);
            string s = Regex.Replace(sentence.Trim().TrimEnd('.').Trim(), @"^Then,?\s+", "", I);
            foreach (var alt in Rewrites43(s))
            {
                if (alt == s) continue;
                var r = ParseStep(alt, card, triggers);
                if (r.Error == null && r.Step != null) return (r.Step with { Text = sentence.Trim() }, null);
            }
            return null;
        }
        finally { _fallbackDepth43--; }
    }

    /// <summary>같은 뜻의 다른 표기 (렌더러도 쓴다). 여러 규칙을 차례로 겹쳐 적용한 결과도 낸다</summary>
    internal static IEnumerable<string> Rewrites43(string s)
    {
        string acc = s;
        foreach (var f in RewriteRules43)
        {
            string next = f(acc);
            if (next != acc)
            {
                yield return next;
                acc = next;
            }
        }
    }

    private static readonly Func<string, string>[] RewriteRules43 =
    {
        // 원문 오타 "1 of opponent's Digimon" (BT9-041) → "1 of your opponent's Digimon"
        t => Regex.Replace(t, @"\b(?<n>\d+) of opponent's (?=Digimon\b)", "${n} of your opponent's ", I),
        // "~ may digivolve into a Digimon card in your hand with the [Armor Form] trait for its digivolution cost" (BT8-110) → 조건을 "in the hand" 앞으로
        t => Regex.Replace(t, @"^(?<a>.+? may digivolve into (?:an? |1 )?Digimon card) in (?:your|the) hand (?<w>with (?:the )?\[[^\]]+\](?: or \[[^\]]+\])* traits?|with \[[^\]]+\] in (?:its|their) names?)(?<rest> for .+)?$", "${a} ${w} in the hand${rest}", I),
        // "If you have a Digimon in play with 2 or more colors, or with 2 or more colors in its digivolution cards, ~ instead" (BT8-096 · BT8-100 — 공식 "2색 이상인 디지몬, 또는 진화원에 2색 이상 있는 디지몬이 있다면")
        t => Regex.Replace(t, @"^If you have a Digimon in play with 2 or more colors, or with 2 or more colors in its digivolution cards, ", "If you have a Digimon with 2 or more colors or with 2 or more colors in its digivolution cards, ", I),
    };

    // =====================================================================
    // 3. 동작 — 원래 규칙이 읽지 못한 동작만 (ParseAction 끝)
    // =====================================================================

    private static (Action<EffectContext>? Act, string? Error)? ParseAction43(string raw, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth43 >= 3) return null;
        _fallbackDepth43++;
        try
        {
            string s = raw.Trim().TrimEnd('.');
            string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
            if (AppFuseAction43(s, card, triggers, label) is { } af) return af;
            if (ActionExtra43(s, card, triggers, label) is { } ex) return ex;
            foreach (var alt in Rewrites43(s))
            {
                if (alt == s) continue;
                var r = ParseAction(alt, card, triggers);
                if (r.Error == null && r.Act != null) return r;
            }
            return null;
        }
        finally { _fallbackDepth43--; }
    }

    // ---- 효과로 어플 합체: "1 of your Digimon may app fuse into a Digimon card in the hand" (BT21-084 · BT25-089 · EX10-062 …) ·
    //      "~ into a Digimon card with the [System], [Life] or [Transmutation (App Name)] trait in the trash" (BT24-087)
    //      종합 룰 8-4-2-2: 어플 합체를 하는 효과로만 어플 합체한다. 조건 · 코스트는 그 카드의 것 (공식 한글 — 모르면 하지 않는다)

    private static (Action<EffectContext>? Act, string? Error)? AppFuseAction43(string s, CardData card, List<EffectTrigger> triggers, string label)
    {
        var m = Regex.Match(s, @"^(?<who>1 of your (?<spec>[^,]*?)Digimon(?<with> with [^,]+?)?) may app fuse into (?<pick>.+?) in (?:the|your) (?<from>hand|trash)$", I);
        if (!m.Success) return null;
        var hostSpec = TargetSpec.Parse("all of your " + m.Groups["spec"].Value + "Digimon" + m.Groups["with"].Value);
        string pickText = Regex.Replace(m.Groups["pick"].Value, @"\s*\(App Name\)", "", I);
        var pick = ParseCardPick("1 " + pickText) ?? ParseCardPick(pickText) ?? ParseNamePick("1 " + pickText);
        if (hostSpec == null || pick == null) return (null, $"미지원 어플 합체 대상: {s}");
        var match = pick.Value.Match;
        bool fromTrash = m.Groups["from"].Value.Equals("trash", StringComparison.OrdinalIgnoreCase);
        return (c =>
        {
            var zone = fromTrash ? c.Owner.Trash : c.Owner.Hand;
            var options = zone.Where(x => x.Type == CardType.Digimon && match(x)).Distinct()
                .SelectMany(x => c.Engine.AppFusionTargets43(c.Owner, x).Where(t => hostSpec.Filters.All(f => f(t.Host))).Select(t => (Card: x, t.Host, t.Link)))
                .Where(o => (GoldfishEngine.GetAppFusionCost(o.Card) ?? 99) <= Math.Max(0, c.OwnerMemory) || !c.OwnerIsPlayer)
                .OrderByDescending(o => o.Card.Level).ThenByDescending(o => o.Host.EvolutionSources.Count).ToList();
            if (options.Count == 0 || !c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 어플 합체할까요? (후보: {string.Join(", ", options.Select(o => o.Card.Name).Distinct())})", recommended: true))
            {
                c.LastStepResolved = false;
                Emit(c, label, options.Count == 0 ? "어플 합체할 수 있는 카드 · 디지몬이 없습니다" : "어플 합체하지 않았습니다");
                return;
            }
            var cards = options.Select(o => o.Card).Distinct().ToList();
            int ci = c.Engine.ChooseCardIndexes(c.Owner, fromTrash ? CardChoiceZone.Trash : CardChoiceZone.Hand, cards, 1, upTo: false, $"[{c.Card.Name}] 어플 합체할 카드를 고르세요.").FirstOrDefault();
            var chosenCard = cards[Math.Clamp(ci, 0, cards.Count - 1)];
            var hosts = options.Where(o => ReferenceEquals(o.Card, chosenCard)).Select(o => o.Host).Distinct().ToList();
            var host = c.Engine.ChooseTargets(c.Owner, hosts, 1, $"[{chosenCard.Name}](으)로 어플 합체할 디지몬을 고르세요.", optional: false, preselected: hosts[0]).FirstOrDefault() ?? hosts[0];
            var link = options.First(o => ReferenceEquals(o.Card, chosenCard) && ReferenceEquals(o.Host, host)).Link;
            var msg = c.Engine.AppFuseCore43(c.Owner, chosenCard, host, link, GoldfishEngine.GetAppFusionCost(chosenCard) ?? 0, zone, byEffect: true);
            c.LastStepResolved = msg != null && c.Owner.BattleArea.Contains(host) && ReferenceEquals(host.TopCard, chosenCard);
            c.LastTargets = c.LastStepResolved ? new List<CardInstance> { host } : new List<CardInstance>();
            Emit(c, label, msg ?? "어플 합체하지 못했습니다");
        }, null);
    }
}
