namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// A user's profile picture. Kept in its own table, one row per user, so that the many queries
    /// that load <see cref="User"/> never drag image bytes along; <see cref="User.AvatarUpdatedAtUtc"/>
    /// is the light-weight marker that one exists (and the cache key the client fetches it by).
    /// <para>
    /// The client crops and re-encodes the picture to a 512×512 JPEG before upload, which also
    /// strips the photo's EXIF metadata (a phone picture's GPS location among it). The server still
    /// checks the bytes really are an image of an allowed type and size.
    /// </para>
    /// </summary>
    public class UserAvatar
    {
        public Guid UserId { get; set; }

        public User? User { get; set; }

        public byte[] Content { get; set; } = [];

        /// <summary>image/jpeg, image/png, or image/webp — taken from the bytes, not the upload's claim.</summary>
        public string ContentType { get; set; } = "image/jpeg";

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
