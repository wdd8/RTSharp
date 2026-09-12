namespace RTSharp.Scripts.FastVerify;

public partial record FastVerifyInput(byte[] Torrent, string BaseDirectory);
public partial record FastVerifyResult(bool Verified, string? Detail);
