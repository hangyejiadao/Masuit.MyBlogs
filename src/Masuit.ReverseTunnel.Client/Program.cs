using Masuit.ReverseTunnel.Client;
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
app.MapGet("/api/config", (ClientStore store) => Results.Ok(store.LoadForWeb()));
app.MapGet("/api/status", (TunnelClientService service) => Results.Ok(service.GetStatus()));
app.MapPut("/api/config", (ClientConfiguration configuration, ClientStore store, TunnelClientService service) =>
{
	ClientConfiguration current = store.Load();
	configuration = configuration with { Token = string.IsNullOrWhiteSpace(configuration.Token) ? current.Token : configuration.Token };
	var validationError = configuration.Validate();
	if (validationError is not null)
	{
		return Results.BadRequest(new { message = validationError });
	}
	store.Save(configuration with { Enabled = false });
	service.Reload();
	return Results.Ok(store.LoadForWeb());
});
app.MapPost("/api/connect", (ClientStore store, TunnelClientService service) =>
{
	var configuration = store.Load();
	var validationError = configuration.Validate();
	if (validationError is not null)
	{
		return Results.BadRequest(new { message = validationError });
	}
	store.Save(configuration with { Enabled = true });
	service.Reload();
	return Results.Ok(store.LoadForWeb());
});
app.MapPost("/api/disconnect", (ClientStore store, TunnelClientService service) =>
{
	store.Save(store.Load() with { Enabled = false });
	service.Reload();
	return Results.Ok(store.LoadForWeb());
});

app.Run();
