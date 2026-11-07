using Communication.Devices;
using Communication.Drivers;
using Communication.Mitsubishi;


namespace Communication.Tests
{
    /// <summary>Chỉ kiểm phần logic của mình; không gọi thư viện Mitsubishi thật (cần thiết bị).</summary>
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
    //    public void OffsetPacketBuildsFromTemplate()
    //    {
    //        Assert.Equal("1.250,-0.800,0.500\r", OffsetPacket.Build("{dx},{dy},{drz}\\r", 1.25, -0.8, 0, 0.5, 7));
    //        Assert.Equal("X1.25,Y-0.80,#000007\r\n", OffsetPacket.Build("X{dx:F2},Y{dy:F2},#{id:D6}\\r\\n", 1.25, -0.8, 0, 0, 7));
    //        Assert.Equal("1250,-800", OffsetPacket.Build("{dx},{dy}", 1.25, -0.8, 0, 0, 1, decimals: 0, scale: 1000));
    //        Assert.NotNull(OffsetPacket.Validate("{dx},{abc}"));
    //        Assert.Throws<ArgumentException>(() => OffsetPacket.Build("{xyz}", 0, 0, 0, 0, 1));
    //        Assert.Equal("a\\r\\n", OffsetPacket.Visible("a\r\n"));
    //    }

    //    [Fact]
    //    public async Task SimulatedRobotSendsPacket()
    //    {
    //        var packet = new RobotPacketSettings("127.0.0.1", 10002, "X{dx},Y{dy}\\r", "NG\\r", 2, 1);
    //        using var r = new Communication.Simulated.SimulatedRobot(
    //            new RobotPointSettings(RobotSendMode.Packet, "P1", "P_VOFS", 'C', packet));
    //        await r.ConnectAsync();
    //        var reply = await r.SendOffsetAsync(1.5, -2, 0, 3);
    //        Assert.True(reply.Accepted);
    //        Assert.Contains("X1.50,Y-2.00\\r", reply.Raw);
    //        Assert.Contains("NG", (await r.SendRejectAsync(3)).Raw);
    //    }

    //    [Fact]
    //    public async Task SimulatedRobotMovesToCoordinatesWithSafetyChecks()
    //    {
    //        using var r = new Communication.Simulated.SimulatedRobot();
    //        await r.ConnectAsync();
    //        var start = (await r.GetCurrentPointAsync()).Point;

    //        Assert.False((await r.MoveToAsync(start with { X = start.X + 10 }, true)).Accepted);   // chưa chiếm quyền
    //        await r.TakeControlAsync(false);
    //        Assert.False((await r.MoveToAsync(start with { X = start.X + 10 }, true)).Accepted);   // chưa bật servo
    //        await r.SetServoAsync(true);

    //        Assert.True((await r.MoveToAsync(start with { X = start.X + 10, C = 45 }, true)).Accepted);
    //        Assert.Equal(start.X + 10, (await r.GetCurrentPointAsync()).Point.X);
    //        Assert.False((await r.MoveToAsync(start with { X = start.X + 1000 }, false)).Accepted); // quá xa
    //        Assert.True((await r.MoveToAsync(start with { X = start.X + 1000 }, false, enforceDistanceLimit: false)).Accepted);   // điểm đã dạy
    //    }

    //    [Fact]
    //    public void PointParsingAndFormatting()
    //    {
    //        Assert.True(RobotPoint.TryParse("P1=(300.5,-20,50,180,0,-90.25)(7,1)", out var p));
    //        Assert.Equal(new RobotPoint(300.5, -20, 50, 180, 0, -90.25, 7, 1), p);
    //        Assert.Equal("(300.5,-20,50,180,0,-90.25)(7,1)", p.ToMelfa());
    //        Assert.True(RobotPoint.TryParse("P1=(298.860,496.760,517.340,180.000,-26.050,-121.030,0.000,0.000)(7,0)", out var q));
    //        Assert.Equal(new RobotPoint(298.86, 496.76, 517.34, 180, -26.05, -121.03, 7, 0), q);
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
