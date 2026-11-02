using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.69.0 미지원 문장 43차 묶음 — 원래 규칙(42차까지)이 읽지 못한 절만:
///  - BT19-084 자신의 디지몬을 앞면 시큐리티의 디지몬 카드로 진화 (진화 코스트를 낸다 — 42차 앞면 시큐리티 구조)
///  - BT26-100 [시큐리티]【서로의 턴】 "~ gain ＜Blocker＞ While you have ~, they also get +3000 DP" — 두 지속 효과로 나눠 읽는다 (BT25-095 · BT25-102와 같은 방식)
///  - BT19-100 [시큐리티]【상대의 턴】 상대의 디지몬이 어택했을 때 (앞면 시큐리티 — 종합 룰 15-14-5-1) · 【시큐리티】 「마더 디・리퍼」의 진화원 매수 이하의 등장 코스트
///  - ST22-10 [시큐리티]【서로의 턴】 디지몬이 배틀 이외로 떠날 때, 앞면 시큐리티의 이 카드를 파기하는 것으로 떠나지 않는다
///  - BT22-090 · BT22-101 이 테이머를 패의 카드로 진화 코스트 -N (진화 조건은 만족해야 한다 — 공식 Q&amp;A BT22-090 Q1 · BT22-101 Q1)
///  - EX10-068 이 효과로 되돌린 카드와 같은 색 (공식 Q&amp;A EX10-068 Q1: 2색이면 그중 한 색을 포함하면 된다)
///  - BT11-094 진화한 디지몬과 명칭이 서로 다른 카드 (공식 Q&amp;A BT11-094 Q1) · BT11-093 같은 Lv.로 진화했다면 · 상대의 옵션 카드의 효과를 받지 않는다 (Q&amp;A BT11-093 Q1)
/// </summary>
internal static partial class SentenceParser
{
    /// <summary>바꿔 쓴 절을 다시 읽는 중 (같은 규칙으로 또 바꿔 쓰지 않는다)</summary>
    [ThreadStatic] private static int _depth69;

    /// <summary>앞면 시큐리티 지속 효과를 임시 카드로 읽는 중</summary>
    [ThreadStatic] private static int _faceUp69;

    private static string? TryParse69(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        // [시큐리티]【서로의 턴】 등의 지속 효과는 앞면 시큐리티에 있는 동안의 지속 효과 (종합 룰 15-14-5-1) — 42차까지는 【시큐리티】 시점 효과로 읽었다 (원래 규칙보다 먼저)
        if (_faceUp69 == 0 && FaceUpStatic69(rawClause, card, def)) return null;
        string? err = TryParse68(rawClause, inherited, card, def);
        if (err == null || _depth69 > 0) return err;
        string t = Normalize58(rawClause);
        _depth69++;
        try
        {
            // 둘로 나눈 절 (BT26-100) — 모두 읽을 수 있을 때만
            if (SplitClause69(t) is { } parts)
            {
                if (parts.Any(p => TryParse68(p, inherited, card, null) != null)) return err;
                if (def != null) foreach (var p in parts) TryParse68(p, inherited, card, def);
                return null;
            }
            var (alt, zone) = ClauseRewrite69(t, card);
            if (alt == t) return err;
            if (TryParse69Inner(alt, inherited, card, null, zone) != null) return err;
            if (def != null) TryParse69Inner(alt, inherited, card, def, zone);
            return null;
        }
        finally { _depth69--; }
    }

    private static string? TryParse69Inner(string clause, bool inherited, CardData card, CardScriptDefinition? def, int zone)
    {
        if (zone == 0) return TryParse68(clause, inherited, card, def);
        int outer = _zone68;
        _zone68 = zone;
        try { return TryParse67(clause, inherited, card, def); }
        finally { _zone68 = outer; }
    }

    // ---- 앞면 시큐리티의 지속 효과 ----

