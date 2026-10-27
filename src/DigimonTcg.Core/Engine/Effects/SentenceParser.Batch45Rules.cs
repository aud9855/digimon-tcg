using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.45.0 19차 묶음 — 표기 규칙 · 절 · 문장 · 동작 · 비용 · 조건 · 셈 (원래 규칙이 읽지 못한 것만)</summary>
internal static partial class SentenceParser
{
    /// <summary>절 전체 규칙 (문장 경계를 넘는 표기)</summary>
    private static readonly Func<string, string>[] ClauseRules45 =
    {
        // 덱 위 공개 묶음을 한 문장으로 (SentenceParser.Batch45Reveal)
        JoinRevealGroup45,
        // 앞 조건을 받는 짧은 조건: "If your opponent has 3 or fewer security cards, A. If they have 4 or more, B" (BT15-017) ·
        //  "If your opponent has 6 or more cards in their hand, A. Then, if they have 5 or fewer, B" (ST22-14) — 주어 · 대상을 채운다
        t => Regex.Replace(t, @"(?<a>\bIf your opponent has \d+ or (?:fewer|more) (?<w>security cards|cards in their hand), [^.]+\.)\s+(?<then>Then, )?(?<if>[Ii]f) they have (?<m>\d+) or (?<cmp>more|fewer), ",
            m => $"{m.Groups["a"].Value} {m.Groups["then"].Value}{m.Groups["if"].Value} your opponent has {m.Groups["m"].Value} or {m.Groups["cmp"].Value} {m.Groups["w"].Value}, ", I),
        // "If a level 3 Digimon card Is in this Digimon's digivolution cards, A. If there isn't, B" (BT18-024) — 앞 조건의 부정
        t => Regex.Replace(t, @"\bIf (?<c>an? [^,]+? card) [Ii]s in this Digimon's digivolution cards, (?<a>[^.]+)\.\s+If there isn't, ",
            "If ${c} is in this Digimon's digivolution cards, ${a}. If ${c} isn't in this Digimon's digivolution cards, ", I),
    };

    private static readonly Func<string, string>[] RewriteRules45 =
    {
        // "one of them" = 사건의 디지몬: "When your Digimon get linked, one of them may attack" (BT25-075) ·
        //  "When your Digimon are played or digivolve, if any of them have the [Xros Heart] or [Hero] trait, by suspending this Tamer, one of them may attack" (BT21-083)
        t => Regex.Replace(t, @"\bif any of them have (?<x>the \[[^\]]+\](?: or \[[^\]]+\])* traits?)\b", "if that Digimon has ${x}", I),
        t => Regex.Replace(t, @"(?<=^|, )one of them may attack$", "that Digimon may attack", I),
        // "trash 1 Option card in your opponent's battle area" (BT18-100)
        t => Regex.Replace(t, @"\btrash 1 Option card in your opponent's battle area\b", "trash 1 of your opponent's Option cards in the battle area", I),
        // "If you did either, ~" (BT16-094 — 앞 문장 "A, or you may B")
        t => Regex.Replace(t, @"^If you did either, ", "If you do, ", I),
        // "While you have another Digimon in play with the [X Antibody] trait, or ~" (BT7-062)
        t => Regex.Replace(t, @"\byou have another Digimon in play with (?<x>the \[[^\]]+\] trait)\b", "you have another Digimon with ${x} in play", I),
        // "If you don't delete an opponent's Digimon with this effect, ~" (P-066) = "If no Digimon was deleted by this effect"
        t => Regex.Replace(t, @"^If you don't delete an opponent's Digimon with this effect, ", "If no Digimon was deleted by this effect, ", I),
        // 원문 오타 · 빠진 말: "you may plays 1 ~" (BT10-083) · "1 of your Digimon on the field may digivolve" (BT20-087) · "All of your [Marcus Damon] in play get ~" (BT12-043)
        t => Regex.Replace(t, @"\byou may plays\b", "you may play", I),
        t => Regex.Replace(t, @"\b1 of your Digimon on the field may digivolve\b", "1 of your Digimon may digivolve", I),
        t => Regex.Replace(t, @"^All of your (?<x>\[[^\]]+\]) in play (?=gets? )", "All of your ${x} ", I),
        // "gain 1 memory for each suspended Digimon with ~ you have in play" (BT10-057)
        t => Regex.Replace(t, @"\bgain (?<n>\d+) memory for each suspended Digimon with (?<w>.+?) you have in play\b", "gain ${n} memory for each of your suspended Digimon with ${w}", I),
        // "all of your Digimon get +2000 DP and your opponent's effects can't return them to hands or decks or reduce their DP" (BT10-068)
        t => Regex.Replace(t, @"\ball of your Digimon get (?<dp>[+-]\d+ DP) and your opponent's effects can't return them to hands or decks or reduce their DP\b",
            "all of your Digimon get ${dp} and can't be returned to hands or decks by your opponent's effects and can't have their DP reduced by your opponent's effects", I),
        // 효과로 쓰는 옵션은 원래 색 조건을 보지 않는다 (엔진 UseOptionByEffect): "without meeting its color requirements or paying its memory cost" (BT10-039) = 코스트 없이
        t => Regex.Replace(t, @"\bwithout meeting its colou?r requirements or paying its memory cost\b", "without paying the cost", I),
        // "use 1 option card that has [Plug-In] in its name or that's yellow from the hand" (BT17-035)
        t => Regex.Replace(t, @"\buse 1 option card that has (?<x>\[[^\]]+\]) in its name or that's (?<c>red|blue|yellow|green|black|purple|white) from the hand\b", "use 1 Option card with ${x} in its name or 1 ${c} Option card from your hand", I),
        // 비용 뒤의 "it" = 비용으로 옮긴 디지몬: "By moving ~ from the breeding area to the battle area, it may digivolve into ~" (BT20-095)
        t => Regex.Replace(t, @"^it may digivolve into ", "That Digimon may digivolve into ", I),
    };

