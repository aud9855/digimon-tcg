using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.39.0 시큐리티: "At the end of the battle, ~" ([시큐리티] 디지몬 — 배틀이 끝난 뒤) · 서로의 시큐리티 파기 · "N장이 되도록 파기" · 상대가 패에서 시큐리티 아래에 놓기 등</summary>
internal static partial class SentenceParser
{
    /// <summary>같은 뜻의 다른 표기 (시큐리티) — Rewrites39에서 부른다</summary>
    internal static IEnumerable<string> SecurityRewrites39(string s)
    {
        // "Trash 1 card from the top of your opponent's security stack" (BT13-020) = "Trash the top card of your opponent's security stack"
        var m = Regex.Match(s, @"^(?<pre>.*?)\b[Tt]rash (?<n>\d+) cards? from the top of (?<w>your opponent's|their|your) security stack(?<post>.*)$");
        if (m.Success)
        {
            string n = m.Groups["n"].Value;
            string what = n == "1" ? "the top card" : $"the top {n} cards";
            string who = m.Groups["w"].Value;
            yield return $"{m.Groups["pre"].Value}{(m.Groups["pre"].Value.Length == 0 ? "T" : "t")}rash {what} of {who} security stack{m.Groups["post"].Value}";
        }
        // "add the top card of your security stack to the hand" (BT14-032 · BT15-034 · BT18-038) = "add your top security card to the hand"
        if (Regex.IsMatch(s, @"\b[Aa]dd the top card of your security stack to (?:the|your) hand\b"))
            yield return Regex.Replace(s, @"\b(?<a>[Aa])dd the top card of your security stack to (?:the|your) hand\b", "${a}dd your top security card to the hand");
        // "Then, add this card into your hand" (EX2-068)
        if (Regex.IsMatch(s, @"\badd this card into your hand\b", I))
            yield return Regex.Replace(s, @"\badd this card into your hand\b", "add this card to your hand", I);
        // "Place this Digimon on top of your security stack" (LM-007) = "Place this Digimon as the top security card"
        if (Regex.IsMatch(s, @"^Place this (?:Digimon|card) on top of your security stack$", I))
            yield return Regex.Replace(s, @"on top of your security stack$", "as the top security card", I);
    }

    /// <summary>
    /// [시큐리티] "At the end of the battle, ~" (P-066 — 공식 "배틀 종료 시, ~") — 시큐리티 디지몬의 배틀이 끝난 뒤 발휘. 뒤에 이어지는 문장("Then, add this card to its owner's hand")도 함께 배틀 뒤로
    /// </summary>
    private static Step? EndOfBattle39(string s, CardData card, List<EffectTrigger> triggers)
    {
        if (!triggers.Contains(EffectTrigger.Security) || card.Type != CardType.Digimon) return null;
        var m = Regex.Match(s, @"^At the end of (?:the )?battle, (?<x>.+)$", I);
        if (!m.Success) return null;
        string x = m.Groups["x"].Value.Trim();
        var inner = ParseStep(char.ToUpperInvariant(x[0]) + x[1..], card, triggers);
        if (inner.Error != null || inner.Step == null) return null;
        var step = inner.Step;
        return new Step(null, c =>
        {
            var deferred = new List<Action> { () => RunStep(step, c) };
            c.Deferred39 = deferred;   // 뒤의 문장도 배틀 뒤로 (RunStep)
            c.Engine.QueueAfterSecurityBattle39(() =>
            {
                c.Deferred39 = null;
                Diag39.Hit(ref Diag39.EndOfBattle);
                string before = c.Message;
                foreach (var a in deferred.ToList()) { if (c.State.IsGameOver) break; a(); }
                if (c.Message != before) c.Log($"[{c.Card.Name} 시큐리티 · 배틀 종료 시] {c.Message[(before.Length > 0 && c.Message.StartsWith(before) ? before.Length : 0)..].TrimStart(' ', '/')}");
            });
            c.Log($"[{c.Card.Name}] 배틀이 끝나면 발휘합니다: {x}");
        }, false, s);
    }

