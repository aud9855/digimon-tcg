using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.85.0 59차 묶음 — 테이머 아래 카드를 테이머마다 (PlayerZones.UnderTamerHosts85 · GoldfishEngine.UnderTamer85).
/// 이 파일: 테이머 아래에 놓을 곳 · 테이머 조건을 읽는 도우미.
///  - "under this Tamer" = 이 테이머 · "under 1 of / one of / any of your Tamers" · "under your Tamers" = 테이머 1명을 고른다 (종합 룰 16-20 ≪세이브≫ "자신의 테이머 1명의 밑에")
///  - 테이머 조건: "any of your [Glowing Dawn] trait Tamers" · "1 of your blue or red Tamers" · "any of your red Tamers with inherited effects" · "1 of your Tamers with the [General] trait"
///    (v0.85.0 전에는 테이머 아래 카드가 한 묶음이라 이 조건을 보지 않았다)
/// </summary>
internal static partial class SentenceParser
{
    private const RegexOptions I85 = RegexOptions.IgnoreCase;

    /// <summary>
    /// 테이머 조건 (테이머 카드에): "" · "red" · "red or blue" · "[X] trait" · "[X]" (명칭) · "with inherited effects" · "with the [X] trait" · "with [X] in its name" 조합. 읽지 못하면 null
    /// </summary>
    internal static Func<CardData, bool>? TamerFilter85(string? pre, string? post)
    {
        var fs = new List<Func<CardData, bool>>();
        string a = (pre ?? "").Trim();
        string b = (post ?? "").Trim();
        Match m;
        // 앞: 색 · [X] trait · [X] (명칭)
        while (a.Length > 0)
        {
            if ((m = Regex.Match(a, $@"^(?<c>{Colors}(?: or {Colors})*)\b\s*", I85)).Success)
            {
                var cols = Regex.Matches(m.Groups["c"].Value, Colors, I85).Select(x => Enum.Parse<CardColor>(x.Value, ignoreCase: true)).ToList();
                fs.Add(t => t.AllColors.Any(cols.Contains));
                a = a[m.Length..].Trim();
                continue;
            }
            if ((m = Regex.Match(a, @"^(?<x>\[[^\]]+\](?: or \[[^\]]+\])*) trait\s*", I85)).Success)
            {
                var xs = Regex.Matches(m.Groups["x"].Value, @"\[([^\]]+)\]").Select(x => x.Groups[1].Value.TrimEnd('≡')).ToList();
                fs.Add(t => xs.Any(x => TargetSpec.HasTrait(t, x)));
                a = a[m.Length..].Trim();
                continue;
            }
            if ((m = Regex.Match(a, @"^\[(?<x>[^\]]+)\]\s*", I85)).Success)
            {
                string x = m.Groups["x"].Value;
                fs.Add(t => CardNames.Is(t, x));
                a = a[m.Length..].Trim();
                continue;
            }
            return null;
        }
        // 뒤: with inherited effects · with the [X] trait · with [X] in its name
        while (b.Length > 0)
        {
            if ((m = Regex.Match(b, @"^(?:and )?with inherited effects\s*", I85)).Success)
            {
                fs.Add(t => !string.IsNullOrWhiteSpace(t.InheritedEffectText));
                b = b[m.Length..].Trim();
                continue;
            }
            if ((m = Regex.Match(b, @"^(?:and )?with the (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) trait\s*", I85)).Success)
            {
                var xs = Regex.Matches(m.Groups["x"].Value, @"\[([^\]]+)\]").Select(x => x.Groups[1].Value.TrimEnd('≡')).ToList();
                fs.Add(t => xs.Any(x => TargetSpec.HasTrait(t, x)));
                b = b[m.Length..].Trim();
                continue;
            }
            if ((m = Regex.Match(b, @"^(?:and )?with \[(?<x>[^\]]+)\] in (?:its|their) names?\s*", I85)).Success)
            {
                string x = m.Groups["x"].Value;
                fs.Add(t => TargetSpec.NameHas(t, x));
                b = b[m.Length..].Trim();
                continue;
            }
            return null;
        }
        return fs.Count == 0 ? _ => true : t => fs.All(f => f(t));
    }

