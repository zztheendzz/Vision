using Communication.Devices;
using Communication.Mitsubishi;


namespace Communication.Tests
{
    /// <summary>Chỉ kiểm phần logic của mình; không gọi thư viện Mitsubishi thật (cần thiết bị).</summary>
    //public class MitsubishiDriverTests
    //{
    //    private static MitsubishiRobot Robot(string rejectCmd = "")
    //        => new(new MitsubishiRobot.Settings("127.0.0.1", 10009, 1, "Test", false, rejectCmd, 50, 30,
    //            new RobotPointSettings(RobotSendMode.OffsetViaPoint, "P1", "P_VOFS", 'C')));

    //    [Fact]
    //    public void TemplateUsesInvariantDecimalPointAndAllPlaceholders()
    //    {
    //        string cmd = MitsubishiRobot.FillTemplate("P_OFS=({dx},{dy},0,0,0,{drz}) ' {id}", 1.5, -2.25, 0.125, 12);
    //        Assert.Equal("P_OFS=(1.5,-2.25,0,0,0,0.125) ' 12", cmd);
    //    }

    //    [Fact]
    //    public void PointParsingAndFormatting()
    //    {
    //        Assert.True(RobotPoint.TryParse("P1=(300.5,-20,50,180,0,-90.25)(7,1)", out var p));
    //        Assert.Equal(new RobotPoint(300.5, -20, 50, 180, 0, -90.25, 7, 1), p);
    //        Assert.Equal("(300.5,-20,50,180,0,-90.25)(7,1)", p.ToMelfa());
    //        Assert.False(RobotPoint.TryParse("(1,2,3)", out _));
    //        Assert.False(RobotPoint.TryParse("", out _));
    //        Assert.True(RobotPoint.IsValidName("P12"));
    //        Assert.False(RobotPoint.IsValidName("P1;STOP"));
    //        Assert.Equal(12.5, p.WithAxis('A', 12.5).A);
    //    }

    //    [Fact]
    //    public async Task SimulatedRobotStoresOffsetPointAndOverwrittenPoint()
    //    {
    //        using var r = new Communication.Simulated.SimulatedRobot(
    //            new RobotPointSettings(RobotSendMode.OffsetViaPoint, "P1", "P_VOFS", 'C'));
    //        await r.ConnectAsync();

    //        Assert.True((await r.SendOffsetAsync(1.5, -2, 3, 1)).Accepted);
    //        var pdx = await r.ReadPointAsync("P_VOFS");
    //        Assert.True(pdx.Ok);
    //        Assert.Equal(1.5, pdx.Point.X);
    //        Assert.Equal(3, pdx.Point.C);

    //        Assert.False((await r.SendOffsetAsync(99, 0, 0, 2)).Accepted);           // vượt giới hạn
    //        Assert.Contains("P1", await r.ListPointsAsync());

    //        var moved = (await r.ReadPointAsync("P1")).Point with { X = 301 };
    //        Assert.True((await r.WritePointAsync("P1", moved)).Accepted);
    //        Assert.Equal(301, (await r.ReadPointAsync("P1")).Point.X);
    //        Assert.False((await r.WritePointAsync("P1;X", moved)).Accepted);
    //    }

    //    [Fact]
    //    public void ValidateRefusesOutOfRangeAndNaN()
    //    {
    //        using var r = Robot();
    //        Assert.Null(r.Validate(10, -10, 5));
    //        Assert.NotNull(r.Validate(51, 0, 0));
    //        Assert.NotNull(r.Validate(0, 0, 31));
    //        Assert.NotNull(r.Validate(double.NaN, 0, 0));
    //    }

    //    [Fact]
    //    public async Task NotConnectedOrUnconfiguredNeverReachesTheRobot()
    //    {
    //        using var r = Robot();
    //        Assert.False(r.IsConnected);

    //        var offset = await r.SendOffsetAsync(1, 1, 1, 1);
    //        Assert.False(offset.Accepted);
    //        Assert.Contains("chưa kết nối", offset.Error);

    //        var reject = await r.SendRejectAsync(1);
    //        Assert.False(reject.Accepted);
    //    }

    //    [Fact]
    //    public async Task PlcRefusesAccessWhenNotConnected()
    //    {
    //        using var plc = new MitsubishiPlc("127.0.0.1", 3006, false, 1000);
    //        Assert.False(plc.IsConnected);
    //        await Assert.ThrowsAsync<InvalidOperationException>(() => plc.ReadBitAsync("M100"));
    //        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => plc.WriteWordAsync("D100", 40000));
    //    }
    //}
}
