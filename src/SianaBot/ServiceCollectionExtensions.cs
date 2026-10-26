using SianaBot.ModerationLog;
using SianaBot.NicknameConvention;
using SianaBot.ReactionRoles;

namespace SianaBot;

public static class ServiceCollectionExtensions
{
    /// <summary>Options et services métier communs au bot et à la CLI. Le <c>DiscordRestClient</c> est fourni par chacun.</summary>
    public static IServiceCollection AddSianaCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DiscordOptions>(configuration.GetSection(DiscordOptions.Section));
        services.Configure<ReactionRoleOptions>(configuration.GetSection(ReactionRoleOptions.Section));
        services.Configure<NicknameConventionOptions>(configuration.GetSection(NicknameConventionOptions.Section));
        services.Configure<ModerationLogOptions>(configuration.GetSection(ModerationLogOptions.Section));

        services.AddSingleton<ReactionRoleStore>();
        services.AddSingleton<ReactionRoleService>();
        services.AddSingleton<PendingRoleStore>();
        services.AddSingleton<NicknameConventionService>();
        return services;
    }
}
