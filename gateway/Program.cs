using Yarp.ReverseProxy.Forwarder;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddProblemDetails();
var app = builder.Build();
app.UseExceptionHandler();
app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "ok" }));
app.MapReverseProxy(pipeline => pipeline.Use(async (ctx, next) =>
{
    await next();
    if (ctx.Features.Get<IForwarderErrorFeature>() is { } error && !ctx.Response.HasStarted)
    {
        app.Logger.LogWarning("Error de proxy {Error} en {Path}", error.Error, ctx.Request.Path);
        ctx.Response.Clear();
        ctx.Response.StatusCode = 503;
        await ctx.Response.WriteAsJsonAsync(new
        {
            message = "El servicio solicitado no está disponible. Inténtalo de nuevo en unos segundos.",
            traceId = ctx.TraceIdentifier
        });
    }
}));
app.Run();
