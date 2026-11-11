using Communication.Configuration;
using Communication.Fixtures;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
namespace Communication.Tests
{
    public class FixtureProtocolTests
    {
        private static FixtureProtocol Default()
        {
            var c = new DeviceCatalog();
            return new FixtureProtocol(k => c.GetGroupValue(DeviceType.Fixture, k));
        }

        [Fact]
        public void BuildsCommandWithTwoDigitFixtureNumber()
            => Assert.Equal("OPEN_03", Default().BuildCommand(FixtureCommand.Open, 3));

        [Fact]
        public void OpenReplyOkAndError()
        {
            var p = Default();
            Assert.True(p.Interpret(FixtureCommand.Open, "OPEN_03_OK", 3) is { Matched: true, Positive: true });
            Assert.True(p.Interpret(FixtureCommand.Open, "OPEN_03_ERROR", 3) is { Matched: true, Positive: false });
            Assert.False(p.Interpret(FixtureCommand.Open, "OPEN_04_OK", 3).Matched);   // sai số khuôn
        }

        [Fact]
        public void OpenAllErrorCarriesSlots()
        {
            var r = Default().Interpret(FixtureCommand.OpenAll, "OPEN_ALL_ERROR_01-02", 1);
            Assert.True(r.Matched);
            Assert.False(r.Positive);
            Assert.Equal("01-02", r.Slots);
        }

        [Fact]
        public void SensorAndStateReplies()
        {
            var p = Default();
            Assert.True(p.Interpret(FixtureCommand.CheckSensor, "YES_01", 1) is { Matched: true, Positive: true });
            Assert.True(p.Interpret(FixtureCommand.CheckSensor, "NO_01", 1) is { Matched: true, Positive: false });
            Assert.True(p.Interpret(FixtureCommand.State, "OPENED_01", 1) is { Matched: true, Positive: true });
            Assert.True(p.Interpret(FixtureCommand.State, "CLOSED_01", 1) is { Matched: true, Positive: false });
        }

        [Fact]
        public void ParsesSlotResultsAndPlcCodes()
        {
            var p = Default();
            Assert.True(p.TryParseSlotResult("P_02_01", out int ss, out int nn, out var state));
            Assert.Equal((2, 1, SlotState.Pass), (nn, ss, state));   // P_{NN}_{SS}: slot 2 của khuôn 1
            Assert.True(p.TryParseSlotResult("F_01_03", out ss, out nn, out state));
            Assert.Equal(SlotState.Fail, state);
            Assert.False(p.TryParseSlotResult("HELLO", out _, out _, out _));
            Assert.Equal(2, p.PlcCode(SlotState.Pass));
            Assert.Equal(1, p.PlcCode(SlotState.Empty));
            Assert.Null(p.PlcCode(SlotState.Error));
        }
    }
}