    private static bool ExtraClause45(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string c0 = Regex.Replace(rawClause.Trim(), @"^(?:Inherited Effect|Security Effect)\s*", "", I);
        if (BreedingClause45(c0, inherited, card, def)) return true;   // [육성] (SentenceParser.Batch45Breeding)
        // "You can ignore this card's color requirements." (P-206 — 공식 "이 카드는 색 조건을 무시할 수 있다")
        if (card.Type == CardType.Option && !inherited && Regex.IsMatch(c0, @"^You (?:can|may) ignore this card's colou?r requirements\.?$", I))
        {
            def?.IgnoreColorRequirementWhen(_ => true);
            return true;
        }
        return false;
    }

    private static Step? StepExtra45(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        string s = sentence.Trim();
        if (s.Contains(RevealMark45)) return RevealGroup45(s, card, triggers);
        Match m;
        // "The Digimon played by this effect can't digivolve to level 7, and at the end of your opponent's turn, delete that Digimon." (EX3-069 — 공식
        //  "이 효과로 등장한 디지몬은 Lv.7로 진화할 수 없고, 다음 상대의 턴 종료 시에 소멸한다")
        if ((m = Regex.Match(s.TrimEnd('.'), @"^The Digimon played by this effect can't digivolve to level (?<lv>\d+), and (?<rest>at the end of .+?), delete that Digimon$", I)).Success)
        {
            int lv = int.Parse(m.Groups["lv"].Value);
            var later = ParseStep(Cap44(m.Groups["rest"].Value) + ", delete the Digimon played by this effect.", card, triggers);
            if (later.Error != null || later.Step == null) return null;
            var st = later.Step;
            return new Step(null, c =>
            {
                foreach (var d in c.PlayedThisEffect.Where(c.Owner.BattleArea.Contains)) { d.CannotEvoToLevel45 = lv; Diag45.Hit("cannotEvoToLevel"); }
                RunStep(st, c);
            }, Text: s);
        }
        return null;
    }

