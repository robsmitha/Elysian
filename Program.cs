using Elysian.Application;
using Elysian.Infrastructure;
using Elysian.Infrastructure.Context;
using Elysian.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureAppConfiguration((context, config) =>
    {
        if (context.HostingEnvironment.IsDevelopment())
        {
            config.AddUserSecrets<Program>();
        }
    })
    .ConfigureServices((hostContext, services) =>
    {
        services.AddInfrastructure(hostContext.Configuration);
        services.AddApplication();
    })
    .ConfigureFunctionsWorkerDefaults(builder =>
    {
        builder.UseMiddleware<ClaimsPrincipalMiddleware>();
        builder.UseMiddleware<ExceptionHandlingMiddleware>();
        builder.UseMiddleware<MuliTenantFunctionsWorkerMiddleware>();
    })
    .Build();

var environment = host.Services.GetRequiredService<IHostEnvironment>();
if (environment.IsDevelopment())
{
    var db = host.Services.GetRequiredService<ElysianContext>();
    await db.Database.MigrateAsync();
}

await host.RunAsync();
