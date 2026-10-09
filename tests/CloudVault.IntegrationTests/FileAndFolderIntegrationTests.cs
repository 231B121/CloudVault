using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CloudVault.Application.Common.Models;
using CloudVault.Application.DTOs.Auth;
using CloudVault.Application.DTOs.Files;
using CloudVault.Application.DTOs.Folders;
using CloudVault.Application.DTOs.Storage;
using FluentAssertions;
using Xunit;

namespace CloudVault.IntegrationTests;

public class FileAndFolderIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public FileAndFolderIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<(string Token, Guid UserId)> RegisterUserAsync(string prefix)
    {
        string email = $"{prefix}_{Guid.NewGuid():N}@example.com";
        var res = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequestDto
        {
            FullName = $"{prefix} User",
            Email = email,
            Password = "Password123!",
            ConfirmPassword = "Password123!"
        });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>(JsonOptions);
        return (body!.Data!.AccessToken, body.Data.User.Id);
    }

    [Fact]
    public async Task FileAndFolderLifecycle_WithQuotaAndIdorProtection_WorksCorrectly()
    {
        // 1. Create two separate users: User A and User B
        var (tokenA, _) = await RegisterUserAsync("userA");
        var (tokenB, _) = await RegisterUserAsync("userB");

        using var clientA = _factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);

        using var clientB = _factory.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);

        // 2. User A creates a folder
        var createFolderRes = await clientA.PostAsJsonAsync("/api/folders", new CreateFolderDto
        {
            Name = "Work Documents"
        });
        createFolderRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var folderData = (await createFolderRes.Content.ReadFromJsonAsync<ApiResponse<FolderDto>>(JsonOptions))!.Data!;
        Guid userAFolderId = folderData.Id;

        // 3. User A uploads a file into the folder
        var content = new MultipartFormDataContent();
        var fileBytes = Encoding.UTF8.GetBytes("Important document content for user A");
        var byteContent = new ByteArrayContent(fileBytes);
        byteContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        content.Add(byteContent, "file", "contracts.txt");
        content.Add(new StringContent(userAFolderId.ToString()), "folderId");

        var uploadRes = await clientA.PostAsync("/api/files/upload", content);
        uploadRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var uploadData = (await uploadRes.Content.ReadFromJsonAsync<ApiResponse<FileUploadResultDto>>(JsonOptions))!.Data!;
        Guid userAFileId = uploadData.Id;

        // 4. Verify User A can list and find the file inside the folder
        var listFilesRes = await clientA.GetAsync($"/api/files?folderId={userAFolderId}");
        listFilesRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var filesList = (await listFilesRes.Content.ReadFromJsonAsync<ApiResponse<PagedResult<FileDto>>>(JsonOptions))!.Data!;
        filesList.Items.Should().Contain(f => f.Id == userAFileId);

        // 5. Verify User A's storage quota increased
        var usageRes = await clientA.GetAsync("/api/storage/usage");
        usageRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var usageData = (await usageRes.Content.ReadFromJsonAsync<ApiResponse<StorageUsageDto>>(JsonOptions))!.Data!;
        usageData.StorageUsedBytes.Should().Be(fileBytes.Length);

        // 5a. User A downloads the file directly with Bearer header
        var downloadDirectRes = await clientA.GetAsync($"/api/files/{userAFileId}/download?direct=true");
        downloadDirectRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var downloadedBytes = await downloadDirectRes.Content.ReadAsByteArrayAsync();
        downloadedBytes.Should().Equal(fileBytes);

        // 5b. User A downloads the file using ?token= query parameter (native browser link)
        using var unauthClient = _factory.CreateClient();
        var downloadQueryRes = await unauthClient.GetAsync($"/api/files/{userAFileId}/download?direct=true&token={tokenA}");
        downloadQueryRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var downloadedQueryBytes = await downloadQueryRes.Content.ReadAsByteArrayAsync();
        downloadedQueryBytes.Should().Equal(fileBytes);

        // 5c. User A views/previews the file in browser tab using /view?token=
        var viewRes = await unauthClient.GetAsync($"/api/files/{userAFileId}/view?token={tokenA}");
        viewRes.StatusCode.Should().Be(HttpStatusCode.OK);
        viewRes.Content.Headers.ContentDisposition?.DispositionType.Should().Be("inline");
        var viewedBytes = await viewRes.Content.ReadAsByteArrayAsync();
        viewedBytes.Should().Equal(fileBytes);

        // 6. IDOR TEST 1: User B tries to access User A's file -> MUST BE 404 Not Found
        var idorGetFileRes = await clientB.GetAsync($"/api/files/{userAFileId}");
        idorGetFileRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 7. IDOR TEST 2: User B tries to download User A's file -> MUST BE 404 Not Found
        var idorDownloadRes = await clientB.GetAsync($"/api/files/{userAFileId}/download");
        idorDownloadRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 8. IDOR TEST 3: User B tries to delete User A's file -> MUST BE 404 Not Found
        var idorDeleteRes = await clientB.DeleteAsync($"/api/files/{userAFileId}");
        idorDeleteRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 9. IDOR TEST 4: User B tries to access User A's folder -> MUST BE 404 Not Found
        var idorGetFolderRes = await clientB.GetAsync($"/api/folders/{userAFolderId}");
        idorGetFolderRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 10. User A renames the file
        var renameRes = await clientA.PutAsJsonAsync($"/api/files/{userAFileId}/rename", new RenameFileDto
        {
            NewFileName = "contracts-updated.txt"
        });
        renameRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 11. User A deletes the file -> Quota decreases back to 0
        var deleteRes = await clientA.DeleteAsync($"/api/files/{userAFileId}");
        deleteRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var usageAfterDelete = (await (await clientA.GetAsync("/api/storage/usage")).Content.ReadFromJsonAsync<ApiResponse<StorageUsageDto>>(JsonOptions))!.Data!;
        usageAfterDelete.StorageUsedBytes.Should().Be(0);
    }
}
