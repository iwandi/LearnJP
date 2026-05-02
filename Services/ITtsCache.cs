namespace LearnJP.Services;

public interface ITtsCache
{
    Task<byte[]?> GetAsync(string provider, string voice, string lang, string text, CancellationToken ct = default);
    Task SetAsync(string provider, string voice, string lang, string text, byte[] data, CancellationToken ct = default);
}
