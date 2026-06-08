namespace LostAndFound.Services
{
    public static class ImageService
    {
        public static async Task<string?> SaveImageAsync(IFormFile? file, string wwwrootPath)
        {
            if (file == null || file.Length == 0) return null;
            if (file.Length > 2 * 1024 * 1024) return null; // 最大2MB

            var uploadsDir = Path.Combine(wwwrootPath, "uploads");
            Directory.CreateDirectory(uploadsDir);

            var ext = Path.GetExtension(file.FileName).ToLower();
            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png" && ext != ".gif") return null;

            var fileName = $"{Guid.NewGuid()}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);

            using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);

            return $"/uploads/{fileName}";
        }
    }
}