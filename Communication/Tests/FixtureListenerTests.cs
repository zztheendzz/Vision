using Communication.Fixtures;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace Communication.Tests
{
    public class FixtureListenerTests
    {
        private static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        [Fact]
        public async Task ReceivesLinesSplitByTerminatorAcrossChunks()
        {
            int port = FreePort();
            var lines = new List<string>();
            var done = new TaskCompletionSource();

            using var listener = new FixtureListener(port, new byte[] { 13, 10 });
            listener.LineReceived += (_, line) =>
            {
                lock (lines) { lines.Add(line); if (lines.Count == 2) done.TrySetResult(); }
            };
            listener.Start();

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            var stream = client.GetStream();

            // Dòng đầu bị tách làm hai lần gửi, dòng hai gửi liền sau
            await stream.WriteAsync(Encoding.ASCII.GetBytes("P_01"));
            await Task.Delay(50);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("_02\r\nF_01_03\r\n"));

            await done.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(new[] { "P_01_02", "F_01_03" }, lines);
        }
    }
}
