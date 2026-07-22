using System.Text;
using Api.Authentication;
using Api.OpenApi;
using Application.Common.Abstractions.Authentication;
using Application;
using Infrastructure;
using Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

const string FrontendDevelopmentCorsPolicy = "FrontendDevelopment";

var builder = WebApplication.CreateBuilder(args);

var authenticationOptions =
    CotizadorAuthenticationOptions.FromConfiguration(
        builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<BearerSecurityRequirementTransformer>();
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(
    builder.Configuration,
    authenticationOptions);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata =
            !builder.Environment.IsDevelopment();
        options.SaveToken = false;
        options.IncludeErrorDetails = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authenticationOptions.Jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = authenticationOptions.Jwt.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(
                    authenticationOptions.Jwt.SigningKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();

if (builder.Environment.IsDevelopment())
{
    var allowedOrigins = builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .GetChildren()
        .Select(origin => origin.Value)
        .Where(origin => !string.IsNullOrWhiteSpace(origin))
        .Select(origin => origin!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (allowedOrigins.Length == 0)
    {
        throw new InvalidOperationException(
            "La configuracion CORS de desarrollo 'Cors:AllowedOrigins' no esta definida.");
    }

    builder.Services.AddCors(options =>
    {
        options.AddPolicy(
            FrontendDevelopmentCorsPolicy,
            policy =>
            {
                policy
                    .WithOrigins(allowedOrigins)
                    .WithMethods(
                        "GET",
                        "POST",
                        "PUT",
                        "PATCH",
                        "DELETE",
                        "OPTIONS")
                    .WithHeaders(
                        "Content-Type",
                        "Authorization");
            });
    });
}

var app = builder.Build();

app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            "/openapi/v1.json",
            "Cotizador Backend API v1");
    });

    app.UseCors(FrontendDevelopmentCorsPolicy);
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
