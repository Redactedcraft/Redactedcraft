using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    app.Urls.Add($"http://0.0.0.0:{port}");
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/eos/config", (HttpRequest request) =>
{
    var apiKey = GetEnv("EOS_CONFIG_API_KEY");
    if (!string.IsNullOrWhiteSpace(apiKey))
    {
        if (!request.Headers.TryGetValue("X-Api-Key", out var header) || header != apiKey)
            return Results.Unauthorized();
    }

    var config = new EosConfig
    {
        ProductId = GetEnv("EOS_PRODUCT_ID") ?? "",
        SandboxId = GetEnv("EOS_SANDBOX_ID") ?? "",
        DeploymentId = GetEnv("EOS_DEPLOYMENT_ID") ?? "",
        ClientId = GetEnv("EOS_CLIENT_ID") ?? "",
        ClientSecret = GetEnv("EOS_CLIENT_SECRET") ?? "",
        ProductName = GetEnv("EOS_PRODUCT_NAME") ?? "RedactedCraft",
        ProductVersion = GetEnv("EOS_PRODUCT_VERSION") ?? "1.0",
        LoginMode = GetEnv("EOS_LOGIN_MODE") ?? "device"
    };

    if (!config.IsValid(out var error))
    {
        return Results.Problem(
            detail: $"Server is missing required EOS config: {error}",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    return Results.Json(config);
});

app.Run();

static string? GetEnv(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

sealed class EosConfig
{
    public string ProductId { get; set; } = "";
    public string SandboxId { get; set; } = "";
    public string DeploymentId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string ProductName { get; set; } = "RedactedCraft";
    public string ProductVersion { get; set; } = "1.0";
    public string LoginMode { get; set; } = "device";

    public bool IsValid(out string? error)
    {
        if (string.IsNullOrWhiteSpace(ProductId))
        {
            error = "EOS_PRODUCT_ID";
            return false;
        }
        if (string.IsNullOrWhiteSpace(SandboxId))
        {
            error = "EOS_SANDBOX_ID";
            return false;
        }
        if (string.IsNullOrWhiteSpace(DeploymentId))
        {
            error = "EOS_DEPLOYMENT_ID";
            return false;
        }
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            error = "EOS_CLIENT_ID";
            return false;
        }
        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            error = "EOS_CLIENT_SECRET";
            return false;
        }

        error = null;
        return true;
    }
}
