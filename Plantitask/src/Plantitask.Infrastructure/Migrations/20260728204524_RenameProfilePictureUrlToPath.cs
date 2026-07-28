using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plantitask.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameProfilePictureUrlToPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ProfilePictureUrl",
                table: "Users",
                newName: "ProfilePicturePath");

            // The column used to hold a rendered URL and now holds a storage key, so strip
            // everything up to and including the last slash off our own upload rows.
            //
            // The guard matches the naming rule the storage layer owns. UploadFileAsync names
            // every file "{guid}{extension}" with the extension whitelisted by
            // FileUploadRules.ImageExtensions, so a value ending in that shape is ours and
            // whatever sits in front of it is a rendered base URL.
            //
            // Matching on the shape rather than the scheme matters because our own production
            // base URL is https too. A scheme guard would skip every real CDN row and leave a
            // full URL sitting in a column named Path.
            //
            // Google SSO avatars never match, so https://lh3.googleusercontent.com/a/ABC is
            // left alone instead of collapsing to ABC and losing that user their picture.
            migrationBuilder.Sql(@"
                UPDATE ""Users""
                SET ""ProfilePicturePath"" = substring(""ProfilePicturePath"" from '[^/]+$')
                WHERE ""ProfilePicturePath"" ~* '/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.(jpg|jpeg|png|webp|gif)$';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ProfilePicturePath",
                table: "Users",
                newName: "ProfilePictureUrl");

            // Not reversing the value rewrite. The base URL is config and not data so it
            // cannot be reconstructed here.
        }
    }
}
