using System.Windows;
using System.Windows.Controls;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Wpf;

/// <summary>
/// v0.68.0 필드 카드의 "효과로 바뀐 정보" 배지 — 원래 명칭 · 색 · DP가 바뀐 디지몬 (BT11-043 · BT14-097 · EX13-031 「스카몬」, BT18-078 원래 색, BT8-040 이 턴 동안의 색).
/// 카드 그림은 원래 카드 그대로이므로, 카드 아래에 작은 보라색 줄로 바뀐 내용을 보여 준다 (링크 줄과 같은 크기). 그 효과를 받지 않는 동안이면 흐리게
/// </summary>
public partial class MainWindow
{
    private static Border? ChangedInfoBadge(CardInstance instance, double maxWidth)
    {
        if (KoreanEffectRenderer.ChangedInfoBadge68(instance) is not { } text)
            return null;
        bool inactive = text.EndsWith("(지금은 받지 않음)", System.StringComparison.Ordinal);
        return new Border
        {
            Background = BrushFromHex("#2A1F3D"),
            BorderBrush = BrushFromHex(inactive ? "#585B70" : "#CBA6F7"),
            BorderThickness = new Thickness(1, 1, 3, 1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(0, 2, 0, 0),
            MaxWidth = maxWidth,
            ToolTip = $"효과로 바뀐 정보 (카드 그림은 원래 카드)\n{text}",
            Opacity = inactive ? 0.6 : 1.0,
            Child = new TextBlock
            {
                Text = CompactChangedInfo(text),   // v0.69.0 카드 폭(104)에 맞게 줄여 2줄까지 (전체 문장은 툴팁)
                Foreground = BrushFromHex(inactive ? "#A6ADC8" : "#CBA6F7"),
                FontWeight = FontWeights.Bold,
                FontSize = 9.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 26,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    /// <summary>
    /// v0.69.0 필드 배지용 짧은 문장 — v0.68.0에는 카드 폭에서 「원래 명칭 「스카…」처럼 잘려 무엇이 바뀌었는지 보이지 않았다.
    /// 「원래 명칭 「스카몬」 · 화이트 · DP 3,000」 → 「「스카몬」 화이트 DP 3,000」, 「이 턴 동안 레드/블루로도 취급」 → 「+레드/블루 (이 턴)」
    /// </summary>
    internal static string CompactChangedInfo(string text)
    {
        string s = System.Text.RegularExpressions.Regex.Replace(text, @"원래 명칭 (「[^」]*」)(?: · ([^·(]+?))? · DP ([\d,]+)", m => $"{m.Groups[1].Value}{(m.Groups[2].Success ? " " + m.Groups[2].Value.Trim() : "")} DP {m.Groups[3].Value}");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"이 턴 동안 (\S+)로도 취급", "+$1 (이 턴)");
        // 받지 않는 동안: 끝의 "(지금은 받지 않음)" 대신 앞의 표시를 ⊘로 (흐린 배지 · 툴팁에 전체 문장)
        const string off = " (지금은 받지 않음)";
        return s.EndsWith(off, System.StringComparison.Ordinal) ? "⊘ " + s[..^off.Length].TrimStart('✦', ' ') : s;
    }
}
