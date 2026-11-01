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
                Text = text,
                Foreground = BrushFromHex(inactive ? "#A6ADC8" : "#CBA6F7"),
                FontWeight = FontWeights.Bold,
                FontSize = 9.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }
}
