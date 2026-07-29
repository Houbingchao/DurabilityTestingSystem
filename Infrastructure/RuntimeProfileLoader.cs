using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using DurabilityTestingSystem.Models;

namespace DurabilityTestingSystem.Infrastructure;

public static class RuntimeProfileLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "system-profile.json");

    public static SystemProfile Load()
    {
        if (!File.Exists(ProfilePath))
        {
            var profile = new SystemProfile();
            File.WriteAllText(ProfilePath, JsonSerializer.Serialize(profile, JsonOptions));
            return profile;
        }

        var json = File.ReadAllText(ProfilePath);
        return JsonSerializer.Deserialize<SystemProfile>(json, JsonOptions) ?? new SystemProfile();
    }

    public static IHardwarePlatform LoadHardwarePlatform(SystemProfile profile)
    {
        if (profile.Mode != RuntimeMode.Production)
            return new UnconfiguredHardwarePlatform("当前为演示模式，未加载真实硬件适配器。");

        if (string.IsNullOrWhiteSpace(profile.HardwareAdapterAssembly) ||
            string.IsNullOrWhiteSpace(profile.HardwareAdapterType))
        {
            return new UnconfiguredHardwarePlatform(
                "正式模式已启用，但 system-profile.json 尚未配置 HardwareAdapterAssembly 和 HardwareAdapterType。");
        }

        try
        {
            var assemblyPath = Path.IsPathRooted(profile.HardwareAdapterAssembly)
                ? profile.HardwareAdapterAssembly
                : Path.Combine(AppContext.BaseDirectory, profile.HardwareAdapterAssembly);
            var assembly = Assembly.LoadFrom(assemblyPath);
            var type = assembly.GetType(profile.HardwareAdapterType, throwOnError: true)!;
            if (Activator.CreateInstance(type) is not IHardwarePlatform platform)
                throw new InvalidOperationException($"类型 {profile.HardwareAdapterType} 未实现 IHardwarePlatform。 ");
            return platform;
        }
        catch (Exception ex)
        {
            return new UnconfiguredHardwarePlatform($"硬件适配器加载失败：{ex.Message}");
        }
    }
}

