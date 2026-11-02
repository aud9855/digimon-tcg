using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.83.0 57차 묶음 — 영어 API에 빠져 있던 효과 문구를 공식 영어로 채운 카드(Data.OfficialCardData83)의 새 문장 (원래 규칙이 읽지 못한 절만):
///  - BT24-061 "Return 1 of your opponent's play cost 3 or lower Digimon or Tamers to the top of the deck" — 덱 위로 (덱 아래로와 같은 대상 · 같은 처리, 놓는 곳만 위)
///  - BT26-060 "Return the top 5 stacked cards of 3 of your opponent's Digimon to the top of the deck" — 겹쳐진 카드를 위에서부터 N장 덱 위로
///    (BT21-030 "Trash the top 10 stacked cards ~"와 같은 셈 — 맨 위 카드도 겹쳐진 카드. 모두 되돌리면 그 디지몬은 배틀 에어리어를 떠난다)
///  - BT26-080 "Delete 1 of your opponent's Digimon with the same orientation as this Digimon" — 이 디지몬과 같은 표시 형식 (액티브 · 레스트)
///  - BT26-027 비용 "By suspending 1 of your Digimon with the [Vegetation], [Fairy] or [WG] trait," — 특징 셋을 늘어놓은 쉼표
///  - BT26-089 "After, if removed from by effects, ~" — 이 효과를 일으킨 "시큐리티가 줄어들었을 때"가 효과로 줄어든 것이면 (체크가 아니면)
/// </summary>
internal static partial class SentenceParser
{
    private const RegexOptions I83 = RegexOptions.IgnoreCase;

    /// <summary>BT26-060 "Return the top 5 stacked cards of 3 of your opponent's Digimon to the top of the deck"</summary>
    internal static readonly Regex StackToDeckTopRe83 = new(@"^Return the top (?<n>\d+) stacked cards of (?<t>.+?) to the top of (?:the|its owner's|their owners'?|their) decks?$", I83 | RegexOptions.Compiled);

    /// <summary>"Return ~ to the top of the deck" (대상을 덱 위로 — 덱 아래로의 대상 표기와 같다)</summary>
    internal static readonly Regex ReturnToDeckTopRe83 = new(@"^Return (?<t>(?:\d+|up to \d+|all) of (?:your opponent's|their) .+?) to the top of (?:the|its owner's|their owners'?|their|the owner's) decks?$", I83 | RegexOptions.Compiled);

    /// <summary>BT26-080 "Delete 1 of your opponent's Digimon with the same orientation as this Digimon"</summary>
    internal static readonly Regex SameOrientationRe83 = new(@"^(?<v>Delete|Suspend|Return) (?<n>\d+) of your opponent's Digimon with the same orientation as this Digimon(?<rest> to (?:the|its owner's) hand)?$", I83 | RegexOptions.Compiled);

    /// <summary>원래 규칙이 읽지 못한 절 → 같은 뜻의 다른 표기 (바꾸지 못하면 null)</summary>
    internal static string? Rewrite83(string rawClause)
    {
        string t = Normalize58(rawClause);
        // BT26-027 "By suspending 1 of your Digimon with the [A], [B] or [C] trait, ~" — 비용 안의 늘어놓은 쉼표 ("[A] or [B] or [C]")
        string a = Regex.Replace(t, @"\b(?<p>[Bb]y suspending \d+ of your Digimon with the )(?<l>\[[^\]]+\](?:, \[[^\]]+\])+ or \[[^\]]+\])(?= traits?,)",
            m => m.Groups["p"].Value + NoListCommas40(m.Groups["l"].Value));
        // EX4-072 (공식 영어) "From your hand, ignoring digivolution requirements and without paying the cost, it may digivolve into a level 6 Digimon card with a different name
        //   that includes the chosen Digimon's name." — 영어 API의 같은 뜻 표기로
        a = Regex.Replace(a, @"\bFrom your hand, ignoring digivolution requirements and without paying the cost, it may digivolve into (?<x>a level \d+ Digimon card) with a different name that includes the chosen Digimon's name\.",
            "It may digivolve into ${x} in your hand with a different name that includes the name of the chosen Digimon, ignoring digivolution requirements and without paying the cost.", I83);
        return a == t ? null : a;
    }

    /// <summary>EX4-072 "Choose 1 of your [Gallantmon], [Sakuyamon] or [MegaGargomon]" — 그 명칭의 자신의 디지몬 1마리</summary>
    internal static readonly Regex ChooseNamedRe83 = new(@"^Choose (?:1|one) of your (?<names>\[[^\]]+\](?:(?:,| or|, or) \[[^\]]+\])*)$", I83 | RegexOptions.Compiled);

    private static string? TryParse83(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse80(rawClause, inherited, card, def);
        if (err == null) return null;
        // 원래 규칙이 읽지 못한 절만
        if (Rewrite83(rawClause) is not { } alt || TryParse80(alt, inherited, card, null) != null) return err;
        if (def != null)
        {
            TryParse80(alt, inherited, card, def);
            Diag46.Hit("rewrite83");
        }
        return null;
    }

