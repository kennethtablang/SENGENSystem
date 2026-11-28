using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SENGENSystem.Server.Features.Profile.Avatar;

namespace SENGENSystem.Server.Tests.Integration
{
    /// <summary>Profile pictures: every user has one slot, served only to its owner.</summary>
    [Collection(AppCollection.Name)]
    public class ProfileAvatarTests(SengenAppFactory app)
    {
        private const string Password = "Student@Test2026";

        // The smallest valid JPEG header is enough: the server checks the signature, not the pixels.
        private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0xFF, 0xD9];

        private static MultipartFormDataContent Form(byte[] bytes, string contentType, string name = "avatar.jpg")
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(file, "file", name);
            return form;
        }

        private async Task<HttpClient> NewUserAsync()
        {
            var email = $"avatar.{Guid.NewGuid():N}@example.test";
            await app.CreateStudentUserAsync(email, Password);
            return await app.SignedInAsync(email, Password);
        }

        private static async Task<string?> AvatarVersionAsync(HttpClient client)
        {
            using var me = JsonDocument.Parse(await client.GetStringAsync("/api/auth/me"));
            var root = me.RootElement.TryGetProperty("user", out var u) ? u : me.RootElement;
            return root.GetProperty("avatarVersion").ValueKind == JsonValueKind.Null
                ? null : root.GetProperty("avatarVersion").GetString();
        }

        [SqlFact]
        public async Task A_user_uploads_sees_and_removes_their_own_picture()
        {
            var client = await NewUserAsync();
            Assert.Null(await AvatarVersionAsync(client));
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/profile/avatar")).StatusCode);

            var upload = await client.PutAsync("/api/profile/avatar", Form(Jpeg, "image/jpeg"));
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
            var version = await AvatarVersionAsync(client);
            Assert.NotNull(version);

            var get = await client.GetAsync($"/api/profile/avatar?v={version}");
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            Assert.Equal("image/jpeg", get.Content.Headers.ContentType?.MediaType);
            Assert.Equal(Jpeg, await get.Content.ReadAsByteArrayAsync());
            Assert.Contains("private", get.Headers.CacheControl?.ToString());
            Assert.Equal("nosniff", get.Headers.GetValues("X-Content-Type-Options").Single());

            // Another user never gets this picture — they only ever see their own (none).
            var other = await NewUserAsync();
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/api/profile/avatar")).StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/api/profile/avatar")).StatusCode);
            Assert.Null(await AvatarVersionAsync(client));
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/profile/avatar")).StatusCode);
        }

        [SqlFact]
        public async Task A_file_that_is_not_an_image_is_refused_whatever_it_claims_to_be()
        {
            var client = await NewUserAsync();
            var html = "<html><script>alert(1)</script></html>"u8.ToArray();

            var response = await client.PutAsync("/api/profile/avatar", Form(html, "image/jpeg"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null(await AvatarVersionAsync(client));
        }

        [SqlFact]
        public async Task An_oversized_picture_is_refused()
        {
            var client = await NewUserAsync();
            var big = new byte[ProfileAvatarEndpoints.MaxBytes + 1];
            Jpeg.CopyTo(big, 0);

            var response = await client.PutAsync("/api/profile/avatar", Form(big, "image/jpeg"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [SqlFact]
        public async Task Anonymous_callers_cannot_reach_the_picture_endpoint()
        {
            var anonymous = app.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/profile/avatar")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsync("/api/profile/avatar", Form(Jpeg, "image/jpeg"))).StatusCode);
        }

        [Theory]
        [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xDB }, "image/jpeg")]
        [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 }, "image/png")]
        [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
        [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, null)] // GIF
        [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67 }, null)]             // "<svg"
        public void Image_types_are_read_from_the_bytes(byte[] bytes, string? expected) =>
            Assert.Equal(expected, ProfileAvatarEndpoints.SniffImageType(bytes));
    }
}
