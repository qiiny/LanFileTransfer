using System.Diagnostics;

namespace LanFileTransfer.Services;

public static class FirewallHelper
{
    private const string RulePrefix = "LanFileTransfer HTTP Server";

    public static async Task<bool> EnsureFirewallRuleAsync(int port, Action<string>? log = null)
    {
        if (await RuleForPortExistsAsync(port))
        {
            log?.Invoke($"防火墙规则已存在: 端口 {port}，跳过添加");
            return true;
        }

        await RemoveOldRulesAsync();

        var ruleName = $"{RulePrefix} (Port {port})";
        var args = $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport={port}";
        return await RunNetshAsync(args, log, $"防火墙规则已持久化添加: 端口 {port} 已放行");
    }

    private static async Task<bool> RuleForPortExistsAsync(int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = "advfirewall firewall show rule name=all",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var process = Process.Start(psi);
            if (process == null) return true;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0) return true;

            if (!output.Contains(RulePrefix)) return false;

            foreach (var line in output.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("LocalPort:", StringComparison.OrdinalIgnoreCase))
                {
                    var portStr = t["LocalPort:".Length..].Trim();
                    if (int.TryParse(portStr, out var p) && p == port)
                        return true;
                }
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static async Task RemoveOldRulesAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = "advfirewall firewall show rule name=all",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var process = Process.Start(psi);
            if (process == null) return;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Rule Name:") && trimmed.Contains(RulePrefix))
                {
                    var name = trimmed["Rule Name:".Length..].Trim();
                    await RunNetshSilentAsync(
                        $"advfirewall firewall delete rule name=\"{name}\"");
                }
            }
        }
        catch { }
    }

    private static async Task RunNetshSilentAsync(string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var process = Process.Start(psi);
            if (process != null)
                await process.WaitForExitAsync();
        }
        catch { }
    }

    private static async Task<bool> RunNetshAsync(string args, Action<string>? log, string successMessage)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var process = Process.Start(psi);
            if (process == null)
            {
                log?.Invoke("防火墙配置被用户取消");
                return false;
            }

            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                log?.Invoke(successMessage);
                return true;
            }

            log?.Invoke($"防火墙配置命令退出码: {process.ExitCode}");
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            log?.Invoke("防火墙配置需要管理员权限（已跳过，请在 Windows 防火墙中手动放行端口）");
            return false;
        }
        catch (Exception ex)
        {
            log?.Invoke($"防火墙配置失败: {ex.Message}");
            return false;
        }
    }
}