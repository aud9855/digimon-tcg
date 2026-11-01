using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.70.0 미지원 문장 44차 묶음:
///  - 괄호 안의 명칭 제외 "[Greymon] in its name (other than [DoruGreymon], [BurningGreymon] or [DexDoruGreymon])" — 전에는 설명 괄호로 지워 제외가 빠졌다.
///    이름 조건 안에 제외할 명칭을 붙여 읽는다 (CardNames.ExceptMark70). 바꿔 읽지 못하면 원래대로
///  - 그 밖의 새 규칙은 원래 규칙(43차까지)이 읽지 못한 절만
/// </summary>
internal static partial class SentenceParser
{
    /// <summary>바꿔 쓴 절을 다시 읽는 중 (같은 규칙으로 또 바꿔 쓰지 않는다)</summary>
    [ThreadStatic] private static int _depth70;

    // "[A] or [B] in its name (other than [X], [Y] or [Z])" · "[A] in its name attacks (other than ~)" · "in their names (other than ~)"
    internal static readonly Regex OtherThanNamesRe70 = new($@"(?<list>{TargetSpec.NameList}) in (?<p>its|their) (?<n>names?)(?<mid>(?: [a-z]+)?) \(other than (?<ex>{TargetSpec.NameList})\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>괄호 안의 명칭 제외를 이름 조건에 붙인다 ("[Greymon⊄DoruGreymon⊄BurningGreymon⊄DexDoruGreymon] in its name")</summary>
    internal static string OtherThanNames70(string clause) =>
        OtherThanNamesRe70.Replace(clause, m =>
        {
            var ex = TargetSpec.Names(m.Groups["ex"].Value);
            string list = Regex.Replace(m.Groups["list"].Value, @"\[([^\]]+)\]", x => "[" + x.Groups[1].Value + string.Concat(ex.Select(e => CardNames.ExceptMark70 + e)) + "]");
            return $"{list} in {m.Groups["p"].Value} {m.Groups["n"].Value}{m.Groups["mid"].Value}";
        });

    // EX9-072 "[Security] [All Turns] All of your [DM] trait Digimon get +1000 DP for each of their face-down digivolution cards." — 받는 디지몬마다 값이 다르다
    internal static readonly Regex FaceUpPerFaceDownRe70 = new(@"^(?:Security Effect\s*)?\[Security\]\s*(?<lab>\[(?:All Turns|Your Turn|Opponent's Turn)\]\s*)All of your (?<x>[^.]+?Digimon) gets? \+(?<n>\d+) DP for each of their face[- ]down digivolution cards\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>받는 디지몬의 뒷면 진화원 수 상한 (그 이상은 같은 값 — 실제 게임에서 닿지 않는다)</summary>
    private const int PerFaceDownCap70 = 30;

    /// <summary>
    /// EX9-072 앞면 시큐리티의 지속 효과 (종합 룰 15-14-5-1) "받는 디지몬의 뒷면 진화원 1장마다 DP +N" — 43차 앞면 시큐리티 지속 효과(FaceUpStatic69)로 "+N"을 읽고,
    /// 받는 디지몬 조건에 "뒷면 진화원 k장 이상"을 더한 효과를 k = 1..30개 둔다 (k장이면 k개를 받는다)
    /// </summary>
    private static bool FaceUpPerFaceDown70(string rawClause, CardData card, CardScriptDefinition? def)
    {
        var m = FaceUpPerFaceDownRe70.Match(Normalize58(rawClause).Trim());
        if (!m.Success) return false;
        string baseClause = $"[Security] {m.Groups["lab"].Value}All of your {m.Groups["x"].Value} get +{m.Groups["n"].Value} DP.";
        var tdef = new CardScriptBuilder().Card(card.CardId + "#perfd70");
        if (!FaceUpStatic69(baseClause, card, tdef) || tdef.Script41.Statics.Count == 0) return false;
        if (def == null) return true;
        foreach (var st in tdef.Script41.Statics)
            for (int k = 1; k <= PerFaceDownCap70; k++)
            {
                int need = k;
                var subject = st.Subject;
                def.Script41.Statics.Add(st with { Subject = d => (subject == null || subject(d)) && d.EvolutionSources.Count(d.IsFaceDown) >= need });
            }
        return true;
    }

    private static string? TryParse70(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        if (_depth70 == 0 && !inherited && FaceUpPerFaceDown70(rawClause, card, def)) return null;   // EX9-072 (원래는 체크했을 때 한 번 — 42차 이전 방식)
        // 괄호 안의 명칭 제외 (원래 규칙보다 먼저 — 원래는 제외 없이 읽었다)
        if (_depth70 == 0)
        {
            string ex = OtherThanNames70(rawClause);
            if (ex != rawClause)
            {
                _depth70++;
                try
                {
                    if (TryParse69(ex, inherited, card, null) == null) return TryParse69(ex, inherited, card, def);
                }
                finally { _depth70--; }
            }
        }
        string? err = TryParse69(rawClause, inherited, card, def);
        if (err == null || _depth70 > 0) return err;
        string t = Normalize58(rawClause);
        string alt = ClauseRewrite70(t);
        if (alt == t) return err;
        _depth70++;
        try
        {
            if (TryParse69(alt, inherited, card, null) != null) return err;
            if (def != null) TryParse69(alt, inherited, card, def);
            return null;
        }
        finally { _depth70--; }
    }

    // ---- 동작 (원래 규칙이 읽지 못한 것만) ----

    private static (Action<EffectContext>? Act, string? Error)? ParseAction70(string s, CardData card, List<EffectTrigger> triggers)
    {
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        s = Regex.Replace(s.Trim().TrimEnd('.'), @"^Then,?\s+", "", RegexOptions.IgnoreCase);
        // EX14-012 듀얼 카드 "[Security] [On Deletion] You may use this card without paying the cost." — 옵션 쪽(종합 룰 4-6-2 · 4-6-6)을 사용.
        // 사용은 색 조건이 필요 (9-1-1 — 《사용 조건》 16-42로 무시 가능), 사용 뒤 트래시 (9-1-5). 【시큐리티】로 사용하면 시큐리티 디지몬이 아니게 되어 배틀하지 않는다
        if (card.IsDual && card.Type == CardType.Digimon && _zone68 == 0
            && triggers.All(t => t is EffectTrigger.Security or EffectTrigger.OnDeletion)
            && Regex.IsMatch(s, @"^(?:you may )?use this card without paying (?:the|its) (?:memory |use )?cost$", RegexOptions.IgnoreCase))
            return (c => UseThisDual70(c, label), null);
        // BT24-085 "use 1 [TS] trait Option card with as high or lower a use cost as your opponent's memory from your hand without paying the cost"
        // — 상대의 메모리 (종합 룰 4-1-3: 상대 쪽의 수. 메모리가 자신 쪽이면 0보다 작아 사용할 수 있는 옵션이 없다). 색 조건은 그대로 (9-1-1)
        // ST22-07 "~ with as high or lower a use cost as that Digimon's level from under this Tamer ~" — 어택한 그 디지몬의 Lv. (테이머 아래의 앞면 카드)
        Match m;
        if ((m = Regex.Match(s, @"^(?:you may )?use (?<p>1 .+?Option card) with as high or lower a (?:use )?cost as (?<ref>your opponent's memory|that Digimon's level) from (?<from>your hand|under this Tamer) without paying (?:the|its) (?:use |memory )?cost$", RegexOptions.IgnoreCase)).Success
            && ParseCardPick(m.Groups["p"].Value) is { } op)
        {
            var match = op.Match;
            bool byLevel = m.Groups["ref"].Value.EndsWith("level", StringComparison.OrdinalIgnoreCase);
            bool fromUnder = m.Groups["from"].Value.StartsWith("under", StringComparison.OrdinalIgnoreCase);
            return (c =>
            {
                int limit = byLevel ? (c.Other?.TopCard.Level ?? -1) : c.Engine.MemoryOf(c.Enemy);
                var zone = fromUnder ? c.Owner.UnderTamers : c.Owner.Hand;
                var pool = zone.Where(x => x.Type == CardType.Option && match(x) && x.PlayCost <= limit && !(fromUnder && c.Owner.IsFaceDownUnderTamer(x))
                    && ActionValidator.MissingOptionColors(c.Owner, x, c.Enemy).Count == 0 && !EffectRules.OptionUseBlocked(c.State, c.Owner)).Distinct().OrderByDescending(x => x.PlayCost).ToList();
                if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, $"사용할 수 있는 옵션 카드가 없습니다 ({(byLevel ? "Lv." : "상대의 메모리 ")}{limit})"); return; }
                var pick = pool.Count == 1 ? pool[0] : c.Engine.ChooseCards(c.Owner, fromUnder ? CardChoiceZone.Other : CardChoiceZone.Hand, pool, 1, upTo: false, $"[{c.Card.Name}] 코스트 없이 사용할 옵션 카드를 고르세요 (사용 코스트 {limit} 이하).").FirstOrDefault() ?? pool[0];
                if (!zone.Remove(pick)) { c.LastStepResolved = false; return; }
                Diag46.Hit("costLimitUse70");
                c.Engine.UseOptionByEffect(c.Owner, pick);
                c.RecalculateFieldBuffs();
            }, null);
        }
        // EX3-024 "your opponent attacks with 1 of their Digimon" — 상대가 어택할 수 있는 디지몬 1마리를 골라 어택한다 (어택 대상도 상대가 고른다 · 공식 "상대는 본인의 디지몬 1마리를 어택시킨다")
        if (Regex.IsMatch(s, @"^your opponent attacks with (?:1|one) of their Digimon$", RegexOptions.IgnoreCase))
            return (c =>
            {
                var cands = c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null && c.Engine.CanEffectAttack(c.Enemy, d, false)).OrderByDescending(d => d.GetCurrentDP()).ToList();
                if (cands.Count == 0) { c.LastStepResolved = false; Emit(c, label, "상대에게 어택할 수 있는 디지몬이 없습니다"); return; }
                var d = cands.Count == 1 ? cands[0] : c.Engine.ChooseTargets(c.Enemy, cands, 1, $"[{c.Card.Name}] 어택할 디지몬 1마리를 고르세요 (상대의 효과).", optional: false, preselected: cands[0]).FirstOrDefault() ?? cands[0];
                bool ok = c.Engine.EffectAttack(c.Enemy, d, EffectAttackTarget.Any, withoutSuspending: false, label, forced: true);
                if (ok) Diag46.Hit("forcedOppAttack70"); else c.LastStepResolved = false;
                Emit(c, label, ok ? $"상대의 [{d.TopCard.Name}]이(가) 어택합니다" : $"[{d.TopCard.Name}]은(는) 어택할 수 없습니다");
            }, null);
        return null;
    }

    // ---- 같은 뜻 다른 표기 (원래 규칙이 읽지 못한 절만) ----

    /// <summary>BT13-099 "treat this Tamer as a 3000 DP Digimon" = "this Tamer is also treated as a 3000 DP Digimon" (공식 "디지몬・DP 3000으로도 취급" · Q&amp;A BT13-099 Q1)</summary>
    internal static string ClauseRewrite70(string clause) =>
        Regex.Replace(clause, @"\btreat this Tamer as an? (?=\d+ DP Digimon\b)", "this Tamer is also treated as a ", RegexOptions.IgnoreCase);

    private static void UseThisDual70(EffectContext c, string label)
    {
        var card = c.Card;
        // 【시큐리티】: 체크한 카드는 이미 트래시에 놓여 있다 (GoldfishEngine) · 【소멸 시】: 소멸해 트래시에
        if (card == null || !c.Owner.Trash.Contains(card) || CardScripts.OptionSideFor(card)?.Effects.Any(e => e.Trigger == EffectTrigger.Main) != true)
        { c.LastStepResolved = false; Emit(c, label, "사용할 이 카드가 트래시에 없습니다"); return; }
        if (EffectRules.OptionUseBlocked(c.State, c.Owner) || ActionValidator.MissingOptionColors(c.Owner, card, c.Enemy).Count > 0)
        { c.LastStepResolved = false; Emit(c, label, $"[{card.Name}]을(를) 사용할 수 없습니다 (색 조건 · 사용 조건)"); return; }
        // 추천: 옵션 쪽 [메인]으로 소멸시킬 상대의 디지몬이 있을 때 (플레이어는 "~할 수 있다"에서 이미 물었다)
        bool rec = c.Enemy.BattleArea.Any(d => d.TamerProxyOf == null && !c.Engine.IsUnaffectedBy(c.Owner, d));
        if (!(c.OwnerIsPlayer && c.Engine.PlayerChoices != null) && !rec) { c.LastStepResolved = false; return; }
        int at = c.Owner.Trash.LastIndexOf(card);
        c.Owner.Trash.RemoveAt(at);
        Diag46.Hit("dualUse70");
        if (c.Trigger == EffectTrigger.Security) c.Engine.SecurityUsedAsOption70 = card;
        c.Engine.UseOptionByEffect(c.Owner, card);
        c.RecalculateFieldBuffs();
    }
}
