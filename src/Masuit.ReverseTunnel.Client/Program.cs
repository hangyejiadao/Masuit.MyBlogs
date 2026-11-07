using Masuit.ReverseTunnel.Client;
using Microsoft.Data.Sqlite;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<ClientStore>();
builder.Services.AddSingleton<TunnelClientService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<TunnelClientService>());

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/config", (ClientStore store) => Results.Ok(store.LoadAll().Select(ToWebConfiguration)));
app.MapGet("/api/config/{id:long}", (long id, ClientStore store) =>
{
	ClientConfiguration? configuration = store.Load(id);
	return configuration is null ? Results.NotFound(new { message = "隧道配置不存在。" }) : Results.Ok(ToWebConfiguration(configuration));
});
app.MapGet("/api/status", (TunnelClientService service) => Results.Ok(service.GetStatuses()));

app.MapPost("/api/config", (ClientConfigurationRequest request, ClientStore store, TunnelClientService service) =>
	SaveConfiguration(0, request, store, service));
app.MapPut("/api/config/{id:long}", (long id, ClientConfigurationRequest request, ClientStore store, TunnelClientService service) =>
	SaveConfiguration(id, request, store, service));

app.MapPost("/api/config/{id:long}/connect", (long id, ClientStore store, TunnelClientService service) =>
{
	ClientConfiguration? configuration = store.Load(id);
	if (configuration is null)
	{
		return Results.NotFound(new { message = "隧道配置不存在。" });
	}
	string? validationError = configuration.Validate();
	if (validationError is not null)
	{
		return Results.BadRequest(new { message = validationError });
	}
	store.SetEnabled(id, true);
	service.Reload(id);
	return Results.Ok(ToWebConfiguration(store.Load(id)!));
});

app.MapPost("/api/config/{id:long}/disconnect", (long id, ClientStore store, TunnelClientService service) =>
{
	if (!store.SetEnabled(id, false))
	{
		return Results.NotFound(new { message = "隧道配置不存在。" });
	}
	service.Reload(id);
	return Results.Ok(ToWebConfiguration(store.Load(id)!));
});

app.MapDelete("/api/config/{id:long}", (long id, ClientStore store, TunnelClientService service) =>
{
	if (!store.Delete(id))
	{
		return Results.NotFound(new { message = "隧道配置不存在。" });
	}
	service.Forget(id);
	return Results.Ok(new { deleted = true });
});

app.Run();

static IResult SaveConfiguration(long id, ClientConfigurationRequest request, ClientStore store, TunnelClientService service)
{
	ClientConfiguration? current = id > 0 ? store.Load(id) : null;
	if (id > 0 && current is null)
	{
		return Results.NotFound(new { message = "隧道配置不存在。" });
	}

	string token = string.IsNullOrWhiteSpace(request.Token) ? current?.Token ?? string.Empty : request.Token.Trim();
	var configuration = new ClientConfiguration(
		id,
		request.Name?.Trim() ?? string.Empty,
		request.ServerUrl?.Trim() ?? string.Empty,
		request.TunnelId?.Trim() ?? string.Empty,
		token,
		request.LocalHost?.Trim() ?? string.Empty,
		request.LocalPort,
		request.Enabled);
	string? validationError = configuration.Validate();
	if (validationError is not null)
	{
		return Results.BadRequest(new { message = validationError });
	}
	if (store.HasDuplicate(configuration.ServerUrl, configuration.TunnelId, id))
	{
		return Results.Conflict(new { message = "相同服务端地址和隧道编号的配置已经存在。" });
	}

	ClientConfiguration saved;
	try
	{
		saved = store.Save(configuration);
	}
	catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
	{
		return Results.Conflict(new { message = "相同服务端地址和隧道编号的配置已经存在。" });
	}
	service.Reload(saved.Id);
	return Results.Ok(ToWebConfiguration(saved));
}

static object ToWebConfiguration(ClientConfiguration configuration)
{
	return new
	{
		configuration.Id,
		configuration.Name,
		configuration.ServerUrl,
		configuration.TunnelId,
		TokenConfigured = !string.IsNullOrWhiteSpace(configuration.Token),
		configuration.LocalHost,
		configuration.LocalPort,
		configuration.Enabled
	};
}