    /// <summary>v0.83.0 조건: "removed from by effects" (BT26-089 — 이 효과를 일으킨 "자신의 시큐리티가 줄어들었을 때"가 효과로 줄어든 것)</summary>
    private static Func<EffectContext, bool>? ParseEventCondition83(string cond) =>
        Regex.IsMatch(cond.Trim(), @"^(?:(?:your security stack|it) (?:was|is) )?removed from by (?:an )?effects?$", I83)
            ? c => c.Trigger == EffectTrigger.AllySecurityRemoved && c.Engine.SecurityRemovedByEffect
            : null;

    private static (Action<EffectContext>? Act, string? Error)? ParseAction83(string s, CardData card, List<EffectTrigger> triggers)
    {
        s = Regex.Replace(s.Trim().TrimEnd('.'), @"^Then,?\s+", "", I83);
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        bool security = triggers.Contains(EffectTrigger.Security);
        Match m;
        // EX4-072 고른 디지몬을 다음 문장이 쓴다 (ChooseAction34와 같은 처리 — 후보만 명칭으로)
        if ((m = ChooseNamedRe83.Match(s)).Success)
        {
            var names = TargetSpec.Names(m.Groups["names"].Value);
            if (names.Count == 0) return null;
            return (c =>
            {
                // 추천: 진화할 수 있는 카드가 패에 있는 디지몬 (EX4-072 다음 문장 — NamedEvo67과 같은 후보)
                var cands = c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && names.Any(n => CardNames.Is(d, n)))
                    .OrderByDescending(d => NamedEvoPool67(c, d, d.TopCard.Level).Count > 0 ? 1 : 0).ThenByDescending(d => d.EvolutionSources.Count).ToList();
                var d = cands.Count == 0 ? null : c.Engine.ChooseTargets(c.Owner, cands, 1, $"[{c.Card.Name}] 효과로 고를 디지몬을 고르세요.", false, null).FirstOrDefault();
                if (d == null) { c.LastStepResolved = false; c.LastTargets = new List<CardInstance>(); Emit(c, label, "고를 디지몬이 없습니다"); return; }
                c.LastTargets = new List<CardInstance> { d };
                c.Chosen34 = d;
                Diag46.Hit("chooseNamed83");
                Emit(c, label, $"[{d.TopCard.Name}]을(를) 선택했습니다");
            }, null);
        }
        // EX4-072 "It may digivolve into a level 6 Digimon card in your hand with a different name that includes the name of the chosen Digimon, ignoring ~ without paying the cost"
        //   — 앞 문장에서 고른 디지몬 (NamedEvo67과 같은 처리: 명칭에 고른 디지몬의 명칭을 포함하고 명칭이 다른 패의 Lv.N 디지몬 카드)
        if ((m = Regex.Match(s, @"^It may digivolve into a level (?<lv2>\d+) Digimon card in your hand with a different name that includes the name of the chosen Digimon, ignoring (?:its )?digivolution requirements and without paying the cost$", I83)).Success)
        {
            int lv2 = int.Parse(m.Groups["lv2"].Value);
            return (c =>
            {
                var chosen = c.Chosen34;
                if (chosen == null || !c.Owner.BattleArea.Contains(chosen)) { c.LastStepResolved = false; return; }
                var pool = NamedEvoPool67(c, chosen, lv2);
                Diag46.Hit("namedEvoChosen83");
                if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, $"[{chosen.TopCard.Name}] → 진화할 수 있는 카드가 없습니다"); return; }
                var x = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, pool, 1, upTo: true, $"[{c.Card.Name}] [{chosen.TopCard.Name}]을(를) 진화 조건을 무시하고 코스트 없이 진화시킬 카드를 고르세요 (명칭에 「{CardNames.MatchName(chosen)}」 포함 · 명칭이 다른 Lv.{lv2}).").FirstOrDefault();
                if (x == null) { c.LastStepResolved = false; return; }
                string before = chosen.TopCard.Name;
                c.Engine.EvolveByEffect(c.Owner, chosen, x, 0);
                bool done = ReferenceEquals(chosen.TopCard, x);
                if (!done) c.LastStepResolved = false;
                Emit(c, label, done ? $"[{before}] → [{x.Name}] 진화 조건을 무시하고 코스트 없이 진화" : $"[{before}]은(는) [{x.Name}]으로 진화할 수 없습니다");
            }, null);
        }
        // BT25-101 "Link this card or 1 [TS] trait card from your trash to 1 of your Digimon on the field without paying the cost" — 둘 중 하나를 링크
        //   (이 카드: 옵션 · 링크 조건 [Vulcanusmon] / 트래시의 카드: 그 카드의 링크 조건). 추천: 이 카드를 링크할 수 있으면 이 카드
        if ((m = Regex.Match(s, @"^Link this card or (?<pick>(?:1|one) .+?) from your trash to (?<t>(?:1|one) of your Digimon(?: on the field)?) without paying the cost$", I83)).Success)
        {
            string onField = m.Groups["t"].Value, mine = Regex.Replace(onField, @" on the field$", "", I83);   // "on the field" = 배틀 에어리어 (트래시에서 링크하는 표기에는 없다)
            var self = ParseStep($"Link this card to {onField} without paying the cost.", card, triggers);
            var trash = ParseStep($"Link {m.Groups["pick"].Value} from your trash to {mine} without paying the cost.", card, triggers);
            var pick = ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value);
            if (self.Step == null || trash.Step == null || pick == null) return null;
            var (aSelf, aTrash, match) = (self.Step.Act, trash.Step.Act, pick.Value.Match);
            return (c =>
            {
                bool canSelf = c.Owner.BattleArea.Any(d => !GoldfishEngine.IsToken(d.TopCard) && GoldfishEngine.MeetsLinkRequirement(c.Card, d) == true);
                bool canTrash = c.Owner.Trash.Any(x => match(x) && c.Owner.BattleArea.Any(d => !GoldfishEngine.IsToken(d.TopCard) && GoldfishEngine.MeetsLinkRequirement(x, d) != false));
                Diag46.Hit("linkThisOrTrash83");
                if (!canSelf && !canTrash) { c.LastStepResolved = false; Emit(c, label, "링크할 카드가 없습니다"); return; }
                bool useSelf = canSelf && (!canTrash || c.Engine.AskYesNo(c.Owner, null, $"[{c.Card.Name}] 이 카드를 링크할까요? (아니오: 트래시의 카드를 링크)", recommended: true));
                (useSelf ? aSelf : aTrash)(c);
            }, null);
        }
        // BT26-060 겹쳐진 카드를 위에서부터 N장 덱 위로
        if ((m = StackToDeckTopRe83.Match(s)).Success && TargetSpec.Parse(m.Groups["t"].Value) is { } stackTarget)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                var parts = new List<string>();
                foreach (var d in Pick(stackTarget, c, security).ToList())
                {
                    var zone = c.Engine.OwnerOf(d);
                    string name = d.TopCard.Name;
                    int k = c.Engine.ReturnTopStackedToDeckTop83(zone, d, n);
                    if (k > 0) parts.Add($"[{name}] {k}장");
                }
                if (parts.Count == 0) c.LastStepResolved = false;
                Diag46.Hit("stackToDeckTop83");
                Emit(c, label, parts.Count > 0 ? $"겹쳐진 카드를 덱 위로: {string.Join(", ", parts)}" : "되돌릴 카드가 없습니다");
            }, null);
        }
        // BT24-061 대상을 덱 위로 — 덱 아래로와 같은 처리 (놓는 곳만 위: GoldfishEngine.DeckTop83)
        if ((m = ReturnToDeckTopRe83.Match(s)).Success)
        {
            string bottom = $"Return {m.Groups["t"].Value} to the bottom of the deck";
            var inner = Regex.IsMatch(m.Groups["t"].Value, @"\bTamers\b", I83) && TamerTargetAction34(bottom, label, security) is { } withTamers ? withTamers : ParseAction(bottom, card, triggers);   // 디지몬 또는 테이머 (SentenceParser.Batch34b)
            if (inner.Error != null || inner.Act == null) return null;
            var act = inner.Act;
            return (c =>
            {
                c.Engine.DeckTop83++;
                try { act(c); }
                finally { c.Engine.DeckTop83--; }
                Diag46.Hit("returnToDeckTop83");
            }, null);
        }
        // BT26-080 이 디지몬과 같은 표시 형식 — 처리할 때 이 디지몬이 레스트 상태면 레스트 상태인 디지몬, 아니면 액티브 상태인 디지몬
        if ((m = SameOrientationRe83.Match(s)).Success)
        {
            string head = $"{m.Groups["v"].Value} {m.Groups["n"].Value} of your opponent's ";
            var susp = ParseAction($"{head}suspended Digimon{m.Groups["rest"].Value}", card, triggers);
            var unsusp = ParseAction($"{head}unsuspended Digimon{m.Groups["rest"].Value}", card, triggers);
            if (susp.Error != null || unsusp.Error != null || susp.Act == null || unsusp.Act == null) return null;
            var (aS, aU) = (susp.Act, unsusp.Act);
            return (c =>
            {
                if (c.Self == null || !c.Owner.BattleArea.Contains(c.Self)) { c.LastStepResolved = false; Emit(c, label, "이 디지몬이 배틀 에어리어에 없습니다"); return; }
                Diag46.Hit("sameOrientation83");
                (c.Self.IsSuspended ? aS : aU)(c);
            }, null);
        }
        return null;
    }
}
