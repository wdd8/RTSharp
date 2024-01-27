namespace RTSharp.Core.Services.Cache.Images
{
    public class CachedImage
    {
        public CachedImage()
        {
        }

        public CachedImage(byte[] ImageHash, byte[] Image)
        {
            this.ImageHash = ImageHash;
            this.Image = Image;
        }

        public byte[] ImageHash { get; init; }
        public byte[] Image { get; init; }

        public void Deconstruct(out byte[] ImageHash, out byte[] Image)
        {
            ImageHash = this.ImageHash;
            Image = this.Image;
        }
    }
}