    // "[Security] [All Turns] All of your [Royal Base] trait Digimon get +1000 DP." · "[Security] [Opponent's Turn] All of your ~ gain ＜Blocker＞" (시점 · "When ~" 없는 지속 효과만)
    internal static readonly Regex FaceUpStaticRe69 = new(@"^(?:Security Effect\s*)?\[Security\]\s*(?<rest>\[(?:All Turns|Your Turn|Opponent's Turn)\]\s*(?!\[)(?!When\b)(?!At the\b)(?!If\b).+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>앞면 시큐리티에서 주는 지속 효과로 쓸 수 있는 것 (다른 카드에게 주는 효과 — 42차 [육성] 지속 효과와 같은 종류)</summary>
    private static readonly HashSet<StaticStat> FaceUpStaticStats69 = new()
    {
        StaticStat.AllyKeyword, StaticStat.AllyDigimonDp, StaticStat.AllySecurityAttack, StaticStat.EnemySecurityAttack, StaticStat.SecurityDigimonDp, StaticStat.EnemySecurityDigimonDp, StaticStat.EnemyKeyword,
    };

    /// <summary>
    /// "[Security]"를 뗀 문장을 임시 카드로 읽어, 지속 효과만 나오면 출처를 EffectSource.FaceUpSecurity68로 바꿔 등록한다 (엔진: GoldfishEngine.SecurityAuras69).
    /// 받는 디지몬마다 값이 달라지는 문장 ("for each of their ~" EX9-072)은 읽지 않는다 (앞의 규칙대로)
    /// </summary>
    private static bool FaceUpStatic69(string rawClause, CardData card, CardScriptDefinition? def)
    {
        var m = FaceUpStaticRe69.Match(Normalize58(rawClause));
        if (!m.Success) return false;
        string rest = m.Groups["rest"].Value.Trim();
        if (Regex.IsMatch(rest, @"\bfor each of their\b|\beach of them\b", RegexOptions.IgnoreCase)) return false;
        var tmp = new CardData
        {
            CardId = card.CardId + "#sec69", Name = card.Name, Type = CardType.Digimon, Level = card.Level, Color = card.Color, SecondaryColor = card.SecondaryColor, ExtraColors83 = card.ExtraColors83,
            Traits = card.Traits, EffectText = card.EffectText, InheritedEffectText = card.InheritedEffectText, PlayCost = card.PlayCost, DP = card.DP,
        };
        var tdef = new CardScriptBuilder().Card(tmp.CardId);
        _faceUp69++;
        try
        {
            if (TryParse69(rest, false, tmp, null) != null) return false;
            TryParse69(rest, false, tmp, tdef);
        }
        finally { _faceUp69--; }
        var s = tdef.Script41;
        bool onlyStatics = s.Effects.Count == 0 && s.Keywords.Count + s.InheritedKeywords.Count == 0 && s.SpecialEvolutions.Count == 0 && s.EvoCostModifiers.Count == 0 && s.FieldPlayCostModifiers.Count == 0
            && s.PlayCostOptions.Count == 0 && s.PlayCostReduction == null && s.TamerAuras.Count == 0 && s.GainedSourceEffects.Count == 0 && s.EvoOnlyInto.Count == 0;
        if (!onlyStatics || s.Statics.Count == 0 || s.Statics.Any(e => !FaceUpStaticStats69.Contains(e.Stat) || e.Source != EffectSource.Main)) return false;
        if (def != null)
            foreach (var st in s.Statics)
                def.Script41.Statics.Add(st with { Source = EffectSource.FaceUpSecurity68 });
        return true;
    }

    // ---- 둘로 나누기 ----

    // BT26-100 "[Security] [All Turns] All of your [Titan] trait Digimon gain ＜Blocker＞ (~) While you have a Digimon with [Plutomon] or [Titamon] in its name, they also get +3000 DP."
    internal static readonly Regex AlsoGetRe69 = new(@"^(?<labels>(?:\[[^\]]+\]\s*)*)(?<a>All of your (?<x>[^.]+?Digimon) (?:gain|get) [^.(]+?)(?:\s*\([^)]*\))?\.?\s+While (?<cond>[^,]+), they also (?<b>get [+-]\d+ DP)\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static string[]? SplitClause69(string clause)
    {
        var m = AlsoGetRe69.Match(clause.Trim());
        if (!m.Success) return null;
        string labels = m.Groups["labels"].Value;
        return new[]
        {
            $"{labels}{m.Groups["a"].Value.Trim()}.",
            $"{labels}While {m.Groups["cond"].Value}, all of your {m.Groups["x"].Value} {m.Groups["b"].Value}.",
        };
    }

    // ---- 표시 (값 묶음은 번호로 — 42차와 같은 표) ----

    private static string Mark69(string kind, params string[] values) => Mark68(kind + "69x", values);

    // ST22-10 "[Security] [All Turns] When any of your Digimon with ~ would leave the battle area other than by battle, by trashing this card, 1 of those Digimon doesn't leave."
    internal static readonly Regex SecurityGuardRe69 = new(@"^\[Security\]\s*(?<lab>\[All Turns\]\s*)When (?<ev>any of your .+? would leave the battle area(?: other than by (?:battle|your effects))?), by trashing this card, (?:1|one) of those Digimon doesn't leave\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // BT19-100 "[Security] [Opponent's Turn] When an opponent's Digimon attacks, if ~, for each of 1 of your [Mother D-Reaper]'s digivolution cards, the attacking Digimon get -1000 DP for the turn."
    internal static readonly Regex SecurityAttackedRe69 = new(@"^\[Security\]\s*(?<lab>\[Opponent's Turn\]\s*(?:\[Once Per Turn\]\s*)?)When an opponent's Digimon attacks, (?<cond>if [^,]+, )?for each of (?:1|one) of your (?<n>\[[^\]]+\])'s digivolution cards, the attacking Digimon gets? -(?<dp>\d+) DP for the turn\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // BT19-100 【시큐리티】 "You may play 1 [D-Reaper] trait card with a play cost equal to or lower than the number of digivolution cards of 1 of your [Mother D-Reaper]'s from your hand without paying the cost."
    internal static readonly Regex SourceCountPlayRe69 = new(@"\bYou may play (?<p>1 [^.]+?) with a play cost equal to or lower than the number of digivolution cards of (?:1|one) of your (?<n>\[[^\]]+\])'s from your hand without paying (?:the|its) cost\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // EX10-068 "Then, by returning 1 Digimon card from your opponent's trash to the bottom of the deck, from your hand or trash and without paying the cost, you may play 1 level 4 or lower Digimon card with the same color as the card this effect returned."
    internal static readonly Regex SameColorPlayRe69 = new(@"\bfrom your hand or trash and without paying (?:the|its) cost, you may play (?<p>1 [^.]+?) with the same colou?r as the card this effect returned\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // BT11-094 "you may play 1 [Angewomon] or [LadyDevimon] with a different name than the Digimon you digivolved into from your hand without paying the cost."
    internal static readonly Regex DiffNamePlayRe69 = new(@"\byou may play (?<p>1 \[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*) with a different name than the Digimon you digivolved into from your hand without paying (?:the|its) cost\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // BT11-093 "~ isn't affected by your opponent's Option cards until the end of your opponent's turn" → 원래 규칙의 "Option card effects"
    internal static readonly Regex OptionCardsRe69 = new(@"\b(?<a>isn't|is not|aren't|are not) affected by your opponent's Option cards (?=until\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // BT20-053 "Then, if during an attack until the end of your opponent's turn, 1 of your Digimon ~" — 공식 "그 후, 어택 중이라면, 상대의 턴 종료까지 ~" (조건 뒤의 쉼표가 빠진 영어 데이터)
    internal static readonly Regex DuringAttackRe69 = new(@"\bif during an attack until (?=the end of |your opponent's turn ends)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // BT11-089 "When you play a red Digimon with ~ in any of its traits (other than [Sea Animal] trait), by an effect, by suspending ~" — 공식 "~ 레드인 자신의 디지몬이 효과로 등장했을 때"
    internal static readonly Regex OtherThanByEffectRe69 = new(@"\bWhen you play (?<a>an? [^,\[]*?Digimon with )(?<list>\[[^\]]+\](?:, \[[^\]]+\])+,? or \[[^\]]+\])(?<b> in any of its traits) \(other than (?<x>\[[^\]]+\])(?: trait)?\), by an effect, ", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>같은 뜻 다른 표기 · 이 묶음의 동작 표시 (zone: [트래시] 형식으로 읽은 시점 효과가 실제로 있는 곳 — 1 앞면 시큐리티)</summary>
    internal static (string Text, int Zone) ClauseRewrite69(string rawClause, CardData? card)
    {
        string s = Regex.Replace(rawClause.Trim(), @"\.""$", ".");   // BT11-093 영어 데이터 끝의 따옴표
        Match m;
        if ((m = SecurityGuardRe69.Match(s)).Success && card is not { Type: CardType.Tamer })
            return ($"[Trash] {m.Groups["lab"].Value}When {m.Groups["ev"].Value}, {Mark69("secguard", "1")}.", 1);
        if ((m = SecurityAttackedRe69.Match(s)).Success && card is not { Type: CardType.Tamer })
            return ($"[Trash] {m.Groups["lab"].Value}When an opponent's Digimon attacks, {m.Groups["cond"].Value}{Mark69("srcdp", m.Groups["n"].Value, m.Groups["dp"].Value)}.", 1);
        s = SourceCountPlayRe69.Replace(s, x => Mark69("srcplay", x.Groups["p"].Value, x.Groups["n"].Value) + ".");
        s = SameColorPlayRe69.Replace(s, x => Mark69("samecolor", x.Groups["p"].Value) + ".");
        s = DiffNamePlayRe69.Replace(s, x => Mark69("diffname", x.Groups["p"].Value) + ".");
        s = OptionCardsRe69.Replace(s, x => $"{x.Groups["a"].Value} affected by your opponent's Option card effects ");
        s = DuringAttackRe69.Replace(s, "if during an attack, until ");
        s = OtherThanByEffectRe69.Replace(s, x => $"When you play {x.Groups["a"].Value}{Regex.Replace(x.Groups["list"].Value, @",? (?:or )?(?=\[)", " or ")}{x.Groups["b"].Value} other than {x.Groups["x"].Value} by an effect, ");   // 쉼표 목록은 사건 문장이 읽지 못한다 → "or"로
        return (s, 0);
    }

    /// <summary>렌더러용: 바꿔 쓴 절 (zone 없이)</summary>
    internal static string ClauseRewriteText69(string rawClause, CardData? card = null) => ClauseRewrite69(rawClause, card).Text;

    // ---- 조건 (ParseEventConditionChain 끝 — 원래 규칙이 읽지 못한 조건만) ----

    private static Func<EffectContext, bool>? ParseEventCondition69(string cond)
    {
        string c0 = cond.Trim();
        Match m;
        // BT19-100 "all of your Digimon and Tamers have the [D-Reaper] trait" — 배틀 에어리어의 자신의 디지몬과 테이머 전부 (공식 Q&A BT19-100 Q1)
        if ((m = Regex.Match(c0, @"^all of your Digimon and Tamers have the \[(?<t>[^\]]+)\] trait$", RegexOptions.IgnoreCase)).Success)
        {
            string tr = m.Groups["t"].Value;
            return c => c.Owner.BattleArea.Where(d => d.TamerProxyOf == null).All(d => TargetSpec.HasTrait(d, tr)) && c.Owner.Tamers.All(x => TargetSpec.HasTrait(x, tr));
        }
        // BT11-093 "it digivolved into a Digimon with the same level" — 진화한 디지몬 (Other)의 맨 위 카드와 바로 아래 카드 (진화하기 전의 맨 위)의 Lv.가 같다
        if (Regex.IsMatch(c0, @"^(?:it|that Digimon) digivolved into a Digimon with the same level$", RegexOptions.IgnoreCase))
            return c => (c.Other ?? c.Self) is { } d && d.EvolutionSources.Count > 0 && d.EvolutionSources.Peek().Level == d.TopCard.Level && d.TopCard.Level > 0;
        // BT22-093 "it has a digivolution card with the same level as the digivolved Digimon" — 공식 "그 디지몬의 진화원에 진화한 디지몬과 Lv.이 같은 카드가 있다면" (Q&A BT22-093 Q1)
        if (Regex.IsMatch(c0, @"^it has a digivolution card with the same level as the digivolved Digimon$", RegexOptions.IgnoreCase))
            return c => (c.Other ?? c.Self) is { } d && d.TopCard.Level > 0 && d.EvolutionSources.Any(x => x.Level == d.TopCard.Level);
        return null;
    }

    // ---- 동작 ----

    /// <summary>이 묶음의 동작 (원래 규칙이 읽지 못한 문장만 — ParseStep에서 앞의 묶음 다음)</summary>
    private static (Action<EffectContext>? Act, string? Error)? ParseAction69(string s, CardData card, List<EffectTrigger> triggers)
    {
        string label = _zone68 == 1 ? "시큐 (앞면)" : _zone68 == 2 ? "패의 이 카드"
            : string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        s = Regex.Replace(s.Trim().TrimEnd('.'), @"^Then,?\s+", "", RegexOptions.IgnoreCase);
        Match m;
        // BT19-084 "digivolve 1 of your Digimon into a Digimon card in your face-up security cards" (진화 코스트를 낸다)
        if ((m = Regex.Match(s, @"^(?:digivolve (?:1|one) of your Digimon into|(?:1|one) of your Digimon (?<may>may )?digivolve into) (?:an?|1) Digimon card in your face[- ]up security (?:cards|stack)$", RegexOptions.IgnoreCase)).Success)
            return (c => FaceUpSecurityEvolve69(c, label), null);
        // BT22-090 · BT22-101 "this Tamer may digivolve into [LordKnightmon] in the hand with the digivolution cost reduced by 3" — 진화 조건은 만족해야 한다 (공식 Q&A BT22-090 Q1)
        if (card.Type == CardType.Tamer
            && (m = Regex.Match(s, @"^this Tamer may digivolve into (?<p>.+?) in (?:the|your) hand with the (?:digivolution |memory )?cost reduced by (?<n>\d+)$", RegexOptions.IgnoreCase)).Success
            && (ParseNamePick("1 " + m.Groups["p"].Value) ?? ParseCardPick("1 " + Regex.Replace(m.Groups["p"].Value, @"^an? ", "", RegexOptions.IgnoreCase))) is { } tp)
        {
            var match = tp.Match;
            int red = int.Parse(m.Groups["n"].Value);
            return (c => TamerEvolveReduced69(c, label, match, red), null);
        }
        // BT12-090 "digivolve that Digimon into a Digimon card with [Imperialdramon] in its name in your hand for its digivolution cost" (공식 "~ 진화시킬 수 있다") ·
        // BT22-102 "digivolve it into a Digimon card with the [Night Claw], ~ trait in the trash with the digivolution cost reduced by 2" (공식 "~ 진화시킨다")
        if ((m = Regex.Match(s, @"^digivolve (?<who>that Digimon|it|this Digimon) into (?<pick>.+?) in (?:your |the )?(?<from>hand|trash) (?:for (?:its|the) digivolution cost|with the (?:digivolution )?cost reduced by (?<r>\d+))$", RegexOptions.IgnoreCase)).Success
            && (ParseCardPick("1 " + Regex.Replace(m.Groups["pick"].Value, @"^(?:an?|1) ", "", RegexOptions.IgnoreCase)) ?? ParseNamePick("1 " + Regex.Replace(m.Groups["pick"].Value, @"^(?:an?|1) ", "", RegexOptions.IgnoreCase))) is { } ep)
        {
            var match = ep.Match;
            bool self = m.Groups["who"].Value.Equals("this Digimon", StringComparison.OrdinalIgnoreCase);
            bool trash = m.Groups["from"].Value.Equals("trash", StringComparison.OrdinalIgnoreCase);
            int red = m.Groups["r"].Success ? int.Parse(m.Groups["r"].Value) : 0;
            bool optional = !trash && red == 0;   // 공식 한글: BT12-090 "진화시킬 수 있다" · BT22-102 "진화시킨다"
            return (c => EvolveThat69(c, label, self, trash, match, red, optional), null);
        }
        var mk = Regex.Match(s, @"^⟦(?<k>[a-z]+)69x68:(?<id>\d+)⟧$");
        if (!mk.Success || Spec68(int.Parse(mk.Groups["id"].Value)) is not { } v) return null;
        switch (mk.Groups["k"].Value)
        {
            case "secguard":
                if (_zone68 != 1) return (null, "미지원: 앞면 시큐리티의 이 카드만");
                return (c => SecurityGuard69(c, label), null);
            case "srcdp":
            {
                string name = TargetSpec.Names(v[0]).FirstOrDefault() ?? "";
                int dp = int.Parse(v[1]);
                return (c => SourceCountDebuff69(c, label, name, dp), null);
            }
            case "srcplay":
            {
                var pick = ParseCardPick(v[0]) ?? ParseNamePick(v[0]);
                if (pick == null) return (null, $"미지원 등장 조건 ({v[0]})");
                string name = TargetSpec.Names(v[1]).FirstOrDefault() ?? "";
                var match = pick.Value.Match;
                return (c => SourceCountPlay69(c, label, match, name), null);
            }
            case "samecolor":
            {
                var pick = ParseCardPick(v[0]) ?? ParseNamePick(v[0]);
                if (pick == null) return (null, $"미지원 등장 조건 ({v[0]})");
                var match = pick.Value.Match;
                return (c => SameColorPlay69(c, label, match), null);
            }
            case "diffname":
            {
                var names = TargetSpec.Names(v[0]);
                return (c => DiffNamePlay69(c, label, names), null);
            }
        }
        return null;
    }

    // ---- BT19-084 앞면 시큐리티의 카드로 진화 ----

    /// <summary>
    /// 자신의 디지몬 1마리를 자신의 앞면 시큐리티의 디지몬 카드로 진화시킨다 (진화 조건 · 진화 코스트 — 진화할 수 있는 카드가 있으면 한다).
    /// 추천: Lv. · DP가 가장 높은 카드
    /// </summary>
    private static void FaceUpSecurityEvolve69(EffectContext c, string label)
    {
        bool CanInto(CardInstance d, CardData x) => !EffectRules.IsDigivolveLocked(c.Engine.State, d) && CardScripts.EvoAllowed(d, x) && ActionValidator.ValidateEvolution(d, x).CanExecute;
        var bodies = c.Owner.BattleArea.Where(d => d.TamerProxyOf == null).ToList();
        var pool = GoldfishEngine.FaceUpCards(c.Owner).Where(x => x.Type == CardType.Digimon && bodies.Any(d => CanInto(d, x)))
            .Distinct().OrderByDescending(x => x.Level).ThenByDescending(x => x.DP).ToList();
        if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, "앞면 시큐리티에 진화할 수 있는 디지몬 카드가 없습니다"); return; }
        var chosen = pool.Count == 1 ? pool[0] : c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, pool, 1, upTo: false, $"[{c.Card.Name}] 진화할 앞면 시큐리티의 디지몬 카드를 고르세요.").FirstOrDefault() ?? pool[0];
        var targets = bodies.Where(d => CanInto(d, chosen)).OrderByDescending(d => d.TopCard.Level).ThenByDescending(d => d.EvolutionSources.Count).ToList();
        if (targets.Count == 0) { c.LastStepResolved = false; return; }
        var target = targets.Count == 1 ? targets[0] : c.Engine.ChooseTargets(c.Owner, targets, 1, $"[{c.Card.Name}] [{chosen.Name}]으로 진화할 자신의 디지몬을 고르세요.", optional: false, preselected: targets[0]).FirstOrDefault() ?? targets[0];
        int cost = ActionValidator.EvolutionCost(target, chosen);
        string before = target.TopCard.Name;
        int serial = c.Engine.EffectEvolveSerial40;
        c.Engine.EvolveByEffect(c.Owner, target, chosen, cost, from: c.Owner.SecurityStack);
        if (c.Engine.EffectEvolveSerial40 == serial) { c.LastStepResolved = false; return; }
        c.Owner.FaceUpSecurity.Remove(chosen);
        c.Engine.NotifySecurityRemoved(c.Owner, chosen);
        c.LastTargets = new List<CardInstance> { target };
        Diag46.Hit("faceUpSecEvolve69");
        Emit(c, label, $"[{before}] → 앞면 시큐리티의 [{chosen.Name}] 진화 (코스트 {cost})");
    }

    // ---- BT12-090 · BT22-102 그 디지몬을 패 · 트래시의 카드로 진화 ----

    /// <summary>
    /// 그 디지몬 (어택한 디지몬 — Other, "this Digimon"이면 Self)을 패 · 트래시의 조건에 맞는 디지몬 카드로 진화 (진화 조건 · 진화 코스트 -red).
    /// optional이면 고를 수 있다 (추천: 낸 뒤 메모리가 0 이상). 추천 카드: Lv. · DP가 가장 높은 것
    /// </summary>
    private static void EvolveThat69(EffectContext c, string label, bool self, bool trash, Func<CardData, bool> match, int red, bool optional)
    {
        var d = self ? c.Self : c.Other;
        if (d == null || !c.Owner.BattleArea.Contains(d) || EffectRules.IsDigivolveLocked(c.Engine.State, d)) { c.LastStepResolved = false; return; }
        var zone = trash ? c.Owner.Trash : c.Owner.Hand;
        var pool = zone.Where(x => x.Type == CardType.Digimon && match(x) && CardScripts.EvoAllowed(d, x) && ActionValidator.ValidateEvolution(d, x).CanExecute)
            .Distinct().OrderByDescending(x => x.Level).ThenByDescending(x => x.DP).ToList();
        string where = trash ? "트래시" : "패";
        if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, $"{where}에 [{d.TopCard.Name}]이(가) 진화할 수 있는 카드가 없습니다"); return; }
        int CostOf(CardData x) => Math.Max(0, ActionValidator.EvolutionCost(d, x) - red);
        var x0 = pool[0];
        if (optional && !c.Engine.AskYesNo(c.Owner, d, $"[{c.Card.Name}] [{d.TopCard.Name}]을(를) {where}의 카드로 진화시킬까요? (진화 코스트 {CostOf(x0)})", c.OwnerMemory - CostOf(x0) >= 0)) { c.LastStepResolved = false; return; }
        if (pool.Count > 1) x0 = c.Engine.ChooseCards(c.Owner, trash ? CardChoiceZone.Trash : CardChoiceZone.Hand, pool, 1, upTo: false, $"[{c.Card.Name}] [{d.TopCard.Name}]이(가) 진화할 {where}의 카드를 고르세요{(red > 0 ? $" (진화 코스트 -{red})" : "")}.").FirstOrDefault() ?? x0;
        int cost = CostOf(x0);
        string before = d.TopCard.Name;
        int serial = c.Engine.EffectEvolveSerial40;
        c.Engine.EvolveByEffect(c.Owner, d, x0, cost, from: zone);
        if (c.Engine.EffectEvolveSerial40 == serial) { c.LastStepResolved = false; return; }
        c.LastTargets = new List<CardInstance> { d };
        Diag46.Hit("evolveThat69");
        Emit(c, label, $"[{before}] → {where}의 [{x0.Name}] 진화 (코스트 {cost})");
    }

    // ---- BT22-090 · BT22-101 이 테이머를 패의 카드로 진화 (코스트 -N) ----

    /// <summary>
    /// 이 테이머를 패의 카드로 진화시킬 수 있다 — 테이머에서 진화하는 진화 조건 (〔진화〕 「키시베 리에」 등 · 필드 조건 포함)을 만족하는 카드만, 진화 코스트 -N.
    /// </summary>
    private static void TamerEvolveReduced69(EffectContext c, string label, Func<CardData, bool> match, int red)
    {
        var tamer = c.Card;
        if (tamer == null || !c.Owner.Tamers.Contains(tamer)) { c.LastStepResolved = false; return; }
        var pool = c.Owner.Hand.Where(x => x.Type == CardType.Digimon && match(x)).Distinct()
            .Select(x => (Card: x, Cost: c.Engine.TamerEvolutionCostFor(c.Owner, x, tamer)))
            .Where(p => p.Cost != null).Select(p => (p.Card, Cost: Math.Max(0, p.Cost!.Value - red)))
            .OrderByDescending(p => p.Card.Level).ThenBy(p => p.Cost).ToList();
        if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, "진화 조건을 만족하는 패의 카드가 없습니다"); return; }
        var (evo, cost) = pool[0];
        int after = c.OwnerMemory - cost;
        // 추천: 턴 종료 시 (BT22-090 — 비용을 이미 냈고, 메모리는 이미 상대 쪽)는 늘, 그 밖 (BT22-101)은 메모리가 상대 쪽으로 넘어가지 않을 때
        if (!c.Engine.AskYesNo(c.Owner, null, $"[{tamer.Name}] 이 테이머를 패의 [{evo.Name}]으로 진화시킬까요? (진화 코스트 {cost} — 메모리 {c.OwnerMemory} → {after})", c.Trigger == EffectTrigger.EndOfYourTurn || after >= 0)) { c.LastStepResolved = false; return; }
        if (pool.Count > 1)
        {
            var pick = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, pool.Select(p => p.Card).ToList(), 1, upTo: false, $"[{tamer.Name}] 진화할 카드를 고르세요 (진화 코스트 -{red}).").FirstOrDefault();
            if (pick != null) (evo, cost) = pool.First(p => ReferenceEquals(p.Card, pick));
        }
        if (c.Engine.EvolveTamerByEffect(c.Owner, tamer, evo, cost) is not { } inst) { c.LastStepResolved = false; return; }
        c.LastTargets = new List<CardInstance> { inst };
        Diag46.Hit("tamerEvolveReduced69");
        Emit(c, label, $"이 테이머를 [{evo.Name}]으로 진화 (코스트 {cost})");
    }

    /// <summary>v0.69.0 "by ~, this Tamer may digivolve into ~ in the hand ~" (BT22-090) — AI: 진화 조건을 만족하는 패의 카드가 있을 때만 비용을 낸다</summary>
    private static Func<EffectContext, bool> CostForRest69(string costText, string rest, Func<EffectContext, bool> recommend)
    {
        var m = Regex.Match(rest.Trim(), @"^this Tamer may digivolve into (?<p>.+?) in (?:the|your) hand with the (?:digivolution |memory )?cost reduced by \d+\.?$", RegexOptions.IgnoreCase);
        if (!m.Success || (ParseNamePick("1 " + m.Groups["p"].Value) ?? ParseCardPick("1 " + Regex.Replace(m.Groups["p"].Value, @"^an? ", "", RegexOptions.IgnoreCase))) is not { } pick) return recommend;
        var match = pick.Match;
        return c => c.Card != null && c.Owner.Tamers.Contains(c.Card)
            && c.Owner.Hand.Any(x => x.Type == CardType.Digimon && match(x) && c.Engine.TamerEvolutionCostFor(c.Owner, x, c.Card) != null);
    }

    // ---- ST22-10 앞면 시큐리티의 이 카드를 파기하는 것으로 떠나지 않는다 ----

    /// <summary>
    /// 앞면 시큐리티의 이 카드를 파기하는 것으로, 떠나려는 그 디지몬 1마리는 떠나지 않는다 ("~하는 것으로"는 고를 수 있다 — 추천: 늘 낸다).
    /// 효과로 시큐리티에서 파기한 것이므로 이 카드의 "시큐리티에서 효과로 파기되었을 때" (【시큐리티】 효과 발휘)도 유발한다 (공식 Q&amp;A ST22-10 Q1)
    /// </summary>
    private static void SecurityGuard69(EffectContext c, string label)
    {
        var card = c.Card;
        var who = c.Other;
        int at = card == null ? -1 : c.Owner.SecurityStack.IndexOf(card);
        if (card == null || who == null || at < 0 || !GoldfishEngine.IsFaceUp(c.Owner, card) || !c.Owner.BattleArea.Contains(who)) { c.LastStepResolved = false; return; }
        if (c.Engine.CurrentLeave is { CanPrevent: false }) { c.LastStepResolved = false; return; }
        if (!c.Engine.AskYesNo(c.Owner, who, $"[{card.Name}] 앞면 시큐리티의 이 카드를 파기하여 [{who.TopCard.Name}]이(가) 떠나지 않게 할까요?", true)) { c.LastStepResolved = false; return; }
        c.Owner.SecurityStack.RemoveAt(at);
        c.Owner.FaceUpSecurity.Remove(card);
        c.Owner.Trash.Add(card);
        c.Engine.NotifySecurityRemoved(c.Owner, card, trashed: true);
        c.PreventLeave = true;
        Diag46.Hit("securityGuard69");
        Emit(c, label, $"앞면 시큐리티의 이 카드를 파기 → [{who.TopCard.Name}]은(는) 떠나지 않는다");
    }

    // ---- BT19-100 「마더 디・리퍼」의 진화원 1장마다 ----

    /// <summary>자신의 「name」 1마리의 진화원 매수 (배틀 에어리어 — 여러 마리면 고른다, 추천: 가장 많은 것)</summary>
    private static int SourceCount69(EffectContext c, string name, string label)
    {
        // v0.76.0 바로잡음: 육성 에어리어의 「마더 디・리퍼」는 세지 않는다 — 육성 에어리어를 지정하지 않은 효과는 그 카드를 고르거나 정보를 참조할 수 없다 (종합 룰 3-4-7-5 · 3-4-7-8).
        //  같은 문형의 EX2-053 · EX2-054 · EX2-055 · BT19-078 (배틀 에어리어만)과 같다
        var hosts = c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && CardNames.Is(d, name))
           .OrderByDescending(d => d.EvolutionSources.Count).ToList();
        if (hosts.Count == 0) return -1;
        var h = hosts.Count == 1 || hosts[0].EvolutionSources.Count == hosts[^1].EvolutionSources.Count ? hosts[0]
            : c.Engine.ChooseTargets(c.Owner, hosts, 1, $"[{label}] 진화원 매수를 셀 자신의 [{name}]을(를) 고르세요.", optional: false, preselected: hosts[0]).FirstOrDefault() ?? hosts[0];
        return h.EvolutionSources.Count;
    }

    private static void SourceCountDebuff69(EffectContext c, string label, string name, int dp)
    {
        var target = c.Other;
        if (target == null || !c.Enemy.BattleArea.Contains(target)) { c.LastStepResolved = false; return; }
        int n = SourceCount69(c, name, label);
        if (n <= 0) { c.LastStepResolved = false; Emit(c, label, $"자신의 [{name}]의 진화원이 없습니다"); return; }
        if (c.Engine.IsUnaffectedBy(c.Owner, target)) { c.LastStepResolved = false; Emit(c, label, $"[{target.TopCard.Name}]은(는) 효과를 받지 않습니다"); return; }
        Diag46.Hit("sourceCountDebuff69");
        Emit(c, label, $"[{name}]의 진화원 {n}장 → 어택한 [{target.TopCard.Name}] DP -{dp * n:N0}");
        c.Engine.ApplyDpMinus(target, dp * n, c.Card.Name);
    }

    private static void SourceCountPlay69(EffectContext c, string label, Func<CardData, bool> match, string name)
    {
        int n = SourceCount69(c, name, label);
        if (n < 0) { c.LastStepResolved = false; Emit(c, label, $"자신의 [{name}]이(가) 없습니다"); return; }
        var pool = c.Owner.Hand.Where(x => x.Type is CardType.Digimon or CardType.Tamer && match(x) && x.PlayCost <= n && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Hand))
            .Distinct().OrderByDescending(x => x.PlayCost).ThenByDescending(x => x.Level).ToList();
        if (pool.Count == 0 || c.Owner.BattleArea.Count >= 12) { c.LastStepResolved = false; Emit(c, label, $"등장 코스트 {n} 이하로 등장시킬 카드가 없습니다"); return; }
        var x0 = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, pool, 1, upTo: true, $"[{c.Card.Name}] 코스트 없이 등장시킬 카드를 고르세요 (등장 코스트 {n} 이하 — 고르지 않아도 됩니다).").FirstOrDefault();
        if (x0 == null) { c.LastStepResolved = false; return; }
        if (!c.Engine.TryPlayByEffect(c.Owner, x0, PlayOrigin.Hand, () => c.Owner.Hand.Remove(x0))) { c.LastStepResolved = false; return; }
        Diag46.Hit("sourceCountPlay69");
        Emit(c, label, $"패의 [{x0.Name}] 코스트 없이 등장 ([{name}]의 진화원 {n}장)");
    }

    // ---- EX10-068 이 효과로 되돌린 카드와 같은 색 ----

    private static void SameColorPlay69(EffectContext c, string label, Func<CardData, bool> match)
    {
        if (c.RefCard38 is not { } back) { c.LastStepResolved = false; return; }
        var colors = back.AllColors;
        bool Same(CardData x) => x.AllColors.Any(colors.Contains);   // 공식 Q&A EX10-068 Q1: 되돌린 카드의 색 중 하나를 포함하면 된다
        var pool = c.Owner.Hand.Select(x => (Card: x, Hand: true)).Concat(c.Owner.Trash.Select(x => (Card: x, Hand: false)))
            .Where(p => p.Card.Type == CardType.Digimon && match(p.Card) && Same(p.Card) && c.Engine.CanPlayByEffect(c.Owner, p.Card, p.Hand ? PlayOrigin.Hand : PlayOrigin.Trash))
            .GroupBy(p => p.Card).Select(g => g.First()).OrderByDescending(p => p.Card.Level).ThenByDescending(p => p.Card.DP).ToList();
        string col = string.Join("/", colors.Select(ColorKo66));
        if (pool.Count == 0 || c.Owner.BattleArea.Count >= 12) { c.LastStepResolved = false; Emit(c, label, $"{col}인 등장시킬 카드가 없습니다"); return; }
        var cards = pool.Select(p => p.Card).ToList();
        var x0 = c.Engine.ChooseCards(c.Owner, CardChoiceZone.HandOrTrash, cards, 1, upTo: true, $"[{c.Card.Name}] 되돌린 [{back.Name}]과(와) 같은 색 ({col})의 카드를 고르세요 (고르지 않아도 됩니다).").FirstOrDefault();
        if (x0 == null) { c.LastStepResolved = false; return; }
        bool fromHand = pool.First(p => ReferenceEquals(p.Card, x0)).Hand;
        var zone = fromHand ? c.Owner.Hand : c.Owner.Trash;
        if (!c.Engine.TryPlayByEffect(c.Owner, x0, fromHand ? PlayOrigin.Hand : PlayOrigin.Trash, () => zone.Remove(x0))) { c.LastStepResolved = false; return; }
        Diag46.Hit("sameColorPlay69");
        Emit(c, label, $"{(fromHand ? "패" : "트래시")}의 [{x0.Name}] 코스트 없이 등장 (되돌린 [{back.Name}]과(와) 같은 색 {col})");
    }

    // ---- BT11-094 진화한 디지몬과 명칭이 서로 다른 카드 ----

    private static void DiffNamePlay69(EffectContext c, string label, List<string> names)
    {
        var d = c.Other;
        if (d == null) { c.LastStepResolved = false; return; }
        string into = CardNames.MatchName(d);
        var pool = c.Owner.Hand.Where(x => x.Type == CardType.Digimon && names.Any(n => CardNames.Is(x, n)) && !CardNames.Is(x, into) && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Hand))
            .Distinct().OrderByDescending(x => x.Level).ToList();
        if (pool.Count == 0 || c.Owner.BattleArea.Count >= 12) { c.LastStepResolved = false; Emit(c, label, $"[{into}]과(와) 명칭이 다른 등장시킬 카드가 없습니다"); return; }
        var x0 = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, pool, 1, upTo: true, $"[{c.Card.Name}] [{into}]과(와) 명칭이 다른 카드를 고르세요 (고르지 않아도 됩니다).").FirstOrDefault();
        if (x0 == null) { c.LastStepResolved = false; return; }
        if (!c.Engine.TryPlayByEffect(c.Owner, x0, PlayOrigin.Hand, () => c.Owner.Hand.Remove(x0))) { c.LastStepResolved = false; return; }
        Diag46.Hit("diffNamePlay69");
        Emit(c, label, $"패의 [{x0.Name}] 코스트 없이 등장 ([{into}]과(와) 명칭이 다르다)");
    }
}
