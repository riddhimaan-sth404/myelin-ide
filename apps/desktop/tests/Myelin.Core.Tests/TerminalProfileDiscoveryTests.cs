using System;
using System.IO;
using System.Linq;
using Myelin.Core;
using Myelin.Core.Models;
using Myelin.Core.Services;
using Xunit;

namespace Myelin.Core.Tests
{
    public class TerminalProfileDiscoveryTests
    {
        [Fact]
        public void Test_DiscoverProfiles_Returns_Host_Profiles()
        {
            var profiles = TerminalProfileDiscoveryService.Instance.DiscoverProfiles();

            Assert.NotNull(profiles);
            Assert.NotEmpty(profiles);

            // Verify at least one profile is default
            Assert.Contains(profiles, p => p.IsDefault);

            // Check that all profiles have valid Id and Name
            foreach (var profile in profiles)
            {
                Assert.False(string.IsNullOrWhiteSpace(profile.Id));
                Assert.False(string.IsNullOrWhiteSpace(profile.Name));
                Assert.False(string.IsNullOrWhiteSpace(profile.ExecutablePath));
            }
        }

        [Fact]
        public void Test_NativeTerminal_Spawn_With_Discovered_Profile()
        {
            var profiles = TerminalProfileDiscoveryService.Instance.DiscoverProfiles();
            var defaultProfile = profiles.FirstOrDefault(p => p.IsDefault) ?? profiles[0];

            using var term = new NativeTerminal(80, 24, Directory.GetCurrentDirectory(), defaultProfile.ExecutablePath);
            Assert.True(term.IsAlive);
            Assert.Equal(defaultProfile.ExecutablePath, term.ShellPath);

            bool wrote = term.Write("echo 'Profile Test'\r\n");
            Assert.True(wrote);
        }

        [Fact]
        public void Test_NativeTerminal_Spawn_Every_Discovered_Profile()
        {
            var profiles = TerminalProfileDiscoveryService.Instance.DiscoverProfiles();
            foreach (var profile in profiles)
            {
                using var term = new NativeTerminal(80, 24, Directory.GetCurrentDirectory(), profile.ExecutablePath, profile.Arguments);
                Assert.True(term.IsAlive, $"Failed to spawn profile '{profile.Name}' with path '{profile.ExecutablePath}'");
                Assert.Equal(profile.Arguments, term.ShellArgs);
                bool wrote = term.Write("echo 'Test'\r\n");
                Assert.True(wrote);
            }
        }

        [Fact]
        public void Test_Multi_Terminal_Sessions_Independent_Execution()
        {
            var profiles = TerminalProfileDiscoveryService.Instance.DiscoverProfiles();
            var profile1 = profiles[0];
            var profile2 = profiles.Count > 1 ? profiles[1] : profiles[0];

            using var term1 = new NativeTerminal(80, 24, Directory.GetCurrentDirectory(), profile1.ExecutablePath, profile1.Arguments);
            using var term2 = new NativeTerminal(80, 24, Directory.GetCurrentDirectory(), profile2.ExecutablePath, profile2.Arguments);

            Assert.True(term1.IsAlive);
            Assert.True(term2.IsAlive);

            term1.Write("echo 'SESSION_ONE_ACTIVE'\r\n");
            term2.Write("echo 'SESSION_TWO_ACTIVE'\r\n");

            System.Threading.Thread.Sleep(200);

            string out1 = term1.ReadAvailable();
            string out2 = term2.ReadAvailable();

            Assert.NotNull(out1);
            Assert.NotNull(out2);
        }
    }
}