    private static (Action<EffectContext>? Act, string? Error)? SecurityAction39(string s, CardData card, string label, bool security)
    {
        Match m;
        // [시큐리티] 디지몬 "add this card to its owner's hand" — 배틀이 끝난 뒤 트래시에서 패로 (P-066 "그 후, 이 카드를 패에 추가한다")
        if (security && card.Type == CardType.Digimon && Regex.IsMatch(s, @"^Add this card (?:to|into) (?:the|your|its owner's) hand$", I))
            return (c =>
            {
                void Move()
                {
                    if (!c.Owner.Trash.Remove(c.Card)) { c.Log($"[{c.Card.Name}] 트래시에 없어 패에 추가하지 않습니다"); return; }
                    c.Owner.Hand.Add(c.Card);
                    Diag39.Hit(ref Diag39.SecurityToHand);
                    c.Log($"[{c.Card.Name}] 이 카드를 패에 추가");
                }
                if (c.Deferred39 != null) c.Deferred39.Add(Move); else c.Engine.QueueAfterSecurityBattle39(Move);
            }, null);
        // "Trash the top card of both players' security stacks" (BT16-036 — 공식 "서로의 시큐리티를 위에서부터 1장 파기한다")
        if ((m = Regex.Match(s, @"^Trash the top (?:card|(?<n>\d+) cards) of both players' security stacks?$", I)).Success)
        {
            int n = m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value) : 1;
            return (c =>
            {
                int a = TrashTopSecurity39(c, c.Owner, n), b = TrashTopSecurity39(c, c.Enemy, n);
                Emit(c, label, $"서로의 시큐리티 위에서 파기 (자신 {a}장 · 상대 {b}장)");
            }, null);
        }
        // "trash the top cards of your opponent's security stack so that it has 1 card left" (BT21-098) · "~ of both players' security stacks so that they have 3 cards left" (BT23-102) ·
        // "trash cards from the top of your security stack until there are 3 left; gain 1 memory for each security card trashed" (BT6-033)
        if ((m = Regex.Match(s, @"^Trash (?:the top cards of|cards from the top of) (?<w>your opponent's|their|your|both players') security stacks? (?:so that (?:it has|they have|there (?:is|are)) (?<n>\d+) cards? left|until there (?:is|are) (?<n>\d+) left)(?<mem>; gain 1 memory for each (?:security )?card trashed)?$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            string w = m.Groups["w"].Value.ToLowerInvariant();
            bool mem = m.Groups["mem"].Success;
            return (c =>
            {
                int a = 0, b = 0;
                if (w is "your" or "both players'") a = TrashTopSecurity39(c, c.Owner, Math.Max(0, c.Owner.SecurityStack.Count - n));
                if (w is not "your") b = TrashTopSecurity39(c, c.Enemy, Math.Max(0, c.Enemy.SecurityStack.Count - n));
                if (mem && a > 0) c.AddMemory(a);
                Emit(c, label, $"시큐리티가 {n}장이 되도록 파기 (자신 {a}장 · 상대 {b}장){(mem && a > 0 ? $", 메모리 +{a}" : "")}");
            }, null);
        }
        // "Your opponent places 1 card from their hand as the bottom security card" (BT24-016) · "they place 1 card from their hand ~" (BT21-024) — 상대가 고른다
        if ((m = Regex.Match(s, @"^(?:Your opponent places|They place|they place) (?<n>\d+) cards? from their hand (?:as the bottom security cards?|at the bottom of their security stack)$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                int k = 0;
                for (int i = 0; i < n && c.Enemy.Hand.Count > 0; i++)
                {
                    // 상대가 고른다 — 가치가 낮은 카드부터 (AI 추천: 코스트가 낮은 카드)
                    var ranked = c.Enemy.Hand.OrderBy(x => x.Type == CardType.Option ? 0 : 1).ThenBy(x => x.PlayCost).ToList();
                    var pick = c.Engine.ChooseCards(c.Enemy, CardChoiceZone.Hand, ranked, 1, false, $"[{c.Card.Name}] 시큐리티 아래에 놓을 패의 카드를 고르세요.").FirstOrDefault();
                    if (pick == null || !c.Enemy.Hand.Remove(pick)) break;
                    c.Enemy.SecurityStack.Add(pick);
                    k++;
                }
                if (k == 0) { c.LastStepResolved = false; Emit(c, label, "상대의 패가 없습니다"); return; }
                Diag39.Hit(ref Diag39.SecurityTrash);
                Emit(c, label, $"상대가 패에서 {k}장을 시큐리티 아래에 놓았습니다");
            }, null);
        }
        // "Trash any 1 of your opponent's security cards" (BT24-018 — 상대의 시큐리티 중 1장. 뒷면이라 위의 카드)
        if ((m = Regex.Match(s, @"^Trash any (?<n>\d+) of (?:your opponent's|their) security cards$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                int k = TrashTopSecurity39(c, c.Enemy, n);
                if (k == 0) { c.LastStepResolved = false; Emit(c, label, "상대의 시큐리티가 없습니다"); return; }
                Emit(c, label, $"상대의 시큐리티 {k}장 파기");
            }, null);
        }
        return null;
    }

    /// <summary>시큐리티 위에서 n장 파기 (파기한 매수)</summary>
    private static int TrashTopSecurity39(EffectContext c, PlayerZones zones, int n)
    {
        int k = 0;
        for (int i = 0; i < n && zones.SecurityStack.Count > 0; i++)
        {
            var x = zones.SecurityStack[0];
            zones.SecurityStack.RemoveAt(0);
            zones.Trash.Add(x);
            c.Engine.NotifySecurityRemoved(zones, x, trashed: true);
            k++;
        }
        if (k > 0) Diag39.Hit(ref Diag39.SecurityTrash);
        return k;
    }
}