    private static (Action<EffectContext>? Act, string? Error)? ActionExtra45(string s, CardData card, List<EffectTrigger> triggers)
    {
        Match m;
        // "reduce the play costs of all of your opponent's Digimon by 1 until the end of the turn" (BT16-060 — 공식 "턴 종료까지 상대의 디지몬 전부를 등장 코스트 -1")
        if ((m = Regex.Match(s, @"^reduce the play costs of all of your opponent's Digimon by (?<n>\d+)(?: (?:until the end of the turn|for the turn))?$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                foreach (var d in c.Enemy.BattleArea) d.TimedPlayCostMods45.Add((-n, c.TurnSequence));
                c.RecalculateFieldBuffs();
                Diag45.Hit("enemyPlayCostDown");
                c.Log($"[{c.Card.Name}] 턴 종료까지 상대의 디지몬 전부 등장 코스트 -{n}");
            }, null);
        }
        // 등장 코스트 합계까지 여러 장: "You may play up to 8 play cost's total worth of [Iliad] trait cards from your hand or trash without paying the costs" (BT26-081 ·
        //  BT23-074 · BT27-084 · EX8-029 "from this Digimon's digivolution cards" · EX8-064) — 1장씩 고르고, 고르지 않으면 끝
        if ((m = Regex.Match(s, @"^(?<may>you may )?play up to (?<n>\d+) play cost's total worth of (?<p>.+?) from (?<z>your hand or trash|your hand or your trash|your hand|your trash|this Digimon's digivolution cards) without paying (?:the|their|its) costs?$", I)).Success
            && RevealPick45(m.Groups["p"].Value) is { } bp)
        {
            int budget = int.Parse(m.Groups["n"].Value);
            string z = m.Groups["z"].Value.ToLowerInvariant();
            bool hand = z.Contains("hand"), trash = z.Contains("trash"), sources = z.Contains("digivolution");
            if (sources && card.Type != CardType.Digimon) return null;
            var match = bp.Match;
            return (c =>
            {
                int spent = 0, count = 0;
                while (c.Owner.BattleArea.Count < 12 && !c.State.IsGameOver)
                {
                    var pool = new List<(CardData Card, PlayOrigin Origin)>();
                    if (hand) pool.AddRange(c.Owner.Hand.Distinct().Select(x => (x, PlayOrigin.Hand)));
                    if (trash) pool.AddRange(c.Owner.Trash.Distinct().Select(x => (x, PlayOrigin.Trash)));
                    if (sources && c.Self != null && c.Owner.BattleArea.Contains(c.Self)) pool.AddRange(c.Self.FaceUpSources.Distinct().Select(x => (x, PlayOrigin.Sources)));
                    pool = pool.Where(p => p.Card.Type is CardType.Digimon or CardType.Tamer && match(p.Card) && spent + p.Card.PlayCost <= budget)
                        .OrderByDescending(p => p.Card.PlayCost).ThenByDescending(p => p.Card.Level).ToList();
                    if (pool.Count == 0) break;
                    var ks = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Other, pool.Select(p => p.Card).ToList(), 1, true,
                        $"[{c.Card.Name}] 등장시킬 카드를 고르세요 (등장 코스트 합계 {spent}/{budget} — 고르지 않으면 끝).");
                    if (ks.Count == 0) break;
                    var (x, origin) = pool[ks[0]];
                    var self = c.Self;
                    Func<bool> take = origin switch
                    {
                        PlayOrigin.Hand => () => c.Owner.Hand.Remove(x),
                        PlayOrigin.Trash => () => c.Owner.Trash.Remove(x),
                        _ => () => self != null && self.TakeSource(y => ReferenceEquals(y, x)) != null,
                    };
                    if (!c.Engine.TryPlayByEffect(c.Owner, x, origin, take)) break;
                    spent += x.PlayCost;
                    count++;
                    Diag45.Hit("playTotalWorth");
                }
                if (count == 0) c.LastStepResolved = false;
                else c.RecalculateFieldBuffs();
            }, null);
        }
        // "You may play 1 Tamer card with the same color as any of your Digimon on the field from your hand with the play cost reduced by 4" (P-206)
        if ((m = Regex.Match(s, @"^(?<may>you may )?play 1 Tamer card with the same colou?r as any of your Digimon(?: on the field| in play)? from your hand with the play cost reduced by (?<n>\d+)$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            bool may = m.Groups["may"].Success;
            return (c =>
            {
                var colors = c.Owner.BattleArea.SelectMany(d => d.Colors).ToHashSet();
                var pool = c.Owner.Hand.Where(x => x.Type == CardType.Tamer && x.AllColors.Any(colors.Contains)).Distinct().OrderByDescending(x => x.PlayCost).ToList();
                if (pool.Count == 0) { c.LastStepResolved = false; return; }
                var k = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Hand, pool, 1, may, $"[{c.Card.Name}] 등장시킬 테이머를 고르세요 (등장 코스트 -{n}).");
                if (k.Count == 0) { c.LastStepResolved = false; return; }
                var x = pool[k[0]];
                if (c.Engine.TryPlayByEffect(c.Owner, x, PlayOrigin.Hand, () => c.Owner.Hand.Remove(x), payCost: Math.Max(0, x.PlayCost - n))) Diag45.Hit("playTamerSameColor");
                else c.LastStepResolved = false;
            }, null);
        }
        // "Place 1 [Trial of the Four Great Dragons] from your hand in the battle area, or you may trash 1 [Four Great Dragons] trait card in your hand" (BT16-094 — 공식
        //  "자신의 패에서 「4대용의 시련」 1장을 배틀 에어리어에 놓거나, 특징으로 「4대용」을 가진 카드 1장을 파기할 수 있다") — 둘 다 하지 않으면 뒤의 "If you did either"는 없다
        if ((m = Regex.Match(s, @"^Place 1 \[(?<x>[^\]]+)\] from your hand in the battle area, or you may trash 1 (?<p>.+?) in your hand$", I)).Success
            && RevealPick45(m.Groups["p"].Value) is { } tp)
        {
            string name = m.Groups["x"].Value;
            var match = tp.Match;
            return (c =>
            {
                var opt = c.Owner.Hand.FirstOrDefault(x => x.Type == CardType.Option && TargetSpec.NameHas(x, name) && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (opt != null && c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 패의 [{opt.Name}]을(를) 배틀 에어리어에 놓을까요?", true))
                {
                    c.Owner.Hand.Remove(opt);
                    c.Engine.PlaceOption(c.Owner, opt);
                    Diag45.Hit("placeOrTrash");
                    return;
                }
                var pool = c.Owner.Hand.Where(match).Distinct().OrderBy(x => x.Level).ToList();
                if (pool.Count > 0)
                {
                    var k = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Hand, pool, 1, true, $"[{c.Card.Name}] 파기할 패를 고르세요 (고르지 않으면 효과 없음).");
                    if (k.Count > 0)
                    {
                        c.Owner.Hand.Remove(pool[k[0]]);
                        c.Owner.Trash.Add(pool[k[0]]);
                        c.Log($"[{c.Card.Name}] 패의 [{pool[k[0]].Name}] 파기");
                        Diag45.Hit("placeOrTrash");
                        return;
                    }
                }
                c.LastStepResolved = false;
            }, null);
        }
        return null;
    }

    private static (List<EffectTrigger> Triggers, Func<EffectContext, bool>? Condition)? ParseEventPhrase45(string ev) => null;

    private static Func<PlayerZones, PlayerZones, CardInstance?, int>? ParseCounter45(string counter)
    {
        if (!Gate45) return null;
        Match m;
        // "of your [Iliad] or [TS] trait Digimon or Tamers" (BT26-081 — 특징을 가진 자신의 디지몬과 테이머의 합계)
        if ((m = Regex.Match(counter.Trim(), @"^of your (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) trait Digimon (?:or|and/or|and) Tamers$", I)).Success)
        {
            var xs = Regex.Matches(m.Groups["x"].Value, @"\[([^\]]+)\]").Select(g => g.Groups[1].Value).ToList();
            bool Has(CardData c) => xs.Any(x => TargetSpec.HasTrait(c, x));
            return (o, _, _) => o.BattleArea.Count(d => d.TamerProxyOf == null && Has(d.TopCard)) + o.Tamers.Count(Has);
        }
        return null;
    }

    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost45(string cost, CardData card)
    {
        if (!Gate45) return null;
        Match m;
        // "digivolving 1 of your Digimon with [Lucemon] in its name into a Digimon card with [Lucemon] in its name from your trash with the digivolution cost reduced by 3" (BT18-100)
        if ((m = Regex.Match(cost.Trim(), @"^digivolving (?<t>1 of your Digimon(?: with [^,]+?)?) into (?<p>an? .+?) from your trash with the digivolution cost reduced by (?<n>\d+)$", I)).Success
            && TargetSpec.Parse(m.Groups["t"].Value) is { Opponent: false, AnySide: false } ts && RevealPick45(m.Groups["p"].Value) is { } ep)
        {
            int n = int.Parse(m.Groups["n"].Value);
            var fs = ts.Filters.ToList();
            var match = ep.Match;
            List<(CardInstance D, CardData X, int Cost)> Pairs(EffectContext c) =>
                c.Owner.BattleArea.Where(d => fs.All(f => f(d)) && !EffectRules.IsDigivolveLocked(c.Engine.State, d))
                    .SelectMany(d => c.Owner.Trash.Where(x => x.Type == CardType.Digimon && match(x)).Distinct()
                        .Where(x => ActionValidator.ValidateEvolution(d, x).CanExecute && CardScripts.EvoAllowed(d, x))
                        .Select(x => (d, x, Math.Max(0, ActionValidator.EvolutionCost(d, x) - n))))
                    .OrderByDescending(p => p.x.Level).ThenBy(p => p.Item3).ToList();
            return (c => Pairs(c).Count > 0,
                c =>
                {
                    var pairs = Pairs(c);
                    if (pairs.Count == 0) { c.CostFailed = true; return; }
                    var bases = pairs.Select(p => p.D).Distinct().ToList();
                    var d = bases.Count == 1 ? bases[0] : c.Engine.ChooseTargets(c.Owner, bases, 1, $"[{c.Card.Name}] 진화시킬 디지몬을 고르세요.", optional: false, preselected: bases[0]).FirstOrDefault() ?? bases[0];
                    var cards = pairs.Where(p => ReferenceEquals(p.D, d)).ToList();
                    var k = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Trash, cards.Select(p => p.X).ToList(), 1, false, $"[{c.Card.Name}] 트래시에서 진화할 카드를 고르세요 (진화 코스트 -{n}).");
                    var pick = cards[k.Count > 0 ? k[0] : 0];
                    c.Engine.EvolveByEffect(c.Owner, pick.D, pick.X, pick.Cost, c.Owner.Trash);
                    if (pick.D.TopCard != pick.X) { c.CostFailed = true; return; }
                    Diag45.Hit("digivolveFromTrashCost");
                },
                c => Pairs(c).Any(p => p.Cost <= Math.Max(0, c.OwnerMemory)),
                "트래시의 카드로 진화시켜");
        }
        // "moving your level 3 or higher Digimon from the breeding area to the battle area" (BT20-095) — 옮긴 디지몬이 "it"
        if ((m = Regex.Match(cost.Trim(), @"^moving your level (?<lv>\d+) or higher Digimon from the breeding area to the battle area$", I)).Success)
        {
            int lv = int.Parse(m.Groups["lv"].Value);
            return (c => c.Owner.BreedingArea is { } b && b.TopCard.Type == CardType.Digimon && b.TopCard.Level >= lv && c.Owner.BattleArea.Count < 12,
                c =>
                {
                    if (c.Owner.BreedingArea is not { } b || b.TopCard.Level < lv || c.Engine.MoveBreedingToBattle(c.Owner) is not { } moved) { c.CostFailed = true; return; }
                    c.LastTargets = new List<CardInstance> { moved };
                    Diag45.Hit("moveBreedingCost");
                },
                _ => true,
                "육성 에어리어의 디지몬을 배틀 에어리어로 옮겨");
        }
        return null;
    }

}
