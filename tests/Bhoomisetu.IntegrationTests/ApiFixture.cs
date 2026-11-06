using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Bhoomisetu.Modules.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace Bhoomisetu.IntegrationTests;

/// <summary>
/// Starts the real API against a throw-away database. Set BHOOMISETU_TEST_DB to an admin connection string
/// for a PostgreSQL server with PostGIS (CI uses the postgis/postgis image). A uniquely named database is
/// created for the run and dropped afterwards, so the tests never touch an existing database.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "BHOOMISETU_TEST_DB";

    private readonly string? _adminConnectionString = Environment.GetEnvironmentVariable(EnvironmentVariable);
    private readonly string _databaseName = "bhoomisetu_it_" + Guid.NewGuid().ToString("N")[..12];
    private WebApplicationFactory<Program>? _factory;

    public bool Available => _adminConnectionString is not null;

    public string ConnectionString { get; private set; } = string.Empty;

    public CapturingOtpSender Sms { get; } = new();

    public HttpClient CreateClient() =>
        (_factory ?? throw new InvalidOperationException("Fixture not started.")).CreateClient();

    public async ValueTask InitializeAsync()
    {
        if (_adminConnectionString is null)
        {
            return;
        }

        await using (var admin = new NpgsqlConnection(_adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = _databaseName }.ConnectionString;

        // Program.cs reads configuration before the host is built, so settings go in as environment variables.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("ConnectionStrings__Bhoomisetu", ConnectionString);
        Environment.SetEnvironmentVariable("Auth__SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        Environment.SetEnvironmentVariable("Otp__Pepper", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Environment.SetEnvironmentVariable("Database__ApplyMigrationsOnStartup", "true");
        Environment.SetEnvironmentVariable("RateLimits__OtpPerIpPerMinute", "10000");
        Environment.SetEnvironmentVariable("Otp__ResendCooldownSeconds", "0");
        Environment.SetEnvironmentVariable("Otp__MaxRequestsPerHour", "100");
        Environment.SetEnvironmentVariable("RateLimits__RequestsPerIpPerMinute", "100000");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOtpSender>();
                services.AddSingleton<IOtpSender>(Sms);
            });
        });

        // Starting the server applies the migrations.
        _ = _factory.Server;
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_adminConnectionString is null)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>Signs in a phone number through the real OTP endpoints and returns an authorised client.</summary>
    public async Task<(HttpClient Client, Guid UserId)> SignInAsync(string phone)
    {
        var client = CreateClient();
        var request = await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phone });
        Assert.Equal(System.Net.HttpStatusCode.Accepted, request.StatusCode);

        var e164 = IndianMobileNumber.Parse(phone).Value.E164;
        var verify = await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { phone, code = Sms.CodeFor(e164) });
        verify.EnsureSuccessStatusCode();
        var body = await verify.Content.ReadFromJsonAsync<SignIn>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return (client, body.UserId);
    }

    public async Task ExecuteSqlAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    public sealed record SignIn(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId);
}

public sealed class CapturingOtpSender : IOtpSender
{
    private readonly ConcurrentDictionary<string, string> _codes = new();

    public string CodeFor(string e164) => _codes.TryGetValue(e164, out var code) ? code : throw new InvalidOperationException($"No code sent to {e164}.");

    public Task SendAsync(IndianMobileNumber phone, string code, TimeSpan validFor, CancellationToken cancellationToken)
    {
        _codes[phone.E164] = code;
        return Task.CompletedTask;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
