using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CloudVault.Application.Common.Models;
using CloudVault.Application.DTOs.Auth;
using FluentAssertions;
using Xunit;

namespace CloudVault.IntegrationTests;

public class AuthIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AuthIntegrationTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RegisterAndLogin_CompleteFlow_Succeeds()
    {
        string email = $"integration_{Guid.NewGuid():N}@test.com";

        // 1. Register
        var registerRequest = new RegisterRequestDto
        {
            FullName = "Integration User",
            Email = email,
            Password = "SecurePassword123!",
            ConfirmPassword = "SecurePassword123!"
        };

        var regResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        regResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var regBody = await regResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>(JsonOptions);
        regBody.Should().NotBeNull();
        regBody!.Success.Should().BeTrue();
        regBody.Data!.AccessToken.Should().NotBeNullOrEmpty();
        regBody.Data.User.Email.Should().Be(email);

        // 2. Duplicate registration attempt should return 409 Conflict
        var dupResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        dupResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 3. Login
        var loginRequest = new LoginRequestDto
        {
            Email = email,
            Password = "SecurePassword123!"
        };

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>(JsonOptions);
        loginBody.Should().NotBeNull();
        loginBody!.Success.Should().BeTrue();
        string token = loginBody.Data!.AccessToken;

        // 4. Unauthorized access to /api/auth/me without token should return 401
        var unauthResponse = await _client.GetAsync("/api/auth/me");
        unauthResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 5. Authorized access to /api/auth/me with Bearer token
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var authResponse = await _client.SendAsync(request);
        authResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var userBody = await authResponse.Content.ReadFromJsonAsync<ApiResponse<UserDto>>(JsonOptions);
        userBody.Should().NotBeNull();
        userBody!.Data!.Email.Should().Be(email);
    }
}
