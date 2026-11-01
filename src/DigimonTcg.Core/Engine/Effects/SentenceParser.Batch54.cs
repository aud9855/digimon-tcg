using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.54.0 미지원 문장 28차 묶음: 원래 규칙(1~27차)이 읽지 못한 절만 28차 규칙으로 다시 읽는다.
///  - 진화할 때 지불하는 코스트 감소의 진화할 카드 (Into54): "그 카드의 색 1색마다" (BT11-064) · "명칭에 ~을 포함하거나 특징으로 ~" (BT14-013) ·
///    "트래시의 디지몬 카드" (BT9-077) · "패의 ~" (EX1-061 · ST13-04) · "2색 또는 특징" (P-076) · {시큐리티} 앞면 시큐리티의 효과 (P-181)
/// 렌더러도 같은 표 (KoreanEffectRenderer.Batch54.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _clauseDepth54;

    private static string? TryParse54(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse53(rawClause, inherited, card, def);
        if (err == null || _clauseDepth54 > 0) return err;
        _clauseDepth54++;
        try
        {
            if (ClauseSpecial54(rawClause, inherited, card, def) is { } special) return special.Length == 0 ? null : err;
            string alt = ClauseRewrite54(rawClause);
            if (alt == rawClause || TryParse53(alt, inherited, card, null) != null) return err;
            if (def != null) TryParse53(alt, inherited, card, def);
            return null;
        }
        finally { _clauseDepth54--; }
    }

    /// <summary>같은 뜻 다른 표기 — 절 전체에 차례로 (렌더러도 쓴다)</summary>
    internal static readonly Func<string, string>[] ClauseRules54 =
    {
        // P-095 "Until the end of your opponent's turn, 1 of your opponent's Digimon gets -6000 DP and that Digimon's [When Digivolving] effects don't activate."
        s => Regex.Replace(s, @"\b(?<dur>Until the end of your opponent's turn), (?<t>1 of your opponent's Digimon) gets (?<v>[+-]\d+) DP and that Digimon's \[When Digivolving\] effects don't activate\.",
            "${dur}, ${t} gets ${v} DP. ${dur}, it can't activate [When Digivolving] effects.", RegexOptions.IgnoreCase),
    };

    internal static string ClauseRewrite54(string rawClause)
    {
        string s = rawClause;
        foreach (var f in ClauseRules54) s = f(s);
        return s;
    }

    /// <summary>진화 코스트 감소 (28차): labels · 주어 · 진화할 카드 · 감소량 · "그 카드의 색 1색마다"</summary>
    internal static readonly Regex EvoReduce54 = new(
        @"^(?<labels>(?:\[(?:Security|Your Turn|Once Per Turn|Start of Your Main Phase)\]\s*)*)(?<turn>For the turn, )?When (?<who>this Digimon|any of your Digimon|one of your Digimon) would digivolve into (?<into>.+?), reduce the (?:memory |digivolution )?cost(?: of the digivolution)? by (?<n>\d+)(?<colors> for each of that card's colou?rs)?\.?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>진화할 카드: (조건, 있어야 하는 곳 — null = 어디든) · 읽지 못하면 null</summary>
    internal static (Func<CardData, bool> Match, GoldfishEngine.EvoOrigin54? From)? Into54(string into)
    {
        string s = into.Trim();
        GoldfishEngine.EvoOrigin54? from = null;
        Match m;
        if ((m = Regex.Match(s, @"^(?<a>an? (?:Digimon )?card) in your (?<z>hand|trash) that's (?<rest>.+)$", RegexOptions.IgnoreCase)).Success)
        {
            from = m.Groups["z"].Value.Equals("hand", StringComparison.OrdinalIgnoreCase) ? GoldfishEngine.EvoOrigin54.Hand : GoldfishEngine.EvoOrigin54.Trash;
            s = m.Groups["a"].Value + " that's " + m.Groups["rest"].Value;
        }
        else if ((m = Regex.Match(s, @"^(?<a>.+?) (?:from|in) your (?<z>hand|trash)$", RegexOptions.IgnoreCase)).Success)
        {
            from = m.Groups["z"].Value.Equals("hand", StringComparison.OrdinalIgnoreCase) ? GoldfishEngine.EvoOrigin54.Hand : GoldfishEngine.EvoOrigin54.Trash;
            s = m.Groups["a"].Value;
        }
        bool digimon = Regex.IsMatch(s, @"^an? Digimon card\b", RegexOptions.IgnoreCase);
        Func<CardData, bool> kind = digimon ? c => c.Type == CardType.Digimon : _ => true;
        // "a card with [Tyrannomon] in its name, or the [Dinosaur] or [Ceratopsian] trait"
        if ((m = Regex.Match(s, @"^an? (?:Digimon )?card with (?<n>\[[^\]]+\]) in its name,? or the (?<t>\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*) traits?$", RegexOptions.IgnoreCase)).Success)
        {
            var names = TargetSpec.Names(m.Groups["n"].Value);
            var traits = TargetSpec.Names(m.Groups["t"].Value);
            return (c => kind(c) && (names.Any(x => TargetSpec.NameHas(c, x)) || traits.Any(x => TargetSpec.HasTrait(c, x))), from);
        }
        // "a card that's black or has the [Legend-Arms] trait"
        if ((m = Regex.Match(s, $@"^an? (?:Digimon )?card that's (?<c>{Colors}) or has the (?<t>\[[^\]]+\]) trait$", RegexOptions.IgnoreCase)).Success
            && Enum.TryParse<CardColor>(m.Groups["c"].Value, true, out var color))
        {
            var traits = TargetSpec.Names(m.Groups["t"].Value);
            return (c => kind(c) && (c.AllColors.Contains(color) || traits.Any(x => TargetSpec.HasTrait(c, x))), from);
        }
        // "a card with 2 colors or [Composite] trait"
        if ((m = Regex.Match(s, @"^an? (?:Digimon )?card with (?<k>\d+) colou?rs or (?:the )?(?<t>\[[^\]]+\]) trait$", RegexOptions.IgnoreCase)).Success)
        {
            int k = int.Parse(m.Groups["k"].Value);
            var traits = TargetSpec.Names(m.Groups["t"].Value);
            return (c => kind(c) && (c.AllColors.Count == k || traits.Any(x => TargetSpec.HasTrait(c, x))), from);
        }
        // "a Digimon card" (트래시의 디지몬 카드 — BT9-077)
        if (Regex.IsMatch(s, @"^an? Digimon card$", RegexOptions.IgnoreCase)) return (kind, from);
        string body = Regex.Replace(s, @"^an? ", "1 ", RegexOptions.IgnoreCase);
        if ((ParseCardPick(body) ?? ParseNamePick(body)) is { } pick) return (pick.Match, from);
        return null;
    }

    /// <summary>절 하나로 읽는 규칙 — 읽었으면 "" (등록함), 해당 없으면 null</summary>
    private static string? ClauseSpecial54(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string t = rawClause.Replace(' ', ' ').Trim();
        if (EvoReduction54(t, inherited, card, def) is { } evo) return evo;
        if (GainLabeled54(t, inherited, card, def) is { } gain) return gain;
        if (DontActivate54(t, inherited, card, def) is { } dont) return dont;
        if (Xros54(t, inherited, card, def) is { } xros) return xros;
        return null;
    }

    internal static readonly Regex TamerOnPlayRe54 = new(@"^\[All Turns\]\s*While (?<cond>you have [^,]+), all of your opponent's Tamers(?:'s?)? \[On Play\] effects don't activate\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex LevelWhenDigivolvingRe54 = new(@"^\[All Turns\]\s*The \[When Digivolving\] effects on level (?<n>\d+) Digimon don't activate\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"[All Turns] While you have [Eosmon], all of your opponent's Tamers [On Play] effects don't activate." (BT17-092) · "[All Turns] The [When Digivolving] effects on level 7 Digimon don't activate." (BT5-085)</summary>
    private static string? DontActivate54(string t, bool inherited, CardData card, CardScriptDefinition? def)
    {
        if (inherited) return null;
        Match m;
        if ((m = TamerOnPlayRe54.Match(t)).Success && card.Type is CardType.Tamer or CardType.Digimon)
        {
            string ct = m.Groups["cond"].Value.Trim();
            Func<PlayerZones, bool>? cond = null;
            if (Regex.Match(ct, @"^you have (?<n>\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*)$", RegexOptions.IgnoreCase) is { Success: true } have)
            {
                var names = TargetSpec.Names(have.Groups["n"].Value);   // "자신의 「에오스몬」이 있는 동안" — 배틀 에어리어의 디지몬 · 테이머
                cond = z => z.BattleArea.Any(d => names.Any(x => TargetSpec.NameHas(d.TopCard, x))) || z.Tamers.Any(c => names.Any(x => TargetSpec.NameHas(c, x)));
            }
            cond ??= ParseZoneCondition(ct);
            if (cond == null) return null;
            if (def != null) def.Script41.BlockOpponentTamerOnPlay54 = (own, _) => cond(own);
            return "";
        }
        if ((m = LevelWhenDigivolvingRe54.Match(t)).Success && card.Type == CardType.Digimon)
        {
            if (def != null) def.Script41.BlockWhenDigivolvingLevel54 = int.Parse(m.Groups["n"].Value);
            return "";
        }
        return null;
    }

    /// <summary>"[Your Turn] This Digimon gains all [Main] effects of its digivolution cards with the [Flame] trait." (BT22-078) ·
    /// "[All Turns] This Digimon gains all [All Turns] effects on all level 6 [Bagra Army] trait Digimon cards in its digivolution cards." (EX10-059)</summary>
    internal static readonly Regex GainLabeledRe54 = new(
        @"^\[(?<turn>Your Turn|All Turns)\]\s*This Digimon gains all \[(?<label>Main|All Turns)\] effects (?:of|on) (?:all )?(?:its digivolution cards with the (?<t>\[[^\]]+\](?: or \[[^\]]+\])*) traits?|(?<spec>.+?) in (?:its|this Digimon's) digivolution cards)\.?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static Func<CardData, bool>? GainLabeledWhich54(Match m)
    {
        if (m.Groups["t"].Success)
        {
            var traits = TargetSpec.Names(m.Groups["t"].Value);
            return c => traits.Any(x => TargetSpec.HasTrait(c, x));
        }
        string spec = Regex.Replace(m.Groups["spec"].Value.Trim(), @"\bcards$", "card", RegexOptions.IgnoreCase);
        return ParseCardPick("1 " + spec) is { } pk ? pk.Match : null;
    }

    private static string? GainLabeled54(string t, bool inherited, CardData card, CardScriptDefinition? def)
    {
        var m = GainLabeledRe54.Match(t);
        if (!m.Success || inherited || card.Type != CardType.Digimon) return null;
        if (GainLabeledWhich54(m) is not { } which) return null;
        def?.GainLabeled54(which, "[" + m.Groups["label"].Value + "]", m.Groups["turn"].Value.Equals("Your Turn", StringComparison.OrdinalIgnoreCase));
        return "";
    }

    /// <summary>
    /// 진화 코스트 감소 (28차). 【자신의 메인 페이즈 개시 시】 "For the turn, ~" (BT14-013)는 그 턴 동안의 감소를 남기는 효과,
    /// [Security] (P-181 — 종합 룰 15-14-5-1: 시큐리티에 앞면으로 있는 동안)는 앞면 시큐리티 카드의 감소
    /// </summary>
    private static string? EvoReduction54(string t, bool inherited, CardData card, CardScriptDefinition? def)
    {
        var m = EvoReduce54.Match(t);
        if (!m.Success) return null;
        string labels = m.Groups["labels"].Value;
        bool security = labels.Contains("[Security]", StringComparison.OrdinalIgnoreCase);
        bool startMain = labels.Contains("[Start of Your Main Phase]", StringComparison.OrdinalIgnoreCase);
        bool forTurn = m.Groups["turn"].Success;
        if (startMain != forTurn) return null;   // "For the turn, ~"는 【자신의 메인 페이즈 개시 시】에만
        if (security && (inherited || startMain)) return null;
        if (startMain && (inherited || card.Type != CardType.Digimon)) return null;
        bool any = !m.Groups["who"].Value.Equals("this Digimon", StringComparison.OrdinalIgnoreCase);
        if (!any && card.Type != CardType.Digimon) return null;
        if (any && !security) return null;   // 자신의 디지몬 전부는 원래 규칙 — 28차는 {시큐리티}만
        if (Into54(m.Groups["into"].Value) is not { } into) return null;
        int n = int.Parse(m.Groups["n"].Value);
        Func<EffectContext, CardInstance, bool>? origin = into.From is { } from ? (c, _) => c.Engine.CurrentEvoOrigin54 == from : null;
        var mod = new EvoCostModifier(inherited ? EffectSource.Inherited : EffectSource.Main, any ? EvoCostSubject.AnyOwnDigimon : EvoCostSubject.ThisDigimon,
            origin, into.Match, null, null, n,
            OncePerTurn: labels.Contains("[Once Per Turn]", StringComparison.OrdinalIgnoreCase),
            YourTurnOnly: labels.Contains("[Your Turn]", StringComparison.OrdinalIgnoreCase) || startMain)
        {
            Times = m.Groups["colors"].Success ? (c, _) => c.TriggerCard?.AllColors.Count ?? 0 : null,   // "그 카드의 색 1색마다" (진화할 카드)
            FaceUpSecurity54 = security,
        };
        if (def == null) return "";
        if (startMain)
        {
            def.On(EffectTrigger.StartOfYourMainPhase, c =>
            {
                if (c.Self == null) return;
                c.Engine.AddTurnEvoCostModifier41(c.Owner, mod, c.Card, c.Self, oneShot: false);
                Emit(c, "자신의 메인 페이즈 개시 시", "이 턴 동안 진화 코스트를 줄입니다");
            });
        }
        else def.EvoCostModifier(mod);
        return "";
    }
}

/// <summary>v0.54.0 디지크로스 재료 (BT18-065 · BT19-087 · EX10-064 · EX4-062)</summary>
internal static partial class SentenceParser
{
    internal static readonly Regex XrosTrashOnlyRe54 = new(@"^While you have no Digimon other than (?<n>\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*), cards from your trash may also be placed for this card's DigiXros\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static readonly Regex XrosUnderAndTrashRe54 = new(
        @"^\[All Turns\]\s*When (?:any of your (?<spec>[^,]+?) Digimon cards with DigiXros requirements would be played|you would play 1 Digimon card with the (?<spec>[^,]+?) with DigiXros requirements), by suspending this Tamer, (?:you may place )?1 card (?:from )?under your Tamers and 1 card (?:from |in )your trash (?:can also be placed for their DigiXros|as digivolution cards for a DigiXros)\.?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"[Composite] or [Twilight] trait" · "[Blue Flare] trait or [Twilight] trait" → 특징 목록 (특징이 아니면 null)</summary>
    internal static List<string>? XrosTraits54(string spec)
    {
        if (!Regex.IsMatch(spec.Trim(), @"^(?:\[[^\]]+\](?: trait)?(?:,? (?:or )?)?)+$", RegexOptions.IgnoreCase) || !spec.Contains("trait", StringComparison.OrdinalIgnoreCase)) return null;
        var xs = TargetSpec.Names(spec);
        return xs.Count == 0 ? null : xs;
    }

    private static string? Xros54(string t, bool inherited, CardData card, CardScriptDefinition? def)
    {
        if (inherited) return null;
        Match m;
        if ((m = XrosTrashOnlyRe54.Match(t)).Success && card.Type == CardType.Digimon)
        {
            var names = TargetSpec.Names(m.Groups["n"].Value);
            def?.XrosFromTrash(z => z.BattleArea.All(d => names.Any(x => CardNames.Is(d.TopCard, x))));   // "「벰몬」 이외의 자신의 디지몬이 없는 동안"
            return "";
        }
        if ((m = XrosUnderAndTrashRe54.Match(t)).Success && card.Type == CardType.Tamer && XrosTraits54(m.Groups["spec"].Value) is { } traits)
        {
            if (def != null) def.Script41.XrosUnderTamersAndTrash54 = c => c.Type == CardType.Digimon && traits.Any(x => TargetSpec.HasTrait(c, x));
            return "";
        }
        return null;
    }
}
