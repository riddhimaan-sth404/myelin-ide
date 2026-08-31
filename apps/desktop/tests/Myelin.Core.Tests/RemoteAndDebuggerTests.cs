using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Myelin.Core.Models;
using Myelin.Core.Services;
using Xunit;

namespace Myelin.Core.Tests
{
    public class RemoteAndDebuggerTests
    {
        [Fact]
        public void RemoteConnectionService_InitializesDefaultTargets()
        {
            var service = new RemoteConnectionService();
            Assert.NotEmpty(service.Targets);
            Assert.Contains(service.Targets, t => t.Type == RemoteTargetType.WSL || t.Type == RemoteTargetType.SSH || t.Type == RemoteTargetType.Container);
        }

        [Fact]
        public void RemoteConnectionService_AddsAndRemovesTarget()
        {
            var service = new RemoteConnectionService();
            var target = new RemoteTarget
            {
                Name = "Production Server",
                Host = "10.0.0.50",
                User = "admin",
                Port = 2222,
                Type = RemoteTargetType.SSH
            };

            service.RemoveTarget(target);
            service.AddTarget(target);
            Assert.Contains(service.Targets, t => t.Name == "Production Server");
            Assert.Equal("admin@10.0.0.50:2222", target.DisplaySubtitle);
            Assert.Equal("IconSsh", target.IconKey);

            service.RemoveTarget(target);
            Assert.DoesNotContain(service.Targets, t => t.Name == "Production Server");
        }

        [Fact]
        public async Task RemoteConnectionService_ConnectAndDisconnect()
        {
            var service = new RemoteConnectionService();
            var target = new RemoteTarget
            {
                Name = "Ubuntu-22.04",
                DistroName = "Ubuntu-22.04",
                Type = RemoteTargetType.WSL
            };

            bool connected = await service.ConnectAsync(target);
            Assert.True(connected);
            Assert.True(service.CurrentState.IsConnected);
            Assert.Equal(RemoteConnectionStatus.Connected, target.Status);
            Assert.Equal("wsl.exe -d Ubuntu-22.04", service.GetTerminalLaunchCommand(target));

            service.Disconnect();
            Assert.False(service.CurrentState.IsConnected);
            Assert.Equal(RemoteConnectionStatus.Disconnected, target.Status);
        }

        [Fact]
        public void PortForwardingService_ManagesPortForwarding()
        {
            var service = new PortForwardingService();
            var port = service.ForwardPort(4000, 4000, "localhost", "Custom API");
            Assert.NotNull(port);
            Assert.Contains(service.ForwardedPorts, p => p.LocalPort == 4000);
            Assert.Equal("http://localhost:4000", port.LocalAddress);

            service.StopForwarding(port);
            Assert.DoesNotContain(service.ForwardedPorts, p => p.LocalPort == 4000);
        }

        [Fact]
        public void LaunchConfigurationService_ResolvesVariables()
        {
            var service = new LaunchConfigurationService();
            string resolved = service.ResolveVariables("${workspaceFolder}/target/debug/${workspaceFolderBasename}.exe", "d:/Projects/myelin", "d:/Projects/myelin/src/main.rs");
            Assert.Equal("d:/Projects/myelin/target/debug/myelin.exe", resolved);
        }

        [Fact]
        public void DebuggerService_ManagesBreakpointsCorrectly()
        {
            var service = new DebuggerService();
            service.ClearAllBreakpoints();

            string testFile = "d:/Projects/myelin/src/main.rs";

            // Add breakpoint
            var bp = service.ToggleBreakpoint(testFile, 15);
            Assert.NotNull(bp);
            Assert.True(service.HasBreakpoint(testFile, 15));
            Assert.False(service.HasBreakpoint(testFile, 16));

            var fileBps = service.GetBreakpointsForFile(testFile);
            Assert.Single(fileBps);
            Assert.Equal((nuint)15, fileBps[0].Line);

            // Conditional Breakpoint
            var condBp = service.SetConditionalBreakpoint(testFile, 20, "x > 10");
            Assert.Equal(BreakpointKind.Conditional, condBp.Kind);
            Assert.Equal("x > 10", condBp.Condition);

            // Logpoint
            var logBp = service.SetLogpoint(testFile, 25, "Value of x is {x}");
            Assert.Equal(BreakpointKind.Logpoint, logBp.Kind);
            Assert.Equal("Value of x is {x}", logBp.LogMessage);

            // Toggle again to remove
            service.ToggleBreakpoint(testFile, 15);
            Assert.False(service.HasBreakpoint(testFile, 15));
        }

        [Fact]
        public async Task DebuggerService_DebugLifecycleAndStepping()
        {
            var service = new DebuggerService();
            service.ClearAllBreakpoints();

            string testFile = "d:/Projects/myelin/src/main.rs";
            service.ToggleBreakpoint(testFile, 10);

            // Start debugging
            await service.StartDebuggingAsync(service.Configurations.FirstOrDefault(), "d:/Projects/myelin");

            Assert.Equal(DebugState.Paused, service.State);
            Assert.NotEmpty(service.StackFrames);
            Assert.NotEmpty(service.Variables);
            Assert.NotEmpty(service.Threads);
            Assert.Equal((nuint)10, service.CurrentFrame?.Line);

            // Step Over
            await service.StepOverAsync();
            Assert.Equal((nuint)11, service.CurrentFrame?.Line);

            // Step Into
            await service.StepIntoAsync();
            Assert.Equal((nuint)12, service.CurrentFrame?.Line);

            // Stop Debugging
            await service.StopAsync();
            Assert.Equal(DebugState.Inactive, service.State);
            Assert.Empty(service.StackFrames);
        }

        [Fact]
        public void DebuggerService_WatchExpressionsEvaluation()
        {
            var service = new DebuggerService();
            service.AddWatchExpression("buffer_len");
            service.AddWatchExpression("1 + 2");

            Assert.Equal(2, service.WatchItems.Count);
            Assert.Equal("14280", service.WatchItems[0].Value);
            Assert.Equal("42", service.WatchItems[1].Value);

            service.RemoveWatchExpression(service.WatchItems[0]);
            Assert.Single(service.WatchItems);
        }

        [Fact]
        public async Task DebuggerService_ReplEvaluation()
        {
            var service = new DebuggerService();
            string result = await service.EvaluateInReplAsync("buffer_len");
            Assert.Equal("14280", result);
        }
    }
}
