using Autofac.Extensions.DependencyInjection;
using Masuit.MyBlogs.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Diagnostics;
using AngleSharp.Text;
using Serilog;
using Serilog.Events;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
try
{
    if (Environment.OSVersion.Platform is not (PlatformID.MacOSX or PlatformID.Unix))
    {
        // 设置相关进程优先级为高于正常，防止其他进程影响应用程序的运行性能
        Process.GetProcessesByName("pg_ctl").ForEach(p => p.PriorityClass = ProcessPriorityClass.AboveNormal);
        Process.GetProcessesByName("postgres").ForEach(p => p.PriorityClass = ProcessPriorityClass.AboveNormal);
        Process.GetProcessesByName("redis-server").ForEach(p => p.PriorityClass = ProcessPriorityClass.AboveNormal);
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;
    }
}
catch
{
    // ignored
}

await Host.CreateDefaultBuilder(args).ConfigureAppConfiguration(builder => builder.AddJsonFile("appsettings.json", true, true)).UseSerilog((_, loggerConfiguration) =>
{
    loggerConfiguration
        .MinimumLevel.Information()
        .Enrich.FromLogContext()
        .WriteTo.Console();

    foreach (var level in Enum.GetValues<LogEventLevel>())
    {
        loggerConfiguration.WriteTo.Logger(levelLogger => levelLogger
            .MinimumLevel.Verbose()
            .Filter.ByIncludingOnly(logEvent => logEvent.Level == level)
            .WriteTo.File(
                Path.Combine(AppContext.BaseDirectory, "logs", level.ToString(), "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: null,
                retainedFileTimeLimit: TimeSpan.FromDays(7)));
    }
})
    .UseServiceProviderFactory(new AutofacServiceProviderFactory()).ConfigureWebHostDefaults(hostBuilder => hostBuilder.UseQuic().UseKestrel(opt =>
{
    var config = opt.ApplicationServices.GetService<IConfiguration>();
    var port = config["Port"] ?? "5000";
    var sslport = config["Https:Port"] ?? "5001";
    opt.ListenAnyIP(port.ToInt32(), options => options.Protocols = HttpProtocols.Http1AndHttp2AndHttp3);
#if !DEBUG
    if (config["Https:Enabled"].ToBoolean())
    {
        opt.ListenAnyIP(sslport.ToInt32(), s =>
        {
            if (Environment.OSVersion is { Platform: PlatformID.Win32NT, Version.Major: >= 10 })
            {
                s.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
            }

            s.UseHttps(AppContext.BaseDirectory + config["Https:CertPath"], config["Https:CertPassword"]);
        });
    } 
#endif

    opt.Limits.MaxRequestBodySize = null;
    Console.WriteLine($"应用程序监听端口：http：{port}，https：{sslport}");
}).UseStartup<Startup>()).Build().RunAsync();