    /// <summary>놓을 곳 "this Tamer" · "(1|one|any) of your [조건] Tamers [조건]" · "your Tamers" · "your Tamer" (뒤에 다른 말 없이)</summary>
    internal static readonly Regex UnderWhere85 = new(
        @"^(?:(?<this>this Tamer)|(?:(?:1|one|any) of )?(?<who>your|your opponent's|their) (?:(?<pre>.*?) )?Tamers?(?: (?<post>with .+))?)$", I85 | RegexOptions.Compiled);

    /// <summary>놓을 곳의 테이머 (조건 · 이 테이머). 읽지 못하면 null</summary>
    internal sealed record UnderHostSpec85(bool ThisTamer, bool Enemy, Func<CardData, bool> Filter);

    internal static UnderHostSpec85? ParseUnderHost85(string where)
    {
        var m = UnderWhere85.Match(where.Trim());
        if (!m.Success) return null;
        if (m.Groups["this"].Success) return new UnderHostSpec85(true, false, _ => true);
        var f = TamerFilter85(m.Groups["pre"].Value, m.Groups["post"].Value);
        if (f == null) return null;
        return new UnderHostSpec85(false, !m.Groups["who"].Value.Equals("your", StringComparison.OrdinalIgnoreCase), f);
    }

    /// <summary>
    /// 카드를 놓을 테이머 1명 (없으면 null): 이 테이머 (필드에 있을 때) · 조건에 맞는 테이머 중 고른다 (자신이 고른다 — 상대의 테이머 아래도).
    /// cards = 놓을 카드 (AI 추천용)
    /// </summary>
    internal static CardData? UnderHost85(EffectContext c, UnderHostSpec85? spec, IReadOnlyCollection<CardData> cards, bool faceDown = false)
    {
        var zones = spec?.Enemy == true ? c.Enemy : c.Owner;
        if (spec?.ThisTamer == true)
            return c.Card.Type == CardType.Tamer && c.Owner.Tamers.Any(t => ReferenceEquals(t, c.Card)) ? c.Card : null;
        return c.Engine.ChooseUnderTamerHost85(zones, c.Owner, cards, $"[{c.Card.Name}] 카드를 놓을 {(ReferenceEquals(zones, c.Owner) ? "자신의" : "상대의")} 테이머를 고르세요.", spec?.Filter, faceDown);
    }

    /// <summary>"자신의 테이머" 중 조건 spec에 맞는 테이머가 있는지 (놓을 곳이 있는지)</summary>
    internal static bool HasUnderHost85(EffectContext c, UnderHostSpec85? spec)
    {
        if (spec?.ThisTamer == true) return c.Card.Type == CardType.Tamer && c.Owner.Tamers.Any(t => ReferenceEquals(t, c.Card));
        var zones = spec?.Enemy == true ? c.Enemy : c.Owner;
        return zones.Tamers.Any(t => spec == null || spec.Filter(t));
    }

    /// <summary>놓을 곳이 있는지: "this Tamer"면 이 테이머가 필드에, 아니면 조건 filter에 맞는 자신의 테이머</summary>
    internal static bool HostOk85(EffectContext c, bool thisTamer, Func<CardData, bool> filter) =>
        thisTamer ? c.Card.Type == CardType.Tamer && c.Owner.Tamers.Any(t => ReferenceEquals(t, c.Card)) : c.Owner.Tamers.Any(filter);

    /// <summary>카드를 놓을 자신의 테이머 1명: "this Tamer"면 이 테이머, 아니면 조건 filter에 맞는 테이머 중 고른다</summary>
    internal static CardData? DestHost85(EffectContext c, bool thisTamer, Func<CardData, bool> filter, IReadOnlyCollection<CardData> cards, bool faceDown) =>
        UnderHost85(c, thisTamer ? new UnderHostSpec85(true, false, _ => true) : new UnderHostSpec85(false, false, filter), cards, faceDown);

    /// <summary>테이머 아래에서 카드를 꺼낼 곳: "this Tamer" = 이 테이머 아래만, 그 밖엔 모든 테이머 아래 (공식 Q&A P-096 Q4183 — 여러 테이머 아래에서 함께 고를 수 있다)</summary>
    internal static IEnumerable<CardData> UnderPool85(EffectContext c, bool thisTamer) =>
        thisTamer ? (c.Card.Type == CardType.Tamer ? c.Owner.UnderOf(c.Card) : Enumerable.Empty<CardData>()) : c.Owner.UnderTamers;
}
