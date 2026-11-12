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

        [Fact]
        public void SingleAddressExpandsToConsecutiveSlots()
        {
            var a = SlotAddressing.Resolve("D1001", 3, out var warning);
            Assert.Equal(new[] { "D1001", "D1002", "D1003" }, a);
            Assert.Null(warning);
        }

        [Fact]
        public void ListGivesOneAddressPerSlotAndWarnsWhenShort()
        {
            var a = SlotAddressing.Resolve("D1001, D1005", 3, out var warning);
            Assert.Equal(new string?[] { "D1001", "D1005", null }, a);
            Assert.NotNull(warning);
        }

        [Theory]
        [InlineData("YES_1", 1, true, new[] { true })]            // một slot: bỏ phần sau được
        [InlineData("YES_1_1", 1, true, new[] { true })]
        [InlineData("YES_1_1011", 1, true, new[] { true, false, true, true })]
        [InlineData("YES_1_1_0_1_1", 1, true, new[] { true, false, true, true })]
        [InlineData("NO_01", 1, false, new[] { false, false, false, false })]
        public void SensorReplyGivesPerSlotState(string reply, int ss, bool positive, bool[] expected)
        {
            var r = Default().InterpretSensor(reply, ss, expected.Length);
            Assert.True(r.Matched);
            Assert.Equal(positive, r.Positive);
            Assert.Equal(expected, r.Slots);
        }

        [Fact]
        public void SensorReplyWithWrongSlotCountIsRejectedWithWarning()
        {
            var r = Default().InterpretSensor("YES_1_10", 1, 4);
            Assert.True(r.Matched);
            Assert.Null(r.Slots);
            Assert.NotNull(r.Warning);
        }

        [Fact]
        public void SensorReplyForOtherFixtureDoesNotMatch()
            => Assert.False(Default().InterpretSensor("YES_2_1", 1, 1).Matched);
    }
}
