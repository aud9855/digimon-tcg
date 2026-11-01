using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.58.0 미지원 문장 32차 묶음: 원래 규칙(1~31차)이 읽지 못한 절만 32차 규칙으로 다시 읽는다.
///  - ≪디지버스트 N까지≫ + "for each card trashed by this card's effect" (BT7-040)
///  - 이 효과로 소멸한 디지몬의 Lv. 1마다 (EX6-058) · 이 효과로 되돌린 1장마다 등장 코스트 상한 +1 (EX13-053)
///  - 패의 카드를 공개하고 덱 위에 (P-077 · BT7-043)
///  - 어택 대상이 바뀌지 않는다 (EX7-022 지속 · BT26-021 이 턴) · 어택 중에는 상대의 효과를 받지 않는다 (BT27-103) · 특징을 얻는다 (EX7-010)
/// 31차(TryParse57)는 읽지 못한 절을 늘 문을 연 채로 한 번 더 읽어 도감 계산이 느려졌다 — 32차는 바꿔 쓸 것이 있거나 32차 표현(Hint58)이 있을 때만 다시 읽는다.
/// 렌더러도 같은 표 (KoreanEffectRenderer.Batch58.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _clauseDepth58;
    [ThreadStatic] private static int _gate58;
    internal static bool Gate58 => _gate58 > 0;

    private static string? TryParse58(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse57(rawClause, inherited, card, def);
        if (err == null || _clauseDepth58 > 0) return err;
        // 32차 규칙에 걸릴 것이 없으면 다시 읽지 않는다 (같은 결과 — 시간만 든다)
        string t = Normalize58(rawClause);
        string alt = ClauseRewrite58(t);
        bool special = ClauseSpecialRe58.IsMatch(t);
        if (!special && alt == t && !Hint58.IsMatch(t)) return err;
        _clauseDepth58++;
        _gate58++;
        int before = def?.Script41.Effects.Count ?? 0;
        try
        {
            if (special) return ClauseSpecial58(t, inherited, card, def) ? null : err;
            if (TryParse57(alt, inherited, card, null) != null) return err;
            if (def != null) TryParse57(alt, inherited, card, def);
            return null;
        }
        finally
        {
            _clauseDepth58--;
            _gate58--;
            if (def != null) WrapGate58(def, before);
        }
    }

    private static string Normalize58(string rawClause) => rawClause.Replace(' ', ' ').Replace('’', '\'').Trim();

    /// <summary>32차 규칙으로 등록한 시점 효과는 처리할 때도 문을 연다</summary>
    private static void WrapGate58(CardScriptDefinition def, int from)
    {
        var effects = def.Script41.Effects;
        for (int i = from; i < effects.Count; i++)
        {
            var inner = effects[i].Resolve;
            effects[i] = effects[i] with { Resolve = c => { _gate58++; try { inner(c); } finally { _gate58--; } } };
        }
    }

    /// <summary>32차의 동작 · 문장이 쓰이는 절 (바꿔 쓰지 않아도 문을 열고 다시 읽는다)</summary>
    internal static readonly Regex Hint58 = new(@"For each level of the Digimon deleted by this effect|attack target can't (?:change|be changed|be switched|switch) for the turn|from your hand and place it on top of your deck|⟦cap58⟧|for each of that Digimon's colou?rs|At the end of the battle, if you don't have (?:any )?Digimon|or up to \d+ [^.]+ from your trash without paying their (?:memory )?costs", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>같은 뜻 다른 표기 — 절 전체에 차례로 (렌더러도 쓴다)</summary>
    internal static readonly Func<string, string>[] ClauseRules58 =
    {
        // BT7-043 "You may reveal 1 green Digimon card from your hand. If you do, place it on top of your deck." (P-077 "~ and place it on top of your deck"과 같다)
        s => Regex.Replace(s, @"\bYou may reveal (?<p>1 [^.]+?) from your hand\. If you do, place it on top of your deck\.", "You may reveal ${p} from your hand and place it on top of your deck.", RegexOptions.IgnoreCase),
        // EX13-053 "You may return up to 3 ~ from your trash to the top of the deck. Then, delete 1 of your opponent's Digimon with a play cost of 3 or less. For each one this effect returned, add 1 to this effect's play cost maximum."
        //  — 되돌린 매수를 알아야 하므로 한 문장으로 ("up to"라 0장도 된다 — "You may"를 떼도 같다)
        s => Regex.Replace(s, @"\bYou may return (?<a>up to \d+ [^.]+?) from your trash to the top of (?:the|your) deck\. Then, delete (?<d>1 of your opponent's Digimon with a play cost of \d+ or less)\. For each one this effect returned, add 1 to this effect's play cost maximum\.",
            "Return ${a} from your trash to the top of the deck ⟦cap58⟧ delete ${d}.", RegexOptions.IgnoreCase),
        // BT7-040 선택지 "~ for each card trashed by this card's effect" (≪디지버스트≫로 파기한 매수)
        s => Regex.Replace(s, @"\bfor each card trashed by this card's effect\b", "for each card trashed by this effect", RegexOptions.IgnoreCase),
        // BT9-009 "[Your Turn] Add 1000 to the maximum DP you can choose with DP-based delete effects." (= BT12-001 "DP-based deletion effects")
        s => Regex.Replace(s, @"\bwith DP-based delete effects\.", "with DP-based deletion effects.", RegexOptions.IgnoreCase),
        // P-142 "[On Deletion] When this Digimon is deleted other than in battle, ~" — 【소멸 시】 배틀 이외로 소멸했다면
        s => Regex.Replace(s, @"^\[On Deletion\]\s*When this Digimon is deleted other than in battle, ", "[On Deletion] If this Digimon wasn't deleted in battle, ", RegexOptions.IgnoreCase),
    };

    internal static string ClauseRewrite58(string rawClause)
    {
        string s = rawClause;
        foreach (var f in ClauseRules58) s = f(s);
        return s;
    }

    // =====================================================================
    // 절 하나로 읽는 지속 효과
    // =====================================================================

    internal static readonly Regex TargetLockAllRe58 = new(@"^(?<q>\[(?:Your Turn|All Turns|Opponent's Turn)\])\s*All of your (?<x>Digimon with (?:the )?\[[^\]]+\](?: or \[[^\]]+\])* trait)(?: trait)? can't have their attack targets? (?:switched|changed)\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex DuringAttacksImmuneRe58 = new(@"^(?<q>\[All Turns\])\s*During attacks, your opponent's effects don't affect this Digimon\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex GainTraitRe58 = new(@"^(?:Inherited Effect\s*)?(?<q>\[(?:Your Turn|All Turns|Opponent's Turn)\])\s*This Digimon gains the (?<x>\[[^\]]+\]) trait\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ClauseSpecialRe58 = new($"{TargetLockAllRe58}|{DuringAttacksImmuneRe58}|{GainTraitRe58}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Func<StaticContext, bool> Qualifier58(string q) => q.ToLowerInvariant() switch
    {
        "[your turn]" => c => c.OwnersTurn,
        "[opponent's turn]" => c => !c.OwnersTurn,
        _ => _ => true,
    };

    /// <summary>절 하나로 읽는 규칙 — 읽었으면 true (def가 있으면 등록)</summary>
    private static bool ClauseSpecial58(string t, bool inherited, CardData card, CardScriptDefinition? def)
    {
        if (card.Type != CardType.Digimon) return false;
        var source = inherited ? EffectSource.Inherited : EffectSource.Main;
        Match m;
        // EX7-022 "[Your Turn] All of your Digimon with the [NSp] trait trait can't have their attack targets switched." — 자신의 그 디지몬 전부 (≪돌진≫ · 효과 모두)
        if ((m = TargetLockAllRe58.Match(t)).Success)
        {
            if (TargetSpec.Parse("all of your " + m.Groups["x"].Value) is not { Opponent: false, AnySide: false, ContextFilters.Count: 0 } spec || inherited) return false;
            var q = Qualifier58(m.Groups["q"].Value);
            var filters = spec.Filters.ToList();
            def?.Static(StaticStat.AllyKeyword, source, c => q(c) ? 1 : 0, Keyword.AttackTargetLocked, subject: d => filters.All(f => f(d)));
            return true;
        }
        // BT27-103 "[All Turns] During attacks, your opponent's effects don't affect this Digimon." — 어택 중(어느 쪽이든)에는 상대의 효과를 받지 않는다
        if (DuringAttacksImmuneRe58.IsMatch(t))
        {
            def?.Static(StaticStat.Keyword, source, _ => 1, Keyword.UnaffectedByOpponentDuringAttacks58);
            return true;
        }
        // EX7-010 "[Your Turn] This Digimon gains the [Three Musketeers] trait."
        if ((m = GainTraitRe58.Match(t)).Success)
        {
            var q = Qualifier58(m.Groups["q"].Value);
            string trait = TargetSpec.Names(m.Groups["x"].Value)[0];
            GoldfishEngine.ExtraTraitScripts58 = true;   // 읽기만 해도 켠다 (스크립트를 만들기 전에 지속 효과를 계산할 수 있다)
            if (def != null)
            {
                int from = def.StaticCount;
                def.Static(StaticStat.ExtraTrait58, source, c => q(c) ? 1 : 0);
                def.PatchStatics(from, e => e with { Trait58 = trait });
            }
            return true;
        }
        return false;
    }

    // =====================================================================
    // 동작 · 문장 (문이 열려 있을 때만)
    // =====================================================================

    private static (Action<EffectContext>? Act, string? Error)? ParseAction58(string s, CardData card, List<EffectTrigger> triggers)
    {
        if (!Gate58) return null;
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        Match m;
        // P-077 · BT7-043 "Reveal 1 purple card from your hand and place it on top of your deck" — 공개하고 덱 위에
        if ((m = Regex.Match(s, @"^reveal (?<p>1 .+?) from your hand and place it on (?:the )?top of your deck$", I)).Success
            && ParseCardPick(m.Groups["p"].Value) is { Count: 1 } pick)
        {
            var match = pick.Match;
            return (c =>
            {
                var cands = c.Owner.Hand.Where(match).ToList();
                if (cands.Count == 0) { c.LastStepResolved = false; Emit(c, label, "공개할 카드가 패에 없습니다"); return; }
                // AI: 레벨이 낮은 · 같은 카드가 여러 장인 것부터 (다음 드로우로 돌아온다)
                var order = cands.OrderBy(x => x.Type == CardType.Digimon ? x.Level : 0).ThenByDescending(x => c.Owner.Hand.Count(h => h.CardId == x.CardId)).ToList();
                int i = order.Count == 1 ? 0 : c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Hand, order, 1, upTo: false, $"[{c.Card.Name}] 공개하고 덱 위에 놓을 패 1장을 고르세요.").FirstOrDefault();
                var x = order[Math.Clamp(i, 0, order.Count - 1)];
                if (!c.Owner.Hand.Remove(x)) { c.LastStepResolved = false; return; }
                c.Owner.MainDeck.Insert(0, x);
                c.LastCard = x;
                Diag46.Hit("revealHandToDeckTop58");
                Emit(c, label, $"패의 [{x.Name}]을(를) 공개하고 덱 위에 놓았습니다");
            }, null);
        }
        // BT26-021 "1 of your [TS] trait Digimon's attack target can't change for the turn" — 고른 디지몬은 이 턴 동안 어택 대상이 바뀌지 않는다
        if ((m = Regex.Match(s, @"^(?<t>1 of your .+?)'s attack target can't (?:change|be changed|be switched|switch) for the turn$", I)).Success)
        {
            string tp = Regex.Replace(m.Groups["t"].Value, @"^1 of your (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) trait Digimon$", "1 of your Digimon with the ${x} trait", I);
            if (TargetSpec.Parse(tp) is not { Opponent: false, AnySide: false, Count: 1 } spec) return null;
            return (c =>
            {
                var picked = Pick(spec, c, false).Where(d => c.Owner.BattleArea.Contains(d)).ToList();
                if (picked.Count == 0) { c.LastStepResolved = false; Emit(c, label, "대상이 없습니다"); return; }
                foreach (var d in picked) d.TimedKeywords.Add((Keyword.AttackTargetLocked, c.TurnSequence));
                c.RecalculateFieldBuffs();
                Diag46.Hit("attackTargetLockTurn58");
                Emit(c, label, $"[{picked[0].TopCard.Name}]은(는) 이 턴 동안 어택 대상이 바뀌지 않습니다");
            }, null);
        }
        // ST17-13 "Trash the top digivolution card of 1 of your opponent's Digimon for each of that Digimon's colors" — 고른 디지몬의 색 1색마다 위에서부터 1장
        if ((m = Regex.Match(s, @"^trash the top digivolution card of (?<t>1 of your opponent's Digimon(?: [^,]+?)?) for each of that Digimon's colou?rs$", I)).Success)
        {
            if (TargetSpec.Parse(m.Groups["t"].Value) is not { Opponent: true, AnySide: false, Count: 1 } spec) return null;
            return (c =>
            {
                var d = Pick(spec, c, triggers.Contains(EffectTrigger.Security)).FirstOrDefault(x => c.Enemy.BattleArea.Contains(x));
                if (d == null) { c.LastStepResolved = false; Emit(c, label, "대상이 없습니다"); return; }
                int k = Math.Max(1, d.Colors.Count);
                var gone = c.Engine.TrashTopSources(c.Enemy, d, k);
                Diag46.Hit("trashTopPerColor58");
                Emit(c, label, gone.Count > 0 ? $"[{d.TopCard.Name}]의 진화원 {gone.Count}장 파기 (색 {k}색)" : $"[{d.TopCard.Name}]의 진화원을 파기하지 못했습니다");
            }, null);
        }
        // BT17-047 [시큐리티] "At the end of the battle, if you don't have Digimon, play this card without paying the cost."
        //  — 시큐리티 디지몬의 배틀로 자신의 디지몬 수는 바뀌지 않으므로 지금 본다 (배틀 뒤 등장은 원래 규칙 "At the end of the battle, play this card ~"와 같다)
        if (card.Type == CardType.Digimon && triggers.Contains(EffectTrigger.Security)
            && Regex.IsMatch(s, @"^At the end of the battle, if you don't have (?:any )?Digimon(?: in play)?, play this card without paying (?:the|its) (?:memory )?cost$", I))
            return (c =>
            {
                if (c.Owner.BattleArea.Any(d => d.TamerProxyOf == null)) { c.LastStepResolved = false; Emit(c, label, "자신의 디지몬이 있어 등장하지 않습니다"); return; }
                c.PlayAfterBattle = true;
                Diag46.Hit("playAfterBattleIfNoDigimon58");
                Emit(c, label, "배틀이 끝나면 이 카드를 코스트 없이 등장");
            }, null);
        return null;
    }

    /// <summary>문장 (ParseStep 끝)</summary>
    private static (Step? Step, string? Error)? StepFallback58(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        if (!Gate58) return null;
        string s = Regex.Replace(sentence.Trim().TrimEnd('.').Trim(), @"^Then,?\s+", "", I);
        Match m;
        // EX6-058 "For each level of the Digimon deleted by this effect, trash the top card of your deck." — 이 효과로 소멸한 디지몬의 Lv. 1마다 (소멸 직전의 Lv.)
        if ((m = Regex.Match(s, @"^For each level of the Digimon deleted by this effect, (?<core>.+)$", I)).Success)
        {
            var inner = ParseStep(char.ToUpperInvariant(m.Groups["core"].Value[0]) + m.Groups["core"].Value[1..] + ".", card, triggers);
            if (inner.Error != null || inner.Step == null) return null;
            var st = inner.Step;
            return (new Step(null, c =>
            {
                int k = Math.Min(c.LastTargets.Where(d => !c.Owner.BattleArea.Contains(d) && !c.Enemy.BattleArea.Contains(d)).Sum(d => Math.Max(0, d.TopCard.Level)), 20);
                if (k <= 0) { c.LastStepResolved = false; return; }
                Diag46.Hit("perLevelDeleted58");
                for (int i = 0; i < k; i++) RunStep(st, c);
            }, false, sentence.Trim()), null);
        }
        // EX13-053 "Return up to 3 ~ from your trash to the top of the deck ⟦cap58⟧ delete 1 of your opponent's Digimon with a play cost of 3 or less" —
        // 되돌린 뒤 소멸 (원문 순서 그대로), 이 효과로 되돌린 1장마다 등장 코스트 상한 +1
        if ((m = Regex.Match(s, @"^(?<a>return up to \d+ .+? from your trash to the top of the deck) ⟦cap58⟧ (?<t>delete 1 of your opponent's Digimon with a play cost of )(?<n>\d+)(?<r> or less)$", I)).Success)
        {
            string a = m.Groups["a"].Value, head = m.Groups["t"].Value, tail = m.Groups["r"].Value;
            int n = int.Parse(m.Groups["n"].Value);
            var ret = ParseStep(char.ToUpperInvariant(a[0]) + a[1..] + ".", card, triggers);
            string Del(int cap) => char.ToUpperInvariant(head[0]) + head[1..] + cap + tail + ".";
            var del0 = ParseStep(Del(n), card, triggers);
            if (ret.Error != null || ret.Step == null || del0.Error != null || del0.Step == null) return null;
            var retStep = ret.Step;
            var byK = new Dictionary<int, Step?> { [0] = del0.Step };
            return (new Step(null, c =>
            {
                int trash = c.Owner.Trash.Count, deck = c.Owner.MainDeck.Count;
                RunStep(retStep, c);
                int k = Math.Clamp(Math.Min(trash - c.Owner.Trash.Count, c.Owner.MainDeck.Count - deck), 0, 20);
                if (k > 0) Diag46.Hit("capPerReturned58");
                if (!byK.TryGetValue(k, out var del)) byK[k] = del = ParseStep(Del(n + k), card, triggers).Step;
                if (del != null) RunStep(del, c);
            }, false, sentence.Trim()), null);
        }
        // BT6-042 "You may play 1 [Rosemon] or up to 2 yellow level 3 Digimon Cards from your trash without paying their memory costs." — 둘 중 하나를 골라 등장
        if ((m = Regex.Match(s, @"^You may play (?<a>1 \[[^\]]+\](?: or \[[^\]]+\])*) or (?<b>up to \d+ .+?) from your trash without paying their (?:memory )?costs$", I)).Success
            && ParseNamePick(m.Groups["a"].Value) is { } pa && ParseCardPick(m.Groups["b"].Value) is { } pb)
        {
            var sa = ParseStep($"You may play {m.Groups["a"].Value} from your trash without paying the cost.", card, triggers);
            var sb = ParseStep($"You may play {m.Groups["b"].Value} from your trash without paying the costs.", card, triggers);
            if (sa.Error != null || sa.Step == null || sb.Error != null || sb.Step == null) return null;
            var (stepA, stepB, matchA, matchB) = (sa.Step, sb.Step, pa.Match, pb.Match);
            string ta = m.Groups["a"].Value, tb = m.Groups["b"].Value;
            return (new Step(null, c =>
            {
                bool canA = c.Owner.Trash.Any(x => x.Type == CardType.Digimon && matchA(x)), canB = c.Owner.Trash.Any(x => x.Type == CardType.Digimon && matchB(x));
                if (!canA && !canB) { c.LastStepResolved = false; c.Log($"[{c.Card.Name}] 트래시에 등장시킬 카드가 없습니다"); return; }
                int rec = canA ? 0 : 1;   // AI: 이름이 지정된 카드(보통 더 강하다)가 있으면 그쪽
                int pick = canA && canB ? c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 트래시에서 어느 쪽을 등장시킬까요?", new[] { ta, tb }, rec) : rec;
                Diag46.Hit("playEither58");
                RunStep(pick == 0 ? stepA : stepB, c);
            }, false, sentence.Trim()), null);
        }
        return null;
    }

    // =====================================================================
    // AI: 고르는 효과의 추천 — 고를 때마다 지금 할 수 있는 선택지 중에서
    // =====================================================================

    /// <summary>
    /// 고르는 효과의 추천 (AI가 고르는 것 · 플레이어 화면의 추천): 지금 할 수 있는(OptionFeasible51 — 진화할 카드 · 쓸 옵션 · 대상 · 시큐리티가 있는) 선택지 중
    /// 문장 점수가 가장 큰 것. 할 수 있는 것이 없으면 전처럼 점수가 가장 큰 것 (전: 늘 점수가 가장 큰 것 — 대상이 없는 선택지를 골라 아무것도 하지 않았다)
    /// </summary>
    private static Func<EffectContext, int> Recommender58(IReadOnlyList<string> optionTexts, IReadOnlyList<double> values)
    {
        int best = 0;
        for (int i = 1; i < values.Count; i++) if (values[i] > values[best]) best = i;
        if (optionTexts.Count <= 1) return _ => best;
        var feasible = optionTexts.Select(OptionFeasible51).ToArray();
        if (feasible.All(f => f == null)) return _ => best;
        return c =>
        {
            int pick = -1;
            for (int i = 0; i < feasible.Length; i++)
                if ((feasible[i] == null || feasible[i]!(c)) && (pick < 0 || values[i] > values[pick])) pick = i;
            if (pick < 0) return best;
            if (pick != best) Diag46.Hit("choiceFeasible58");
            return pick;
        };
    }

    /// <summary>선택지를 지금 할 수 있는지 (대상 디지몬 · 상대의 시큐리티) — 모르면 null</summary>
    private static Func<EffectContext, bool>? OptionTargetFeasible58(string text)
    {
        string t = Normalize(text).TrimStart('・').Trim();
        // 첫 문장의 대상 (조건 · 비용이 붙은 것은 보지 않는다)
        string first = SplitSentences(t).FirstOrDefault()?.Trim() ?? "";
        if (Regex.IsMatch(first, @"^(?:If|While|By|When)\b|\bfor each\b|\bfor every\b", I)) return null;
        if (Regex.IsMatch(first, @"^Trash (?:your opponent's top security card|the top card of your opponent's security stack|your opponent's top \d+ security cards)\b", I))
            return c => c.Enemy.SecurityStack.Count > 0;
        foreach (Match m in OptionTargetPhrase.Matches(first))
        {
            if (m.Index > 40) break;   // 문장 앞쪽의 대상만 (뒤쪽은 다른 동작의 대상일 수 있다)
            if (TargetSpec.Parse(m.Groups["t"].Value) is not { } spec || spec.IsThis || spec.IsThat || spec.UpTo) continue;
            return c => PoolOf(c, spec).Count > 0;
        }
        return null;
    }

    // =====================================================================
    // ≪디지버스트 N까지≫ (고르는 효과의 머리말)
    // =====================================================================

    internal static readonly Regex BurstUpToRe58 = new(@"^＜Digi-Burst up to (?<n>\d+)＞\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string? TryParseChoice58(string rawHeader, IReadOnlyList<string> rawOptions, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParseChoice57(rawHeader, rawOptions, inherited, card, def);
        if (err == null || _gate58 > 0 || !rawHeader.Contains("Digi-Burst up to", StringComparison.OrdinalIgnoreCase)) return err;
        _gate58++;
        int before = def?.Script41.Effects.Count ?? 0;
        try
        {
            if (BurstUpTo58(rawHeader, rawOptions, inherited, card, null) != null) return err;
            if (def != null) BurstUpTo58(rawHeader, rawOptions, inherited, card, def);
            return null;
        }
        finally
        {
            _gate58--;
            if (def != null) WrapGate58(def, before);
        }
    }

    /// <summary>
    /// "[Main] [Once Per Turn] ＜Digi-Burst up to 4＞ (You may trash up to 4 of this Digimon's digivolution cards to activate the effect below.)" (BT7-040) —
    /// 진화원을 1 ~ N장 파기(≪디지버스트≫ — 파기한 카드의 효과 · "디지버스트를 발휘했을 때"도)하는 것으로 아래 효과. 파기한 매수는 PaidCount ("for each card trashed by this card's effect")
    /// </summary>
    private static string? BurstUpTo58(string rawHeader, IReadOnlyList<string> rawOptions, bool inherited, CardData card, CardScriptDefinition? def)
    {
        if (inherited || card.Type != CardType.Digimon || rawOptions.Count != 1) return "미지원: ≪디지버스트 N까지≫";
        string header = Normalize(rawHeader);
        var prefix = Regex.Match(header, @"^((?:\s*\[[^\]]+\])*)\s*(.*)$");
        var triggers = new List<EffectTrigger>();
        bool oncePerTurn = false;
        foreach (var label in Regex.Matches(prefix.Groups[1].Value, @"\[([^\]]+)\]").Select(x => x.Groups[1].Value.Trim()))
        {
            if (EventTriggers.TryGetValue(label, out var tr)) triggers.Add(tr);
            else if (label.Equals("Once Per Turn", StringComparison.OrdinalIgnoreCase)) oncePerTurn = true;
            else return $"미지원 시점 [{label}]";
        }
        if (triggers.Count == 0) return "미지원: 시점 없는 고르는 효과";
        if (!MapMainTrigger(triggers, card, inherited)) return "미지원: 진화원의 [메인] 효과";
        var bm = BurstUpToRe58.Match(prefix.Groups[2].Value.Trim());
        if (!bm.Success) return "문장 파서(3단계) 대상: 고르는 효과";
        int n = int.Parse(bm.Groups["n"].Value);
        if (n <= 0) return "미지원: ≪디지버스트 N까지≫";

        string optText = Regex.Replace(Normalize(rawOptions[0]).TrimStart('・').Trim(), @"\bby this card's effect\b", "by this effect", I);
        var steps = new List<Step>();
        foreach (var sentence in MergeRevealSentences(SplitSentences(optText)))
        {
            var step = ParseStep(sentence, card, triggers);
            if (step.Error != null) return step.Error;
            if (step.Step != null) steps.Add(step.Step);
        }
        if (steps.Count == 0) return "미지원: ≪디지버스트 N까지≫";
        // AI 추천 매수: "-N DP for each card trashed"면 가장 DP가 높은 상대 디지몬(대상으로 고르는 순서)을 소멸시킬 만큼 — 모자라면 발휘하지 않는다
        var unitM = Regex.Match(optText, @"gets -(?<u>\d+) DP\b.*for each card trashed", I);
        int unit = unitM.Success ? int.Parse(unitM.Groups["u"].Value) : 0;
        int Recommend(EffectContext c, int cap)
        {
            if (unit <= 0) return cap;
            var top = c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null && !c.Engine.IsUnaffectedBy(c.Owner, d)).OrderByDescending(d => d.GetCurrentDP()).FirstOrDefault();
            if (top == null) return 0;
            int need = Math.Max(1, (top.GetCurrentDP() + unit - 1) / unit);
            return need <= cap ? need : 0;
        }
        Action<EffectContext> resolve = c =>
        {
            if (c.Self == null || !c.Owner.BattleArea.Contains(c.Self)) return;
            int cap = Math.Min(n, c.Self.EvolutionSources.Count);
            if (cap <= 0) { c.Log($"[{c.Card.Name}] 진화원이 없어 ≪디지버스트≫를 발휘하지 않습니다"); return; }
            int k = c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] ≪디지버스트 {n}까지≫ — 파기할 진화원의 매수를 고르세요 (0 = 발휘하지 않는다).",
                Enumerable.Range(0, cap + 1).Select(i => i == 0 ? "발휘하지 않는다" : $"{i}장").ToList(), Recommend(c, cap));
            k = Math.Clamp(k, 0, cap);
            if (k == 0) { c.Log($"[{c.Card.Name}] ≪디지버스트≫를 발휘하지 않았습니다"); return; }
            c.Engine.PayDigiBurst(c.Owner, c.Self, k);
            c.PaidCount = k;
            c.Log($"[{c.Card.Name}] ≪디지버스트≫ 진화원 {k}장 파기");
            Diag46.Hit("digiBurstUpTo58");
            foreach (var s in steps) RunStep(s, c);
        };
        if (def == null) return null;
        foreach (var t in triggers)
        {
            if (t == EffectTrigger.Main) def.Main(TargetKind.None, resolve);
            else def.On(t, resolve, null, oncePerTurn, false);
        }
        return null;
    }
}
