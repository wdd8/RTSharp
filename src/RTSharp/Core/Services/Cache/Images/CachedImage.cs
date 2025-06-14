namespace RTSharp.Core.Services.Cache.Images;

public record struct CachedImage(byte[] ImageHash, byte[] Image);
