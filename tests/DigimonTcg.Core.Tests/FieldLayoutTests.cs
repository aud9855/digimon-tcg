using DigimonTcg.Core.Layout;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>자유 배치 필드: 빈 자리 찾기 · 자동 정렬 격자 · 겹침 밀어내기 (화면 전용 계산)</summary>
public class FieldLayoutTests
{
    private const double W = 800, H = 300;

    [Fact]
    public void FreeSpot_KeepsDesiredPosition_WhenNothingOverlaps()
    {
        var desired = new FieldRect(120, 40, 160, 220);
        var spot = FieldLayout.FindFreeSpot(desired, new[] { new FieldRect(400, 0, 160, 220) }, W, H);
        Assert.Equal(desired, spot);
    }

    [Fact]
    public void FreeSpot_ClampsIntoField()
    {
        var spot = FieldLayout.FindFreeSpot(new FieldRect(760, -30, 160, 220), Array.Empty<FieldRect>(), W, H);
        Assert.Equal(new FieldRect(640, 0, 160, 220), spot);
    }

    [Fact]
    public void FreeSpot_NudgesToNearestFreeSpot_WhenDroppedOnAnotherCard()
    {
        var other = new FieldRect(300, 0, 160, 220);
        var desired = new FieldRect(320, 0, 160, 220);   // 오른쪽으로 살짝 겹치게 놓음
        var spot = FieldLayout.FindFreeSpot(desired, new[] { other }, W, H);

        Assert.False(spot.Overlaps(other, FieldLayout.DefaultGap));
        Assert.True(spot.X >= other.Right + FieldLayout.DefaultGap);   // 가까운 오른쪽으로 밀림
        Assert.True(spot.X - desired.X < 200);
        Assert.Equal(0, spot.Y);
    }

    [Fact]
    public void FreeSpot_GoesBelowEverything_WhenFieldIsFull()
    {
        var occupied = FieldLayout.Flow(Enumerable.Repeat((160.0, 280.0), 5).ToList(), W);
        var spot = FieldLayout.FindFreeSpot(new FieldRect(0, 0, 160, 280), occupied, W, H);

        Assert.True(FieldLayout.IsFree(spot, occupied));
        Assert.True(spot.Y >= 280);
    }

    [Fact]
    public void Flow_FillsRowsLeftToRight_AndWraps()
    {
        var rects = FieldLayout.Flow(Enumerable.Repeat((160.0, 200.0), 5).ToList(), 700);
        Assert.Equal(new[] { 0.0, 168, 336, 504, 0 }, rects.Select(r => r.X));
        Assert.Equal(new[] { 0.0, 0, 0, 0, 208 }, rects.Select(r => r.Y));
    }

    [Fact]
    public void Flow_FromRight_StartsAtRightEdge()
    {
        var rects = FieldLayout.Flow(new List<(double, double)> { (112, 170), (140, 130) }, W, fromRight: true);
        Assert.Equal(W - 112, rects[0].X);
        Assert.Equal(W - 112 - 8 - 140, rects[1].X);
    }

    [Fact]
    public void ResolveOverlaps_KeepsEarlierCard_AndPushesWiderRestedCard()
    {
        // 테이머가 레스트되어 넓어지면서 옆 카드와 겹친 경우: 앞 카드는 그대로, 뒤 카드만 밀린다
        var first = new FieldRect(100, 0, 160, 220);
        var rested = new FieldRect(200, 10, 140, 130);
        var result = FieldLayout.ResolveOverlaps(new[] { first, rested }, W, H);

        Assert.Equal(first, result[0]);
        Assert.False(result[1].Overlaps(result[0], FieldLayout.DefaultGap));
    }
}
