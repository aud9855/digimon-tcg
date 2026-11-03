using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.86.0 트래시 목록: [Trash] [Main] 효과를 지금 쓸 수 있는지 미리 본다 (효과를 처리하지 않고).
/// 효과 문장 앞머리의 발동 조건 "If ~," 과 비용 "By ~," 만 본다 — 같은 문장을 처리할 때 쓰는 조건 · 비용 규칙(ParseEventCondition · ParseCost)을 그대로 쓴다.
/// 대상이 없어서 처리하지 못하는 것 같은 그 밖의 이유는 실제로 써 봐야 안다 (GoldfishEngine.ActivateTrashEffect가 그때 이유를 돌려준다).
/// </summary>
internal static partial class SentenceParser
{
    /// <summary>미리 볼 것 하나: Ok가 거짓이면 쓸 수 없다. IsCost = 비용(아니면 발동 조건), Text = 영어 원문 구절</summary>
    internal sealed record ZoneMainCheck86(Func<EffectContext, bool> Ok, bool IsCost, string Text);

    private static readonly ConcurrentDictionary<string, IReadOnlyList<ZoneMainCheck86>> ZoneMainChecks86 = new(StringComparer.Ordinal);

    private static readonly Regex TrashMainLine86 = new(@"^\s*\[Trash\]\s*\[Main\]\s*(?<body>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeadIfBy86 = new(@"^(?:If (?<if>[^,]+), )?(?:by (?<cost>[^,]+), )?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// v0.86.0 "~할 수 있다" 확인 창에 보여 줄 문장 — 플레이어에게 물을 때는 한글 (KoreanEffectRenderer, 옮기지 못하면 영어 원문).
    /// AI에게 물을 때는 보여 주지 않으므로 옮기지 않는다
    /// </summary>
    private static string AskText86(EffectContext c, string text)
    {
        if (!c.OwnerIsPlayer || string.IsNullOrWhiteSpace(text)) return text;
        try { return KoreanEffectRenderer.RenderClause(text.Trim()) ?? text; }
        catch (Exception) { return text; }
    }

    /// <summary>카드의 [Trash] [Main] 효과 앞머리 조건 · 비용 (읽지 못한 구절은 건너뛴다 — 써 볼 때 엔진이 다시 본다)</summary>
    internal static IReadOnlyList<ZoneMainCheck86> TrashMainChecks86(CardData card) =>
        ZoneMainChecks86.GetOrAdd(card.CardId + "\u0001" + card.EffectText, _ =>
        {
            var list = new List<ZoneMainCheck86>();
            foreach (var line in (card.EffectText ?? "").Split('\n'))
            {
                var m = TrashMainLine86.Match(line);
                if (!m.Success) continue;
                var head = HeadIfBy86.Match(Normalize(m.Groups["body"].Value));   // 효과를 처리할 때와 같은 정규화 (API 문구의 줄바꿈 없는 공백 U+00A0 등 — EX10-054 · EX10-011)
                if (head.Groups["if"].Success && ParseEventCondition(head.Groups["if"].Value) is { } cond)
                    list.Add(new ZoneMainCheck86(cond, false, head.Groups["if"].Value));
                if (head.Groups["cost"].Success && ParseCost(head.Groups["cost"].Value, card) is { } cost)
                    list.Add(new ZoneMainCheck86(cost.CanPay, true, head.Groups["cost"].Value));
            }
            return list;
        });
}
